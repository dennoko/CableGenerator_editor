using UnityEditor;
using UnityEngine;
using CableGeneratorRuntime;

namespace CableGeneratorEditor
{
    [CustomEditor(typeof(CableKnotAttachment))]
    internal class CableKnotAttachmentInspector : Editor
    {
        // ─── Serialized Properties ────────────────────────────────────────────
        SerializedProperty knotIndexProp;
        SerializedProperty prefabProp;
        SerializedProperty positionOffsetProp;
        SerializedProperty rotationOffsetProp;
        SerializedProperty scaleProp;

        void OnEnable()
        {
            knotIndexProp      = serializedObject.FindProperty("knotIndex");
            prefabProp         = serializedObject.FindProperty("prefab");
            positionOffsetProp = serializedObject.FindProperty("positionOffset");
            rotationOffsetProp = serializedObject.FindProperty("rotationOffset");
            scaleProp          = serializedObject.FindProperty("scale");
        }

        // ─── Inspector GUI ────────────────────────────────────────────────────
        public override void OnInspectorGUI()
        {
            CableGeneratorTheme.Initialize();
            serializedObject.Update();
            var attachment = (CableKnotAttachment)target;

            GUILayout.BeginVertical(CableGeneratorTheme.InspectorRootStyle);

            DrawSection("アタッチ設定", () =>
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(knotIndexProp, new GUIContent("ノットインデックス"));
                EditorGUILayout.PropertyField(prefabProp,    new GUIContent("Prefab / FBX"));
                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.ApplyModifiedProperties();
                    attachment.UpdateAttachment();
                }
            });

            DrawSection("トランスフォームオフセット", () =>
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(positionOffsetProp, new GUIContent("位置オフセット"));
                EditorGUILayout.PropertyField(rotationOffsetProp, new GUIContent("回転オフセット"));
                EditorGUILayout.PropertyField(scaleProp,          new GUIContent("スケール"));
                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.ApplyModifiedProperties();
                    attachment.UpdateAttachment();
                }

                GUILayout.Space(8);

                if (GUILayout.Button("アタッチメントを更新", CableGeneratorTheme.ActionButtonStyle))
                {
                    Undo.RecordObject(attachment, "Update Knot Attachment");
                    attachment.UpdateAttachment();
                    EditorUtility.SetDirty(attachment);
                }
            });

            GUILayout.EndVertical();
        }

        // ─── ヘルパー ─────────────────────────────────────────────────────────
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
    }
}
