using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using CableGeneratorRuntime;

namespace CableGeneratorEditor
{
    internal partial class CableSplineContainerEditor
    {
        // ---- 詳細パネルの展開状態 ----


        // ---- Rate-slider 用インスタンス状態 ----
        float  s_rateSliderValue    = 0f;
        string s_activeRateSliderId = null;
        double s_lastTime           = 0;

        // ================================================================
        //  Rate Slider (連続速度入力)
        // ================================================================

        bool DrawRateSlider(string label, string id, ref float lengthValue)
        {
            bool changed = false;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, CableGeneratorTheme.CaptionStyle, GUILayout.Width(130));

            float val = s_activeRateSliderId == id ? s_rateSliderValue : 0f;

            EditorGUI.BeginChangeCheck();
            val = EditorGUILayout.Slider(GUIContent.none, val, -1f, 1f);
            if (EditorGUI.EndChangeCheck())
            {
                if (s_activeRateSliderId != id)
                {
                    s_activeRateSliderId = id;
                    s_lastTime = EditorApplication.timeSinceStartup;
                }
                s_rateSliderValue = val;
            }

            if (s_activeRateSliderId == id)
            {
                if (GUIUtility.hotControl == 0)
                {
                    s_activeRateSliderId = null;
                    s_rateSliderValue    = 0f;
                }
                else
                {
                    double time = EditorApplication.timeSinceStartup;
                    float  dt   = (float)(time - s_lastTime);
                    if (dt > 0.1f) dt = 0.016f;

                    if (Mathf.Abs(s_rateSliderValue) > 0.001f)
                    {
                        lengthValue *= Mathf.Pow(12f, s_rateSliderValue * dt);
                        if (lengthValue < 0.01f) lengthValue = 0.01f;
                        changed = true;
                    }
                    s_lastTime = time;
                    Repaint();
                }
            }

            GUILayout.Label($"{lengthValue:F2} m", CableGeneratorTheme.CaptionStyle, GUILayout.Width(45));
            EditorGUILayout.EndHorizontal();

            return changed;
        }

        // ================================================================
        //  Selected Knot Detail Panel
        // ================================================================

        void DrawSelectedKnotDetail(SplineContainer container, Spline spline)
        {
            int index = CableGeneratorInspector.s_snapKnotIndex;
            if (index < 0 || index >= spline.Count) return;

            GUILayout.BeginVertical(CableGeneratorTheme.CardStyle);

            // ヘッダ行: タイトル + 座標系トグル + 前後ナビ
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"ノット [{index}] の詳細", CableGeneratorTheme.SectionHeaderStyle);

            GUILayout.FlexibleSpace();

            EditorGUI.BeginChangeCheck();
            bool useGlobal = GUILayout.Toggle(UseGlobalKnotCoords,
                UseGlobalKnotCoords ? "Global" : "Local",
                CableGeneratorTheme.SecondaryButtonStyle, GUILayout.Width(60));
            if (EditorGUI.EndChangeCheck()) UseGlobalKnotCoords = useGlobal;

            GUILayout.Space(8);

            EditorGUI.BeginDisabledGroup(index <= 0);
            if (GUILayout.Button(new GUIContent("◀", "前のノットを選択"),
                CableGeneratorTheme.SecondaryButtonStyle, GUILayout.Width(22)))
            {
                CableGeneratorInspector.s_snapKnotIndex--;
                CableGeneratorInspector.s_selectedKnotIndices.Clear();
                SceneView.RepaintAll();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(index >= spline.Count - 1);
            if (GUILayout.Button(new GUIContent("▶", "次のノットを選択"),
                CableGeneratorTheme.SecondaryButtonStyle, GUILayout.Width(22)))
            {
                CableGeneratorInspector.s_snapKnotIndex++;
                CableGeneratorInspector.s_selectedKnotIndices.Clear();
                SceneView.RepaintAll();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            var lineRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(lineRect, CableGeneratorTheme.Outline);
            GUILayout.Space(4);

            var knot = spline[index];

            // 位置フィールド
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(kLabelPos, CableGeneratorTheme.CaptionStyle, GUILayout.Width(28));
            Vector3 worldPos = container.transform.TransformPoint((Vector3)(float3)knot.Position);
            Vector3 displayPos = new Vector3(
                (float)System.Math.Round(worldPos.x, 2),
                (float)System.Math.Round(worldPos.y, 2),
                (float)System.Math.Round(worldPos.z, 2));
            EditorGUI.BeginChangeCheck();
            Vector3 newWorldPos = EditorGUILayout.Vector3Field(GUIContent.none, displayPos);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(container, "制御点を移動");
                knot.Position = (float3)container.transform.InverseTransformPoint(newWorldPos);
                spline.SetKnot(index, knot);
                EditorUtility.SetDirty(container);
                container.GetComponent<CableGenerator>()?.RebuildMesh();
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            TangentMode currentMode = spline.GetTangentMode(index);

            // 詳細セクション: 回転・接線 (トグルを廃止し、常に表示)
            GUILayout.Label("回転・接線（詳細）", CableGeneratorTheme.SectionHeaderStyle);

            {
                var advLine = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(advLine, CableGeneratorTheme.Outline);
                GUILayout.Space(2);

                if (currentMode == TangentMode.AutoSmooth)
                {
                    EditorGUILayout.HelpBox(
                        "自動モードのため詳細設定は無効です。詳細を調整するには上のリストからモードを「スムーズ」または「コーナー」に変更してください。",
                        MessageType.Info);
                    GUILayout.Space(4);
                }
                else
                {
                    DrawAdvancedDetailContents(container, spline, index, knot, currentMode);
                }
            }

            GUILayout.EndVertical();
        }

        void DrawAdvancedDetailContents(SplineContainer container, Spline spline,
            int index, BezierKnot knot, TangentMode currentMode)
        {
            // ── 回転 ──
            Vector3 euler = UseGlobalKnotCoords
                ? (container.transform.rotation * knot.Rotation).eulerAngles
                : ((Quaternion)knot.Rotation).eulerAngles;

            EditorGUI.BeginChangeCheck();
            DrawRotationXYZ("回転", ref euler);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(container, "ノット回転を変更");
                knot = spline[index];
                if (UseGlobalKnotCoords)
                    knot.Rotation = (quaternion)(Quaternion.Inverse(container.transform.rotation) * Quaternion.Euler(euler));
                else
                    knot.Rotation = (quaternion)Quaternion.Euler(euler);
                spline.SetKnot(index, knot);
                EditorUtility.SetDirty(container);
                container.GetComponent<CableGenerator>()?.RebuildMesh();
            }

            GUILayout.Space(6);

            float tOutLen = math.length(knot.TangentOut);
            float tInLen  = math.length(knot.TangentIn);

            if (currentMode == TangentMode.Mirrored || currentMode == TangentMode.Continuous)
            {
                float currentLen = tOutLen > 0 ? tOutLen : tInLen;
                if (currentLen < 0.01f) currentLen = 0.01f;

                EditorGUI.BeginChangeCheck();
                bool sliding = DrawRateSlider("ハンドル長さ（同期）", "Len_Smooth", ref currentLen);
                if (EditorGUI.EndChangeCheck() || sliding)
                {
                    Undo.RecordObject(container, "接線長さを変更");
                    knot = spline[index];
                    knot.TangentOut = math.lengthsq(knot.TangentOut) > 1e-6f
                        ? math.normalize(knot.TangentOut) * currentLen
                        : new float3(0, 0, 1) * currentLen;
                    knot.TangentIn = math.lengthsq(knot.TangentIn) > 1e-6f
                        ? math.normalize(knot.TangentIn) * currentLen
                        : new float3(0, 0, -1) * currentLen;
                    spline.SetKnot(index, knot);
                    EditorUtility.SetDirty(container);
                    container.GetComponent<CableGenerator>()?.RebuildMesh();
                }
            }
            else if (currentMode == TangentMode.Broken)
            {
                if (tOutLen < 0.01f) tOutLen = 0.01f;
                if (tInLen  < 0.01f) tInLen  = 0.01f;

                // TangentOut 角度
                Vector3 tOutWorld = UseGlobalKnotCoords
                    ? container.transform.TransformDirection(((Quaternion)knot.Rotation) * (Vector3)knot.TangentOut)
                    : (Vector3)knot.TangentOut;
                Vector3 tOutDir  = tOutWorld.sqrMagnitude > 1e-6f ? tOutWorld
                    : (UseGlobalKnotCoords
                        ? container.transform.TransformDirection(((Quaternion)knot.Rotation) * Vector3.forward)
                        : Vector3.forward);
                Vector3 eulerOut = Quaternion.LookRotation(tOutDir).eulerAngles;

                EditorGUI.BeginChangeCheck();
                DrawRotationXYZ("タンジェントOut 角度", ref eulerOut);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(container, "タンジェントOut角度を変更");
                    knot = spline[index];
                    Vector3 newDirWorldOrLocal = Quaternion.Euler(eulerOut) * Vector3.forward;
                    Vector3 newDirLocal = UseGlobalKnotCoords
                        ? (Quaternion.Inverse(knot.Rotation) * container.transform.InverseTransformDirection(newDirWorldOrLocal))
                        : newDirWorldOrLocal;
                    float len = math.length(knot.TangentOut);
                    knot.TangentOut = (float3)newDirLocal * len;
                    spline.SetKnot(index, knot);
                    EditorUtility.SetDirty(container);
                    container.GetComponent<CableGenerator>()?.RebuildMesh();
                }

                EditorGUI.BeginChangeCheck();
                bool slidingOut = DrawRateSlider("タンジェントOut 長さ", "Len_Out", ref tOutLen);
                if (EditorGUI.EndChangeCheck() || slidingOut)
                {
                    Undo.RecordObject(container, "タンジェントOut長さを変更");
                    knot = spline[index];
                    knot.TangentOut = math.lengthsq(knot.TangentOut) > 1e-6f
                        ? math.normalize(knot.TangentOut) * tOutLen
                        : new float3(0, 0, 1) * tOutLen;
                    spline.SetKnot(index, knot);
                    EditorUtility.SetDirty(container);
                    container.GetComponent<CableGenerator>()?.RebuildMesh();
                }

                GUILayout.Space(8);

                // TangentIn 角度
                Vector3 tInWorld = UseGlobalKnotCoords
                    ? container.transform.TransformDirection(((Quaternion)knot.Rotation) * -(Vector3)knot.TangentIn)
                    : -(Vector3)knot.TangentIn;
                Vector3 tInDir  = tInWorld.sqrMagnitude > 1e-6f ? tInWorld
                    : (UseGlobalKnotCoords
                        ? container.transform.TransformDirection(((Quaternion)knot.Rotation) * Vector3.forward)
                        : Vector3.forward);
                Vector3 eulerIn = Quaternion.LookRotation(tInDir).eulerAngles;

                EditorGUI.BeginChangeCheck();
                DrawRotationXYZ("タンジェントIn 角度", ref eulerIn);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(container, "タンジェントIn角度を変更");
                    knot = spline[index];
                    Vector3 newDirWorldOrLocal = Quaternion.Euler(eulerIn) * Vector3.forward;
                    Vector3 newDirLocal = UseGlobalKnotCoords
                        ? (Quaternion.Inverse(knot.Rotation) * container.transform.InverseTransformDirection(newDirWorldOrLocal))
                        : newDirWorldOrLocal;
                    float len = math.length(knot.TangentIn);
                    knot.TangentIn = -(float3)newDirLocal * len;
                    spline.SetKnot(index, knot);
                    EditorUtility.SetDirty(container);
                    container.GetComponent<CableGenerator>()?.RebuildMesh();
                }

                EditorGUI.BeginChangeCheck();
                bool slidingIn = DrawRateSlider("タンジェントIn 長さ", "Len_In", ref tInLen);
                if (EditorGUI.EndChangeCheck() || slidingIn)
                {
                    Undo.RecordObject(container, "タンジェントIn長さを変更");
                    knot = spline[index];
                    knot.TangentIn = math.lengthsq(knot.TangentIn) > 1e-6f
                        ? math.normalize(knot.TangentIn) * tInLen
                        : new float3(0, 0, -1) * tInLen;
                    spline.SetKnot(index, knot);
                    EditorUtility.SetDirty(container);
                    container.GetComponent<CableGenerator>()?.RebuildMesh();
                }
            }
        }

        // ================================================================
        //  Rotation Field Helpers
        // ================================================================

        static void DrawAxisRotationRow(string label, ref float eulerAxis)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUILayout.Label(label, CableGeneratorTheme.CaptionStyle, GUILayout.Width(16));

            float displayAngle = (float)System.Math.Round(eulerAxis, 2);

            EditorGUI.BeginChangeCheck();
            float newAngle = EditorGUILayout.FloatField(GUIContent.none, displayAngle, GUILayout.Width(50));
            if (EditorGUI.EndChangeCheck()) eulerAxis = newAngle;

            if (GUILayout.Button("0", CableGeneratorTheme.SecondaryButtonStyle, GUILayout.Width(22))) { eulerAxis  = 0f;  GUI.changed = true; }
            if (GUILayout.Button("-", CableGeneratorTheme.SecondaryButtonStyle, GUILayout.Width(22))) { eulerAxis -= 15f; GUI.changed = true; }
            if (GUILayout.Button("+", CableGeneratorTheme.SecondaryButtonStyle, GUILayout.Width(22))) { eulerAxis += 15f; GUI.changed = true; }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        static void DrawRotationXYZ(string header, ref Vector3 eulerAngles)
        {
            GUILayout.Label(header, CableGeneratorTheme.SecondaryTextStyle);
            DrawAxisRotationRow("X", ref eulerAngles.x);
            DrawAxisRotationRow("Y", ref eulerAngles.y);
            DrawAxisRotationRow("Z", ref eulerAngles.z);
        }
    }
}
