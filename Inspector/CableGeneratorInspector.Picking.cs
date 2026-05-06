using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using CableGeneratorRuntime;

namespace CableGeneratorEditor
{
    public partial class CableGeneratorInspector
    {
        // ================================================================
        //  Picking Mode — Scene ビューからの 2点選択
        // ================================================================

        void DrawPickedPoints()
        {
            for (int i = 0; i < s_pickCount; i++)
            {
                Handles.color = i == 0 ? new Color(0.2f, 1f, 0.3f) : new Color(0.3f, 0.6f, 1f);

                float   size      = HandleUtility.GetHandleSize(s_pickedPoints[i]) * 0.07f;
                Vector3 normalEnd = s_pickedPoints[i] + s_pickedNormals[i] * size * 4f;

                Handles.SphereHandleCap(0, s_pickedPoints[i], Quaternion.identity, size, EventType.Repaint);
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
            float   x    = (Screen.width - size.x) * 0.5f;
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
                new float3(0f, 0f, -tangentLen), new float3(0f, 0f, tangentLen), rotA),
                TangentMode.Mirrored);
            spline.Add(new BezierKnot((float3)localB,
                new float3(0f, 0f, -tangentLen), new float3(0f, 0f, tangentLen), rotB),
                TangentMode.Mirrored);

            EditorUtility.SetDirty(splineContainer);
        }
    }
}
