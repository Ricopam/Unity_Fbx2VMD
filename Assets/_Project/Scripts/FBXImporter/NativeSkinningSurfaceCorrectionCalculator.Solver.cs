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
    /// 표면 보정 솔버의 CG 잡·워커 스크래치 버퍼 — 본체에서 이관한 partial.
    /// </summary>
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
        [BurstCompile]
        private struct CgSolveJob : IJob
        {
            internal int Count;
            internal int MaximumIterations;
            internal byte WarmStart;
            [ReadOnly] internal NativeArray<float> Diagonal;
            [ReadOnly] internal NativeArray<float> Preconditioner;
            [ReadOnly] internal NativeArray<Vector3> RightHandSide;
            internal NativeArray<Vector3> Solution;
            internal NativeArray<int> IterationsRun;
            [ReadOnly] internal NativeArray<int2> EdgeIndices;
            [ReadOnly] internal NativeArray<int> BendIndices;
            [ReadOnly] internal NativeArray<Vector3> BendGradients;
            [ReadOnly] internal NativeArray<int2> StrainIndices;
            [ReadOnly] internal NativeArray<Vector3> StrainFirstGradients;
            [ReadOnly] internal NativeArray<Vector3> StrainSecondGradients;
            [ReadOnly] internal NativeArray<int> MaterialOffsets;
            [ReadOnly] internal NativeArray<int> MaterialIndices;
            [ReadOnly] internal NativeArray<float> MaterialCoefficients;
            [ReadOnly] internal NativeArray<float> MaterialWeights;

            public void Execute()
            {
                var residual =
                    new NativeArray<Vector3>(Count, Allocator.Temp);
                var direction =
                    new NativeArray<Vector3>(Count, Allocator.Temp);
                var preconditionedResidual =
                    new NativeArray<Vector3>(Count, Allocator.Temp);
                var matrixDirection =
                    new NativeArray<Vector3>(Count, Allocator.Temp);
                InitializeResidual(residual, matrixDirection);
                ApplyPreconditioner(residual, direction);
                IterationsRun[0] = RunIterations(
                    residual, direction, preconditionedResidual, matrixDirection);
                residual.Dispose();
                direction.Dispose();
                preconditionedResidual.Dispose();
                matrixDirection.Dispose();
            }

            private void InitializeResidual(
                NativeArray<Vector3> residual,
                NativeArray<Vector3> matrixDirection)
            {
                if (WarmStart != 0)
                {
                    ApplyMatrix(Solution, matrixDirection);
                    for (int index = 0; index < Count; index++)
                    {
                        residual[index] = RightHandSide[index] -
                            matrixDirection[index];
                    }
                }
                else
                {
                    for (int index = 0; index < Count; index++)
                    {
                        Solution[index] = Vector3.zero;
                        residual[index] = RightHandSide[index];
                    }
                }
            }

            private int RunIterations(
                NativeArray<Vector3> residual,
                NativeArray<Vector3> direction,
                NativeArray<Vector3> preconditionedResidual,
                NativeArray<Vector3> matrixDirection)
            {
                double residualDot = Dot(residual, direction);
                int run = 0;
                for (int iteration = 0;
                     iteration < MaximumIterations && residualDot > 1e-20d;
                     iteration++)
                {
                    run++;
                    ApplyMatrix(direction, matrixDirection);
                    double denominator = Dot(direction, matrixDirection);
                    if (math.abs(denominator) <= 1e-20d)
                    {
                        break;
                    }
                    float step = (float)(residualDot / denominator);
                    for (int index = 0; index < Count; index++)
                    {
                        Solution[index] += direction[index] * step;
                        residual[index] -= matrixDirection[index] * step;
                    }
                    if (Dot(residual, residual) <= 1e-18d)
                    {
                        break;
                    }
                    ApplyPreconditioner(residual, preconditionedResidual);
                    double nextResidualDot =
                        Dot(residual, preconditionedResidual);
                    float beta = (float)(nextResidualDot / residualDot);
                    for (int index = 0; index < Count; index++)
                    {
                        direction[index] = preconditionedResidual[index] +
                            direction[index] * beta;
                    }
                    residualDot = nextResidualDot;
                }
                return run;
            }

            private void ApplyMatrix(
                NativeArray<Vector3> values,
                NativeArray<Vector3> result)
            {
                for (int index = 0; index < Count; index++)
                {
                    result[index] = values[index] * Diagonal[index];
                }
                for (int index = 0; index < EdgeIndices.Length; index++)
                {
                    int2 edge = EdgeIndices[index];
                    if (edge.x >= 0 && edge.y >= 0)
                    {
                        result[edge.x] -= values[edge.y] *
                            DisplacementSmoothnessWeight;
                        result[edge.y] -= values[edge.x] *
                            DisplacementSmoothnessWeight;
                    }
                }
                ApplyBendTerms(values, result);
                ApplyStrainTerms(values, result);
                ApplyMaterialTerms(values, result);
            }

            private void ApplyBendTerms(
                NativeArray<Vector3> values,
                NativeArray<Vector3> result)
            {
                int bendCount = BendIndices.Length / 4;
                for (int term = 0; term < bendCount; term++)
                {
                    float projectedDisplacement = 0f;
                    for (int index = 0; index < 4; index++)
                    {
                        int localIndex = BendIndices[term * 4 + index];
                        if (localIndex >= 0)
                        {
                            projectedDisplacement += Vector3.Dot(
                                BendGradients[term * 4 + index],
                                values[localIndex]);
                        }
                    }
                    for (int index = 0; index < 4; index++)
                    {
                        int localIndex = BendIndices[term * 4 + index];
                        if (localIndex >= 0)
                        {
                            result[localIndex] += BendingWeight *
                                BendGradients[term * 4 + index] *
                                projectedDisplacement;
                        }
                    }
                }
            }

            private void ApplyStrainTerms(
                NativeArray<Vector3> values,
                NativeArray<Vector3> result)
            {
                for (int term = 0; term < StrainIndices.Length; term++)
                {
                    int2 indices = StrainIndices[term];
                    float projectedDisplacement = 0f;
                    if (indices.x >= 0)
                    {
                        projectedDisplacement += Vector3.Dot(
                            StrainFirstGradients[term],
                            values[indices.x]);
                    }
                    if (indices.y >= 0)
                    {
                        projectedDisplacement += Vector3.Dot(
                            StrainSecondGradients[term],
                            values[indices.y]);
                    }
                    if (indices.x >= 0)
                    {
                        result[indices.x] += EdgeStrainBarrierWeight *
                            StrainFirstGradients[term] *
                            projectedDisplacement;
                    }
                    if (indices.y >= 0)
                    {
                        result[indices.y] += EdgeStrainBarrierWeight *
                            StrainSecondGradients[term] *
                            projectedDisplacement;
                    }
                }
            }

            private void ApplyMaterialTerms(
                NativeArray<Vector3> values,
                NativeArray<Vector3> result)
            {
                for (int constraint = 0;
                     constraint < MaterialWeights.Length;
                     constraint++)
                {
                    int start = MaterialOffsets[constraint];
                    int end = MaterialOffsets[constraint + 1];
                    Vector3 projectedDisplacement = Vector3.zero;
                    for (int index = start; index < end; index++)
                    {
                        projectedDisplacement +=
                            values[MaterialIndices[index]] *
                            MaterialCoefficients[index];
                    }
                    float weight = MaterialWeights[constraint];
                    for (int index = start; index < end; index++)
                    {
                        result[MaterialIndices[index]] +=
                            projectedDisplacement *
                            MaterialCoefficients[index] * weight;
                    }
                }
            }

            private void ApplyPreconditioner(
                NativeArray<Vector3> values,
                NativeArray<Vector3> result)
            {
                for (int index = 0; index < Count; index++)
                {
                    result[index] = values[index] /
                        math.max(Preconditioner[index], 0.000001f);
                }
            }

            private double Dot(
                NativeArray<Vector3> first,
                NativeArray<Vector3> second)
            {
                double result = 0d;
                for (int index = 0; index < Count; index++)
                {
                    result += Vector3.Dot(first[index], second[index]);
                }
                return result;
            }
        }

        private static void ApplyMatrixInto(
            IReadOnlyList<Vector3> values,
            IReadOnlyList<float> diagonal,
            IEnumerable<LocalEdge> edges,
            IEnumerable<BendingLinearization> bendingLinearizations,
            IEnumerable<EdgeStrainLinearization> edgeStrainLinearizations,
            IEnumerable<NativeSkinningMaterialStrainConstraint>
                materialStrainConstraints,
            Vector3[] result,
            int count)
        {
            for (int index = 0; index < count; index++)
            {
                result[index] = values[index] * diagonal[index];
            }
            foreach (LocalEdge edge in edges)
            {
                if (edge.FirstLocalIndex >= 0 && edge.SecondLocalIndex >= 0)
                {
                    result[edge.FirstLocalIndex] -=
                        values[edge.SecondLocalIndex] * DisplacementSmoothnessWeight;
                    result[edge.SecondLocalIndex] -=
                        values[edge.FirstLocalIndex] * DisplacementSmoothnessWeight;
                }
            }
            ApplyBendingTermsInto(values, bendingLinearizations, result);
            ApplyStrainTermsInto(values, edgeStrainLinearizations, result);
            ApplyMaterialTermsInto(values, materialStrainConstraints, result);
        }

        private static void ApplyBendingTermsInto(
            IReadOnlyList<Vector3> values,
            IEnumerable<BendingLinearization> bendingLinearizations,
            Vector3[] result)
        {
            foreach (BendingLinearization term in bendingLinearizations)
            {
                float projectedDisplacement = 0f;
                for (int index = 0; index < 4; index++)
                {
                    int localIndex = term.LocalIndexAt(index);
                    if (localIndex >= 0)
                    {
                        projectedDisplacement += Vector3.Dot(
                            term.GradientAt(index),
                            values[localIndex]);
                    }
                }
                for (int index = 0; index < 4; index++)
                {
                    int localIndex = term.LocalIndexAt(index);
                    if (localIndex >= 0)
                    {
                        result[localIndex] += BendingWeight *
                            term.GradientAt(index) * projectedDisplacement;
                    }
                }
            }
        }

        private static void ApplyStrainTermsInto(
            IReadOnlyList<Vector3> values,
            IEnumerable<EdgeStrainLinearization> edgeStrainLinearizations,
            Vector3[] result)
        {
            foreach (EdgeStrainLinearization term in edgeStrainLinearizations)
            {
                float projectedDisplacement = 0f;
                if (term.FirstLocalIndex >= 0)
                {
                    projectedDisplacement += Vector3.Dot(
                        term.FirstGradient,
                        values[term.FirstLocalIndex]);
                }
                if (term.SecondLocalIndex >= 0)
                {
                    projectedDisplacement += Vector3.Dot(
                        term.SecondGradient,
                        values[term.SecondLocalIndex]);
                }
                if (term.FirstLocalIndex >= 0)
                {
                    result[term.FirstLocalIndex] += EdgeStrainBarrierWeight *
                        term.FirstGradient * projectedDisplacement;
                }
                if (term.SecondLocalIndex >= 0)
                {
                    result[term.SecondLocalIndex] += EdgeStrainBarrierWeight *
                        term.SecondGradient * projectedDisplacement;
                }
            }
        }

        private static void ApplyMaterialTermsInto(
            IReadOnlyList<Vector3> values,
            IEnumerable<NativeSkinningMaterialStrainConstraint> materialStrainConstraints,
            Vector3[] result)
        {
            foreach (NativeSkinningMaterialStrainConstraint constraint in
                     materialStrainConstraints)
            {
                Vector3 projectedDisplacement = Vector3.zero;
                for (int index = 0; index < constraint.LocalIndices.Length; index++)
                {
                    projectedDisplacement +=
                        values[constraint.LocalIndices[index]] *
                        constraint.Coefficients[index];
                }
                for (int index = 0; index < constraint.LocalIndices.Length; index++)
                {
                    int localIndex = constraint.LocalIndices[index];
                    result[localIndex] += projectedDisplacement *
                        constraint.Coefficients[index] * constraint.Weight;
                }
            }
        }

        private static void ApplyDiagonalPreconditionerInto(
            IReadOnlyList<Vector3> values,
            IReadOnlyList<float> diagonal,
            Vector3[] result,
            int count)
        {
            for (int index = 0; index < count; index++)
            {
                result[index] = values[index] /
                    Mathf.Max(diagonal[index], 0.000001f);
            }
        }

        private static double Dot(
            IReadOnlyList<Vector3> first,
            IReadOnlyList<Vector3> second,
            int count)
        {
            double result = 0d;
            for (int index = 0; index < count; index++)
            {
                result += Vector3.Dot(first[index], second[index]);
            }
            return result;
        }

        private static void LimitDisplacement(
            IList<Vector3> displacement,
            int count,
            IReadOnlyList<Vector3> vertices,
            NativeSkinningSurfaceContract contract,
            IReadOnlyCollection<int> activePairIndices)
        {
            float minimumCharacteristicLength = float.PositiveInfinity;
            foreach (int pairIndex in activePairIndices)
            {
                NativeSkinningFacePair pair = contract.FacePairs[pairIndex];
                if (!NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                        vertices,
                        pair,
                        out float angle) ||
                    angle <= ProjectionAngleDegrees)
                {
                    continue;
                }
                minimumCharacteristicLength = Mathf.Min(
                    minimumCharacteristicLength,
                    CalculateFacePairCharacteristicLength(vertices, pair));
            }
            float relativeLimit = float.IsPositiveInfinity(minimumCharacteristicLength)
                ? MaximumAbsoluteStep
                : minimumCharacteristicLength * MaximumRelativeStep;
            float maximumStep = Mathf.Min(MaximumAbsoluteStep, relativeLimit);
            // 버퍼는 이전 패스의 잔여를 담을 수 있으므로 논리 크기까지만 스캔한다.
            float requestedStep = 0f;
            for (int index = 0; index < count; index++)
            {
                float magnitude = displacement[index].magnitude;
                if (magnitude > requestedStep)
                {
                    requestedStep = magnitude;
                }
            }
            if (requestedStep <= maximumStep || requestedStep <= 0f)
            {
                return;
            }
            float scale = maximumStep / requestedStep;
            for (int index = 0; index < count; index++)
            {
                displacement[index] *= scale;
            }
        }

        private static void ProjectEdgeLengthBounds(
            IReadOnlyList<Vector3> currentVertices,
            IList<Vector3> proposedLocalPositions,
            IReadOnlyList<int> constrainedVertices,
            IReadOnlyList<LocalEdge> edges,
            float maximumAbsoluteStrain,
            int projectionIterations)
        {
            for (int iteration = 0; iteration < projectionIterations; iteration++)
            {
                bool hasViolation = false;
                for (int orderedIndex = 0; orderedIndex < edges.Count; orderedIndex++)
                {
                    int edgeIndex = iteration % 2 == 0
                        ? orderedIndex
                        : edges.Count - 1 - orderedIndex;
                    hasViolation |= TryProjectSingleEdgeBound(
                        currentVertices, proposedLocalPositions, constrainedVertices,
                        edges[edgeIndex], maximumAbsoluteStrain);
                }
                if (!hasViolation)
                {
                    break;
                }
            }
        }

        private static bool TryProjectSingleEdgeBound(
            IReadOnlyList<Vector3> currentVertices,
            IList<Vector3> proposedLocalPositions,
            IReadOnlyList<int> constrainedVertices,
            LocalEdge edge,
            float maximumAbsoluteStrain)
        {
            Vector3 first = edge.FirstLocalIndex >= 0
                ? proposedLocalPositions[edge.FirstLocalIndex]
                : currentVertices[edge.FirstVertex];
            Vector3 second = edge.SecondLocalIndex >= 0
                ? proposedLocalPositions[edge.SecondLocalIndex]
                : currentVertices[edge.SecondVertex];
            Vector3 delta = second - first;
            float length = delta.magnitude;
            if (length <= 0.0000001f)
            {
                return false;
            }
            float minimumLength =
                edge.BaselineLength * (1f - maximumAbsoluteStrain);
            float maximumLength =
                edge.BaselineLength * (1f + maximumAbsoluteStrain);
            float targetLength = Mathf.Clamp(
                length,
                minimumLength,
                maximumLength);
            if (Mathf.Approximately(length, targetLength))
            {
                return false;
            }

            Vector3 correction = delta * ((length - targetLength) / length);
            bool movesFirst = edge.FirstLocalIndex >= 0;
            bool movesSecond = edge.SecondLocalIndex >= 0;
            float share = movesFirst && movesSecond ? 0.5f : 1f;
            if (movesFirst)
            {
                proposedLocalPositions[edge.FirstLocalIndex] =
                    ClampDisplacementFromBaseline(
                        currentVertices[
                            constrainedVertices[edge.FirstLocalIndex]],
                        first + correction * share,
                        MaximumAbsoluteStep);
            }
            if (movesSecond)
            {
                proposedLocalPositions[edge.SecondLocalIndex] =
                    ClampDisplacementFromBaseline(
                        currentVertices[
                            constrainedVertices[edge.SecondLocalIndex]],
                        second - correction * share,
                        MaximumAbsoluteStep);
            }
            return true;
        }

        private static void ProjectRestShapeRecoveryConstraints(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> currentVertices,
            IList<Vector3> proposedLocalPositions,
            LocalWorkset workset,
            NativeSkinningSurfaceContract contract,
            IEnumerable<int> activePairIndices)
        {
            for (int pass = 0;
                 pass < RestShapeActiveSetProjectionPasses;
                 pass++)
            {
                int strainProjectionCount = ProjectRestShapeStrainBounds(
                    currentVertices,
                    proposedLocalPositions,
                    workset);
                int angleProjectionCount = ProjectRestShapeDihedralBounds(
                    baselineVertices,
                    currentVertices,
                    proposedLocalPositions,
                    workset.LocalIndexByVertex,
                    contract,
                    activePairIndices);
                if (strainProjectionCount == 0 && angleProjectionCount == 0)
                {
                    break;
                }
            }
        }

        private static int ProjectRestShapeStrainBounds(
            IReadOnlyList<Vector3> currentVertices,
            IList<Vector3> proposedLocalPositions,
            LocalWorkset workset)
        {
            int projectionCount = 0;
            foreach (LocalEdge edge in workset.Edges)
            {
                bool canMoveFirst = edge.FirstLocalIndex >= 0;
                bool canMoveSecond = edge.SecondLocalIndex >= 0;
                if (!canMoveFirst && !canMoveSecond)
                {
                    continue;
                }

                Vector3 currentEdge =
                    currentVertices[edge.SecondVertex] -
                    currentVertices[edge.FirstVertex];
                Vector3 proposedFirst = canMoveFirst
                    ? proposedLocalPositions[edge.FirstLocalIndex]
                    : currentVertices[edge.FirstVertex];
                Vector3 proposedSecond = canMoveSecond
                    ? proposedLocalPositions[edge.SecondLocalIndex]
                    : currentVertices[edge.SecondVertex];
                Vector3 proposedEdge = proposedSecond - proposedFirst;
                float currentLength = currentEdge.magnitude;
                float proposedLength = proposedEdge.magnitude;
                if (currentLength <= 0.00000001f ||
                    proposedLength <= 0.00000001f)
                {
                    continue;
                }

                float baselineRatio = edge.BaselineLength / edge.RestLength;
                // 계산용 스키닝과 재생 정점의 반올림 차이로 경계를 넘지 않도록 출력 여유를 남김.
                float maximumAbsoluteStrain = Mathf.Abs(baselineRatio - 1f) +
                    NativeSkinningRestShapeRecoveryCalculator
                        .MaximumRestStrainIncrease - EdgeLengthProjectionMargin;
                float minimumRatio = Mathf.Max(0f, 1f - maximumAbsoluteStrain);
                float maximumRatio = 1f + maximumAbsoluteStrain;
                float currentRatio = currentLength / edge.RestLength;
                float proposedRatio = proposedLength / edge.RestLength;
                if (!TryResolveStrainTargetRatio(
                        currentRatio, proposedRatio, minimumRatio, maximumRatio,
                        out float targetRatio))
                {
                    continue;
                }

                ApplyStrainCorrection(
                    edge, proposedEdge, proposedLength, targetRatio,
                    canMoveFirst, canMoveSecond, proposedLocalPositions);
                projectionCount++;
            }
            return projectionCount;
        }

        private static void ApplyStrainCorrection(
            LocalEdge edge,
            Vector3 proposedEdge,
            float proposedLength,
            float targetRatio,
            bool canMoveFirst,
            bool canMoveSecond,
            IList<Vector3> proposedLocalPositions)
        {
            float lengthCorrection =
                proposedLength - targetRatio * edge.RestLength;
            Vector3 correction = proposedEdge / proposedLength * lengthCorrection;
            if (canMoveFirst && canMoveSecond)
            {
                correction *= 0.5f;
            }
            if (canMoveFirst)
            {
                proposedLocalPositions[edge.FirstLocalIndex] += correction;
            }
            if (canMoveSecond)
            {
                proposedLocalPositions[edge.SecondLocalIndex] -= correction;
            }
        }

        private static bool TryResolveStrainTargetRatio(
            float currentRatio,
            float proposedRatio,
            float minimumRatio,
            float maximumRatio,
            out float targetRatio)
        {
            if (proposedRatio > maximumRatio)
            {
                targetRatio = maximumRatio;
            }
            else if (currentRatio >=
                         maximumRatio - RestShapeActiveSetActivationMargin &&
                     proposedRatio > currentRatio)
            {
                targetRatio = currentRatio;
            }
            else if (proposedRatio < minimumRatio)
            {
                targetRatio = minimumRatio;
            }
            else if (currentRatio <=
                         minimumRatio + RestShapeActiveSetActivationMargin &&
                     proposedRatio < currentRatio)
            {
                targetRatio = currentRatio;
            }
            else
            {
                targetRatio = 0f;
                return false;
            }
            return true;
        }

        private static int ProjectRestShapeDihedralBounds(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> currentVertices,
            IList<Vector3> proposedLocalPositions,
            IReadOnlyList<int> localIndexByVertex,
            NativeSkinningSurfaceContract contract,
            IEnumerable<int> activePairIndices)
        {
            int projectionCount = 0;
            foreach (int pairIndex in activePairIndices)
            {
                if (!IsRestShapeRecoveryPair(
                        baselineVertices,
                        contract,
                        pairIndex))
                {
                    continue;
                }

                projectionCount += TryProjectDihedralPair(
                    baselineVertices, currentVertices, proposedLocalPositions,
                    localIndexByVertex, contract, pairIndex);
            }
            return projectionCount;
        }

        private static int TryProjectDihedralPair(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> currentVertices,
            IList<Vector3> proposedLocalPositions,
            IReadOnlyList<int> localIndexByVertex,
            NativeSkinningSurfaceContract contract,
            int pairIndex)
        {
            NativeSkinningFacePair pair = contract.FacePairs[pairIndex];
            Vector3 opposite0 = ReadProposedPosition(
                pair.FirstOpposite, currentVertices, proposedLocalPositions, localIndexByVertex);
            Vector3 opposite1 = ReadProposedPosition(
                pair.SecondOpposite, currentVertices, proposedLocalPositions, localIndexByVertex);
            Vector3 edge0 = ReadProposedPosition(
                pair.FirstEdge, currentVertices, proposedLocalPositions, localIndexByVertex);
            Vector3 edge1 = ReadProposedPosition(
                pair.SecondEdge, currentVertices, proposedLocalPositions, localIndexByVertex);
            if (!MeshDihedralConstraintCalculator.TryCalculateCorrections(
                    opposite0,
                    opposite1,
                    edge0,
                    edge1,
                    ResolveProjectionAngleDegrees(
                        baselineVertices,
                        contract,
                        pairIndex),
                    1f,
                    out Vector3 correction0,
                    out Vector3 correction1,
                    out Vector3 correction2,
                    out Vector3 correction3))
            {
                return 0;
            }

            float maximumCorrectionMagnitude = Mathf.Max(
                correction0.magnitude,
                correction1.magnitude,
                correction2.magnitude,
                correction3.magnitude);
            float correctionScale = maximumCorrectionMagnitude >
                                    RestShapeMaximumProjectionCorrection
                ? RestShapeMaximumProjectionCorrection /
                  maximumCorrectionMagnitude
                : 1f;
            ApplyPairCorrections(
                pair, correction0 * correctionScale, correction1 * correctionScale,
                correction2 * correctionScale, correction3 * correctionScale,
                proposedLocalPositions, localIndexByVertex);
            return 1;
        }

        private static void ApplyPairCorrections(
            NativeSkinningFacePair pair,
            Vector3 correction0,
            Vector3 correction1,
            Vector3 correction2,
            Vector3 correction3,
            IList<Vector3> proposedLocalPositions,
            IReadOnlyList<int> localIndexByVertex)
        {
            ApplyProjectionCorrection(
                pair.FirstOpposite, correction0, proposedLocalPositions, localIndexByVertex);
            ApplyProjectionCorrection(
                pair.SecondOpposite, correction1, proposedLocalPositions, localIndexByVertex);
            ApplyProjectionCorrection(
                pair.FirstEdge, correction2, proposedLocalPositions, localIndexByVertex);
            ApplyProjectionCorrection(
                pair.SecondEdge, correction3, proposedLocalPositions, localIndexByVertex);
        }

        private static Vector3 ReadProposedPosition(
            int vertexIndex,
            IReadOnlyList<Vector3> currentVertices,
            IList<Vector3> proposedLocalPositions,
            IReadOnlyList<int> localIndexByVertex)
        {
            int localIndex = localIndexByVertex[vertexIndex];
            return localIndex < 0
                ? currentVertices[vertexIndex]
                : proposedLocalPositions[localIndex];
        }

        private static void ApplyProjectionCorrection(
            int vertexIndex,
            Vector3 correction,
            IList<Vector3> proposedLocalPositions,
            IReadOnlyList<int> localIndexByVertex)
        {
            int localIndex = localIndexByVertex[vertexIndex];
            if (localIndex < 0)
            {
                return;
            }

            proposedLocalPositions[localIndex] += correction;
        }

        private static bool TryAcceptGlobalStep(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> currentVertices,
            IReadOnlyList<Vector3> proposedLocalPositions,
            LocalWorkset workset,
            NativeSkinningSurfaceContract contract,
            IReadOnlyCollection<int> activePairIndices,
            double beforeEnergy,
            CorrectionConfiguration configuration,
            bool shouldRecoverRestShape,
            bool allowsStrainRecovery,
            Vector3[] candidate,
            out Vector3[] acceptedVertices,
            out double acceptedEnergy)
        {
            acceptedVertices = null;
            acceptedEnergy = 0d;
            float blend = CalculateOrientationSafeBlend(
                baselineVertices,
                currentVertices,
                proposedLocalPositions,
                workset.LocalIndexByVertex,
                contract,
                workset.FaceIndices);
            if (blend <= 0f)
            {
                return false;
            }
            Array.Copy(
                (Vector3[])currentVertices,
                candidate,
                currentVertices.Count);
            for (int attempt = 0; attempt < LineSearchIterations; attempt++)
            {
                BlendCandidate(
                    baselineVertices, currentVertices, proposedLocalPositions,
                    workset, contract, configuration, candidate, blend);
                if (TryEvaluateStepCandidate(
                        baselineVertices, currentVertices, candidate, workset,
                        contract, activePairIndices, beforeEnergy, configuration,
                        shouldRecoverRestShape, allowsStrainRecovery,
                        out acceptedEnergy))
                {
                    acceptedVertices = candidate;
                    return true;
                }
                blend *= 0.5f;
            }
            return false;
        }

        private static void BlendCandidate(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> currentVertices,
            IReadOnlyList<Vector3> proposedLocalPositions,
            LocalWorkset workset,
            NativeSkinningSurfaceContract contract,
            CorrectionConfiguration configuration,
            Vector3[] candidate,
            float blend)
        {
            for (int localIndex = 0;
                 localIndex < workset.ConstrainedVertices.Length;
                 localIndex++)
            {
                int vertexIndex = workset.ConstrainedVertices[localIndex];
                candidate[vertexIndex] = Vector3.LerpUnclamped(
                    currentVertices[vertexIndex],
                    proposedLocalPositions[localIndex],
                    blend);
                candidate[vertexIndex] = ClampDisplacementFromBaseline(
                    baselineVertices[vertexIndex],
                    candidate[vertexIndex],
                    contract.ArmChainLength *
                    configuration.MaximumTotalDisplacementToArmLengthRatio);
            }
        }

        // 후보의 결함·에너지를 평가해 수용 여부를 판정한다.
        private static bool TryEvaluateStepCandidate(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> currentVertices,
            Vector3[] candidate,
            LocalWorkset workset,
            NativeSkinningSurfaceContract contract,
            IReadOnlyCollection<int> activePairIndices,
            double beforeEnergy,
            CorrectionConfiguration configuration,
            bool shouldRecoverRestShape,
            bool allowsStrainRecovery,
            out double candidateEnergy)
        {
            bool hasStrainDefect = shouldRecoverRestShape
                ? HasRestShapeRecoveryStrainDefect(
                    baselineVertices,
                    candidate,
                    workset.Edges)
                : allowsStrainRecovery
                    ? HasUnrecoveringEdgeStrainDefect(
                        currentVertices,
                        candidate,
                        workset.Edges,
                        configuration.MaximumEdgeLengthStrain)
                    : HasEdgeStrainDefect(
                        candidate,
                        workset.Edges,
                        configuration.MaximumEdgeLengthStrain);
            bool hasRestShapeSharpFoldDefect = shouldRecoverRestShape &&
                HasRestShapeSharpFoldDefect(
                    baselineVertices,
                    candidate,
                    contract,
                    workset.FacePairIndices);
            candidateEnergy = CalculateConstraintEnergy(
                baselineVertices,
                candidate,
                contract,
                activePairIndices,
                workset.FacePairIndices,
                workset.FaceIndices,
                workset.Edges,
                shouldRecoverRestShape);
            return !HasOrientationDefect(
                    baselineVertices,
                    candidate,
                    contract,
                    workset.FaceIndices) &&
                !hasStrainDefect &&
                !hasRestShapeSharpFoldDefect &&
                candidateEnergy < beforeEnergy;
        }

        private static bool HasRestShapeSharpFoldDefect(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> candidateVertices,
            NativeSkinningSurfaceContract contract,
            IEnumerable<int> localPairIndices)
        {
            foreach (int pairIndex in localPairIndices)
            {
                if (contract.RestAnglesDegrees[pairIndex] > RestSmoothAngleDegrees)
                {
                    continue;
                }

                NativeSkinningFacePair pair = contract.FacePairs[pairIndex];
                if (NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                        baselineVertices,
                        pair,
                        out float baselineAngleDegrees) &&
                    baselineAngleDegrees <= SharpFoldAngleDegrees &&
                    NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                        candidateVertices,
                        pair,
                        out float candidateAngleDegrees) &&
                    candidateAngleDegrees > SharpFoldAngleDegrees)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasRestShapeRecoveryStrainDefect(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> candidateVertices,
            IEnumerable<LocalEdge> edges)
        {
            foreach (LocalEdge edge in edges)
            {
                float strainIncrease = NativeSkinningRestShapeRecoveryCalculator
                    .CalculateStrainIncrease(
                        edge.BaselineLength,
                        Vector3.Distance(
                            candidateVertices[edge.FirstVertex],
                            candidateVertices[edge.SecondVertex]),
                        edge.RestLength);
                if (!IsFinite(strainIncrease) ||
                    strainIncrease > NativeSkinningRestShapeRecoveryCalculator
                        .MaximumRestStrainIncrease - EdgeLengthProjectionMargin)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasUnrecoveringEdgeStrainDefect(
            IReadOnlyList<Vector3> currentVertices,
            IReadOnlyList<Vector3> candidateVertices,
            IReadOnlyList<LocalEdge> edges,
            float maximumAbsoluteStrain)
        {
            double currentViolation = CalculateEdgeStrainViolationEnergy(
                currentVertices,
                edges,
                maximumAbsoluteStrain);
            double candidateViolation = CalculateEdgeStrainViolationEnergy(
                candidateVertices,
                edges,
                maximumAbsoluteStrain);
            return double.IsNaN(candidateViolation) ||
                   double.IsInfinity(candidateViolation) ||
                   (candidateViolation > 0d &&
                    candidateViolation >= currentViolation - 0.000000000001d);
        }

        private static double CalculateEdgeStrainViolationEnergy(
            IReadOnlyList<Vector3> vertices,
            IEnumerable<LocalEdge> edges,
            float maximumAbsoluteStrain)
        {
            double energy = 0d;
            foreach (LocalEdge edge in edges)
            {
                float absoluteStrain = Mathf.Abs(
                    Vector3.Distance(
                        vertices[edge.FirstVertex],
                        vertices[edge.SecondVertex]) /
                    edge.BaselineLength - 1f);
                if (!IsFinite(absoluteStrain))
                {
                    return double.PositiveInfinity;
                }
                float excess = Mathf.Max(
                    0f,
                    absoluteStrain - maximumAbsoluteStrain);
                energy += excess * excess;
            }
            return energy;
        }

        private static bool HasEdgeStrainDefect(
            IReadOnlyList<Vector3> vertices,
            IEnumerable<LocalEdge> edges,
            float maximumAbsoluteStrain)
        {
            foreach (LocalEdge edge in edges)
            {
                float absoluteStrain = Mathf.Abs(
                    Vector3.Distance(
                        vertices[edge.FirstVertex],
                        vertices[edge.SecondVertex]) /
                    edge.BaselineLength - 1f);
                if (!IsFinite(absoluteStrain) ||
                    absoluteStrain > maximumAbsoluteStrain)
                {
                    return true;
                }
            }
            return false;
        }

        private static float CalculateOrientationSafeBlend(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> currentVertices,
            IReadOnlyList<Vector3> proposedLocalPositions,
            IReadOnlyList<int> localIndexByVertex,
            NativeSkinningSurfaceContract contract,
            IEnumerable<int> faceIndices)
        {
            float safeBlend = 1f;
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
                float minimumSignedArea = Mathf.Max(
                    minimumNormalMagnitude,
                    baselineArea * MinimumSignedAreaRatio);
                float candidateSignedArea = CalculateBlendedSignedArea(
                    safeBlend,
                    firstIndex,
                    secondIndex,
                    thirdIndex,
                    baselineNormal,
                    currentVertices,
                    proposedLocalPositions,
                    localIndexByVertex);
                if (candidateSignedArea >= minimumSignedArea)
                {
                    continue;
                }

                safeBlend = SearchSafeBlendForFace(
                    safeBlend, minimumSignedArea,
                    firstIndex, secondIndex, thirdIndex, baselineNormal,
                    currentVertices, proposedLocalPositions, localIndexByVertex) *
                    OrientationSafetyFactor;
            }
            return safeBlend;
        }

        private static float SearchSafeBlendForFace(
            float safeBlend,
            float minimumSignedArea,
            int firstIndex,
            int secondIndex,
            int thirdIndex,
            Vector3 baselineNormal,
            IReadOnlyList<Vector3> currentVertices,
            IReadOnlyList<Vector3> proposedLocalPositions,
            IReadOnlyList<int> localIndexByVertex)
        {
            float lowerBlend = 0f;
            float upperBlend = safeBlend;
            for (int iteration = 0;
                 iteration < OrientationSearchIterations;
                 iteration++)
            {
                float middleBlend = (lowerBlend + upperBlend) * 0.5f;
                float signedArea = CalculateBlendedSignedArea(
                    middleBlend,
                    firstIndex,
                    secondIndex,
                    thirdIndex,
                    baselineNormal,
                    currentVertices,
                    proposedLocalPositions,
                    localIndexByVertex);
                if (signedArea >= minimumSignedArea)
                {
                    lowerBlend = middleBlend;
                }
                else
                {
                    upperBlend = middleBlend;
                }
            }
            return lowerBlend;
        }

        private static float CalculateBlendedSignedArea(
            float blend,
            int firstIndex,
            int secondIndex,
            int thirdIndex,
            Vector3 baselineNormal,
            IReadOnlyList<Vector3> currentVertices,
            IReadOnlyList<Vector3> proposedLocalPositions,
            IReadOnlyList<int> localIndexByVertex)
        {
            Vector3 first = ReadBlendedPosition(
                blend,
                firstIndex,
                currentVertices,
                proposedLocalPositions,
                localIndexByVertex);
            Vector3 second = ReadBlendedPosition(
                blend,
                secondIndex,
                currentVertices,
                proposedLocalPositions,
                localIndexByVertex);
            Vector3 third = ReadBlendedPosition(
                blend,
                thirdIndex,
                currentVertices,
                proposedLocalPositions,
                localIndexByVertex);
            return Vector3.Dot(
                Vector3.Cross(second - first, third - first),
                baselineNormal);
        }

        private static Vector3 ReadBlendedPosition(
            float blend,
            int vertexIndex,
            IReadOnlyList<Vector3> currentVertices,
            IReadOnlyList<Vector3> proposedLocalPositions,
            IReadOnlyList<int> localIndexByVertex)
        {
            int localIndex = localIndexByVertex[vertexIndex];
            return localIndex < 0
                ? currentVertices[vertexIndex]
                : Vector3.LerpUnclamped(
                    currentVertices[vertexIndex],
                    proposedLocalPositions[localIndex],
                    blend);
        }

        private static double CalculateConstraintEnergy(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> vertices,
            NativeSkinningSurfaceContract contract,
            IReadOnlyCollection<int> activePairIndices,
            IEnumerable<int> facePairIndices,
            IEnumerable<int> faceIndices,
            IEnumerable<LocalEdge> edges,
            bool shouldRecoverRestShape)
        {
            double energy = 0d;
            if (shouldRecoverRestShape)
            {
                foreach (LocalEdge edge in edges)
                {
                    if (edge.FirstLocalIndex < 0 || edge.SecondLocalIndex < 0)
                    {
                        continue;
                    }
                    float edgeLength = Vector3.Distance(
                        vertices[edge.FirstVertex],
                        vertices[edge.SecondVertex]);
                    energy += NativeSkinningRestShapeRecoveryCalculator
                        .CalculateSquaredStrainEnergy(
                            edgeLength,
                            edge.RestLength);
                }
                return energy;
            }

            energy += CalculateBendingEnergy(
                baselineVertices, vertices, contract, activePairIndices, facePairIndices);
            energy += CalculateEdgeStrainEnergy(vertices, edges);
            energy += CalculateOrientationEnergy(
                baselineVertices, vertices, contract, faceIndices);
            return energy;
        }

        private static double CalculateBendingEnergy(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> vertices,
            NativeSkinningSurfaceContract contract,
            IReadOnlyCollection<int> activePairIndices,
            IEnumerable<int> facePairIndices)
        {
            double energy = 0d;
            foreach (int pairIndex in facePairIndices)
            {
                if (!IsSharpFoldCorrectionEligible(
                        baselineVertices,
                        contract,
                        pairIndex) ||
                    !NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                        vertices,
                        contract.FacePairs[pairIndex],
                        out float angle))
                {
                    continue;
                }
                float targetAngle = activePairIndices.Contains(pairIndex)
                    ? ResolveProjectionAngleDegrees(
                        baselineVertices,
                        contract,
                        pairIndex)
                    : SharpFoldAngleDegrees;
                float violationRadians =
                    Mathf.Max(0f, angle - targetAngle) * Mathf.Deg2Rad;
                energy += 0.5d * BendingWeight *
                    violationRadians * violationRadians;
            }
            return energy;
        }

        private static double CalculateEdgeStrainEnergy(
            IReadOnlyList<Vector3> vertices,
            IEnumerable<LocalEdge> edges)
        {
            double energy = 0d;
            foreach (LocalEdge edge in edges)
            {
                float edgeLength = Vector3.Distance(
                    vertices[edge.FirstVertex],
                    vertices[edge.SecondVertex]);
                float strain = edgeLength / edge.BaselineLength - 1f;
                float violation = Mathf.Max(
                    0f,
                    Mathf.Abs(strain) - EdgeStrainActivation);
                energy += 0.5d * EdgeStrainBarrierWeight *
                    violation * violation;
            }
            return energy;
        }

        private static double CalculateOrientationEnergy(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> vertices,
            NativeSkinningSurfaceContract contract,
            IEnumerable<int> faceIndices)
        {
            double energy = 0d;
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
                float signedArea = Vector3.Dot(
                    Vector3.Cross(
                        vertices[secondIndex] - vertices[firstIndex],
                        vertices[thirdIndex] - vertices[firstIndex]),
                    baselineNormal);
                float activationArea = baselineArea * OrientationActivationRatio;
                float violation = Mathf.Max(
                    0f,
                    (activationArea - signedArea) / activationArea);
                energy += 0.5d * OrientationBarrierWeight * violation * violation;
            }
            return energy;
        }

        private static void UpdateActiveSharpFoldPairs(
            IReadOnlyList<Vector3> vertices,
            NativeSkinningSurfaceContract contract,
            IEnumerable<int> facePairIndices,
            ISet<int> activePairIndices)
        {
            foreach (int pairIndex in facePairIndices)
            {
                if (NativeSkinningSurfaceDefectDetector.IsCorrectable(
                        vertices,
                        contract.FacePairs[pairIndex],
                        contract.FacePairRestLengths[pairIndex],
                        contract.RestAnglesDegrees[pairIndex],
                        contract.RestShapeRecoveryMaximumCosines[pairIndex]))
                {
                    activePairIndices.Add(pairIndex);
                }
            }
        }

        // rest 각도 상한을 넘는 자연 주름도 baseline에서 접히지 않았다면
        // 보정 도중 임계값을 넘었을 때 되돌릴 제약 대상으로 인정한다.
        // baseline이 이미 접힌 쌍은 계약이 허용한 상태이므로 건드리지 않는다.
        private static bool IsSharpFoldCorrectionEligible(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            int pairIndex)
        {
            if (contract.RestAnglesDegrees[pairIndex] <= RestSmoothAngleDegrees)
            {
                return true;
            }
            return NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                       baselineVertices,
                       contract.FacePairs[pairIndex],
                       out float baselineAngleDegrees) &&
                   baselineAngleDegrees <= SharpFoldAngleDegrees;
        }

        private static void UpdateActiveCorrectionPairs(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> currentVertices,
            NativeSkinningSurfaceContract contract,
            IEnumerable<int> facePairIndices,
            bool shouldRecoverRestShape,
            ISet<int> activePairIndices)
        {
            foreach (int pairIndex in facePairIndices)
            {
                if (shouldRecoverRestShape)
                {
                    if (IsRestShapeRecoveryPair(
                            baselineVertices,
                            contract,
                            pairIndex))
                    {
                        activePairIndices.Add(pairIndex);
                    }
                    continue;
                }

                if (NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                        currentVertices,
                        contract.FacePairs[pairIndex],
                        out float angleDegrees) &&
                    angleDegrees > SharpFoldAngleDegrees &&
                    IsSharpFoldCorrectionEligible(
                        baselineVertices,
                        contract,
                        pairIndex))
                {
                    activePairIndices.Add(pairIndex);
                }
            }
        }

        private static float ResolveProjectionAngleDegrees(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            int pairIndex)
        {
            if (!IsRestShapeRecoveryPair(
                    baselineVertices,
                    contract,
                    pairIndex))
            {
                return ProjectionAngleDegrees;
            }

            NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                baselineVertices,
                contract.FacePairs[pairIndex],
                out float baselineAngleDegrees);
            return Mathf.Min(
                RestShapeRecoveryAngleDegrees,
                baselineAngleDegrees - RestShapeMinimumAngleImprovementDegrees);
        }

        private static bool IsRestShapeRecoveryPair(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            int pairIndex)
        {
            NativeSkinningFacePair pair = contract.FacePairs[pairIndex];
            return NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                       baselineVertices,
                       pair,
                       out float baselineAngleDegrees) &&
                   baselineAngleDegrees <= SharpFoldAngleDegrees &&
                   NativeSkinningRestShapeCollapseDetector.IsSeverelyCollapsed(
                       baselineVertices,
                       pair,
                       contract.FacePairRestLengths[pairIndex],
                       contract.RestAnglesDegrees[pairIndex],
                       baselineAngleDegrees);
        }

        private static LocalWorkset BuildLocalWorkset(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            IEnumerable<int> seedFacePairIndices,
            int ringCount,
            IReadOnlyList<FrameEdge> frameEdges,
            SolverBuffers buffers)
        {
            var constrainedVertexSet = new HashSet<int>();
            foreach (int pairIndex in seedFacePairIndices)
            {
                NativeSkinningFacePair pair = contract.FacePairs[pairIndex];
                constrainedVertexSet.Add(pair.FirstOpposite);
                constrainedVertexSet.Add(pair.SecondOpposite);
                constrainedVertexSet.Add(pair.FirstEdge);
                constrainedVertexSet.Add(pair.SecondEdge);
            }
            int vertexCount = baselineVertices.Count;
            int faceCount = contract.Triangles.Length / 3;
            buffers.EnsureWorksetSize(
                vertexCount,
                contract.FacePairs.Length,
                faceCount);
            int[] localIndexByVertex = buffers.LocalIndexByVertex;
            int[] faceMark = buffers.FaceMark;
            int[] facePairMark = buffers.FacePairMark;
            ExpandConstrainedVertices(contract, constrainedVertexSet, ringCount, buffers, faceMark);
            int[] constrainedVertices = AssignLocalIndices(
                constrainedVertexSet, localIndexByVertex, vertexCount);

            int pairStamp = buffers.NextMarkStamp();
            MarkByVertex(contract.FacePairIndicesByVertex, constrainedVertices,
                facePairMark, pairStamp);
            int[] facePairIndices = CollectMarked(
                facePairMark, pairStamp, contract.FacePairs.Length);

            int faceStamp = buffers.NextMarkStamp();
            MarkByVertex(contract.AffectedFaceIndicesByVertex, constrainedVertices,
                faceMark, faceStamp);
            int[] faceIndices = CollectMarked(faceMark, faceStamp, faceCount);

            LocalEdge[] edges = BuildLocalEdges(frameEdges, localIndexByVertex);
            return new LocalWorkset(
                constrainedVertices,
                localIndexByVertex,
                facePairIndices,
                faceIndices,
                edges);
        }

        // 시드 정점에서 ringCount만큼 면 이웃을 확장한다. 스탬프로 중복 순회를 막는다.
        private static void ExpandConstrainedVertices(
            NativeSkinningSurfaceContract contract,
            HashSet<int> constrainedVertexSet,
            int ringCount,
            SolverBuffers buffers,
            int[] faceMark)
        {
            var frontier = new HashSet<int>(constrainedVertexSet);
            for (int ring = 0; ring < ringCount && frontier.Count > 0; ring++)
            {
                // 같은 면을 두 번 순회하지 않게 스탬프로 표시한다.
                int ringStamp = buffers.NextMarkStamp();
                var nextFrontier = new HashSet<int>();
                foreach (int index in frontier)
                {
                    foreach (int faceIndex in
                             contract.AffectedFaceIndicesByVertex[index])
                    {
                        if (faceMark[faceIndex] == ringStamp)
                        {
                            continue;
                        }
                        faceMark[faceIndex] = ringStamp;
                        int offset = faceIndex * 3;
                        for (int corner = 0; corner < 3; corner++)
                        {
                            int vertexIndex =
                                contract.Triangles[offset + corner];
                            if (constrainedVertexSet.Add(vertexIndex))
                            {
                                nextFrontier.Add(vertexIndex);
                            }
                        }
                    }
                }
                frontier = nextFrontier;
            }
        }

        // 멤버십 표시 후 오름차순 스캔 — 정렬된 인덱스와 로컬 인덱스를 동시에 얻는다.
        private static int[] AssignLocalIndices(
            HashSet<int> constrainedVertexSet,
            int[] localIndexByVertex,
            int vertexCount)
        {
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                localIndexByVertex[vertexIndex] =
                    constrainedVertexSet.Contains(vertexIndex) ? 0 : -1;
            }
            int[] constrainedVertices = new int[constrainedVertexSet.Count];
            int constrainedCount = 0;
            for (int vertexIndex = 0; vertexIndex < vertexCount; vertexIndex++)
            {
                if (localIndexByVertex[vertexIndex] >= 0)
                {
                    localIndexByVertex[vertexIndex] = constrainedCount;
                    constrainedVertices[constrainedCount] = vertexIndex;
                    constrainedCount++;
                }
            }
            return constrainedVertices;
        }

        private static void MarkByVertex(
            IReadOnlyList<int>[] indicesByVertex,
            int[] vertices,
            int[] marks,
            int stamp)
        {
            foreach (int vertexIndex in vertices)
            {
                foreach (int index in indicesByVertex[vertexIndex])
                {
                    marks[index] = stamp;
                }
            }
        }

        private static int[] CollectMarked(int[] marks, int stamp, int total)
        {
            int markedCount = 0;
            for (int index = 0; index < total; index++)
            {
                if (marks[index] == stamp)
                {
                    markedCount++;
                }
            }
            var indices = new int[markedCount];
            int writeIndex = 0;
            for (int index = 0; index < total; index++)
            {
                if (marks[index] == stamp)
                {
                    indices[writeIndex] = index;
                    writeIndex++;
                }
            }
            return indices;
        }

        private static LocalEdge[] BuildLocalEdges(
            IReadOnlyList<FrameEdge> frameEdges,
            int[] localIndexByVertex)
        {
            int edgeCount = 0;
            foreach (FrameEdge edge in frameEdges)
            {
                if (localIndexByVertex[edge.FirstVertex] >= 0 ||
                    localIndexByVertex[edge.SecondVertex] >= 0)
                {
                    edgeCount++;
                }
            }
            var edges = new LocalEdge[edgeCount];
            int edgeWriteIndex = 0;
            foreach (FrameEdge edge in frameEdges)
            {
                int firstLocalIndex = localIndexByVertex[edge.FirstVertex];
                int secondLocalIndex = localIndexByVertex[edge.SecondVertex];
                if (firstLocalIndex < 0 && secondLocalIndex < 0)
                {
                    continue;
                }
                edges[edgeWriteIndex] = new LocalEdge(
                    edge.FirstVertex,
                    edge.SecondVertex,
                    firstLocalIndex,
                    secondLocalIndex,
                    edge.BaselineLength,
                    edge.RestLength);
                edgeWriteIndex++;
            }
            return edges;
        }

        private static FrameEdge[] BuildFrameEdges(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract)
        {
            return contract.AffectedFaceIndices
                .SelectMany(faceIndex =>
                {
                    int[] vertices = GetFaceVertices(contract.Triangles, faceIndex);
                    return new[]
                    {
                        new NativeSkinningEdgeKey(vertices[0], vertices[1]),
                        new NativeSkinningEdgeKey(vertices[1], vertices[2]),
                        new NativeSkinningEdgeKey(vertices[2], vertices[0])
                    };
                })
                .Distinct()
                .Select(edge => new FrameEdge(
                    edge.First,
                    edge.Second,
                    Vector3.Distance(
                        baselineVertices[edge.First],
                        baselineVertices[edge.Second]),
                    Vector3.Distance(
                        contract.RestVertices[edge.First],
                        contract.RestVertices[edge.Second])))
                .Where(edge =>
                    edge.BaselineLength > 0.00000001f &&
                    edge.RestLength > 0.00000001f)
                .ToArray();
        }

        private static int[] GetFaceVertices(
            IReadOnlyList<int> triangles,
            int faceIndex)
        {
            int offset = faceIndex * 3;
            return new[]
            {
                triangles[offset],
                triangles[offset + 1],
                triangles[offset + 2]
            };
        }

        private static bool HasOrientationDefect(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> candidateVertices,
            NativeSkinningSurfaceContract contract,
            IEnumerable<int> faceIndices)
        {
            foreach (int faceIndex in faceIndices)
            {
                if (!TryCalculateFaceNormal(
                        baselineVertices,
                        contract.Triangles,
                        faceIndex,
                        out Vector3 baselineNormal))
                {
                    continue;
                }
                if (!TryCalculateFaceNormal(
                        candidateVertices,
                        contract.Triangles,
                        faceIndex,
                        out Vector3 candidateNormal) ||
                    Vector3.Dot(baselineNormal, candidateNormal) <= 0f)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryCalculateFaceNormal(
            IReadOnlyList<Vector3> vertices,
            IReadOnlyList<int> triangles,
            int faceIndex,
            out Vector3 normal)
        {
            int offset = faceIndex * 3;
            Vector3 first = vertices[triangles[offset]];
            Vector3 second = vertices[triangles[offset + 1]];
            Vector3 third = vertices[triangles[offset + 2]];
            normal = Vector3.Cross(second - first, third - first);
            if (normal.sqrMagnitude < MinimumNormalSquaredMagnitude)
            {
                normal = Vector3.zero;
                return false;
            }
            normal /= Mathf.Sqrt(normal.sqrMagnitude);
            return true;
        }

        private static float CalculateFacePairCharacteristicLength(
            IReadOnlyList<Vector3> vertices,
            NativeSkinningFacePair pair)
        {
            float minimumLength = Vector3.Distance(
                vertices[pair.FirstEdge],
                vertices[pair.SecondEdge]);
            minimumLength = Mathf.Min(
                minimumLength,
                Vector3.Distance(
                    vertices[pair.FirstOpposite],
                    vertices[pair.FirstEdge]));
            minimumLength = Mathf.Min(
                minimumLength,
                Vector3.Distance(
                    vertices[pair.FirstOpposite],
                    vertices[pair.SecondEdge]));
            minimumLength = Mathf.Min(
                minimumLength,
                Vector3.Distance(
                    vertices[pair.SecondOpposite],
                    vertices[pair.FirstEdge]));
            return Mathf.Min(
                minimumLength,
                Vector3.Distance(
                    vertices[pair.SecondOpposite],
                    vertices[pair.SecondEdge]));
        }

        private static Vector3 ClampDisplacementFromBaseline(
            Vector3 baseline,
            Vector3 candidate,
            float maximumDistance)
        {
            Vector3 displacement = candidate - baseline;
            float squaredDistance = displacement.sqrMagnitude;
            float squaredMaximumDistance = maximumDistance * maximumDistance;
            if (squaredDistance <= squaredMaximumDistance || squaredDistance <= 0f)
            {
                return candidate;
            }
            return baseline + displacement *
                (maximumDistance / Mathf.Sqrt(squaredDistance));
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private readonly struct CorrectionConfiguration
        {
            // 진단 테스트가 이 6인자 시그니처로 인스턴스를 만들므로 유지한다.
            internal CorrectionConfiguration(
                int localGlobalIterations,
                int conjugateGradientIterations,
                int edgeLengthProjectionIterations,
                float maximumEdgeLengthStrain,
                float maximumTotalDisplacementToArmLengthRatio,
                int worksetRingCount = DefaultWorksetRingCount)
                : this(
                    localGlobalIterations,
                    conjugateGradientIterations,
                    edgeLengthProjectionIterations,
                    maximumEdgeLengthStrain,
                    maximumTotalDisplacementToArmLengthRatio,
                    worksetRingCount,
                    false,
                    false,
                    false)
            {
            }

            internal CorrectionConfiguration(
                int localGlobalIterations,
                int conjugateGradientIterations,
                int edgeLengthProjectionIterations,
                float maximumEdgeLengthStrain,
                float maximumTotalDisplacementToArmLengthRatio,
                int worksetRingCount,
                bool allowsEarlyConvergence,
                bool allowsWarmStart,
                bool allowsBurst = false)
            {
                LocalGlobalIterations = localGlobalIterations;
                ConjugateGradientIterations = conjugateGradientIterations;
                EdgeLengthProjectionIterations = edgeLengthProjectionIterations;
                MaximumEdgeLengthStrain = maximumEdgeLengthStrain;
                MaximumTotalDisplacementToArmLengthRatio =
                    maximumTotalDisplacementToArmLengthRatio;
                WorksetRingCount = worksetRingCount;
                AllowsEarlyConvergence = allowsEarlyConvergence;
                AllowsWarmStart = allowsWarmStart;
                AllowsBurst = allowsBurst;
            }

            internal int LocalGlobalIterations { get; }
            internal int ConjugateGradientIterations { get; }
            internal int EdgeLengthProjectionIterations { get; }
            internal float MaximumEdgeLengthStrain { get; }
            internal float MaximumTotalDisplacementToArmLengthRatio { get; }
            internal int WorksetRingCount { get; }
            internal bool AllowsEarlyConvergence { get; }
            internal bool AllowsWarmStart { get; }
            internal bool AllowsBurst { get; }
        }

        private readonly struct FrameEdge
        {
            internal FrameEdge(
                int firstVertex,
                int secondVertex,
                float baselineLength,
                float restLength)
            {
                FirstVertex = firstVertex;
                SecondVertex = secondVertex;
                BaselineLength = baselineLength;
                RestLength = restLength;
            }

            internal int FirstVertex { get; }
            internal int SecondVertex { get; }
            internal float BaselineLength { get; }
            internal float RestLength { get; }
        }

        private readonly struct LocalEdge
        {
            internal LocalEdge(
                int firstVertex,
                int secondVertex,
                int firstLocalIndex,
                int secondLocalIndex,
                float baselineLength,
                float restLength)
            {
                FirstVertex = firstVertex;
                SecondVertex = secondVertex;
                FirstLocalIndex = firstLocalIndex;
                SecondLocalIndex = secondLocalIndex;
                BaselineLength = baselineLength;
                RestLength = restLength;
            }

            internal int FirstVertex { get; }
            internal int SecondVertex { get; }
            internal int FirstLocalIndex { get; }
            internal int SecondLocalIndex { get; }
            internal float BaselineLength { get; }
            internal float RestLength { get; }
        }

        private readonly struct LocalWorkset
        {
            internal LocalWorkset(
                int[] constrainedVertices,
                int[] localIndexByVertex,
                int[] facePairIndices,
                int[] faceIndices,
                LocalEdge[] edges)
            {
                ConstrainedVertices = constrainedVertices;
                LocalIndexByVertex = localIndexByVertex;
                FacePairIndices = facePairIndices;
                FaceIndices = faceIndices;
                Edges = edges;
            }

            internal int[] ConstrainedVertices { get; }
            internal int[] LocalIndexByVertex { get; }
            internal int[] FacePairIndices { get; }
            internal int[] FaceIndices { get; }
            internal LocalEdge[] Edges { get; }
        }

        // 쌍당 4정점 고정 — 배열 대신 인라인 필드로 반복 할당을 없앤다.
        private readonly struct BendingLinearization
        {
            internal BendingLinearization(
                int index0,
                int index1,
                int index2,
                int index3,
                Vector3 gradient0,
                Vector3 gradient1,
                Vector3 gradient2,
                Vector3 gradient3)
            {
                Index0 = index0;
                Index1 = index1;
                Index2 = index2;
                Index3 = index3;
                Gradient0 = gradient0;
                Gradient1 = gradient1;
                Gradient2 = gradient2;
                Gradient3 = gradient3;
            }

            internal int Index0 { get; }
            internal int Index1 { get; }
            internal int Index2 { get; }
            internal int Index3 { get; }
            internal Vector3 Gradient0 { get; }
            internal Vector3 Gradient1 { get; }
            internal Vector3 Gradient2 { get; }
            internal Vector3 Gradient3 { get; }

            internal int LocalIndexAt(int index)
            {
                switch (index)
                {
                    case 0: return Index0;
                    case 1: return Index1;
                    case 2: return Index2;
                    default: return Index3;
                }
            }

            internal Vector3 GradientAt(int index)
            {
                switch (index)
                {
                    case 0: return Gradient0;
                    case 1: return Gradient1;
                    case 2: return Gradient2;
                    default: return Gradient3;
                }
            }
        }

        private readonly struct EdgeStrainLinearization
        {
            internal EdgeStrainLinearization(
                int firstLocalIndex,
                int secondLocalIndex,
                Vector3 firstGradient,
                Vector3 secondGradient)
            {
                FirstLocalIndex = firstLocalIndex;
                SecondLocalIndex = secondLocalIndex;
                FirstGradient = firstGradient;
                SecondGradient = secondGradient;
            }

            internal int FirstLocalIndex { get; }
            internal int SecondLocalIndex { get; }
            internal Vector3 FirstGradient { get; }
            internal Vector3 SecondGradient { get; }
        }

        // TryCalculate 호출 1회분의 재사용 스크래치 — 워커 스레드 한정(공유 없음)이라
        // 별도 동기화 없이 반복 할당만 제거한다.
        private sealed class SolverBuffers
        {
            internal float[] Diagonal = Array.Empty<float>();
            internal float[] Preconditioner = Array.Empty<float>();
            internal Vector3[] RightHandSide = Array.Empty<Vector3>();
            internal Vector3[] Solution = Array.Empty<Vector3>();
            internal Vector3[] Residual = Array.Empty<Vector3>();
            internal Vector3[] Direction = Array.Empty<Vector3>();
            internal Vector3[] PreconditionedResidual = Array.Empty<Vector3>();
            internal Vector3[] MatrixDirection = Array.Empty<Vector3>();
            internal Vector3[] ProposedLocal = Array.Empty<Vector3>();
            // 수락된 후보가 다음 반복의 current로 이어지므로 핑퐁 2개가 필요하다.
            internal Vector3[] CandidateA = Array.Empty<Vector3>();
            internal Vector3[] CandidateB = Array.Empty<Vector3>();
            internal readonly List<BendingLinearization> BendingTerms =
                new List<BendingLinearization>();
            internal readonly List<EdgeStrainLinearization> EdgeStrainTerms =
                new List<EdgeStrainLinearization>();
            // 워크셋 구축용 스탬프 마크 — 매번 클리어 대신 스탬프를 올려 재사용한다.
            internal int[] LocalIndexByVertex = Array.Empty<int>();
            internal int[] FacePairMark = Array.Empty<int>();
            internal int[] FaceMark = Array.Empty<int>();
            private int _markStamp;

            internal void EnsureLocalSize(int count)
            {
                Ensure(ref Diagonal, count);
                Ensure(ref Preconditioner, count);
                Ensure(ref RightHandSide, count);
                Ensure(ref Solution, count);
                Ensure(ref Residual, count);
                Ensure(ref Direction, count);
                Ensure(ref PreconditionedResidual, count);
                Ensure(ref MatrixDirection, count);
                Ensure(ref ProposedLocal, count);
            }

            internal void EnsureVertexSize(int count)
            {
                Ensure(ref CandidateA, count);
                Ensure(ref CandidateB, count);
            }

            internal void EnsureWorksetSize(
                int vertexCount,
                int facePairCount,
                int faceCount)
            {
                Ensure(ref LocalIndexByVertex, vertexCount);
                Ensure(ref FacePairMark, facePairCount);
                Ensure(ref FaceMark, faceCount);
            }

            internal int NextMarkStamp() => ++_markStamp;

            internal Vector3[] CandidateFor(IReadOnlyList<Vector3> current) =>
                ReferenceEquals(current, CandidateA) ? CandidateB : CandidateA;

            private static void Ensure(ref int[] array, int count)
            {
                if (array.Length < count)
                {
                    array = new int[count];
                }
            }

            private static void Ensure(ref float[] array, int count)
            {
                if (array.Length < count)
                {
                    array = new float[count];
                }
            }

            private static void Ensure(ref Vector3[] array, int count)
            {
                if (array.Length < count)
                {
                    array = new Vector3[count];
                }
            }
        }
    }
}
