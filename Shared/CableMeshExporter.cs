using UnityEditor;
using UnityEngine;

namespace CableGeneratorEditor
{
    internal static class CableMeshExporter
    {
        public const string DefaultOutputFolder = "Assets/dennokoworks/CableGenerator/BakedMesh";

        /// <summary>
        /// メッシュを .asset ファイルとして保存する。
        /// </summary>
        /// <param name="sourceMesh">保存元のメッシュ</param>
        /// <param name="defaultName">デフォルトファイル名（拡張子なし）</param>
        /// <param name="outputFolder">保存先フォルダパス（Assets/...）。null または空の場合はデフォルトフォルダを使用</param>
        /// <returns>保存先パス。キャンセル時は null</returns>
        public static string SaveMeshAsset(Mesh sourceMesh, string defaultName, string outputFolder)
        {
            if (sourceMesh == null || sourceMesh.vertexCount == 0)
            {
                EditorUtility.DisplayDialog("エラー", "保存するメッシュがありません。先にメッシュを生成してください。", "OK");
                return null;
            }

            // 保存先フォルダを決定
            string folder = string.IsNullOrEmpty(outputFolder) ? DefaultOutputFolder : outputFolder;

            // フォルダが存在しない場合は作成
            EnsureFolderExists(folder);

            string path = GenerateUniqueAssetPath(folder, defaultName);
            string actualName = System.IO.Path.GetFileNameWithoutExtension(path);

            // メッシュをディープコピーして保存
            Mesh copy = Object.Instantiate(sourceMesh);
            copy.name = actualName;

            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();

            EditorGUIUtility.PingObject(copy);
            Debug.Log($"メッシュを保存しました: {path}");

            return path;
        }

        static string GenerateUniqueAssetPath(string folder, string baseName)
        {
            string path = $"{folder}/{baseName}.asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(path) == null)
                return path;

            int counter = 1;
            do
            {
                path = $"{folder}/{baseName} {counter}.asset";
                counter++;
            }
            while (AssetDatabase.LoadAssetAtPath<Object>(path) != null);

            return path;
        }

        static void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            // パスを分割して順番にフォルダを作成
            string[] parts = folderPath.Replace("\\", "/").Split('/');
            string current = parts[0]; // "Assets"

            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
