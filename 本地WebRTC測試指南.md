# 🎯 本地WebRTC三端對接測試指南

## 📋 測試環境
- **信令服務器**: Node.js WebSocket (端口 8081)
- **發送端**: HTML5 WebRTC (Screen1020.html)
- **接收端**: Unity WebRTC (TestScreenCon.cs)

## 🚀 啟動步驟

### 1. 啟動信令服務器
```bash
node simplewebrtc-server.js
```
**預期輸出**:
```
🚀 SimpleWebRTC 信令服務器啟動成功!
🔌 WebSocket 端點: ws://localhost:8081
```

### 2. Unity 接收端配置
1. 在Unity中打開場景
2. 將 `TestScreenCon.cs` 腳本掛載到GameObject上
3. 配置參數：
   - `signalingUrl`: `ws://localhost:8081`
   - `roomId`: `default-room`
   - `targetRenderer`: 指定要顯示視頻的Renderer
4. 運行Unity場景

**預期Unity日誌**:
```
收到信令消息: {"type":"joined","room":"default-room","role":"unity-receiver"}
已加入房間: default-room
收到信令消息: {"type":"ready","room":"default-room"}
房間已就緒，等待接收 Offer
```

### 3. HTML發送端測試
1. 打開瀏覽器訪問: `TestHtml/Screen1020.html`
2. 點擊"分享螢幕"或"啟用攝影機"
3. 允許瀏覽器權限請求

**預期瀏覽器日誌**:
```
[時間] ✅ 已連接至信令伺服器
[時間] 👋 已加入房間: default-room
[時間] 📡 房間就緒，準備發送 Offer...
[時間] 🖥️ 已啟動螢幕分享
[時間] 📤 發送 Offer 給 Unity
[時間] ✅ 收到 Unity Answer
```

## 🔄 信令流程

### 完整消息流:
```
1. Web -> Server: {"type":"join","room":"default-room","role":"web-sender"}
2. Server -> Web: {"type":"joined","room":"default-room","role":"web-sender"}
3. Unity -> Server: {"type":"join","room":"default-room","role":"unity-receiver"}
4. Server -> Unity: {"type":"joined","room":"default-room","role":"unity-receiver"}
5. Server -> All: {"type":"ready","room":"default-room"}
6. Web -> Server: {"type":"offer","room":"default-room","from":"web-sender","sdp":"..."}
7. Server -> Unity: {"type":"offer","room":"default-room","from":"web-sender","sdp":"..."}
8. Unity -> Server: {"type":"answer","room":"default-room","from":"unity-receiver","sdp":"..."}
9. Server -> Web: {"type":"answer","room":"default-room","from":"unity-receiver","sdp":"..."}
10. Web -> Server: {"type":"candidate","room":"default-room","from":"web-sender","candidate":{...}}
11. Server -> Unity: {"type":"candidate","room":"default-room","from":"web-sender","candidate":{...}}
12. Unity -> Server: {"type":"candidate","room":"default-room","from":"unity-receiver","candidate":{...}}
13. Server -> Web: {"type":"candidate","room":"default-room","from":"unity-receiver","candidate":{...}}
```

## 🐛 常見問題排查

### 1. 端口被佔用
**錯誤**: `Error: listen EADDRINUSE: address already in use :::8081`
**解決**: 
```bash
netstat -ano | findstr :8081
taskkill /PID [進程ID] /F
```

### 2. Unity連接失敗
**檢查**:
- Unity腳本中的 `signalingUrl` 是否為 `ws://localhost:8081`
- 服務器是否正在運行
- Unity Console是否有錯誤日誌

### 3. Web端無法連接
**檢查**:
- 瀏覽器控制檯是否有WebSocket連接錯誤
- 服務器日誌是否顯示客戶端連接
- 防火牆是否阻止了8081端口

### 4. 視頻不顯示
**檢查**:
- Unity的 `targetRenderer` 是否正確設置
- WebRTC連接狀態是否為 `connected`
- ICE候選是否正常交換

## 📊 服務器日誌示例

**正常連接**:
```
🔌 新客戶端連接
📨 收到消息: join from unknown
✅ web-sender joined room: default-room, peers: 1
🔌 新客戶端連接
📨 收到消息: join from unknown
✅ unity-receiver joined room: default-room, peers: 2
📢 房間 default-room 已就緒，WebRTC 可以開始
📨 收到消息: offer from web-sender
📡 轉發 offer from web-sender 到房間 default-room 的其他客戶端
📨 收到消息: answer from unity-receiver
📡 轉發 answer from unity-receiver 到房間 default-room 的其他客戶端
```

## ✅ 成功標誌

1. **服務器**: 顯示兩個客戶端連接，房間就緒
2. **Web端**: 顯示"收到 Unity Answer"，視頻預覽正常
3. **Unity端**: 顯示"收到 Offer"和"收到 ICE Candidate"，視頻在Renderer上顯示
4. **網絡**: ICE連接狀態為 `connected`

## 🔧 調試技巧

1. **開啟詳細日誌**: 所有組件都有Debug.Log輸出
2. **檢查WebRTC狀態**: 在瀏覽器開發者工具中查看 `pc.connectionState`
3. **驗證信令**: 服務器會打印所有轉發的消息
4. **測試ICE**: 確保STUN服務器可訪問 (`stun:stun.l.google.com:19302`)

## 📝 注意事項

- 確保所有組件都連接到同一個房間 (`default-room`)
- Web端需要HTTPS或localhost才能訪問攝像頭/屏幕
- Unity需要正確配置WebRTC包和NativeWebSocket
- 本地測試不需要TURN服務器，但生產環境可能需要
