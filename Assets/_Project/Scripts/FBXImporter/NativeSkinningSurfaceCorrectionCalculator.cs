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
                    baselineVertices, initialVertices,
                    contract, shouldValidateVertices))
            {
                return false;
            }
            if (!TryResolvePairSets(
                    baselineVertices, initialVertices, contract,
                    out int initialSharpFoldCount,
                    out int[] restShapeRecoveryPairIndices,
                    out int[] sharpFoldPairIndices,
                    ref result))
            {
                return true;
            }
            result = RunPassesAndEvaluate(
                baselineVertices, initialVertices, contract, configuration,
                allowsStrainRecovery, initialSharpFoldCount,
                restShapeRecoveryPairIndices, sharpFoldPairIndices, timing);
            return true;
        }

        // 활성 쌍을 수집하고 폴드 없음이면 빈 결과로 종료한다.
        private static bool TryResolvePairSets(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> initialVertices,
            NativeSkinningSurfaceContract contract,
            out int initialSharpFoldCount,
            out int[] restShapeRecoveryPairIndices,
            out int[] sharpFoldPairIndices,
            ref NativeSkinningSurfaceCorrectionResult result)
        {
            HashSet<int> activePairIndices = CollectActivePairs(
                baselineVertices,
                contract,
                out initialSharpFoldCount);
            restShapeRecoveryPairIndices = null;
            sharpFoldPairIndices = null;
            if (initialSharpFoldCount == 0)
            {
                result = CreateEmptyCorrectionResult(initialVertices);
                return false;
            }
            restShapeRecoveryPairIndices = SplitRestShapeRecoveryPairs(
                baselineVertices,
                contract,
                activePairIndices,
                out sharpFoldPairIndices);
            return true;
        }

        // 패스 실행부터 결과 평가까지의 본 계산 단계.
        private static NativeSkinningSurfaceCorrectionResult RunPassesAndEvaluate(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> initialVertices,
            NativeSkinningSurfaceContract contract,
            CorrectionConfiguration configuration,
            bool allowsStrainRecovery,
            int initialSharpFoldCount,
            int[] restShapeRecoveryPairIndices,
            int[] sharpFoldPairIndices,
            NativeSkinningSolverTiming timing)
        {
            Vector3[] vertices = RunCorrectionPasses(
                baselineVertices,
                initialVertices,
                contract,
                configuration,
                allowsStrainRecovery,
                restShapeRecoveryPairIndices,
                sharpFoldPairIndices,
                out FrameEdge[] frameEdges,
                out SolverBuffers buffers,
                timing);

            return EvaluateTimedResult(
                baselineVertices,
                initialVertices,
                vertices,
                contract,
                configuration,
                allowsStrainRecovery,
                frameEdges,
                initialSharpFoldCount,
                restShapeRecoveryPairIndices,
                sharpFoldPairIndices,
                buffers,
                timing);
        }

        private static HashSet<int> CollectActivePairs(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            out int initialSharpFoldCount)
        {
            var activePairIndices = new HashSet<int>();
            UpdateActiveSharpFoldPairs(
                baselineVertices,
                contract,
                Enumerable.Range(0, contract.FacePairs.Length),
                activePairIndices);
            initialSharpFoldCount = activePairIndices.Count;
            return activePairIndices;
        }

        private static int[] SplitRestShapeRecoveryPairs(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            HashSet<int> activePairIndices,
            out int[] sharpFoldPairIndices)
        {
            int[] restShapeRecoveryPairIndices = activePairIndices
                .Where(pairIndex => IsRestShapeRecoveryPair(
                    baselineVertices,
                    contract,
                    pairIndex))
                .ToArray();
            sharpFoldPairIndices = activePairIndices
                .Except(restShapeRecoveryPairIndices)
                .ToArray();
            return restShapeRecoveryPairIndices;
        }

        private static NativeSkinningSurfaceCorrectionResult CreateEmptyCorrectionResult(
            IReadOnlyList<Vector3> initialVertices)
        {
            return new NativeSkinningSurfaceCorrectionResult(
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
        }

        private static Vector3[] RunCorrectionPasses(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> initialVertices,
            NativeSkinningSurfaceContract contract,
            CorrectionConfiguration configuration,
            bool allowsStrainRecovery,
            int[] restShapeRecoveryPairIndices,
            int[] sharpFoldPairIndices,
            out FrameEdge[] frameEdges,
            out SolverBuffers buffers,
            NativeSkinningSolverTiming timing)
        {
            Vector3[] vertices = initialVertices.ToArray();
            frameEdges = BuildFrameEdges(baselineVertices, contract);
            buffers = new SolverBuffers();
            vertices = ApplyPassForPairs(
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
            vertices = ApplyPassForPairs(
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
            return vertices;
        }

        private static Vector3[] ApplyPassForPairs(
            IReadOnlyList<Vector3> baselineVertices,
            Vector3[] vertices,
            NativeSkinningSurfaceContract contract,
            CorrectionConfiguration configuration,
            int[] pairIndices,
            bool isRestShapeRecovery,
            bool allowsStrainRecovery,
            FrameEdge[] frameEdges,
            SolverBuffers buffers,
            NativeSkinningSolverTiming timing)
        {
            if (pairIndices.Length == 0)
            {
                return vertices;
            }
            return ApplyCorrectionPass(
                baselineVertices,
                vertices,
                contract,
                configuration,
                pairIndices,
                isRestShapeRecovery,
                allowsStrainRecovery,
                frameEdges,
                buffers,
                timing);
        }

    }
}
