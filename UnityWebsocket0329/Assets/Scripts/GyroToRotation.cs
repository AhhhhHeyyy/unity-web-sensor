using UnityEngine;

public class GyroToRotation : MonoBehaviour
{
    [Header("Arrow Settings")]
    [SerializeField] private Transform arrowChild;
    [SerializeField] private Transform target;

    private Quaternion pendingRotation = Quaternion.identity;
    private bool hasData = false;

    void Start()
    {
        if (arrowChild != null && target != null)
        {
            Vector3 dir = target.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion worldRot = Quaternion.LookRotation(dir);
                arrowChild.localRotation = Quaternion.Inverse(transform.rotation) * worldRot;
            }
        }
    }

    void OnEnable()
    {
        SensorEvents.OnGyroscopeDataReceived += HandleGyroscopeData;
    }

    void OnDisable()
    {
        SensorEvents.OnGyroscopeDataReceived -= HandleGyroscopeData;
    }

    private void HandleGyroscopeData(SensorEvents.GyroscopeData data)
    {
        float qx = data.qx, qy = data.qy, qz = data.qz, qw = data.qw;
        float mag2 = qx*qx + qy*qy + qz*qz + qw*qw;
        if (mag2 < 0.5f) return;
        // Browser right-hand (X=East, Y=North, Z=Up) → Unity left-hand (X=Right, Y=Up, Z=Forward)
        pendingRotation = new Quaternion(qx, -qz, qy, qw);
        hasData = true;
    }

    void Update()
    {
        if (hasData)
            transform.localRotation = pendingRotation;
    }
}
