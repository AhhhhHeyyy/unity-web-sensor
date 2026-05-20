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

MIT License
