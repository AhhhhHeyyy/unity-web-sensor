using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.WebRTC;
using NativeWebSocket;

/// <summary>
/// WebRTC DataChannel 感測器接收器。
/// 流程：NativeWebSocket 連 Railway signaling → 收到 guest 加入 → 建立 PeerConnection + DataChannel
///       → 送出 Offer → 協商完成 → DataChannel 開啟 → 解析 28 bytes Big-Endian → 觸發 SensorEvents。
/// </summary>
public class WebRtcGyroscopeReceiver : MonoBehaviour
{
    [Header("Signaling")]
    [SerializeField] private string signalingUrl = "wss://testgyroscopehtml-production.up.railway.app";
    [SerializeField] private bool   debugLog     = false;

    [Header("狀態（唯讀）")]
    public string roomCode      = "";
    public bool   isConnected   = false;
    public float  currentHz     = 0f;
    public int    totalPackets  = 0;

    // ── WebRTC ────────────────────────────────────────────
    private RTCPeerConnection   pc;
    private RTCDataChannel      dc;
    private NativeWebSocket.WebSocket ws;

    // ── 主執行緒佇列 ──────────────────────────────────────
    private readonly Queue<byte[]> rxQueue  = new Queue<byte[]>();
    private readonly object        rxLock   = new object();

    // ── Hz 計算 ────────────────────────────────────────────
    private int   frameCount = 0;
    private float hzTimer    = 0f;

    // ── 生命週期 ──────────────────────────────────────────
    void Start()
    {
        roomCode = GenerateRoomCode();
        StartCoroutine(WebRTC.Update());
        ConnectSignaling();
    }

    void Update()
    {
        // NativeWebSocket 需要在主執行緒 dispatch
        ws?.DispatchMessageQueue();

        // 從佇列取出封包，在主執行緒處理
        lock (rxLock)
        {
            while (rxQueue.Count > 0)
                ProcessPacket(rxQueue.Dequeue());
        }

        hzTimer += Time.deltaTime;
        if (hzTimer >= 1f)
        {
            currentHz  = frameCount / hzTimer;
            frameCount = 0;
            hzTimer    = 0f;
        }
    }

    void OnDestroy()
    {
        dc?.Close();
        pc?.Close();
        ws?.Close();
    }

    // ── Signaling ─────────────────────────────────────────
    private async void ConnectSignaling()
    {
        ws = new NativeWebSocket.WebSocket(signalingUrl);

        ws.OnOpen += () =>
        {
            if (debugLog) Debug.Log("[WebRTC] Signaling 已連線");
            ws.SendText($"{{\"type\":\"join\",\"room\":\"{roomCode}\",\"role\":\"host\"}}");
        };

        ws.OnMessage += data =>
        {
            var text = System.Text.Encoding.UTF8.GetString(data);
            HandleSignalingMessage(text);
        };

        ws.OnError += err => Debug.LogWarning("[WebRTC] Signaling 錯誤: " + err);

        ws.OnClose += code =>
        {
            if (debugLog) Debug.Log("[WebRTC] Signaling 關閉: " + code);
        };

        await ws.Connect();
    }

    private void HandleSignalingMessage(string text)
    {
        if (debugLog) Debug.Log("[WebRTC] 收到: " + text);

        var msg = JsonUtility.FromJson<SignalMsg>(text);
        if (msg == null || string.IsNullOrEmpty(msg.type)) return;

        switch (msg.type)
        {
            case "ready":
                // guest 已加入，開始建立 WebRTC
                StartCoroutine(CreateOffer());
                break;

            case "answer":
                StartCoroutine(SetRemoteAnswer(msg.sdp));
                break;

            case "candidate":
                if (pc != null && msg.candidate != null)
                {
                    var c = new RTCIceCandidate(new RTCIceCandidateInit
                    {
                        candidate        = msg.candidate.candidate,
                        sdpMid           = msg.candidate.sdpMid,
                        sdpMLineIndex    = msg.candidate.sdpMLineIndex
                    });
                    pc.AddIceCandidate(c);
                }
                break;
        }
    }

    // ── WebRTC 握手（Coroutine）───────────────────────────
    private IEnumerator CreateOffer()
    {
        var config = new RTCConfiguration
        {
            iceServers = new[] { new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } } }
        };
        pc = new RTCPeerConnection(ref config);

        // ICE candidate 收集後轉發
        pc.OnIceCandidate = candidate =>
        {
            if (candidate == null) return;
            var json = $"{{\"type\":\"candidate\",\"room\":\"{roomCode}\"," +
                       $"\"candidate\":{{\"candidate\":\"{EscapeJson(candidate.Candidate)}\"," +
                       $"\"sdpMid\":\"{candidate.SdpMid}\"," +
                       $"\"sdpMLineIndex\":{candidate.SdpMLineIndex}}}}}";
            ws.SendText(json);
        };

        pc.OnConnectionStateChange = state =>
        {
            isConnected = (state == RTCPeerConnectionState.Connected);
            if (debugLog) Debug.Log("[WebRTC] 連線狀態: " + state);
        };

        // 建立 DataChannel（ordered:false = UDP 語意）
        var dcInit = new RTCDataChannelInit { ordered = false, maxRetransmits = 0 };
        dc = pc.CreateDataChannel("sensor", dcInit);
        dc.OnOpen    = () => Debug.Log("[WebRTC] DataChannel 已開啟");
        dc.OnClose   = () => Debug.Log("[WebRTC] DataChannel 已關閉");
        dc.OnMessage = bytes =>
        {
            if (bytes.Length == 28 ||
                (bytes.Length == 9 && bytes[0] == 0x02) ||
                (bytes.Length == 1 && (bytes[0] == 0x03 || bytes[0] == 0x04)))
                lock (rxLock) rxQueue.Enqueue(bytes);
        };

        // 建立並傳送 Offer
        var offerOp = pc.CreateOffer();
        yield return offerOp;
        if (offerOp.IsError) { Debug.LogError("[WebRTC] CreateOffer 失敗"); yield break; }

        var desc = offerOp.Desc;
        var setLocalOp = pc.SetLocalDescription(ref desc);
        yield return setLocalOp;

        ws.SendText($"{{\"type\":\"offer\",\"room\":\"{roomCode}\",\"sdp\":\"{EscapeJson(desc.sdp)}\"}}");
        if (debugLog) Debug.Log("[WebRTC] Offer 已送出");
    }

    private IEnumerator SetRemoteAnswer(string sdp)
    {
        var desc    = new RTCSessionDescription { type = RTCSdpType.Answer, sdp = sdp };
        var setOp   = pc.SetRemoteDescription(ref desc);
        yield return setOp;
        if (setOp.IsError) Debug.LogError("[WebRTC] SetRemoteAnswer 失敗");
        else if (debugLog) Debug.Log("[WebRTC] Answer 設定完成");
    }

    // ── 封包解析 ──────────────────────────────────────────
    private void ProcessPacket(byte[] data)
    {
        // 1-byte 抓取封包：[0x03]=按下, [0x04]=放開
        if (data.Length == 1)
        {
            if (data[0] == 0x03) SensorEvents.RaiseGrabPressed();
            else if (data[0] == 0x04) SensorEvents.RaiseGrabReleased();
            return;
        }

        // 9-byte 搖桿封包：[0x02][h_BE][v_BE]
        if (data.Length == 9 && data[0] == 0x02)
        {
            float h = ReadF32BE(data, 1);
            float v = ReadF32BE(data, 5);
            SensorEvents.RaiseJoystickReceived(new SensorEvents.JoystickData
                { horizontal = h, vertical = v });
            return;
        }

        // 28-byte 感測器封包：四元數 + 加速度
        float qx = ReadF32BE(data,  0);
        float qy = ReadF32BE(data,  4);
        float qz = ReadF32BE(data,  8);
        float qw = ReadF32BE(data, 12);
        float ax = ReadF32BE(data, 16);
        float ay = ReadF32BE(data, 20);
        float az = ReadF32BE(data, 24);

        totalPackets++;
        frameCount++;

        SensorEvents.RaiseGyroscopeDataReceived(new SensorEvents.GyroscopeData
        {
            qx = qx, qy = qy, qz = qz, qw = qw,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        });
        SensorEvents.RaiseAccelerationReceived(new Vector3(ax, ay, az));
    }

    // ── 工具 ──────────────────────────────────────────────
    private static float ReadF32BE(byte[] b, int i)
    {
        var tmp = new byte[4] { b[i], b[i+1], b[i+2], b[i+3] };
        if (BitConverter.IsLittleEndian) Array.Reverse(tmp);
        return BitConverter.ToSingle(tmp, 0);
    }

    private static string GenerateRoomCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var rng  = new System.Random();
        var code = new char[4];
        for (int i = 0; i < 4; i++) code[i] = chars[rng.Next(chars.Length)];
        return new string(code);
    }

    private static string EscapeJson(string s) =>
        s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") ?? "";

    // ── 訊息結構（JsonUtility 用）────────────────────────
    [Serializable] private class SignalMsg
    {
        public string type;
        public string sdp;
        public IceMsg candidate;
    }
    [Serializable] private class IceMsg
    {
        public string candidate;
        public string sdpMid;
        public int    sdpMLineIndex;
    }
}
