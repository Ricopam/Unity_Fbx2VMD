using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
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
            LocalWorkset workset = BuildLocalWorkset( baselineVertices, contract, activePairIndices, configuration.WorksetRingCount, frameEdges, buffers);
            timing?.AddSetup(sectionStamp);
            buffers.EnsureLocalSize(workset.ConstrainedVertices.Length);
            buffers.EnsureVertexSize(vertices.Length);
            for (int iteration = 0;
                 iteration < configuration.LocalGlobalIterations;
                 iteration++)
            {
                if (!RunCorrectionIteration( baselineVertices, ref vertices, contract, workset, activePairIndices, shouldRecoverRestShape, allowsStrainRecovery, configuration, buffers, timing,
                        iteration))
                {
                    break;
                }
            }
            return vertices;
        }

        // 로컬-글로벌 한 반복: 쌍 갱신 → 시스템 구축 → CG → 투영 → 스텝 수용.
        // false면 반복 종료(에너지 0·제약 없음·스텝 거부·조기 수렴).
        private static bool RunCorrectionIteration(
            IReadOnlyList<Vector3> baselineVertices,
            ref Vector3[] vertices,
            NativeSkinningSurfaceContract contract,
            LocalWorkset workset,
            HashSet<int> activePairIndices,
            bool shouldRecoverRestShape,
            bool allowsStrainRecovery,
            CorrectionConfiguration configuration,
            SolverBuffers buffers,
            NativeSkinningSolverTiming timing,
            int iteration)
        {
            timing?.AddLgIteration();
            if (!PrepareIterationSystem( baselineVertices, vertices, contract, workset, activePairIndices, shouldRecoverRestShape, buffers, timing, out double beforeEnergy,
                    out NativeSkinningMaterialStrainConstraint[]
                        materialStrainConstraints))
            {
                return false;
            }

            return ProposeAndAcceptStep( baselineVertices, ref vertices, contract, workset, activePairIndices, materialStrainConstraints, beforeEnergy, shouldRecoverRestShape, allowsStrainRecovery,
                configuration, buffers, timing, iteration);
        }

        // 솔브·제안·수용을 잇는 스텝 실행부.
        private static bool ProposeAndAcceptStep(
            IReadOnlyList<Vector3> baselineVertices,
            ref Vector3[] vertices,
            NativeSkinningSurfaceContract contract,
            LocalWorkset workset,
            HashSet<int> activePairIndices,
            NativeSkinningMaterialStrainConstraint[] materialStrainConstraints,
            double beforeEnergy,
            bool shouldRecoverRestShape,
            bool allowsStrainRecovery,
            CorrectionConfiguration configuration,
            SolverBuffers buffers,
            NativeSkinningSolverTiming timing,
            int iteration)
        {
            Vector3[] proposedLocalPositions = SolveAndProposeStep(
                baselineVertices,
                vertices,
                contract,
                workset,
                activePairIndices,
                materialStrainConstraints,
                shouldRecoverRestShape,
                configuration,
                buffers,
                timing,
                iteration,
                out long sectionStamp);
            return AcceptIterationStep(
                baselineVertices,
                ref vertices,
                proposedLocalPositions,
                workset,
                contract,
                activePairIndices,
                beforeEnergy,
                configuration,
                shouldRecoverRestShape,
                allowsStrainRecovery,
                buffers,
                timing,
                sectionStamp);
        }

        // 변위 CG 솔브 + 국소 제안/투영 한 단계. 호출부 스탬프는 투영 직전 시각.
        private static Vector3[] SolveAndProposeStep(
            IReadOnlyList<Vector3> baselineVertices,
            Vector3[] vertices,
            NativeSkinningSurfaceContract contract,
            LocalWorkset workset,
            HashSet<int> activePairIndices,
            NativeSkinningMaterialStrainConstraint[] materialStrainConstraints,
            bool shouldRecoverRestShape,
            CorrectionConfiguration configuration,
            SolverBuffers buffers,
            NativeSkinningSolverTiming timing,
            int iteration,
            out long sectionStamp)
        {
            long stamp = NativeSkinningSolverTiming.Stamp();
            Vector3[] displacement = SolveDisplacementStep(
                buffers,
                workset,
                materialStrainConstraints,
                shouldRecoverRestShape,
                configuration,
                iteration,
                timing);
            timing?.AddCgSolve(stamp);
            sectionStamp = NativeSkinningSolverTiming.Stamp();
            return ProposeAndProjectLocal(
                baselineVertices,
                vertices,
                displacement,
                workset,
                contract,
                activePairIndices,
                shouldRecoverRestShape,
                configuration,
                buffers);
        }

        // 스텝 수용 + 조기 수렴 판정. 수용 시 vertices를 갱신하고 계속 여부를 돌려준다.
        private static bool AcceptIterationStep(
            IReadOnlyList<Vector3> baselineVertices,
            ref Vector3[] vertices,
            Vector3[] proposedLocalPositions,
            LocalWorkset workset,
            NativeSkinningSurfaceContract contract,
            HashSet<int> activePairIndices,
            double beforeEnergy,
            CorrectionConfiguration configuration,
            bool shouldRecoverRestShape,
            bool allowsStrainRecovery,
            SolverBuffers buffers,
            NativeSkinningSolverTiming timing,
            long sectionStamp)
        {
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
                return false;
            }
            timing?.AddProjectAccept(sectionStamp);
            vertices = acceptedVertices;
            // 상대 개선폭이 수렴 임계 미만이면 꼬리 반복을 생략한다.
            return !configuration.AllowsEarlyConvergence ||
                (beforeEnergy - acceptedEnergy) / beforeEnergy >=
                EarlyConvergenceRelativeImprovement;
        }

        private static bool PrepareIterationSystem(
            IReadOnlyList<Vector3> baselineVertices,
            Vector3[] vertices,
            NativeSkinningSurfaceContract contract,
            LocalWorkset workset,
            HashSet<int> activePairIndices,
            bool shouldRecoverRestShape,
            SolverBuffers buffers,
            NativeSkinningSolverTiming timing,
            out double beforeEnergy,
            out NativeSkinningMaterialStrainConstraint[] materialStrainConstraints)
        {
            long sectionStamp = NativeSkinningSolverTiming.Stamp();
            UpdateActiveCorrectionPairs( baselineVertices, vertices, contract, workset.FacePairIndices, shouldRecoverRestShape, activePairIndices);
            beforeEnergy = CalculateConstraintEnergy( baselineVertices, vertices, contract, activePairIndices, workset.FacePairIndices, workset.FaceIndices, workset.Edges, shouldRecoverRestShape);
            timing?.AddPairEnergy(sectionStamp);
            if (beforeEnergy <= 0d)
            {
                materialStrainConstraints = null;
                return false;
            }

            sectionStamp = NativeSkinningSolverTiming.Stamp();
            BuildDisplacementSystem( baselineVertices, vertices, contract, workset, activePairIndices, shouldRecoverRestShape, buffers, out materialStrainConstraints, out int activeConstraintCount);
            timing?.AddBuildSystem(sectionStamp);
            return activeConstraintCount > 0;
        }

        private static Vector3[] SolveDisplacementStep(
            SolverBuffers buffers,
            LocalWorkset workset,
            NativeSkinningMaterialStrainConstraint[] materialStrainConstraints,
            bool shouldRecoverRestShape,
            CorrectionConfiguration configuration,
            int iteration,
            NativeSkinningSolverTiming timing)
        {
            IReadOnlyList<LocalEdge> cgEdges = shouldRecoverRestShape
                ? (IReadOnlyList<LocalEdge>)Array.Empty<LocalEdge>()
                : workset.Edges;
            bool warmStart =
                configuration.AllowsWarmStart && iteration > 0;
            return configuration.AllowsBurst
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
        }

        private static Vector3[] ProposeAndProjectLocal(
            IReadOnlyList<Vector3> baselineVertices,
            Vector3[] vertices,
            Vector3[] displacement,
            LocalWorkset workset,
            NativeSkinningSurfaceContract contract,
            HashSet<int> activePairIndices,
            bool shouldRecoverRestShape,
            CorrectionConfiguration configuration,
            SolverBuffers buffers)
        {
            LimitDisplacement( displacement, workset.ConstrainedVertices.Length, vertices, contract, activePairIndices);
            Vector3[] proposedLocalPositions = buffers.ProposedLocal;
            for (int localIndex = 0;
                 localIndex < workset.ConstrainedVertices.Length;
                 localIndex++)
            {
                proposedLocalPositions[localIndex] = vertices[workset.ConstrainedVertices[localIndex]] + displacement[localIndex];
            }
            if (shouldRecoverRestShape)
            {
                ProjectRestShapeRecoveryConstraints( baselineVertices, vertices, proposedLocalPositions, workset, contract, activePairIndices);
            }
            else
            {
                ProjectEdgeLengthBounds( vertices, proposedLocalPositions, workset.ConstrainedVertices, workset.Edges, configuration.MaximumEdgeLengthStrain -
                        EdgeLengthProjectionMargin, configuration.EdgeLengthProjectionIterations);
            }
            return proposedLocalPositions;
        }
    }
}
