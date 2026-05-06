using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Splines;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using CableGeneratorRuntime;

namespace CableGeneratorEditor
{
    [CustomEditor(typeof(CableGenerator))]
    public partial class CableGeneratorInspector : Editor
    {
        // ---- Serialized Properties ----
        SerializedProperty profileProp;
        SerializedProperty resolutionProp;
        SerializedProperty uvTilingProp;

        string bakeFolderPath = "";

        // ---- Picking Mode (static: 複数インスペクタ間で共有) ----
        internal static CableGenerator s_pickingTarget = null;
        static int            s_pickCount     = 0;
        static Vector3[]      s_pickedPoints  = new Vector3[2];
        static Vector3[]      s_pickedNormals = new Vector3[2];
        static float          s_tangentScale  = 1f;

        // ---- Knot Projection Settings ----
        internal static int       s_snapKnotIndex        = 0;
        internal static readonly HashSet<int> s_selectedKnotIndices = new HashSet<int>();
        static Vector3   s_snapDirection        = Vector3.down;
        static bool      s_snapDirectionIsLocal = false;
        static float     s_snapMaxDistance      = 10f;
        static float     s_snapSurfaceOffset    = 0.003f;
        static LayerMask s_snapLayerMask        = ~0;
        static string    s_snapLastResult       = string.Empty;
        static TangentMode s_addedKnotMode      = TangentMode.AutoSmooth;
        static int         s_initialDivisionCount = 4;
        static string      s_knotInitLastResult = string.Empty;

        // ---- Cable Sag Settings ----
        static float     s_sagDropDistance   = 0.5f;
        static float     s_sagHandleLength   = 0.5f;
        static string    s_sagLastResult     = string.Empty;
        static bool      s_hasSagKnots       = false;
        static bool      s_sagUseMirrored    = false;
        static int       s_sagOriginalCount  = 0;
        static bool      s_sagWasClosed      = false;
        static Vector3[] s_sagBasePositions  = null;

        // ---- Section Fold States (デフォルト折りたたみ) ----
        static bool s_foldSplineSetup     = false;
        static bool s_foldKnotSubdivision = false;
        static bool s_foldCableSag        = false;
        static bool s_foldKnotProjection  = false;
        static bool s_foldAttachments     = false;
        static bool s_foldExport          = false;

        const float kVectorEpsilon          = 0.000001f;
        const float kVectorEpsilonSqr       = kVectorEpsilon * kVectorEpsilon;
        const float kMidpointTangentDivisor = 6f;
        const float kLinearTangentDivisor   = 3f;

        // Rate-slider 用インスタンス状態
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
        //  Lifecycle
        // ================================================================

        void OnEnable()
        {
            profileProp    = serializedObject.FindProperty("profile");
            resolutionProp = serializedObject.FindProperty("resolution");
            uvTilingProp   = serializedObject.FindProperty("uvTiling");
        }

        // ================================================================
        //  Inspector GUI
        // ================================================================

        public override void OnInspectorGUI()
        {
            CableGeneratorTheme.Initialize();

            serializedObject.Update();
            var generator = (CableGenerator)target;

            EditorGUILayout.BeginVertical(CableGeneratorTheme.InspectorRootStyle);

            // ---- 断面プロファイルの設定 ----
            DrawSection("断面プロファイルの設定", () =>
            {
                EditorGUI.BeginChangeCheck();

                EditorGUILayout.PropertyField(profileProp,    new GUIContent("断面プロファイル"));
                EditorGUILayout.PropertyField(resolutionProp, new GUIContent("分割数（滑らかさ）"));
                EditorGUILayout.PropertyField(uvTilingProp,   new GUIContent("UVタイリング"));

                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.ApplyModifiedProperties();
                    generator.RebuildMesh();
                }

                GUILayout.Space(8);

                if (GUILayout.Button(" メッシュを再生成 ", CableGeneratorTheme.ActionButtonStyle))
                {
                    Undo.RecordObject(generator, "Rebuild Cable Mesh");
                    generator.RebuildMesh();
                    EditorUtility.SetDirty(generator);
                }
            });

            // ---- スプラインの基本構成 ----
            DrawFoldableSection("スプラインの基本構成", ref s_foldSplineSetup, () =>
            {
                EditorGUILayout.HelpBox(
                    "2点選択機能を使うには、対象メッシュにコライダーが必要です（MeshCollider 推奨）。\n" +
                    "Box / Capsule / Sphere コライダーでは法線方向がずれる場合があります。",
                    MessageType.Warning);

                GUILayout.Space(6);

                EditorGUI.BeginChangeCheck();
                bool sliding1 = DrawRateSlider("ハンドルの長さ", "Len_TangentScale", ref s_tangentScale);
                if (EditorGUI.EndChangeCheck() || sliding1)
                    ApplyTangentScaleToSpline(generator, s_tangentScale);

                GUILayout.Space(6);

                bool isMyTarget = s_pickingTarget == generator;

                if (!isMyTarget)
                {
                    using (new EditorGUI.DisabledScope(s_pickingTarget != null))
                    {
                        if (GUILayout.Button("2点選択でSplineを設定", CableGeneratorTheme.SecondaryButtonStyle))
                            StartPickingMode(generator);
                    }

                    if (s_pickingTarget != null)
                        GUILayout.Label("別のオブジェクトで選択中です。", CableGeneratorTheme.CaptionStyle);
                }
                else
                {
                    string hint = s_pickCount == 0
                        ? "シーンで 1点目 をクリックしてください"
                        : "シーンで 2点目 をクリックしてください";

                    EditorGUILayout.HelpBox(hint, MessageType.Info);

                    if (s_pickCount > 0)
                        GUILayout.Label(
                            $"1点目:  座標 {s_pickedPoints[0]:F3}  /  法線 {s_pickedNormals[0]:F2}",
                            CableGeneratorTheme.CaptionStyle);

                    GUILayout.Space(4);

                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("やり直し", CableGeneratorTheme.SecondaryButtonStyle))
                        s_pickCount = 0;
                    if (GUILayout.Button("キャンセル", CableGeneratorTheme.SecondaryButtonStyle))
                        CancelPickingMode();
                    EditorGUILayout.EndHorizontal();
                }
            });

            // ---- ノットの細分化・等分 ----
            DrawFoldableSection("ノットの細分化・等分", ref s_foldKnotSubdivision, () =>
            {
                s_addedKnotMode        = (TangentMode)EditorGUILayout.EnumPopup("追加ノットモード", s_addedKnotMode);
                s_initialDivisionCount = Mathf.Max(1, EditorGUILayout.IntField("始点-終点 分割数", s_initialDivisionCount));

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("始点-終点を等分してノット再配置", CableGeneratorTheme.SecondaryButtonStyle))
                    RedistributeKnotsBetweenEndpoints(generator, s_initialDivisionCount, s_addedKnotMode);
                if (GUILayout.Button("全区間を細分化してノット追加", CableGeneratorTheme.SecondaryButtonStyle))
                    SubdivideSplineKnots(generator, s_addedKnotMode);
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(s_knotInitLastResult))
                    GUILayout.Label(s_knotInitLastResult, CableGeneratorTheme.CaptionStyle);
            });

            // ---- ノット投影 ----
            DrawFoldableSection("ノット投影", ref s_foldKnotProjection, () =>
            {
                EditorGUI.BeginChangeCheck();
                s_snapKnotIndex = Mathf.Max(0, EditorGUILayout.IntField("対象ノット Index", s_snapKnotIndex));
                if (EditorGUI.EndChangeCheck())
                    s_selectedKnotIndices.Clear();

                if (s_selectedKnotIndices.Count > 0)
                {
                    var sorted = new List<int>(s_selectedKnotIndices);
                    sorted.Sort();
                    GUILayout.Label($"複数選択中: {string.Join(", ", sorted)}", CableGeneratorTheme.CaptionStyle);
                }
                else
                {
                    GUILayout.Label("Shiftキー+クリックで複数選択", CableGeneratorTheme.CaptionStyle);
                }

                s_snapDirection        = EditorGUILayout.Vector3Field("投影方向", s_snapDirection);
                s_snapDirectionIsLocal = EditorGUILayout.Toggle("方向をローカル扱い", s_snapDirectionIsLocal);
                s_snapMaxDistance      = Mathf.Max(0f, EditorGUILayout.FloatField("最大距離", s_snapMaxDistance));
                s_snapSurfaceOffset    = Mathf.Max(0f, EditorGUILayout.FloatField("面オフセット", s_snapSurfaceOffset));
                s_snapLayerMask        = DrawLayerMaskField("LayerMask", s_snapLayerMask);

                GUILayout.Space(6);

                string snapButtonLabel = s_selectedKnotIndices.Count > 0
                    ? $"選択した {s_selectedKnotIndices.Count} ノットを面へ投影"
                    : "指定ノットを面へ投影";
                if (GUILayout.Button(snapButtonLabel, CableGeneratorTheme.SecondaryButtonStyle))
                {
                    bool ok = SnapKnotInDirection(generator);
                    if (!ok && string.IsNullOrEmpty(s_snapLastResult))
                        s_snapLastResult = "投影に失敗しました。";
                }

                if (!string.IsNullOrEmpty(s_snapLastResult))
                {
                    GUILayout.Space(4);
                    GUILayout.Label(s_snapLastResult, CableGeneratorTheme.CaptionStyle);
                }
            });

            // ---- ケーブルたわみ設定 ----
            DrawFoldableSection("ケーブルたわみ設定", ref s_foldCableSag, () =>
            {
                if (GUILayout.Button("たわみノットを挿入", CableGeneratorTheme.SecondaryButtonStyle))
                    InsertSagKnots(generator);

                if (!string.IsNullOrEmpty(s_sagLastResult))
                {
                    GUILayout.Space(2);
                    GUILayout.Label(s_sagLastResult, CableGeneratorTheme.CaptionStyle);
                }

                GUILayout.Space(6);

                EditorGUI.BeginChangeCheck();
                s_sagDropDistance = EditorGUILayout.Slider("降下距離", s_sagDropDistance, 0f, 5f);
                if (EditorGUI.EndChangeCheck() && s_hasSagKnots)
                    UpdateSagKnots(generator, s_sagDropDistance, s_sagHandleLength);

                EditorGUI.BeginChangeCheck();
                s_sagUseMirrored = EditorGUILayout.Toggle("Mirrored ハンドル", s_sagUseMirrored);
                if (EditorGUI.EndChangeCheck() && s_hasSagKnots)
                    UpdateSagKnots(generator, s_sagDropDistance, s_sagHandleLength);

                EditorGUI.BeginDisabledGroup(!s_sagUseMirrored);
                EditorGUI.BeginChangeCheck();
                bool sliding2 = DrawRateSlider("ハンドルの長さ", "Len_SagHandle", ref s_sagHandleLength);
                if ((EditorGUI.EndChangeCheck() || sliding2) && s_hasSagKnots)
                    UpdateSagKnots(generator, s_sagDropDistance, s_sagHandleLength);
                EditorGUI.EndDisabledGroup();
            });

            // ---- アタッチメント ----
            var splineContainerForUI = generator.GetComponent<SplineContainer>();
            if (splineContainerForUI != null && splineContainerForUI.Splines.Count > 0)
            {
                DrawFoldableSection("アタッチメント", ref s_foldAttachments, () =>
                {
                    int knotCount = splineContainerForUI.Splines[0].Count;
                    for (int i = 0; i < knotCount; i++)
                    {
                        if (GUILayout.Button($"ノット {i} にアタッチメントを追加", CableGeneratorTheme.SecondaryButtonStyle))
                        {
                            var child = new GameObject($"Attachment_Knot{i}");
                            child.transform.SetParent(generator.transform);
                            var attachment = child.AddComponent<CableKnotAttachment>();
                            attachment.knotIndex = i;
                            Undo.RegisterCreatedObjectUndo(child, "Add Knot Attachment");
                            Selection.activeGameObject = child;
                        }
                        if (i < knotCount - 1) GUILayout.Space(2);
                    }
                });
            }

            // ---- エクスポート ----
            DrawFoldableSection("エクスポート", ref s_foldExport, () =>
            {
                var currentMesh = generator.GetComponent<MeshFilter>()?.sharedMesh;
                bool hasMesh = currentMesh != null && currentMesh.vertexCount > 0;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel("保存先フォルダ");
                bakeFolderPath = EditorGUILayout.TextField(bakeFolderPath);
                if (GUILayout.Button("...", CableGeneratorTheme.SecondaryButtonStyle, GUILayout.Width(28)))
                {
                    string selected = EditorUtility.OpenFolderPanel("保存先フォルダを選択", "Assets", "");
                    if (!string.IsNullOrEmpty(selected))
                    {
                        string dataPath = Application.dataPath.Replace("\\", "/");
                        selected = selected.Replace("\\", "/");
                        if (selected.StartsWith(dataPath))
                            bakeFolderPath = "Assets" + selected.Substring(dataPath.Length);
                        else
                            EditorUtility.DisplayDialog("エラー", "Assetsフォルダ内を選択してください。", "OK");
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (string.IsNullOrEmpty(bakeFolderPath))
                    GUILayout.Label($"未設定の場合: {CableMeshExporter.DefaultOutputFolder}", CableGeneratorTheme.CaptionStyle);

                GUILayout.Space(8);

                EditorGUI.BeginDisabledGroup(!hasMesh);
                if (GUILayout.Button("メッシュを保存 (.asset)", CableGeneratorTheme.SecondaryButtonStyle))
                {
                    string meshName      = generator.gameObject.name + "_cable";
                    string meshAssetPath = CableMeshExporter.SaveMeshAsset(currentMesh, meshName, bakeFolderPath);
                    if (!string.IsNullOrEmpty(meshAssetPath))
                        SetupBakedMeshObject(generator, meshAssetPath);
                }
                EditorGUI.EndDisabledGroup();
            });

            GUILayout.EndVertical();
        }

        // ================================================================
        //  UI Helpers
        // ================================================================

        void DrawSection(string title, System.Action content)
        {
            GUILayout.BeginVertical(CableGeneratorTheme.CardStyle);
            GUILayout.Label(title, CableGeneratorTheme.SectionHeaderStyle);

            var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, CableGeneratorTheme.Outline);
            EditorGUILayout.Space(4);

            content?.Invoke();
            GUILayout.EndVertical();
        }

        void DrawFoldableSection(string title, ref bool foldout, System.Action content)
        {
            GUILayout.BeginVertical(CableGeneratorTheme.CardStyle);

            string label = (foldout ? "▼  " : "▶  ") + title;
            if (GUILayout.Button(label, CableGeneratorTheme.SectionHeaderStyle))
            {
                foldout     = !foldout;
                GUI.changed = true;
            }

            if (foldout)
            {
                var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(rect, CableGeneratorTheme.Outline);
                EditorGUILayout.Space(4);
                content?.Invoke();
            }

            GUILayout.EndVertical();
        }
    }
}
