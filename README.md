# unity-web-sensor

**用手機瀏覽器即時控制 Unity — 不需要安裝 App**  
**Control Unity in real-time from a mobile browser — no app required**

手機透過 WebSocket 或 UDP 將感測器資料傳送到 Unity。  
Phone sends sensor data to Unity over WebSocket or UDP.

---

## 系統架構 / Architecture

```
手機瀏覽器 sensor.html          Mobile Browser
  加速度 / 陀螺儀 / 虛擬搖桿       Accelerometer / Gyro / Joystick
         │ WebSocket JSON
         ▼
  server.js  (Node.js)
  廣播轉發 / Broadcast relay
         │ WebSocket JSON
         ▼
  Unity Scene
  WebRtcGyroscopeReceiver.cs
         │
         ▼
  SensorEvents.cs  ──→  你的腳本 / Your scripts
```

> 本地測試也可以跳過 Node.js，用 UDP 直連：  
> For local testing, you can also skip Node.js and use direct UDP:
> ```
> 手機 sensor.html  ──UDP──>  UdpGyroscopeReceiver.cs
> ```

---

## 檔案結構 / File Structure

```
unity-web-sensor/
├── server.js              # WebSocket 廣播伺服器 / Relay server
├── package.json
├── railway.toml           # Railway 雲端部署 / Cloud deploy config
├── Dockerfile
│
├── TestHtml/              # 網頁端（server.js 靜態服務）/ Web pages
│   ├── sensor.html        # ★ 手機控制頁（搖桿＋感測器）/ Main controller
│   ├── index.html
│   ├── gyroscope.html
│   └── gyroscope-cube.html
│
└── UnityWebsocket0329/    # ★ Unity 專案 / Unity Project
    ├── Assets/Scenes/
    │   └── 0520.unity              # 主場景 / Main scene
    ├── Assets/Scripts/
    │   ├── SensorEvents.cs              # 事件中樞 / Event hub
    │   ├── WebRtcGyroscopeReceiver.cs   # WebSocket 接收器 / WS receiver
    │   ├── UdpGyroscopeReceiver.cs      # UDP 接收器 / UDP receiver
    │   ├── TcpGyroscopeReceiver.cs      # TCP 接收器 / TCP receiver
    │   ├── WebRtcJoystickController.cs  # 搖桿控制 / Joystick
    │   ├── GyroToRotation.cs            # 陀螺儀旋轉 / Gyro rotation
    │   ├── QrCodeDisplay.cs             # 連線 QR Code
    │   ├── AccelerometerBallEffect.cs   # 加速度球體（見備註）
    │   └── ...（其他輔助腳本）
    ├── Packages/manifest.json   # 套件清單（首次開啟自動安裝）
    └── ProjectSettings/
```

---

## 快速開始 / Quick Start

### 環境需求 / Requirements

| | 版本 |
|---|---|
| Node.js | >= 16 |
| Unity | 2022.3 LTS |
| 瀏覽器 | iOS Safari 16+ / Android Chrome |

---

### 1. Clone

```bash
git clone https://github.com/AhhhhHeyyy/unity-web-sensor.git
cd unity-web-sensor
```

---

### 2. 開啟 Unity 專案 / Open Unity Project

**Unity Hub → Add → 選擇 `UnityWebsocket0329/` 資料夾**

首次開啟會自動安裝套件（需等幾分鐘）。  
First open auto-installs packages (may take a few minutes).

開啟場景：`Assets/Scenes/0520.unity`

---

### 3. 啟動伺服器 / Start server

```bash
npm install
node server.js
```

---

### 4. 設定 Unity 連線 / Set connection URL

找到場景中掛有 `WebRtcGyroscopeReceiver` 的 GameObject，  
將 **Server Url** 改為你電腦的區域網路 IP：

```
ws://192.168.x.x:8080
```

> 查詢 IP：Windows → `ipconfig` → IPv4 位址  
> Find IP: Windows → `ipconfig` → IPv4 Address

確認手機與電腦在**同一 Wi-Fi**，按 Play。

---

### 5. 手機開啟控制頁面 / Open phone controller

```
http://192.168.x.x:8080/sensor.html
```

iOS 首次進入需點「允許感測器」按鈕。  
iOS: Tap "Allow Sensors" on first visit.

---

### 驗證 / Verify

| 測試 | 預期結果 |
|---|---|
| `http://localhost:8080/health` | 回傳 JSON |
| Unity Play | Console：`WebSocket 連接已建立` |
| 手機搖桿拖曳 | 目標物件移動 |
| 手機傾斜 | 球體偏移（若場景有掛 AccelerometerBallEffect） |

---

## UDP 直連（本地，免伺服器）/ UDP Direct (local, no server)

適合在同一台電腦或局域網測試，延遲更低。  
Lower latency option for local testing — no Node.js needed.

1. 在 Unity 場景改掛 **`UdpGyroscopeReceiver`**（替換 `WebRtcGyroscopeReceiver`）
2. 設定 **Port**（預設 `9000`）
3. 手機網頁端改用 UDP 發送模式（或用 `web-demo/gyro-demo.html`）

> Windows 防火牆可能封鎖 UDP → 改用 `TcpGyroscopeReceiver` 可繞過。  
> Windows Firewall may block UDP → use `TcpGyroscopeReceiver` as fallback.

---

## 雲端部署 / Cloud Deploy (Railway)

iOS Safari 需要 HTTPS 才能存取感測器，本地 HTTP 無法使用。  
iOS Safari requires HTTPS to access sensors — local HTTP won't work.

```bash
npm install -g @railway/cli
railway login
railway up
```

或在 [railway.app](https://railway.app) 直接連接 GitHub repo `unity-web-sensor`。  
Or connect the GitHub repo directly on [railway.app](https://railway.app).

部署後更新 Unity 的 **Server Url**：  
After deploy, update Unity **Server Url** to:

```
wss://wtb-sensor-production.up.railway.app
```

手機控制頁面 / Mobile controller:
```
https://wtb-sensor-production.up.railway.app/sensor.html
```

---

## 訊息格式 / Message Format

```json
{ "type": "acceleration", "data": { "x": 0.12, "y": -0.05, "z": 9.81 } }
{ "type": "joystick",     "data": { "x": 0.5,  "y": -0.3 } }
{ "type": "gyroscope",    "alpha": 120.5, "beta": -15.2, "gamma": 3.8 }
{ "type": "shake",        "data": { "intensity": 12.3, "count": 2 } }
```

---

## 故障排除 / Troubleshooting

**Unity 連不到** → `serverUrl` 填電腦 IP，不是 `localhost`；確認同一 Wi-Fi；開放防火牆 8080 埠  
**iOS 感測器無反應** → 必須用 HTTPS（部署到 Railway），首次進入點允許按鈕  
**數值抖動** → 增大 `inputFilterTime`（`0.05 → 0.1`）或降低 `sensitivity`  
**球體方向相反** → 在 `movementAxesMask` 將對應軸改為負值

---

<details>
<summary>📦 備註：AccelerometerBallEffect（加速度球體移動）</summary>

讓球體跟著手機傾斜移動，停止後自動回中，模擬水平儀氣泡效果。

**掛載方式：** 將 `AccelerometerBallEffect.cs` 掛到球體 GameObject 上。

| Inspector 欄位 | 說明 | 建議值 |
|---|---|---|
| `sensitivity` | 加速度放大倍率 | `0.1 ~ 1.0` |
| `smoothSpeed` | 追蹤速度 | `5 ~ 20` |
| `maxOffset` | 最大偏移（米）| `1.0 ~ 5.0` |
| `movementAxesMask` | 受影響的軸 | `(1,1,0)` = XY 平面 |

輔助腳本：
- `AccelerometerBallEffect.HUD.cs` — 畫面除錯顯示
- `AccelerometerBallEffect.Wizard.cs` — 互動式靈敏度校正嚮導
- `AccelerometerBallEffectUI.cs` — IMGUI 即時參數面板

</details>

<details>
<summary>🎛️ 備註：WebRtcJoystickController（虛擬搖桿 / 扭蛋機旋鈕）</summary>

接收手機 `sensor.html` 上的圓形拖曳搖桿輸入，控制目標物件移動或旋轉。

**掛載方式：** 將 `WebRtcJoystickController.cs` 掛到目標 GameObject 上。

訂閱事件寫法：
```csharp
void OnEnable()  => SensorEvents.OnJoystickReceived += HandleJoystick;
void OnDisable() => SensorEvents.OnJoystickReceived -= HandleJoystick;

void HandleJoystick(Vector2 input)
{
    transform.Translate(input.x * speed, 0, input.y * speed);
}
```

> 扭蛋機旋鈕模式（snap to 90°）需搭配 `SpinTest/index.html` 頁面使用。

</details>

---

## 技術棧 / Tech Stack

| | 用途 |
|---|---|
| Unity 2022.3 LTS | 3D 場景、C# 邏輯 |
| SimpleWebRTC (Package) | Unity WebSocket 客戶端 |
| Node.js + ws | WebSocket 廣播伺服器 |
| DeviceMotion / DeviceOrientation API | 手機感測器讀取 |
| Railway | 雲端部署（支援 WSS / HTTPS）|

---

## 更新紀錄 / Changelog

### 2026-06-16
**long0610/sensor0610.html — 導入 8thWall AR 地板偵測，直升機初始位置錨定真實地板**

- 整合 `@8thwall/engine-binary`（jsdelivr CDN，無需 API Key）提供跨平台 SLAM 地板偵測，支援 iOS Safari 與 Android Chrome
- 新增 `data-preload-chunks="slam"` 預載 SLAM 模組，頁面載入即開始環境掃描
- 實作 8thWall Camera Pipeline Module：每幀從 `processCpuResult.reality` 取得相機位姿（position + quaternion）驅動 Three.js camera，讓 3D 場景正確疊合真實世界
- 新增綠色 Reticle 圓環（`THREE.RingGeometry`）作為地板偵測指示器：觸碰螢幕時呼叫 `XR8.XrController.hitTest()` 更新 Reticle 位置，放開手指後直升機錨定至該位置上方 0.3m
- 長按螢幕 2 秒可重置放置位置，重新選點
- `arAnchorPos` 偏移疊加到原有的 `movePos + chopperPosDebug`，搖桿與陀螺儀控制邏輯完全不受影響
- AR 模式啟動時隱藏 `#camera-bg` video（改由 `XR8.GlTextureRenderer` 渲染鏡頭背景）；8thWall 未載入時（桌機/網路問題）3 秒後自動 fallback 至原有動畫迴圈

---

### 2026-06-12（六）
**long0610/sensor0610.html — 搖桿放開時禁止傾斜造成上下漂移，修正朝向指示棒翻轉**

- 問題：搖桿放開時，傾斜角度仍會單獨驅動垂直速度，導致飛機在搖桿無輸入時也會因手機傾斜而上下漂移；且此時 `targetVel` 變成純垂直向量，傾斜方向跨越死區時朝向指示棒會在「機身前方」與「正上/正下」之間瞬間切換，呈現 180° 翻轉
- 改為：新增 `joyActive`（`joyH²+joyV² > 1e-4`）判斷，只有搖桿有水平輸入時，傾斜角度才會疊加垂直速度；搖桿放開時 `vertInput` 強制為 0，`targetVel` 為零向量，朝向指示棒穩定顯示機身前方

---

### 2026-06-12（五）
**long0610/sensor0610.html — 上下飛行改由機身傾斜角度單獨控制**

- 原本搖桿前後（`joyV`）是沿著隨機身傾斜旋轉的 `flightForward` 移動，導致手機朝上傾斜時「後退」反而往下飛，朝向指示棒也會從朝上瞬間翻成朝下，違反直覺
- 改為：`flightForward`／`flightRight` 先投影到水平面（XZ）得到 `flightForwardXZ`／`flightRightXZ`，搖桿前後左右只用這兩個水平方向換算速度
- 垂直速度改由 `flightForward.y`（機身傾斜量）直接決定，與搖桿輸入無關；新增 `TILT_DEADZONE = 0.05` 避免微小傾斜造成持續緩慢漂移
- 朝向指示棒：有實際飛行速度（水平搖桿輸入或垂直傾斜分量）時指向該方向，否則顯示機身前方

---

### 2026-06-12（四）
**long0610/sensor0610.html — 朝向指示棒改為顯示實際飛行方向**

- 原本朝向指示棒（`headingArrow`）固定指向機身前方 `flightForward`，但搖桿往後推時，實際飛行方向（含上下）是 `flightForward` 的反方向，導致箭頭朝向與實際移動的上下方向相反
- 改為：先計算 `targetVel`（搖桿輸入換算後的目標飛行速度），有輸入時箭頭方向改為 `targetVel` 正規化後的方向；無輸入時維持顯示機身前方 `flightForward` 作為預設參考

---

### 2026-06-12（三）
**sensor.html — 直式模式按鈕排版調整；long0610 直升機改用陀螺儀四元數姿態控制**

- **sensor.html 直式（非橫式）模式排版**：
  - `#controls-row` 加上 `flex-wrap: wrap`，避免抓取／設定點／搖桿三者同排時超出螢幕寬度被裁切
  - 用 `order` 與 `flex: 0 0 100%` 讓「抓取」與「搖桿」固定同一排，「設定點」獨立換到下一排置中
  - 抓取鈕放大（100→130px，窄螢幕 110px）、搖桿縮小（220→190px，窄螢幕 170px），讓兩者大小較平均；`joy-knob` 同步等比縮放
  - 直式模式下隱藏「模擬搖桿」標題文字，讓出空間給「⇄ 左右對調」按鈕
- **long0610/sensor0610.html — 直升機姿態改用四元數**：
  - 新增 `eulerToQuat()`（與 sensor.html `sendPacket()` 相同寫法），將陀螺儀 alpha/beta/gamma 轉為四元數，取代原本只用 `chopper.rotation.x/z` 控制俯仰／橫滾
  - 新增 `cs.alphaOffset`：感測器啟動當下的 alpha 角度作為機頭正前方基準，`alphaRel = alpha - alphaOffset`
  - 橫式持握時 `effBeta = -gamma`、`effGamma = beta`（與 sensor.html 的橫式校正一致）
  - 移除原本以「玩家輸入方向」計算 `yawOffset` 的機頭轉向邏輯與 `TURN_SPEED`，機頭朝向改為直接反映裝置實際姿態（含 yaw）
  - 中斷連線時重置 `cs.alphaOffset = null`，下次連線重新校正基準角

---

### 2026-06-12（二）
**sensor.html — 修正直拿橫式旋轉模式的版面裁切；long0610 場景偵錯面板數值調整**

- **修正版面裁切**：直拿手機看橫式 UI 時，抓取鈕／搖桿的上下緣會被裁掉。原因是旋轉容器用 `100vw`/`100vh`（含被網址列遮住的區域），實際可視高度比這個值小，`overflow:hidden` 因此裁掉超出可視範圍的內容
  - 改用 JS（`updateViewportVars()`）讀取 `window.visualViewport` 的即時寬高，寫入 CSS 變數 `--vvw`/`--vvh`，旋轉容器與 `--joy-size`、`#controls-row` 高度都改用這兩個變數計算
  - 不用 `dvh`/`dvw`：先前曾改用過，但網址列顯示/隱藏時會造成版面跳動（即 6/12 稍早一版已修正過的問題），改用 JS 量測可同時避免裁切與跳動
  - `resize` / `orientationchange` / `visualViewport.resize` 事件都會重新量測並呼叫 `updateJoyMax()`
- **long0610/sensor0610.html**：相機與城市場景偵錯面板的預設數值調整（`offsetY`、城市角度、移動範圍 X/Z、直升機初始位置等），用於場景視角微調

---

### 2026-06-12
**sensor.html — 新增「設定點」按鈕；橫式旋轉跑版修正；自動連線**

- **新增「設定點」按鈕**：`#controls-row` 中，抓取鈕與搖桿之間新增綠色圓形按鈕，按下時透過 WebRTC DataChannel 送出 **1-byte `0x05`** 訊息
  - 格式延續抓取鈕（`0x03`=按下／`0x04`=放開）的 1-byte 指令慣例，`0x05` = 設定點
  - ⚠️ Unity 端尚未實作接收：`dc.OnMessage` 過濾條件目前只放行 28-byte / 9-byte(0x02) / 1-byte(`0x03`,`0x04`)，`0x05` 會被直接丟棄；`SensorEvents.cs` 已有 `OnGrabPressed`/`OnGrabReleased`，但尚無 `OnSetPointReceived`
  - 之後若要接上邏輯，需要：① `OnMessage` 過濾條件加入 `bytes.Length == 1 && bytes[0] == 0x05`　② `ProcessPacket()` 加判斷並呼叫 `SensorEvents.RaiseSetPointReceived()`　③ `SensorEvents.cs` 新增：
    ```csharp
    public static event Action OnSetPointReceived;
    public static void RaiseSetPointReceived() => OnSetPointReceived?.Invoke();
    ```
- **修正橫式旋轉跑版**：直拿手機時用來模擬橫式的 `rotate(90deg)` hack，改用「貼右邊界 + 左上角為旋轉軸心」的標準寫法（`top:0; left:100vw; transform-origin:0 0`），取代原本混用 `dvh`/`dvw` 置中算式（在網址列顯示/隱藏時會跑位）；同時修正 `landscape` class 只在「傳輸中」畫面套用，避免連線/權限畫面被一起旋轉
- **自動連線**：頁面載入後，若瀏覽器不需要 iOS 13+ 的 `requestPermission` 手勢觸發，會自動開始連線，不需再按「開始傳送」按鈕

---

### 2026-06-03
**sensor.html — 直拿手機（橫式旋轉模式）版面修正**

- **修正根本 bug**：`body { min-height: 100dvh }` 在 portrait+landscape 旋轉模式下蓋過 `height: 100dvw`，導致 body 變成 667×667 正方形，rotate 後整個版面往下偏移 146px；加入 `min-height: 0` 修正
- **修正搖桿/抓取按鈕溢出**：`--joy-size` 從 `calc(100dvh - 52px)` 改為 `calc(50dvh - 10px)`（橫式）與 `calc(50dvw - 10px)`（直拿橫式），確保搖桿不超出 `controls-row` 高度
- **控制區垂直置中**：`#grab-section` / `#joy-wrapper` 由 `align-items: flex-end + padding-bottom` 改為 `align-items: center`，按鈕置中於下半段
- **按鈕上移**：`#controls-row` 加 `margin-bottom: 4dvh`（橫式）/ `4dvw`（直拿橫式），控制區略往上偏移
- **新增 `?preview` 參數**：網址加 `?preview` 可跳過 WebRTC 連線直接預覽 UI

---

MIT License
