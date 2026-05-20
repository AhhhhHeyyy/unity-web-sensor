using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(AccelerometerBallEffect))]
public class AccelerometerBallEffectEditor : Editor
{
    static bool _foldGyro, _foldPipe, _foldSwitch, _foldFlatPipe, _foldWizard, _foldLevel;

    static readonly string[] s_gyro     = { "debugGDevice", "debugFlatnessRatio", "debugQx", "debugQy", "debugQz", "debugQw", "debugLinearAccInput" };
    static readonly string[] s_pipe     = { "debugRawAcceleration", "debugFilteredAcceleration", "debugCalibratedAcceleration", "debugDebiasedAcceleration", "debugTargetOffset", "debugCurrentOffset", "debugTransitionProgress", "debugActualPosition", "debugScaledBeforeFilter", "debugScaledAfterFilter", "debugMinOutputStep" };
    static readonly string[] s_switch   = { "debugLastSwitchDir", "debugTimeSinceLastSwitch", "debugSwitchStartDist", "debugMaxFrameJump", "debugTransitionRemaining" };
    static readonly string[] s_flatPipe = { "dbPipe_raw", "dbPipe_filtered", "dbPipe_tare", "dbPipe_preSwap", "dbPipe_postSwap", "dbPipe_afterDz", "dbPipe_target", "dbPipe_emaAlpha", "dbPipe_idleRatio" };
    static readonly string[] s_wizard   = { "debugTiltAngleDeg", "wizardStatusText", "wizardUprightFlip", "wizardFlatFlip", "wizardMinPeakDisplay", "wizardMaxPeakDisplay" };
    static readonly string[] s_level    = { "levelAxisX", "levelAxisY", "rollDeg", "pitchDeg" };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawDefaultInspector();

        // ── 校正按鈕 ──
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("校正操作", EditorStyles.boldLabel);
        GUI.backgroundColor = new Color(0.4f, 0.85f, 0.4f);
        if (GUILayout.Button("Space / 雙指點擊 — 重新校正（Tare）", GUILayout.Height(36)))
            ((AccelerometerBallEffect)target).Recalibrate();
        GUI.backgroundColor = Color.white;

        // ── 除錯下拉清單 ──
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("── 除錯（唯讀）──", EditorStyles.centeredGreyMiniLabel);
        DrawDebugFoldout(ref _foldGyro,     "陀螺儀原始輸入", s_gyro);
        DrawDebugFoldout(ref _foldPipe,     "位移管線",       s_pipe);
        DrawDebugFoldout(ref _foldSwitch,   "模式切換記錄",   s_switch);
        DrawDebugFoldout(ref _foldFlatPipe, "平放 XZ 管線",   s_flatPipe);
        DrawDebugFoldout(ref _foldWizard,   "嚮導狀態",       s_wizard);
        DrawDebugFoldout(ref _foldLevel,    "水平儀",         s_level);

        serializedObject.ApplyModifiedProperties();
    }

    void DrawDebugFoldout(ref bool expanded, string title, string[] props)
    {
        expanded = EditorGUILayout.BeginFoldoutHeaderGroup(expanded, title);
        if (expanded)
        {
            EditorGUI.indentLevel++;
            EditorGUI.BeginDisabledGroup(true);
            foreach (var name in props)
            {
                var prop = serializedObject.FindProperty(name);
                if (prop != null)
                    EditorGUILayout.PropertyField(prop, true);
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
    }
}
