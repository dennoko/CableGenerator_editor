using System;
using System.Collections.Generic;
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
    internal class CableSplineContainerEditor : UnityEditor.Editor
    {
        static readonly string[] kTangentModeLabels = { "自動", "スムーズ", "コーナー" };
        static readonly GUIContent kLabelPos        = new GUIContent("位置", "制御点のワールド座標。直接入力で移動できます。");
        static readonly GUIContent kLabelRot        = new GUIContent("回転", "制御点のオイラー角。断面の向きに影響します。");
        static readonly GUIContent kLabelTangentOut = new GUIContent("T出→", "TangentOut: スプライン進行方向の接線ベクトル（ノットローカル空間）");
        static readonly GUIContent kLabelTangentIn  = new GUIContent("T入←", "TangentIn:  スプライン逆方向の接線ベクトル（ノットローカル空間）\nコーナーモードのみ独立して編集できます。");
        static readonly GUIContent kLabelRotH       = new GUIContent("H", "水平方向の回転（ノットローカルY軸）");
        static readonly GUIContent kLabelRotV       = new GUIContent("V", "垂直方向の回転（ノットローカルX軸）");

        // シーンビューのノットインデックスラベル用（遅延初期化）
        static GUIStyle s_indexLabelStyle;
        static GUIStyle s_indexLabelSelectedStyle;

        // 選択中ノット詳細パネルの「回転・接線」展開状態
        static bool s_showAdvancedKnotDetail = false;

        const string kPrefKeyCustomUI = "CableGenerator_UseCustomSplineUI";
        static bool UseCustomUI
        {
            get => EditorPrefs.GetBool(kPrefKeyCustomUI, true);
            set => EditorPrefs.SetBool(kPrefKeyCustomUI, value);
        }

        Editor _defaultEditor;

        // ================================================================
        //  Editor Lifecycle
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
                if (EditorGUI.EndChangeCheck())
                {
                    UseCustomUI = useCustomUI;
                }

                if (useCustomUI)
                    DrawCableUI(container);
                else
                    DrawFallbackUI();
            }
            else
            {
                DrawFallbackUI();
            }
        }

        void DrawFallbackUI()
        {
            if (_defaultEditor != null) _defaultEditor.OnInspectorGUI();
            else DrawDefaultInspector();
        }

        // ================================================================
        //  Scene GUI ─ Index labels
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
                float size = HandleUtility.GetHandleSize(worldPos) * 0.18f;
                bool isSelected = (i == CableGeneratorInspector.s_snapKnotIndex);

                bool isFirst = i == 0;
                bool isLast  = i == spline.Count - 1 && !spline.Closed;
                string text  = isFirst ? $"[{i}]▶" : isLast ? $"◀[{i}]" : $"[{i}]";

                Handles.Label(
                    worldPos + Camera.current.transform.up * size * 1.4f,
                    new GUIContent(text, $"Knot {i}"),
                    isSelected ? s_indexLabelSelectedStyle : s_indexLabelStyle);
            }
        }

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

            s_indexLabelSelectedStyle = new GUIStyle(s_indexLabelStyle);
            s_indexLabelSelectedStyle.normal.textColor = new Color(1f, 0.9f, 0.2f);
            s_indexLabelSelectedStyle.fontSize = 13;
        }

        // ================================================================
        //  Cable Inspector UI
        // ================================================================

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

        // ─── Stats Bar ────────────────────────────────────────────────────

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

        // ─── Knot List ────────────────────────────────────────────────────

        void DrawKnotList(SplineContainer container, Spline spline)
        {
            GUILayout.BeginVertical(CableGeneratorTheme.CardStyle);

            GUILayout.Label("制御点リスト", CableGeneratorTheme.SectionHeaderStyle);
            var lineRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(lineRect, CableGeneratorTheme.Outline);
            GUILayout.Space(4);

            int count          = spline.Count;
            int deleteIndex    = -1;

            for (int i = 0; i < count; i++)
            {
                DrawKnotRow(container, spline, i, count, ref deleteIndex);
                GUILayout.Space(2);
            }

            GUILayout.Space(4);

            // 追加ボタン（プライマリアクション）
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
            bool isSelected = (index == CableGeneratorInspector.s_snapKnotIndex);
            bool isFirst    = index == 0;
            bool isLast     = index == total - 1 && !spline.Closed;

            var rowStyle = isSelected
                ? CableGeneratorTheme.KnotRowSelectedStyle
                : CableGeneratorTheme.KnotRowStyle;

            GUILayout.BeginHorizontal(rowStyle);

            // ラベルボタン（クリックで選択、空き領域全体を埋める）
            string labelText = isFirst ? $"[{index}] スタート"
                             : isLast  ? $"[{index}] エンド"
                             :           $"[{index}]";

            var labelBtnStyle = new GUIStyle(CableGeneratorTheme.CaptionStyle);
            labelBtnStyle.alignment = TextAnchor.MiddleLeft;

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
            var deleteBtnStyle = new GUIStyle(CableGeneratorTheme.DangerButtonStyle);
            deleteBtnStyle.fontSize = Mathf.RoundToInt(deleteBtnStyle.fontSize * 1.5f);
            deleteBtnStyle.alignment = TextAnchor.MiddleCenter;
            deleteBtnStyle.padding = new RectOffset(0, 0, 0, 0);

            if (GUILayout.Button(new GUIContent("×", $"ノット {index} を削除します（最低2点必要）"),
                deleteBtnStyle, GUILayout.Width(22), GUILayout.Height(22)))
                deleteIndex = index;
            EditorGUI.EndDisabledGroup();

            GUILayout.EndHorizontal();
        }

        // ─── Selected Knot Detail Panel ───────────────────────────────────

        void DrawSelectedKnotDetail(SplineContainer container, Spline spline)
        {
            int index = CableGeneratorInspector.s_snapKnotIndex;
            if (index < 0 || index >= spline.Count) return;

            GUILayout.BeginVertical(CableGeneratorTheme.CardStyle);

            // ヘッダ行: タイトル + 前後ナビ
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"ノット [{index}] の詳細", CableGeneratorTheme.SectionHeaderStyle);
            GUILayout.FlexibleSpace();
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

            // 位置フィールド（ワールド空間）
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(kLabelPos, CableGeneratorTheme.CaptionStyle, GUILayout.Width(28));
            Vector3 worldPos = container.transform.TransformPoint((Vector3)(float3)knot.Position);
            EditorGUI.BeginChangeCheck();
            Vector3 newWorldPos = EditorGUILayout.Vector3Field(GUIContent.none, worldPos);
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

            // 詳細セクション（折りたたみ）: 回転・接線
            string advLabel = (s_showAdvancedKnotDetail ? "▼  " : "▶  ") + "回転・接線（詳細）";
            if (GUILayout.Button(new GUIContent(advLabel,
                    "ノットの回転と接線ベクトルを編集します。\n接線の向きがケーブルの曲がり具合を決めます。"),
                CableGeneratorTheme.SectionHeaderStyle))
            {
                s_showAdvancedKnotDetail = !s_showAdvancedKnotDetail;
                GUI.changed = true;
            }

            if (s_showAdvancedKnotDetail)
            {
                var advLine = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(advLine, CableGeneratorTheme.Outline);
                GUILayout.Space(2);

                // 回転
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(kLabelRot, CableGeneratorTheme.CaptionStyle, GUILayout.Width(28));
                Vector3 euler = ((Quaternion)knot.Rotation).eulerAngles;
                EditorGUI.BeginChangeCheck();
                Vector3 newEuler = EditorGUILayout.Vector3Field(GUIContent.none, euler);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(container, "ノット回転を変更");
                    knot = spline[index];
                    knot.Rotation = (quaternion)Quaternion.Euler(newEuler);
                    spline.SetKnot(index, knot);
                    EditorUtility.SetDirty(container);
                    container.GetComponent<CableGenerator>()?.RebuildMesh();
                }
                EditorGUILayout.EndHorizontal();

                TangentMode currentMode = spline.GetTangentMode(index);

                // 接線Out
                DrawTangentField(container, spline, index, isOut: true, mode: currentMode);

                // 接線In（Brokenモードのみ独立編集可、その他は読み取り専用）
                if (currentMode == TangentMode.Broken)
                    DrawTangentField(container, spline, index, isOut: false, mode: currentMode);
                else
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(kLabelTangentIn, CableGeneratorTheme.CaptionStyle, GUILayout.Width(38));
                    EditorGUI.BeginDisabledGroup(true);
                    EditorGUILayout.Vector3Field(GUIContent.none, (Vector3)spline[index].TangentIn);
                    EditorGUI.EndDisabledGroup();
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndVertical();
        }

        // ─── Tangent Field with Rotation Buttons ──────────────────────────

        static void DrawTangentField(SplineContainer container, Spline spline,
            int index, bool isOut, TangentMode mode)
        {
            var knot          = spline[index];
            float3 tangent    = isOut ? knot.TangentOut : knot.TangentIn;
            bool   changed    = false;
            float3 newTangent = tangent;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(isOut ? kLabelTangentOut : kLabelTangentIn,
                CableGeneratorTheme.CaptionStyle, GUILayout.Width(38));
            EditorGUI.BeginChangeCheck();
            newTangent = (float3)EditorGUILayout.Vector3Field(GUIContent.none, (Vector3)tangent);
            if (EditorGUI.EndChangeCheck()) changed = true;
            EditorGUILayout.EndHorizontal();

            // 回転ボタン行
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40);

            GUILayout.Label(kLabelRotH, CableGeneratorTheme.CaptionStyle, GUILayout.Width(12));
            changed |= TangentRotBtn(ref newTangent, -15f, true,
                new GUIContent("−", "水平方向に −15° 回転（ノットローカルY軸）"));
            changed |= TangentRotBtn(ref newTangent, +15f, true,
                new GUIContent("+", "水平方向に +15° 回転（ノットローカルY軸）"));

            GUILayout.Space(6);
            GUILayout.Label(kLabelRotV, CableGeneratorTheme.CaptionStyle, GUILayout.Width(12));
            changed |= TangentRotBtn(ref newTangent, -15f, false,
                new GUIContent("−", "垂直方向に −15° 回転（ノットローカルX軸）"));
            changed |= TangentRotBtn(ref newTangent, +15f, false,
                new GUIContent("+", "垂直方向に +15° 回転（ノットローカルX軸）"));

            GUILayout.Space(6);
            if (GUILayout.Button(new GUIContent("⊙ 0",
                    "接線の向きをノットのZ軸（スプライン進行方向）にリセット。長さは保持。"),
                CableGeneratorTheme.SecondaryButtonStyle, GUILayout.Width(36)))
            {
                float len = math.length(newTangent);
                float z   = len > 1e-6f ? len : 0.5f;
                newTangent = new float3(0f, 0f, isOut ? z : -z);
                changed    = true;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (!changed) return;

            Undo.RecordObject(container, "接線を編集");
            knot = spline[index];
            if (isOut)
            {
                knot.TangentOut = newTangent;
                if (mode == TangentMode.Mirrored) knot.TangentIn = -newTangent;
            }
            else
            {
                knot.TangentIn = newTangent;
                if (mode == TangentMode.Mirrored) knot.TangentOut = -newTangent;
            }
            spline.SetKnot(index, knot);
            EditorUtility.SetDirty(container);
            container.GetComponent<CableGenerator>()?.RebuildMesh();
        }

        static bool TangentRotBtn(ref float3 tangent, float degrees, bool aroundY, GUIContent content)
        {
            if (!GUILayout.Button(content, CableGeneratorTheme.SecondaryButtonStyle, GUILayout.Width(22)))
                return false;
            float rad = math.radians(degrees);
            tangent   = math.rotate(aroundY ? quaternion.RotateY(rad) : quaternion.RotateX(rad), tangent);
            return true;
        }

        // ================================================================
        //  Knot Operations
        // ================================================================

        static void InsertKnotAfter(SplineContainer container, Spline spline, int index)
        {
            int nextIndex = (index + 1) % spline.Count;
            var kA = spline[index];
            var kB = spline[nextIndex];

            // 前後ノット位置の線形中点（既存ノットの位置は変えない）
            float3 midPos = (kA.Position + kB.Position) * 0.5f;
            float3 dir    = kB.Position - kA.Position;
            float  tanLen = math.length(dir) > 1e-6f ? math.length(dir) / 6f : 0.5f;

            Vector3 fwd = math.length(dir) > 1e-6f
                ? (Vector3)math.normalize(dir) : Vector3.forward;
            quaternion rot = (quaternion)Quaternion.LookRotation(fwd,
                Mathf.Abs(Vector3.Dot(fwd, Vector3.up)) > 0.99f ? Vector3.right : Vector3.up);

            var newKnot = new BezierKnot(midPos,
                new float3(0f, 0f, -tanLen), new float3(0f, 0f, tanLen), rot);

            int count = spline.Count;
            var knots = new BezierKnot[count];
            var modes = new TangentMode[count];
            for (int i = 0; i < count; i++) { knots[i] = spline[i]; modes[i] = spline.GetTangentMode(i); }

            Undo.RecordObject(container, "制御点を挿入");
            bool closed = spline.Closed;
            spline.Clear();
            spline.Closed = closed;

            for (int i = 0; i <= count; i++)
            {
                if (i == index + 1) { spline.Add(newKnot, TangentMode.AutoSmooth); continue; }
                int src = i < index + 1 ? i : i - 1;
                spline.Add(knots[src], modes[src]);
            }

            CableGeneratorInspector.s_snapKnotIndex = index + 1;
            CableGeneratorInspector.s_selectedKnotIndices.Clear();
            EditorUtility.SetDirty(container);
            container.GetComponent<CableGenerator>()?.RebuildMesh();
            SceneView.RepaintAll();
        }

        static void AddKnotAfterSelected(SplineContainer container, Spline spline)
        {
            if (spline == null || spline.Count == 0)
            {
                AddKnotAtEnd(container, spline);
                return;
            }

            int index = CableGeneratorInspector.s_snapKnotIndex;
            if (index < 0 || index >= spline.Count)
                index = spline.Count - 1;

            if (!spline.Closed && index == spline.Count - 1)
                AddKnotAtEnd(container, spline);
            else
                InsertKnotAfter(container, spline, index);
        }

        static void AddKnotAtEnd(SplineContainer container, Spline spline)
        {
            float3 newPos;
            if (spline.Count >= 1)
            {
                var last   = spline[spline.Count - 1];
                float3 dir = math.rotate(last.Rotation, last.TangentOut);
                float3 off = math.lengthsq(dir) > 1e-6f ? math.normalize(dir) * 2f : new float3(0f, 0f, 2f);
                newPos = last.Position + off;
            }
            else newPos = float3.zero;

            Undo.RecordObject(container, "制御点を末尾に追加");
            spline.Add(new BezierKnot(newPos, new float3(0f, 0f, -0.5f), new float3(0f, 0f, 0.5f)),
                TangentMode.AutoSmooth);
            // 追加した knot を即座に選択状態にして詳細パネルを開く
            CableGeneratorInspector.s_snapKnotIndex = spline.Count - 1;
            CableGeneratorInspector.s_selectedKnotIndices.Clear();
            EditorUtility.SetDirty(container);
            container.GetComponent<CableGenerator>()?.RebuildMesh();
            SceneView.RepaintAll();
        }

        static void FlipSplineDirection(SplineContainer container, Spline spline)
        {
            int count = spline.Count;
            var knots = new BezierKnot[count];
            var modes = new TangentMode[count];
            for (int i = 0; i < count; i++) { knots[i] = spline[i]; modes[i] = spline.GetTangentMode(i); }

            Undo.RecordObject(container, "スプラインの方向を反転");
            bool closed = spline.Closed;
            spline.Clear();
            spline.Closed = closed;

            for (int i = count - 1; i >= 0; i--)
            {
                var k = knots[i];
                (k.TangentIn, k.TangentOut) = (-k.TangentOut, -k.TangentIn);
                spline.Add(k, modes[i]);
            }

            EditorUtility.SetDirty(container);
            container.GetComponent<CableGenerator>()?.RebuildMesh();
            SceneView.RepaintAll();
        }

        static void RedistributeKnotsEvenly(SplineContainer container, Spline spline)
        {
            int count = spline.Count;
            if (count < 2) return;

            var positions = new float3[count];
            var oldKnots  = new BezierKnot[count];
            var modes     = new TangentMode[count];
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / (count - 1);
                SplineUtility.Evaluate(spline, t, out positions[i], out _, out _);
                oldKnots[i] = spline[i];
                modes[i]    = spline.GetTangentMode(i);
            }

            Undo.RecordObject(container, "制御点を均等再配置");
            bool closed = spline.Closed;
            spline.Clear();
            spline.Closed = closed;

            for (int i = 0; i < count; i++)
                spline.Add(new BezierKnot(positions[i],
                    oldKnots[i].TangentIn, oldKnots[i].TangentOut, oldKnots[i].Rotation), modes[i]);

            EditorUtility.SetDirty(container);
            container.GetComponent<CableGenerator>()?.RebuildMesh();
            SceneView.RepaintAll();
        }

        // ================================================================
        //  Helpers
        // ================================================================

        static int ToSimpleIndex(TangentMode mode) => mode switch
        {
            TangentMode.AutoSmooth  => 0,
            TangentMode.Mirrored   => 1,
            TangentMode.Continuous => 1,
            TangentMode.Broken     => 2,
            _                      => 0,
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
