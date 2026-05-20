using UnityEngine;

/// <summary>
/// 訂閱 SensorEvents.OnJoystickReceived，依搖桿輸入移動此 GameObject。
/// horizontal(-1~1) = 左右，vertical(-1~1) = 前後。
/// 需在同場景中有 WebRtcGyroscopeReceiver 負責接收 WebRTC 封包。
/// </summary>
public class WebRtcJoystickController : MonoBehaviour
{
    [Header("移動設定")]
    [Tooltip("移動速度（單位/秒）")]
    public float speed = 5f;

    [Tooltip("勾選後以物件自身朝向決定前後左右；取消則以世界座標 XZ 平面移動")]
    public bool useLocalSpace = false;

    private float _h, _v;

    void OnEnable()  => SensorEvents.OnJoystickReceived += HandleJoystick;
    void OnDisable() => SensorEvents.OnJoystickReceived -= HandleJoystick;

    private void HandleJoystick(SensorEvents.JoystickData data)
    {
        _h = data.horizontal;
        _v = data.vertical;
    }

    void Update()
    {
        if (_h == 0f && _v == 0f) return;

        var dir = new Vector3(_h, 0f, _v);
        var space = useLocalSpace ? Space.Self : Space.World;
        transform.Translate(dir * speed * Time.deltaTime, space);
    }
}
