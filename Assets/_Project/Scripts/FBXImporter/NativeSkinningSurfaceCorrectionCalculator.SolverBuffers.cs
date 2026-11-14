using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
        private readonly struct LocalWorkset
        {
            internal LocalWorkset(
                int[] constrainedVertices,
                int[] localIndexByVertex,
                int[] facePairIndices,
                int[] faceIndices,
                LocalEdge[] edges)
            {
                ConstrainedVertices = constrainedVertices;
                LocalIndexByVertex = localIndexByVertex;
                FacePairIndices = facePairIndices;
                FaceIndices = faceIndices;
                Edges = edges;
            }

            internal int[] ConstrainedVertices { get; }
            internal int[] LocalIndexByVertex { get; }
            internal int[] FacePairIndices { get; }
            internal int[] FaceIndices { get; }
            internal LocalEdge[] Edges { get; }
        }

        // 쌍당 4정점 고정 — 배열 대신 인라인 필드로 반복 할당을 없앤다.

        private sealed class SolverBuffers
        {
            internal float[] Diagonal = Array.Empty<float>();
            internal float[] Preconditioner = Array.Empty<float>();
            internal Vector3[] RightHandSide = Array.Empty<Vector3>();
            internal Vector3[] Solution = Array.Empty<Vector3>();
            internal Vector3[] Residual = Array.Empty<Vector3>();
            internal Vector3[] Direction = Array.Empty<Vector3>();
            internal Vector3[] PreconditionedResidual = Array.Empty<Vector3>();
            internal Vector3[] MatrixDirection = Array.Empty<Vector3>();
            internal Vector3[] ProposedLocal = Array.Empty<Vector3>();
            // 수락된 후보가 다음 반복의 current로 이어지므로 핑퐁 2개가 필요하다.
            internal Vector3[] CandidateA = Array.Empty<Vector3>();
            internal Vector3[] CandidateB = Array.Empty<Vector3>();
            internal readonly List<BendingLinearization> BendingTerms =
                new List<BendingLinearization>();
            internal readonly List<EdgeStrainLinearization> EdgeStrainTerms =
                new List<EdgeStrainLinearization>();
            // 워크셋 구축용 스탬프 마크 — 매번 클리어 대신 스탬프를 올려 재사용한다.
            internal int[] LocalIndexByVertex = Array.Empty<int>();
            internal int[] FacePairMark = Array.Empty<int>();
            internal int[] FaceMark = Array.Empty<int>();
            private int _markStamp;

            internal void EnsureLocalSize(int count)
            {
                Ensure(ref Diagonal, count);
                Ensure(ref Preconditioner, count);
                Ensure(ref RightHandSide, count);
                Ensure(ref Solution, count);
                Ensure(ref Residual, count);
                Ensure(ref Direction, count);
                Ensure(ref PreconditionedResidual, count);
                Ensure(ref MatrixDirection, count);
                Ensure(ref ProposedLocal, count);
            }

            internal void EnsureVertexSize(int count)
            {
                Ensure(ref CandidateA, count);
                Ensure(ref CandidateB, count);
            }

            internal void EnsureWorksetSize(
                int vertexCount,
                int facePairCount,
                int faceCount)
            {
                Ensure(ref LocalIndexByVertex, vertexCount);
                Ensure(ref FacePairMark, facePairCount);
                Ensure(ref FaceMark, faceCount);
            }

            internal int NextMarkStamp() => ++_markStamp;

            internal Vector3[] CandidateFor(IReadOnlyList<Vector3> current) =>
                ReferenceEquals(current, CandidateA) ? CandidateB : CandidateA;

            private static void Ensure(ref int[] array, int count)
            {
                if (array.Length < count)
                {
                    array = new int[count];
                }
            }

            private static void Ensure(ref float[] array, int count)
            {
                if (array.Length < count)
                {
                    array = new float[count];
                }
            }

            private static void Ensure(ref Vector3[] array, int count)
            {
                if (array.Length < count)
                {
                    array = new Vector3[count];
                }
            }
        }
    }
}
