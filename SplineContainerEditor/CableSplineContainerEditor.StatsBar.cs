using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using CableGeneratorRuntime;

namespace CableGeneratorEditor
{
    internal partial class CableSplineContainerEditor
    {
        // ================================================================
        //  Stats Bar
        // ================================================================

        static void DrawStatsBar(SplineContainer container, Spline spline)
        {
            GUILayout.BeginVertical(CableGeneratorTheme.CardStyle);

            // 上段: 制御点数・全長・ループトグル
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"制御点: {spline.Count}", CableGeneratorTheme.SecondaryTextStyle, GUILayout.Width(72));

            float length = 0f;
            try { length = SplineUtility.CalculateLength(spline, (float4x4)container.transform.localToWorldMatrix); }
            catch { }
            GUILayout.Label(new GUIContent($"全長: {length:F2} m", "スプライン弧長のワールド空間での推定値"),
                CableGeneratorTheme.CaptionStyle);

            GUILayout.FlexibleSpace();

            bool wasClosed = spline.Closed;
            EditorGUI.BeginChangeCheck();
            bool isClosed = EditorGUILayout.ToggleLeft(
                new GUIContent("ループ", "ONにするとスプラインを閉じたループにします"),
                wasClosed, CableGeneratorTheme.SecondaryTextStyle, GUILayout.Width(56));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(container, "スプラインの開閉を切り替え");
                spline.Closed = isClosed;
                EditorUtility.SetDirty(container);
                container.GetComponent<CableGenerator>()?.RebuildMesh();
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);

            // 下段: 整列ユーティリティ
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("↕ 反転", "スプラインの始点と終点を入れ替えます"),
                CableGeneratorTheme.SecondaryButtonStyle))
                FlipSplineDirection(container, spline);

            if (GUILayout.Button(new GUIContent("⟺ 均等配置",
                    "全制御点をスプライン弧長に沿って等間隔に再配置します。始点と終点の位置は保持されます。"),
                CableGeneratorTheme.SecondaryButtonStyle))
                RedistributeKnotsEvenly(container, spline);
            EditorGUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }
    }
}
