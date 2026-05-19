using UnityEngine;
// update: 2026-05-16
/// <summary>
/// 重現「加速度儀校準 App」紅球效果（UDP 四元數模式）：
/// 手機往哪個方向推，物件就往那個方向移動；
/// 停止施力時會自動彈回；靜止時停在重力傾斜角所對應的位置。
///
/// 座標系映射（Android GAME_ROTATION_VECTOR → Unity）：
///   Android 世界系 Z 朝上；重力 = (0,0,-g)
///   gDevice = Inverse(q) * (0,0,-g) → 本體座標系重力
///   Unity X（直立） = gDevice.x     （左右傾斜，重力投影；持續量）
///   Unity Z（直立） = worldAcc.y    （前後推力，線性加速度；瞬時量）
///   Unity X（平放） = gDevice.x     （左右傾斜，重力投影；持續量）
///   Unity Y（平放） = gDevice.y     （前後傾斜，重力投影；持續量）← 傾斜→持續；對稱 X 軸
///   Unity Z（平放） = worldAcc.y    （前後推力，線性加速度；瞬時量）
///
/// 直立與平放模式參數完全分離，各自獨立微調。
/// 自動校正嚮導可一鍵偵測 axisFlip、axisDeadzone、sensitivity 與軸交換（swapXZ）。
/// </summary>
public partial class AccelerometerBallEffect : MonoBehaviour
{
    [System.Serializable]
    private struct ModeSettings
    {
        [Tooltip("各軸感應靈敏度（sensor m/s² → targetOffset 單位）。由嚮導自動計算：sensitivity.x = maxOffsetPerAxis.x / usableRange")]
        public Vector3 sensitivity;

        [Tooltip("平滑速度，越大越即時、越小越滑順")]
        [Range(1f, 30f)] public float smoothSpeed;

        [Tooltip("輸入濾波時間常數（秒），越小越即時、越大越平滑，建議 0.03 ~ 0.1")]
        [Range(0.01f, 0.5f)] public float inputFilterTime;

        [Tooltip("哪些軸要受加速度影響（1=開，0=關）")]
        public Vector3 movementAxesMask;

        [Tooltip("各軸方向翻轉（1=正常, -1=反轉）；作用在死區之前的訊號管線中段。\n" +
                 "※ 若只是要修正輸出方向相反，建議改用下方 outputFlip（純前端，不影響積分與死區）")]
        public Vector3 axisFlip;

        [Tooltip("輸出方向翻轉（純前端，1=正常, -1=反轉）：\n" +
                 "在 EMA、積分、死區、回彈全部計算完畢後才翻轉輸出，完全不進入任何內部訊號。\n" +
                 "Y 或 Z 方向感覺反了 → 將對應軸設為 -1，不會造成卡頓或汙染積分邏輯。")]
        public Vector3 outputFlip;

        [Tooltip("各軸死區（m/s²）：低於此值的輸入歸零，消除靜止漂移。超過死區後連續輸出")]
        public Vector3 axisDeadzone;

        [Tooltip("各軸移動幅度縮放（X=左右, Y=上下, Z=前後），值越大移動範圍越大")]
        public Vector3 axisScale;

        [Tooltip("各軸允許的最大偏移距離（米），各軸獨立不互相壓縮")]
        public Vector3 maxOffsetPerAxis;

        [Tooltip("輸出端最小有效位移（Unity 單位）：scaledOffset 低於此值歸零，消除微小抖動。由嚮導自動計算")]
        public Vector3 minOutputStep;

        [Tooltip("各軸靜止（輸入落入死區）時的歸中拉力比例（0=保持位置不彈回；1=正常速度彈回中心）。\n" +
                 "X 建議保持 1；Y/Z 設 0.01~0.05 可防止手機停止後球自動彈回，同時透過極慢速漂移避免感測器長期偏移累積。")]
        public Vector3 idleReturnStrength;

        [Tooltip("交換 X 與 Z 軸的輸入來源（嚮導偵測到手機旋轉 90° 時自動設定）")]
        public bool swapXZ;
    }

    [Header("中心點")]
    [Tooltip("移動範圍的錨點，若不指定則以 Start 時的位置為中心")]
    [SerializeField] private Transform centerPoint;

    [Header("自動校正")]
    [Tooltip("收到第一筆感測器資料後，等待幾秒再自動校正（讓濾波先穩定）；0 = 立即校正")]
    [SerializeField] [Range(0f, 5f)] private float autoCalibrationDelay = 1.5f;
    [Tooltip("（唯讀）是否已完成初始校正；未完成前物件鎖在原點")]
    [SerializeField] private bool hasCalibrated = false;

    [Header("平放/直立切換")]
    [Tooltip("flatness 閾值（0~1），超過此值視為平放；建議 0.6~0.8")]
    [SerializeField] [Range(0f, 1f)] private float flatnessThreshold = 0.7f;
    [Tooltip("遲滯帶寬（0~0.4）：退出平放所需 flatnessRatio = flatnessThreshold − flatnessHysteresis。\n" +
             "讓進入/離開平放使用不同門檻，防止斜拿時反覆切換。建議 0.1~0.2")]
    [SerializeField] [Range(0f, 0.4f)] private float flatnessHysteresis = 0.15f;
    [Tooltip("（唯讀）目前是否判定為平放模式")]
    [SerializeField] private bool phoneIsFlat = false;
    [Tooltip("模式防抖時間（秒）：新狀態需穩定超過此值才真正切換，防止快速揮動造成頻繁切換；建議 0.2~0.4")]
    [SerializeField] [Range(0f, 1f)] private float modeSwitchDebounceTime = 0.3f;
    [Tooltip("模式切換時位置跳動補償的淡出時間（秒），預設 0.5 秒")]
    [SerializeField] [Range(0.1f, 2f)] private float modeSwitchTransitionDuration = 0.5f;

    [Header("直立模式設定")]
    [SerializeField]
    private ModeSettings uprightSettings = new()
    {
        sensitivity        = new Vector3(0.3f, 0.3f, 0.3f),
        smoothSpeed        = 10f,
        inputFilterTime    = 0.05f,
        movementAxesMask   = new Vector3(1, 1, 1),
        axisFlip           = new Vector3(1f, 1f, -1f),
        outputFlip         = new Vector3(1f, 1f, 1f),
        axisDeadzone       = new Vector3(0.3f, 0.3f, 0.3f),
        axisScale          = new Vector3(1f, 1f, 1f),
        maxOffsetPerAxis   = new Vector3(3f, 3f, 3f),
        idleReturnStrength = new Vector3(1f, 0.02f, 0.15f)
    };

    [Header("平放模式設定")]
    [Tooltip("平放 X 軸（左右）linX 混合比例。gDevice.x 已是穩定傾斜投影，加入 linX 只帶入手部晃動噪聲。建議保持 0。")]
    [SerializeField] [Range(0f, 5f)] private float flatLinearBlendX = 0f;
    [HideInInspector]
    [SerializeField] [Range(0f, 5f)] private float flatLinearBlendZ = 0f; // 已棄用：平放 Z 改回 worldAcc.y，不再混合 gDevice.y
    [Tooltip("平放 Z 軸線性加速截斷（m/s²）：防止快速甩動時尖峰暴衝。建議 6~12。")]
    [SerializeField] [Range(2f, 20f)] private float flatLinZClamp = 8f;

    [Header("平放 Z 軸積分（抗反彈）")]
    [Tooltip("啟用後：平放 Z 改用「速度積分（有阻尼）」模式；停止後速度指數衰減歸零，不累積位置、不反彈\n" +
             "嚮導期間自動暫停積分，以確保 sensitivity.z 校正正確")]
    [SerializeField] private bool flatZUseIntegration = true;
    [Tooltip("卡爾曼過程噪聲 Q：越大 = 濾波器更信任感測器原始值、追蹤更快但輸出較嘈雜")]
    [SerializeField] [Range(0.001f, 2f)] private float flatZKalmanQ = 0.1f;
    [Tooltip("卡爾曼量測噪聲 R：越大 = 輸出更平滑但反應更慢；建議 0.3~1.0")]
    [SerializeField] [Range(0.01f, 5f)]  private float flatZKalmanR = 0.5f;
    [Tooltip("積分力度（加速度 → 速度的增益）；越大 = 推力更靈敏")]
    [SerializeField] [Range(0.05f, 5f)]  private float flatZForceGain = 1f;
    [Tooltip("速度阻尼（每秒指數衰減率）；越大 = 停手後球越快停住；建議 flatZOutputScale = flatZFriction / flatZForceGain 保持等比")]
    [SerializeField] [Range(0.5f, 30f)]  private float flatZFriction = 5f;
    [Tooltip("速度輸出縮放；建議設為 flatZFriction / flatZForceGain，使峰值速度信號與原始加速度等量，嚮導校正才正確")]
    [SerializeField] [Range(1f, 50f)]    private float flatZOutputScale = 5f;

    [Header("Z 軸衝量錨點（前後持續定位）")]
    [Tooltip("啟用後：超過閾值的推力持續推動錨點，放手後錨點鎖定；停止施力球不再彈回。與傾斜軸（X）組合使用")]
    [SerializeField] private bool zAnchorEnabled = false;
    [Tooltip("觸發錨點移動的快速濾波閾值（m/s²）；低於此的靜止雜訊被過濾，超過此才推動錨點")]
    [SerializeField] [Range(0.2f, 8f)] private float zAnchorThreshold = 1.5f;
    [Tooltip("錨點推進速率（Unity 米/秒 per 超出閾值 m/s²）；推動 1 秒移動量 = (加速度 − 閾值) × 此值；建議 1~4")]
    [SerializeField] [Range(0.1f, 10f)] private float zAnchorSensitivity = 2f;
    [Tooltip("靜止時錨點每秒歸中比例；0 = 完全不歸中；0.02 ≈ 約 50 秒緩慢歸中")]
    [SerializeField] [Range(0f, 0.5f)] private float zAnchorIdleReturn = 0.02f;
    [Tooltip("衝量偵測快速 EMA 時間常數（秒）；越小越靈敏但雜訊越多；建議 0.02~0.05")]
    [SerializeField] [Range(0.01f, 0.2f)] private float zAnchorFilterTime = 0.03f;
    [Tooltip("推力結束後封鎖反向輸入的時間（秒）；防止手臂回彈把錨點拉回來；建議 0.15~0.35")]
    [SerializeField] [Range(0.05f, 0.8f)] private float zAnchorLockDuration = 0.25f;
    [HideInInspector] [Tooltip("（唯讀）目前 Z 軸錨點偏移（Unity 米）")]
    [SerializeField] private float debugZAnchorOffset    = 0f;
    [HideInInspector] [Tooltip("（唯讀）衝量偵測快速濾波值（m/s²）")]
    [SerializeField] private float debugZImpulseFiltered = 0f;
    [HideInInspector] [Tooltip("（唯讀）方向鎖定冷卻剩餘秒數；> 0 = 反方向被封鎖")]
    [SerializeField] private float debugZAnchorCooldown  = 0f;

    [Header("Z 軸姿態輔助（傾斜補強）")]
    [Tooltip("啟用後：在推力 / 錨點算出的 targetOffset.z 之上疊加傾斜分量，讓推出去的位置更穩定。\n" +
             "直立模式用 gDevice.z（俯仰傾斜）；平放模式用 gDevice.y（前後傾斜，最乾淨）。\n" +
             "Tare 校正自動扣除靜止基準，只有偏離校正角度時才有貢獻。")]
    [SerializeField] private bool zPitchAssistEnabled = false;
    [Tooltip("傾斜訊號混入比例（m/s² per m/s²）。建議從 0.3 開始試；\n" +
             "直立模式因 gDevice.z 同時驅動 Y 軸，建議 ≤ 0.4 以降低 Y/Z 耦合")]
    [SerializeField] [Range(0f, 1.5f)] private float zPitchAssistScale = 0.3f;

    [SerializeField]
    private ModeSettings flatSettings = new()
    {
        sensitivity        = new Vector3(0.3f, 0.3f, 0.3f),
        smoothSpeed        = 7f,
        inputFilterTime    = 0.06f,
        movementAxesMask   = new Vector3(1, 0, 1),
        axisFlip           = new Vector3(1f, 1f, -1f),
        outputFlip         = new Vector3(1f, 1f, 1f),
        axisDeadzone       = new Vector3(0.2f, 0.2f, 0.2f),
        axisScale          = new Vector3(1f, 1f, 1f),
        maxOffsetPerAxis   = new Vector3(3f, 3f, 3f),
        idleReturnStrength = new Vector3(0.02f, 1f, 0.3f) // X 重力持續量→緩慢歸中；Z 線性瞬時量→適度緩慢歸中
    };

    [HideInInspector] [Tooltip("裝置座標系重力向量 gDevice = Inverse(q)*(0,0,-g)；直立時 z≈0，平放時 z≈±9.81")]
    [SerializeField] private Vector3 debugGDevice         = Vector3.zero;
    [HideInInspector] [Tooltip("|gDevice.z|/g；超過 Flatness Threshold 判定為平放")]
    [SerializeField] private float   debugFlatnessRatio   = 0f;
    [HideInInspector] [Tooltip("四元數（Android GAME_ROTATION_VECTOR）")]
    [SerializeField] private float   debugQx = 0f;
    [HideInInspector] [SerializeField] private float   debugQy = 0f;
    [HideInInspector] [SerializeField] private float   debugQz = 0f;
    [HideInInspector] [SerializeField] private float   debugQw = 1f;
    [HideInInspector] [Tooltip("HandleAcceleration 收到的線性加速度（已去重力）；平放模式的位移來源")]
    [SerializeField] private Vector3 debugLinearAccInput  = Vector3.zero;
    [HideInInspector] [SerializeField] private Vector3 debugRawAcceleration        = Vector3.zero;
    [HideInInspector] [SerializeField] private Vector3 debugFilteredAcceleration   = Vector3.zero;
    [HideInInspector] [SerializeField] private Vector3 debugCalibratedAcceleration = Vector3.zero;
    [HideInInspector] [SerializeField] private Vector3 debugDebiasedAcceleration   = Vector3.zero;
    [HideInInspector] [SerializeField] private Vector3 debugTargetOffset           = Vector3.zero;
    [HideInInspector] [SerializeField] private Vector3 debugCurrentOffset          = Vector3.zero;
    [HideInInspector] [SerializeField] private float   debugTransitionProgress     = 1f;
    [HideInInspector] [SerializeField] private Vector3 debugActualPosition         = Vector3.zero;
    [HideInInspector] [Tooltip("scaledOffset 套用 minOutputStep 前（原始縮放後位移）")]
    [SerializeField] private Vector3 debugScaledBeforeFilter     = Vector3.zero;
    [HideInInspector] [Tooltip("scaledOffset 套用 minOutputStep 後（實際驅動位置的值）")]
    [SerializeField] private Vector3 debugScaledAfterFilter      = Vector3.zero;
    [HideInInspector] [Tooltip("目前模式的 minOutputStep（輸出端死區閾值）")]
    [SerializeField] private Vector3 debugMinOutputStep          = Vector3.zero;

    [Header("平放 XZ 管線（調試：來回移動時觀察）")]
    [Tooltip("Console 輸出間隔（秒）；0 = 關閉定時輸出")]
    [SerializeField] [Range(0f, 5f)] private float pipeLogInterval = 0.5f;
    [HideInInspector] [Tooltip("rawAcceleration.x/z — 輸入濾波前 (gDevice.x / gDevice.y)")]
    [SerializeField] private Vector2 dbPipe_raw        = Vector2.zero;
    [HideInInspector] [Tooltip("filteredAcceleration.x/z — 感測器 EMA 濾波後")]
    [SerializeField] private Vector2 dbPipe_filtered   = Vector2.zero;
    [HideInInspector] [Tooltip("calibratedAcceleration.x/z — Tare 參考點（重力補償會緩慢移動此值！）")]
    [SerializeField] private Vector2 dbPipe_tare       = Vector2.zero;
    [HideInInspector] [Tooltip("debiased x/z（swap 前）= filtered − tare")]
    [SerializeField] private Vector2 dbPipe_preSwap    = Vector2.zero;
    [HideInInspector] [Tooltip("debiased x/z（swap 後）= 實際驅動 Unity XZ 的訊號")]
    [SerializeField] private Vector2 dbPipe_postSwap   = Vector2.zero;
    [HideInInspector] [Tooltip("flip + 死區後 x/z（0 = 在死區內未驅動球）")]
    [SerializeField] private Vector2 dbPipe_afterDz    = Vector2.zero;
    [HideInInspector] [Tooltip("targetOffset x/z — 球的目標位置（EMA 前）")]
    [SerializeField] private Vector2 dbPipe_target     = Vector2.zero;
    [HideInInspector] [Tooltip("EMA alpha — 每幀追蹤速率（smoothSpeed=7 @ 60fps ≈ 0.10）")]
    [SerializeField] private float   dbPipe_emaAlpha   = 0f;
    [HideInInspector] [Tooltip("重力補償活躍度：1=Tare 正在向 filteredAcc 靠（會把球拉回中心）；0=已暫停")]
    [SerializeField] private float   dbPipe_idleRatio  = 0f;

    [HideInInspector] [Tooltip("最後一次切換方向")]
    [SerializeField] private string debugLastSwitchDir       = "—";
    [HideInInspector] [Tooltip("距上次切換經過秒數（-1 = 尚未切換）")]
    [SerializeField] private float  debugTimeSinceLastSwitch = -1f;
    [HideInInspector] [Tooltip("切換瞬間起點與終點的距離（m）")]
    [SerializeField] private float  debugSwitchStartDist     = 0f;
    [HideInInspector] [Tooltip("過渡期間最大單幀位置跳動（m）；越小越平滑")]
    [SerializeField] private float  debugMaxFrameJump        = 0f;
    [HideInInspector] [Tooltip("過渡剩餘比例（1=剛切換，0=完成）")]
    [SerializeField] private float  debugTransitionRemaining = 0f;

    [Header("平放模式重力中心補償")]
    [Tooltip("啟用後：手機平放靜止時，自動緩慢修正 XZ 漂移（四元數時間累積誤差），讓球回中心")]
    [SerializeField] private bool enableFlatGravityCorrection = false;
    [Tooltip("XZ 漂移補償時間常數（秒）。越大越保守，越小越積極。建議 20~60；太小會吃掉慢速傾斜")]
    [SerializeField] [Range(5f, 120f)] private float flatGravityCorrectionTime = 30f;

    [Header("輸出平滑（抗抖動）")]
    [Tooltip("位置輸出的低通濾波時間常數（秒）。越大越平滑但反應越慢；0 = 關閉。建議 0.03 ~ 0.08")]
    [SerializeField] [Range(0f, 0.3f)] private float positionFilterTime = 0.05f;
    [Tooltip("模式切換後的阻尼過渡時間（秒）。SmoothDamp 以此為起始 smoothTime，再線性收斂回 positionFilterTime；越大越絲滑，建議 0.2 ~ 0.4")]
    [SerializeField] [Range(0.05f, 1f)] private float modeSwitchSmoothTime = 0.3f;

    [Header("軸示意線（Scene / Game 視窗）")]
    [Tooltip("是否顯示 XYZ 軸示意線（紅=X，綠=Y，藍=Z）")]
    [SerializeField] private bool showAxisGizmos = true;
    [Tooltip("軸示意線長度（米）")]
    [SerializeField] [Range(0.1f, 5f)] private float axisGizmoLength = 1f;

    [HideInInspector] [Tooltip("X 軸加速度：左右傾斜（負=左, 正=右）")]
    [SerializeField] private float levelAxisX = 0f;
    [HideInInspector] [Tooltip("Y 軸加速度：前後傾斜（負=前, 正=後）")]
    [SerializeField] private float levelAxisY = 0f;
    [HideInInspector] [Tooltip("Roll 角（繞 Z 軸，左右傾斜角度）")]
    [SerializeField] private float rollDeg  = 0f;
    [HideInInspector] [Tooltip("Pitch 角（繞 X 軸，前後傾斜角度）")]
    [SerializeField] private float pitchDeg = 0f;

    private Vector3    centerLocalPosition;
    private Vector3    targetOffset        = Vector3.zero;
    private Vector3    currentOffset       = Vector3.zero;
    private Vector3    currentVelocity     = Vector3.zero;
    private Vector3    filteredAcceleration = Vector3.zero;
    private Vector3    calibratedAcceleration = Vector3.zero; // Tare 基準：校正時記錄，下幀 debiased = filtered - calibrated ≈ 0
    private Vector3    savedTareFlat          = Vector3.zero; // 平放模式校正基準（模式切換時恢復，不被覆寫）
    private Vector3    savedTareUpright       = Vector3.zero; // 直立模式校正基準
    private Vector3    rawAcceleration     = Vector3.zero;
    private bool       hasOrientationData  = false;
    private Quaternion currentOrientation  = Quaternion.identity;
    private bool       rawPhoneIsFlat               = false; // 感測器即時值，未經防抖
    private float      flatnessHoldTimer            = 0f;   // 新狀態持續計時
    private bool       prevPhoneIsFlat              = false;
    private float      modeSwitchTransitionProgress = 1f;
    private Vector3    modeSwitchTransitionOffset   = Vector3.zero; // 切換瞬間跳動量，淡出至 0
    private Vector3    prevFramePosition            = Vector3.zero;
    private float      switchTimer               = -1f;
    private bool       switchLoggedComplete      = false;
    private float      _effectiveTransitionDuration = 0.5f; // 保留供 debug log 使用
    private Vector3    _posVelocity          = Vector3.zero; // SmoothDamp 輸出速度（物理阻尼）
    private float      _switchSmoothRemaining = 0f;          // 模式切換阻尼剩餘時間（秒）
    private float      firstDataTime             = -1f; // 第一筆感測器資料到達的時間戳
    private Vector3    smoothedPosition          = Vector3.zero; // 輸出位置 EMA（抗抖動）
    private float      calibrationMsgTimer       = 0f;
    private Vector3    calibrationMsgPosition    = Vector3.zero;

    // 平放模式混合輸入暫存
    private float _gravX = 0f; // gDevice.x 傾斜分量（平放 X 軸用）
    private float _linX  = 0f; // worldAcc.x 線性分量（平放 X 軸可選混入）

    // 平放管線調試暫存（不序列化，僅在 Update 內部傳遞到 Inspector 欄位）
    private float   _dbIdleRatio     = 0f;
    private Vector3 _dbPreSwap       = Vector3.zero;
    private Vector3 _dbPostSwap      = Vector3.zero;
    private Vector3 _dbAfterDz       = Vector3.zero;
    private float   _dbEmaAlpha      = 0f;

    // 平放模式統計（供定時 Console 輸出 & OnDisable 總結用）
    private float   _pipeLogTimer    = 0f;
    private float   _statFlatTime    = 0f;
    private Vector2 _statTareEntry   = Vector2.zero;  // 進入平放時的 Tare 起點
    private bool    _statEntrySet    = false;
    private float   _statMaxIdle     = 0f;
    private Vector2 _statMaxTarget   = Vector2.zero;  // 歷史最大絕對 targetOffset
    private float   _statMaxJitter   = 0f;
    private float   _statJitterSum   = 0f;
    private int     _statJitterCount = 0;
    private Vector3 _statPrevPos     = Vector3.zero;

    // 平放 Z 軸速度積分 & 卡爾曼濾波狀態（Update 幀間持續）
    private float _flatZVelInt  = 0f;  // 速度積分值（含阻尼，無位置累積）
    private float _kalmanZState = 0f;
    private float _kalmanZCov   = 1f;
    private float _flatZIntegBlockTimer = 0f; // 切入平放後暫時封鎖積分，防止翻轉加速度積分成持續速度
    private float _flatGDeviceYSmooth   = 0f; // gDevice.y 慢速 EMA（0.25s），用於偵測傾斜速率

    private float _zAnchorOffset    = 0f; // Z 軸持續錨點（Unity 米）
    private float _zImpulseFiltered = 0f; // 衝量偵測快速 EMA
    private float _zAnchorCooldown   = 0f; // 方向鎖定冷卻剩餘時間
    private int   _zAnchorLockedSign = 0;  // 0=未鎖, ±1=鎖定方向
    private bool  _zInPush           = false; // 目前是否在推力期
    private float _gDevicePitchZ     = 0f; // 直立模式：gDevice.z（俯仰傾斜），Z 姿態輔助用
    private float _gDevicePitchY     = 0f; // 平放模式：gDevice.y（前後傾斜），Z 姿態輔助用
    private float _tiltTareUpright   = 0f; // 直立模式姿態輔助校正基準
    private float _tiltTareFlat      = 0f; // 平放模式姿態輔助校正基準

    private void Start()
    {
        centerLocalPosition = centerPoint != null
            ? (transform.parent != null
                ? transform.parent.InverseTransformPoint(centerPoint.position)
                : centerPoint.position)
            : transform.localPosition;

        SensorEvents.OnGyroscopeDataReceived += HandleGyroscopeData;
        SensorEvents.OnAccelerationReceived  += HandleAcceleration;
    }

    private void OnDestroy()
    {
        SensorEvents.OnGyroscopeDataReceived -= HandleGyroscopeData;
        SensorEvents.OnAccelerationReceived  -= HandleAcceleration;
    }

    private void OnDisable()
    {
        if (_statFlatTime < 0.5f) return;
        float avgJitter  = _statJitterCount > 0 ? _statJitterSum / _statJitterCount : 0f;
        Vector2 tareDrift = dbPipe_tare - _statTareEntry;
        Debug.Log(
            $"[平放模式總結]\n" +
            $"  平放累計時間 : {_statFlatTime:F1}s\n" +
            $"  Tare 起點 XZ : ({_statTareEntry.x:+0.000;-0.000}, {_statTareEntry.y:+0.000;-0.000})\n" +
            $"  Tare 終點 XZ : ({dbPipe_tare.x:+0.000;-0.000}, {dbPipe_tare.y:+0.000;-0.000})  " +
            $"漂移量 = {tareDrift.magnitude:F4} m/s²\n" +
            $"  最大 targetOffset XZ : ({_statMaxTarget.x:F3}, {_statMaxTarget.y:F3})\n" +
            $"  最大 idleRatio : {_statMaxIdle:F3}  " +
            $"{(_statMaxIdle > 0.05f ? "⚠ 重力補償曾活躍 → Tare 被修改，可能是被拉回中心的原因" : "✓ 重力補償未明顯活躍")}\n" +
            $"  位置抖動 最大/平均 : {_statMaxJitter:F4}m / {avgJitter:F5}m");
    }


    /// <summary>
    /// [UDP 模式] 以四元數計算裝置座標系重力向量，判斷手機是否平放。
    ///   直立模式：用重力方向映射到 Unity 座標系。
    ///   平放模式：僅儲存四元數，等 HandleAcceleration 用線性加速度驅動。
    /// </summary>
    private void HandleGyroscopeData(SensorEvents.GyroscopeData data)
    {
        const float g = 9.81f;

        if (data.qw != 0f)
        {
            // UDP 四元數模式
            var q = new Quaternion(data.qx, data.qy, data.qz, data.qw);
            currentOrientation = q;
            hasOrientationData = true;

            // gDevice.z ≈ ±9.81 → 平放（螢幕朝上或朝下均可）
            Vector3 gDevice = Quaternion.Inverse(q) * new Vector3(0f, 0f, -g);
            rawPhoneIsFlat = Mathf.Abs(gDevice.z) / g >= flatnessThreshold;

            if (!phoneIsFlat)
            {
                // 直立模式：X=左右傾斜（gDevice.x），Y=前後傾斜（gDevice.z）；持續量不彈回
                // Z 由 HandleAcceleration 的線性加速度負責（推力感）
                // rawPhoneIsFlat=true（防抖中）時保持上一幀的 rawAcceleration.y，
                // 避免 EMA 在防抖期間被拉向 0 造成切換前提前位移（卡頓感）
                rawAcceleration.x = gDevice.x;
                if (!rawPhoneIsFlat)
                    rawAcceleration.y = gDevice.z;
            }
            else
            {
                // 平放模式：X/Y 用重力投影（持續量，穩定），Z 由 HandleAcceleration 填入線性加速（瞬時量）
                // Y = gDevice.y（前後傾斜）對稱 X = gDevice.x（左右傾斜）→ 傾斜持續量
                _gravX = gDevice.x;
                rawAcceleration.x = _gravX + _linX * flatLinearBlendX;
                rawAcceleration.y = gDevice.y;
                // rawAcceleration.z → HandleAcceleration 用 worldAcc.y 填入
            }

            debugQx            = data.qx;
            debugQy            = data.qy;
            debugQz            = data.qz;
            debugQw            = data.qw;
            debugGDevice       = gDevice;
            debugFlatnessRatio = Mathf.Abs(gDevice.z) / g;
            _gDevicePitchZ     = gDevice.z; // 直立 Z 姿態輔助
            _gDevicePitchY     = gDevice.y; // 平放 Z 姿態輔助
            debugTiltAngleDeg  = new Vector2(
                Mathf.Asin(Mathf.Clamp(gDevice.x / g, -1f, 1f)) * Mathf.Rad2Deg,
                Mathf.Asin(Mathf.Clamp(gDevice.y / g, -1f, 1f)) * Mathf.Rad2Deg);
        }
        else
        {
            // WebSocket / 無四元數備用：用傾斜角估算
            float betaRad  = data.beta  * Mathf.Deg2Rad;
            float gammaRad = data.gamma * Mathf.Deg2Rad;
            rawAcceleration = new Vector3(
                 Mathf.Sin(gammaRad) * g,
                -Mathf.Cos(betaRad) * Mathf.Cos(gammaRad) * g,
                 Mathf.Sin(betaRad) * Mathf.Cos(gammaRad) * g
            );
            debugQx = 0f; debugQy = 0f; debugQz = 0f; debugQw = 0f;
            debugGDevice = Vector3.zero;
            debugFlatnessRatio = 0f;
        }
    }

    /// <summary>
    /// 接收 Android TYPE_LINEAR_ACCELERATION（已去重力）。
    /// 直立模式：更新 Z 軸（前後推力）；X 由 HandleGyroscopeData 的 gDevice.x 負責。
    ///   worldAcc.y（Android 水平前後）→ rawAcceleration.z（Unity Z）
    /// 平放模式：X = gDevice.x（HandleGyroscopeData）+ 可選 linX 混入；Z = worldAcc.y（線性推力）。
    /// </summary>
    private void HandleAcceleration(Vector3 acc)
    {
        debugLinearAccInput = acc;

        if (hasOrientationData && !phoneIsFlat)
        {
            // 直立模式：X/Y 已由 HandleGyroscopeData 的 gDevice 負責；此處只更新 Z（前後推力）
            // 與平放 Z 相同做法：限幅防止快速翻轉時離心加速度（可達 15~25 m/s²）暴衝
            Vector3 worldAcc = currentOrientation * acc;
            rawAcceleration.z = Mathf.Clamp(worldAcc.y, -flatLinZClamp, flatLinZClamp);
        }
        else if (hasOrientationData && phoneIsFlat)
        {
            Vector3 worldAcc = currentOrientation * acc;
            // X：重力傾斜（持續量）+ 可選線性混入（HandleGyroscopeData 已設定 rawAcceleration.y = gDevice.y）
            _linX = worldAcc.x;
            rawAcceleration.x = _gravX + _linX * flatLinearBlendX;
            // Z：前後線性加速（瞬時量），與直立 Z 同語義；限幅防止甩動暴衝
            rawAcceleration.z = Mathf.Clamp(worldAcc.y, -flatLinZClamp, flatLinZClamp);
            // Y 不在此更新：由 HandleGyroscopeData 以 gDevice.y（前後傾斜持續量）負責
        }
        else if (!hasOrientationData)
        {
            rawAcceleration = acc;
        }
    }

    private void Update()
    {
        // ── 自動初始校正：第一筆資料到位 + 等待濾波穩定後執行一次 ──
        if (!hasCalibrated)
        {
            if (hasOrientationData)
            {
                if (firstDataTime < 0f)
                    firstDataTime = Time.time;
                else if (Time.time - firstDataTime >= autoCalibrationDelay)
                    Recalibrate();
            }
            // 嚮導在初始校正完成前也可運行，補跑濾波讓樣本有效
            if (wizardPhase != WizardPhase.Idle && wizardPhase != WizardPhase.Done)
            {
                ModeSettings ws = phoneIsFlat ? flatSettings : uprightSettings;
                float wa = 1f - Mathf.Exp(-Time.deltaTime / ws.inputFilterTime);
                filteredAcceleration = Vector3.Lerp(filteredAcceleration, rawAcceleration, wa);
                UpdateWizard();
            }
            transform.localPosition = centerLocalPosition;
            return;
        }

        // ── 模式防抖（含遲滯）：進入平放用 flatnessThreshold，退出平放用較低的 flatnessThreshold − flatnessHysteresis ──
        // 手機斜拿時 flatnessRatio 在閾值附近震盪，遲滯帶讓兩方向使用不同門檻，消除反覆切換。
        float exitThresh   = Mathf.Max(0f, flatnessThreshold - flatnessHysteresis);
        bool  targetFlat   = phoneIsFlat
            ? debugFlatnessRatio >= exitThresh   // 已在平放：需降到更低才離開
            : rawPhoneIsFlat;                    // 非平放：用正常閾值決定進入
        if (targetFlat != phoneIsFlat)
        {
            flatnessHoldTimer += Time.deltaTime;
            if (flatnessHoldTimer >= modeSwitchDebounceTime)
            {
                phoneIsFlat       = targetFlat;
                flatnessHoldTimer = 0f;
            }
        }
        else
        {
            flatnessHoldTimer = 0f;
        }

        // phoneIsFlat 防抖更新完畢後才取 settings，確保本幀套用正確的模式參數
        ModeSettings s = phoneIsFlat ? flatSettings : uprightSettings;

        // ── 切換偵測（必須在濾波更新前執行）──
        // 問題根源：切換時 filteredAcceleration 仍殘留舊模式值，
        //           被新模式的 axisScale/flip 放大後 proposedPosition 暴衝。
        // 對策：立即重設濾波值到新模式的合理起點，並跳到新穩態 currentOffset，
        //       讓 proposedPosition 瞬間穩定；視覺連續性由跳動補償淡出處理。
        bool modeJustSwitched = phoneIsFlat != prevPhoneIsFlat;
        if (modeJustSwitched)
        {
            if (phoneIsFlat)
            {
                // 切入平放：恢復校正時儲存的平放 Tare，保持「手機回原點=球回原點」不變
                // 若尚未在平放模式下校正過，退回使用當前重力（避免初次進入時爆衝）
                bool hasFlatTare = savedTareFlat != Vector3.zero || hasCalibrated;
                var  useTare     = hasFlatTare ? savedTareFlat
                                               : new Vector3(debugGDevice.x, debugGDevice.y, 0f);
                filteredAcceleration   = useTare;
                calibratedAcceleration = useTare;
                // Y 由 HandleGyroscopeData 的 gDevice.y 驅動，此處以當前值初始化以減少 EMA 追蹤距離
                rawAcceleration.y = debugGDevice.y;
                // Z 積分模式：清除殘留的直立 Z 值，防止切換瞬間積分暴衝
                rawAcceleration.z = 0f;
                // 封鎖積分 0.4s：等翻轉手機的慣性動作平息後再啟動，防止翻轉加速度積分成持續速度
                _flatZIntegBlockTimer = 0.4f;
            }
            else
            {
                // 切回直立：恢復校正時儲存的直立 Tare
                bool hasUprightTare = savedTareUpright != Vector3.zero || hasCalibrated;
                var  useTare        = hasUprightTare ? savedTareUpright : rawAcceleration;
                filteredAcceleration   = useTare;
                calibratedAcceleration = useTare;
            }
            // X 軸兩模式語義相同（gDevice.x 左右傾斜），繼承以保持視覺連續
            // Y/Z 語義在兩模式不同（upright Y=gDevice.z, flat Y=gDevice.y；Z flip 方向相反），
            // 繼承舊值會讓新模式把球往反方向拉，改為讓新模式感測器立即主導（從 0 開始）
            {
                Vector3 inh = smoothedPosition - centerLocalPosition;
                currentOffset = new Vector3(
                    s.axisScale.x > 0.001f ? Mathf.Clamp(inh.x / s.axisScale.x, -s.maxOffsetPerAxis.x, s.maxOffsetPerAxis.x) : 0f,
                    0f,
                    0f
                );
            }
            currentVelocity = Vector3.zero;
            prevPhoneIsFlat = phoneIsFlat;
            // 積分狀態重設；卡爾曼從 0 開始避免殘留舊模式 Z 值造成切換後立即暴衝
            _flatZVelInt        = 0f;
            _kalmanZState       = 0f;
            _kalmanZCov         = 1f;
            _flatGDeviceYSmooth = rawAcceleration.y; // 重設為當前 gDevice.y，避免切換瞬間產生假傾斜速率
            _zAnchorOffset    = 0f;
            _zImpulseFiltered = 0f;
            _zAnchorCooldown   = 0f;
            _zAnchorLockedSign = 0;
            _zInPush           = false;
            // 積分模式下 Z 從 0 起算，Tare.z 必須同步歸零，否則 debiased.z 帶常數偏移
            if (phoneIsFlat && flatZUseIntegration)
                calibratedAcceleration.z = 0f;
        }

        float alpha = 1f - Mathf.Exp(-Time.deltaTime / s.inputFilterTime);
        Vector3 emaTarget = rawAcceleration;
        if (_flatZIntegBlockTimer > 0f) _flatZIntegBlockTimer -= Time.deltaTime;

        // 傾斜速率偵測（平放專用）：gDevice.y 快速變化 = 正在傾斜手機。
        // 傾斜同時也產生 worldAcc.y 假訊號，若與 zPitchAssist 同時驅動 Z 會雙重暴衝；
        // 偵測到傾斜中時暫停積分，讓 pitchAssist 單獨負責 Z 的傾斜響應。
        if (phoneIsFlat)
        {
            float gyAlpha = 1f - Mathf.Exp(-Time.deltaTime / 0.25f);
            _flatGDeviceYSmooth = Mathf.Lerp(_flatGDeviceYSmooth, rawAcceleration.y, gyAlpha);
        }
        float _tiltRate = phoneIsFlat ? Mathf.Abs(rawAcceleration.y - _flatGDeviceYSmooth) : 0f;
        // 閾值 0.4 m/s²：平舉推動（純線性加速）幾乎不改變 gDevice.y，
        // 傾斜動作則會讓 gDevice.y 與慢速 EMA 出現明顯偏差。
        bool _tiltInProgress = zPitchAssistEnabled && phoneIsFlat && _tiltRate > 0.4f;

        // 嚮導進行中跳過積分；切換模式後封鎖視窗期間也跳過，等翻轉動作平息後再啟動；
        // 傾斜中（pitchAssist 已接手）也跳過，避免 worldAcc.y 假訊號被雙重積分。
        bool runIntegration = phoneIsFlat && flatZUseIntegration &&
            (wizardPhase == WizardPhase.Idle || wizardPhase == WizardPhase.Done) &&
            _flatZIntegBlockTimer <= 0f &&
            !_tiltInProgress;
        if (runIntegration)
        {
            // 卡爾曼預濾波：消除高頻雜訊再積分，避免噪聲累積成系統性漂移
            _kalmanZCov   += flatZKalmanQ;
            float kGain    = _kalmanZCov / (_kalmanZCov + flatZKalmanR);
            _kalmanZState += kGain * (rawAcceleration.z - _kalmanZState);
            _kalmanZCov   *= 1f - kGain;
            // 純速度積分（有阻尼）：停手後速度指數衰減歸零，不累積位置，不會漂移
            // 建議：flatZOutputScale = flatZFriction / flatZForceGain，使峰值信號≈原始加速度
            // 積分前先套死區：過濾手臂回彈的小反向脈衝（< axisDeadzone.z），
            // 避免停止推力時加速度計的反向噪聲被積分成負速度，造成球過衝反彈。
            float _integDz   = s.axisDeadzone.z;
            float _integInput = Mathf.Abs(_kalmanZState) > _integDz
                ? Mathf.Sign(_kalmanZState) * (Mathf.Abs(_kalmanZState) - _integDz)
                : 0f;
            // 零穿保護：反向輸入只能讓速度趨向 0，不能強制翻轉方向。
            // 停止推力時加速度計的反向慣性脈衝不應把球推到反方向；
            // 真正的反向推力需等摩擦把速度衰減到 0 後才生效。
            if (_flatZVelInt != 0f && _integInput != 0f &&
                Mathf.Sign(_integInput) != Mathf.Sign(_flatZVelInt))
                _integInput = 0f;
            _flatZVelInt += _integInput * flatZForceGain * Time.deltaTime;
            _flatZVelInt *= Mathf.Exp(-flatZFriction * Time.deltaTime);
            // Anti-windup：輸出到達邊界時夾住速度，防止繼續累積後反轉大幅彈跳
            float _velLimit = flatLinZClamp / Mathf.Max(flatZOutputScale, 0.01f);
            _flatZVelInt = Mathf.Clamp(_flatZVelInt, -_velLimit, _velLimit);
            emaTarget.z  = _flatZVelInt * flatZOutputScale;
        }
        filteredAcceleration = Vector3.Lerp(filteredAcceleration, emaTarget, alpha);

        // 直立 Z（前後推力）用更快的獨立 EMA：
        // 死區套用在 EMA 下游，若濾波太慢，訊號要等很久才超過死區閾值，造成「卡頓感」。
        // 用 inputFilterTime × 0.35 讓 Z 軸響應更即時，X/Y 維持原時間常數不變。
        if (!phoneIsFlat && !runIntegration)
        {
            float zFastAlpha = 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(s.inputFilterTime * 0.35f, 0.005f));
            filteredAcceleration.z = Mathf.Lerp(filteredAcceleration.z, rawAcceleration.z, zFastAlpha);
        }

        // ── Z 軸衝量錨點更新（方向鎖定 + 冷卻封鎖）────────────────────────────
        // 原理：推力結束後啟動 zAnchorLockDuration 秒冷卻，期間封鎖反方向累積，
        //       防止手臂自然回彈的加速度被誤讀為刻意反推，消除回彈與單向漂移。
        if (zAnchorEnabled && wizardPhase is WizardPhase.Idle or WizardPhase.Done)
        {
            float impAlpha    = 1f - Mathf.Exp(-Time.deltaTime / zAnchorFilterTime);
            _zImpulseFiltered = Mathf.Lerp(_zImpulseFiltered, rawAcceleration.z, impAlpha);

            float absImp  = Mathf.Abs(_zImpulseFiltered);
            int   curSign = _zImpulseFiltered >= 0f ? 1 : -1;

            if (_zAnchorCooldown > 0f) _zAnchorCooldown -= Time.deltaTime;

            if (absImp > zAnchorThreshold)
            {
                bool inCooldown = _zAnchorCooldown > 0f;
                bool isOpposite = _zAnchorLockedSign != 0 && curSign != _zAnchorLockedSign;

                if (!(inCooldown && isOpposite))
                {
                    // 允許累積：同方向、或冷卻已結束
                    if (!_zInPush) { _zInPush = true; _zAnchorLockedSign = curSign; }
                    float excess    = absImp - zAnchorThreshold;
                    _zAnchorOffset += curSign * excess * zAnchorSensitivity * Time.deltaTime;
                    _zAnchorOffset  = Mathf.Clamp(_zAnchorOffset, -s.maxOffsetPerAxis.z, s.maxOffsetPerAxis.z);
                }
                // else: 冷卻期間反向輸入 → 忽略，不修改錨點
            }
            else
            {
                if (_zInPush) { _zAnchorCooldown = zAnchorLockDuration; _zInPush = false; }
                if (_zAnchorCooldown <= 0f) _zAnchorLockedSign = 0;

                if (absImp < zAnchorThreshold * 0.3f && zAnchorIdleReturn > 0f)
                    _zAnchorOffset = Mathf.Lerp(_zAnchorOffset, 0f, zAnchorIdleReturn * Time.deltaTime);
            }

            debugZAnchorOffset    = _zAnchorOffset;
            debugZImpulseFiltered = _zImpulseFiltered;
            debugZAnchorCooldown  = _zAnchorCooldown;
        }

        // ── 嚮導校正進行中：凍結小球 ──
        if (wizardPhase != WizardPhase.Idle && wizardPhase != WizardPhase.Done)
        {
            UpdateWizard();
            debugRawAcceleration      = rawAcceleration;
            debugFilteredAcceleration = filteredAcceleration;
            debugActualPosition       = transform.localPosition;
            transform.localPosition   = centerLocalPosition;
            return;
        }
        // 嚮導完成後顯示 overlay 3 秒，倒計時結束自動隱藏
        if (wizardPhase == WizardPhase.Done)
        {
            wizardTimer += Time.deltaTime;
            if (wizardTimer >= 3f) { wizardTimer = 0f; wizardPhase = WizardPhase.Idle; }
        }

        // Tare 去偏：以校正瞬間的 filteredAcceleration 為基準，使當前姿勢 = (0,0,0)
        Vector3 debiased = filteredAcceleration - calibratedAcceleration;

        // ── 平放 XZ 重力漂移自動補償 ──────────────────────────────────
        // 根本原因：GAME_ROTATION_VECTOR 只用陀螺儀積分，長時間使用後四元數漂移，
        //           導致 gDevice.x/y 緩慢偏離 0，XZ 去偏值持續非零，球不回中心。
        // 對策：球靜止時（debiased.xz 在死區內），以 flatGravityCorrectionTime 為時間常數
        //       緩慢把 calibratedAcceleration.xz 往 filteredAcceleration.xz 靠，
        //       讓漂移自動被吸收；主動傾斜時（debiased 超出死區）暫停補償。
        if (phoneIsFlat && enableFlatGravityCorrection &&
            wizardPhase is WizardPhase.Idle or WizardPhase.Done)
        {
            // 逐軸獨立判斷：任一軸訊號超過其自身死區時，重力補償立即停止
            // 舊實作以 halfDz*2 為門檻，導致訊號明顯超過死區時補償仍有 40~75% 活躍，
            // 使 tare 緩慢追蹤傾斜位置，令球被「吸回中心」
            float normX = Mathf.Abs(debiased.x) / Mathf.Max(s.axisDeadzone.x, 0.01f);
            // 積分模式下 filteredAcceleration.z 是速度輸出，debiased.z 被補償扯向 0，
            // 無法反映真實推力；改用原始訊號判斷是否靜止，避免補償誤判為 idle 而干擾積分。
            float normZ = flatZUseIntegration
                ? Mathf.Abs(rawAcceleration.z) / Mathf.Max(s.axisDeadzone.z, 0.01f)
                : Mathf.Abs(debiased.z)        / Mathf.Max(s.axisDeadzone.z, 0.01f);
            float idleRatio = 1f - Mathf.Clamp01(Mathf.Max(normX, normZ));
            _dbIdleRatio = idleRatio;
            float corrAlpha = (1f - Mathf.Exp(-Time.deltaTime / flatGravityCorrectionTime)) * idleRatio;
            calibratedAcceleration.x += (filteredAcceleration.x - calibratedAcceleration.x) * corrAlpha;
            // 積分模式下 Z 的參考點由積分自行管理（calibratedAcceleration.z 已鎖 0），不做重力補償
            if (!flatZUseIntegration)
                calibratedAcceleration.z += (filteredAcceleration.z - calibratedAcceleration.z) * corrAlpha;
            // 重新計算去偏（本幀立即生效）
            debiased = filteredAcceleration - calibratedAcceleration;
        }
        else
        {
            _dbIdleRatio = 0f;
        }

        _dbPreSwap = debiased;
        // ── 軸交換（嚮導偵測到手機旋轉 90° 時啟用）──
        if (s.swapXZ)
            debiased = new Vector3(debiased.z, debiased.y, debiased.x);
        _dbPostSwap = debiased;

        // 套用方向翻轉
        Vector3 flipped = new Vector3(
            debiased.x * s.axisFlip.x,
            debiased.y * s.axisFlip.y,
            debiased.z * s.axisFlip.z
        );

        // 逐軸死區
        Vector3 deadzoned = ApplyDeadzone(flipped, s.axisDeadzone);
        _dbAfterDz = deadzoned;

        // 套用遮罩與各軸靈敏度
        targetOffset = new Vector3(
            deadzoned.x * s.movementAxesMask.x * s.sensitivity.x,
            deadzoned.y * s.movementAxesMask.y * s.sensitivity.y,
            deadzoned.z * s.movementAxesMask.z * s.sensitivity.z);

        // 逐軸獨立 Clamp（不互相壓縮）
        targetOffset = new Vector3(
            Mathf.Clamp(targetOffset.x, -s.maxOffsetPerAxis.x, s.maxOffsetPerAxis.x),
            Mathf.Clamp(targetOffset.y, -s.maxOffsetPerAxis.y, s.maxOffsetPerAxis.y),
            Mathf.Clamp(targetOffset.z, -s.maxOffsetPerAxis.z, s.maxOffsetPerAxis.z)
        );

        // Z 軸錨點覆蓋：用持續錨點取代彈回的線性加速度映射
        // 乘上 axisFlip.z：anchor 基於 raw sensor 方向累積，需對齊 mid-pipeline 空間，
        // 確保 outputFlip.z 可一致控制方向（不論 axisFlip.z 為 ±1）
        if (zAnchorEnabled && wizardPhase is WizardPhase.Idle or WizardPhase.Done)
            targetOffset.z = _zAnchorOffset * s.axisFlip.z;

        // Z 軸姿態輔助：以傾斜分量（持續量，不回彈）疊加在 targetOffset.z 上
        // 直立：gDevice.z（俯仰傾斜）；平放：gDevice.y（前後傾斜，最乾淨無耦合）
        // 校正基準在 Recalibrate() 設定，靜止時貢獻為 0
        // 同樣乘上 axisFlip.z，與錨點和主訊號保持一致的座標空間
        if (zPitchAssistEnabled && wizardPhase is WizardPhase.Idle or WizardPhase.Done)
        {
            float rawTilt  = phoneIsFlat ? _gDevicePitchY : _gDevicePitchZ;
            float tiltTare = phoneIsFlat ? _tiltTareFlat  : _tiltTareUpright;
            float tiltZ    = (rawTilt - tiltTare) * zPitchAssistScale * s.axisFlip.z;
            targetOffset.z = Mathf.Clamp(targetOffset.z + tiltZ,
                                         -s.maxOffsetPerAxis.z, s.maxOffsetPerAxis.z);
        }

        if (phoneIsFlat)
        {
            // 平放 X/Z 來自重力投影（位置信號），EMA 追蹤不累積速度，不過衝
            // 各軸靜止時（deadzoned ≈ 0）以 idleReturnStrength 縮小歸中拉力，防止手機停止後球彈回
            float flatAlpha = 1f - Mathf.Exp(-Time.deltaTime * s.smoothSpeed);
            _dbEmaAlpha = flatAlpha;
            float axX = flatAlpha * (Mathf.Abs(deadzoned.x) > 0.001f ? 1f : s.idleReturnStrength.x);
            float axY = flatAlpha * (Mathf.Abs(deadzoned.y) > 0.001f ? 1f : s.idleReturnStrength.y);
            // 錨點或姿態輔助有非零目標時全速追蹤，消除 idleReturnStrength 造成的緩慢漸近感。
            // 積分模式（flatZUseIntegration）時也永遠全速追蹤：停止推力後 _flatZVelInt 因摩擦衰減，
            // targetOffset.z 在 ~0.35s 內自然歸零；currentOffset.z 全速跟追，
            // 避免 idleReturnStrength 讓球在 2-3 秒內被緩慢拉回原點（使用者感知為「硬拉」）。
            bool zHasTarget = zAnchorEnabled || (zPitchAssistEnabled && Mathf.Abs(targetOffset.z) > 0.001f)
                || (flatZUseIntegration && runIntegration);
            float axZ = zHasTarget
                ? flatAlpha
                : flatAlpha * (Mathf.Abs(deadzoned.z) > 0.001f ? 1f : s.idleReturnStrength.z);
            currentOffset.x = Mathf.Lerp(currentOffset.x, targetOffset.x, axX);
            currentOffset.y = Mathf.Lerp(currentOffset.y, targetOffset.y, axY);
            currentOffset.z = Mathf.Lerp(currentOffset.z, targetOffset.z, axZ);
            currentVelocity = Vector3.zero;
        }
        else
        {
            // 直立模式：idle 軸直接 EMA 緩慢歸中；active 軸保持 SmoothDamp 彈性追蹤。
            // idle 軸完全繞過 SmoothDamp，避免殘留速度持續加速回彈。
            _dbEmaAlpha = 0f;
            float smoothTime  = 1f / s.smoothSpeed;
            float smoothAlpha = 1f - Mathf.Exp(-Time.deltaTime * s.smoothSpeed);

            bool xIdle = Mathf.Abs(deadzoned.x) <= 0.001f;
            bool yIdle = Mathf.Abs(deadzoned.y) <= 0.001f;
            // 錨點或姿態輔助有非零目標時強制 SmoothDamp，不走 idle EMA（避免緩慢漸近）
            bool zTiltTarget = zPitchAssistEnabled && Mathf.Abs(targetOffset.z) > 0.001f;
            bool zIdle = !zAnchorEnabled && !zTiltTarget && Mathf.Abs(deadzoned.z) <= 0.001f;

            // idle 軸：清速度 + 直接 EMA（每幀移動量 = smoothAlpha × idleReturnStrength，完全 frame-rate independent）
            if (xIdle) { currentVelocity.x = 0f; currentOffset.x = Mathf.Lerp(currentOffset.x, targetOffset.x, smoothAlpha * s.idleReturnStrength.x); }
            if (yIdle) { currentVelocity.y = 0f; currentOffset.y = Mathf.Lerp(currentOffset.y, targetOffset.y, smoothAlpha * s.idleReturnStrength.y); }
            if (zIdle) { currentVelocity.z = 0f; currentOffset.z = Mathf.Lerp(currentOffset.z, targetOffset.z, smoothAlpha * s.idleReturnStrength.z); }

            // active 軸用 SmoothDamp；idle 軸 target = currentOffset → SmoothDamp 對它無作用
            Vector3 sdTarget = new Vector3(
                xIdle ? currentOffset.x : targetOffset.x,
                yIdle ? currentOffset.y : targetOffset.y,
                zIdle ? currentOffset.z : targetOffset.z
            );
            currentOffset = Vector3.SmoothDamp(currentOffset, sdTarget, ref currentVelocity, smoothTime);
        }

        // axisFlip 在管線中段（死區前）套用；此處再乘一次以抵消其方向效果，
        // 使 axisFlip 僅影響死區對稱性（供嚮導內部校正用），不決定最終輸出方向。
        // 輸出方向完全由 outputFlip 控制，與 axisFlip 設定無關。
        Vector3 scaledOffset = new Vector3(
            currentOffset.x * s.axisScale.x * s.axisFlip.x,
            currentOffset.y * s.axisScale.y * s.axisFlip.y,
            currentOffset.z * s.axisScale.z * s.axisFlip.z
        );
        // 輸出端死區：位移小於 minOutputStep 歸零，消除靜止微抖
        debugScaledBeforeFilter = scaledOffset;
        if (Mathf.Abs(scaledOffset.x) < s.minOutputStep.x) scaledOffset.x = 0f;
        if (Mathf.Abs(scaledOffset.y) < s.minOutputStep.y) scaledOffset.y = 0f;
        // Z：主動施力時跳過 minOutputStep，讓 EMA/SmoothDamp 從 0 連續建立不被截斷（防止卡頓感）；
        // 只在 idle（訊號在死區內）時才過濾微抖。
        bool zInputIdle = Mathf.Abs(deadzoned.z) <= 0.001f &&
            !(zAnchorEnabled) &&
            !(zPitchAssistEnabled && Mathf.Abs(targetOffset.z) > 0.001f) &&
            !(flatZUseIntegration && runIntegration);
        if (zInputIdle && Mathf.Abs(scaledOffset.z) < s.minOutputStep.z) scaledOffset.z = 0f;
        debugScaledAfterFilter = scaledOffset;
        debugMinOutputStep     = s.minOutputStep;
        // 先做座標空間轉換，再套用 outputFlip：
        // outputFlip 作用在 parent local 空間，各軸真正獨立，不受 parent 旋轉影響
        Vector3 localScaledOffset = transform.parent != null
            ? transform.parent.InverseTransformDirection(scaledOffset)
            : scaledOffset;
        localScaledOffset = new Vector3(
            localScaledOffset.x * s.outputFlip.x,
            localScaledOffset.y * s.outputFlip.y,
            localScaledOffset.z * s.outputFlip.z
        );
        // 若指定了 centerPoint 物體，每幀動態跟隨其位置，確保球始終以該物體為原點偏移
        if (centerPoint != null)
            centerLocalPosition = transform.parent != null
                ? transform.parent.InverseTransformPoint(centerPoint.position)
                : centerPoint.position;
        Vector3 proposedPosition = centerLocalPosition + localScaledOffset;

        // ── 切換瞬間：記錄跳動補償量，讓 proposedPosition 仍即時反應傾斜 ──
        // 只補償切換造成的位移跳動，不拖慢正常傾斜的反應速度
        if (modeJustSwitched)
        {
            // offset = 舊位置 - 新模式預測位置；之後隨時間淡出至 0
            modeSwitchTransitionOffset = smoothedPosition - proposedPosition;
            // 平放模式 Y 軸遮罩為 0（targetOffset.y = 0），但若直立時 Y 已偏移很大，
            // 直接清零會造成瞬間彈回；改為只在跳動量小（< 0.5m）時才跳過 blend。
            if (phoneIsFlat && Mathf.Abs(modeSwitchTransitionOffset.y) < 0.5f)
                modeSwitchTransitionOffset.y = 0f;

            float jumpMag = modeSwitchTransitionOffset.magnitude;
            // 起始補償量已計算，保留並隨時間混合淡出（不清零）。
            // blendedTarget = proposedPosition + offset × (1 − SmoothStep(progress))
            // → 切換瞬間 blendedTarget = smoothedPosition（無跳動），0.5s 後完全收斂到新模式。
            modeSwitchTransitionProgress = 0f;
            _effectiveTransitionDuration = modeSwitchTransitionDuration;
            _posVelocity = Vector3.zero;

            string dir = phoneIsFlat ? "直立 → 平放" : "平放 → 直立";
            debugLastSwitchDir   = dir;
            debugSwitchStartDist = jumpMag;
            debugMaxFrameJump    = 0f;
            switchTimer          = 0f;
            switchLoggedComplete = false;
            Debug.Log($"[模式切換] {dir}\n" +
                      $"  切換前位置 : {smoothedPosition:F3}\n" +
                      $"  新模式預測 : {proposedPosition:F3}\n" +
                      $"  跳動距離={jumpMag:F2}m  → 位置混合過渡 {modeSwitchTransitionDuration:F2}s");
        }

        // 輸出位置：混合過渡 + SmoothDamp 抗抖動
        // 原理：modeSwitchTransitionProgress 從 0 爬升到 1（持續 modeSwitchTransitionDuration 秒）。
        //   blendedTarget = proposedPosition + transitionOffset × (1 − SmoothStep(progress))
        //   → 起點與 smoothedPosition 重合（零跳動），終點完全收斂到新模式位置。
        //   每幀最大位移 ≈ jumpMag / duration / fps，完全受 duration 參數控制，不依賴 SmoothDamp 初速。
        if (modeSwitchTransitionProgress < 1f)
            modeSwitchTransitionProgress = Mathf.Clamp01(
                modeSwitchTransitionProgress + Time.deltaTime / Mathf.Max(modeSwitchTransitionDuration, 0.01f));
        {
            float blendFade      = 1f - Mathf.SmoothStep(0f, 1f, modeSwitchTransitionProgress);
            Vector3 blendedTarget = proposedPosition + modeSwitchTransitionOffset * blendFade;
            float outT            = Mathf.Max(positionFilterTime, 0.01f);
            smoothedPosition = Vector3.SmoothDamp(smoothedPosition, blendedTarget, ref _posVelocity, outT);
        }
        transform.localPosition = smoothedPosition;

        if (showAxisGizmos)
        {
            Vector3 p = transform.position;
            Debug.DrawLine(p, p + Vector3.right   * axisGizmoLength, Color.red);
            Debug.DrawLine(p, p + Vector3.up      * axisGizmoLength, Color.green);
            Debug.DrawLine(p, p + Vector3.forward * axisGizmoLength, Color.blue);
        }

        // --- 淡出期間追蹤 ---
        if (switchTimer >= 0f)
        {
            switchTimer              += Time.deltaTime;
            float frameDelta          = Vector3.Distance(transform.localPosition, prevFramePosition);
            debugMaxFrameJump         = Mathf.Max(debugMaxFrameJump, frameDelta);
            debugTimeSinceLastSwitch  = switchTimer;
            debugTransitionRemaining  = 1f - modeSwitchTransitionProgress;

            if (!switchLoggedComplete && modeSwitchTransitionProgress >= 1f)
            {
                switchLoggedComplete = true;
                Debug.Log($"[切換過渡完成] {debugLastSwitchDir} | " +
                          $"耗時={switchTimer:F2}s | 最大單幀跳動={debugMaxFrameJump:F3}m");
            }
        }
        prevFramePosition = transform.localPosition;

        // Editor：Space 鍵校正
        if (Input.GetKeyDown(KeyCode.Space))
            Recalibrate();

        // 實機：同時觸碰兩指（雙指點擊）校正
        if (Input.touchCount == 2 &&
            Input.GetTouch(0).phase == TouchPhase.Began &&
            Input.GetTouch(1).phase == TouchPhase.Began)
            Recalibrate();

        debugRawAcceleration        = rawAcceleration;
        debugFilteredAcceleration   = filteredAcceleration;
        debugCalibratedAcceleration = calibratedAcceleration;
        debugDebiasedAcceleration   = filteredAcceleration - calibratedAcceleration;
        debugTargetOffset           = targetOffset;
        debugCurrentOffset          = currentOffset;
        debugTransitionProgress     = modeSwitchTransitionProgress;
        debugActualPosition         = transform.localPosition;

        if (phoneIsFlat)
        {
            dbPipe_raw       = new Vector2(rawAcceleration.x,        rawAcceleration.z);
            dbPipe_filtered  = new Vector2(filteredAcceleration.x,   filteredAcceleration.z);
            dbPipe_tare      = new Vector2(calibratedAcceleration.x, calibratedAcceleration.z);
            dbPipe_preSwap   = new Vector2(_dbPreSwap.x,  _dbPreSwap.z);
            dbPipe_postSwap  = new Vector2(_dbPostSwap.x, _dbPostSwap.z);
            dbPipe_afterDz   = new Vector2(_dbAfterDz.x,  _dbAfterDz.z);
            dbPipe_target    = new Vector2(targetOffset.x, targetOffset.z);
            dbPipe_emaAlpha  = _dbEmaAlpha;
            dbPipe_idleRatio = _dbIdleRatio;
        }

        Vector3 db = filteredAcceleration - calibratedAcceleration;
        levelAxisX = db.x;
        levelAxisY = db.y;
        rollDeg    = Mathf.Atan2(db.x, db.z) * Mathf.Rad2Deg;
        pitchDeg   = Mathf.Atan2(db.y, db.z) * Mathf.Rad2Deg;

        // ── 平放模式統計 & 定時 Console 輸出 ──
        if (phoneIsFlat && hasCalibrated)
        {
            if (!_statEntrySet)
            {
                _statTareEntry = dbPipe_tare;
                _statEntrySet  = true;
            }
            _statFlatTime  += Time.deltaTime;
            _statMaxIdle    = Mathf.Max(_statMaxIdle, _dbIdleRatio);
            _statMaxTarget  = new Vector2(
                Mathf.Max(_statMaxTarget.x, Mathf.Abs(targetOffset.x)),
                Mathf.Max(_statMaxTarget.y, Mathf.Abs(targetOffset.z)));
            float jitter    = Vector3.Distance(transform.localPosition, _statPrevPos);
            _statMaxJitter  = Mathf.Max(_statMaxJitter, jitter);
            _statJitterSum += jitter;
            _statJitterCount++;

            if (pipeLogInterval > 0f)
            {
                _pipeLogTimer += Time.deltaTime;
                if (_pipeLogTimer >= pipeLogInterval)
                {
                    _pipeLogTimer = 0f;
                    Debug.Log(
                        $"[FlatPipe {_statFlatTime:F1}s]" +
                        $"  raw({dbPipe_raw.x:+0.00;-0.00},{dbPipe_raw.y:+0.00;-0.00})" +
                        $"  tare({dbPipe_tare.x:+0.00;-0.00},{dbPipe_tare.y:+0.00;-0.00})" +
                        $"  pre({dbPipe_preSwap.x:+0.00;-0.00},{dbPipe_preSwap.y:+0.00;-0.00})" +
                        $"  post({dbPipe_postSwap.x:+0.00;-0.00},{dbPipe_postSwap.y:+0.00;-0.00})" +
                        $"  dz({dbPipe_afterDz.x:+0.00;-0.00},{dbPipe_afterDz.y:+0.00;-0.00})" +
                        $"  tgt({dbPipe_target.x:+0.00;-0.00},{dbPipe_target.y:+0.00;-0.00})" +
                        $"  scaled_before({debugScaledBeforeFilter.x:+0.00;-0.00},{debugScaledBeforeFilter.z:+0.00;-0.00})" +
                        $"  scaled_after({debugScaledAfterFilter.x:+0.00;-0.00},{debugScaledAfterFilter.z:+0.00;-0.00})" +
                        $"  minStep({debugMinOutputStep.x:F2},{debugMinOutputStep.z:F2})" +
                        $"  idle={_dbIdleRatio:F2}");
                }
            }
        }
        else if (!phoneIsFlat)
        {
            _statEntrySet = false;
            _pipeLogTimer = 0f;
        }
        _statPrevPos = transform.localPosition;
    }

    /// <summary>
    /// 將加速度向量經過 flip → deadzone → mask → sensitivity → clamp 完整管線，
    /// 回傳對應的 targetOffset。用於切換時立即算出新模式穩態。
    /// </summary>
    private static Vector3 ComputeTargetOffset(Vector3 acc, ModeSettings s)
    {
        Vector3 flipped = new Vector3(
            acc.x * s.axisFlip.x,
            acc.y * s.axisFlip.y,
            acc.z * s.axisFlip.z
        );
        Vector3 deadzoned = ApplyDeadzone(flipped, s.axisDeadzone);
        Vector3 masked = new Vector3(
            deadzoned.x * s.movementAxesMask.x * s.sensitivity.x,
            deadzoned.y * s.movementAxesMask.y * s.sensitivity.y,
            deadzoned.z * s.movementAxesMask.z * s.sensitivity.z);
        return new Vector3(
            Mathf.Clamp(masked.x, -s.maxOffsetPerAxis.x, s.maxOffsetPerAxis.x),
            Mathf.Clamp(masked.y, -s.maxOffsetPerAxis.y, s.maxOffsetPerAxis.y),
            Mathf.Clamp(masked.z, -s.maxOffsetPerAxis.z, s.maxOffsetPerAxis.z)
        );
    }

    private static Vector3 ApplyDeadzone(Vector3 v, Vector3 dz)
    {
        return new Vector3(
            Mathf.Abs(v.x) < dz.x ? 0f : Mathf.Sign(v.x) * (Mathf.Abs(v.x) - dz.x),
            Mathf.Abs(v.y) < dz.y ? 0f : Mathf.Sign(v.y) * (Mathf.Abs(v.y) - dz.y),
            Mathf.Abs(v.z) < dz.z ? 0f : Mathf.Sign(v.z) * (Mathf.Abs(v.z) - dz.z)
        );
    }

    public void Recalibrate()
    {
        // Tare：以當前 filteredAcceleration 為新的「靜止基準」
        // 下一幀 debiased = filteredAcceleration - calibratedAcceleration ≈ 0
        calibratedAcceleration = filteredAcceleration;
        // 依模式保存校正基準，模式切換時恢復而非覆蓋
        if (phoneIsFlat)
        {
            savedTareFlat = filteredAcceleration;
            // 積分模式：Z 從 0 起算，Tare.z 必須同步歸零，否則 debiased.z 帶常數偏移
            if (flatZUseIntegration) { calibratedAcceleration.z = 0f; savedTareFlat.z = 0f; }
        }
        else
            savedTareUpright = filteredAcceleration;

        // 原點鎖定：以「校正當下的實際位置」為新中心
        // 之後物件只在 maxOffsetPerAxis 範圍內移動，不會再累加漂移
        centerLocalPosition = centerPoint != null
            ? (transform.parent != null
                ? transform.parent.InverseTransformPoint(centerPoint.position)
                : centerPoint.position)
            : transform.localPosition;

        hasCalibrated            = true;
        currentOffset            = Vector3.zero;
        currentVelocity          = Vector3.zero;
        targetOffset             = Vector3.zero;
        modeSwitchTransitionProgress = 1f;
        modeSwitchTransitionOffset   = Vector3.zero;
        smoothedPosition             = centerLocalPosition;
        switchTimer                  = -1f;
        switchLoggedComplete         = false;
        debugMaxFrameJump            = 0f;
        debugTransitionRemaining     = 0f;
        debugTimeSinceLastSwitch     = -1f;
        _flatZVelInt  = 0f;
        _kalmanZState = 0f;
        _kalmanZCov   = 1f;
        _flatZIntegBlockTimer = 0f;
        _switchSmoothRemaining = 0f;
        _posVelocity = Vector3.zero;
        _zAnchorOffset    = 0f;
        _zImpulseFiltered = 0f;
        _zAnchorCooldown   = 0f;
        _zAnchorLockedSign = 0;
        _zInPush           = false;
        if (phoneIsFlat) _tiltTareFlat    = _gDevicePitchY;
        else             _tiltTareUpright = _gDevicePitchZ;
        calibrationMsgPosition = centerLocalPosition;
        calibrationMsgTimer    = 3f;
        Debug.Log($"[AccelerometerBallEffect] 已校正 | Tare 基準: {calibratedAcceleration:F3} | 原點鎖定: {centerLocalPosition}");
    }

    private void OnDrawGizmos()
    {
        if (!showAxisGizmos) return;
        Vector3 pos = transform.position;
        Gizmos.color = new Color(1f, 0.2f, 0.2f);
        Gizmos.DrawRay(pos, Vector3.right   * axisGizmoLength);
        Gizmos.color = new Color(0.2f, 1f, 0.2f);
        Gizmos.DrawRay(pos, Vector3.up      * axisGizmoLength);
        Gizmos.color = new Color(0.2f, 0.5f, 1f);
        Gizmos.DrawRay(pos, Vector3.forward * axisGizmoLength);
    }

}
