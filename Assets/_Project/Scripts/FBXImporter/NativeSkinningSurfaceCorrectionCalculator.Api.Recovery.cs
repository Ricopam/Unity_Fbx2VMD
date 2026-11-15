using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
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

            if (boneTransforms != null &&
                TryDualQuaternionSeed(baselineVertices, contract, originalPairs, edges,
                    restVertices, boneWeights, boneTransforms, timing,
                    ref displayRepairSeed, out result))
            {
                return true;
            }

            int[] recoveryPairs = originalPairs.Where(pairIndex =>
                IsRestShapeRecoveryPair(baselineVertices, contract, pairIndex)).ToArray();
            if (TryResidualRetryImprove(
                    baselineVertices, contract, allPairs, originalPairs,
                    recoveryPairs, edges, timing,
                    ref previous, ref displayRepairSeed, ref result))
            {
                return true;
            }
            return (displayRepairSeed != null && TryProjectJointConstraints(
                    baselineVertices, contract, displayRepairSeed, originalPairs, recoveryPairs, edges, out result)) ||
                TryProjectJointConstraints(
                baselineVertices, contract, initial, originalPairs, recoveryPairs, edges, out result);
        }

        // 잔여 폴드를 최대 ResidualFoldRetryCount회 재보정한다.
        // 개선이 멈추면 중단하고 마지막으로 수용된 후보·표시용 시드를 갱신한다.
        private static bool TryResidualRetryImprove(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            IEnumerable<int> allPairs,
            HashSet<int> originalPairs,
            int[] recoveryPairs,
            FrameEdge[] edges,
            NativeSkinningSolverTiming timing,
            ref NativeSkinningSurfaceCorrectionResult previous,
            ref NativeSkinningSurfaceCorrectionResult displayRepairSeed,
            ref NativeSkinningSurfaceCorrectionResult result)
        {
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
                if (TryAcceptRetry(baselineVertices, contract, candidate, ref displayRepairSeed))
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
            return false;
        }

        // 품질 게이트를 통과하면 true. 여유가 부족하면 표시 복구 시드만 갱신하고 false.
        private static bool TryAcceptRetry(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            NativeSkinningSurfaceCorrectionResult candidate,
            ref NativeSkinningSurfaceCorrectionResult displayRepairSeed)
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

        private static bool TryDualQuaternionSeed(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            HashSet<int> originalPairs,
            FrameEdge[] edges,
            Vector3[] restVertices,
            BoneWeight[] boneWeights,
            NativeSkinningDualQuaternionTransform[] boneTransforms,
            NativeSkinningSolverTiming timing,
            ref NativeSkinningSurfaceCorrectionResult displayRepairSeed,
            out NativeSkinningSurfaceCorrectionResult recovered)
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
                    if (TryDualQuaternionSeedWithInfluence(
                            baselineVertices, contract, originalPairs, edges, workset,
                            indices, dqVertices, localPositions, influence, timing,
                            ref displayRepairSeed, out recovered))
                    {
                        return true;
                    }
                }
                return false;
        }

        private static bool TryDualQuaternionSeedWithInfluence(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            HashSet<int> originalPairs,
            FrameEdge[] edges,
            LocalWorkset workset,
            int[] indices,
            Vector3[] dqVertices,
            Vector3[] localPositions,
            float influence,
            NativeSkinningSolverTiming timing,
            ref NativeSkinningSurfaceCorrectionResult displayRepairSeed,
            out NativeSkinningSurfaceCorrectionResult recovered)
        {
            recovered = null;
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
                TryAcceptRetry(baselineVertices, contract, candidate, ref displayRepairSeed))
            {
                recovered = candidate;
                return true;
            }
            return false;
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
            var buffers = new SolverBuffers();
            LocalWorkset workset = BuildHeadroomWorkset(
                baselineVertices, contract, buffers,
                out FrameEdge[] edges);
            Vector3[] vertices = headroom.CorrectedVertices.ToArray();
            var localPositions = new Vector3[workset.ConstrainedVertices.Length];
            for (int round = 0; round < HeadroomStrainProjectionRoundCount; round++)
            {
                if (!TryProjectRoundPositions(
                        vertices, localPositions, workset,
                        out Vector3[] projected, out bool hasMovement))
                {
                    return false;
                }
                if (TryAcceptProjectedSeed(
                        baselineVertices, projected, contract, edges,
                        headroom, timing, out result, out bool abort))
                {
                    return true;
                }
                if (abort || !hasMovement)
                {
                    return false;
                }
                vertices = projected;
            }
            return false;
        }

        private static LocalWorkset BuildHeadroomWorkset(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract, SolverBuffers buffers,
            out FrameEdge[] edges)
        {
            var activePairs = new HashSet<int>();
            UpdateActiveSharpFoldPairs(
                baselineVertices, contract,
                Enumerable.Range(0, contract.FacePairs.Length), activePairs);
            edges = BuildFrameEdges(baselineVertices, contract);
            return BuildLocalWorkset(
                baselineVertices, contract, activePairs,
                StrongWideConfiguration.WorksetRingCount, edges, buffers);
        }

        // 중간 접힘은 내부 초기값에만 허용하고 원본 기준 strict로 재검증함.
        private static bool TryAcceptProjectedSeed(
            IReadOnlyList<Vector3> baselineVertices, Vector3[] projected,
            NativeSkinningSurfaceContract contract, FrameEdge[] edges,
            NativeSkinningSurfaceCorrectionResult headroom,
            NativeSkinningSolverTiming timing,
            out NativeSkinningSurfaceCorrectionResult result,
            out bool abort)
        {
            result = null;
            abort = false;
            NativeSkinningSurfaceCorrectionResult seed = EvaluateResult(
                baselineVertices, projected, contract, edges,
                headroom.InitialSharpFoldCount, Array.Empty<int>());
            if (IsProjectionUnsafe(seed, contract))
            {
                abort = true;
                return false;
            }
            if (seed.MaximumEdgeLengthStrain <= StrongConfiguration.MaximumEdgeLengthStrain)
            {
                abort = true;
                return TryCalculateTimed(
                    baselineVertices, projected, contract, StrongConfiguration,
                    false, false, timing, out result) &&
                    IsWithinQuality(result, contract, StrongConfiguration);
            }
            return false;
        }

        // 한 번의 투영은 0.05mm로 제한되므로 직전 후보에서 작은 단계를 이어감.
        private static bool TryProjectRoundPositions(
            Vector3[] vertices,
            Vector3[] localPositions,
            LocalWorkset workset,
            out Vector3[] projected,
            out bool hasMovement)
        {
            for (int localIndex = 0; localIndex < localPositions.Length; localIndex++)
            {
                localPositions[localIndex] =
                    vertices[workset.ConstrainedVertices[localIndex]];
            }
            ProjectEdgeLengthBounds(
                vertices, localPositions, workset.ConstrainedVertices, workset.Edges,
                StrongConfiguration.MaximumEdgeLengthStrain - EdgeLengthProjectionMargin,
                StrongConfiguration.EdgeLengthProjectionIterations);
            projected = vertices.ToArray();
            hasMovement = false;
            for (int localIndex = 0; localIndex < localPositions.Length; localIndex++)
            {
                int vertexIndex = workset.ConstrainedVertices[localIndex];
                projected[vertexIndex] = localPositions[localIndex];
                hasMovement |= !projected[vertexIndex].Equals(vertices[vertexIndex]);
            }
            return AreVerticesFinite(projected);
        }

        private static bool IsProjectionUnsafe(
            NativeSkinningSurfaceCorrectionResult seed,
            NativeSkinningSurfaceContract contract)
        {
            return seed.NewDegenerateFaceCount > 0 || seed.ReversedFaceCount > 0 ||
                seed.MaximumVertexDisplacement > contract.ArmChainLength *
                    StrongConfiguration.MaximumTotalDisplacementToArmLengthRatio;
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
