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
        //  Scene GUI
        // ================================================================

        void OnSceneGUI()
        {
            var gen             = (CableGenerator)target;
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
            var spline    = splineContainer.Splines[0];
            Transform transform = splineContainer.transform;

            for (int i = 0; i < spline.Count; i++)
            {
                var     knot     = spline[i];
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
                        if (s_selectedKnotIndices.Contains(i)) s_selectedKnotIndices.Remove(i);
                        else                                    s_selectedKnotIndices.Add(i);
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
        //  Handle Drawing
        // ================================================================

        void DrawTangentHandle(Spline spline, SplineContainer container, Transform transform,
            int knotIndex, BezierKnot knot, CableGenerator gen, bool isIn)
        {
            float3  tangent       = isIn ? knot.TangentIn : knot.TangentOut;
            Vector3 knotWorld     = transform.TransformPoint((Vector3)(float3)knot.Position);
            float3  rotatedTangent = math.rotate(knot.Rotation, tangent);
            Vector3 tangentWorld  = knotWorld + transform.TransformDirection((Vector3)rotatedTangent);

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
                float3  newTangent  = math.rotate(math.inverse(knot.Rotation), (float3)newLocalDir);

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
            int     previewSteps = 64;
            Vector3 prevPoint    = Vector3.zero;

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
    }
}
