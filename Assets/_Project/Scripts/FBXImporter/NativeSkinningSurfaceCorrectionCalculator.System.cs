using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 변위 시스템 구축·에너지 항·CG 솔버 — 본체에서 이관한 partial.
    /// </summary>
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
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
            Vector3[] rightHandSide = buffers.RightHandSide;
            List<BendingLinearization> bendingTerms = buffers.BendingTerms;
            List<EdgeStrainLinearization> edgeStrainTerms = buffers.EdgeStrainTerms;
            InitializeAttachmentTerms(
                baselineVertices, vertices, workset, shouldRecoverRestShape,
                count, diagonal, rightHandSide, bendingTerms, edgeStrainTerms);

            AccumulateBendingAndStrainTerms(
                baselineVertices, vertices, contract, workset,
                activePairIndices, shouldRecoverRestShape,
                diagonal, rightHandSide, bendingTerms, edgeStrainTerms,
                out materialStrainConstraints, out activeConstraintCount);
            BuildPreconditioner(
                buffers, count, diagonal, bendingTerms, edgeStrainTerms,
                materialStrainConstraints);
        }

        private static void AccumulateBendingAndStrainTerms(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> vertices,
            NativeSkinningSurfaceContract contract,
            LocalWorkset workset,
            IReadOnlyCollection<int> activePairIndices,
            bool shouldRecoverRestShape,
            float[] diagonal,
            Vector3[] rightHandSide,
            List<BendingLinearization> bendingTerms,
            List<EdgeStrainLinearization> edgeStrainTerms,
            out NativeSkinningMaterialStrainConstraint[]
                materialStrainConstraints,
            out int activeConstraintCount)
        {
            activeConstraintCount = AccumulateBendingTerms(
                baselineVertices, vertices, contract, workset,
                activePairIndices, rightHandSide, bendingTerms);
            AddStrainAndBarrierTerms(
                baselineVertices, vertices, contract, workset,
                shouldRecoverRestShape, diagonal, rightHandSide,
                edgeStrainTerms,
                out materialStrainConstraints, ref activeConstraintCount);
        }

        private static void AddBarrierAndEdgeTerms(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> vertices,
            NativeSkinningSurfaceContract contract,
            LocalWorkset workset,
            bool shouldRecoverRestShape,
            float[] diagonal,
            Vector3[] rightHandSide,
            List<EdgeStrainLinearization> edgeStrainTerms,
            ref int activeConstraintCount)
        {
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
        }

        private static bool IsEligibleActivePair(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            IReadOnlyCollection<int> activePairIndices,
            int pairIndex)
        {
            return activePairIndices.Contains(pairIndex) &&
                IsSharpFoldCorrectionEligible(
                    baselineVertices, contract, pairIndex);
        }

        private static NativeSkinningMaterialStrainConstraint[]
            BuildMaterialStrainConstraints(
                IReadOnlyList<Vector3> baselineVertices,
                IReadOnlyList<Vector3> vertices,
                NativeSkinningSurfaceContract contract,
                LocalWorkset workset,
                bool shouldRecoverRestShape)
        {
            return shouldRecoverRestShape
                ? NativeSkinningRestShapeRecoveryCalculator.BuildConstraints(
                    baselineVertices,
                    contract.RestVertices,
                    vertices,
                    contract.Triangles,
                    workset.FaceIndices,
                    workset.LocalIndexByVertex)
                : Array.Empty<NativeSkinningMaterialStrainConstraint>();
        }

        private static void InitializeAttachmentTerms(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> vertices,
            LocalWorkset workset,
            bool shouldRecoverRestShape,
            int count,
            float[] diagonal,
            Vector3[] rightHandSide,
            List<BendingLinearization> bendingTerms,
            List<EdgeStrainLinearization> edgeStrainTerms)
        {
            Array.Clear(diagonal, 0, count);
            Array.Clear(rightHandSide, 0, count);
            bendingTerms.Clear();
            edgeStrainTerms.Clear();
            InitializeAttachmentDiagonal(
                baselineVertices, vertices, workset,
                shouldRecoverRestShape, count, diagonal, rightHandSide);
            if (!shouldRecoverRestShape)
            {
                AddSmoothnessDiagonal(workset.Edges, diagonal);
            }
        }

        private static void AddSmoothnessDiagonal(
            IEnumerable<LocalEdge> edges,
            IList<float> diagonal)
        {
            foreach (LocalEdge edge in edges)
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

        private static void InitializeAttachmentDiagonal(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> vertices,
            LocalWorkset workset,
            bool shouldRecoverRestShape,
            int count,
            float[] diagonal,
            Vector3[] rightHandSide)
        {
            for (int localIndex = 0; localIndex < count; localIndex++)
            {
                diagonal[localIndex] = shouldRecoverRestShape
                    ? RestShapeRecoveryAttachmentWeight
                    : DisplacementAttachmentWeight;
                if (shouldRecoverRestShape)
                {
                    int vertexIndex = workset.ConstrainedVertices[localIndex];
                    rightHandSide[localIndex] =
                        (baselineVertices[vertexIndex] -
                         vertices[vertexIndex]) *
                        RestShapeRecoveryAttachmentWeight;
                }
            }
        }

        private static int AccumulateBendingTerms(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> vertices,
            NativeSkinningSurfaceContract contract,
            LocalWorkset workset,
            IReadOnlyCollection<int> activePairIndices,
            Vector3[] rightHandSide,
            List<BendingLinearization> bendingTerms)
        {
            int activeConstraintCount = 0;
            foreach (int pairIndex in workset.FacePairIndices)
            {
                if (!TryMeasurePairViolation(
                        baselineVertices, vertices, contract,
                        activePairIndices, pairIndex,
                        out NativeSkinningFacePair pair,
                        out float angle, out float targetAngleDegrees,
                        out Vector3 correction0, out Vector3 correction1,
                        out Vector3 correction2, out Vector3 correction3))
                {
                    continue;
                }

                activeConstraintCount += TryAddBendingTerm(
                    vertices, pair, targetAngleDegrees, angle,
                    correction0, correction1, correction2, correction3,
                    workset.LocalIndexByVertex, rightHandSide, bendingTerms);
            }
            return activeConstraintCount;

        }

        private static bool TryMeasurePairAngles(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> vertices,
            NativeSkinningSurfaceContract contract,
            int pairIndex,
            NativeSkinningFacePair pair,
            ref float angle,
            ref float targetAngleDegrees)
        {
            if (!NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                    vertices, pair, out float measured))
            {
                return false;
            }
            angle = measured;
            targetAngleDegrees = ResolveProjectionAngleDegrees(
                baselineVertices, contract, pairIndex);
            return angle > targetAngleDegrees;
        }

        private static bool TryCalculatePairCorrections(
            IReadOnlyList<Vector3> vertices,
            NativeSkinningFacePair pair,
            float targetAngleDegrees,
            out Vector3 correction0,
            out Vector3 correction1,
            out Vector3 correction2,
            out Vector3 correction3)
        {
            return MeshDihedralConstraintCalculator.TryCalculateCorrections(
                vertices[pair.FirstOpposite],
                vertices[pair.SecondOpposite],
                vertices[pair.FirstEdge],
                vertices[pair.SecondEdge],
                targetAngleDegrees,
                1f,
                out correction0,
                out correction1,
                out correction2,
                out correction3);
        }

        private static bool TryCalculateBendingGradient(
            float angle,
            float targetAngleDegrees,
            Vector3 correction0,
            Vector3 correction1,
            Vector3 correction2,
            Vector3 correction3,
            out float gradientSquaredSum,
            out float gradientScale)
        {
            float violationRadians =
                (angle - targetAngleDegrees) * Mathf.Deg2Rad;
            float correctionSquaredSum =
                correction0.sqrMagnitude +
                correction1.sqrMagnitude +
                correction2.sqrMagnitude +
                correction3.sqrMagnitude;
            if (correctionSquaredSum <= MinimumNormalSquaredMagnitude)
            {
                gradientSquaredSum = 0f;
                gradientScale = 0f;
                return false;
            }
            gradientSquaredSum =
                violationRadians * violationRadians / correctionSquaredSum;
            gradientScale = -gradientSquaredSum / violationRadians;
            return true;
        }

        private static int TryAddBendingTerm(
            IReadOnlyList<Vector3> vertices,
            NativeSkinningFacePair pair,
            float targetAngleDegrees,
            float angle,
            Vector3 correction0,
            Vector3 correction1,
            Vector3 correction2,
            Vector3 correction3,
            IReadOnlyList<int> localIndexByVertex,
            Vector3[] rightHandSide,
            List<BendingLinearization> bendingTerms)
        {
            if (!TryCalculateBendingGradient(
                    angle, targetAngleDegrees,
                    correction0, correction1, correction2, correction3,
                    out float gradientSquaredSum, out float gradientScale))
            {
                return 0;
            }
            AccumulateBendingRhs(
                pair, correction0, correction1, correction2, correction3,
                gradientSquaredSum, localIndexByVertex, rightHandSide);
            bendingTerms.Add(BuildLinearizedTerm(
                pair, gradientScale, localIndexByVertex,
                correction0, correction1, correction2, correction3));
            return 1;
        }

        private static void AccumulateBendingRhs(
            NativeSkinningFacePair pair,
            Vector3 correction0,
            Vector3 correction1,
            Vector3 correction2,
            Vector3 correction3,
            float gradientSquaredSum,
            IReadOnlyList<int> localIndexByVertex,
            Vector3[] rightHandSide)
        {
            AddPairBendingRightHandSide(
                pair, correction0, correction1, correction2, correction3,
                gradientSquaredSum, localIndexByVertex, rightHandSide);
        }

        private static void AddStrainAndBarrierTerms(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> vertices,
            NativeSkinningSurfaceContract contract,
            LocalWorkset workset,
            bool shouldRecoverRestShape,
            float[] diagonal,
            Vector3[] rightHandSide,
            List<EdgeStrainLinearization> edgeStrainTerms,
            out NativeSkinningMaterialStrainConstraint[]
                materialStrainConstraints,
            ref int activeConstraintCount)
        {
            materialStrainConstraints = BuildMaterialStrainConstraints(
                baselineVertices, vertices, contract, workset,
                shouldRecoverRestShape);
            activeConstraintCount += AccumulateMaterialStrainTerms(
                materialStrainConstraints, diagonal, rightHandSide);

            AddBarrierAndEdgeTerms(
                baselineVertices, vertices, contract, workset,
                shouldRecoverRestShape, diagonal, rightHandSide,
                edgeStrainTerms, ref activeConstraintCount);
        }

        private static bool TryMeasurePairViolation(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> vertices,
            NativeSkinningSurfaceContract contract,
            IReadOnlyCollection<int> activePairIndices,
            int pairIndex,
            out NativeSkinningFacePair pair,
            out float angle,
            out float targetAngleDegrees,
            out Vector3 correction0,
            out Vector3 correction1,
            out Vector3 correction2,
            out Vector3 correction3)
        {
            pair = default;
            angle = 0f;
            targetAngleDegrees = 0f;
            correction0 = correction1 = correction2 = correction3 =
                Vector3.zero;
            if (!IsEligibleActivePair(
                    baselineVertices, contract,
                    activePairIndices, pairIndex))
            {
                return false;
            }
            pair = contract.FacePairs[pairIndex];
            if (!TryMeasurePairAngles(
                    baselineVertices, vertices, contract,
                    pairIndex, pair, ref angle, ref targetAngleDegrees))
            {
                return false;
            }
            return TryCalculatePairCorrections(
                vertices, pair, targetAngleDegrees,
                out correction0, out correction1,
                out correction2, out correction3);
        }

        private static BendingLinearization BuildLinearizedTerm(
            NativeSkinningFacePair pair,
            float gradientScale,
            IReadOnlyList<int> localIndexByVertex,
            Vector3 correction0,
            Vector3 correction1,
            Vector3 correction2,
            Vector3 correction3)
        {
            return new BendingLinearization(
                localIndexByVertex[pair.FirstOpposite],
                localIndexByVertex[pair.SecondOpposite],
                localIndexByVertex[pair.FirstEdge],
                localIndexByVertex[pair.SecondEdge],
                correction0 * gradientScale,
                correction1 * gradientScale,
                correction2 * gradientScale,
                correction3 * gradientScale);
        }

        private static void AddPairBendingRightHandSide(
            NativeSkinningFacePair pair,
            Vector3 correction0,
            Vector3 correction1,
            Vector3 correction2,
            Vector3 correction3,
            float gradientSquaredSum,
            IReadOnlyList<int> localIndexByVertex,
            IList<Vector3> rightHandSide)
        {
            AddBendingRightHandSide(
                pair.FirstOpposite, correction0, gradientSquaredSum,
                localIndexByVertex, rightHandSide);
            AddBendingRightHandSide(
                pair.SecondOpposite, correction1, gradientSquaredSum,
                localIndexByVertex, rightHandSide);
            AddBendingRightHandSide(
                pair.FirstEdge, correction2, gradientSquaredSum,
                localIndexByVertex, rightHandSide);
            AddBendingRightHandSide(
                pair.SecondEdge, correction3, gradientSquaredSum,
                localIndexByVertex, rightHandSide);
        }

        private static int AccumulateMaterialStrainTerms(
            NativeSkinningMaterialStrainConstraint[] materialStrainConstraints,
            float[] diagonal,
            Vector3[] rightHandSide)
        {
            int activeConstraintCount = 0;
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
            return activeConstraintCount;
        }


    }
}
