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

        private sealed partial class SolverBuffers
        {
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
