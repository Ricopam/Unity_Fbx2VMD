using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 표면 보정 솔버의 CG 잡·워커 스크래치 버퍼 — 본체에서 이관한 partial.
    /// </summary>
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {

        private static void UpdateActiveSharpFoldPairs(
            IReadOnlyList<Vector3> vertices,
            NativeSkinningSurfaceContract contract,
            IEnumerable<int> facePairIndices,
            ISet<int> activePairIndices)
        {
            foreach (int pairIndex in facePairIndices)
            {
                if (NativeSkinningSurfaceDefectDetector.IsCorrectable(
                        vertices,
                        contract.FacePairs[pairIndex],
                        contract.FacePairRestLengths[pairIndex],
                        contract.RestAnglesDegrees[pairIndex],
                        contract.RestShapeRecoveryMaximumCosines[pairIndex]))
                {
                    activePairIndices.Add(pairIndex);
                }
            }
        }

        // rest 각도 상한을 넘는 자연 주름도 baseline에서 접히지 않았다면
        // 보정 도중 임계값을 넘었을 때 되돌릴 제약 대상으로 인정한다.
        // baseline이 이미 접힌 쌍은 계약이 허용한 상태이므로 건드리지 않는다.
        private static bool IsSharpFoldCorrectionEligible(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            int pairIndex)
        {
            if (contract.RestAnglesDegrees[pairIndex] <= RestSmoothAngleDegrees)
            {
                return true;
            }
            return NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                       baselineVertices,
                       contract.FacePairs[pairIndex],
                       out float baselineAngleDegrees) &&
                   baselineAngleDegrees <= SharpFoldAngleDegrees;
        }

        private static void UpdateActiveCorrectionPairs(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> currentVertices,
            NativeSkinningSurfaceContract contract,
            IEnumerable<int> facePairIndices,
            bool shouldRecoverRestShape,
            ISet<int> activePairIndices)
        {
            foreach (int pairIndex in facePairIndices)
            {
                if (shouldRecoverRestShape)
                {
                    if (IsRestShapeRecoveryPair(
                            baselineVertices,
                            contract,
                            pairIndex))
                    {
                        activePairIndices.Add(pairIndex);
                    }
                    continue;
                }
                if (NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                        currentVertices,
                        contract.FacePairs[pairIndex],
                        out float angleDegrees) &&
                    angleDegrees > SharpFoldAngleDegrees && IsSharpFoldCorrectionEligible( baselineVertices, contract, pairIndex))
                {
                    activePairIndices.Add(pairIndex); } } }

        private static float ResolveProjectionAngleDegrees(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            int pairIndex)
        {
            if (!IsRestShapeRecoveryPair(
                    baselineVertices,
                    contract,
                    pairIndex))
            {
                return ProjectionAngleDegrees;
            }

            NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                baselineVertices,
                contract.FacePairs[pairIndex],
                out float baselineAngleDegrees);
            return Mathf.Min(
                RestShapeRecoveryAngleDegrees,
                baselineAngleDegrees - RestShapeMinimumAngleImprovementDegrees);
        }

        private static bool IsRestShapeRecoveryPair(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            int pairIndex)
        {
            NativeSkinningFacePair pair = contract.FacePairs[pairIndex];
            return NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                       baselineVertices,
                       pair,
                       out float baselineAngleDegrees) &&
                   baselineAngleDegrees <= SharpFoldAngleDegrees &&
                   NativeSkinningRestShapeCollapseDetector.IsSeverelyCollapsed(
                       baselineVertices,
                       pair,
                       contract.FacePairRestLengths[pairIndex],
                       contract.RestAnglesDegrees[pairIndex],
                       baselineAngleDegrees);
        }

        private static LocalWorkset BuildLocalWorkset(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract, IEnumerable<int> seedFacePairIndices, int ringCount, IReadOnlyList<FrameEdge> frameEdges, SolverBuffers buffers)
        {
            var constrainedVertexSet = new HashSet<int>();
            foreach (int pairIndex in seedFacePairIndices)
            {
                NativeSkinningFacePair pair = contract.FacePairs[pairIndex];
                constrainedVertexSet.Add(pair.FirstOpposite);
                constrainedVertexSet.Add(pair.SecondOpposite);
                constrainedVertexSet.Add(pair.FirstEdge);
                constrainedVertexSet.Add(pair.SecondEdge); }
            int vertexCount = baselineVertices.Count;
            int faceCount = contract.Triangles.Length / 3;
            buffers.EnsureWorksetSize( vertexCount, contract.FacePairs.Length, faceCount);
            int[] localIndexByVertex = buffers.LocalIndexByVertex;
            int[] faceMark = buffers.FaceMark;
            int[] facePairMark = buffers.FacePairMark;
            ExpandConstrainedVertices(contract, constrainedVertexSet, ringCount, buffers, faceMark);
            int[] constrainedVertices = AssignLocalIndices( constrainedVertexSet, localIndexByVertex, vertexCount);
            int pairStamp = buffers.NextMarkStamp();
            MarkByVertex(contract.FacePairIndicesByVertex, constrainedVertices, facePairMark, pairStamp);
            int[] facePairIndices = CollectMarked( facePairMark, pairStamp, contract.FacePairs.Length);
            int faceStamp = buffers.NextMarkStamp();
            MarkByVertex(contract.AffectedFaceIndicesByVertex, constrainedVertices, faceMark, faceStamp);
            int[] faceIndices = CollectMarked(faceMark, faceStamp, faceCount);
            LocalEdge[] edges = BuildLocalEdges(frameEdges, localIndexByVertex);
            return new LocalWorkset( constrainedVertices, localIndexByVertex, facePairIndices, faceIndices, edges); }

        // 시드 정점에서 ringCount만큼 면 이웃을 확장한다. 스탬프로 중복 순회를 막는다.
        private static void ExpandConstrainedVertices(
            NativeSkinningSurfaceContract contract,
            HashSet<int> constrainedVertexSet, int ringCount, SolverBuffers buffers, int[] faceMark)
        {
            var frontier = new HashSet<int>(constrainedVertexSet);
            for (int ring = 0; ring < ringCount && frontier.Count > 0; ring++)
            {
                // 같은 면을 두 번 순회하지 않게 스탬프로 표시한다.
                int ringStamp = buffers.NextMarkStamp();
                var nextFrontier = new HashSet<int>();
                foreach (int index in frontier)
                {
                    foreach (int faceIndex in
                             contract.AffectedFaceIndicesByVertex[index])
                    {
                        if (faceMark[faceIndex] == ringStamp)
                        {
                            continue; }
                        faceMark[faceIndex] = ringStamp;
                        int offset = faceIndex * 3;
                        for (int corner = 0; corner < 3; corner++)
                        {
                            int vertexIndex = contract.Triangles[offset + corner];
                            if (constrainedVertexSet.Add(vertexIndex))
                            {
                                nextFrontier.Add(vertexIndex); } } } }
                frontier = nextFrontier; } }

        // 멤버십 표시 후 오름차순 스캔 — 정렬된 인덱스와 로컬 인덱스를 동시에 얻는다.
        private static int[] AssignLocalIndices(
            HashSet<int> constrainedVertexSet,
            int[] localIndexByVertex,
            int vertexCount)
        {
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                localIndexByVertex[vertexIndex] =
                    constrainedVertexSet.Contains(vertexIndex) ? 0 : -1;
            }
            int[] constrainedVertices = new int[constrainedVertexSet.Count];
            int constrainedCount = 0;
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                if (localIndexByVertex[vertexIndex] >= 0)
                {
                    localIndexByVertex[vertexIndex] = constrainedCount;
                    constrainedVertices[constrainedCount] = vertexIndex;
                    constrainedCount++;
                }
            }
            return constrainedVertices;
        }

        private static void MarkByVertex(
            IReadOnlyList<int>[] indicesByVertex,
            int[] vertices,
            int[] marks,
            int stamp)
        {
            foreach (int vertexIndex in vertices)
            {
                foreach (int index in indicesByVertex[vertexIndex])
                {
                    marks[index] = stamp;
                }
            }
        }

        private static int[] CollectMarked(int[] marks, int stamp, int total)
        {
            int markedCount = 0;
            for (int index = 0; index < total; index++)
            {
                if (marks[index] == stamp)
                {
                    markedCount++;
                }
            }
            var indices = new int[markedCount];
            int writeIndex = 0;
            for (int index = 0; index < total; index++)
            {
                if (marks[index] == stamp)
                {
                    indices[writeIndex] = index;
                    writeIndex++;
                }
            }
            return indices;
        }

        private static LocalEdge[] BuildLocalEdges(
            IReadOnlyList<FrameEdge> frameEdges,
            int[] localIndexByVertex)
        {
            int edgeCount = 0;
            foreach (FrameEdge edge in frameEdges)
            {
                if (localIndexByVertex[edge.FirstVertex] >= 0 ||
                    localIndexByVertex[edge.SecondVertex] >= 0)
                {
                    edgeCount++;
                }
            }
            var edges = new LocalEdge[edgeCount];
            int edgeWriteIndex = 0;
            foreach (FrameEdge edge in frameEdges)
            {
                int firstLocalIndex = localIndexByVertex[edge.FirstVertex];
                int secondLocalIndex = localIndexByVertex[edge.SecondVertex];
                if (firstLocalIndex < 0 && secondLocalIndex < 0)
                {
                    continue;
                }
                edges[edgeWriteIndex] = new LocalEdge(
                    edge.FirstVertex,
                    edge.SecondVertex, firstLocalIndex, secondLocalIndex, edge.BaselineLength, edge.RestLength);
                edgeWriteIndex++; }
            return edges; }

        private static FrameEdge[] BuildFrameEdges(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract)
        {
            return contract.AffectedFaceIndices
                .SelectMany(faceIndex =>
                {
                    int[] vertices = GetFaceVertices(contract.Triangles, faceIndex);
                    return new[]
                    {
                        new NativeSkinningEdgeKey(vertices[0], vertices[1]),
                        new NativeSkinningEdgeKey(vertices[1], vertices[2]),
                        new NativeSkinningEdgeKey(vertices[2], vertices[0])
                    };
                })
                .Distinct()
                .Select(edge => new FrameEdge(
                    edge.First,
                    edge.Second,
                    Vector3.Distance(
                        baselineVertices[edge.First],
                        baselineVertices[edge.Second]),
                    Vector3.Distance(
                        contract.RestVertices[edge.First],
                        contract.RestVertices[edge.Second])))
                .Where(edge =>
                    edge.BaselineLength > 0.00000001f &&
                    edge.RestLength > 0.00000001f) .ToArray(); }

        private static int[] GetFaceVertices(
            IReadOnlyList<int> triangles,
            int faceIndex)
        {
            int offset = faceIndex * 3;
            return new[]
            {
                triangles[offset],
                triangles[offset + 1],
                triangles[offset + 2]
            };
        }

        private static bool HasOrientationDefect(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> candidateVertices,
            NativeSkinningSurfaceContract contract,
            IEnumerable<int> faceIndices)
        {
            foreach (int faceIndex in faceIndices)
            {
                if (!TryCalculateFaceNormal(
                        baselineVertices,
                        contract.Triangles,
                        faceIndex,
                        out Vector3 baselineNormal))
                {
                    continue;
                }
                if (!TryCalculateFaceNormal(
                        candidateVertices,
                        contract.Triangles,
                        faceIndex,
                        out Vector3 candidateNormal) ||
                    Vector3.Dot(baselineNormal, candidateNormal) <= 0f)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryCalculateFaceNormal(
            IReadOnlyList<Vector3> vertices,
            IReadOnlyList<int> triangles,
            int faceIndex,
            out Vector3 normal)
        {
            int offset = faceIndex * 3;
            Vector3 first = vertices[triangles[offset]];
            Vector3 second = vertices[triangles[offset + 1]];
            Vector3 third = vertices[triangles[offset + 2]];
            normal = Vector3.Cross(second - first, third - first);
            if (normal.sqrMagnitude < MinimumNormalSquaredMagnitude)
            {
                normal = Vector3.zero;
                return false;
            }
            normal /= Mathf.Sqrt(normal.sqrMagnitude);
            return true;
        }

        private static float CalculateFacePairCharacteristicLength(
            IReadOnlyList<Vector3> vertices,
            NativeSkinningFacePair pair)
        {
            float minimumLength = Vector3.Distance(
                vertices[pair.FirstEdge],
                vertices[pair.SecondEdge]);
            minimumLength = Mathf.Min(
                minimumLength,
                Vector3.Distance(
                    vertices[pair.FirstOpposite],
                    vertices[pair.FirstEdge]));
            minimumLength = Mathf.Min(
                minimumLength,
                Vector3.Distance(
                    vertices[pair.FirstOpposite],
                    vertices[pair.SecondEdge]));
            minimumLength = Mathf.Min(
                minimumLength,
                Vector3.Distance(
                    vertices[pair.SecondOpposite],
                    vertices[pair.FirstEdge]));
            return Mathf.Min(
                minimumLength,
                Vector3.Distance(
                    vertices[pair.SecondOpposite],
                    vertices[pair.SecondEdge]));
        }

        private static Vector3 ClampDisplacementFromBaseline(
            Vector3 baseline,
            Vector3 candidate,
            float maximumDistance)
        {
            Vector3 displacement = candidate - baseline;
            float squaredDistance = displacement.sqrMagnitude;
            float squaredMaximumDistance = maximumDistance * maximumDistance;
            if (squaredDistance <= squaredMaximumDistance || squaredDistance <= 0f)
            {
                return candidate;
            }
            return baseline + displacement *
                (maximumDistance / Mathf.Sqrt(squaredDistance));
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}