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
    /// 스키닝된 팔 표면의 급접힘을 위치 기반 제약으로 완화함.
    /// </summary>
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
        private const float RestSmoothAngleDegrees =
            NativeSkinningSurfaceDefectDetector.MaximumCorrectableRestAngleDegrees;
        private const float SharpFoldAngleDegrees =
            NativeSkinningSurfaceDefectDetector.MinimumSharpFoldAngleDegrees;
        private const float ProjectionAngleDegrees = 145f;
        private const float RestShapeRecoveryAngleDegrees = 140f;
        private const float RestShapeMinimumAngleImprovementDegrees = 5f;
        private const float RestShapeActiveSetActivationMargin = 0.0005f;
        private const float RestShapeMaximumProjectionCorrection = 0.000025f;
        private const int RestShapeActiveSetProjectionPasses = 8;
        private const int DefaultWorksetRingCount = 2;
        private const float DisplacementSmoothnessWeight = 1f;
        private const float DisplacementAttachmentWeight = 0.02f;
        private const float RestShapeRecoveryAttachmentWeight = 0.05f;
        private const float BendingWeight = 4f;
        private const float EdgeStrainBarrierWeight = 4f;
        private const float EdgeStrainActivation = 0.015f;
        private const float EdgeLengthProjectionMargin = 0.0001f;
        // LBS에서 저장한 희소 보정량을 BakeMesh에 적용할 때의 수치 차이를 흡수함.
        private const float DisplayEdgeLengthStrainMargin = EdgeLengthProjectionMargin * 0.5f;
        private const float MaximumAbsoluteStep = 0.00005f;
        private const float MaximumRelativeStep = 0.02f;
        private const float QualityComparisonTolerance = 0.0000001f;
        // 상대 에너지 개선이 이 값 미만이면 LG 반복을 조기 종료한다.
        private const double EarlyConvergenceRelativeImprovement = 0.001;
        private const float MinimumStoredCorrectionSquaredMagnitude =
            0.0000000000000001f;
        private const int LineSearchIterations = 10;
        private const int CoupledBlendSearchStepCount = 32;
        private const int CoupledProjectionRoundCount = 8;
        private const int HeadroomStrainProjectionRoundCount = 8;
        private const int ResidualFoldRetryCount = 2;
        private const float MaximumDualQuaternionSeedDisplacement = 0.001f;
        private static readonly float[] DualQuaternionSeedInfluences = { 0.1f, 0.25f };
        private const int OrientationSearchIterations = 24;
        private const float OrientationBarrierWeight = 4f;
        private const float OrientationActivationRatio = 0.25f;
        private const float MinimumSignedAreaRatio = 0.05f;
        private const float OrientationSafetyFactor = 0.95f;
        private const float MinimumNormalSquaredMagnitude = 0.0000000000000001f;
        private static readonly CorrectionConfiguration StrongConfiguration =
            new CorrectionConfiguration(640, 64, 32, 0.03f, 0.055f);
        // 예산 크기에 따라 수렴 지점이 달라지므로 국소 최적해 정체 시 축소 예산으로 재시도한다.
        private static readonly CorrectionConfiguration StrongRetryConfiguration =
            new CorrectionConfiguration(640, 64, 32, 0.03f, 0.02f);
        // 반복 투영이 신장 경계에서 정체하는 경우가 있어 충분한 헤드룸과 넓은 workset으로 재검색한다.
        // 2링은 변위가 좁은 영역에 집중돼 신장 한계에 먼저 도달하므로 3링으로 넓혀 분산한다.
        // 경계 근처(0.035)는 다시 경계에 갇히므로 0.05까지 벌려 경계 끌림을 벗어난다.
        // 수용 결과는 엄격한 게이트로 다시 검증하므로 실질 품질 기준은 유지된다.
        private static readonly CorrectionConfiguration StrongHeadroomConfiguration =
            new CorrectionConfiguration(640, 64, 32, 0.05f, 0.055f, 3);
        // 좁은 영역에서 신장 한계에 정체한 입력만 이웃 영역을 넓혀 재계산함.
        private static readonly CorrectionConfiguration StrongWideConfiguration =
            new CorrectionConfiguration(640, 64, 32, 0.03f, 0.055f, 4);
        // 기존 후보가 모두 실패할 때만 영역을 한 링 넓혀 경계에 집중된 변위를 분산함.
        private static readonly CorrectionConfiguration StrongExtendedConfiguration =
            new CorrectionConfiguration(640, 64, 32, 0.03f, 0.055f, 5);
        // 조기 수렴·웜스타트는 fast 경로에만 허용한다. strong은 정밀 fallback이라 기준 궤적을 유지한다.
        // Burst 경로는 계측에서 회귀(CG 호출당 네이티브 포장 비용이 커널 이득을 상쇄)가
        // 확인돼 비활성으로 둔다 — 코드는 fast 전용 게이트 뒤에 보존한다.
        private static readonly CorrectionConfiguration FastConfiguration =
            new CorrectionConfiguration(80, 16, 0, 0.02f, 0.002512f,
                DefaultWorksetRingCount,
                allowsEarlyConvergence: true,
                allowsWarmStart: true,
                allowsBurst: false);

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

        // 진단 테스트가 이 7인자 시그니처를 리플렉션으로 호출하므로 이름·인수를 유지한다.
        private static bool TryCalculate(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> initialVertices,
            NativeSkinningSurfaceContract contract,
            CorrectionConfiguration configuration,
            bool allowsStrainRecovery,
            bool shouldValidateVertices,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            return TryCalculateTimed(
                baselineVertices,
                initialVertices,
                contract,
                configuration,
                allowsStrainRecovery,
                shouldValidateVertices,
                null,
                out result);
        }

        private static bool TryCalculateTimed(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> initialVertices,
            NativeSkinningSurfaceContract contract,
            CorrectionConfiguration configuration,
            bool allowsStrainRecovery,
            bool shouldValidateVertices,
            NativeSkinningSolverTiming timing,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            result = null;
            if (!HasValidInput(
                    baselineVertices,
                    initialVertices,
                    contract,
                    shouldValidateVertices))
            {
                return false;
            }

            var activePairIndices = new HashSet<int>();
            UpdateActiveSharpFoldPairs(
                baselineVertices,
                contract,
                Enumerable.Range(0, contract.FacePairs.Length),
                activePairIndices);
            int initialSharpFoldCount = activePairIndices.Count;
            if (initialSharpFoldCount == 0)
            {
                result = new NativeSkinningSurfaceCorrectionResult(
                    initialVertices,
                    Array.Empty<int>(),
                    0,
                    0,
                    0,
                    0,
                    0,
                    0f,
                    0f,
                    false,
                    0f,
                    0f,
                    0f,
                    0f,
                    0);
                return true;
            }
            int[] restShapeRecoveryPairIndices = activePairIndices
                .Where(pairIndex => IsRestShapeRecoveryPair(
                    baselineVertices,
                    contract,
                    pairIndex))
                .ToArray();
            int[] sharpFoldPairIndices = activePairIndices
                .Except(restShapeRecoveryPairIndices)
                .ToArray();

            Vector3[] vertices = initialVertices.ToArray();
            FrameEdge[] frameEdges = BuildFrameEdges(baselineVertices, contract);
            var buffers = new SolverBuffers();
            if (restShapeRecoveryPairIndices.Length > 0)
            {
                vertices = ApplyCorrectionPass(
                    baselineVertices,
                    vertices,
                    contract,
                    configuration,
                    restShapeRecoveryPairIndices,
                    true,
                    allowsStrainRecovery,
                    frameEdges,
                    buffers,
                    timing);
            }
            if (sharpFoldPairIndices.Length > 0)
            {
                vertices = ApplyCorrectionPass(
                    baselineVertices,
                    vertices,
                    contract,
                    configuration,
                    sharpFoldPairIndices,
                    false,
                    allowsStrainRecovery ||
                    restShapeRecoveryPairIndices.Length > 0,
                    frameEdges,
                    buffers,
                    timing);
            }

            result = EvaluateResult(
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
            return true;
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
            Vector3[] projectionStartVertices = sharpFoldCorrectedVertices;
            for (int round = 0; round < CoupledProjectionRoundCount; round++)
            {
                if (!NativeSkinningCoupledDihedralProjectionCalculator.TryProject(
                        baselineVertices,
                        projectionStartVertices,
                        contract,
                        restShapeRecoveryPairIndices,
                        sharpFoldPairIndices,
                        RestSmoothAngleDegrees,
                        SharpFoldAngleDegrees,
                        ProjectionAngleDegrees,
                        out Vector3[] projectedVertices))
                {
                    return false;
                }

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
                if (TryFindQualityPreservingCoupledBlend(
                        baselineVertices,
                        projectionStartVertices,
                        projectedVertices,
                        contract,
                        configuration,
                        frameEdges,
                        initialSharpFoldCount,
                        restShapeRecoveryPairIndices,
                        out result))
                {
                    return true;
                }
                projectionStartVertices = projectedVertices;
            }
            return false;
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

        private static Vector3[] ApplyCorrectionPass(
            IReadOnlyList<Vector3> baselineVertices,
            Vector3[] initialVertices,
            NativeSkinningSurfaceContract contract,
            CorrectionConfiguration configuration,
            IEnumerable<int> seedPairIndices,
            bool shouldRecoverRestShape,
            bool allowsStrainRecovery,
            IReadOnlyList<FrameEdge> frameEdges,
            SolverBuffers buffers,
            NativeSkinningSolverTiming timing)
        {
            Vector3[] vertices = initialVertices;
            var activePairIndices = new HashSet<int>(seedPairIndices);
            long sectionStamp = NativeSkinningSolverTiming.Stamp();
            LocalWorkset workset = BuildLocalWorkset(
                baselineVertices,
                contract,
                activePairIndices,
                configuration.WorksetRingCount,
                frameEdges,
                buffers);
            timing?.AddSetup(sectionStamp);
            buffers.EnsureLocalSize(workset.ConstrainedVertices.Length);
            buffers.EnsureVertexSize(vertices.Length);
            for (int iteration = 0;
                 iteration < configuration.LocalGlobalIterations;
                 iteration++)
            {
                timing?.AddLgIteration();
                sectionStamp = NativeSkinningSolverTiming.Stamp();
                UpdateActiveCorrectionPairs(
                    baselineVertices,
                    vertices,
                    contract,
                    workset.FacePairIndices,
                    shouldRecoverRestShape,
                    activePairIndices);
                double beforeEnergy = CalculateConstraintEnergy(
                    baselineVertices,
                    vertices,
                    contract,
                    activePairIndices,
                    workset.FacePairIndices,
                    workset.FaceIndices,
                    workset.Edges,
                    shouldRecoverRestShape);
                timing?.AddPairEnergy(sectionStamp);
                if (beforeEnergy <= 0d)
                {
                    break;
                }

                sectionStamp = NativeSkinningSolverTiming.Stamp();
                BuildDisplacementSystem(
                    baselineVertices,
                    vertices,
                    contract,
                    workset,
                    activePairIndices,
                    shouldRecoverRestShape,
                    buffers,
                    out NativeSkinningMaterialStrainConstraint[]
                        materialStrainConstraints,
                    out int activeConstraintCount);
                timing?.AddBuildSystem(sectionStamp);
                if (activeConstraintCount == 0)
                {
                    break;
                }

                sectionStamp = NativeSkinningSolverTiming.Stamp();
                IReadOnlyList<LocalEdge> cgEdges = shouldRecoverRestShape
                    ? (IReadOnlyList<LocalEdge>)Array.Empty<LocalEdge>()
                    : workset.Edges;
                bool warmStart =
                    configuration.AllowsWarmStart && iteration > 0;
                Vector3[] displacement = configuration.AllowsBurst
                    ? SolveConjugateGradientBurst(
                        buffers,
                        cgEdges,
                        materialStrainConstraints,
                        workset.ConstrainedVertices.Length,
                        configuration.ConjugateGradientIterations,
                        warmStart,
                        timing)
                    : SolveConjugateGradient(
                        buffers,
                        cgEdges,
                        materialStrainConstraints,
                        workset.ConstrainedVertices.Length,
                        configuration.ConjugateGradientIterations,
                        warmStart,
                        timing);
                timing?.AddCgSolve(sectionStamp);
                sectionStamp = NativeSkinningSolverTiming.Stamp();
                LimitDisplacement(
                    displacement,
                    workset.ConstrainedVertices.Length,
                    vertices,
                    contract,
                    activePairIndices);
                Vector3[] proposedLocalPositions = buffers.ProposedLocal;
                for (int localIndex = 0;
                     localIndex < workset.ConstrainedVertices.Length;
                     localIndex++)
                {
                    proposedLocalPositions[localIndex] =
                        vertices[workset.ConstrainedVertices[localIndex]] +
                        displacement[localIndex];
                }
                if (shouldRecoverRestShape)
                {
                    ProjectRestShapeRecoveryConstraints(
                        baselineVertices,
                        vertices,
                        proposedLocalPositions,
                        workset,
                        contract,
                        activePairIndices);
                }
                else
                {
                    ProjectEdgeLengthBounds(
                        vertices,
                        proposedLocalPositions,
                        workset.ConstrainedVertices,
                        workset.Edges,
                        configuration.MaximumEdgeLengthStrain -
                            EdgeLengthProjectionMargin,
                        configuration.EdgeLengthProjectionIterations);
                }
                if (!TryAcceptGlobalStep(
                        baselineVertices,
                        vertices,
                        proposedLocalPositions,
                        workset,
                        contract,
                        activePairIndices,
                        beforeEnergy,
                        configuration,
                        shouldRecoverRestShape,
                        allowsStrainRecovery,
                        buffers.CandidateFor(vertices),
                        out Vector3[] acceptedVertices,
                        out double acceptedEnergy))
                {
                    timing?.AddProjectAccept(sectionStamp);
                    break;
                }
                timing?.AddProjectAccept(sectionStamp);
                vertices = acceptedVertices;
                // 상대 개선폭이 수렴 임계 미만이면 꼬리 반복을 생략한다.
                if (configuration.AllowsEarlyConvergence &&
                    (beforeEnergy - acceptedEnergy) / beforeEnergy <
                    EarlyConvergenceRelativeImprovement)
                {
                    break;
                }
            }
            return vertices;
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
            int residualSharpFoldCount = 0;
            int newSharpFoldCount = 0;
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

            int newDegenerateFaceCount = 0;
            int reversedFaceCount = 0;
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

            int[] affectedVertices = contract.AffectedFaceIndices
                .SelectMany(faceIndex => GetFaceVertices(contract.Triangles, faceIndex))
                .Distinct()
                .ToArray();
            int[] correctedVertexIndices = Enumerable.Range(0, vertices.Length)
                .Where(index =>
                    (vertices[index] - baselineVertices[index]).sqrMagnitude >
                    MinimumStoredCorrectionSquaredMagnitude)
                .ToArray();
            bool usedRestShapeRecovery = restShapeRecoveryPairIndices.Count > 0;
            float maximumRestEdgeStrainIncrease = usedRestShapeRecovery
                ? edges.Max(edge => Mathf.Max(
                    0f,
                    NativeSkinningRestShapeRecoveryCalculator
                        .CalculateStrainIncrease(
                            edge.BaselineLength,
                            Vector3.Distance(
                                vertices[edge.FirstVertex],
                                vertices[edge.SecondVertex]),
                            edge.RestLength)))
                : 0f;
            float minimumRestShapeAngleImprovementDegrees =
                CalculateMinimumRestShapeAngleImprovement(
                    baselineVertices,
                    vertices,
                    contract,
                    restShapeRecoveryPairIndices);
            float minimumRestEdgeLengthRatioBeforeRecovery =
                usedRestShapeRecovery
                    ? CalculateMinimumRestEdgeLengthRatio(
                        baselineVertices,
                        edges)
                    : 0f;
            float minimumRestEdgeLengthRatioAfterRecovery =
                usedRestShapeRecovery
                    ? CalculateMinimumRestEdgeLengthRatio(vertices, edges)
                    : 0f;
            int unresolvedRestShapeRecoveryCount = restShapeRecoveryPairIndices
                .Count(pairIndex => IsRestShapeRecoveryPairUnresolved(
                    vertices,
                    contract,
                    pairIndex));
            // vertices는 재사용 버퍼를 별칭할 수 있어 fallback 덮어쓰기를 막기 위해 스냅샷한다.
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

        private static void BuildDisplacementSystem(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> vertices,
            NativeSkinningSurfaceContract contract,
            LocalWorkset workset,
            IReadOnlyCollection<int> activePairIndices,
            bool shouldRecoverRestShape,
            SolverBuffers buffers,
            out NativeSkinningMaterialStrainConstraint[] materialStrainConstraints,
            out int activeConstraintCount)
        {
            int count = workset.ConstrainedVertices.Length;
            float[] diagonal = buffers.Diagonal;
            Array.Clear(diagonal, 0, count);
            Vector3[] rightHandSide = buffers.RightHandSide;
            Array.Clear(rightHandSide, 0, count);
            List<BendingLinearization> bendingTerms = buffers.BendingTerms;
            bendingTerms.Clear();
            List<EdgeStrainLinearization> edgeStrainTerms = buffers.EdgeStrainTerms;
            edgeStrainTerms.Clear();
            for (int localIndex = 0; localIndex < count; localIndex++)
            {
                diagonal[localIndex] = shouldRecoverRestShape
                    ? RestShapeRecoveryAttachmentWeight
                    : DisplacementAttachmentWeight;
                if (shouldRecoverRestShape)
                {
                    int vertexIndex = workset.ConstrainedVertices[localIndex];
                    rightHandSide[localIndex] =
                        (baselineVertices[vertexIndex] - vertices[vertexIndex]) *
                        RestShapeRecoveryAttachmentWeight;
                }
            }
            if (!shouldRecoverRestShape)
            {
                foreach (LocalEdge edge in workset.Edges)
                {
                    if (edge.FirstLocalIndex >= 0)
                    {
                        diagonal[edge.FirstLocalIndex] +=
                            DisplacementSmoothnessWeight;
                    }
                    if (edge.SecondLocalIndex >= 0)
                    {
                        diagonal[edge.SecondLocalIndex] +=
                            DisplacementSmoothnessWeight;
                    }
                }
            }

            activeConstraintCount = 0;
            foreach (int pairIndex in workset.FacePairIndices)
            {
                if (!activePairIndices.Contains(pairIndex) ||
                    !IsSharpFoldCorrectionEligible(
                        baselineVertices,
                        contract,
                        pairIndex))
                {
                    continue;
                }
                NativeSkinningFacePair pair = contract.FacePairs[pairIndex];
                if (!NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                        vertices,
                        pair,
                        out float angle))
                {
                    continue;
                }
                float targetAngleDegrees = ResolveProjectionAngleDegrees(
                    baselineVertices,
                    contract,
                    pairIndex);
                if (angle <= targetAngleDegrees)
                {
                    continue;
                }
                if (!MeshDihedralConstraintCalculator.TryCalculateCorrections(
                        vertices[pair.FirstOpposite],
                        vertices[pair.SecondOpposite],
                        vertices[pair.FirstEdge],
                        vertices[pair.SecondEdge],
                        targetAngleDegrees,
                        1f,
                        out Vector3 correction0,
                        out Vector3 correction1,
                        out Vector3 correction2,
                        out Vector3 correction3))
                {
                    continue;
                }

                float violationRadians =
                    (angle - targetAngleDegrees) * Mathf.Deg2Rad;
                float correctionSquaredSum =
                    correction0.sqrMagnitude +
                    correction1.sqrMagnitude +
                    correction2.sqrMagnitude +
                    correction3.sqrMagnitude;
                if (correctionSquaredSum <= MinimumNormalSquaredMagnitude)
                {
                    continue;
                }
                float gradientSquaredSum =
                    violationRadians * violationRadians / correctionSquaredSum;
                float gradientScale = -gradientSquaredSum / violationRadians;
                AddBendingRightHandSide(
                    pair.FirstOpposite,
                    correction0,
                    gradientSquaredSum,
                    workset.LocalIndexByVertex,
                    rightHandSide);
                AddBendingRightHandSide(
                    pair.SecondOpposite,
                    correction1,
                    gradientSquaredSum,
                    workset.LocalIndexByVertex,
                    rightHandSide);
                AddBendingRightHandSide(
                    pair.FirstEdge,
                    correction2,
                    gradientSquaredSum,
                    workset.LocalIndexByVertex,
                    rightHandSide);
                AddBendingRightHandSide(
                    pair.SecondEdge,
                    correction3,
                    gradientSquaredSum,
                    workset.LocalIndexByVertex,
                    rightHandSide);
                bendingTerms.Add(new BendingLinearization(
                    workset.LocalIndexByVertex[pair.FirstOpposite],
                    workset.LocalIndexByVertex[pair.SecondOpposite],
                    workset.LocalIndexByVertex[pair.FirstEdge],
                    workset.LocalIndexByVertex[pair.SecondEdge],
                    correction0 * gradientScale,
                    correction1 * gradientScale,
                    correction2 * gradientScale,
                    correction3 * gradientScale));
                activeConstraintCount++;
            }

            materialStrainConstraints = shouldRecoverRestShape
                ? NativeSkinningRestShapeRecoveryCalculator.BuildConstraints(
                    baselineVertices,
                    contract.RestVertices,
                    vertices,
                    contract.Triangles,
                    workset.FaceIndices,
                    workset.LocalIndexByVertex)
                : Array.Empty<NativeSkinningMaterialStrainConstraint>();
            foreach (NativeSkinningMaterialStrainConstraint constraint in
                     materialStrainConstraints)
            {
                for (int index = 0; index < constraint.LocalIndices.Length; index++)
                {
                    int localIndex = constraint.LocalIndices[index];
                    float coefficient = constraint.Coefficients[index];
                    diagonal[localIndex] +=
                        constraint.Weight * coefficient * coefficient;
                    rightHandSide[localIndex] +=
                        constraint.ProjectedDelta * coefficient * constraint.Weight;
                }
                activeConstraintCount++;
            }

            AddOrientationBarrierTerms(
                baselineVertices,
                vertices,
                contract,
                workset.LocalIndexByVertex,
                workset.FaceIndices,
                diagonal,
                rightHandSide,
                ref activeConstraintCount);
            if (!shouldRecoverRestShape)
            {
                AddEdgeStrainTerms(
                    vertices,
                    workset.Edges,
                    rightHandSide,
                    edgeStrainTerms,
                    ref activeConstraintCount);
            }
            float[] preconditionerDiagonal = buffers.Preconditioner;
            Array.Copy(diagonal, preconditionerDiagonal, count);
            foreach (BendingLinearization term in bendingTerms)
            {
                for (int index = 0; index < 4; index++)
                {
                    int localIndex = term.LocalIndexAt(index);
                    if (localIndex >= 0)
                    {
                        preconditionerDiagonal[localIndex] +=
                            BendingWeight * term.GradientAt(index).sqrMagnitude;
                    }
                }
            }
            foreach (EdgeStrainLinearization term in edgeStrainTerms)
            {
                if (term.FirstLocalIndex >= 0)
                {
                    preconditionerDiagonal[term.FirstLocalIndex] +=
                        EdgeStrainBarrierWeight * term.FirstGradient.sqrMagnitude;
                }
                if (term.SecondLocalIndex >= 0)
                {
                    preconditionerDiagonal[term.SecondLocalIndex] +=
                        EdgeStrainBarrierWeight * term.SecondGradient.sqrMagnitude;
                }
            }
            foreach (NativeSkinningMaterialStrainConstraint constraint in
                     materialStrainConstraints)
            {
                for (int index = 0; index < constraint.LocalIndices.Length; index++)
                {
                    int localIndex = constraint.LocalIndices[index];
                    float coefficient = constraint.Coefficients[index];
                    preconditionerDiagonal[localIndex] +=
                        constraint.Weight * coefficient * coefficient;
                }
            }
        }

        private static void AddBendingRightHandSide(
            int vertexIndex,
            Vector3 correction,
            float gradientSquaredSum,
            IReadOnlyList<int> localIndexByVertex,
            IList<Vector3> rightHandSide)
        {
            int localIndex = localIndexByVertex[vertexIndex];
            if (localIndex >= 0)
            {
                rightHandSide[localIndex] +=
                    correction * gradientSquaredSum * BendingWeight;
            }
        }

        private static void AddOrientationBarrierTerms(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> vertices,
            NativeSkinningSurfaceContract contract,
            IReadOnlyList<int> localIndexByVertex,
            IEnumerable<int> faceIndices,
            IList<float> diagonal,
            IList<Vector3> rightHandSide,
            ref int activeConstraintCount)
        {
            float minimumNormalMagnitude = Mathf.Sqrt(MinimumNormalSquaredMagnitude);
            foreach (int faceIndex in faceIndices)
            {
                int offset = faceIndex * 3;
                int firstIndex = contract.Triangles[offset];
                int secondIndex = contract.Triangles[offset + 1];
                int thirdIndex = contract.Triangles[offset + 2];
                Vector3 baselineCross = Vector3.Cross(
                    baselineVertices[secondIndex] - baselineVertices[firstIndex],
                    baselineVertices[thirdIndex] - baselineVertices[firstIndex]);
                float baselineArea = baselineCross.magnitude;
                if (baselineArea < minimumNormalMagnitude)
                {
                    continue;
                }

                Vector3 baselineNormal = baselineCross / baselineArea;
                Vector3 first = vertices[firstIndex];
                Vector3 second = vertices[secondIndex];
                Vector3 third = vertices[thirdIndex];
                float signedArea = Vector3.Dot(
                    Vector3.Cross(second - first, third - first),
                    baselineNormal);
                float activationArea = baselineArea * OrientationActivationRatio;
                if (signedArea >= activationArea)
                {
                    continue;
                }

                float normalizedViolation = Mathf.Max(
                    0f,
                    (activationArea - signedArea) / activationArea);
                Vector3[] gradients =
                {
                    Vector3.Cross(second - third, baselineNormal),
                    Vector3.Cross(third - first, baselineNormal),
                    Vector3.Cross(baselineNormal, second - first)
                };
                int[] faceVertices = { firstIndex, secondIndex, thirdIndex };
                for (int index = 0; index < faceVertices.Length; index++)
                {
                    int localIndex = localIndexByVertex[faceVertices[index]];
                    if (localIndex < 0)
                    {
                        continue;
                    }
                    Vector3 normalizedGradient = gradients[index] / activationArea;
                    diagonal[localIndex] +=
                        OrientationBarrierWeight * normalizedGradient.sqrMagnitude;
                    rightHandSide[localIndex] +=
                        OrientationBarrierWeight *
                        normalizedViolation * normalizedGradient;
                }
                activeConstraintCount++;
            }
        }

        private static void AddEdgeStrainTerms(
            IReadOnlyList<Vector3> vertices,
            IEnumerable<LocalEdge> edges,
            IList<Vector3> rightHandSide,
            ICollection<EdgeStrainLinearization> terms,
            ref int activeConstraintCount)
        {
            foreach (LocalEdge edge in edges)
            {
                Vector3 edgeVector =
                    vertices[edge.SecondVertex] - vertices[edge.FirstVertex];
                float edgeLength = edgeVector.magnitude;
                if (edgeLength <= 0.00000001f ||
                    edge.BaselineLength <= 0.00000001f)
                {
                    continue;
                }
                float signedStrain = edgeLength / edge.BaselineLength - 1f;
                float excessMagnitude =
                    Mathf.Abs(signedStrain) - EdgeStrainActivation;
                if (excessMagnitude <= 0f)
                {
                    continue;
                }

                float signedExcess = Mathf.Sign(signedStrain) * excessMagnitude;
                Vector3 normalizedDirection =
                    edgeVector / (edgeLength * edge.BaselineLength);
                Vector3 firstCorrection =
                    EdgeStrainBarrierWeight * signedExcess * normalizedDirection;
                if (edge.FirstLocalIndex >= 0)
                {
                    rightHandSide[edge.FirstLocalIndex] += firstCorrection;
                }
                if (edge.SecondLocalIndex >= 0)
                {
                    rightHandSide[edge.SecondLocalIndex] -= firstCorrection;
                }
                terms.Add(new EdgeStrainLinearization(
                    edge.FirstLocalIndex,
                    edge.SecondLocalIndex,
                    -normalizedDirection,
                    normalizedDirection));
                activeConstraintCount++;
            }
        }

        private static Vector3[] SolveConjugateGradient(
            SolverBuffers buffers,
            IReadOnlyList<LocalEdge> edges,
            IReadOnlyList<NativeSkinningMaterialStrainConstraint>
                materialStrainConstraints,
            int count,
            int maximumIterations,
            bool warmStart,
            NativeSkinningSolverTiming timing)
        {
            timing?.AddCgCall();
            float[] diagonal = buffers.Diagonal;
            float[] preconditionerDiagonal = buffers.Preconditioner;
            Vector3[] rightHandSide = buffers.RightHandSide;
            Vector3[] solution = buffers.Solution;
            Vector3[] residual = buffers.Residual;
            Vector3[] direction = buffers.Direction;
            Vector3[] preconditionedResidual = buffers.PreconditionedResidual;
            Vector3[] matrixDirection = buffers.MatrixDirection;
            List<BendingLinearization> bendingLinearizations =
                buffers.BendingTerms;
            List<EdgeStrainLinearization> edgeStrainLinearizations =
                buffers.EdgeStrainTerms;
            if (warmStart)
            {
                // 직전 반복 해법을 x0로 — 잔차는 우변−A·x0를 실제 계산한다.
                ApplyMatrixInto(
                    solution,
                    diagonal,
                    edges,
                    bendingLinearizations,
                    edgeStrainLinearizations,
                    materialStrainConstraints,
                    matrixDirection,
                    count);
                for (int index = 0; index < count; index++)
                {
                    residual[index] =
                        rightHandSide[index] - matrixDirection[index];
                }
            }
            else
            {
                // x0=0이면 A·x0=0이라 잔차는 우변 그대로 — 첫 ApplyMatrix를 생략한다.
                Array.Clear(solution, 0, count);
                Array.Copy(rightHandSide, residual, count);
            }
            ApplyDiagonalPreconditionerInto(
                residual,
                preconditionerDiagonal,
                direction,
                count);
            double residualDot = Dot(residual, direction, count);
            for (int iteration = 0;
                 iteration < maximumIterations && residualDot > 1e-20d;
                 iteration++)
            {
                timing?.AddCgIteration();
                ApplyMatrixInto(
                    direction,
                    diagonal,
                    edges,
                    bendingLinearizations,
                    edgeStrainLinearizations,
                    materialStrainConstraints,
                    matrixDirection,
                    count);
                double denominator = Dot(direction, matrixDirection, count);
                if (Math.Abs(denominator) <= 1e-20d)
                {
                    break;
                }
                float step = (float)(residualDot / denominator);
                for (int index = 0; index < count; index++)
                {
                    solution[index] += direction[index] * step;
                    residual[index] -= matrixDirection[index] * step;
                }
                if (Dot(residual, residual, count) <= 1e-18d)
                {
                    break;
                }
                ApplyDiagonalPreconditionerInto(
                    residual,
                    preconditionerDiagonal,
                    preconditionedResidual,
                    count);
                double nextResidualDot = Dot(
                    residual,
                    preconditionedResidual,
                    count);
                float beta = (float)(nextResidualDot / residualDot);
                for (int index = 0; index < count; index++)
                {
                    direction[index] = preconditionedResidual[index] +
                        direction[index] * beta;
                }
                residualDot = nextResidualDot;
            }
            return solution;
        }

        // fast 전용 Burst 경로 — CG 한 호출을 하나의 Job으로 실행해
        // 반복마다 스케줄 비용을 치르지 않는다. 포장·해제는 호출당 1회.
        // 포장이나 실행이 실패하면 managed 경로로 되돌아가 결과 계약을 유지한다.
        private static Vector3[] SolveConjugateGradientBurst(
            SolverBuffers buffers,
            IReadOnlyList<LocalEdge> edges,
            IReadOnlyList<NativeSkinningMaterialStrainConstraint>
                materialStrainConstraints,
            int count,
            int maximumIterations,
            bool warmStart,
            NativeSkinningSolverTiming timing)
        {
            timing?.AddCgCall();
            int edgeCount = edges.Count;
            int bendCount = buffers.BendingTerms.Count;
            int strainCount = buffers.EdgeStrainTerms.Count;
            int materialCount = materialStrainConstraints.Count;
            int materialTermTotal = 0;
            for (int index = 0; index < materialCount; index++)
            {
                materialTermTotal +=
                    materialStrainConstraints[index].LocalIndices.Length;
            }

            NativeArray<float> diagonal = default;
            NativeArray<float> preconditioner = default;
            NativeArray<Vector3> rightHandSide = default;
            NativeArray<Vector3> solution = default;
            NativeArray<int> iterationsRun = default;
            NativeArray<int2> edgeIndices = default;
            NativeArray<int> bendIndices = default;
            NativeArray<Vector3> bendGradients = default;
            NativeArray<int2> strainIndices = default;
            NativeArray<Vector3> strainFirstGradients = default;
            NativeArray<Vector3> strainSecondGradients = default;
            NativeArray<int> materialOffsets = default;
            NativeArray<int> materialIndices = default;
            NativeArray<float> materialCoefficients = default;
            NativeArray<float> materialWeights = default;
            try
            {
                diagonal = new NativeArray<float>(count, Allocator.TempJob);
                preconditioner =
                    new NativeArray<float>(count, Allocator.TempJob);
                rightHandSide =
                    new NativeArray<Vector3>(count, Allocator.TempJob);
                solution = new NativeArray<Vector3>(count, Allocator.TempJob);
                iterationsRun = new NativeArray<int>(1, Allocator.TempJob);
                edgeIndices =
                    new NativeArray<int2>(edgeCount, Allocator.TempJob);
                bendIndices =
                    new NativeArray<int>(bendCount * 4, Allocator.TempJob);
                bendGradients = new NativeArray<Vector3>(
                    bendCount * 4,
                    Allocator.TempJob);
                strainIndices =
                    new NativeArray<int2>(strainCount, Allocator.TempJob);
                strainFirstGradients = new NativeArray<Vector3>(
                    strainCount,
                    Allocator.TempJob);
                strainSecondGradients = new NativeArray<Vector3>(
                    strainCount,
                    Allocator.TempJob);
                materialOffsets =
                    new NativeArray<int>(materialCount + 1, Allocator.TempJob);
                materialIndices =
                    new NativeArray<int>(materialTermTotal, Allocator.TempJob);
                materialCoefficients =
                    new NativeArray<float>(materialTermTotal, Allocator.TempJob);
                materialWeights =
                    new NativeArray<float>(materialCount, Allocator.TempJob);

                NativeArray<float>.Copy(
                    buffers.Diagonal, 0, diagonal, 0, count);
                NativeArray<float>.Copy(
                    buffers.Preconditioner, 0, preconditioner, 0, count);
                NativeArray<Vector3>.Copy(
                    buffers.RightHandSide, 0, rightHandSide, 0, count);
                if (warmStart)
                {
                    NativeArray<Vector3>.Copy(
                        buffers.Solution, 0, solution, 0, count);
                }
                for (int index = 0; index < edgeCount; index++)
                {
                    LocalEdge edge = edges[index];
                    edgeIndices[index] = new int2(
                        edge.FirstLocalIndex,
                        edge.SecondLocalIndex);
                }
                for (int index = 0; index < bendCount; index++)
                {
                    BendingLinearization term = buffers.BendingTerms[index];
                    bendIndices[index * 4] = term.Index0;
                    bendIndices[index * 4 + 1] = term.Index1;
                    bendIndices[index * 4 + 2] = term.Index2;
                    bendIndices[index * 4 + 3] = term.Index3;
                    bendGradients[index * 4] = term.Gradient0;
                    bendGradients[index * 4 + 1] = term.Gradient1;
                    bendGradients[index * 4 + 2] = term.Gradient2;
                    bendGradients[index * 4 + 3] = term.Gradient3;
                }
                for (int index = 0; index < strainCount; index++)
                {
                    EdgeStrainLinearization term =
                        buffers.EdgeStrainTerms[index];
                    strainIndices[index] = new int2(
                        term.FirstLocalIndex,
                        term.SecondLocalIndex);
                    strainFirstGradients[index] = term.FirstGradient;
                    strainSecondGradients[index] = term.SecondGradient;
                }
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

                new CgSolveJob
                {
                    Count = count,
                    MaximumIterations = maximumIterations,
                    WarmStart = warmStart ? (byte)1 : (byte)0,
                    Diagonal = diagonal,
                    Preconditioner = preconditioner,
                    RightHandSide = rightHandSide,
                    Solution = solution,
                    IterationsRun = iterationsRun,
                    EdgeIndices = edgeIndices,
                    BendIndices = bendIndices,
                    BendGradients = bendGradients,
                    StrainIndices = strainIndices,
                    StrainFirstGradients = strainFirstGradients,
                    StrainSecondGradients = strainSecondGradients,
                    MaterialOffsets = materialOffsets,
                    MaterialIndices = materialIndices,
                    MaterialCoefficients = materialCoefficients,
                    MaterialWeights = materialWeights,
                }.Schedule().Complete();
                timing?.AddCgIterations(iterationsRun[0]);
                NativeArray<Vector3>.Copy(
                    solution, 0, buffers.Solution, 0, count);
            }
            catch (Exception)
            {
                // 네이티브 경로 실패 시 managed 솔버로 동일 입력을 다시 푼다.
                return SolveConjugateGradient(
                    buffers,
                    edges,
                    materialStrainConstraints,
                    count,
                    maximumIterations,
                    warmStart,
                    timing);
            }
            finally
            {
                if (diagonal.IsCreated) diagonal.Dispose();
                if (preconditioner.IsCreated) preconditioner.Dispose();
                if (rightHandSide.IsCreated) rightHandSide.Dispose();
                if (solution.IsCreated) solution.Dispose();
                if (iterationsRun.IsCreated) iterationsRun.Dispose();
                if (edgeIndices.IsCreated) edgeIndices.Dispose();
                if (bendIndices.IsCreated) bendIndices.Dispose();
                if (bendGradients.IsCreated) bendGradients.Dispose();
                if (strainIndices.IsCreated) strainIndices.Dispose();
                if (strainFirstGradients.IsCreated)
                {
                    strainFirstGradients.Dispose();
                }
                if (strainSecondGradients.IsCreated)
                {
                    strainSecondGradients.Dispose();
                }
                if (materialOffsets.IsCreated) materialOffsets.Dispose();
                if (materialIndices.IsCreated) materialIndices.Dispose();
                if (materialCoefficients.IsCreated)
                {
                    materialCoefficients.Dispose();
                }
                if (materialWeights.IsCreated) materialWeights.Dispose();
            }
            return buffers.Solution;
        }
    }
}
