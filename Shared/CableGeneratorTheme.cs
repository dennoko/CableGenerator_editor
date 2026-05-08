using UnityEngine;
using UnityEditor;

namespace CableGeneratorEditor
{
    /// <summary>
    /// dennoko.dev カラースキーマに基づくテーマ定義 (CableGenerator Inspector 用)。
    /// colors_spec.md / design_reference.md の仕様を Unity IMGUI に変換する。
    /// OnInspectorGUI の先頭で Initialize() を呼び出すことで、スタイルを遅延初期化する。
    /// </summary>
    internal static class CableGeneratorTheme
    {
        // ─── Colors ──────────────────────────────────────────────────────────

        public static readonly Color Surface0 = Hex(0x121212); // app background
        public static readonly Color Surface1 = Hex(0x1e1e1e); // cards, inputs
        public static readonly Color Surface2 = Hex(0x2c2c2c); // hover, toolbar

        public static readonly Color Outline = Hex(0x3a3a3a);

        public static readonly Color TextPrimary   = Hex(0xffffff);
        public static readonly Color TextSecondary = Hex(0xcccccc);
        public static readonly Color TextTertiary  = Hex(0xaaaaaa);
        public static readonly Color TextDisabled  = Hex(0x555555);

        // ─── Cached Textures ─────────────────────────────────────────────────

        private static Texture2D _texSurface0;
        private static Texture2D _texSurface1;
        private static Texture2D _texSurface2;
        private static Texture2D _texCard;        // Surface1 fill + Outline border (3x3)
        private static Texture2D _texAccentCard;  // Surface2 fill + Outline border (3x3)

        // ─── Styles ──────────────────────────────────────────────────────────

        private static bool _initialized;

        // Layout / Container
        public static GUIStyle InspectorRootStyle { get; private set; } // インスペクター全体の背景 (Surface0)
        public static GUIStyle CardStyle          { get; private set; } // セクションカード (Surface1)

        // Typography
        public static GUIStyle SectionHeaderStyle    { get; private set; } // 見出し
        public static GUIStyle SecondaryTextStyle    { get; private set; } // 説明文
        public static GUIStyle CaptionStyle          { get; private set; } // 補足・メタデータ

        // Buttons
        public static GUIStyle ActionButtonStyle     { get; private set; } // Primary Action
        public static GUIStyle SecondaryButtonStyle  { get; private set; } // Secondary Action
        public static GUIStyle DangerButtonStyle     { get; private set; } // Destructive Action (delete)

        // Knot List
        public static GUIStyle KnotRowStyle          { get; private set; } // 個別 Knot の行コンテナ
        public static GUIStyle KnotRowSelectedStyle  { get; private set; } // 選択中 Knot の行コンテナ

        // ─────────────────────────────────────────────────────────────────────

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            EnsureTextures();
            BuildStyles();
        }

        private static void EnsureTextures()
        {
            if (!_texSurface0)   _texSurface0   = MakeTex(Surface0);
            if (!_texSurface1)   _texSurface1   = MakeTex(Surface1);
            if (!_texSurface2)   _texSurface2   = MakeTex(Surface2);
            if (!_texCard)       _texCard       = MakeBorderedTex(Surface1, Outline);
            if (!_texAccentCard) _texAccentCard = MakeBorderedTex(Surface2, Outline);
        }

        private static void BuildStyles()
        {
            // ── Container ────────────────────────────────────────────────────

            // インスペクター全体の背景: Surface0 で塗り、Inspector の内側パディングを打ち消す
            InspectorRootStyle = new GUIStyle();
            InspectorRootStyle.normal.background = _texSurface0;
            InspectorRootStyle.margin   = new RectOffset(0, 0, 0, 0);
            InspectorRootStyle.padding  = new RectOffset(10, 10, 8, 8);
            InspectorRootStyle.overflow = new RectOffset(20, 20, 0, 0); // 背景描画領域だけを左右に広げる

            CardStyle = new GUIStyle();
            CardStyle.normal.background = _texCard;
            CardStyle.border  = new RectOffset(1, 1, 1, 1);
            CardStyle.padding = new RectOffset(10, 10, 8, 8);
            CardStyle.margin  = new RectOffset(0, 0, 0, 12);

            // ── Typography ───────────────────────────────────────────────────

            SectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel);
            SectionHeaderStyle.fontSize = 10;
            SectionHeaderStyle.normal.textColor = TextTertiary;
            SectionHeaderStyle.margin = new RectOffset(0, 0, 0, 2);

            SecondaryTextStyle = new GUIStyle(EditorStyles.label);
            SecondaryTextStyle.normal.textColor = TextSecondary;
            SecondaryTextStyle.wordWrap = true;

            CaptionStyle = new GUIStyle(EditorStyles.miniLabel);
            CaptionStyle.normal.textColor = TextTertiary;

            // ── Buttons ──────────────────────────────────────────────────────

            ActionButtonStyle = new GUIStyle();
            ActionButtonStyle.normal.background  = _texAccentCard;
            ActionButtonStyle.normal.textColor   = TextPrimary;
            ActionButtonStyle.hover.background   = MakeTex(Color.Lerp(Surface2, Color.white, 0.07f));
            ActionButtonStyle.hover.textColor    = TextPrimary;
            ActionButtonStyle.active.background  = MakeTex(Color.Lerp(Surface2, Color.white, 0.15f));
            ActionButtonStyle.active.textColor   = TextPrimary;
            ActionButtonStyle.border     = new RectOffset(1, 1, 1, 1);
            ActionButtonStyle.margin     = new RectOffset(4, 4, 2, 2);
            ActionButtonStyle.padding    = new RectOffset(2, 2, 2, 2);
            ActionButtonStyle.fontSize   = 13;
            ActionButtonStyle.fontStyle  = FontStyle.Bold;
            ActionButtonStyle.fixedHeight = 30; // インスペクタ用なので少し小さめ
            ActionButtonStyle.alignment  = TextAnchor.MiddleCenter;

            SecondaryButtonStyle = new GUIStyle();
            SecondaryButtonStyle.normal.background = MakeBorderedTex(Surface1, Outline);
            SecondaryButtonStyle.normal.textColor  = TextSecondary;
            SecondaryButtonStyle.hover.background  = _texAccentCard;
            SecondaryButtonStyle.hover.textColor   = TextPrimary;
            SecondaryButtonStyle.active.background = MakeTex(Color.Lerp(Surface1, Color.white, 0.10f));
            SecondaryButtonStyle.active.textColor  = TextPrimary;
            SecondaryButtonStyle.border     = new RectOffset(1, 1, 1, 1);
            SecondaryButtonStyle.margin     = new RectOffset(4, 4, 2, 2);
            SecondaryButtonStyle.padding    = new RectOffset(2, 2, 2, 2);
            SecondaryButtonStyle.fontSize   = 11;
            SecondaryButtonStyle.fixedHeight = 24;
            SecondaryButtonStyle.alignment  = TextAnchor.MiddleCenter;

            DangerButtonStyle = new GUIStyle();
            DangerButtonStyle.normal.background = MakeBorderedTex(Surface1, Outline);
            DangerButtonStyle.normal.textColor  = new Color(0.80f, 0.35f, 0.35f);
            DangerButtonStyle.hover.background  = MakeTex(new Color(0.35f, 0.08f, 0.08f));
            DangerButtonStyle.hover.textColor   = TextPrimary;
            DangerButtonStyle.active.background = MakeTex(new Color(0.50f, 0.12f, 0.12f));
            DangerButtonStyle.active.textColor  = TextPrimary;
            DangerButtonStyle.border      = new RectOffset(1, 1, 1, 1);
            DangerButtonStyle.margin      = new RectOffset(4, 4, 2, 2);
            DangerButtonStyle.padding     = new RectOffset(2, 2, 2, 2);
            DangerButtonStyle.fontSize    = 11;
            DangerButtonStyle.fixedHeight = 18;
            DangerButtonStyle.alignment   = TextAnchor.MiddleCenter;

            KnotRowStyle = new GUIStyle();
            KnotRowStyle.normal.background = _texCard;
            KnotRowStyle.border  = new RectOffset(1, 1, 1, 1);
            KnotRowStyle.padding = new RectOffset(8, 8, 4, 4);
            KnotRowStyle.margin  = new RectOffset(0, 0, 0, 4);

            KnotRowSelectedStyle = new GUIStyle(KnotRowStyle);
            KnotRowSelectedStyle.normal.background = _texAccentCard; // Surface2 でハイライト
        }

        // ─── Texture Utilities ───────────────────────────────────────────────

        private static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        private static Texture2D MakeBorderedTex(Color fillColor, Color borderColor)
        {
            const int size = 3;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y,
                        (x == 0 || x == size - 1 || y == 0 || y == size - 1)
                            ? borderColor
                            : fillColor);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            tex.hideFlags  = HideFlags.HideAndDontSave;
            return tex;
        }

        private static Color Hex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >>  8) & 0xFF) / 255f,
            ( rgb        & 0xFF) / 255f);
    }
}
