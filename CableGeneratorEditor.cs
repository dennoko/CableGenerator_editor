using UnityEditor;
using UnityEditor.Splines;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using CableGeneratorRuntime;

namespace CableGeneratorEditor
{
    [CustomEditor(typeof(CableGenerator))]
    public class CableGeneratorInspector : Editor
    {
        SerializedProperty profileProp;
        SerializedProperty resolutionProp;
        SerializedProperty uvTilingProp;

        string bakeFolderPath = "";

        void OnEnable()
        {
            profileProp = serializedObject.FindProperty("profile");
            resolutionProp = serializedObject.FindProperty("resolution");
            uvTilingProp = serializedObject.FindProperty("uvTiling");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var generator = (CableGenerator)target;

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.PropertyField(profileProp, new GUIContent("断面プロファイル"));
            EditorGUILayout.PropertyField(resolutionProp, new GUIContent("分割数（滑らかさ）"));
            EditorGUILayout.PropertyField(uvTilingProp, new GUIContent("UVタイリング"));

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                var gen = (CableGenerator)target;
                gen.RebuildMesh();
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("メッシュを再生成"))
            {
                var gen = (CableGenerator)target;
                Undo.RecordObject(gen, "Rebuild Cable Mesh");
                gen.RebuildMesh();
                EditorUtility.SetDirty(gen);
            }

            // --- メッシュ保存 ---
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("メッシュ保存", EditorStyles.boldLabel);

            var currentMesh = generator.GetComponent<MeshFilter>()?.sharedMesh;
            bool hasMesh = currentMesh != null && currentMesh.vertexCount > 0;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("保存先フォルダ");
            bakeFolderPath = EditorGUILayout.TextField(bakeFolderPath);
            if (GUILayout.Button("...", GUILayout.Width(24)))
            {
                string selected = EditorUtility.OpenFolderPanel("保存先フォルダを選択", "Assets", "");
                if (!string.IsNullOrEmpty(selected))
                {
                    // 絶対パスをAssets相対パスに変換
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
                EditorGUILayout.HelpBox($"未設定の場合: {CableMeshExporter.DefaultOutputFolder}", MessageType.None);

            EditorGUI.BeginDisabledGroup(!hasMesh);
            if (GUILayout.Button("メッシュを保存 (.asset)"))
            {
                string meshName = generator.gameObject.name + "_cable";
                CableMeshExporter.SaveMeshAsset(currentMesh, meshName, bakeFolderPath);
            }
            EditorGUI.EndDisabledGroup();

            // --- アタッチメント ---
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("アタッチメント", EditorStyles.boldLabel);

            var splineContainerForUI = generator.GetComponent<SplineContainer>();
            if (splineContainerForUI != null && splineContainerForUI.Splines.Count > 0)
            {
                int knotCount = splineContainerForUI.Splines[0].Count;
                for (int i = 0; i < knotCount; i++)
                {
                    if (GUILayout.Button($"ノット {i} にアタッチメントを追加"))
                    {
                        var child = new GameObject($"Attachment_Knot{i}");
                        child.transform.SetParent(generator.transform);
                        var attachment = child.AddComponent<CableKnotAttachment>();
                        attachment.knotIndex = i;
                        Undo.RegisterCreatedObjectUndo(child, "Add Knot Attachment");
                        Selection.activeGameObject = child;
                    }
                }
            }
        }

        void OnSceneGUI()
        {
            var gen = (CableGenerator)target;
            var splineContainer = gen.GetComponent<SplineContainer>();
            if (splineContainer == null || splineContainer.Splines.Count == 0) return;

            var spline = splineContainer.Splines[0];
            Transform transform = splineContainer.transform;

            // 制御点のカスタムハンドル表示
            for (int i = 0; i < spline.Count; i++)
            {
                var knot = spline[i];
                Vector3 worldPos = transform.TransformPoint((Vector3)(float3)knot.Position);

                // カメラ距離に依存しない固定サイズのハンドル
                float handleSize = HandleUtility.GetHandleSize(worldPos) * 0.1f;

                // 制御点の色（制約モードに応じて変更）
                Color knotColor = GetKnotColor(spline.GetTangentMode(i));
                Handles.color = knotColor;

                EditorGUI.BeginChangeCheck();
                Vector3 newWorldPos = Handles.PositionHandle(worldPos, Quaternion.identity);

                // 制御点の視覚的マーカー
                Handles.SphereHandleCap(0, worldPos, Quaternion.identity, handleSize * 2f, EventType.Repaint);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(splineContainer, "Move Cable Control Point");
                    Vector3 localPos = transform.InverseTransformPoint(newWorldPos);
                    knot.Position = (float3)localPos;
                    spline.SetKnot(i, knot);
                    EditorUtility.SetDirty(splineContainer);
                    gen.RebuildMesh();
                }

                // タンジェントハンドル
                DrawTangentHandle(spline, splineContainer, transform, i, knot, gen, true);  // TangentIn
                DrawTangentHandle(spline, splineContainer, transform, i, knot, gen, false); // TangentOut
            }

            // スプラインのプレビュー線を描画
            DrawSplinePreview(spline, transform);
        }

        void DrawTangentHandle(Spline spline, SplineContainer container, Transform transform,
            int knotIndex, BezierKnot knot, CableGenerator gen, bool isIn)
        {
            float3 tangent = isIn ? knot.TangentIn : knot.TangentOut;
            Vector3 knotWorld = transform.TransformPoint((Vector3)(float3)knot.Position);

            // タンジェントをノットのローカル回転で変換
            float3 rotatedTangent = math.rotate(knot.Rotation, tangent);
            Vector3 tangentWorld = knotWorld + transform.TransformDirection((Vector3)rotatedTangent);

            float handleSize = HandleUtility.GetHandleSize(tangentWorld) * 0.06f;

            // タンジェントの線とハンドル
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

                if (isIn)
                    knot.TangentIn = newTangent;
                else
                    knot.TangentOut = newTangent;

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

                if (i > 0)
                    Handles.DrawLine(prevPoint, worldPos);

                prevPoint = worldPos;
            }
        }

        Color GetKnotColor(TangentMode mode)
        {
            switch (mode)
            {
                case TangentMode.Mirrored:
                    return Color.green;
                case TangentMode.Continuous:
                    return Color.yellow;
                case TangentMode.Broken:
                    return Color.red;
                case TangentMode.AutoSmooth:
                    return Color.cyan;
                default:
                    return Color.white;
            }
        }

        [MenuItem("GameObject/Cable Generator/Create Cable", false, 10)]
        static void CreateCableGenerator(MenuCommand menuCommand)
        {
            GameObject go = new GameObject("Cable");
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);

            // 必要コンポーネントを追加
            var splineContainer = go.AddComponent<SplineContainer>();
            go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();

            // デフォルトマテリアル設定
            var defaultMat = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/Editor/CableGenerator/Material/default_cable.mat");
            renderer.sharedMaterial = defaultMat;

            // デフォルトスプラインを作成（始点と終点）
            var spline = splineContainer.Splines[0];
            spline.Clear();
            spline.Add(new BezierKnot(new float3(0, 0, 0), new float3(0, 0, -0.5f), new float3(0, 0, 0.5f)),
                TangentMode.Mirrored);
            spline.Add(new BezierKnot(new float3(0, 0, 2), new float3(0, 0, -0.5f), new float3(0, 0, 0.5f)),
                TangentMode.Mirrored);

            var gen = go.AddComponent<CableGenerator>();

            Undo.RegisterCreatedObjectUndo(go, "Create Cable Generator");
            Selection.activeGameObject = go;
        }
    }
}
