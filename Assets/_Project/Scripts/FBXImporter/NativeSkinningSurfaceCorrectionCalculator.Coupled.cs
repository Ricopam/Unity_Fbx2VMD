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
        private static NativeSkinningSurfaceCorrectionResult EvaluateTimedResult(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> initialVertices,
            Vector3[] vertices,
            NativeSkinningSurfaceContract contract,
            CorrectionConfiguration configuration,
            bool allowsStrainRecovery,
            IReadOnlyList<FrameEdge> frameEdges,
            int initialSharpFoldCount,
            IReadOnlyList<int> restShapeRecoveryPairIndices,
            IReadOnlyList<int> sharpFoldPairIndices,
            SolverBuffers buffers,
            NativeSkinningSolverTiming timing)
        {
            NativeSkinningSurfaceCorrectionResult result = EvaluateResult(
                baselineVertices,
                vertices,
                contract,
                frameEdges,
                initialSharpFoldCount,
                restShapeRecoveryPairIndices);
            if (!IsWithinQuality(result, contract, configuration) &&
                TryCalculateCoupledProjectionFallback(
                    baselineVertices,
                    initialVertices,
                    contract,
                    configuration,
                    allowsStrainRecovery,
                    frameEdges,
                    initialSharpFoldCount,
                    restShapeRecoveryPairIndices,
                    sharpFoldPairIndices,
                    buffers,
                    timing,
                    out NativeSkinningSurfaceCorrectionResult fallbackResult))
            {
                result = fallbackResult;
            }
            return result;
        }

        private static bool TryCalculateCoupledProjectionFallback(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> initialVertices,
            NativeSkinningSurfaceContract contract,
            CorrectionConfiguration configuration,
            bool allowsStrainRecovery,
            IReadOnlyList<FrameEdge> frameEdges,
            int initialSharpFoldCount,
            IReadOnlyList<int> restShapeRecoveryPairIndices,
            IReadOnlyList<int> sharpFoldPairIndices,
            SolverBuffers buffers,
            NativeSkinningSolverTiming timing,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            result = null;
            if (restShapeRecoveryPairIndices.Count == 0)
            {
                return false;
            }

            Vector3[] sharpFoldCorrectedVertices = sharpFoldPairIndices.Count > 0
                ? ApplyCorrectionPass(
                    baselineVertices,
                    initialVertices.ToArray(),
                    contract,
                    configuration,
                    sharpFoldPairIndices,
                    false,
                    allowsStrainRecovery,
                    frameEdges,
                    buffers,
                    timing)
                : initialVertices.ToArray();
            return TryRunCoupledProjectionRounds(
                baselineVertices,
                sharpFoldCorrectedVertices,
                contract,
                configuration,
                frameEdges,
                initialSharpFoldCount,
                restShapeRecoveryPairIndices,
                sharpFoldPairIndices,
                out result);
        }

        private static bool TryRunCoupledProjectionRounds(
            IReadOnlyList<Vector3> baselineVertices,
            Vector3[] sharpFoldCorrectedVertices,
            NativeSkinningSurfaceContract contract,
            CorrectionConfiguration configuration,
            IReadOnlyList<FrameEdge> frameEdges,
            int initialSharpFoldCount,
            IReadOnlyList<int> restShapeRecoveryPairIndices,
            IReadOnlyList<int> sharpFoldPairIndices,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            result = null;
            Vector3[] projectionStartVertices = sharpFoldCorrectedVertices;
            for (int round = 0; round < CoupledProjectionRoundCount; round++)
            {
                if (!NativeSkinningCoupledDihedralProjectionCalculator.TryProject(
                        baselineVertices, projectionStartVertices, contract,
                        restShapeRecoveryPairIndices, sharpFoldPairIndices,
                        RestSmoothAngleDegrees, SharpFoldAngleDegrees,
                        ProjectionAngleDegrees,
                        out Vector3[] projectedVertices))
                {
                    return false;
                }

                if (TryAcceptRoundResult(
                        baselineVertices, projectionStartVertices,
                        projectedVertices, contract, configuration, frameEdges,
                        initialSharpFoldCount, restShapeRecoveryPairIndices,
                        ref result))
                {
                    return true;
                }
                projectionStartVertices = projectedVertices;
            }
            return false;
        }

        private static bool TryAcceptRoundResult(
            IReadOnlyList<Vector3> baselineVertices,
            Vector3[] projectionStartVertices,
            Vector3[] projectedVertices,
            NativeSkinningSurfaceContract contract,
            CorrectionConfiguration configuration,
            IReadOnlyList<FrameEdge> frameEdges,
            int initialSharpFoldCount,
            IReadOnlyList<int> restShapeRecoveryPairIndices,
            ref NativeSkinningSurfaceCorrectionResult result)
        {
            result = EvaluateResult(
                baselineVertices,
                projectedVertices,
                contract,
                frameEdges,
                initialSharpFoldCount,
                restShapeRecoveryPairIndices);
            if (IsWithinQuality(result, contract, configuration))
            {
                return true;
            }
            return TryFindQualityPreservingCoupledBlend(
                baselineVertices,
                projectionStartVertices,
                projectedVertices,
                contract,
                configuration,
                frameEdges,
                initialSharpFoldCount,
                restShapeRecoveryPairIndices,
                out result);
        }

        private static bool TryFindQualityPreservingCoupledBlend(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> projectionStartVertices,
            IReadOnlyList<Vector3> projectedVertices,
            NativeSkinningSurfaceContract contract,
            CorrectionConfiguration configuration,
            IReadOnlyList<FrameEdge> frameEdges,
            int initialSharpFoldCount,
            IReadOnlyList<int> restShapeRecoveryPairIndices,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            result = null;
            var blendedVertices = new Vector3[baselineVertices.Count];
            for (int step = CoupledBlendSearchStepCount - 1; step > 0; step--)
            {
                float blend = step / (float)CoupledBlendSearchStepCount;
                for (int vertexIndex = 0;
                     vertexIndex < blendedVertices.Length;
                     vertexIndex++)
                {
                    blendedVertices[vertexIndex] = Vector3.Lerp(
                        projectionStartVertices[vertexIndex],
                        projectedVertices[vertexIndex],
                        blend);
                }

                NativeSkinningSurfaceCorrectionResult candidate = EvaluateResult(
                    baselineVertices,
                    blendedVertices,
                    contract,
                    frameEdges,
                    initialSharpFoldCount,
                    restShapeRecoveryPairIndices);
                if (IsWithinQuality(candidate, contract, configuration))
                {
                    result = candidate;
                    return true;
                }
            }
            return false;
        }


    }
}
