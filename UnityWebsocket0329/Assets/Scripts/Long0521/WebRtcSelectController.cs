using UnityEngine;

/// <summary>
/// 網頁按鈕按下(0x03) → 從鏡頭中心 Raycast 抓取物件。
/// 按鈕放開(0x04) → 放開物件。
/// 抓住期間物件跟著鏡頭前方 holdDistance 處移動。
/// 掛在場景任意常駐 GameObject（建議與 WebRtcGyroscopeReceiver 同一個）。
/// </summary>
public class WebRtcSelectController : MonoBehaviour
{
    [Header("抓取設定")]
    public float dragForce   = 20f;
    public float dragDamping = 5f;
    public LayerMask draggableLayer = ~0;

    private Rigidbody targetBody;
    private float     holdDistance;

    void OnEnable()
    {
        SensorEvents.OnGrabPressed  += TryGrab;
        SensorEvents.OnGrabReleased += Release;
    }

    void OnDisable()
    {
        SensorEvents.OnGrabPressed  -= TryGrab;
        SensorEvents.OnGrabReleased -= Release;
    }

    void FixedUpdate()
    {
        if (targetBody == null) return;

        Vector3 target = Camera.main.transform.position
                       + Camera.main.transform.forward * holdDistance;
        targetBody.linearVelocity = (target - targetBody.position) * dragForce;
    }

    private void TryGrab()
    {
        Ray ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, draggableLayer)
            && hit.rigidbody != null)
        {
            targetBody                = hit.rigidbody;
            holdDistance              = hit.distance;
            targetBody.useGravity     = false;
            targetBody.linearDamping  = dragDamping;
            targetBody.angularDamping = dragDamping;
        }
    }

    private void Release()
    {
        if (targetBody == null) return;

        targetBody.useGravity     = true;
        targetBody.linearDamping  = 0.05f;
        targetBody.angularDamping = 0.05f;
        targetBody = null;
    }
}
