using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
        private sealed partial class SolverBuffers
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

        }
    }
}