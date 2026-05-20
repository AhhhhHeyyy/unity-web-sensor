# 🎯 WebRTC 三端對接測試指南

## 📋 測試環境
- **信令服務器**: Node.js WebSocket (端口 8081) ✅ 已運行
- **發送端**: HTML5 WebRTC (Screen1020.html)
- **接收端**: Unity WebRTC (TestScreenCon.cs)

## 🚀 測試步驟

### 步驟 1: 確認服務器運行狀態
```bash
# 檢查端口 8081 是否被佔用
netstat -ano | findstr :8081
```
**預期輸出**: 應該顯示端口 8081 正在監聽

### 步驟 2: Unity 接收端配置
1. **打開Unity項目**
2. **創建測試場景**:
   - 創建空GameObject，命名為 "WebRTCReceiver"
   - 添加 `TestScreenCon` 腳本
3. **配置參數**:
   - `signalingUrl`: `ws://localhost:8081`
   - `roomId`: `default-room`
   - `targetRenderer`: 拖拽一個Renderer組件（如Cube的Renderer）
4. **運行場景**

**預期Unity Console日誌**:
```
✅ WebSocket連接成功
📩 收到信令消息: {"type":"joined","room":"default-room","role":"unity-receiver"}
✅ 已加入房間: default-room
📩 收到信令消息: {"type":"ready","room":"default-room"}
📡 房間已就緒，等待 Offer...
```

### 步驟 3: Web發送端測試
1. **打開瀏覽器**，訪問: `TestHtml/Screen1020.html`
2. **觀察連接日誌**:
   - 應該顯示 "✅ 已連接至信令伺服器"
   - 應該顯示 "👋 已加入房間: default-room"
3. **點擊"分享螢幕"按鈕**
4. **允許瀏覽器權限請求**

**預期瀏覽器日誌**:
```
[時間] ✅ 已連接至信令伺服器
[時間] 👋 已加入房間: default-room
[時間] 📡 房間就緒，準備發送 Offer...
[時間] 🖥️ 已啟動螢幕分享
[時間] 📤 發送 Offer 給 Unity
[時間] ✅ 收到 Unity Answer
```

### 步驟 4: 驗證視頻傳輸
1. **Unity端**: 檢查Renderer是否顯示視頻內容
2. **服務器端**: 觀察終端日誌，應該顯示消息轉發
3. **Web端**: 檢查視頻預覽是否正常

## 📊 預期消息流程

### 完整信令交換:
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

## 🔍 服務器日誌示例

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

## 🐛 常見問題排查

### 1. Unity連接失敗
**症狀**: Unity Console顯示連接錯誤
**檢查**:
- `signalingUrl` 是否為 `ws://localhost:8081`
- 服務器是否正在運行
- 防火牆是否阻止連接

### 2. Web端無法連接
**症狀**: 瀏覽器顯示連接失敗
**檢查**:
- 瀏覽器控制檯是否有錯誤
- 服務器日誌是否顯示Web端連接
- 網絡連接是否正常

### 3. 視頻不顯示
**症狀**: Unity中Renderer沒有顯示視頻
**檢查**:
- `targetRenderer` 是否正確設置
- Unity Console是否顯示 "🎥 收到遠端視頻流"
- WebRTC連接狀態是否為 `connected`

### 4. 信令消息錯誤
**症狀**: 服務器顯示 "數據格式錯誤"
**檢查**:
- 消息是否為有效JSON格式
- 消息是否包含必要的 `type` 字段
- 房間ID是否匹配

## ✅ 成功標誌

1. **服務器**: 顯示兩個客戶端連接，房間就緒
2. **Web端**: 顯示"收到 Unity Answer"，視頻預覽正常
3. **Unity端**: 顯示"收到 Offer"和"收到 ICE Candidate"，視頻在Renderer上顯示
4. **網絡**: ICE連接狀態為 `connected`

## 🔧 調試技巧

1. **開啟詳細日誌**: 所有組件都有Debug.Log輸出
2. **檢查WebRTC狀態**: 在瀏覽器開發者工具中查看連接狀態
3. **驗證信令**: 服務器會打印所有轉發的消息
4. **測試ICE**: 確保STUN服務器可訪問 (`stun:stun.l.google.com:19302`)

## 📝 測試檢查清單

- [ ] 服務器運行在端口 8081
- [ ] Unity場景運行，TestScreenCon腳本已添加
- [ ] Unity Console顯示WebSocket連接成功
- [ ] 瀏覽器可以訪問 Screen1020.html
- [ ] Web端顯示連接成功和房間加入
- [ ] 點擊"分享螢幕"後Unity收到Offer
- [ ] Unity自動回覆Answer
- [ ] ICE候選正常交換
- [ ] Unity中顯示接收到的視頻流

## 🎉 測試完成

如果所有步驟都成功，你應該能看到：
- Web端顯示屏幕共享預覽
- Unity端在Renderer上顯示相同的視頻內容
- 服務器日誌顯示完整的信令交換過程

恭喜！你的WebRTC三端對接系統已經成功運行！
