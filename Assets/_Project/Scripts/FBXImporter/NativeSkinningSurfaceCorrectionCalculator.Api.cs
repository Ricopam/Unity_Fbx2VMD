using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 공개 진입 오버로드와 품질 게이트 — 본체에서 이관한 partial.
    /// </summary>
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
        internal static bool TryCalculateFast(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            return TryCalculateTimed(
                baselineVertices,
                baselineVertices,
                contract,
                FastConfiguration,
                false,
                true,
                null,
                out result);
        }

        // 계측 경로 — 리플렉션 호출과 이름 충돌하지 않게 별도 명명한다.
        internal static bool TryCalculateFastTimed(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            NativeSkinningSolverTiming timing,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            return TryCalculateTimed(
                baselineVertices,
                baselineVertices,
                contract,
                FastConfiguration,
                false,
                true,
                timing,
                out result);
        }

        internal static bool TryCalculateFastValidatedFrame(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            return TryCalculateTimed(
                baselineVertices,
                baselineVertices,
                contract,
                FastConfiguration,
                false,
                false,
                null,
                out result);
        }

        internal static bool TryCalculateFastValidatedFrameTimed(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            NativeSkinningSolverTiming timing,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            return TryCalculateTimed(
                baselineVertices,
                baselineVertices,
                contract,
                FastConfiguration,
                false,
                false,
                timing,
                out result);
        }

        internal static bool TryCalculateStrong(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            return TryCalculateTimed(
                baselineVertices,
                baselineVertices,
                contract,
                StrongConfiguration,
                false,
                true,
                null,
                out result);
        }

        internal static bool TryCalculateStrongTimed(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            NativeSkinningSolverTiming timing,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            return TryCalculateTimed(
                baselineVertices,
                baselineVertices,
                contract,
                StrongConfiguration,
                false,
                true,
                timing,
                out result);
        }

        internal static bool TryCalculateStrongValidatedFrame(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            return TryCalculateStrongValidatedFrameTimed(
                baselineVertices, contract, null, out result);
        }

        internal static bool TryCalculateStrongValidatedFrameTimed(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            NativeSkinningSolverTiming timing,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            if (!TryCalculateTimed(
                    baselineVertices,
                    baselineVertices,
                    contract,
                    StrongConfiguration,
                    false,
                    false,
                    timing,
                    out result))
            {
                return false;
            }
            if (IsWithinQuality(result, contract, StrongConfiguration))
            {
                return true;
            }
            // 축소 예산 재시도가 더 엄격한 품질 게이트를 통과하면 그 결과를 채택한다.
            if (TryCalculateTimed(
                    baselineVertices,
                    baselineVertices,
                    contract,
                    StrongRetryConfiguration,
                    false,
                    false,
                    timing,
                    out NativeSkinningSurfaceCorrectionResult retry) &&
                IsWithinQuality(retry, contract, StrongRetryConfiguration))
            {
                result = retry;
            }
            NativeSkinningSurfaceCorrectionResult headroom = null;
            if (!IsWithinQuality(result, contract, StrongConfiguration) &&
                TryCalculateTimed(
                    baselineVertices,
                    baselineVertices,
                    contract,
                    StrongHeadroomConfiguration,
                    false,
                    false,
                    timing,
                    out headroom) &&
                IsWithinQuality(headroom, contract, StrongConfiguration))
            {
                result = headroom;
            }
            // 예산과 무관하게 국소 최적해에 정체하는 입력이 있어
            // 직전 결과를 초기 추정으로 재투입해 basin 이탈을 시도한다.
            if (!IsWithinQuality(result, contract, StrongConfiguration) &&
                result?.CorrectedVertices != null &&
                TryCalculateTimed(
                    baselineVertices,
                    result.CorrectedVertices,
                    contract,
                    StrongConfiguration,
                    false,
                    false,
                    timing,
                    out NativeSkinningSurfaceCorrectionResult chained) &&
                IsWithinQuality(chained, contract, StrongConfiguration))
            {
                result = chained;
            }
            NativeSkinningSurfaceCorrectionResult wide = null;
            if (!IsWithinQuality(result, contract, StrongConfiguration) &&
                TryCalculateTimed(
                    baselineVertices,
                    baselineVertices,
                    contract,
                    StrongWideConfiguration,
                    false,
                    false,
                    timing,
                    out wide) &&
                IsWithinQuality(wide, contract, StrongConfiguration))
            {
                result = wide;
            }
            // 접힘이 해소된 후보를 원본 쪽으로 줄여 신장 한도 안의 결과만 재사용함.
            if (!IsWithinQuality(result, contract, StrongConfiguration) &&
                headroom != null && headroom.IsSafe && !headroom.UsedRestShapeRecovery &&
                TryFindQualityPreservingCoupledBlend(
                    baselineVertices,
                    baselineVertices,
                    headroom.CorrectedVertices,
                    contract,
                    StrongConfiguration,
                    BuildFrameEdges(baselineVertices, contract),
                    headroom.InitialSharpFoldCount,
                    Array.Empty<int>(),
                    out NativeSkinningSurfaceCorrectionResult blended))
            {
                result = blended;
            }
            if (!IsWithinQuality(result, contract, StrongConfiguration) &&
                TryCalculateTimed(
                    baselineVertices,
                    baselineVertices,
                    contract,
                    StrongExtendedConfiguration,
                    false,
                    false,
                    timing,
                    out NativeSkinningSurfaceCorrectionResult extended) &&
                IsWithinQuality(extended, contract, StrongConfiguration))
            {
                result = extended;
            }
            if (!IsWithinQuality(result, contract, StrongConfiguration) &&
                headroom != null && headroom.IsSafe && !headroom.UsedRestShapeRecovery &&
                TryCalculateStrainRestoredHeadroom(
                    baselineVertices, headroom, contract, timing,
                    out NativeSkinningSurfaceCorrectionResult restored))
            {
                result = restored;
            }
            // 넓은 영역으로 접힘을 줄인 결과를 다시 풀어 같은 국소 해의 반복을 피함.
            if (!IsWithinQuality(result, contract, StrongConfiguration) &&
                wide != null && !wide.UsedRestShapeRecovery &&
                wide.ResidualSharpFoldCount < result.ResidualSharpFoldCount &&
                wide.NewSharpFoldCount == 0 && wide.NewDegenerateFaceCount == 0 &&
                wide.ReversedFaceCount == 0 &&
                TryCalculateTimed(
                    baselineVertices,
                    wide.CorrectedVertices,
                    contract,
                    StrongWideConfiguration,
                    false,
                    false,
                    timing,
                    out NativeSkinningSurfaceCorrectionResult reseeded) &&
                IsWithinQuality(reseeded, contract, StrongConfiguration))
            {
                result = reseeded;
            }
            return true;
        }

        internal static bool TryRecoverFailedFrame(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            NativeSkinningSurfaceCorrectionResult previous,
            Vector3[] restVertices,
            BoneWeight[] boneWeights,
            NativeSkinningDualQuaternionTransform[] boneTransforms,
            NativeSkinningSolverTiming timing,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            result = null;
            var allPairs = Enumerable.Range(0, contract.FacePairs.Length);
            var originalPairs = new HashSet<int>();
            UpdateActiveSharpFoldPairs(baselineVertices, contract, allPairs, originalPairs);
            FrameEdge[] edges = BuildFrameEdges(baselineVertices, contract);
            NativeSkinningSurfaceCorrectionResult initial = previous;
            NativeSkinningSurfaceCorrectionResult displayRepairSeed = null;

            if (boneTransforms != null && TryDualQuaternionSeed(out result))
            {
                return true;
            }

            int[] recoveryPairs = originalPairs.Where(pairIndex =>
                IsRestShapeRecoveryPair(baselineVertices, contract, pairIndex)).ToArray();
            var residualPairs = new HashSet<int>();
            for (int retry = 0; retry < ResidualFoldRetryCount; retry++)
            {
                residualPairs.Clear();
                UpdateActiveCorrectionPairs(baselineVertices, previous.CorrectedVertices,
                    contract, allPairs, false, residualPairs);
                if (residualPairs.Count == 0)
                {
                    break;
                }

                Vector3[] corrected = ApplyCorrectionPass(baselineVertices,
                    previous.CorrectedVertices.ToArray(), contract, StrongWideConfiguration,
                    residualPairs, false, false, edges, new SolverBuffers(), timing);
                NativeSkinningSurfaceCorrectionResult candidate = EvaluateResult(
                    baselineVertices, corrected, contract, edges, originalPairs.Count, recoveryPairs);
                if (TryAcceptRetry(candidate))
                {
                    result = candidate;
                    return true;
                }
                if (candidate.ResidualSharpFoldCount >= previous.ResidualSharpFoldCount ||
                    candidate.NewSharpFoldCount != 0 || candidate.NewDegenerateFaceCount != 0 ||
                    candidate.ReversedFaceCount != 0)
                {
                    break;
                }
                previous = candidate;
            }
            return (displayRepairSeed != null && TryProjectJointConstraints(
                    baselineVertices, contract, displayRepairSeed, originalPairs, recoveryPairs, edges, out result)) ||
                TryProjectJointConstraints(
                baselineVertices, contract, initial, originalPairs, recoveryPairs, edges, out result);

            bool TryAcceptRetry(NativeSkinningSurfaceCorrectionResult candidate)
            {
                if (!IsWithinStrongQuality(candidate, contract) ||
                    !IsCorrectedFrameWithinStrongQuality(baselineVertices, candidate.CorrectedVertices, contract))
                {
                    return false;
                }
                if (HasDisplayStrainMargin(candidate))
                {
                    return true;
                }
                if (displayRepairSeed == null ||
                    candidate.MaximumEdgeLengthStrain < displayRepairSeed.MaximumEdgeLengthStrain)
                {
                    displayRepairSeed = candidate;
                }
                return false;
            }

            bool TryDualQuaternionSeed(out NativeSkinningSurfaceCorrectionResult recovered)
            {
                recovered = null;
                LocalWorkset workset = BuildLocalWorkset(baselineVertices, contract, originalPairs,
                    StrongExtendedConfiguration.WorksetRingCount, edges, new SolverBuffers());
                int[] indices = workset.ConstrainedVertices;
                var dqVertices = new Vector3[indices.Length];
                for (int index = 0; index < indices.Length; index++)
                {
                    int vertex = indices[index];
                    if (!NativeSkinningDualQuaternionVertexCalculator.TryTransformVertex(
                            restVertices[vertex], boneWeights[vertex], boneTransforms, out dqVertices[index]))
                    {
                        return false;
                    }
                }

                var localPositions = new Vector3[indices.Length];
                foreach (float influence in DualQuaternionSeedInfluences)
                {
                    Vector3[] seed = baselineVertices.ToArray();
                    for (int index = 0; index < indices.Length; index++)
                    {
                        Vector3 baseline = seed[indices[index]];
                        localPositions[index] = baseline + Vector3.ClampMagnitude(
                            (dqVertices[index] - baseline) * influence, MaximumDualQuaternionSeedDisplacement);
                    }
                    ProjectEdgeLengthBounds(seed, localPositions, indices, workset.Edges,
                        StrongConfiguration.MaximumEdgeLengthStrain - EdgeLengthProjectionMargin,
                        StrongConfiguration.EdgeLengthProjectionIterations);
                    for (int index = 0; index < indices.Length; index++)
                    {
                        seed[indices[index]] = localPositions[index];
                    }

                    // DQ는 국소 풀이의 초기값으로 쓰고 최종 품질은 원본 메시 기준으로 판정함.
                    NativeSkinningSurfaceCorrectionResult seedQuality = EvaluateResult(
                        baselineVertices, seed, contract, edges, originalPairs.Count, Array.Empty<int>());
                    if (seedQuality.NewDegenerateFaceCount == 0 && seedQuality.ReversedFaceCount == 0 &&
                        seedQuality.MaximumEdgeLengthStrain <= StrongConfiguration.MaximumEdgeLengthStrain &&
                        TryCalculateTimed(baselineVertices, seed, contract, StrongWideConfiguration,
                            false, true, timing, out NativeSkinningSurfaceCorrectionResult candidate) &&
                        TryAcceptRetry(candidate))
                    {
                        recovered = candidate;
                        return true;
                    }
                }
                return false;
            }
        }

        private static bool HasDisplayStrainMargin(NativeSkinningSurfaceCorrectionResult result) =>
            result.UsedRestShapeRecovery || result.MaximumEdgeLengthStrain <=
            StrongConfiguration.MaximumEdgeLengthStrain - DisplayEdgeLengthStrainMargin;

        private static bool TryCalculateStrainRestoredHeadroom(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceCorrectionResult headroom,
            NativeSkinningSurfaceContract contract,
            NativeSkinningSolverTiming timing,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            result = null;
            var activePairs = new HashSet<int>();
            UpdateActiveSharpFoldPairs(
                baselineVertices, contract,
                Enumerable.Range(0, contract.FacePairs.Length), activePairs);
            FrameEdge[] edges = BuildFrameEdges(baselineVertices, contract);
            var buffers = new SolverBuffers();
            LocalWorkset workset = BuildLocalWorkset(
                baselineVertices, contract, activePairs,
                StrongWideConfiguration.WorksetRingCount, edges, buffers);
            Vector3[] vertices = headroom.CorrectedVertices.ToArray();
            var localPositions = new Vector3[workset.ConstrainedVertices.Length];
            for (int round = 0; round < HeadroomStrainProjectionRoundCount; round++)
            {
                for (int localIndex = 0; localIndex < localPositions.Length; localIndex++)
                {
                    localPositions[localIndex] =
                        vertices[workset.ConstrainedVertices[localIndex]];
                }
                // 한 번의 투영은 0.05mm로 제한되므로 직전 후보에서 작은 단계를 이어감.
                ProjectEdgeLengthBounds(
                    vertices, localPositions, workset.ConstrainedVertices, workset.Edges,
                    StrongConfiguration.MaximumEdgeLengthStrain - EdgeLengthProjectionMargin,
                    StrongConfiguration.EdgeLengthProjectionIterations);
                Vector3[] projected = vertices.ToArray();
                bool hasMovement = false;
                for (int localIndex = 0; localIndex < localPositions.Length; localIndex++)
                {
                    int vertexIndex = workset.ConstrainedVertices[localIndex];
                    projected[vertexIndex] = localPositions[localIndex];
                    hasMovement |= !projected[vertexIndex].Equals(vertices[vertexIndex]);
                }
                if (!AreVerticesFinite(projected))
                {
                    return false;
                }
                NativeSkinningSurfaceCorrectionResult seed = EvaluateResult(
                    baselineVertices, projected, contract, edges,
                    headroom.InitialSharpFoldCount, Array.Empty<int>());
                if (seed.NewDegenerateFaceCount > 0 || seed.ReversedFaceCount > 0 ||
                    seed.MaximumVertexDisplacement > contract.ArmChainLength *
                        StrongConfiguration.MaximumTotalDisplacementToArmLengthRatio)
                {
                    return false;
                }
                if (seed.MaximumEdgeLengthStrain <= StrongConfiguration.MaximumEdgeLengthStrain)
                {
                    // 중간 접힘은 내부 초기값에만 허용하고 원본 기준 strict로 재검증함.
                    return TryCalculateTimed(
                        baselineVertices, projected, contract, StrongConfiguration,
                        false, false, timing, out result) &&
                        IsWithinQuality(result, contract, StrongConfiguration);
                }
                if (!hasMovement)
                {
                    return false;
                }
                vertices = projected;
            }
            return false;
        }

        internal static bool AreVerticesFinite(
            IReadOnlyList<Vector3> vertices)
        {
            return vertices != null && vertices.All(IsFinite);
        }

        internal static bool IsWithinFastQuality(
            NativeSkinningSurfaceCorrectionResult result,
            NativeSkinningSurfaceContract contract)
        {
            return IsWithinQuality(result, contract, FastConfiguration);
        }

        internal static bool IsWithinStrongQuality(
            NativeSkinningSurfaceCorrectionResult result,
            NativeSkinningSurfaceContract contract)
        {
            return IsWithinQuality(result, contract, StrongConfiguration);
        }

        internal static bool IsCorrectedFrameWithinStrongQuality(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> correctedVertices,
            NativeSkinningSurfaceContract contract)
        {
            var activePairs = new HashSet<int>();
            UpdateActiveSharpFoldPairs(baselineVertices, contract,
                Enumerable.Range(0, contract.FacePairs.Length), activePairs);
            int[] recoveryPairs = activePairs.Where(index =>
                IsRestShapeRecoveryPair(baselineVertices, contract, index)).ToArray();
            return IsWithinStrongQuality(EvaluateResult(baselineVertices,
                correctedVertices as Vector3[] ?? correctedVertices.ToArray(),
                contract, BuildFrameEdges(baselineVertices, contract), activePairs.Count,
                recoveryPairs), contract);
        }

        private static bool IsWithinQuality(
            NativeSkinningSurfaceCorrectionResult result,
            NativeSkinningSurfaceContract contract,
            CorrectionConfiguration configuration)
        {
            bool hasAcceptableSurfaceStrain = result != null &&
                (result.UsedRestShapeRecovery
                    ? result.MaximumRestEdgeStrainIncrease <=
                        NativeSkinningRestShapeRecoveryCalculator
                            .MaximumRestStrainIncrease +
                        QualityComparisonTolerance &&
                      result.UnresolvedRestShapeRecoveryCount == 0
                    : result.MaximumEdgeLengthStrain <=
                        configuration.MaximumEdgeLengthStrain);
            return result != null &&
                   contract != null &&
                   result.IsSafe &&
                   hasAcceptableSurfaceStrain &&
                   result.MaximumVertexDisplacement <=
                       contract.ArmChainLength *
                       configuration.MaximumTotalDisplacementToArmLengthRatio +
                       QualityComparisonTolerance;
        }
    }
}
