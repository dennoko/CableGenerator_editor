using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using CableGeneratorRuntime;

namespace CableGeneratorEditor
{
    public partial class CableGeneratorInspector
    {
        // ================================================================
        //  Bake Export
        // ================================================================

        static void SetupBakedMeshObject(CableGenerator generator, string meshAssetPath)
        {
            Mesh bakedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);
            if (bakedMesh == null)
            {
                EditorUtility.DisplayDialog("エラー", "保存したメッシュの読み込みに失敗しました。", "OK");
                return;
            }

            GameObject sourceObject    = generator.gameObject;
            Transform  sourceTransform = sourceObject.transform;
            Transform  sourceParent    = sourceTransform.parent;

            string bakedObjectName = GenerateUniqueGameObjectName(sourceParent, sourceObject.name + "_cable_baked");
            var    bakedObject     = new GameObject(bakedObjectName);
            Undo.RegisterCreatedObjectUndo(bakedObject, "Create Baked Cable Object");

            if (sourceParent != null)
            {
                bakedObject.transform.SetParent(sourceParent, false);
                bakedObject.transform.SetSiblingIndex(sourceTransform.GetSiblingIndex() + 1);
            }

            bakedObject.transform.localPosition = sourceTransform.localPosition;
            bakedObject.transform.localRotation = sourceTransform.localRotation;
            bakedObject.transform.localScale    = sourceTransform.localScale;

            var meshFilter        = Undo.AddComponent<MeshFilter>(bakedObject);
            meshFilter.sharedMesh = bakedMesh;

            var bakedRenderer    = Undo.AddComponent<MeshRenderer>(bakedObject);
            var sourceRenderer   = sourceObject.GetComponent<MeshRenderer>();
            if (sourceRenderer != null)
                bakedRenderer.sharedMaterials = sourceRenderer.sharedMaterials;

            // アタッチされたモデルをワールド座標を維持して子に配置
            var attachments = generator.GetComponentsInChildren<CableKnotAttachment>(true);
            foreach (var attachment in attachments)
            {
                if (attachment.SpawnedInstance == null) continue;

                Transform spawnedTransform = attachment.SpawnedInstance.transform;
                GameObject instanceCopy = Object.Instantiate(
                    attachment.SpawnedInstance,
                    spawnedTransform.position,
                    spawnedTransform.rotation);
                // ワールドスケールを明示的に保持（Instantiate は localScale をコピーするため）
                instanceCopy.transform.localScale = spawnedTransform.lossyScale;
                instanceCopy.name = attachment.SpawnedInstance.name;
                Undo.RegisterCreatedObjectUndo(instanceCopy, "Copy Attachment to Baked Cable");
                instanceCopy.transform.SetParent(bakedObject.transform, true);
            }

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

        static string GenerateUniqueGameObjectName(Transform parent, string baseName)
        {
            var usedNames = new System.Collections.Generic.HashSet<string>();

            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                    usedNames.Add(parent.GetChild(i).name);
            }
            else
            {
                foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                    usedNames.Add(root.name);
            }

            if (!usedNames.Contains(baseName))
                return baseName;

            int counter = 1;
            string candidate;
            do
            {
                candidate = $"{baseName} {counter}";
                counter++;
            }
            while (usedNames.Contains(candidate));

            return candidate;
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
