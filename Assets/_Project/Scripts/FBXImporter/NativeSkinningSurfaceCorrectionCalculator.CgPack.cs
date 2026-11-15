using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
        // CgNativeBuffers.Pack의 구간별 포장 — 구조체 필드 수를 늘리지 않기 위해
        // 클래스 수준 정적 메서드로 둔다.
        private static void PackEdgeIndices(
            NativeArray<int2> edgeIndices,
            IReadOnlyList<LocalEdge> edges,
            int edgeCount)
        {
            for (int index = 0; index < edgeCount; index++)
            {
                LocalEdge edge = edges[index];
                edgeIndices[index] =
                    new int2(edge.FirstLocalIndex, edge.SecondLocalIndex);
            }
        }

        private static void PackBendTerms(
            NativeArray<int> bendIndices,
            NativeArray<Vector3> bendGradients,
            List<BendingLinearization> bendingTerms,
            int bendCount)
        {
            for (int index = 0; index < bendCount; index++)
            {
                BendingLinearization term = bendingTerms[index];
                bendIndices[index * 4] = term.Index0;
                bendIndices[index * 4 + 1] = term.Index1;
                bendIndices[index * 4 + 2] = term.Index2;
                bendIndices[index * 4 + 3] = term.Index3;
                bendGradients[index * 4] = term.Gradient0;
                bendGradients[index * 4 + 1] = term.Gradient1;
                bendGradients[index * 4 + 2] = term.Gradient2;
                bendGradients[index * 4 + 3] = term.Gradient3;
            }
        }

        private static void PackStrainTerms(
            NativeArray<int2> strainIndices,
            NativeArray<Vector3> strainFirstGradients,
            NativeArray<Vector3> strainSecondGradients,
            List<EdgeStrainLinearization> edgeStrainTerms,
            int strainCount)
        {
            for (int index = 0; index < strainCount; index++)
            {
                EdgeStrainLinearization term = edgeStrainTerms[index];
                strainIndices[index] =
                    new int2(term.FirstLocalIndex, term.SecondLocalIndex);
                strainFirstGradients[index] = term.FirstGradient;
                strainSecondGradients[index] = term.SecondGradient;
            }
        }

        private static void PackMaterialTerms(
            NativeArray<int> materialOffsets,
            NativeArray<int> materialIndices,
            NativeArray<float> materialCoefficients,
            NativeArray<float> materialWeights,
            IReadOnlyList<NativeSkinningMaterialStrainConstraint>
                materialStrainConstraints,
            int materialCount)
        {
            int materialOffset = 0;
            for (int index = 0; index < materialCount; index++)
            {
                NativeSkinningMaterialStrainConstraint constraint =
                    materialStrainConstraints[index];
                materialOffsets[index] = materialOffset;
                materialWeights[index] = constraint.Weight;
                for (int term = 0;
                     term < constraint.LocalIndices.Length;
                     term++)
                {
                    materialIndices[materialOffset + term] =
                        constraint.LocalIndices[term];
                    materialCoefficients[materialOffset + term] =
                        constraint.Coefficients[term];
                }
                materialOffset += constraint.LocalIndices.Length;
            }
            materialOffsets[materialCount] = materialOffset;
        }
    }
}
