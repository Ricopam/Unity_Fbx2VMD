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

    }
}
