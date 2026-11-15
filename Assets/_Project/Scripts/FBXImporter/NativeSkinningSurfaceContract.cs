using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{

    internal sealed class NativeSkinningSurfaceContract
    {
        internal NativeSkinningSurfaceContract(
            SkinnedMeshRenderer renderer,
            NativeSkinningArmSide side,
            int vertexCount,
            Vector3[] restVertices,
            int[] triangles,
            int[] affectedFaceIndices,
            NativeSkinningFacePair[] facePairs,
            float[] restAnglesDegrees,
            float[] restShapeRecoveryMaximumCosines,
            NativeSkinningFacePairRestLengths[] facePairRestLengths,
            int[] evaluatedVertexIndices,
            int connectedVertexCount,
            int lowerArmBoneIndex,
            int[][] facePairIndicesByVertex,
            int[][] affectedFaceIndicesByVertex,
            float armChainLength)
        {
            Renderer = renderer;
            Side = side;
            VertexCount = vertexCount;
            RestVertices = restVertices;
            Triangles = triangles;
            AffectedFaceIndices = affectedFaceIndices;
            FacePairs = facePairs;
            RestAnglesDegrees = restAnglesDegrees;
            RestShapeRecoveryMaximumCosines =
                restShapeRecoveryMaximumCosines;
            FacePairRestLengths = facePairRestLengths;
            EvaluatedVertexIndices = evaluatedVertexIndices;
            ConnectedVertexCount = connectedVertexCount;
            LowerArmBoneIndex = lowerArmBoneIndex;
            FacePairIndicesByVertex = facePairIndicesByVertex;
            AffectedFaceIndicesByVertex = affectedFaceIndicesByVertex;
            ArmChainLength = armChainLength;
        }

        internal SkinnedMeshRenderer Renderer { get; }

        internal NativeSkinningArmSide Side { get; }

        internal int VertexCount { get; }

        internal Vector3[] RestVertices { get; }

        internal int[] Triangles { get; }

        internal int[] AffectedFaceIndices { get; }

        internal NativeSkinningFacePair[] FacePairs { get; }

        internal float[] RestAnglesDegrees { get; }

        internal float[] RestShapeRecoveryMaximumCosines { get; }

        internal NativeSkinningFacePairRestLengths[] FacePairRestLengths { get; }

        internal int[] EvaluatedVertexIndices { get; }

        internal int ConnectedVertexCount { get; }

        internal int LowerArmBoneIndex { get; }

        internal int[][] FacePairIndicesByVertex { get; }

        internal int[][] AffectedFaceIndicesByVertex { get; }

        internal float ArmChainLength { get; }

        internal int AffectedFaceCount => AffectedFaceIndices.Length;

        internal int FacePairCount => FacePairs.Length;

        internal int EvaluatedVertexCount => EvaluatedVertexIndices.Length;
    }

    /// <summary>
    /// 자동 선택한 팔 표면을 이면각 solver가 사용할 정적 계약으로 변환함.
    /// </summary>
    internal static class NativeSkinningSurfaceContractBuilder
    {
        private const float MinimumNormalSquaredMagnitude = 0.0000000000000001f;

        internal static bool TryBuild(
            NativeSkinningArmSurfaceSelection selection,
            out NativeSkinningSurfaceContract contract)
        {
            contract = null;
            if (selection == null ||
                selection.Renderer == null ||
                selection.Renderer.sharedMesh == null)
            {
                return false;
            }

            Mesh mesh = selection.Renderer.sharedMesh;
            Vector3[] restVertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            int[] evaluatedVertices = selection.EvaluatedVertexIndices.ToArray();
            if (restVertices.Length != mesh.vertexCount ||
                triangles.Length < 3 ||
                evaluatedVertices.Length < 4 ||
                selection.ConnectedVertexCount < evaluatedVertices.Length ||
                evaluatedVertices.Any(index => index < 0 || index >= mesh.vertexCount))
            {
                return false;
            }

            var evaluatedVertexSet = new HashSet<int>(evaluatedVertices);
            if (!TryCollectTopology(
                    triangles, evaluatedVertexSet,
                    out HashSet<int> affectedFaces,
                    out NativeSkinningFacePair[] facePairs))
            {
                return false;
            }

            BuildRestGeometry(
                restVertices, facePairs,
                out float[] restAngles,
                out float[] restShapeRecoveryMaximumCosines,
                out NativeSkinningFacePairRestLengths[] facePairRestLengths);
            int[] orderedAffectedFaces = affectedFaces
                .OrderBy(index => index)
                .ToArray();
            contract = CreateContract(
                selection,
                mesh,
                restVertices,
                triangles,
                orderedAffectedFaces,
                facePairs,
                restAngles,
                restShapeRecoveryMaximumCosines,
                facePairRestLengths,
                evaluatedVertices);
            return true;
        }

        static NativeSkinningSurfaceContract CreateContract(
            NativeSkinningArmSurfaceSelection selection,
            Mesh mesh,
            Vector3[] restVertices,
            int[] triangles,
            int[] orderedAffectedFaces,
            NativeSkinningFacePair[] facePairs,
            float[] restAngles,
            float[] restShapeRecoveryMaximumCosines,
            NativeSkinningFacePairRestLengths[] facePairRestLengths,
            int[] evaluatedVertices)
        {
            return new NativeSkinningSurfaceContract(
                selection.Renderer,
                selection.Side,
                mesh.vertexCount,
                restVertices,
                triangles,
                orderedAffectedFaces,
                facePairs,
                restAngles,
                restShapeRecoveryMaximumCosines,
                facePairRestLengths,
                evaluatedVertices,
                selection.ConnectedVertexCount,
                selection.LowerArmBoneIndex,
                BuildFacePairIndicesByVertex(mesh.vertexCount, facePairs),
                BuildFaceIndicesByVertex(
                    mesh.vertexCount,
                    triangles,
                    orderedAffectedFaces),
                selection.ArmChainLength);
        }

        static bool TryCollectTopology(
            int[] triangles, HashSet<int> evaluatedVertexSet,
            out HashSet<int> affectedFaces,
            out NativeSkinningFacePair[] facePairs)
        {
            affectedFaces = new HashSet<int>();
            var edgeFaces = new Dictionary<NativeSkinningEdgeKey, List<int>>();
            int faceCount = triangles.Length / 3;
            for (int faceIndex = 0; faceIndex < faceCount; faceIndex++)
            {
                int offset = faceIndex * 3;
                int first = triangles[offset];
                int second = triangles[offset + 1];
                int third = triangles[offset + 2];
                if (evaluatedVertexSet.Contains(first) ||
                    evaluatedVertexSet.Contains(second) ||
                    evaluatedVertexSet.Contains(third))
                {
                    affectedFaces.Add(faceIndex);
                }
                AddEdgeFace(edgeFaces, new NativeSkinningEdgeKey(first, second), faceIndex);
                AddEdgeFace(edgeFaces, new NativeSkinningEdgeKey(second, third), faceIndex);
                AddEdgeFace(edgeFaces, new NativeSkinningEdgeKey(third, first), faceIndex);
            }

            // 람다는 out 파라미터를 캡처할 수 없으므로 로컬 별칭으로 필터링한다.
            HashSet<int> affectedFaceSet = affectedFaces;
            facePairs = edgeFaces
                .Where(entry => entry.Value.Count == 2 &&
                    (affectedFaceSet.Contains(entry.Value[0]) ||
                     affectedFaceSet.Contains(entry.Value[1])))
                .Select(entry => CreateFacePair(
                    triangles,
                    entry.Key,
                    entry.Value[0],
                    entry.Value[1]))
                .ToArray();
            return facePairs.Length != 0;
        }

        static void BuildRestGeometry(
            Vector3[] restVertices, NativeSkinningFacePair[] facePairs,
            out float[] restAngles,
            out float[] restShapeRecoveryMaximumCosines,
            out NativeSkinningFacePairRestLengths[] facePairRestLengths)
        {
            restAngles = facePairs
                .Select(pair => TryMeasureAngle(
                    restVertices,
                    pair,
                    out float angleDegrees)
                        ? angleDegrees
                        : 180f)
                .ToArray();
            restShapeRecoveryMaximumCosines = restAngles
                .Select(angleDegrees => Mathf.Cos(
                    (angleDegrees + 90f) * Mathf.Deg2Rad))
                .ToArray();
            facePairRestLengths = facePairs
                .Select(pair => NativeSkinningRestShapeCollapseDetector
                    .MeasureRestLengths(restVertices, pair))
                .ToArray();
        }

        internal static bool TryMeasureAngle(
            IReadOnlyList<Vector3> vertices,
            NativeSkinningFacePair pair,
            out float angleDegrees)
        {
            angleDegrees = 0f;
            Vector3 firstNormal = Vector3.Cross(
                vertices[pair.FirstEdge] - vertices[pair.FirstOpposite],
                vertices[pair.SecondEdge] - vertices[pair.FirstOpposite]);
            Vector3 secondNormal = Vector3.Cross(
                vertices[pair.SecondEdge] - vertices[pair.SecondOpposite],
                vertices[pair.FirstEdge] - vertices[pair.SecondOpposite]);
            if (firstNormal.sqrMagnitude < MinimumNormalSquaredMagnitude ||
                secondNormal.sqrMagnitude < MinimumNormalSquaredMagnitude)
            {
                return false;
            }

            firstNormal /= Mathf.Sqrt(firstNormal.sqrMagnitude);
            secondNormal /= Mathf.Sqrt(secondNormal.sqrMagnitude);
            angleDegrees = Mathf.Acos(Mathf.Clamp(
                Vector3.Dot(firstNormal, secondNormal),
                -1f,
                1f)) * Mathf.Rad2Deg;
            return !float.IsNaN(angleDegrees) && !float.IsInfinity(angleDegrees);
        }

        private static int[][] BuildFacePairIndicesByVertex(
            int vertexCount,
            IReadOnlyList<NativeSkinningFacePair> pairs)
        {
            var indicesByVertex = Enumerable.Range(0, vertexCount)
                .Select(_ => new List<int>())
                .ToArray();
            for (int pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
            {
                NativeSkinningFacePair pair = pairs[pairIndex];
                foreach (int vertexIndex in new[]
                         {
                             pair.FirstOpposite,
                             pair.SecondOpposite,
                             pair.FirstEdge,
                             pair.SecondEdge
                         }.Distinct())
                {
                    indicesByVertex[vertexIndex].Add(pairIndex);
                }
            }
            return indicesByVertex
                .Select(indices => indices.Distinct().ToArray())
                .ToArray();
        }

        private static int[][] BuildFaceIndicesByVertex(
            int vertexCount,
            IReadOnlyList<int> triangles,
            IEnumerable<int> faceIndices)
        {
            var indicesByVertex = Enumerable.Range(0, vertexCount)
                .Select(_ => new List<int>())
                .ToArray();
            foreach (int faceIndex in faceIndices)
            {
                int offset = faceIndex * 3;
                for (int index = 0; index < 3; index++)
                {
                    indicesByVertex[triangles[offset + index]].Add(faceIndex);
                }
            }
            return indicesByVertex
                .Select(indices => indices.Distinct().ToArray())
                .ToArray();
        }

        private static void AddEdgeFace(
            IDictionary<NativeSkinningEdgeKey, List<int>> edgeFaces,
            NativeSkinningEdgeKey edge,
            int faceIndex)
        {
            if (!edgeFaces.TryGetValue(edge, out List<int> faces))
            {
                faces = new List<int>(2);
                edgeFaces.Add(edge, faces);
            }
            faces.Add(faceIndex);
        }

        private static NativeSkinningFacePair CreateFacePair(
            IReadOnlyList<int> triangles,
            NativeSkinningEdgeKey edge,
            int firstFace,
            int secondFace)
        {
            if (!TryFindOrientedEdge(
                    triangles,
                    firstFace,
                    edge,
                    out int firstOpposite,
                    out int firstEdge,
                    out int secondEdge))
            {
                throw new InvalidOperationException(
                    $"face {firstFace}에서 공유 edge 방향을 찾지 못했습니다.");
            }
            int secondOpposite = FindOppositeVertex(
                triangles,
                secondFace,
                edge);
            return new NativeSkinningFacePair(
                edge,
                firstFace,
                secondFace,
                firstOpposite,
                secondOpposite,
                firstEdge,
                secondEdge);
        }

        private static bool TryFindOrientedEdge(
            IReadOnlyList<int> triangles,
            int faceIndex,
            NativeSkinningEdgeKey edge,
            out int opposite,
            out int firstEdge,
            out int secondEdge)
        {
            int offset = faceIndex * 3;
            int first = triangles[offset];
            int second = triangles[offset + 1];
            int third = triangles[offset + 2];
            if (edge.Equals(new NativeSkinningEdgeKey(first, second)))
            {
                opposite = third;
                firstEdge = first;
                secondEdge = second;
                return true;
            }
            if (edge.Equals(new NativeSkinningEdgeKey(second, third)))
            {
                opposite = first;
                firstEdge = second;
                secondEdge = third;
                return true;
            }
            if (edge.Equals(new NativeSkinningEdgeKey(third, first)))
            {
                opposite = second;
                firstEdge = third;
                secondEdge = first;
                return true;
            }

            opposite = -1;
            firstEdge = -1;
            secondEdge = -1;
            return false;
        }

        private static int FindOppositeVertex(
            IReadOnlyList<int> triangles,
            int faceIndex,
            NativeSkinningEdgeKey edge)
        {
            int offset = faceIndex * 3;
            for (int index = 0; index < 3; index++)
            {
                int vertexIndex = triangles[offset + index];
                if (vertexIndex != edge.First && vertexIndex != edge.Second)
                {
                    return vertexIndex;
                }
            }
            throw new InvalidOperationException(
                $"face {faceIndex}에서 공유 edge 반대 정점을 찾지 못했습니다.");
        }
    }
}
