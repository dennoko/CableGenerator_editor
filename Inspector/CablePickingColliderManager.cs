using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CableGeneratorEditor
{
    /// <summary>
    /// 2点選択ピッキング時に一時的な MeshCollider を付与・削除するマネージャ。
    /// ドメインリロード後は SessionState でインスタンス ID を復元し、
    /// クラッシュ後は EditorPrefs のパスリストを使ってシーンオープン時にクリーンアップする。
    /// </summary>
    [InitializeOnLoad]
    public static class CablePickingColliderManager
    {
        const string kSessionKey = "CableGen_TempCol_IDs";
        const string kPrefsKey   = "CableGen_TempCol_Paths";
        const string kPrefsDirty = "CableGen_TempCol_Dirty";
        const int    kBatchSize  = 10;
        const int    kVertexMax  = 100_000;

        static readonly List<int>        s_ids     = new();
        static readonly List<MeshFilter> s_pending = new();
        static int s_pendingIndex;

        public static bool IsProcessing   => s_pending.Count > 0;
        public static int  TotalCount     => s_pending.Count;
        public static int  ProcessedCount => s_pendingIndex;
        public static int  AttachedCount  => s_ids.Count;

        static CablePickingColliderManager()
        {
            RestoreFromSession();
            EditorSceneManager.sceneOpened += (_, _) => RecoverFromCrash();
            EditorApplication.quitting     += Detach;
        }

        // ----------------------------------------------------------------
        //  ドメインリロード後の復元
        // ----------------------------------------------------------------

        static void RestoreFromSession()
        {
            s_ids.Clear();
            string raw = SessionState.GetString(kSessionKey, "");
            if (string.IsNullOrEmpty(raw)) return;
            foreach (var part in raw.Split(','))
                if (int.TryParse(part, out int id)) s_ids.Add(id);
        }

        // ----------------------------------------------------------------
        //  クラッシュリカバリー（sceneOpened 時）
        // ----------------------------------------------------------------

        static void RecoverFromCrash()
        {
            if (!EditorPrefs.GetBool(kPrefsDirty, false)) return;

            string raw = EditorPrefs.GetString(kPrefsKey, "");
            if (!string.IsNullOrEmpty(raw))
            {
                foreach (var path in raw.Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    var go = GameObject.Find(path);
                    if (go == null) continue;
                    var col = go.GetComponent<MeshCollider>();
                    if (col != null) Object.DestroyImmediate(col);
                }
            }
            ClearPrefs();
        }

        // ----------------------------------------------------------------
        //  Public API
        // ----------------------------------------------------------------

        public static void Attach()
        {
            if (IsProcessing) return;

            // 既存の一時コライダーを先に削除してから再付与
            if (s_ids.Count > 0) DetachInternal();

            s_pending.Clear();
            foreach (var mf in Object.FindObjectsOfType<MeshFilter>(false))
            {
                if (IsEligible(mf)) s_pending.Add(mf);
            }

            // シーンカメラに近い順から優先的に処理
            var cam = SceneView.lastActiveSceneView?.camera;
            if (cam != null)
            {
                Vector3 camPos = cam.transform.position;
                s_pending.Sort((a, b) =>
                    (a.transform.position - camPos).sqrMagnitude
                    .CompareTo((b.transform.position - camPos).sqrMagnitude));
            }

            s_pendingIndex = 0;
            EditorPrefs.SetBool(kPrefsDirty, true);
            EditorApplication.update += ProcessBatch;
        }

        public static void Detach()
        {
            EditorApplication.update -= ProcessBatch;
            s_pending.Clear();
            s_pendingIndex = 0;

            DetachInternal();
            SceneView.RepaintAll();
        }

        // ----------------------------------------------------------------
        //  内部処理
        // ----------------------------------------------------------------

        static void DetachInternal()
        {
            foreach (var id in s_ids)
            {
                var go = EditorUtility.InstanceIDToObject(id) as GameObject;
                if (go == null) continue;
                var col = go.GetComponent<MeshCollider>();
                if (col != null) Object.DestroyImmediate(col);
            }
            s_ids.Clear();
            ClearPrefs();
        }

        static void ProcessBatch()
        {
            int end = Mathf.Min(s_pendingIndex + kBatchSize, s_pending.Count);
            for (int i = s_pendingIndex; i < end; i++)
            {
                var mf = s_pending[i];
                if (mf == null) continue;

                var col = mf.gameObject.AddComponent<MeshCollider>();
                col.sharedMesh = mf.sharedMesh;
                s_ids.Add(mf.gameObject.GetInstanceID());
            }
            s_pendingIndex = end;
            SaveSession();

            if (s_pendingIndex >= s_pending.Count)
            {
                SavePaths();
                EditorApplication.update -= ProcessBatch;
                s_pending.Clear();
                s_pendingIndex = 0;
            }

            SceneView.RepaintAll();
        }

        static bool IsEligible(MeshFilter mf) =>
            mf.sharedMesh != null
            && mf.sharedMesh.vertexCount <= kVertexMax
            && mf.GetComponent<Collider>() == null;

        static string GetPath(GameObject go)
        {
            string path = go.name;
            Transform t = go.transform.parent;
            while (t != null) { path = t.name + "/" + path; t = t.parent; }
            return path;
        }

        static void SaveSession() =>
            SessionState.SetString(kSessionKey, string.Join(",", s_ids));

        static void SavePaths()
        {
            var paths = new List<string>(s_ids.Count);
            foreach (var id in s_ids)
            {
                var go = EditorUtility.InstanceIDToObject(id) as GameObject;
                if (go != null) paths.Add(GetPath(go));
            }
            EditorPrefs.SetString(kPrefsKey, string.Join("\n", paths));
        }

        static void ClearPrefs()
        {
            EditorPrefs.DeleteKey(kPrefsKey);
            EditorPrefs.SetBool(kPrefsDirty, false);
            SessionState.EraseString(kSessionKey);
        }
    }
}
