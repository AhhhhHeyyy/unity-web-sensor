# 修復紀錄

## 基本資訊

| 欄位 | 內容 |
| --- | --- |
| **日期** | 2026-05-13 |
| **問題類型** | 功能調整 / 重要修復 |
| **嚴重程度** | Major |
| **狀態** | 已解決（待 Unity 端驗證） |
| **受影響模組** | `AccelerometerBallEffect.cs` — 嚮導校正、平放模式感測器映射、HUD |
| **關聯 Issue** | N/A |

---

## 背景 / 需求摘要

- **現象 / 需求**：嚮導校正後出現四個問題：(1) 平放 Y 軸幾乎不動、(2) 無移動原點視覺參考、(3) 不知道要晃多大、(4) 位置偏移；後續追加平放 XZ 彈動過劇、嚮導偵測時間太短、各軸 maxOffset 未獨立校正、swapXZ 後死區污染、以及 XZ 長期不回中心等問題。
- **感測器背景**：XZ 由 GAME_ROTATION_VECTOR 四元數推算 gDevice.x/y（重力投影，持續量）；Z（直立）/ Y（平放）由 TYPE_LINEAR_ACCELERATION（線性加速，瞬時量）提供。
- **目標 vs 實際差距**：平放三軸應各自獨立校正並平衡響應幅度；XZ 長期持有特定角度後應能自動回中心；使用者應有視覺回饋知道球目前在範圍內的哪個位置。

---

## 根因分析

### Root Cause Analysis

1. **問題①：平放 Y 軸不動** → **原因**：`movementAxesMask.y = 0` 遮罩關閉，且 `HandleAcceleration` 無平放分支，`rawAcceleration.y` 靜止為舊直立模式殘值 → **修正**：新增平放分支（`worldAcc.z → rawAcceleration.y`），`movementAxesMask.y = 1`，切換時清除殘值。

2. **問題②③：無視覺原點 / 不知幅度** → **原因**：缺少 Game 視窗即時 HUD → **修正**：新增 `DrawOffsetHUD()`，包含 2D 方格（X-Z 平面）、Y 直條、當前偏移數值與範圍邊界。HUD 需在 Inspector 手動勾選 `showOffsetHUD`（Unity 序列化特性，新增欄位對舊物件預設 `false`）。

3. **問題④ / XZ 長期不回中心** → **原因**：GAME_ROTATION_VECTOR 只靠陀螺儀積分，長時間運行後四元數漂移 → `gDevice.x/y` 偏離 0 → `debiased.xz` 持續非零 → 球不回中心。Y（線性加速）靜止時本身接近 0，不受此影響。→ **修正**：新增自動重力中心補償（`enableFlatGravityCorrection` + `flatGravityCorrectionTime = 30s`），球靜止時緩慢將 `calibratedAcceleration.xz` 往 `filteredAcceleration.xz` 靠，主動傾斜時暫停。

4. **問題⑤：swapXZ 死區跨軸污染** → **原因**：嚮導在 Baseline 階段以 `filteredAcceleration.x` 的 std 設定 `deadzone.x`；TiltRight 偵測到 `swapXZ=true` 後，管線把原 Z 訊號放進 X slot，但死區仍用原 X 噪聲標準差 → 不匹配 → **修正**：偵測到 `swapXZ=true` 時，同步交換 `pendingDeadzone.x` ↔ `pendingDeadzone.z`。

5. **問題⑥：各軸 maxOffset / 靈敏度不平衡** → **原因**：嚮導只在 PushForward 校正 `maxOffsetPerAxis.z`；X 軸 maxOffset 保留預設 3m，導致 HUD 邊界代表的幅度與實際校正幅度不一致；Z 軸靈敏度用 X 的校正值，兩軸達到 `wizardTargetOffset` 所需力道不同 → **修正**：TiltRight 階段設 `maxOffsetPerAxis.x = wizardTargetOffset × 1.1`；PushForward 階段計算 `axisScale.z = wizardTargetOffset / (effectivePeak × sens)` 讓 Z 獨立正規化；嚮導結果寫入 `axisScale`。

6. **問題⑦：平放彈動劇烈** → **原因**：平放 `inputFilterTime = 0.05s` 太短，return-to-rest 時訊號急降，感知彈動；加上 `smoothSpeed = 10f` 追蹤過快 → **修正**：`inputFilterTime = 0.12f`，`smoothSpeed = 7f`。

7. **問題⑧：嚮導採樣時間太短** → `wizardCollectDuration` 預設 `1.5s` 改為 `2.5s`，Range 上限 `3f` 改為 `5f`。

### 最根本原因

> GAME_ROTATION_VECTOR 四元數長時間漂移 + 嚮導 swapXZ 後未同步死區，是本次所有 XZ 穩定性問題的根源。

---

## 修正方案 / 工作內容

### 重要程式碼片段

#### ① 平放 Y 軸輸入（HandleAcceleration）

**修改前**
```csharp
if (hasOrientationData && !phoneIsFlat)
{
    Vector3 worldAcc = currentOrientation * acc;
    rawAcceleration.z = worldAcc.y;
}
else if (!hasOrientationData) { rawAcceleration = acc; }
```

**修改後**
```csharp
if (hasOrientationData && !phoneIsFlat)
{
    Vector3 worldAcc = currentOrientation * acc;
    rawAcceleration.z = worldAcc.y;
}
else if (hasOrientationData && phoneIsFlat)
{
    Vector3 worldAcc = currentOrientation * acc;
    rawAcceleration.y = worldAcc.z; // Android 世界 Z（朝上）→ Unity Y
}
else if (!hasOrientationData) { rawAcceleration = acc; }
```

---

#### ② 自動重力中心補償（Update，debiased 計算後）

**修改前**
```csharp
Vector3 debiased = filteredAcceleration - calibratedAcceleration;
```

**修改後**
```csharp
Vector3 debiased = filteredAcceleration - calibratedAcceleration;

if (phoneIsFlat && enableFlatGravityCorrection &&
    wizardPhase is WizardPhase.Idle or WizardPhase.Done)
{
    float halfDz    = (s.axisDeadzone.x + s.axisDeadzone.z) * 0.5f;
    float idleRatio = 1f - Mathf.Clamp01(
        (Mathf.Abs(debiased.x) + Mathf.Abs(debiased.z)) / Mathf.Max(halfDz * 2f, 0.01f));
    float corrAlpha = (1f - Mathf.Exp(-Time.deltaTime / flatGravityCorrectionTime)) * idleRatio;
    calibratedAcceleration.x += (filteredAcceleration.x - calibratedAcceleration.x) * corrAlpha;
    calibratedAcceleration.z += (filteredAcceleration.z - calibratedAcceleration.z) * corrAlpha;
    debiased = filteredAcceleration - calibratedAcceleration;
}
```

---

#### ③ swapXZ 死區修正 + X maxOffset 校正（ProcessPhaseComplete TiltRight/PushRight）

**修改前**（無 swapXZ 死區修正，X maxOffset 保留舊值）
```csharp
wizardRetryCount     = 0;
wizardLastStepResult = $"X...";
AdvanceWizardPhase();
```

**修改後**
```csharp
// swapXZ 後 X slot 存的是原 Z 訊號，交換死區讓噪聲過濾正確
if (swapXZ)
{
    if (isUpright) pendingUprightDeadzone = new Vector3(pendingUprightDeadzone.z, pendingUprightDeadzone.y, pendingUprightDeadzone.x);
    else           pendingFlatDeadzone    = new Vector3(pendingFlatDeadzone.z,    pendingFlatDeadzone.y,    pendingFlatDeadzone.x);
}
// X 軸 maxOffset 對齊 wizardTargetOffset
if (isUpright) pendingUprightMaxOffset = new Vector3(wizardTargetOffset * 1.1f, pendingUprightMaxOffset.y, pendingUprightMaxOffset.z);
else           pendingFlatMaxOffset    = new Vector3(wizardTargetOffset * 1.1f, pendingFlatMaxOffset.y,    pendingFlatMaxOffset.z);
```

---

#### ④ Z 軸獨立 axisScale（ProcessPhaseComplete PushForward）

**新增**
```csharp
float scaleZ = effectivePeak > 0.01f
    ? Mathf.Clamp(wizardTargetOffset / (effectivePeak * sens), 0.1f, 10f)
    : 1f;
if (isUpright) pendingUprightAxisScale.z = scaleZ;
else           pendingFlatAxisScale.z    = scaleZ;
```

---

### 其他參數調整

| 參數 | 修改前 | 修改後 | 說明 |
| --- | --- | --- | --- |
| `flatSettings.inputFilterTime` | 0.05f | 0.12f | 減少平放彈動 |
| `flatSettings.smoothSpeed` | 10f | 7f | 降低追蹤急躁感 |
| `flatSettings.movementAxesMask.y` | 0 | 1 | 開啟 Y 軸（需在 Inspector 手動更新舊場景） |
| `wizardCollectDuration` | 1.5f（Range 1~3） | 2.5f（Range 1~5） | 增加採樣時間 |
| HUD | 無 | `DrawOffsetHUD()` | 需 Inspector 手動勾選 `showOffsetHUD` |

### 關聯 Git Commits

| Commit Hash | 說明 |
| --- | --- |
| 未提交（`HEAD=537e25c`） | 本次所有修改待提交，相對 HEAD 共 +366 行 |

---

## 驗證清單

- [ ] 平放靜止，X/Z/Y 數值穩定（HUD 開啟後觀察）
- [ ] 平放傾斜後放回平面，30~60 秒內 XZ 自動回中心
- [ ] 直立前後推動 Z 有響應，不推時 Z 回 0（彈動在可接受範圍）
- [ ] 嚮導完整跑完後 XZ 在校正幅度下達到 `wizardTargetOffset`，不超過邊界
- [ ] swapXZ 手機（橫持）嚮導校正後 XZ 不交叉
- [ ] 手動驗證平放 `movementAxesMask.y` 在 Inspector 已更新為 1（舊場景需手動）
- [ ] `showOffsetHUD` 在 Inspector 已勾選（舊場景新增欄位預設 false）

---

## 影響評估

| 項目 | 說明 |
| --- | --- |
| **影響範圍** | 所有使用 AccelerometerBallEffect 的場景（平放模式行為全面更新） |
| **向下相容性** | 舊場景的序列化值（`movementAxesMask.y=0`、`showOffsetHUD=false`）需手動更新 |
| **潛在副作用** | `flatGravityCorrectionTime` 設太小會讓慢速持續傾斜被吸收（建議 ≥ 20s） |
| **向後相容性** | 嚮導新增 `pendingUprightAxisScale`/`pendingFlatAxisScale`，舊校正值不受影響 |

---

## 時間軸

| 時段 | 事件 |
| --- | --- |
| 第一輪 | 確認 Y 軸平放問題、新增 HUD、修 rawAcceleration.y 殘值 |
| 第二輪 | 平放彈動調整（inputFilterTime/smoothSpeed）、嚮導採樣時間、swapXZ 死區 bug、per-axis axisScale/maxOffset |
| 第三輪 | 確認 XZ 軸偏移根因（四元數漂移）、實作自動重力中心補償 |

---

## 經驗教訓

1. **觸發規律**：GAME_ROTATION_VECTOR 漂移是時間函數，短時間測試正常、長時間才浮現，需要延長測試周期才能重現。
2. **最佳化機會**：swapXZ 後死區對應的軸已經改變，類似的「交換後需同步更新」邏輯應在所有 swapXZ 相關處統一檢查。
3. **預防措施**：新增序列化欄位若預設值非型別預設（bool 非 false、float 非 0），需在 `Reset()` 或文件中明確標注「舊場景需手動設定」。

---

## 後續待辦

- [ ] 在 Unity 實機測試自動重力補償效果（建議持續傾斜 2 分鐘觀察 HUD） — 優先度 High
- [ ] 考慮將 Y 平放軸改為 gDevice.z（重力垂直分量）以進一步提升穩定性，但需解決 XZ 交叉污染問題 — 優先度 Low

---

## 標籤

`#AccelerometerBallEffect` `#平放模式` `#感測器漂移` `#四元數` `#嚮導校正` `#axisScale` `#swapXZ` `#deadzone` `#HUD` `#重力補償` `#Unity` `#UDP`
