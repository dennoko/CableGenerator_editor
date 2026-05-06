using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using CableGeneratorRuntime;

namespace CableGeneratorEditor
{
    [CustomEditor(typeof(SplineContainer))]
    [CanEditMultipleObjects]
    internal partial class CableSplineContainerEditor : Editor
    {
        // ---- 定数ラベル ----
        static readonly string[]     kTangentModeLabels = { "自動", "スムーズ", "コーナー" };
        static readonly GUIContent   kLabelPos          = new GUIContent("位置",   "制御点のワールド座標。直接入力で移動できます。");
        static readonly GUIContent   kLabelRot          = new GUIContent("回転",   "制御点のオイラー角。断面の向きに影響します。");
        static readonly GUIContent   kLabelTangentOut   = new GUIContent("T出→",   "TangentOut: スプライン進行方向の接線ベクトル（ノットローカル空間）");
        static readonly GUIContent   kLabelTangentIn    = new GUIContent("T入←",   "TangentIn:  スプライン逆方向の接線ベクトル（ノットローカル空間）\nコーナーモードのみ独立して編集できます。");
        static readonly GUIContent   kLabelRotH         = new GUIContent("H",      "水平方向の回転（ノットローカルY軸）");
        static readonly GUIContent   kLabelRotV         = new GUIContent("V",      "垂直方向の回転（ノットローカルX軸）");

        // ---- Scene ビューのノットインデックスラベル用スタイル（遅延初期化） ----
        static GUIStyle s_indexLabelStyle;
        static GUIStyle s_indexLabelSelectedStyle;

        // ---- Editor Preferences ----
        const string kPrefKeyCustomUI = "CableGenerator_UseCustomSplineUI";
        static bool UseCustomUI
        {
            get => EditorPrefs.GetBool(kPrefKeyCustomUI, true);
            set => EditorPrefs.SetBool(kPrefKeyCustomUI, value);
        }

        const string kPrefKeyGlobalKnotCoords = "CableGenerator_UseGlobalKnotCoords";
        static bool UseGlobalKnotCoords
        {
            get => EditorPrefs.GetBool(kPrefKeyGlobalKnotCoords, false);
            set => EditorPrefs.SetBool(kPrefKeyGlobalKnotCoords, value);
        }

        Editor _defaultEditor;

        // ================================================================
        //  Lifecycle
        // ================================================================

        void OnEnable()
        {
            Type defaultType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                defaultType = asm.GetType("UnityEditor.Splines.SplineContainerEditor");
                if (defaultType != null) break;
            }
            if (defaultType != null)
                _defaultEditor = CreateEditor(targets, defaultType);
        }

        void OnDisable()
        {
            if (_defaultEditor != null) { DestroyImmediate(_defaultEditor); _defaultEditor = null; }
        }

        // ================================================================
        //  Inspector
        // ================================================================

        public override void OnInspectorGUI()
        {
            var container = (SplineContainer)target;

            if (container.GetComponent<CableGenerator>() != null)
            {
                EditorGUI.BeginChangeCheck();
                bool useCustomUI = EditorGUILayout.Toggle("カスタムUIを使用", UseCustomUI);
                if (EditorGUI.EndChangeCheck()) UseCustomUI = useCustomUI;

                if (useCustomUI) DrawCableUI(container);
                else             DrawFallbackUI();
            }
            else
            {
                DrawFallbackUI();
            }
        }

        void DrawFallbackUI()
        {
            if (_defaultEditor != null) _defaultEditor.OnInspectorGUI();
            else                        DrawDefaultInspector();
        }

        void DrawCableUI(SplineContainer container)
        {
            CableGeneratorTheme.Initialize();
            if (container.Splines == null || container.Splines.Count == 0) return;
            var spline = container.Splines[0];

            EditorGUILayout.BeginVertical(CableGeneratorTheme.InspectorRootStyle);
            DrawStatsBar(container, spline);
            DrawKnotList(container, spline);
            DrawSelectedKnotDetail(container, spline);
            EditorGUILayout.EndVertical();
        }

        // ================================================================
        //  Scene GUI — ノットインデックスラベル
        // ================================================================

        void OnSceneGUI()
        {
            var container = (SplineContainer)target;
            if (container.GetComponent<CableGenerator>() == null || !UseCustomUI)
            {
                if (_defaultEditor == null) return;
                var m = _defaultEditor.GetType().GetMethod("OnSceneGUI",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                m?.Invoke(_defaultEditor, null);
                return;
            }

            if (container.Splines == null || container.Splines.Count == 0) return;
            // ピッキングモード中はラベルを非表示
            if (CableGeneratorInspector.s_pickingTarget != null) return;

            var spline = container.Splines[0];
            Transform tf = container.transform;
            EnsureIndexLabelStyles();

            for (int i = 0; i < spline.Count; i++)
            {
                Vector3 worldPos = tf.TransformPoint((Vector3)(float3)spline[i].Position);
                float   size     = HandleUtility.GetHandleSize(worldPos) * 0.18f;
                bool    isSelected = i == CableGeneratorInspector.s_snapKnotIndex;

                bool   isFirst = i == 0;
                bool   isLast  = i == spline.Count - 1 && !spline.Closed;
                string text    = isFirst ? $"[{i}]▶" : isLast ? $"◀[{i}]" : $"[{i}]";

                Handles.Label(
                    worldPos + Camera.current.transform.up * size * 1.4f,
                    new GUIContent(text, $"Knot {i}"),
                    isSelected ? s_indexLabelSelectedStyle : s_indexLabelStyle);
            }
        }
    }
}
