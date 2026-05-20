# unity-web-sensor

**用手機瀏覽器即時控制 Unity 場景 — 不需要安裝 App**
**Control Unity scenes in real-time from a mobile browser — no app required**

> 手機透過 WebSocket 將感測器資料（加速度、陀螺儀）與虛擬搖桿傳送到 Unity。
> Phone sends sensor data (accelerometer, gyroscope) and virtual joystick input to Unity over WebSocket.

---

## 目錄 / Table of Contents

- [系統架構 / Architecture](#系統架構--architecture)
- [檔案結構 / File Structure](#檔案結構--file-structure)
- [Unity 腳本說明 / Unity Scripts](#unity-腳本說明--unity-scripts)
- [快速開始 / Quick Start](#快速開始--quick-start)
- [雲端部署 / Cloud Deploy](#雲端部署--cloud-deploy)
- [故障排除 / Troubleshooting](#故障排除--troubleshooting)

---

## 系統架構 / Architecture

```
手機瀏覽器 (sensor.html)          Mobile Browser
  加速度 / 陀螺儀 / 虛擬搖桿         Accelerometer / Gyroscope / Virtual Joystick
         │ WebSocket JSON
         ▼
  Node.js 伺服器 (server.js)       Node.js Server
  廣播轉發所有訊息                   Broadcasts messages to all clients
         │ WebSocket JSON
         ▼
  Unity 場景                        Unity Scene
  WebRtcGyroscopeReceiver.cs        Receives data → fires C# events
  接收資料 → 觸發 C# 事件             ↓
         │                         SensorEvents.cs (event hub)
         ▼                         ↙        ↘
  AccelerometerBallEffect.cs    WebRtcJoystickController.cs
  球體跟著傾斜移動                  搖桿控制物件移動
  Ball moves with phone tilt        Joystick controls object
```

---

## 檔案結構 / File Structure

```
unity-web-sensor/
│
├── server.js                  # WebSocket 廣播伺服器 / WebSocket relay server
├── webrtc-server.js           # WebRTC 信令伺服器變體 / WebRTC signaling variant
├── package.json               # Node.js 依賴 / Node.js dependencies
├── railway.toml               # Railway 雲端部署設定 / Railway deploy config
├── Dockerfile                 # Docker 部署 / Docker deploy
│
├── TestHtml/                  # 網頁端（由 server.js 靜態服務）
│   │                          # Web pages (served by server.js)
│   ├── sensor.html            # ★ 手機控制頁面（搖桿＋感測器）
│   │                          #   Main phone controller (joystick + sensors)
│   ├── index.html             # 首頁 / Landing page
│   ├── gyroscope.html         # 陀螺儀測試 / Gyroscope test
│   └── gyroscope-cube.html    # 3D 方塊展示 / 3D cube demo
│
├── docs/                      # 額外展示頁 / Additional demos
├── web-demo/                  # 本地 UDP→WebSocket 展示 / Local UDP→WS demo
│   ├── gyro-demo.html
│   └── gyro-relay.js
│
└── UnityWebsocket0329/        # ★ Unity 專案（用 Unity Hub 開啟此資料夾）
    │                          #   Unity Project (open this folder in Unity Hub)
    ├── Assets/
    │   ├── Scenes/
    │   │   ├── 0520.unity              # 主場景 / Main scene
    │   │   └── SpinAndGroTest_0329.unity
    │   └── Scripts/
    │       ├── SensorEvents.cs              # 事件中樞（所有腳本的橋樑）
    │       │                                # Event hub (bridge between scripts)
    │       ├── WebRtcGyroscopeReceiver.cs   # WebSocket 接收＋事件分派
    │       │                                # WebSocket receiver + event dispatch
    │       ├── WebRtcJoystickController.cs  # 搖桿輸入 → 物件移動
    │       │                                # Joystick input → object movement
    │       ├── AccelerometerBallEffect.cs   # 加速度 → 球體位移
    │       │                                # Accelerometer → ball displacement
    │       ├── AccelerometerBallEffect.HUD.cs    # HUD 除錯顯示 / Debug HUD
    │       ├── AccelerometerBallEffect.Wizard.cs # 靈敏度校正嚮導 / Calibration wizard
    │       ├── AccelerometerBallEffectUI.cs      # IMGUI 即時參數面板 / IMGUI panel
    │       ├── GyroToRotation.cs            # 陀螺儀 → 物件旋轉 / Gyro → rotation
    │       ├── UdpGyroscopeReceiver.cs      # UDP 接收器（本地測試用）
    │       │                                # UDP receiver (local testing)
    │       ├── TcpGyroscopeReceiver.cs      # TCP 接收器（防火牆替代方案）
    │       │                                # TCP receiver (firewall workaround)
    │       └── QrCodeDisplay.cs             # 顯示連線 QR Code / Show connection QR
    ├── Packages/
    │   └── manifest.json       # ★ 套件清單（首次開啟自動安裝）
    │                           #   Package list (auto-installs on first open)
    └── ProjectSettings/        # Unity 專案設定 / Unity project settings
```

---

## Unity 腳本說明 / Unity Scripts

### `SensorEvents.cs` — 事件中樞 / Event Hub

所有感測器事件的靜態入口。其他腳本訂閱這裡的事件，不需要直接引用接收器。
Static entry point for all sensor events. Other scripts subscribe here without needing a direct reference to the receiver.

```csharp
// 訂閱方式 / Subscribe
void OnEnable()
{
    SensorEvents.OnAccelerationReceived += HandleAcceleration;
    SensorEvents.OnJoystickReceived     += HandleJoystick;
    SensorEvents.OnGyroscopeReceived    += HandleGyro;
}
void OnDisable()
{
    SensorEvents.OnAccelerationReceived -= HandleAcceleration; // 必須取消訂閱！
    SensorEvents.OnJoystickReceived     -= HandleJoystick;     // Must unsubscribe!
    SensorEvents.OnGyroscopeReceived    -= HandleGyro;
}
```

---

### `WebRtcGyroscopeReceiver.cs` — WebSocket 接收器 / Receiver

連線到伺服器，解析 JSON 訊息，觸發 `SensorEvents` 事件。
Connects to server, parses JSON messages, fires `SensorEvents`.

| Inspector 欄位 | 說明 | 預設值 |
|---|---|---|
| `serverUrl` | WebSocket 伺服器網址 | `ws://localhost:8080` |
| `autoConnect` | 啟動時自動連線 | `true` |
| `reconnectInterval` | 斷線重連間隔（秒）| `5` |
| `debugLog` | 詳細 Log | `false` |

---

### `AccelerometerBallEffect.cs` — 球體移動 / Ball Movement

訂閱加速度事件，讓球體跟著手機傾斜移動，停止後自動回中。
Subscribes to acceleration events; ball follows phone tilt and returns to center.

| Inspector 欄位 | 說明 | 建議範圍 |
|---|---|---|
| `sensitivity` | 加速度放大倍率 | `0.1 ~ 1.0` |
| `smoothSpeed` | 追蹤速度 | `5 ~ 20` |
| `maxOffset` | 最大偏移（米）| `1.0 ~ 5.0` |
| `movementAxesMask` | 哪些軸受影響 | `(1,1,0)` = XY 平面 |

---

### `WebRtcJoystickController.cs` — 搖桿控制器 / Joystick

接收手機虛擬搖桿的 XY 輸入，套用到目標物件的移動。
Receives virtual joystick XY input from phone, applies to target object movement.

---

### `GyroToRotation.cs` — 陀螺儀旋轉 / Gyro Rotation

訂閱陀螺儀四元數事件，直接映射到 Unity 物件的旋轉。
Subscribes to gyroscope quaternion events and maps them to object rotation.

---

### `UdpGyroscopeReceiver.cs` / `TcpGyroscopeReceiver.cs`

不透過 Node.js 的本地替代方案。  
Local alternatives that bypass the Node.js server.
- **UDP**：低延遲，但 Windows 防火牆可能封鎖 / Low latency, may be blocked by Windows firewall
- **TCP**：穩定，適合防火牆環境 / Stable, firewall-friendly

---

## 快速開始 / Quick Start

### 環境需求 / Requirements

| 工具 / Tool | 版本 / Version |
|---|---|
| Node.js | >= 16 |
| Unity | 2022.3 LTS |
| 瀏覽器 / Browser | iOS Safari 16+ / Android Chrome |

---

### Step 1 — Clone 專案 / Clone the repo

```bash
git clone https://github.com/AhhhhHeyyy/unity-web-sensor.git
cd unity-web-sensor
```

---

### Step 2 — 開啟 Unity 專案 / Open Unity Project

1. 打開 **Unity Hub**
2. 點 **Add** → 選擇 `unity-web-sensor/UnityWebsocket0329/` 資料夾
3. Unity 會自動讀取 `Packages/manifest.json` 並安裝所有套件（首次需等待幾分鐘）
4. 開啟場景：`Assets/Scenes/0520.unity`

> Unity Hub → **Add** → select the `UnityWebsocket0329/` folder.  
> Unity auto-installs packages from `manifest.json` on first open (may take a few minutes).

---

### Step 3 — 啟動伺服器 / Start the server

```bash
npm install
node server.js
```

成功時終端機會顯示 / Server is running when you see:
```
🚀 WebSocket 伺服器啟動成功
📱 http://localhost:8080
```

---

### Step 4 — 設定 Unity 連線 / Configure Unity connection

1. 在場景中找到掛有 `WebRtcGyroscopeReceiver` 的 GameObject
2. 將 `Server Url` 改為電腦的區域網路 IP：

```
ws://192.168.x.x:8080
```

> 查詢電腦 IP：Windows → `ipconfig`，找 **IPv4 位址**  
> Find your IP: Windows → `ipconfig`, look for **IPv4 Address**

3. 確認手機與電腦在**同一 Wi-Fi**，按下 Play

---

### Step 5 — 開啟手機頁面 / Open phone page

在手機瀏覽器輸入 / Enter in mobile browser:
```
http://192.168.x.x:8080/sensor.html
```

iOS 需要允許感測器權限 → 點選頁面上的「允許」按鈕。  
iOS: Tap the **Allow** button on the page to grant sensor permission.

---

### 驗證清單 / Verification Checklist

| 測試 / Test | 預期結果 / Expected |
|---|---|
| 伺服器健康檢查 / Server health | `http://localhost:8080/health` 回傳 JSON |
| Unity 連線 / Unity connect | Console：`WebSocket 連接已建立` |
| 手機傾斜 / Phone tilt | Unity 中球體偏移 / Ball in Unity moves |
| 搖桿拖曳 / Joystick drag | 目標物件移動 / Target object moves |

---

## 雲端部署 / Cloud Deploy

### Railway（推薦 / Recommended）

[![Deploy on Railway](https://railway.app/button.svg)](https://railway.app)

```bash
npm install -g @railway/cli
railway login
railway up
```

部署完成後，URL 格式為 `wss://your-app.up.railway.app`。  
將 Unity 的 `serverUrl` 改為此 WSS 網址。  
After deploy, update Unity `serverUrl` to `wss://your-app.up.railway.app`.

> iOS Safari 在 HTTP 環境下無法存取感測器，**必須使用 HTTPS/WSS 的雲端網址**。  
> iOS Safari requires HTTPS to access sensors — cloud deployment is required for iOS.

### Docker

```bash
docker build -t unity-web-sensor .
docker run -p 8080:8080 unity-web-sensor
```

---

## 故障排除 / Troubleshooting

### Unity 無法連線 / Unity can't connect

- 確認 `serverUrl` 填的是電腦 IP，不是 `localhost`
- 確認手機和電腦在同一 Wi-Fi
- 暫時關閉 Windows 防火牆，或開放 8080 埠
- Make sure `serverUrl` is the computer's LAN IP, not `localhost`
- Phone and computer must be on the same Wi-Fi
- Temporarily disable Windows Firewall or open port 8080

### iOS 感測器無反應 / iOS sensors not working

- iOS 13+ 需要 HTTPS 環境
- 將伺服器部署至 Railway，使用 `https://` 網址開啟頁面
- 首次載入時點選頁面上的「允許感測器」按鈕
- iOS 13+ requires HTTPS — deploy to Railway
- Tap the "Allow Sensors" button on first load

### 球體方向相反 / Ball moves in wrong direction

在 `AccelerometerBallEffect` 的 Inspector 中，將對應軸的 `movementAxesMask` 值改為負數，或在 `sensor.html` 中反轉發送端的 X/Y 正負號。  
In the Inspector, negate the corresponding axis in `movementAxesMask`, or flip the sign of X/Y in `sensor.html`.

### 數值抖動 / Values jittering

增大 `inputFilterTime`（建議 `0.05 → 0.1`），或降低 `sensitivity`。  
Increase `inputFilterTime` (try `0.05 → 0.1`) or reduce `sensitivity`.

---

## 訊息格式 / Message Format

手機 → 伺服器 → Unity 的 JSON 格式 / JSON format from phone → server → Unity:

```json
// 加速度 / Acceleration
{ "type": "acceleration", "data": { "x": 0.12, "y": -0.05, "z": 9.81 } }

// 搖桿 / Joystick
{ "type": "joystick", "data": { "x": 0.5, "y": -0.3 } }

// 陀螺儀 / Gyroscope
{ "type": "gyroscope", "alpha": 120.5, "beta": -15.2, "gamma": 3.8 }

// 搖晃 / Shake
{ "type": "shake", "data": { "intensity": 12.3, "count": 2 } }
```

---

## 技術棧 / Tech Stack

| 技術 / Tech | 用途 / Purpose |
|---|---|
| Unity 2022.3 LTS | 3D 場景、C# 邏輯 / 3D scene, C# logic |
| SimpleWebRTC (Package) | Unity WebSocket 客戶端 / Unity WS client |
| Node.js + ws | WebSocket 廣播伺服器 / WebSocket relay server |
| DeviceMotion API | 手機加速度感測器 / Phone accelerometer |
| DeviceOrientation API | 手機陀螺儀 / Phone gyroscope |
| Railway | 雲端部署（支援 WSS）/ Cloud deploy with WSS |

---

## 授權 / License

MIT
