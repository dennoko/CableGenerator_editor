using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using CableGeneratorRuntime;

namespace CableGeneratorEditor
{
    internal partial class CableSplineContainerEditor
    {
        // ================================================================
        //  Knot List
        // ================================================================

        void DrawKnotList(SplineContainer container, Spline spline)
        {
            GUILayout.BeginVertical(CableGeneratorTheme.CardStyle);

            GUILayout.Label("制御点リスト", CableGeneratorTheme.SectionHeaderStyle);
            var lineRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(lineRect, CableGeneratorTheme.Outline);
            GUILayout.Space(4);

            int count       = spline.Count;
            int deleteIndex = -1;

            for (int i = 0; i < count; i++)
            {
                DrawKnotRow(container, spline, i, count, ref deleteIndex);
                GUILayout.Space(2);
            }

            GUILayout.Space(4);

            if (GUILayout.Button(new GUIContent("＋  制御点を追加",
                    "現在選択中の制御点の次に新しい制御点を追加します。\n途中の場合は前後の中間地点に挿入され、末尾の場合は接線方向に2mオフセットされます。"),
                CableGeneratorTheme.ActionButtonStyle))
                AddKnotAfterSelected(container, spline);

            GUILayout.EndVertical();

            if (deleteIndex >= 0 && count > 2)
            {
                Undo.RecordObject(container, "制御点を削除");
                spline.RemoveAt(deleteIndex);
                CableGeneratorInspector.s_snapKnotIndex =
                    Mathf.Clamp(CableGeneratorInspector.s_snapKnotIndex, 0, Mathf.Max(0, spline.Count - 1));
                CableGeneratorInspector.s_selectedKnotIndices.Clear();
                EditorUtility.SetDirty(container);
                container.GetComponent<CableGenerator>()?.RebuildMesh();
                SceneView.RepaintAll();
            }
        }

        void DrawKnotRow(SplineContainer container, Spline spline, int index, int total,
            ref int deleteIndex)
        {
            bool isSelected = index == CableGeneratorInspector.s_snapKnotIndex;
            bool isFirst    = index == 0;
            bool isLast     = index == total - 1 && !spline.Closed;

            var rowStyle = isSelected
                ? CableGeneratorTheme.KnotRowSelectedStyle
                : CableGeneratorTheme.KnotRowStyle;

            GUILayout.BeginHorizontal(rowStyle);

            string labelText = isFirst ? $"[{index}] スタート"
                             : isLast  ? $"[{index}] エンド"
                             :           $"[{index}]";

            var labelBtnStyle = new GUIStyle(CableGeneratorTheme.CaptionStyle)
            {
                alignment = TextAnchor.MiddleLeft
            };

            if (GUILayout.Button(new GUIContent(labelText, $"クリックしてノット {index} を選択します"),
                labelBtnStyle, GUILayout.ExpandWidth(true), GUILayout.Height(22)))
            {
                CableGeneratorInspector.s_snapKnotIndex = index;
                CableGeneratorInspector.s_selectedKnotIndices.Clear();
                SceneView.RepaintAll();
                Repaint();
            }

            // 接線モードドロップダウン
            var modeContent = new GUIContent("",
                "接線モード\n" +
                "自動  = AutoSmooth（Unity が曲率を自動計算）\n" +
                "スムーズ = Mirrored（TangentIn/Out が対称）\n" +
                "コーナー = Broken（TangentIn/Out を独立制御）");
            TangentMode currentMode = spline.GetTangentMode(index);
            EditorGUI.BeginChangeCheck();
            GUILayout.BeginVertical(GUILayout.Width(68));
            GUILayout.FlexibleSpace();
            int newSimpleIdx = EditorGUILayout.Popup(modeContent, ToSimpleIndex(currentMode),
                kTangentModeLabels, GUILayout.Width(68));
            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(container, "接線モードを変更");
                spline.SetTangentMode(index, FromSimpleIndex(newSimpleIdx));
                EditorUtility.SetDirty(container);
                container.GetComponent<CableGenerator>()?.RebuildMesh();
            }

            GUILayout.Space(2);

            EditorGUI.BeginDisabledGroup(total <= 2);
            var deleteBtnStyle = new GUIStyle(CableGeneratorTheme.DangerButtonStyle)
            {
                fontSize  = Mathf.RoundToInt(CableGeneratorTheme.DangerButtonStyle.fontSize * 1.5f),
                alignment = TextAnchor.MiddleCenter,
                padding   = new RectOffset(0, 0, 0, 0),
            };

            if (GUILayout.Button(new GUIContent("×", $"ノット {index} を削除します（最低2点必要）"),
                deleteBtnStyle, GUILayout.Width(22), GUILayout.Height(22)))
                deleteIndex = index;
            EditorGUI.EndDisabledGroup();

            GUILayout.EndHorizontal();
        }
    }
}
