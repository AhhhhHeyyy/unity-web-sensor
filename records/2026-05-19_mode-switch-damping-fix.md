# 除錯記錄

## 基本資訊

| 欄位 | 內容 |
| ---- | ---- |
| **日期** | 2026-05-19 |
| **問題類型** | 除錯修復 / 重構優化 |
| **嚴重程度** | Major |
| **狀態** | 已修復 |
| **涉及檔案/模組** | `UnityWebsocket0329/Assets/Scripts/AccelerometerBallEffect.cs` |
| **相關 Issue/Ticket** | N/A |

---

## 問題描述

- **現象**：在直立（upright）↔ 平放（flat）模式切換時，球的移動出現斷斷續續、不自然的阻尼感，體驗非常卡。斜拿手機時尤為明顯，會高頻地觸發切換。
- **預期行為**：模式切換時球應平滑地從舊位置過渡到新模式的目標位置，無明顯跳動或彈性卡頓。
- **差異**：Console 顯示 `最大單幀跳動=8.781m`，切換過渡實際在 0.01~0.06s 即「完成」（等同無過渡），且切換過於頻繁（每秒多次）。

---

## 根因分析

1. **斜拿時 flatnessRatio 在 0.7 附近反覆震盪** → 進入/退出平放共用同一閾值，防抖計時器被每次翻轉重置但仍頻繁觸發切換 → **確認（切換頻率問題的根源）**

2. **切換瞬間 `modeSwitchTransitionOffset` 計算後立即被清零** → 跳動補償量完全丟棄，改由 SmoothDamp 從 `_posVelocity = 0` 起步追趕 20m 以上的距離 → **確認（8.781m 單幀暴衝的根源）**
   ```csharp
   // 問題程式碼
   modeSwitchTransitionOffset = Vector3.zero; // 補償量計算後立即清零
   modeSwitchTransitionProgress = 1f;          // 立即宣告過渡完成
   _posVelocity = Vector3.zero;
   // → SmoothDamp 面對 20m 距離、初速 0、smoothTime=0.12s
   //   第一幀推出 v ≈ Δx/smoothTime = 20/0.12 = 167 m/s → 每幀位移 ≈ 2.7~8m
   ```

3. **模式間位置尺度不相容** → 平放 Z 範圍 `maxOffset(4) × axisScale(5) = ±20m`，直立 Z 範圍 `maxOffset(5) × axisScale(1) = ±5m`；平放中積分推至 Z=-23m，切回直立時目標在 -3.5m，落差 ~20m → **確認（切換跳動距離大的結構性原因）**

**根本原因**
> 模式切換時位置補償量被清零 + SmoothDamp 從零初速追趕 20m，加上進入/退出共用同一閾值使切換頻率過高，共同造成斷斷續續的暴力阻尼感。

---

## 修復方案

### 概念說明

**Fix 1 — 遲滯閾值（Hysteresis）**：新增 `flatnessHysteresis = 0.15`，讓進入平放需 flatnessRatio ≥ 0.70，退出平放需 flatnessRatio ≤ 0.55（0.70 − 0.15）。斜拿 ~45° 時 flatnessRatio 落在穩定帶內，不再觸發切換。

**Fix 2 — 位置混合淡出過渡（Position Blend Fade）**：恢復 `modeSwitchTransitionOffset` 不清零。`modeSwitchTransitionProgress` 從 0 線性爬升至 1，持續 `modeSwitchTransitionDuration`（預設 0.5s）。輸出目標為：
```
blendedTarget = proposedPosition + offset × (1 − SmoothStep(progress))
```
切換瞬間 `blendedTarget = smoothedPosition`（零跳動），0.5s 後完全收斂到新模式位置。每幀最大位移 ≈ `jumpMag / duration / fps`，完全由 `duration` 參數控制，不依賴 SmoothDamp 初速。

---

### 重要程式碼變更

#### Fix 1：新增遲滯欄位 + 修改防抖判斷

**修改前**
```csharp
// 只有一個閾值，進入/退出共用
rawPhoneIsFlat = Mathf.Abs(gDevice.z) / g >= flatnessThreshold;

if (rawPhoneIsFlat != phoneIsFlat)
{
    flatnessHoldTimer += Time.deltaTime;
    if (flatnessHoldTimer >= modeSwitchDebounceTime)
    {
        phoneIsFlat       = rawPhoneIsFlat;
        flatnessHoldTimer = 0f;
    }
}
```

**修改後**
```csharp
// 新增欄位
[SerializeField] [Range(0f, 0.4f)] private float flatnessHysteresis = 0.15f;

// 防抖邏輯改用遲滯（Update 中）
float exitThresh = Mathf.Max(0f, flatnessThreshold - flatnessHysteresis);
bool  targetFlat = phoneIsFlat
    ? debugFlatnessRatio >= exitThresh  // 已平放：需降到更低才離開
    : rawPhoneIsFlat;                   // 非平放：原始閾值決定進入
if (targetFlat != phoneIsFlat)
{
    flatnessHoldTimer += Time.deltaTime;
    if (flatnessHoldTimer >= modeSwitchDebounceTime)
    {
        phoneIsFlat       = targetFlat;
        flatnessHoldTimer = 0f;
    }
}
```

#### Fix 2：過渡補償改為混合淡出

**修改前**
```csharp
// modeJustSwitched block
modeSwitchTransitionOffset   = Vector3.zero; // 補償量清零
modeSwitchTransitionProgress = 1f;           // 立即完成
_switchSmoothRemaining       = modeSwitchSmoothTime;
_posVelocity                 = Vector3.zero;

// 輸出平滑
float switchT = modeSwitchSmoothTime * 0.4f;
float outT    = Mathf.Lerp(normalT, switchT, ratio);
smoothedPosition = Vector3.SmoothDamp(smoothedPosition, proposedPosition, ref _posVelocity, outT);
```

**修改後**
```csharp
// modeJustSwitched block
// offset 保留（不清零），由下方混合淡出處理
modeSwitchTransitionProgress = 0f;            // 從 0 爬升到 1
_effectiveTransitionDuration = modeSwitchTransitionDuration;
_posVelocity                 = Vector3.zero;

// 輸出平滑：混合淡出
if (modeSwitchTransitionProgress < 1f)
    modeSwitchTransitionProgress = Mathf.Clamp01(
        modeSwitchTransitionProgress + Time.deltaTime / Mathf.Max(modeSwitchTransitionDuration, 0.01f));
{
    float blendFade       = 1f - Mathf.SmoothStep(0f, 1f, modeSwitchTransitionProgress);
    Vector3 blendedTarget = proposedPosition + modeSwitchTransitionOffset * blendFade;
    float outT            = Mathf.Max(positionFilterTime, 0.01f);
    smoothedPosition = Vector3.SmoothDamp(smoothedPosition, blendedTarget, ref _posVelocity, outT);
}
```

---

## 驗證清單

- [ ] 斜拿 ~45° 測試：確認不再頻繁切換（遲滯生效）
- [ ] 模式切換測試：`最大單幀跳動` 應 < 1m（原為 8.781m）
- [ ] `切換過渡完成` 日誌應在 ~0.5s 後出現（原為 0.01~0.06s）
- [ ] 正常傾斜操控確認無回歸（平滑度、靈敏度無劣化）
- [ ] 遲滯值 0.15 實測是否合適（可調整 0.10~0.20）

---

## 影響範圍

| 面向 | 說明 |
| ---- | ---- |
| **影響範圍** | 直立/平放模式切換邏輯、輸出位置平滑管線 |
| **潛在風險** | ① `flatnessHysteresis` 過大時，使用者需大幅傾斜才能退出平放；建議限 ≤ 0.20。② 過渡期間（0.5s）球位置受 `modeSwitchTransitionOffset` 影響，若切換後立刻再切換，offset 會以上次殘留值重置，可能有輕微異常（概率低，可觀察）。|

---

## Lessons Learned

1. **成功做對**：透過 Console 的 `最大單幀跳動` 數值直接量化問題嚴重程度，快速定位到 SmoothDamp 初速為 0 的根因；以及識別到進入/退出共用同一閾值是震盪根源。

2. **改進空間**：`modeSwitchTransitionOffset` 未來可考慮逐軸設定上限（per-axis cap），防止極端情況下（平放 Z 超出 ±20m）過渡期球仍需移動過長距離。

3. **預防措施**：任何模式切換/狀態機設計若涉及不同數值尺度（scale 不同的座標空間），應在架構層面預先設計「尺度橋接緩衝」，而非在 patch 階段修補視覺跳動。

---

## 後續待辦

- [ ] 實機測試後調整 `flatnessHysteresis` 最佳值（預設 0.15，建議範圍 0.10~0.20）
- [ ] 若切換後再切換有 offset 殘留異常，考慮在 `modeJustSwitched` 中將新 offset 與舊 offset 混合（而非直接覆蓋）
- [ ] 評估 `modeSwitchTransitionDuration` 預設值 0.5s 是否太長，可實測後調整至 0.3~0.5s

---

## 標籤

`#模式切換` `#AccelerometerBallEffect` `#SmoothDamp` `#遲滯閾值` `#位置過渡` `#UX修復` `#Unity感測器`
