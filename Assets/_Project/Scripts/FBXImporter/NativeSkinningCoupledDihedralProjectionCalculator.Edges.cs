using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 커플링 다이히드럴 계산의 레스트 변형률 에지 구성 partial.
    internal static partial class NativeSkinningCoupledDihedralProjectionCalculator
    {
        // 한 삼각형 면의 세 모서리를 방향 무관 에지 키로 열거한다.
        private static IEnumerable<NativeSkinningEdgeKey> EnumerateFaceEdges(
            NativeSkinningSurfaceContract contract, int faceIndex)
        {
            int offset = faceIndex * 3;
            int first = contract.Triangles[offset];
            int second = contract.Triangles[offset + 1];
            int third = contract.Triangles[offset + 2];
            return new[]
            {
                new NativeSkinningEdgeKey(first, second),
                new NativeSkinningEdgeKey(second, third),
                new NativeSkinningEdgeKey(third, first)
            };
        }

        // 에지 키의 기준선·레스트 길이를 측정해 변형률 에지를 만든다.
        private static RestStrainEdge CreateRestStrainEdge(
            NativeSkinningEdgeKey edge, IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract)
        {
            return new RestStrainEdge(
                edge.First, edge.Second,
                Vector3.Distance(baselineVertices[edge.First], baselineVertices[edge.Second]),
                Vector3.Distance(contract.RestVertices[edge.First], contract.RestVertices[edge.Second]));
        }
    }
}
