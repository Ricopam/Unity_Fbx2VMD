using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 스키닝된 팔 표면의 급접힘을 위치 기반 제약으로 완화함.
    /// </summary>
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
        private static void CountSharpFoldPairs(
            IReadOnlyList<Vector3> baselineVertices,
            Vector3[] vertices,
            NativeSkinningSurfaceContract contract,
            out int residualSharpFoldCount,
            out int newSharpFoldCount)
        {
            residualSharpFoldCount = 0;
            newSharpFoldCount = 0;
            for (int pairIndex = 0; pairIndex < contract.FacePairs.Length; pairIndex++)
            {
                NativeSkinningFacePair pair = contract.FacePairs[pairIndex];
                bool baselineValid = NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                    baselineVertices,
                    pair,
                    out float baselineAngle);
                if (!NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                        vertices,
                        pair,
                        out float candidateAngle))
                {
                    continue;
                }
                if (contract.RestAnglesDegrees[pairIndex] <= RestSmoothAngleDegrees &&
                    candidateAngle > SharpFoldAngleDegrees)
                {
                    residualSharpFoldCount++;
                }
                if (baselineValid &&
                    baselineAngle <= SharpFoldAngleDegrees &&
                    candidateAngle > SharpFoldAngleDegrees)
                {
                    newSharpFoldCount++;
                }
            }
        }

        private static int[] CollectCorrectedVertexIndices(
            IReadOnlyList<Vector3> baselineVertices,
            Vector3[] vertices)
        {
            return Enumerable.Range(0, vertices.Length)
                .Where(index =>
                    (vertices[index] - baselineVertices[index]).sqrMagnitude >
                    MinimumStoredCorrectionSquaredMagnitude)
                .ToArray();
        }

        private static void MeasureRestShapeQuality(
            IReadOnlyList<Vector3> baselineVertices,
            Vector3[] vertices,
            NativeSkinningSurfaceContract contract,
            IReadOnlyList<FrameEdge> edges,
            IReadOnlyList<int> restShapeRecoveryPairIndices,
            bool usedRestShapeRecovery,
            out float maximumRestEdgeStrainIncrease,
            out float minimumRestShapeAngleImprovementDegrees,
            out float minimumRestEdgeLengthRatioBeforeRecovery,
            out float minimumRestEdgeLengthRatioAfterRecovery,
            out int unresolvedRestShapeRecoveryCount)
        {
            maximumRestEdgeStrainIncrease = usedRestShapeRecovery
                ? edges.Max(edge => CalculateEdgeStrainIncrease(edge, vertices))
                : 0f;
            minimumRestShapeAngleImprovementDegrees =
                CalculateMinimumRestShapeAngleImprovement(
                    baselineVertices,
                    vertices,
                    contract,
                    restShapeRecoveryPairIndices);
            minimumRestEdgeLengthRatioBeforeRecovery =
                usedRestShapeRecovery
                    ? CalculateMinimumRestEdgeLengthRatio(
                        baselineVertices,
                        edges)
                    : 0f;
            minimumRestEdgeLengthRatioAfterRecovery =
                usedRestShapeRecovery
                    ? CalculateMinimumRestEdgeLengthRatio(vertices, edges)
                    : 0f;
            unresolvedRestShapeRecoveryCount = restShapeRecoveryPairIndices
                .Count(pairIndex => IsRestShapeRecoveryPairUnresolved(
                    vertices,
                    contract,
                    pairIndex));
        }

        private static float CalculateEdgeStrainIncrease(FrameEdge edge, Vector3[] vertices)
        {
            return Mathf.Max(
                0f,
                NativeSkinningRestShapeRecoveryCalculator.CalculateStrainIncrease(
                    edge.BaselineLength,
                    Vector3.Distance(
                        vertices[edge.FirstVertex],
                        vertices[edge.SecondVertex]),
                    edge.RestLength));
        }

        private static void CountDegenerateFaces(
            IReadOnlyList<Vector3> baselineVertices,
            Vector3[] vertices,
            NativeSkinningSurfaceContract contract,
            out int newDegenerateFaceCount,
            out int reversedFaceCount)
        {
            newDegenerateFaceCount = 0;
            reversedFaceCount = 0;
            foreach (int faceIndex in contract.AffectedFaceIndices)
            {
                bool baselineValid = TryCalculateFaceNormal(
                    baselineVertices,
                    contract.Triangles,
                    faceIndex,
                    out Vector3 baselineNormal);
                bool candidateValid = TryCalculateFaceNormal(
                    vertices,
                    contract.Triangles,
                    faceIndex,
                    out Vector3 candidateNormal);
                if (baselineValid && !candidateValid)
                {
                    newDegenerateFaceCount++;
                }
                else if (baselineValid && candidateValid &&
                         Vector3.Dot(baselineNormal, candidateNormal) < 0f)
                {
                    reversedFaceCount++;
                }
            }
        }

        private static bool HasValidInput(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> initialVertices,
            NativeSkinningSurfaceContract contract,
            bool shouldValidateVertices)
        {
            return baselineVertices != null &&
                   initialVertices != null &&
                   contract != null &&
                   baselineVertices.Count == contract.VertexCount &&
                   initialVertices.Count == contract.VertexCount &&
                   contract.RestVertices != null &&
                   contract.RestVertices.Length == contract.VertexCount &&
                   contract.FacePairs.Length > 0 &&
                   contract.RestShapeRecoveryMaximumCosines != null &&
                   contract.RestShapeRecoveryMaximumCosines.Length ==
                       contract.FacePairs.Length &&
                   contract.FacePairRestLengths != null &&
                   contract.FacePairRestLengths.Length == contract.FacePairs.Length &&
                   contract.AffectedFaceIndices.Length > 0 &&
                   contract.ArmChainLength > 0f &&
                   (!shouldValidateVertices ||
                    AreVerticesFinite(baselineVertices) &&
                    (ReferenceEquals(baselineVertices, initialVertices) ||
                     AreVerticesFinite(initialVertices)));
        }

        private static NativeSkinningSurfaceCorrectionResult EvaluateResult(
            IReadOnlyList<Vector3> baselineVertices,
            Vector3[] vertices,
            NativeSkinningSurfaceContract contract,
            IReadOnlyList<FrameEdge> edges,
            int initialSharpFoldCount,
            IReadOnlyList<int> restShapeRecoveryPairIndices)
        {
            CountSharpFoldPairs(baselineVertices, vertices, contract,
                out int residualSharpFoldCount, out int newSharpFoldCount);
            CountDegenerateFaces(baselineVertices, vertices, contract,
                out int newDegenerateFaceCount, out int reversedFaceCount);

            int[] affectedVertices = CollectAffectedVertices(contract);
            int[] correctedVertexIndices = CollectCorrectedVertexIndices(
                baselineVertices,
                vertices);
            bool usedRestShapeRecovery = restShapeRecoveryPairIndices.Count > 0;
            MeasureRestShapeQuality(
                baselineVertices, vertices, contract, edges,
                restShapeRecoveryPairIndices, usedRestShapeRecovery,
                out float maximumRestEdgeStrainIncrease,
                out float minimumRestShapeAngleImprovementDegrees,
                out float minimumRestEdgeLengthRatioBeforeRecovery,
                out float minimumRestEdgeLengthRatioAfterRecovery,
                out int unresolvedRestShapeRecoveryCount);
            // vertices는 재사용 버퍼를 별칭할 수 있어 fallback 덮어쓰기를 막기 위해 스냅샷한다.
            return CreateEvaluatedResult(
                baselineVertices, vertices, contract, edges,
                initialSharpFoldCount, residualSharpFoldCount,
                newSharpFoldCount, newDegenerateFaceCount, reversedFaceCount,
                affectedVertices, correctedVertexIndices, usedRestShapeRecovery,
                maximumRestEdgeStrainIncrease,
                minimumRestShapeAngleImprovementDegrees,
                minimumRestEdgeLengthRatioBeforeRecovery,
                minimumRestEdgeLengthRatioAfterRecovery,
                unresolvedRestShapeRecoveryCount);
        }

        private static int[] CollectAffectedVertices(NativeSkinningSurfaceContract contract)
        {
            return contract.AffectedFaceIndices
                .SelectMany(faceIndex => GetFaceVertices(contract.Triangles, faceIndex))
                .Distinct()
                .ToArray();
        }

        private static NativeSkinningSurfaceCorrectionResult CreateEvaluatedResult(
            IReadOnlyList<Vector3> baselineVertices,
            Vector3[] vertices,
            NativeSkinningSurfaceContract contract,
            IReadOnlyList<FrameEdge> edges,
            int initialSharpFoldCount,
            int residualSharpFoldCount,
            int newSharpFoldCount,
            int newDegenerateFaceCount,
            int reversedFaceCount,
            int[] affectedVertices,
            int[] correctedVertexIndices,
            bool usedRestShapeRecovery,
            float maximumRestEdgeStrainIncrease,
            float minimumRestShapeAngleImprovementDegrees,
            float minimumRestEdgeLengthRatioBeforeRecovery,
            float minimumRestEdgeLengthRatioAfterRecovery,
            int unresolvedRestShapeRecoveryCount)
        {
            return new NativeSkinningSurfaceCorrectionResult(
                (Vector3[])vertices.Clone(),
                correctedVertexIndices,
                initialSharpFoldCount,
                residualSharpFoldCount,
                newSharpFoldCount,
                newDegenerateFaceCount,
                reversedFaceCount,
                affectedVertices.Max(index => Vector3.Distance(
                    vertices[index],
                    baselineVertices[index])),
                edges.Max(edge => Mathf.Abs(
                    Vector3.Distance(
                        vertices[edge.FirstVertex],
                        vertices[edge.SecondVertex]) /
                    edge.BaselineLength - 1f)),
                usedRestShapeRecovery,
                maximumRestEdgeStrainIncrease,
                minimumRestShapeAngleImprovementDegrees,
                minimumRestEdgeLengthRatioBeforeRecovery,
                minimumRestEdgeLengthRatioAfterRecovery,
                unresolvedRestShapeRecoveryCount);
        }

        internal static bool IsRestShapeRecoveryPairUnresolved(
            IReadOnlyList<Vector3> vertices,
            NativeSkinningSurfaceContract contract,
            int pairIndex)
        {
            NativeSkinningFacePair pair = contract.FacePairs[pairIndex];
            return !NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                       vertices,
                       pair,
                       out float angleDegrees) ||
                   NativeSkinningRestShapeCollapseDetector.IsSeverelyCollapsed(
                       vertices,
                       pair,
                       contract.FacePairRestLengths[pairIndex],
                       contract.RestAnglesDegrees[pairIndex],
                       angleDegrees);
        }

        private static float CalculateMinimumRestEdgeLengthRatio(
            IReadOnlyList<Vector3> vertices,
            IReadOnlyList<FrameEdge> edges)
        {
            return edges.Min(edge =>
                Vector3.Distance(
                    vertices[edge.FirstVertex],
                    vertices[edge.SecondVertex]) /
                edge.RestLength);
        }

        private static float CalculateMinimumRestShapeAngleImprovement(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> correctedVertices,
            NativeSkinningSurfaceContract contract,
            IReadOnlyList<int> recoveryPairIndices)
        {
            if (recoveryPairIndices.Count == 0)
            {
                return 0f;
            }

            float minimumImprovement = float.PositiveInfinity;
            foreach (int pairIndex in recoveryPairIndices)
            {
                NativeSkinningFacePair pair = contract.FacePairs[pairIndex];
                if (!NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                        baselineVertices,
                        pair,
                        out float baselineAngleDegrees) ||
                    !NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                        correctedVertices,
                        pair,
                        out float correctedAngleDegrees))
                {
                    return float.NegativeInfinity;
                }

                minimumImprovement = Mathf.Min(
                    minimumImprovement,
                    baselineAngleDegrees - correctedAngleDegrees);
            }
            return minimumImprovement;
        }
    }
}
