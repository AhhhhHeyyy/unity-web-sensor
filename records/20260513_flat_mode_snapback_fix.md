# 工作紀錄

## 基本資訊
| 欄位 | 內容 |
|------|------|
| **日期** | 2026-05-13 |
| **問題類型** | 修復問題 + 功能新增 |
| **嚴重程度** | Major |
| **狀態** | 已完成（待重新跑嚮導驗證） |
| **相關腳本** | `AccelerometerBallEffect.cs` |
| **Issue/Ticket** | N/A |

---

## 問題描述

### 原始症狀
- 平放模式的 XZ 位置會明顯抖動（大幅度移動時尤其明顯）
- 有不自然的阻尼感（類似橡皮筋拉回）
- 移動過程中球會被「強行拉回中心」
- Unity Scene/Game 視窗缺少 3 軸示意線

### 背景條件
- UDP 四元數模式（Android GAME_ROTATION_VECTOR）
- 嚮導校正後設定：`sens=2.043`、`maxOff.z=0.22`、`axisScale=(5,5,7.34)`、`swapXZ=True`
- 使用者動作：平移滑動（非傾斜）

---

## 根因分析

### Root Cause Analysis

1. **平放模式使用 SmoothDamp**
   → **根因**：XZ 輸入是重力投影（位置信號），SmoothDamp 累積速度造成過衝
   → **修正**：改用 EMA（Lerp），無速度記憶，無過衝

2. **嚮導校正結果異常**
   → **根因**：使用者 PushForward 幅度僅 ~0.1 m/s²（< 1°），導致三個連鎖異常：
   - `maxOff.z(0.22) = dz.z(0.22)` → Z 軸實質二元（0 or ±1.61m），無漸進移動
   - `sens=2.043` → 噪聲被放大 25 倍 → 球在 deadzone 邊界抖動
   - `axisScale.xy=5.0` 從前次嚮導繼承 → Y 軸衝到 25m
   → **修正**：加入三個保護（見下方修改細節）

3. **平放模式不感知平移滑動**
   → **根因**：XZ 只讀 `gDevice.x/y`（重力投影 = 傾斜），平推時值接近 0
   → **修正**：混合模式：`rawAcc.x = gDevice.x + worldLinearAcc.x × flatLinearBlend`

4. **「被拉回中心」的真正原因（排除誤判）**
   → FlatPipe log 顯示：`idle=0` 期間球一直在移動，**重力補償並非罪魁禍首**
   → 真正原因：`maxOff.z = dz.z`，deadzone 邊界的噪聲讓 Z 在 0 和 ±0.22 間隨機切換

### 最終根因
> 嚮導採樣使用 `filteredAcceleration`（EMA 濾波後），動作幅度小時峰值被壓低，
> 導致 sensitivity 過高、maxOff 過小、校正參數失真。

---

## 解決方案 / 實作內容

### 修改摘要

| # | 修改項目 | 說明 |
|---|----------|------|
| 1 | 平放 EMA 替換 SmoothDamp | 重力投影是位置信號，不需要速度記憶 |
| 2 | 3 軸示意線（世界空間） | `OnDrawGizmos` + `Debug.DrawLine`，紅/綠/藍 = X/Y/Z |
| 3 | FlatPipe 調試面板 | Inspector 顯示完整 8 段管線：raw→filtered→tare→preSwap→postSwap→dz→target→EMAalpha |
| 4 | Console 定時輸出 | `[FlatPipe Xs]` 每 0.5s 輸出一行，停止 Play 時輸出總結 |
| 5 | 嚮導保護①：axisScale.xy 重置 | `StartWizard()` 強制 `pendingFlatAxisScale = Vector3.one` |
| 6 | 嚮導保護②：sensitivity 上限 | 平放 `sensitivity` 鎖死最大 0.5 |
| 7 | 嚮導保護③：maxOff.z 下限 | `max(計算值, dz*sens*2, 0.5f)` 確保漸進移動範圍 |
| 8 | 嚮導 scaleZ 上限收緊 | 從 10 降到 5，防止動作幅度小時過度放大 |
| 9 | 混合輸入模式 | `rawAcc.x/z = gDevice + worldLinearAcc × flatLinearBlend(0.4)` |
| 10 | `wizardTargetOffset` 預設 | 從 1.5m 改為 0.3m，符合使用者實際移動幅度 |
| 11 | 平放預設 `maxOffsetPerAxis` | 從 (3,3,3) 改為 (0.5,0.5,0.5) |
| 12 | 平放 Y 軸預設關閉 | `movementAxesMask.y = 0`，垂直甩動太嘈雜 |
| 13 | 嚮導 Phase 5 說明文字修正 | 「往左右來回推動」→「往右邊傾斜並保持」 |

### 關鍵程式碼變更

**平放 EMA 替換 SmoothDamp**
```csharp
// 修改前
float smoothTime = 1f / s.smoothSpeed;
currentOffset = Vector3.SmoothDamp(currentOffset, targetOffset, ref currentVelocity, smoothTime);

// 修改後
if (phoneIsFlat)
{
    float flatAlpha = 1f - Mathf.Exp(-Time.deltaTime * s.smoothSpeed);
    currentOffset   = Vector3.Lerp(currentOffset, targetOffset, flatAlpha);
    currentVelocity = Vector3.zero;
}
else
{
    float smoothTime = 1f / s.smoothSpeed;
    currentOffset = Vector3.SmoothDamp(currentOffset, targetOffset, ref currentVelocity, smoothTime);
}
```

**混合輸入**
```csharp
// HandleGyroscopeData（平放分支）
_gravX = gDevice.x;
_gravZ = gDevice.y;
rawAcceleration.x = _gravX + _linX * flatLinearBlend;
rawAcceleration.z = _gravZ + _linZ * flatLinearBlend;

// HandleAcceleration（平放分支）
_linX = worldAcc.x;
_linZ = worldAcc.y;
rawAcceleration.x = _gravX + _linX * flatLinearBlend;
rawAcceleration.z = _gravZ + _linZ * flatLinearBlend;
```

**嚮導 maxOff.z 下限**
```csharp
// 修改前
float calibMaxOffset = effectivePeak * sens * 1.1f;

// 修改後
float calibMaxOffset = effectivePeak * sens * 1.1f;
float minUsableMax   = noiseThr * sens * 2f;
calibMaxOffset = Mathf.Max(calibMaxOffset, minUsableMax, 0.5f);
```

### 相關 Git Commits
| Commit Hash | 說明 |
|-------------|------|
| `537e25c` | 自動校正 0513（本次工作前的最後提交） |

---

## 偵錯流程

### FlatPipe Log 關鍵發現（53 秒平放測試）

```
最大 idleRatio : 0.977  ← idle>0 僅發生在 debiased 接近 0 時（非移動中）
Tare 漂移量    : 0.0038 m/s²（53s）← 重力補償幾乎沒有移動 Tare
最大 targetOffset XZ : (1.650, 0.575)
位置抖動 最大/平均   : 1.5384m / 0.05455m
```

**排除項**：重力補償不是拉回中心的原因（移動時 idle=0）

**確認項**：
- Z 軸 `tgt` 幾乎只有 `{0, ±0.22}` → deadzone ≈ maxOff 導致的二元狀態
- `raw` 每 0.5s 變化 1–4 m/s² × sens 2.043 × scale 7.34 → 極大視覺抖動（被 clamp 壓在 ±1.61m）

---

## 驗證清單
- [ ] 重新跑嚮導（Phase 5 傾斜 20–30° 並保持；Phase 6 前後來回大幅移動）
- [ ] 確認嚮導結果：`sens ≤ 0.5`、`maxOff.z ≥ 0.5`、`axisScale.xy = 1.0`
- [ ] 傾斜測試：傾斜 → 球穩定停留，放平 → 球回中心
- [ ] 平推測試：平推 → 球有反應，停手 → 球彈回（`flatLinearBlend=0.4`）
- [ ] 模式切換：直立 ↔ 平放不發生暴衝

---

## 影響範圍與相容性
| 面向 | 說明 |
|------|------|
| **影響範圍** | 平放模式 XZ 輸入、嚮導校正邏輯、預設參數 |
| **向下相容性** | 舊有嚮導結果需重新跑（maxOff/scale/sens 都不同） |
| **潛在副作用** | `flatLinearBlend` 讓平推信號進入 tare debiased，可能輕微影響 idleRatio 判定 |
| **可維護性** | `flatLinearBlend=0` 還原純傾斜舊行為；`=1` 純平推 |

---

## 經驗教訓 Lessons Learned
1. **二元陷阱**：`maxOff ≈ dz` 時，任何超過 deadzone 的輸入立刻 clamp → 球只有兩個位置，看起來像抖動或被拉回中心，而非控制問題
2. **採樣代表性**：均值偵測方向適用重力投影（傾斜穩定），不適用線性加速度（平推均值趨近 0）
3. **必要不等式**：`maxOff ≥ dz * 2 * sens` 是平滑移動的必要條件，應在嚮導寫入前強制驗證

---

## 後續待辦
- [ ] 調整 `flatLinearBlend` 最佳值（從 0.4 開始依手感微調）
- [ ] 重新跑嚮導，驗證保護機制是否產生合理參數
- [ ] 若混合仍有抖動，考慮對 `_linX/Z` 加獨立的 EMA 濾波

---

## 標籤
`#平放模式` `#嚮導校正` `#EMA` `#SmoothDamp` `#混合輸入` `#AccelerometerBallEffect`
