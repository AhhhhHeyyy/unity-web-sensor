using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;

/// <summary>
/// 從 WebRtcGyroscopeReceiver 讀取房間碼，產生 QR Code 圖片並顯示在 RawImage 上。
/// 使用 api.qrserver.com 動態產生 QR 圖片（需要網路）。
/// </summary>
[RequireComponent(typeof(RawImage))]
public class QrCodeDisplay : MonoBehaviour
{
    [Header("設定")]
    [SerializeField] private string basePageUrl = "https://testgyroscopehtml-production.up.railway.app/sensor.html";
    [SerializeField] private int    qrSize      = 300;

    [Header("參考")]
    [SerializeField] private WebRtcGyroscopeReceiver receiver;
    [SerializeField] private Text   roomCodeText;   // 可選：顯示房間碼文字

    private RawImage rawImage;
    private string   lastRoomCode = "";

    void Awake()
    {
        rawImage = GetComponent<RawImage>();
        if (receiver == null)
            receiver = FindObjectOfType<WebRtcGyroscopeReceiver>();
    }

    void Update()
    {
        if (receiver == null) return;
        if (receiver.roomCode != lastRoomCode)
        {
            lastRoomCode = receiver.roomCode;
            StartCoroutine(FetchQr(lastRoomCode));
            if (roomCodeText != null)
                roomCodeText.text = "房間碼：" + lastRoomCode;
        }
    }

    private IEnumerator FetchQr(string roomCode)
    {
        string url    = $"{basePageUrl}?room={roomCode}";
        string apiUrl = $"https://api.qrserver.com/v1/create-qr-code/?size={qrSize}x{qrSize}&data={UnityWebRequest.EscapeURL(url)}";

        using var req = UnityWebRequestTexture.GetTexture(apiUrl);
        yield return req.SendWebRequest();

        if (req.result == UnityWebRequest.Result.Success)
        {
            rawImage.texture = DownloadHandlerTexture.GetContent(req);
            rawImage.color   = Color.white;
        }
        else
        {
            Debug.LogWarning("[QrCode] 下載失敗: " + req.error);
        }
    }
}
