using System.Collections.Generic;
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
        //  Tangent Scale
        // ================================================================

        static void ApplyTangentScaleToSpline(CableGenerator cableGen, float scale)
        {
            var splineContainer = cableGen.GetComponent<SplineContainer>();
            if (splineContainer == null || splineContainer.Splines.Count == 0) return;

            var spline = splineContainer.Splines[0];
            int count  = spline.Count;
            if (count == 0) return;

            bool          closed    = spline.Closed;
            Vector3[]     positions = new Vector3[count];
            quaternion[]  rotations = new quaternion[count];
            float3[]      prevIn    = new float3[count];
            float3[]      prevOut   = new float3[count];
            TangentMode[] modes     = new TangentMode[count];

            for (int i = 0; i < count; i++)
            {
                var k        = spline[i];
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
                    dPrev        = Vector3.Distance(positions[prev], positions[i]);
                    dNext        = Vector3.Distance(positions[i], positions[next]);
                    baseLens[i]  = ((dPrev + dNext) * 0.5f) / 3f * scale;
                }
                else
                {
                    if (i > 0)         dPrev = Vector3.Distance(positions[i - 1], positions[i]);
                    if (i < count - 1) dNext = Vector3.Distance(positions[i],     positions[i + 1]);

                    float avg   = (i > 0 && i < count - 1)
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
                float  len = baseLens[i];
                float3 inT, outT;

                bool isEndPoint = i == 0 || i == count - 1;

                if (isEndPoint && modes[i] != TangentMode.AutoSmooth)
                {
                    float3 dirIn  = math.lengthsq(prevIn[i])  > 1e-6f ? math.normalize(prevIn[i])  : new float3(0f, 0f, -1f);
                    float3 dirOut = math.lengthsq(prevOut[i]) > 1e-6f ? math.normalize(prevOut[i]) : new float3(0f, 0f,  1f);
                    inT  = dirIn  * len;
                    outT = dirOut * len;
                }
                else if (modes[i] == TangentMode.Broken)
                {
                    inT  = new float3(0f, 0f, -len);
                    outT = new float3(0f, 0f,  len);
                }
                else
                {
                    inT  = prevIn[i];
                    outT = prevOut[i];
                }

                spline.Add(new BezierKnot((float3)positions[i], inT, outT, rotations[i]), modes[i]);
            }

            EditorUtility.SetDirty(splineContainer);
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

            bool          closed    = spline.Closed;
            int           knotCount = spline.Count;
            BezierKnot[]  knots     = new BezierKnot[knotCount];
            TangentMode[] modes     = new TangentMode[knotCount];

            for (int i = 0; i < knotCount; i++)
            {
                knots[i] = spline[i];
                modes[i] = spline.GetTangentMode(i);
            }

            Vector3 localDir = splineContainer.transform.InverseTransformDirection(dir).normalized;

            int          successCount = 0;
            var          failedIndices = new List<int>();

            foreach (int targetIndex in targetIndices)
            {
                Vector3 worldOrigin = splineContainer.transform.TransformPoint((Vector3)knots[targetIndex].Position);

                if (!Physics.Raycast(worldOrigin, dir, out RaycastHit hit,
                        s_snapMaxDistance, s_snapLayerMask.value, QueryTriggerInteraction.Ignore))
                {
                    failedIndices.Add(targetIndex);
                    continue;
                }

                Vector3    worldTargetPos = hit.point + hit.normal * s_snapSurfaceOffset;
                Vector3    localTargetPos = splineContainer.transform.InverseTransformPoint(worldTargetPos);
                Vector3    localHitNormal = splineContainer.transform.InverseTransformDirection(hit.normal).normalized;

                var        oldKnot    = knots[targetIndex];
                quaternion snappedRot = AlignForwardToPlaneNoNormalTwist(oldKnot.Rotation, localHitNormal, localDir);
                knots[targetIndex]    = new BezierKnot(
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

            // Clear+Add は KnotInserted イベントを N 回発火させ CableKnotAttachment の
            // knotIndex をずらすため、SetKnot/SetTangentMode で既存スロットを上書きする。
            Undo.RecordObject(splineContainer, "Snap Knots To Surface");
            for (int i = 0; i < knotCount; i++)
            {
                spline.SetKnot(i, knots[i]);
                spline.SetTangentMode(i, modes[i]);
            }

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
        //  Cable Sag
        // ================================================================

        static void InsertSagKnots(CableGenerator cableGen)
        {
            s_sagLastResult = string.Empty;
            s_hasSagKnots   = false;

            var splineContainer = cableGen.GetComponent<SplineContainer>();
            if (splineContainer == null || splineContainer.Splines.Count == 0)
            {
                s_sagLastResult = "SplineContainer が見つかりません。";
                return;
            }

            var spline    = splineContainer.Splines[0];
            int knotCount = spline.Count;
            if (knotCount < 2)
            {
                s_sagLastResult = "たわみ挿入には最低2つのノットが必要です。";
                return;
            }

            bool closed     = spline.Closed;
            int  curveCount = closed ? knotCount : knotCount - 1;

            var knots = new BezierKnot[knotCount];
            var modes = new TangentMode[knotCount];
            for (int i = 0; i < knotCount; i++)
            {
                knots[i] = spline[i];
                modes[i] = spline.GetTangentMode(i);
            }

            var basePositions = new Vector3[curveCount];
            var inserted      = new BezierKnot[curveCount];

            for (int i = 0; i < curveCount; i++)
            {
                int     nextIdx = (i + 1) % knotCount;
                Vector3 localA  = (Vector3)(float3)knots[i].Position;
                Vector3 localB  = (Vector3)(float3)knots[nextIdx].Position;
                basePositions[i] = (localA + localB) * 0.5f;

                Vector3 segDir = localB - localA;
                if (segDir.sqrMagnitude < kVectorEpsilonSqr) segDir = Vector3.forward;
                else segDir.Normalize();

                inserted[i] = new BezierKnot(
                    (float3)basePositions[i],
                    new float3(0f, 0f, -0.5f),
                    new float3(0f, 0f,  0.5f),
                    SafeLookRotation(segDir));
            }

            Undo.RecordObject(splineContainer, "Insert Cable Sag Knots");
            spline.Clear();
            spline.Closed = closed;

            for (int i = 0; i < knotCount; i++)
            {
                spline.Add(knots[i], modes[i]);
                if (closed || i < knotCount - 1)
                    spline.Add(inserted[i], s_sagUseMirrored ? TangentMode.Mirrored : TangentMode.AutoSmooth);
            }

            s_sagBasePositions = basePositions;
            s_sagOriginalCount = knotCount;
            s_sagWasClosed     = closed;
            s_hasSagKnots      = true;

            EditorUtility.SetDirty(splineContainer);

            UpdateSagKnots(cableGen, s_sagDropDistance, s_sagHandleLength);

            s_sagLastResult = $"区間 {curveCount} にたわみノットを挿入しました（{knotCount} → {spline.Count} ノット）。";
        }

        static void UpdateSagKnots(CableGenerator cableGen, float dropDistance, float handleLength)
        {
            if (!s_hasSagKnots || s_sagBasePositions == null) return;

            var splineContainer = cableGen.GetComponent<SplineContainer>();
            if (splineContainer == null || splineContainer.Splines.Count == 0) return;

            var spline        = splineContainer.Splines[0];
            int expectedCount = s_sagWasClosed ? s_sagOriginalCount * 2 : s_sagOriginalCount * 2 - 1;

            // Undo などでスプライン構造が変わっていたら調整モードを解除
            if (spline.Count != expectedCount)
            {
                s_hasSagKnots   = false;
                s_sagLastResult = "スプライン構造が変更されたため調整を終了しました。";
                return;
            }

            Transform tf          = splineContainer.transform;
            Vector3   localOffset = tf.InverseTransformVector(Vector3.down * dropDistance);
            int       sagCount    = s_sagBasePositions.Length;

            TangentMode targetMode = s_sagUseMirrored ? TangentMode.Mirrored : TangentMode.AutoSmooth;

            Undo.RecordObject(splineContainer, "Adjust Cable Sag");

            for (int i = 0; i < sagCount; i++)
            {
                int splineIdx = 2 * i + 1;
                var k         = spline[splineIdx];
                k.Position = (float3)(s_sagBasePositions[i] + localOffset);

                if (s_sagUseMirrored)
                {
                    k.TangentIn  = new float3(0f, 0f, -handleLength);
                    k.TangentOut = new float3(0f, 0f,  handleLength);
                }

                spline.SetKnot(splineIdx, k);

                if (spline.GetTangentMode(splineIdx) != targetMode)
                    spline.SetTangentMode(splineIdx, targetMode);
            }

            EditorUtility.SetDirty(splineContainer);
            SceneView.RepaintAll();
        }

        // ================================================================
        //  Knot Initialization (Subdivide / Redistribute)
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

            var spline    = splineContainer.Splines[0];
            int knotCount = spline.Count;
            if (knotCount < 2)
            {
                s_knotInitLastResult = "細分化には最低2つのノットが必要です。";
                return false;
            }

            bool closed     = spline.Closed;
            int  curveCount = closed ? knotCount : knotCount - 1;

            var knots    = new BezierKnot[knotCount];
            var modes    = new TangentMode[knotCount];
            var inserted = new BezierKnot[curveCount];

            for (int i = 0; i < knotCount; i++)
            {
                knots[i] = spline[i];
                modes[i] = spline.GetTangentMode(i);
            }

            for (int i = 0; i < curveCount; i++)
            {
                EvaluateCurveArcLengthMidpoint(knots[i], knots[(i + 1) % knotCount],
                    out float3 pos, out float3 tan, out float3 up);

                Vector3 tangent = ((Vector3)tan).normalized;
                if (tangent.sqrMagnitude < kVectorEpsilonSqr) tangent = Vector3.forward;

                quaternion rot = SafeLookRotation(tangent, (Vector3)up);
                float      len = math.length(tan) / kMidpointTangentDivisor;

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

            var spline    = splineContainer.Splines[0];
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

            var        first     = spline[0];
            var        last      = spline[knotCount - 1];
            TangentMode firstMode = spline.GetTangentMode(0);
            TangentMode lastMode  = spline.GetTangentMode(knotCount - 1);
            Vector3 start       = (Vector3)first.Position;
            Vector3 end         = (Vector3)last.Position;
            Vector3 dir         = end - start;
            float   totalLength = dir.magnitude;

            Vector3    forward         = totalLength > kVectorEpsilon ? dir / totalLength : Vector3.forward;
            quaternion interiorRotation = SafeLookRotation(forward);
            float      tangentLen       = totalLength / Mathf.Max(1, divisionCount) / kLinearTangentDivisor;

            Undo.RecordObject(splineContainer, "Redistribute Cable Knots");
            spline.Clear();
            spline.Closed = false;

            spline.Add(first, firstMode);

            for (int i = 1; i < targetKnotCount - 1; i++)
            {
                float  t   = (float)i / (targetKnotCount - 1);
                float3 pos = (float3)Vector3.Lerp(start, end, t);

                spline.Add(new BezierKnot(
                    pos,
                    new float3(0f, 0f, -tangentLen),
                    new float3(0f, 0f,  tangentLen),
                    interiorRotation), addedKnotMode);
            }

            spline.Add(last, lastMode);

            EditorUtility.SetDirty(splineContainer);
            cableGen.RebuildMesh();
            SceneView.RepaintAll();

            s_knotInitLastResult = $"始点-終点を{divisionCount}分割で再配置しました（{targetKnotCount}ノット）。";
            return true;
        }

        // ================================================================
        //  Math Helpers
        // ================================================================

        // 弧長パラメタライゼーションでセグメントの距離的中点を求める
        static void EvaluateCurveArcLengthMidpoint(BezierKnot startKnot, BezierKnot endKnot,
            out float3 pos, out float3 tan, out float3 up, int samples = 64)
        {
            float3 p0 = startKnot.Position;
            float3 p1 = startKnot.Position + math.rotate(startKnot.Rotation, startKnot.TangentOut);
            float3 p2 = endKnot.Position   + math.rotate(endKnot.Rotation,   endKnot.TangentIn);
            float3 p3 = endKnot.Position;

            // 累積弧長テーブルを構築
            var cumLen = new float[samples + 1];
            var pts    = new float3[samples + 1];
            cumLen[0] = 0f;
            pts[0]    = p0;
            for (int j = 1; j <= samples; j++)
            {
                float s   = (float)j / samples;
                float oms = 1f - s;
                pts[j]    = oms*oms*oms*p0 + 3f*oms*oms*s*p1 + 3f*oms*s*s*p2 + s*s*s*p3;
                cumLen[j] = cumLen[j - 1] + math.length(pts[j] - pts[j - 1]);
            }

            float halfLen = cumLen[samples] * 0.5f;

            // halfLen に対応する t を線形補間で求める
            float tMid = 0.5f;
            for (int j = 1; j <= samples; j++)
            {
                if (cumLen[j] >= halfLen)
                {
                    float span = cumLen[j] - cumLen[j - 1];
                    float frac = span > 1e-9f ? (halfLen - cumLen[j - 1]) / span : 0f;
                    tMid = ((float)(j - 1) + frac) / samples;
                    break;
                }
            }

            float omt = 1f - tMid;
            pos = omt*omt*omt*p0 + 3f*omt*omt*tMid*p1 + 3f*omt*tMid*tMid*p2 + tMid*tMid*tMid*p3;
            tan = 3f*omt*omt*(p1 - p0) + 6f*omt*tMid*(p2 - p1) + 3f*tMid*tMid*(p3 - p2);

            quaternion midRot = math.slerp(startKnot.Rotation, endKnot.Rotation, tMid);
            up = math.rotate(midRot, new float3(0f, 1f, 0f));
        }

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
            Vector3 n      = normal.sqrMagnitude < 0.0001f ? Vector3.up : normal.normalized;
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
            Vector3 n            = planeNormal.sqrMagnitude < 0.0001f ? Vector3.up : planeNormal.normalized;
            Vector3 oldForward   = math.rotate(oldRotation, new float3(0f, 0f, 1f));
            Vector3 planarForward = Vector3.ProjectOnPlane(oldForward, n);

            if (planarForward.sqrMagnitude < 0.000001f)
                planarForward = SurfaceParallelDirection(fallbackDirection, n);
            if (planarForward.sqrMagnitude < 0.000001f)
                return oldRotation;

            Quaternion delta  = Quaternion.FromToRotation(oldForward.normalized, planarForward.normalized);
            Quaternion result = delta * (Quaternion)oldRotation;
            return (quaternion)result;
        }
    }
}
