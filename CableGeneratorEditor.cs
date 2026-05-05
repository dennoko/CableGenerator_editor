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
    public class CableGeneratorInspector : Editor
    {
        // ---- Serialized Properties ----
        SerializedProperty profileProp;
        SerializedProperty resolutionProp;
        SerializedProperty uvTilingProp;

        string bakeFolderPath = "";

        // ---- Picking Mode (static: 複数インスペクタ間で共有) ----
        static CableGenerator s_pickingTarget = null;
        static int            s_pickCount     = 0;
        static Vector3[]      s_pickedPoints  = new Vector3[2];
        static Vector3[]      s_pickedNormals = new Vector3[2];
        static float          s_tangentScale  = 1f;

        // ---- Knot Projection Settings ----
        static int       s_snapKnotIndex        = 0;
        static readonly HashSet<int> s_selectedKnotIndices = new HashSet<int>();
        static Vector3   s_snapDirection        = Vector3.down;
        static bool      s_snapDirectionIsLocal = false;
        static float     s_snapMaxDistance      = 10f;
        static float     s_snapSurfaceOffset    = 0.003f;
        static LayerMask s_snapLayerMask        = ~0;
        static string    s_snapLastResult       = string.Empty;
        static TangentMode s_addedKnotMode      = TangentMode.AutoSmooth;
        static int         s_initialDivisionCount = 4;
        static string      s_knotInitLastResult = string.Empty;

        // ---- Section Fold States (デフォルト折りたたみ) ----
        static bool s_foldSplineSetup    = false;
        static bool s_foldKnotProjection = false;
        static bool s_foldAttachments    = false;
        static bool s_foldExport         = false;

        const float       kVectorEpsilon         = 0.000001f;
        const float       kVectorEpsilonSqr      = kVectorEpsilon * kVectorEpsilon;
        const float       kMidpointTangentDivisor = 6f;
        const float       kLinearTangentDivisor  = 3f;

        void OnEnable()
        {
            profileProp    = serializedObject.FindProperty("profile");
            resolutionProp = serializedObject.FindProperty("resolution");
            uvTilingProp   = serializedObject.FindProperty("uvTiling");
        }

        public override void OnInspectorGUI()
        {
            CableGeneratorTheme.Initialize();

            serializedObject.Update();
            var generator = (CableGenerator)target;

            // 全体を垂直レイアウトで囲む
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

            // ---- スプライン設定 ----
            DrawFoldableSection("スプライン設定", ref s_foldSplineSetup, () =>
            {
                EditorGUILayout.HelpBox(
                    "2点選択機能を使うには、対象メッシュにコライダーが必要です（MeshCollider 推奨）。\n" +
                    "Box / Capsule / Sphere コライダーでは法線方向がずれる場合があります。",
                    MessageType.Warning);

                GUILayout.Space(6);

                EditorGUI.BeginChangeCheck();
                float newScale = EditorGUILayout.Slider("ハンドル強さ", s_tangentScale, 0f, 2f);
                if (EditorGUI.EndChangeCheck())
                {
                    s_tangentScale = newScale;
                    ApplyTangentScaleToSpline(generator, s_tangentScale);
                }

                GUILayout.Space(6);

                EditorGUILayout.LabelField("初期ノット設定", EditorStyles.boldLabel);
                s_addedKnotMode = (TangentMode)EditorGUILayout.EnumPopup("追加ノットモード", s_addedKnotMode);
                s_initialDivisionCount = Mathf.Max(1, EditorGUILayout.IntField("始点-終点 分割数", s_initialDivisionCount));

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("始点-終点を等分してノット再配置", CableGeneratorTheme.SecondaryButtonStyle))
                    RedistributeKnotsBetweenEndpoints(generator, s_initialDivisionCount, s_addedKnotMode);
                if (GUILayout.Button("全区間を細分化してノット追加", CableGeneratorTheme.SecondaryButtonStyle))
                    SubdivideSplineKnots(generator, s_addedKnotMode);
                EditorGUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(s_knotInitLastResult))
                    GUILayout.Label(s_knotInitLastResult, CableGeneratorTheme.CaptionStyle);

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
                    string meshName = generator.gameObject.name + "_cable";
                    string meshAssetPath = CableMeshExporter.SaveMeshAsset(currentMesh, meshName, bakeFolderPath);
                    if (!string.IsNullOrEmpty(meshAssetPath))
                        SetupBakedMeshObject(generator, meshAssetPath);
                }
                EditorGUI.EndDisabledGroup();
            });

            GUILayout.EndVertical(); // InspectorRootStyle
        }

        private void DrawSection(string title, System.Action content)
        {
            GUILayout.BeginVertical(CableGeneratorTheme.CardStyle);
            GUILayout.Label(title, CableGeneratorTheme.SectionHeaderStyle);

            var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, CableGeneratorTheme.Outline);
            EditorGUILayout.Space(4);

            content?.Invoke();
            GUILayout.EndVertical();
        }

        private void DrawFoldableSection(string title, ref bool foldout, System.Action content)
        {
            GUILayout.BeginVertical(CableGeneratorTheme.CardStyle);

            string label = (foldout ? "▼  " : "▶  ") + title;
            if (GUILayout.Button(label, CableGeneratorTheme.SectionHeaderStyle))
            {
                foldout = !foldout;
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

        static void SetupBakedMeshObject(CableGenerator generator, string meshAssetPath)
        {
            Mesh bakedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);
            if (bakedMesh == null)
            {
                EditorUtility.DisplayDialog("エラー", "保存したメッシュの読み込みに失敗しました。", "OK");
                return;
            }

            GameObject sourceObject = generator.gameObject;
            Transform sourceTransform = sourceObject.transform;
            Transform sourceParent = sourceTransform.parent;

            string bakedObjectName = sourceObject.name + "_cable_baked";
            var bakedObject = new GameObject(bakedObjectName);
            Undo.RegisterCreatedObjectUndo(bakedObject, "Create Baked Cable Object");

            if (sourceParent != null)
            {
                bakedObject.transform.SetParent(sourceParent, false);
                bakedObject.transform.SetSiblingIndex(sourceTransform.GetSiblingIndex() + 1);
            }

            bakedObject.transform.localPosition = sourceTransform.localPosition;
            bakedObject.transform.localRotation = sourceTransform.localRotation;
            bakedObject.transform.localScale = sourceTransform.localScale;

            var meshFilter = Undo.AddComponent<MeshFilter>(bakedObject);
            meshFilter.sharedMesh = bakedMesh;

            var bakedRenderer = Undo.AddComponent<MeshRenderer>(bakedObject);
            var sourceRenderer = sourceObject.GetComponent<MeshRenderer>();
            if (sourceRenderer != null)
                bakedRenderer.sharedMaterials = sourceRenderer.sharedMaterials;

            Undo.RecordObject(sourceObject, "Disable Original Cable Object");
            bool editorOnlyTagExists = System.Array.IndexOf(InternalEditorUtility.tags, "EditorOnly") >= 0;
            if (editorOnlyTagExists)
                sourceObject.tag = "EditorOnly";
            else
                Debug.LogWarning("EditorOnly タグが見つからないため、タグ設定をスキップしました。");
            sourceObject.SetActive(false);

            EditorUtility.SetDirty(sourceObject);
            EditorUtility.SetDirty(bakedObject);
            Selection.activeGameObject = bakedObject;
        }

        // ================================================================
        //  Scene GUI
        // ================================================================

        void OnSceneGUI()
        {
            var gen = (CableGenerator)target;
            var splineContainer = gen.GetComponent<SplineContainer>();
            if (splineContainer == null || splineContainer.Splines.Count == 0) return;

            // ---- Picking mode (2点選択) が優先 ----
            if (s_pickingTarget == gen)
            {
                int controlID = GUIUtility.GetControlID(FocusType.Passive);
                HandleUtility.AddDefaultControl(controlID);

                DrawPickedPoints();
                DrawSceneHintLabel();

                Event e = Event.current;

                if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
                {
                    Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                    if (Physics.Raycast(ray, out RaycastHit hit))
                    {
                        s_pickedPoints[s_pickCount]  = hit.point;
                        s_pickedNormals[s_pickCount] = hit.normal;
                        s_pickCount++;

                        if (s_pickCount >= 2)
                        {
                            ApplySplineFromPoints(gen,
                                s_pickedPoints[0], s_pickedNormals[0],
                                s_pickedPoints[1], s_pickedNormals[1]);
                            CancelPickingMode();
                        }

                        e.Use();
                        Repaint();
                        SceneView.RepaintAll();
                    }
                }

                if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
                {
                    CancelPickingMode();
                    e.Use();
                }

                SceneView.RepaintAll();
                return;
            }

            // ---- 通常のスプライン編集ハンドル ----
            var spline = splineContainer.Splines[0];
            Transform transform = splineContainer.transform;

            for (int i = 0; i < spline.Count; i++)
            {
                var knot = spline[i];
                Vector3 worldPos = transform.TransformPoint((Vector3)(float3)knot.Position);

                float handleSize = HandleUtility.GetHandleSize(worldPos) * 0.1f;

                // PositionHandle: ドラッグで移動
                Handles.color = GetKnotColor(spline.GetTangentMode(i));
                EditorGUI.BeginChangeCheck();
                Vector3 newWorldPos = Handles.PositionHandle(worldPos, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(splineContainer, "Move Cable Control Point");
                    Vector3 localPos = transform.InverseTransformPoint(newWorldPos);
                    knot.Position = (float3)localPos;
                    spline.SetKnot(i, knot);
                    EditorUtility.SetDirty(splineContainer);
                    gen.RebuildMesh();
                }

                // 選択スフィア: クリックで単体選択、Shift+クリックで複数選択
                bool isSelected = i == s_snapKnotIndex || s_selectedKnotIndices.Contains(i);
                Handles.color = isSelected
                    ? (i == s_snapKnotIndex ? Color.white : new Color(1f, 0.9f, 0.2f))
                    : GetKnotColor(spline.GetTangentMode(i));

                bool shiftHeld = Event.current.shift;
                if (Handles.Button(worldPos, Quaternion.identity, handleSize * 2f, handleSize * 2.5f, Handles.SphereHandleCap))
                {
                    if (shiftHeld)
                    {
                        if (s_selectedKnotIndices.Contains(i))
                            s_selectedKnotIndices.Remove(i);
                        else
                            s_selectedKnotIndices.Add(i);
                    }
                    else
                    {
                        s_selectedKnotIndices.Clear();
                        s_snapKnotIndex = i;
                    }
                    Repaint();
                    SceneView.RepaintAll();
                }

                DrawTangentHandle(spline, splineContainer, transform, i, knot, gen, true);
                DrawTangentHandle(spline, splineContainer, transform, i, knot, gen, false);
            }

            DrawSplinePreview(spline, transform);
        }

        // ================================================================
        //  Spline Scene Handles
        // ================================================================

        void DrawTangentHandle(Spline spline, SplineContainer container, Transform transform,
            int knotIndex, BezierKnot knot, CableGenerator gen, bool isIn)
        {
            float3 tangent = isIn ? knot.TangentIn : knot.TangentOut;
            Vector3 knotWorld = transform.TransformPoint((Vector3)(float3)knot.Position);

            float3 rotatedTangent = math.rotate(knot.Rotation, tangent);
            Vector3 tangentWorld = knotWorld + transform.TransformDirection((Vector3)rotatedTangent);

            float handleSize = HandleUtility.GetHandleSize(tangentWorld) * 0.06f;

            Handles.color = isIn ? new Color(0.2f, 0.6f, 1f, 0.8f) : new Color(1f, 0.6f, 0.2f, 0.8f);
            Handles.DrawLine(knotWorld, tangentWorld);

            EditorGUI.BeginChangeCheck();
            Vector3 newTangentWorld = Handles.FreeMoveHandle(
                tangentWorld, handleSize, Vector3.zero, Handles.SphereHandleCap);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(container, "Move Cable Tangent");

                Vector3 newLocalDir = transform.InverseTransformDirection(newTangentWorld - knotWorld);
                float3 newTangent = math.rotate(math.inverse(knot.Rotation), (float3)newLocalDir);

                if (isIn) knot.TangentIn  = newTangent;
                else      knot.TangentOut = newTangent;

                spline.SetKnot(knotIndex, knot);
                EditorUtility.SetDirty(container);
                gen.RebuildMesh();
            }
        }

        void DrawSplinePreview(Spline spline, Transform transform)
        {
            Handles.color = new Color(1f, 1f, 0f, 0.5f);
            int previewSteps = 64;
            Vector3 prevPoint = Vector3.zero;

            for (int i = 0; i <= previewSteps; i++)
            {
                float t = (float)i / previewSteps;
                SplineUtility.Evaluate(spline, t, out float3 pos, out float3 tangent, out float3 up);
                Vector3 worldPos = transform.TransformPoint((Vector3)pos);

                if (i > 0) Handles.DrawLine(prevPoint, worldPos);
                prevPoint = worldPos;
            }
        }

        Color GetKnotColor(TangentMode mode)
        {
            switch (mode)
            {
                case TangentMode.Mirrored:   return Color.green;
                case TangentMode.Continuous: return Color.yellow;
                case TangentMode.Broken:     return Color.red;
                case TangentMode.AutoSmooth: return Color.cyan;
                default:                     return Color.white;
            }
        }

        // ================================================================
        //  Picking Mode
        // ================================================================

        void DrawPickedPoints()
        {
            for (int i = 0; i < s_pickCount; i++)
            {
                Handles.color = i == 0 ? new Color(0.2f, 1f, 0.3f) : new Color(0.3f, 0.6f, 1f);

                float size = HandleUtility.GetHandleSize(s_pickedPoints[i]) * 0.07f;
                Handles.SphereHandleCap(0, s_pickedPoints[i], Quaternion.identity, size, EventType.Repaint);

                Vector3 normalEnd = s_pickedPoints[i] + s_pickedNormals[i] * size * 4f;
                Handles.DrawLine(s_pickedPoints[i], normalEnd, 2f);
                Handles.ArrowHandleCap(0, s_pickedPoints[i],
                    Quaternion.LookRotation(s_pickedNormals[i]),
                    size * 2.5f, EventType.Repaint);
            }
        }

        static void DrawSceneHintLabel()
        {
            Handles.BeginGUI();
            string msg = s_pickCount == 0
                ? "[ Spline設定 ]  1点目を選択   Esc: キャンセル"
                : "[ Spline設定 ]  2点目を選択   Esc: キャンセル";

            GUIStyle style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize  = 12,
            };
            Vector2 size = style.CalcSize(new GUIContent(msg));
            float x = (Screen.width - size.x) * 0.5f;
            GUI.Box(new Rect(x, 8, size.x + 16, size.y + 8), msg, style);
            Handles.EndGUI();
        }

        static void StartPickingMode(CableGenerator target)
        {
            s_pickingTarget = target;
            s_pickCount     = 0;
            SceneView.RepaintAll();
        }

        static void CancelPickingMode()
        {
            s_pickingTarget = null;
            s_pickCount     = 0;
            SceneView.RepaintAll();
        }

        // ================================================================
        //  Knot Projection
        // ================================================================

        static LayerMask DrawLayerMaskField(string label, LayerMask selected)
        {
            string[] layerNames   = InternalEditorUtility.layers;
            int[]    layerNumbers = new int[layerNames.Length];

            for (int i = 0; i < layerNames.Length; i++)
                layerNumbers[i] = LayerMask.NameToLayer(layerNames[i]);

            int compressedMask = 0;
            for (int i = 0; i < layerNumbers.Length; i++)
                if ((selected.value & (1 << layerNumbers[i])) != 0)
                    compressedMask |= 1 << i;

            compressedMask = EditorGUILayout.MaskField(label, compressedMask, layerNames);

            int finalMask = 0;
            for (int i = 0; i < layerNumbers.Length; i++)
                if ((compressedMask & (1 << i)) != 0)
                    finalMask |= 1 << layerNumbers[i];

            selected.value = finalMask;
            return selected;
        }

        static bool SnapKnotInDirection(CableGenerator cableGen)
        {
            s_snapLastResult = string.Empty;

            var splineContainer = cableGen.GetComponent<SplineContainer>();
            if (splineContainer == null || splineContainer.Splines.Count == 0)
            {
                s_snapLastResult = "SplineContainer が見つかりません。";
                return false;
            }

            var spline = splineContainer.Splines[0];
            int count  = spline.Count;

            if (count == 0)
            {
                s_snapLastResult = "ノットが存在しません。";
                return false;
            }

            // 複数選択があればそちらを使い、なければ単体インデックス
            List<int> targetIndices;
            if (s_selectedKnotIndices.Count > 0)
            {
                targetIndices = new List<int>(s_selectedKnotIndices);
                targetIndices.Sort();
            }
            else
            {
                targetIndices = new List<int> { s_snapKnotIndex };
            }

            foreach (int idx in targetIndices)
            {
                if (idx < 0 || idx >= count)
                {
                    s_snapLastResult = $"Index {idx} は範囲外です（0..{count - 1}）。";
                    return false;
                }
            }

            Vector3 dir = s_snapDirectionIsLocal
                ? splineContainer.transform.TransformDirection(s_snapDirection)
                : s_snapDirection;

            if (dir.sqrMagnitude < 0.000001f)
            {
                s_snapLastResult = "方向ベクトルがゼロです。";
                return false;
            }
            dir.Normalize();

            bool         closed    = spline.Closed;
            int          knotCount = spline.Count;
            BezierKnot[] knots     = new BezierKnot[knotCount];
            TangentMode[] modes    = new TangentMode[knotCount];

            for (int i = 0; i < knotCount; i++)
            {
                knots[i] = spline[i];
                modes[i] = spline.GetTangentMode(i);
            }

            Vector3 localDir = splineContainer.transform.InverseTransformDirection(dir).normalized;

            int successCount = 0;
            var failedIndices = new List<int>();

            foreach (int targetIndex in targetIndices)
            {
                Vector3 worldOrigin = splineContainer.transform.TransformPoint((Vector3)knots[targetIndex].Position);

                if (!Physics.Raycast(worldOrigin, dir, out RaycastHit hit,
                        s_snapMaxDistance, s_snapLayerMask.value, QueryTriggerInteraction.Ignore))
                {
                    failedIndices.Add(targetIndex);
                    continue;
                }

                Vector3 worldTargetPos = hit.point + hit.normal * s_snapSurfaceOffset;
                Vector3 localTargetPos = splineContainer.transform.InverseTransformPoint(worldTargetPos);
                Vector3 localHitNormal = splineContainer.transform.InverseTransformDirection(hit.normal).normalized;

                var        oldKnot    = knots[targetIndex];
                quaternion snappedRot = AlignForwardToPlaneNoNormalTwist(oldKnot.Rotation, localHitNormal, localDir);
                knots[targetIndex] = new BezierKnot(
                    (float3)localTargetPos,
                    oldKnot.TangentIn,
                    oldKnot.TangentOut,
                    snappedRot);

                successCount++;
            }

            if (successCount == 0)
            {
                s_snapLastResult = "すべてのノットで投影に失敗しました。";
                return false;
            }

            Undo.RecordObject(splineContainer, "Snap Knots To Surface");
            spline.Clear();
            spline.Closed = closed;

            for (int i = 0; i < knotCount; i++)
                spline.Add(knots[i], modes[i]);

            EditorUtility.SetDirty(splineContainer);
            SceneView.RepaintAll();

            if (failedIndices.Count > 0)
            {
                string failedStr = string.Join(", ", failedIndices);
                s_snapLastResult = $"{successCount} ノットを投影。ヒットなし: [{failedStr}]";
            }
            else if (targetIndices.Count == 1)
            {
                s_snapLastResult = $"Knot {targetIndices[0]} をヒット位置へ移動しました。";
            }
            else
            {
                string indexStr = string.Join(", ", targetIndices);
                s_snapLastResult = $"Knot [{indexStr}] をヒット位置へ移動しました。";
            }
            return true;
        }

        // ================================================================
        //  Tangent Scale
        // ================================================================

        static void ApplyTangentScaleToSpline(CableGenerator cableGen, float scale)
        {
            var splineContainer = cableGen.GetComponent<SplineContainer>();
            if (splineContainer == null || splineContainer.Splines.Count == 0) return;

            var spline = splineContainer.Splines[0];
            int count  = spline.Count;
            if (count == 0) return;

            bool        closed    = spline.Closed;
            Vector3[]   positions = new Vector3[count];
            quaternion[] rotations = new quaternion[count];
            float3[]    prevIn    = new float3[count];
            float3[]    prevOut   = new float3[count];
            TangentMode[] modes   = new TangentMode[count];

            for (int i = 0; i < count; i++)
            {
                var k = spline[i];
                positions[i] = (Vector3)k.Position;
                rotations[i] = k.Rotation;
                prevIn[i]    = k.TangentIn;
                prevOut[i]   = k.TangentOut;
                modes[i]     = spline.GetTangentMode(i);
            }

            float[] baseLens = new float[count];
            for (int i = 0; i < count; i++)
            {
                float dPrev = 0f, dNext = 0f;
                if (closed)
                {
                    int prev = (i - 1 + count) % count;
                    int next = (i + 1) % count;
                    dPrev = Vector3.Distance(positions[prev], positions[i]);
                    dNext = Vector3.Distance(positions[i], positions[next]);
                    baseLens[i] = ((dPrev + dNext) * 0.5f) / 3f * scale;
                }
                else
                {
                    if (i > 0)         dPrev = Vector3.Distance(positions[i - 1], positions[i]);
                    if (i < count - 1) dNext = Vector3.Distance(positions[i],     positions[i + 1]);

                    float avg = (i > 0 && i < count - 1)
                        ? (dPrev + dNext) * 0.5f
                        : (dPrev > 0f ? dPrev : dNext);

                    baseLens[i] = (avg / 3f) * scale;
                }
            }

            Undo.RecordObject(splineContainer, "Adjust Spline Tangent Strength");
            spline.Clear();
            spline.Closed = closed;

            for (int i = 0; i < count; i++)
            {
                float len = baseLens[i];
                float3 inT, outT;

                if (modes[i] != TangentMode.Broken)
                {
                    inT  = prevIn[i];
                    outT = prevOut[i];
                }
                else
                {
                    inT  = new float3(0f, 0f, -len);
                    outT = new float3(0f, 0f,  len);
                }

                spline.Add(new BezierKnot((float3)positions[i], inT, outT, rotations[i]), modes[i]);
            }

            EditorUtility.SetDirty(splineContainer);
        }

        // ================================================================
        //  Knot Initialization
        // ================================================================

        static bool SubdivideSplineKnots(CableGenerator cableGen, TangentMode addedKnotMode)
        {
            s_knotInitLastResult = string.Empty;

            var splineContainer = cableGen.GetComponent<SplineContainer>();
            if (splineContainer == null || splineContainer.Splines.Count == 0)
            {
                s_knotInitLastResult = "SplineContainer が見つかりません。";
                return false;
            }

            var spline = splineContainer.Splines[0];
            int knotCount = spline.Count;
            if (knotCount < 2)
            {
                s_knotInitLastResult = "細分化には最低2つのノットが必要です。";
                return false;
            }

            bool closed = spline.Closed;
            int curveCount = closed ? knotCount : knotCount - 1;

            var knots = new BezierKnot[knotCount];
            var modes = new TangentMode[knotCount];
            var inserted = new BezierKnot[curveCount];

            for (int i = 0; i < knotCount; i++)
            {
                knots[i] = spline[i];
                modes[i] = spline.GetTangentMode(i);
            }

            for (int i = 0; i < curveCount; i++)
            {
                EvaluateCurveMidpoint(knots[i], knots[(i + 1) % knotCount], out float3 pos, out float3 tan, out float3 up);

                Vector3 tangent = ((Vector3)tan).normalized;
                if (tangent.sqrMagnitude < kVectorEpsilonSqr)
                    tangent = Vector3.forward;

                quaternion rot = SafeLookRotation(tangent, (Vector3)up);
                Vector3 start = (Vector3)knots[i].Position;
                Vector3 end = (Vector3)knots[(i + 1) % knotCount].Position;
                float len = Vector3.Distance(start, end) / kMidpointTangentDivisor;

                inserted[i] = new BezierKnot(
                    pos,
                    new float3(0f, 0f, -len),
                    new float3(0f, 0f,  len),
                    rot);
            }

            Undo.RecordObject(splineContainer, "Subdivide Cable Knots");
            spline.Clear();
            spline.Closed = closed;

            for (int i = 0; i < knotCount; i++)
            {
                spline.Add(knots[i], modes[i]);

                bool hasInsertion = closed || i < knotCount - 1;
                if (hasInsertion)
                    spline.Add(inserted[i], addedKnotMode);
            }

            EditorUtility.SetDirty(splineContainer);
            cableGen.RebuildMesh();
            SceneView.RepaintAll();

            s_knotInitLastResult = $"ノットを細分化しました（{knotCount} → {spline.Count}）。";
            return true;
        }

        static bool RedistributeKnotsBetweenEndpoints(CableGenerator cableGen, int divisionCount, TangentMode addedKnotMode)
        {
            s_knotInitLastResult = string.Empty;

            var splineContainer = cableGen.GetComponent<SplineContainer>();
            if (splineContainer == null || splineContainer.Splines.Count == 0)
            {
                s_knotInitLastResult = "SplineContainer が見つかりません。";
                return false;
            }

            var spline = splineContainer.Splines[0];
            int knotCount = spline.Count;
            if (knotCount < 2)
            {
                s_knotInitLastResult = "ノットが不足しています。";
                return false;
            }
            if (spline.Closed)
            {
                s_knotInitLastResult = "閉じたSplineでは始点-終点の等分は使用できません。";
                return false;
            }

            int targetKnotCount = Mathf.Max(2, divisionCount + 1);

            var first = spline[0];
            var last = spline[knotCount - 1];
            TangentMode firstMode = spline.GetTangentMode(0);
            TangentMode lastMode  = spline.GetTangentMode(knotCount - 1);
            Vector3 start = (Vector3)first.Position;
            Vector3 end = (Vector3)last.Position;
            Vector3 dir = end - start;
            float totalLength = dir.magnitude;

            Vector3 forward = totalLength > kVectorEpsilon ? dir / totalLength : Vector3.forward;
            quaternion interiorRotation = SafeLookRotation(forward);
            float tangentLen = totalLength / Mathf.Max(1, divisionCount) / kLinearTangentDivisor;

            Undo.RecordObject(splineContainer, "Redistribute Cable Knots");
            spline.Clear();
            spline.Closed = false;

            spline.Add(first, firstMode);

            for (int i = 1; i < targetKnotCount - 1; i++)
            {
                float t = (float)i / (targetKnotCount - 1);
                float3 pos = (float3)Vector3.Lerp(start, end, t);

                var knot = new BezierKnot(
                    pos,
                    new float3(0f, 0f, -tangentLen),
                    new float3(0f, 0f,  tangentLen),
                    interiorRotation);

                spline.Add(knot, addedKnotMode);
            }

            spline.Add(last, lastMode);

            EditorUtility.SetDirty(splineContainer);
            cableGen.RebuildMesh();
            SceneView.RepaintAll();

            s_knotInitLastResult = $"始点-終点を{divisionCount}分割で再配置しました（{targetKnotCount}ノット）。";
            return true;
        }

        static void EvaluateCurveMidpoint(BezierKnot startKnot, BezierKnot endKnot, out float3 pos, out float3 tan, out float3 up)
        {
            float3 p0 = startKnot.Position;
            float3 p1 = startKnot.Position + math.rotate(startKnot.Rotation, startKnot.TangentOut);
            float3 p2 = endKnot.Position + math.rotate(endKnot.Rotation, endKnot.TangentIn);
            float3 p3 = endKnot.Position;

            const float t = 0.5f;
            const float omt = 1f - t;

            pos = (omt * omt * omt) * p0
                + (3f * omt * omt * t) * p1
                + (3f * omt * t * t) * p2
                + (t * t * t) * p3;

            tan = (3f * omt * omt) * (p1 - p0)
                + (6f * omt * t) * (p2 - p1)
                + (3f * t * t) * (p3 - p2);

            quaternion midRot = math.slerp(startKnot.Rotation, endKnot.Rotation, t);
            up = math.rotate(midRot, new float3(0f, 1f, 0f));
        }

        // ================================================================
        //  Spline from Surface Points
        // ================================================================

        static void ApplySplineFromPoints(
            CableGenerator cableGen,
            Vector3 worldA, Vector3 normalA,
            Vector3 worldB, Vector3 normalB)
        {
            var splineContainer = cableGen.GetComponent<SplineContainer>();
            if (splineContainer == null || splineContainer.Splines.Count == 0) return;

            Transform tf = splineContainer.transform;

            Vector3 localA       = tf.InverseTransformPoint(worldA);
            Vector3 localB       = tf.InverseTransformPoint(worldB);
            Vector3 localNormalA = tf.InverseTransformDirection(normalA).normalized;
            Vector3 localNormalB = tf.InverseTransformDirection(normalB).normalized;

            float tangentLen = Vector3.Distance(localA, localB) / 3f * s_tangentScale;

            quaternion rotA = SafeLookRotation(localNormalA,  localNormalA);
            quaternion rotB = SafeLookRotation(-localNormalB, localNormalB);

            Undo.RecordObject(splineContainer, "Set Spline from Surface Points");

            var spline = splineContainer.Splines[0];
            spline.Clear();
            spline.Closed = false;
            spline.Add(new BezierKnot((float3)localA,
                new float3(0f, 0f, -tangentLen), new float3(0f, 0f, tangentLen), rotA), TangentMode.Broken);
            spline.Add(new BezierKnot((float3)localB,
                new float3(0f, 0f, -tangentLen), new float3(0f, 0f, tangentLen), rotB), TangentMode.Broken);

            EditorUtility.SetDirty(splineContainer);
        }

        // ================================================================
        //  Math Helpers
        // ================================================================

        static quaternion SafeLookRotation(Vector3 forward)
        {
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();
            Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.99f
                ? Vector3.forward : Vector3.up;
            return (quaternion)Quaternion.LookRotation(forward, up);
        }

        static quaternion SafeLookRotation(Vector3 forward, Vector3 upHint)
        {
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 up = upHint.sqrMagnitude < 0.0001f ? Vector3.up : upHint.normalized;
            if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.99f)
                up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.99f ? Vector3.right : Vector3.up;

            return (quaternion)Quaternion.LookRotation(forward, up);
        }

        static Vector3 SurfaceParallelDirection(Vector3 preferredDirection, Vector3 normal)
        {
            Vector3 n = normal.sqrMagnitude < 0.0001f ? Vector3.up : normal.normalized;
            Vector3 planar = Vector3.ProjectOnPlane(preferredDirection, n);

            if (planar.sqrMagnitude < 0.000001f)
            {
                planar = Vector3.Cross(n, Vector3.up);
                if (planar.sqrMagnitude < 0.000001f)
                    planar = Vector3.Cross(n, Vector3.right);
            }
            return planar.normalized;
        }

        static quaternion AlignForwardToPlaneNoNormalTwist(
            quaternion oldRotation, Vector3 planeNormal, Vector3 fallbackDirection)
        {
            Vector3 n          = planeNormal.sqrMagnitude < 0.0001f ? Vector3.up : planeNormal.normalized;
            Vector3 oldForward = math.rotate(oldRotation, new float3(0f, 0f, 1f));

            Vector3 planarForward = Vector3.ProjectOnPlane(oldForward, n);
            if (planarForward.sqrMagnitude < 0.000001f)
                planarForward = SurfaceParallelDirection(fallbackDirection, n);
            if (planarForward.sqrMagnitude < 0.000001f)
                return oldRotation;

            Quaternion delta  = Quaternion.FromToRotation(oldForward.normalized, planarForward.normalized);
            Quaternion result = delta * (Quaternion)oldRotation;
            return (quaternion)result;
        }

        // ================================================================
        //  Menu
        // ================================================================

        [MenuItem("GameObject/Cable Generator/Create Cable", false, 10)]
        static void CreateCableGenerator(MenuCommand menuCommand)
        {
            GameObject go = new GameObject("Cable");
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);

            var splineContainer = go.AddComponent<SplineContainer>();
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();

            var defaultMat = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Editor/CableGenerator/Material/default_cable.mat");
            renderer.sharedMaterial = defaultMat;

            var spline = splineContainer.Splines[0];
            spline.Clear();
            spline.Add(new BezierKnot(new float3(0, 0, 0), new float3(0, 0, -0.5f), new float3(0, 0, 0.5f)),
                TangentMode.Mirrored);
            spline.Add(new BezierKnot(new float3(0, 0, 2), new float3(0, 0, -0.5f), new float3(0, 0, 0.5f)),
                TangentMode.Mirrored);

            go.AddComponent<CableGenerator>();

            Undo.RegisterCreatedObjectUndo(go, "Create Cable Generator");
            Selection.activeGameObject = go;
        }
    }
}
