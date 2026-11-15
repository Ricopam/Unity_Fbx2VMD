using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 변위 시스템 구축·에너지 항·CG 솔버 — 본체에서 이관한 partial.
    /// </summary>
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
        private struct CgNativeBuffers
        {
            internal NativeArray<float> Diagonal;
            internal NativeArray<float> Preconditioner;
            internal NativeArray<Vector3> RightHandSide;
            internal NativeArray<Vector3> Solution;
            internal NativeArray<int> IterationsRun;
            internal NativeArray<int2> EdgeIndices;
            internal NativeArray<int> BendIndices;
            internal NativeArray<Vector3> BendGradients;
            internal NativeArray<int2> StrainIndices;
            internal NativeArray<Vector3> StrainFirstGradients;
            internal NativeArray<Vector3> StrainSecondGradients;
            internal NativeArray<int> MaterialOffsets;
            internal NativeArray<int> MaterialIndices;
            internal NativeArray<float> MaterialCoefficients;
            internal NativeArray<float> MaterialWeights;

            internal void Allocate(
                int count, int edgeCount, int bendCount, int strainCount,
                int materialCount, int materialTermTotal)
            {
                Diagonal = new NativeArray<float>(count, Allocator.TempJob);
                Preconditioner = new NativeArray<float>(count, Allocator.TempJob);
                RightHandSide = new NativeArray<Vector3>(count, Allocator.TempJob);
                Solution = new NativeArray<Vector3>(count, Allocator.TempJob);
                IterationsRun = new NativeArray<int>(1, Allocator.TempJob);
                EdgeIndices = new NativeArray<int2>(edgeCount, Allocator.TempJob);
                BendIndices = new NativeArray<int>(bendCount * 4, Allocator.TempJob);
                BendGradients = new NativeArray<Vector3>(bendCount * 4, Allocator.TempJob);
                StrainIndices = new NativeArray<int2>(strainCount, Allocator.TempJob);
                StrainFirstGradients = new NativeArray<Vector3>(strainCount, Allocator.TempJob);
                StrainSecondGradients = new NativeArray<Vector3>(strainCount, Allocator.TempJob);
                MaterialOffsets = new NativeArray<int>(materialCount + 1, Allocator.TempJob);
                MaterialIndices = new NativeArray<int>(materialTermTotal, Allocator.TempJob);
                MaterialCoefficients = new NativeArray<float>(materialTermTotal, Allocator.TempJob);
                MaterialWeights = new NativeArray<float>(materialCount, Allocator.TempJob);
            }

            internal void Pack(
                SolverBuffers buffers,
                IReadOnlyList<LocalEdge> edges,
                IReadOnlyList<NativeSkinningMaterialStrainConstraint> materialStrainConstraints,
                bool warmStart,
                int count, int edgeCount, int bendCount,
                int strainCount, int materialCount)
            {
                NativeArray<float>.Copy(buffers.Diagonal, 0, Diagonal, 0, count);
                NativeArray<float>.Copy(buffers.Preconditioner, 0, Preconditioner, 0, count);
                NativeArray<Vector3>.Copy(buffers.RightHandSide, 0, RightHandSide, 0, count);
                if (warmStart)
                {
                    NativeArray<Vector3>.Copy(buffers.Solution, 0, Solution, 0, count);
                }
                PackEdgeIndices(EdgeIndices, edges, edgeCount);
                PackBendTerms(
                    BendIndices, BendGradients,
                    buffers.BendingTerms, bendCount);
                PackStrainTerms(
                    StrainIndices, StrainFirstGradients,
                    StrainSecondGradients,
                    buffers.EdgeStrainTerms, strainCount);
                PackMaterialTerms(
                    MaterialOffsets, MaterialIndices,
                    MaterialCoefficients, MaterialWeights,
                    materialStrainConstraints, materialCount);
            }

            internal void Dispose()
            {
                if (Diagonal.IsCreated) Diagonal.Dispose();
                if (Preconditioner.IsCreated) Preconditioner.Dispose();
                if (RightHandSide.IsCreated) RightHandSide.Dispose();
                if (Solution.IsCreated) Solution.Dispose();
                if (IterationsRun.IsCreated) IterationsRun.Dispose();
                if (EdgeIndices.IsCreated) EdgeIndices.Dispose();
                if (BendIndices.IsCreated) BendIndices.Dispose();
                if (BendGradients.IsCreated) BendGradients.Dispose();
                if (StrainIndices.IsCreated) StrainIndices.Dispose();
                if (StrainFirstGradients.IsCreated) StrainFirstGradients.Dispose();
                if (StrainSecondGradients.IsCreated) StrainSecondGradients.Dispose();
                if (MaterialOffsets.IsCreated) MaterialOffsets.Dispose();
                if (MaterialIndices.IsCreated) MaterialIndices.Dispose();
                if (MaterialCoefficients.IsCreated) MaterialCoefficients.Dispose();
                if (MaterialWeights.IsCreated) MaterialWeights.Dispose();
            }
        }

    }
}
