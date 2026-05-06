using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;
using CableGeneratorRuntime;

namespace CableGeneratorEditor
{
    internal partial class CableSplineContainerEditor
    {
        // ================================================================
        //  Knot Operations
        // ================================================================

        static void InsertKnotAfter(SplineContainer container, Spline spline, int index)
        {
            int    nextIndex = (index + 1) % spline.Count;
            var    kA        = spline[index];
            var    kB        = spline[nextIndex];
            float3 midPos    = (kA.Position + kB.Position) * 0.5f;
            float3 dir       = kB.Position - kA.Position;
            float  tanLen    = math.length(dir) > 1e-6f ? math.length(dir) / 6f : 0.5f;

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
            if (index < 0 || index >= spline.Count) index = spline.Count - 1;

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
                var    last = spline[spline.Count - 1];
                float3 dir  = math.rotate(last.Rotation, last.TangentOut);
                float3 off  = math.lengthsq(dir) > 1e-6f ? math.normalize(dir) * 2f : new float3(0f, 0f, 2f);
                newPos = last.Position + off;
            }
            else newPos = float3.zero;

            Undo.RecordObject(container, "制御点を末尾に追加");
            spline.Add(new BezierKnot(newPos, new float3(0f, 0f, -0.5f), new float3(0f, 0f, 0.5f)),
                TangentMode.AutoSmooth);
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
    }
}
