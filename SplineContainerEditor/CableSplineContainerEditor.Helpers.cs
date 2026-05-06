using UnityEngine;
using UnityEngine.Splines;

namespace CableGeneratorEditor
{
    internal partial class CableSplineContainerEditor
    {
        // ================================================================
        //  Scene ビュースタイル初期化
        // ================================================================

        static void EnsureIndexLabelStyles()
        {
            if (s_indexLabelStyle != null) return;

            s_indexLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize  = 11,
                alignment = TextAnchor.MiddleCenter,
            };
            s_indexLabelStyle.normal.textColor = new Color(0.5f, 0.9f, 1f);

            s_indexLabelSelectedStyle = new GUIStyle(s_indexLabelStyle)
            {
                fontSize = 13,
            };
            s_indexLabelSelectedStyle.normal.textColor = new Color(1f, 0.9f, 0.2f);
        }

        // ================================================================
        //  TangentMode 変換 (UI 3択 ↔ Unity enum)
        // ================================================================

        static int ToSimpleIndex(TangentMode mode) => mode switch
        {
            TangentMode.AutoSmooth  => 0,
            TangentMode.Mirrored    => 1,
            TangentMode.Continuous  => 1,
            TangentMode.Broken      => 2,
            _                       => 0,
        };

        static TangentMode FromSimpleIndex(int idx) => idx switch
        {
            0 => TangentMode.AutoSmooth,
            1 => TangentMode.Mirrored,
            2 => TangentMode.Broken,
            _ => TangentMode.AutoSmooth,
        };
    }
}
