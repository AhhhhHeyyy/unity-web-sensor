# UnityWebsocket0329 — QR 掃描連線功能 執行企劃書

> 版本：v1.0 ／ 日期：2026-05-20 ／ 負責人：jj
> 適用範圍：UnityWebsocket0329 專案新增「掃 QR 直接連線」功能

---

## 1. 專案目標

替現有 Unity 感測器接收專案新增 **「掃 QR Code 直接連線」** 功能，使用者拿手機掃 Unity 畫面上的 QR Code，即可開始把手機的加速度／陀螺儀／四元數／重力資料傳給 Unity，**不需手動輸入 IP、不需要安裝 App**。

同時支援兩種連線情境：

- **區網模式**：手機與 Unity 主機在同一個 Wi-Fi 下，走 P2P 直連，延遲最低（目標 < 15 ms）。
- **雲端模式**：手機與 Unity 主機分屬不同網路時，透過雲端 signaling server 完成 NAT 穿透後仍走 P2P，延遲取決於對外網路（目標 < 80 ms）。

兩種模式對使用者**無感切換**，由系統自動嘗試區網→失敗 fallback 雲端，使用者亦可在 Unity 端強制指定。

---

## 2. 系統架構

### 2.1 元件總覽

```
┌──────────────────────┐         ┌──────────────────────┐
│   Unity 主機 (PC)     │         │   使用者手機 (Web)    │
│                      │         │                      │
│  ┌────────────────┐  │  WebRTC │  ┌────────────────┐  │
│  │ WebRtcReceiver │◄─┼─────────┼─►│  sensor.html   │  │
│  │   (DataChan.)  │  │  P2P    │  │ (DeviceMotion) │  │
│  └────────┬───────┘  │         │  └────────┬───────┘  │
│           ▼          │         │           │          │
│  ┌────────────────┐  │         │           │          │
│  │ SensorEvents   │  │         │           │          │
│  │ (既有事件匯流排) │  │         │           │          │
│  └────────────────┘  │         │           │          │
│           │          │         │           │          │
│   Unity 視覺化 / HUD  │         │           │          │
└──────────┬───────────┘         └───────────┬──────────┘
           │                                 │
           │    Signaling (WebSocket)        │
           └──────────────┬──────────────────┘
                          ▼
              ┌──────────────────────┐
              │ Signaling Server     │
              │ (Node / Cloudflare)  │
              │  - 房號媒合           │
              │  - SDP / ICE 交換     │
              └──────────────────────┘
```

### 2.2 關鍵原則

1. **新通道與舊通道並存**：保留現有 `UdpGyroscopeReceiver`、`TcpGyroscopeReceiver`、WebSocket receiver，新增 `WebRtcGyroscopeReceiver`。三者透過同一個 `SensorEvents` 事件匯流排對外，**Unity 上層程式（球體效果、HUD、視覺化）零修改**。
2. **wire format 不變**：WebRTC DataChannel 上傳的還是現有的 **28 bytes Big-Endian binary**（qx, qy, qz, qw, ax, ay, az），確保下游解析邏輯共用。
3. **Signaling 只負責配對**：使用者資料**不經過 server**，雲端模式也是 P2P 直連，server 只在握手階段交換 SDP/ICE，握手完即不再參與。
4. **單一 QR 同時相容兩種載體**：QR 內容是 HTTPS URL，目前由網頁解析；未來若做 App，App 註冊 universal/app link 攔截同網域即可，QR 規格不需變更。

---

## 3. 技術選型

| 項目 | 選擇 | 理由 |
|------|------|------|
| 傳輸協定 | **WebRTC DataChannel**（`ordered: false, maxRetransmits: 0`） | UDP-like 語意、瀏覽器原生支援、內建 NAT 穿透 |
| 載體 | **網頁 PWA** | 零安裝，掃 QR 直接用；iOS/Android 一份程式碼；未來可升級 App |
| Signaling | **Node + ws** 或 **Cloudflare Workers + Durable Objects** | 100 行內、免費 hosting、低延遲 |
| Unity 套件 | **com.unity.webrtc**（官方） | 官方維護、跨平台 |
| STUN | `stun:stun.l.google.com:19302` | Google 免費、穩定 |
| TURN | 階段二再加（Coturn / Cloudflare TURN） | MVP 階段先靠 STUN，估算 ~85% 連線可穿透 |
| QR 產生（Unity 端） | **ZXing.Net** | 開源、5 行串接 UI |
| 序列化 | **Binary 28B（既有格式）** | 與舊 receiver 一致，下游不動 |

---

## 4. 使用者流程

### 4.1 Unity 主機端（開發者／展示者）

1. 啟動 Unity 應用 → 主畫面左半顯示 **大 QR Code**，右半為「等待連線中…」狀態列。
2. 狀態列顯示：本機區網 IP（debug 用）、模式選擇下拉（自動 / 強制區網 / 強制雲端）、房號（4 碼英數）。
3. 一旦手機連入：QR 縮至右上角，主區域恢復為原本的感測器視覺化；狀態列改顯示連線資訊（模式 / 延遲 / Hz / 對端裝置）。
4. 手機斷線 → 自動恢復為等待狀態，QR 復原可再次被掃。

### 4.2 手機使用者端（網頁流程）

1. **掃 QR**：用相機 App 對準 Unity 螢幕的 QR，跳出通知「在瀏覽器開啟」。
2. **權限頁**：網頁載入後顯示「**開始傳送感測器**」大按鈕（iOS Safari 必須要有使用者手勢才會給 DeviceMotion 權限）；下方小字顯示房號與目標主機暱稱。
3. **連線中**：依序顯示「嘗試區網直連…(1.5s) → 成功 8 ms」或「區網失敗 → 切換雲端中繼 → 連上 42 ms」。若 Unity 端勾了強制模式，跳過嘗試直接走指定路徑。
4. **傳輸中主畫面**：
   - 上方：連線狀態小卡（綠燈、模式、延遲、Hz、累積封包數）
   - 中間：小型 3D 方塊跟著手機轉動，給使用者「資料有在傳」的可視回饋
   - 下方：「**中斷連線**」按鈕
   - 嘗試呼叫 Wake Lock API 保持螢幕常亮（iOS 16.4+、Android Chrome 84+ 支援）
5. **離開頁面／螢幕鎖**：連線自動斷開，Unity 端切回等待。

### 4.3 連線決策邏輯

```
[手機網頁載入]
    │
    ▼
[同時開始：A. 區網 ICE candidate 嘗試  B. 連 signaling server 拿雲端 candidate]
    │
    ▼
[1500 ms 內任一條通路 DataChannel.open?]
    │
    ├─ 是 → 使用該條，關閉另一條，UI 標示模式
    │
    └─ 否 → 繼續等到 5 s timeout，全失敗則顯示錯誤＋重試按鈕
```

> Unity 端的「強制區網／強制雲端」設定會在 QR URL 加 `mode=lan` 或 `mode=cloud` 參數，網頁端跳過另一條路徑的嘗試。

---

## 5. 各端工作項目

### 5.1 Unity 端

| # | 工作項目 | 預估工時 | 備註 |
|---|---------|---------|------|
| U1 | 安裝 `com.unity.webrtc` package（Package Manager） | 0.5h | 透過 git URL 安裝 |
| U2 | 安裝 ZXing.Net（NuGet for Unity 或 dll） | 0.5h | 用於產 QR 圖 |
| U3 | 新增 `QrCodeDisplay.cs`：抓本機 IP、組 URL、產 QR Texture、貼到 RawImage | 2h | URL 規格見 §6.1 |
| U4 | 新增 `WebRtcGyroscopeReceiver.cs`：建立 PeerConnection、開 DataChannel、收 binary frame 觸發 `SensorEvents.Raise*` | 6h | 解析邏輯複用 UDP receiver 的 byte→float 程式碼 |
| U5 | 新增 `SignalingClient.cs`：用 WebSocket 連 signaling server，處理 register/offer/answer/ice 訊息 | 3h | 訊息規格見 §6.2 |
| U6 | 新增 `ConnectionModeUI.cs`：模式切換下拉、房號顯示、連線狀態 HUD | 2h | 整合到既有 Canvas |
| U7 | 整合測試：與既有 UDP/TCP receiver 共存，事件正確觸發下游視覺化 | 2h | 不應動到 SensorEvents 介面 |
| U8 | 寫一份 Unity 端 README（操作說明 + 防火牆設定提示） | 1h | |
| **小計** | | **17h ≈ 2.5 人日** | |

### 5.2 網頁端（sensor.html）

| # | 工作項目 | 預估工時 | 備註 |
|---|---------|---------|------|
| W1 | 單檔 HTML：UI 結構（權限頁／連線中／傳輸中三個 state） | 2h | 純 HTML + Tailwind CDN |
| W2 | DeviceMotion / DeviceOrientationEvent 取資料並轉換到 Unity 座標系 | 3h | iOS quaternion 需從 alpha/beta/gamma 推導 |
| W3 | WebRTC PeerConnection 建立、SDP offer / answer、ICE 收集 | 4h | 同時開區網與雲端兩條 candidate |
| W4 | DataChannel 開啟後依固定 60-90Hz 傳送 28 bytes binary | 2h | 用 `requestAnimationFrame` + 節流 |
| W5 | 連線決策邏輯（雙路徑競速 + timeout + 模式參數） | 2h | |
| W6 | Wake Lock API 保持螢幕亮 | 1h | feature detect, graceful fallback |
| W7 | 小方塊姿態預覽（純 CSS 3D transform 即可，免 lib） | 1h | 給使用者視覺回饋 |
| W8 | 部署到 GitHub Pages / Cloudflare Pages | 0.5h | 必須 HTTPS（感測器 API 要求） |
| **小計** | | **15.5h ≈ 2 人日** | |

### 5.3 Signaling Server

| # | 工作項目 | 預估工時 | 備註 |
|---|---------|---------|------|
| S1 | 建立 Node 專案 (`ws` + `express`)，或 Cloudflare Worker 範本 | 1h | 二擇一 |
| S2 | 房間管理：`register{role, room}`、配對廣播 | 2h | 房號 4 碼，TTL 5 分鐘 |
| S3 | 訊息中繼：`offer`/`answer`/`ice` 不解析、原樣轉發給對端 | 1h | |
| S4 | 部署到 Render / Fly.io / Cloudflare（免費方案） | 1h | 取得 wss:// 網址 |
| S5 | 簡單測試（兩個瀏覽器分頁互連） | 1h | |
| **小計** | | **6h ≈ 1 人日** | |

### 5.4 總工時與時程

- **單人開發**：17 + 15.5 + 6 ≈ **38.5h ≈ 5 個工作天**（含整合測試）
- **雙人並行**（一人 Unity，一人 Web + Signaling）：**3 個工作天**

---

## 6. 通訊規格

### 6.1 QR Code URL 格式

```
https://sensor.example.com/?room=A3F9&lan=192.168.1.100:8181&host=Lab-PC&mode=auto
```

| 參數 | 說明 | 範例 |
|------|------|------|
| `room` | Signaling server 房號，4 碼英數 | `A3F9` |
| `lan` | Unity 主機區網位址（IP:Port），給區網直連用 | `192.168.1.100:8181` |
| `host` | Unity 主機暱稱，用於 UI 顯示 | `Lab-PC` |
| `mode` | `auto` / `lan` / `cloud`，預設 `auto` | `auto` |

> 注意：`lan` 參數不是真正用來連 socket（瀏覽器無法直接打 UDP），而是給 ICE 用——將其加入 host candidate hint，讓區網 ICE 嘗試直接成功，省去 STUN 來回。

### 6.2 Signaling 訊息格式（WebSocket JSON）

所有訊息為 JSON 文字格式，欄位 `type` 區分種類。

**註冊（Unity / 手機都要送）**
```json
{ "type": "register", "room": "A3F9", "role": "host" }
{ "type": "register", "room": "A3F9", "role": "guest" }
```

**配對成功通知**
```json
{ "type": "peer-joined", "role": "guest" }
```

**WebRTC 握手訊息（原樣轉發）**
```json
{ "type": "offer",  "sdp": "..." }
{ "type": "answer", "sdp": "..." }
{ "type": "ice",    "candidate": { ... } }
```

**對端斷線**
```json
{ "type": "peer-left" }
```

### 6.3 DataChannel 二進位封包（手機 → Unity）

維持與既有 UDP/TCP receiver 完全相同的 wire format：

```
偏移   長度   型別     內容
─────────────────────────────────
0      4      float32  qx  (Big-Endian)
4      4      float32  qy
8      4      float32  qz
12     4      float32  qw
16     4      float32  ax  (m/s²)
20     4      float32  ay
24     4      float32  az
─────────────────────────────────
總長度：28 bytes
```

DataChannel 設定：

```js
peer.createDataChannel('sensor', {
  ordered: false,
  maxRetransmits: 0,   // 不重傳，UDP 語意
});
```

**傳送頻率**：60-90 Hz（用 setInterval 或 RAF 節流），對應 11-16 ms 一筆。

### 6.4 座標系對齊

手機網頁取得的 `DeviceOrientationEvent` 給的是 alpha/beta/gamma 三軸歐拉角；現有 Android App 走的是四元數。網頁端必須在送出前先轉成四元數，且軸向要與 Unity 對齊（Y 軸朝上、左手坐標系）。

對齊基準參考 `GyroToRotation.cs` 中的轉換邏輯，網頁端封裝為 `eulerToUnityQuat(alpha, beta, gamma)` 工具函式。

---

## 7. 開發里程碑

### Milestone 1 — Signaling 與雲端連線打通（Day 1-2）

**交付物**：
- Signaling server 部署完成、可由公開網址連線
- 兩個瀏覽器分頁可互傳訊息 demo
- 雲端模式下手機網頁能與 Unity 建立 DataChannel 並收到一筆 28 bytes 資料

**驗收條件**：Unity Console 印出收到的 qx/qy/qz/qw/ax/ay/az 數值。

### Milestone 2 — Unity 端整合與 QR 顯示（Day 2-3）

**交付物**：
- `QrCodeDisplay` 在主畫面顯示 QR
- `WebRtcGyroscopeReceiver` 接管 DataChannel 並觸發 `SensorEvents`
- 既有的球體效果／HUD 在 WebRTC 來源下正常運作

**驗收條件**：拿手機掃 QR → 開網頁 → 按開始 → Unity 球體跟著手機轉。

### Milestone 3 — 區網直連 + 模式切換（Day 3-4）

**交付物**：
- 雙路徑競速邏輯實作完成
- Unity 端模式下拉選單可強制區網／雲端
- 區網模式延遲量測 < 15 ms

**驗收條件**：同 Wi-Fi 下連線顯示「區網直連」，斷網 Wi-Fi 改用 4G 重連顯示「雲端中繼」，延遲皆達標。

### Milestone 4 — 體驗打磨與部署（Day 4-5）

**交付物**：
- Wake Lock、姿態預覽方塊、錯誤重試 UI
- 防火牆／路由器設定文件
- README 與部署說明

**驗收條件**：交給未參與開發的同學測試，能在 30 秒內完成「掃 QR → 連上 → 看到球體跟著動」全流程。

---

## 8. 驗收標準

### 功能性

- [ ] 在 Windows / macOS Unity Editor 與 build 版皆可顯示 QR 並接受連線
- [ ] iOS Safari、Android Chrome 皆可掃 QR、授權、傳送感測器
- [ ] 區網模式下延遲 < 15 ms（量測方法：手機送出 timestamp，Unity 回送，計算 RTT/2）
- [ ] 雲端模式下延遲 < 80 ms（同網際網路下，台灣境內）
- [ ] 連線中斷後雙方 UI 正確復原，可重連
- [ ] 強制模式設定生效

### 相容性

- [ ] 與既有 UDP / TCP receiver 共存，不互相干擾
- [ ] `SensorEvents` 對外介面零修改
- [ ] 下游視覺化腳本（`AccelerometerBallEffect` 等）不需要任何修改

### 體驗

- [ ] 首次使用者 30 秒內完成連線
- [ ] 連線過程的 UI 反饋清楚（嘗試中／成功／失敗各狀態明顯區分）
- [ ] 失敗時提供明確錯誤訊息（不是只丟一個紅叉）

---

## 9. 風險與替代方案

| 風險 | 機率 | 影響 | 緩解 |
|------|------|------|------|
| iOS Safari 對 DeviceMotion 權限限制變嚴 | 中 | 中 | 在權限頁明確指引使用者；偵測失敗給 fallback 文字說明 |
| 對稱 NAT 導致雲端模式 STUN 穿透失敗 | 中 | 高 | 階段二加 TURN server（Cloudflare TURN 免費額度足夠 demo） |
| `com.unity.webrtc` 在特定平台（Linux/IL2CPP）相容問題 | 低 | 中 | MVP 先鎖定 Windows + Mono backend；提早跑一次 build 驗證 |
| 區網 ICE host candidate 被防火牆擋 | 中 | 中 | 在 Unity README 標示「首次允許防火牆例外」；提供雲端 fallback |
| 網頁端感測器頻率不穩（背景 throttle） | 中 | 低 | Wake Lock + 提示螢幕別鎖；接收端用「保留最新」策略容忍抖動 |

### 替代方案備案

- **若 WebRTC 整合過於困難** → 退而使用 WebSocket（TCP_NODELAY + binary frame + 接收端只留最新），實測區網延遲也能壓到 15-20ms，雲端模式直接走 wss://relay-server，犧牲一點延遲換時程。
- **若部署 signaling server 不便** → 用 [PeerJS Cloud](https://peerjs.com/) 公開 signaling 暫頂，不寫自己的 server。

---

## 10. 附錄

### 10.1 環境需求

- Unity 2022.3 LTS 以上
- Node 18+ （signaling server）
- 部署平台：Cloudflare Pages（網頁靜態）+ Cloudflare Workers / Render Free（signaling）
- 開發機需開放 Port：signaling server port（預設 8787）、若用本機測試 Unity STUN/ICE 通常需 UDP 高埠範圍

### 10.2 主要相依套件

**Unity (Packages/manifest.json)**：
```json
{
  "com.unity.webrtc": "3.0.0-pre.7"
}
```

**網頁端**：無需任何 npm 套件，瀏覽器原生 API。

**Signaling Server**：
```json
{
  "ws": "^8.16.0",
  "express": "^4.18.0"
}
```

### 10.3 既有專案接點

- 新增 `Assets/Scripts/WebRtcGyroscopeReceiver.cs`：仿造 `UdpGyroscopeReceiver.cs` 結構，唯一差異是資料來源。
- 新增 `Assets/Scripts/SignalingClient.cs`：純 C# WebSocket client。
- 新增 `Assets/Scripts/QrCodeDisplay.cs`：依附在主 Canvas 的 RawImage。
- **不修改** `SensorEvents.cs`、`GyroToRotation.cs`、`AccelerometerBallEffect*.cs` 等任何下游腳本。

### 10.4 後續延伸（v2 路線圖）

- 原生 App（同 QR 規格，universal/app link 攔截），給需要 200Hz+ 或螢幕鎖背景傳送的進階使用者
- 一個 QR 多裝置連線（多手機同時送資料給 Unity，做多人互動）
- TURN server 部署，覆蓋對稱 NAT 邊角情境
- Unity 端錄製 + 回放感測器資料（debug 與離線測試用）

---

**文件結束**
