# AR.js 位移追蹤系統使用說明

## 📋 目錄
1. [系統概述](#系統概述)
2. [功能特點](#功能特點)
3. [使用前準備](#使用前準備)
4. [使用步驟](#使用步驟)
5. [技術參數](#技術參數)
6. [常見問題](#常見問題)
7. [故障排除](#故障排除)

---

## 系統概述

本系統實現了通過手機攝像頭追蹤 Marker 圖案，實時獲取手機在真實世界中的位置和旋轉信息，並通過 WebSocket 傳輸到 Unity 應用程序，實現手機移動控制 Unity 中 3D 物體的位置追蹤。

### 系統架構

```
手機瀏覽器 (iOS/Android)
    ↓
AR.js Marker 追蹤
    ↓
WebSocket 數據傳輸
    ↓
Unity 應用程序 (Windows/Mac)
    ↓
3D 物體位置更新
```

---

## 功能特點

### ✅ 主要功能

1. **基於 Marker 的 6DOF 追蹤**
   - 使用 AR.js 開源庫
   - 追蹤相機相對於 Marker 的位置（X, Y, Z）
   - 追蹤相機相對於 Marker 的旋轉（四元數）

2. **實時數據傳輸**
   - WebSocket 實時通信
   - 60fps 位置更新
   - 低延遲數據傳輸

3. **可視化數據面板**
   - 實時顯示 Position (X, Y, Z)
   - 實時顯示 Rotation (X, Y, Z, W)
   - 實時顯示 Delta（相對位移）
   - FPS 幀率顯示

4. **可調參數**
   - 位置縮放比例（Scale）
   - 位置偏移量（Offset X, Y, Z）
   - 重置初始位置

### 🎯 優勢

- ✅ **完全免費**：無需 API Key，開源庫
- ✅ **穩定可靠**：基於標記追蹤，精度高
- ✅ **跨平臺**：支持 iOS 和 Android
- ✅ **易於使用**：打印 Marker 即可使用
- ✅ **實時同步**：Unity 物體實時跟隨手機移動

---

## 使用前準備

### 1. 準備 Marker 圖案

#### 方法一：使用默認 Hiro Marker（推薦）

1. 訪問 AR.js 官網下載 Hiro Marker：
   - 網址：https://jeromeetienne.github.io/AR.js/data/images/HIRO.jpg
   - 或使用項目中的默認 Marker

2. 打印 Marker：
   - 使用 A4 紙張打印
   - 確保 Marker 清晰可見
   - 建議尺寸：10cm × 10cm 或更大

#### 方法二：生成自定義 Marker

1. 訪問 Marker 生成器：
   - https://jeromeetienne.github.io/AR.js/three.js/examples/marker-training/examples/generator.html

2. 上傳圖片或使用默認圖案生成 `.patt` 文件

3. 下載並替換 `position-test.html` 中的 Marker URL

### 2. 環境要求

- **網絡環境**：需要 HTTPS 或 localhost（AR.js 要求）
- **瀏覽器**：支持 WebRTC 的現代瀏覽器
  - Chrome/Edge（Android）
  - Safari（iOS 11+）
- **設備**：帶攝像頭的手機（iOS/Android）
- **Unity 端**：運行 Unity 應用程序並連接到 WebSocket 服務器

### 3. 服務器配置

確保 WebSocket 服務器正在運行：
- 默認地址：`wss://testgyroscopehtml-production.up.railway.app`
- 如需修改，編輯 `position-test.html` 中的 WebSocket URL

---

## 使用步驟

### 第一步：打開位移測試頁面

1. 在手機瀏覽器中打開主頁面：`index.html`
2. 點擊頂部導航欄的 **"📍 位移測試"** 按鈕
3. 頁面會自動跳轉到 `position-test.html`

### 第二步：允許攝像頭權限

1. 瀏覽器會彈出攝像頭權限請求
2. 點擊 **"允許"** 或 **"允許訪問攝像頭"**
3. 如果拒絕，AR.js 無法啟動，需要刷新頁面重新授權

### 第三步：對準 Marker

1. 將打印好的 Marker 圖案放在桌面上
2. 確保 Marker 在攝像頭視野內
3. 保持適當距離（建議 20-50cm）
4. 確保光照充足，Marker 清晰可見

### 第四步：開始追蹤

1. 當 Marker 被檢測到時：
   - 狀態欄會顯示 "Marker: 已檢測"
   - 頁面中央的提示會消失
   - 數據面板開始顯示實時數值

2. 移動手機：
   - **向上移動** → Position Y 增加 → Unity 物體向上
   - **向右移動** → Position X 增加 → Unity 物體向右
   - **向前移動** → Position Z 增加 → Unity 物體向前
   - **旋轉手機** → Rotation 改變 → Unity 物體旋轉

### 第五步：調整參數（可選）

在數據面板底部的控制區域：

1. **重置初始位置**：
   - 點擊 "重置初始位置" 按鈕
   - 當前 Marker 位置會被設為新的參考點

2. **調整縮放**：
   - 修改 "縮放" 輸入框的數值
   - 默認 1.0，增大數值會放大移動幅度

3. **設置偏移**：
   - 修改 "偏移 X/Y/Z" 輸入框
   - 用於微調 Unity 中的物體位置

---

## 技術參數

### AR.js 配置

- **追蹤模式**：Pattern Marker（圖案標記）
- **默認 Marker**：Hiro Marker
- **檢測模式**：mono_and_matrix
- **矩陣類型**：3x3
- **最大檢測率**：60fps
- **平滑追蹤**：啟用

### 數據格式

發送到 Unity 的數據格式：

```json
{
  "type": "position",
  "data": {
    "position": {
      "x": 0.0,
      "y": 0.0,
      "z": 0.0
    },
    "rotation": {
      "x": 0.0,
      "y": 0.0,
      "z": 0.0,
      "w": 1.0
    },
    "delta": {
      "x": 0.0,
      "y": 0.0,
      "z": 0.0
    },
    "timestamp": 1234567890
  }
}
```

### 座標系說明

- **Position (X, Y, Z)**：相機相對於 Marker 的位置（米）
  - X：左右（右為正）
  - Y：上下（上為正）
  - Z：前後（前為正）

- **Rotation (X, Y, Z, W)**：相機相對於 Marker 的旋轉（四元數）
  - Unity 兼容格式

- **Delta (X, Y, Z)**：相對於初始位置的位移（米）

---

## 常見問題

### Q1: Marker 無法被檢測到？

**可能原因：**
- Marker 不在攝像頭視野內
- 光照不足
- Marker 圖案模糊或損壞
- 距離太近或太遠

**解決方法：**
1. 確保 Marker 完全在攝像頭視野內
2. 改善光照條件
3. 重新打印清晰的 Marker
4. 調整距離到 20-50cm

### Q2: WebSocket 連接失敗？

**可能原因：**
- 網絡連接問題
- 服務器未運行
- WebSocket URL 配置錯誤

**解決方法：**
1. 檢查網絡連接
2. 確認服務器正在運行
3. 檢查 `position-test.html` 中的 WebSocket URL
4. 點擊 "重新連接 WebSocket" 按鈕

### Q3: 攝像頭權限被拒絕？

**解決方法：**
1. 刷新頁面
2. 在瀏覽器設置中允許攝像頭權限
3. iOS Safari：設置 → Safari → 攝像頭 → 允許

### Q4: Unity 物體不移動？

**可能原因：**
- WebSocket 未連接
- Unity 端未正確接收數據
- PositionController 未正確配置

**解決方法：**
1. 檢查 WebSocket 連接狀態（頁面頂部狀態欄）
2. 檢查 Unity 控制檯是否有錯誤
3. 確認 `PositionController` 已附加到目標物體
4. 確認 `GyroscopeReceiver` 正在運行

### Q5: 位置數據不準確？

**解決方法：**
1. 確保 Marker 平整放置
2. 避免 Marker 反光
3. 保持穩定的光照
4. 調整縮放比例以適應 Unity 場景

---

## 故障排除

### 問題：AR.js 初始化失敗

**症狀：** 狀態欄顯示 "AR.js: 錯誤" 或頁面無響應

**解決步驟：**
1. 檢查瀏覽器控制檯錯誤信息
2. 確認 A-Frame 和 AR.js 庫已正確加載
3. 檢查網絡連接（需要加載 CDN 資源）
4. 嘗試刷新頁面

### 問題：Marker 檢測不穩定

**症狀：** Marker 頻繁丟失，追蹤中斷

**解決方法：**
1. 改善光照條件（避免強光和陰影）
2. 確保 Marker 平整，無褶皺
3. 保持手機穩定，避免快速移動
4. 調整 Marker 大小（建議 10cm × 10cm 或更大）

### 問題：數據更新延遲

**症狀：** Unity 物體移動有延遲

**解決方法：**
1. 檢查網絡延遲（查看 WebSocket 狀態）
2. 檢查 Unity 端的平滑設置（`smoothingFactor`）
3. 降低 Unity 端的平滑係數以獲得更快響應
4. 檢查服務器性能

### 問題：位置方向相反

**症狀：** 手機向右移動，Unity 物體向左移動

**解決方法：**
1. 調整縮放比例為負值（如 -1.0）
2. 或在 Unity 端調整座標軸映射
3. 使用偏移量進行微調

---

## 高級配置

### 自定義 Marker

1. 生成自定義 Marker：
   ```
   訪問：https://jeromeetienne.github.io/AR.js/three.js/examples/marker-training/examples/generator.html
   ```

2. 修改 `position-test.html`：
   ```html
   <a-marker 
       type="pattern" 
       url="path/to/your/custom-marker.patt"
       id="custom-marker">
   ```

### 調整追蹤參數

在 `position-test.html` 的 `<a-scene>` 標籤中修改：

```html
<a-scene 
    arjs="
        sourceType: webcam;
        sourceWidth: 1280;
        sourceHeight: 720;
        maxDetectionRate: 60;
        smooth: true;
        smoothCount: 10;
        smoothTolerance: .01;
        smoothThreshold: 5;
    ">
```

### Unity 端配置

在 `PositionController.cs` 中調整：

```csharp
[SerializeField] private float positionSensitivity = 1f;  // 位置敏感度
[SerializeField] private float smoothingFactor = 0.1f;   // 平滑係數
[SerializeField] private bool useDeltaMovement = true;    // 使用相對位移
```

---

## 性能優化建議

1. **降低幀率**：如果性能不足，可以降低 `maxDetectionRate`
2. **減少數據發送頻率**：在 `updatePosition()` 中添加節流
3. **優化 Unity 端**：使用對象池、減少不必要的計算
4. **網絡優化**：使用本地服務器減少延遲

---

## 技術支持

### 相關文檔

- AR.js 官方文檔：https://ar-js-org.github.io/AR.js-Docs/
- A-Frame 文檔：https://aframe.io/docs/
- WebSocket API：https://developer.mozilla.org/en-US/docs/Web/API/WebSocket

### 調試工具

1. **瀏覽器控制檯**：
   - 打開開發者工具（F12）
   - 查看 Console 標籤頁
   - 查看 Network 標籤頁（WebSocket 連接）

2. **Unity 控制檯**：
   - 查看 Debug.Log 輸出
   - 檢查 `PositionController` 的調試信息

---

## 更新日誌

### v1.0 (2024-12-06)
- ✅ 初始版本發佈
- ✅ AR.js Marker 追蹤集成
- ✅ WebSocket 數據傳輸
- ✅ 實時數據顯示面板
- ✅ 參數調整功能

---

## 許可證

本系統使用以下開源庫：
- AR.js：MIT License
- A-Frame：MIT License
- Three.js：MIT License

---

**最後更新：2024-12-06**

