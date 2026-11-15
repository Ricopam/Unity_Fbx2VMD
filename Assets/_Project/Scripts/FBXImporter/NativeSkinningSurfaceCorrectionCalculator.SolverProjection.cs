using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
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
            float minimumLength = edge.BaselineLength * (1f - maximumAbsoluteStrain);
            float maximumLength = edge.BaselineLength * (1f + maximumAbsoluteStrain);
            float targetLength = Mathf.Clamp( length, minimumLength, maximumLength);
            if (Mathf.Approximately(length, targetLength))
            {
                return false;
            }

            ApplySingleEdgeCorrection(edge, delta, length, targetLength, first, second,
                currentVertices, constrainedVertices, proposedLocalPositions);
            return true;
        }

        // 한 에지의 길이 보정을 이동 가능한 양단 정점에 분배하고 변위를 상한 내로 고정한다.
        static void ApplySingleEdgeCorrection(
            LocalEdge edge, Vector3 delta, float length, float targetLength,
            Vector3 first, Vector3 second,
            IReadOnlyList<Vector3> currentVertices, IReadOnlyList<int> constrainedVertices,
            IList<Vector3> proposedLocalPositions)
        {
            Vector3 correction = delta * ((length - targetLength) / length);
            bool movesFirst = edge.FirstLocalIndex >= 0;
            bool movesSecond = edge.SecondLocalIndex >= 0;
            float share = movesFirst && movesSecond ? 0.5f : 1f;
            if (movesFirst)
            {
                proposedLocalPositions[edge.FirstLocalIndex] = ClampDisplacementFromBaseline( currentVertices[ constrainedVertices[edge.FirstLocalIndex]], first + correction * share,
                        MaximumAbsoluteStep);
            }
            if (movesSecond)
            {
                proposedLocalPositions[edge.SecondLocalIndex] = ClampDisplacementFromBaseline( currentVertices[ constrainedVertices[edge.SecondLocalIndex]], second - correction * share,
                        MaximumAbsoluteStep);
            }
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
                if (TryProjectRestStrainEdge(edge, currentVertices, proposedLocalPositions))
                {
                    projectionCount++; } }
            return projectionCount;
        }

        // 한 에지의 현재·제안 길이 비율을 임계로 투영해 변형률 보정을 적용한다.
        static bool TryProjectRestStrainEdge(
            LocalEdge edge, IReadOnlyList<Vector3> currentVertices,
            IList<Vector3> proposedLocalPositions)
        {
            bool canMoveFirst = edge.FirstLocalIndex >= 0;
            bool canMoveSecond = edge.SecondLocalIndex >= 0;
            if (!canMoveFirst && !canMoveSecond)
            {
                return false; }
            Vector3 currentEdge = currentVertices[edge.SecondVertex] - currentVertices[edge.FirstVertex];
            Vector3 proposedFirst = canMoveFirst
                ? proposedLocalPositions[edge.FirstLocalIndex] : currentVertices[edge.FirstVertex];
            Vector3 proposedSecond = canMoveSecond
                ? proposedLocalPositions[edge.SecondLocalIndex] : currentVertices[edge.SecondVertex];
            Vector3 proposedEdge = proposedSecond - proposedFirst;
            float currentLength = currentEdge.magnitude;
            float proposedLength = proposedEdge.magnitude;
            if (currentLength <= 0.00000001f || proposedLength <= 0.00000001f)
            {
                return false; }
            float baselineRatio = edge.BaselineLength / edge.RestLength;
            // 계산용 스키닝과 재생 정점의 반올림 차이로 경계를 넘지 않도록 출력 여유를 남김.
            float maximumAbsoluteStrain = Mathf.Abs(baselineRatio - 1f) + NativeSkinningRestShapeRecoveryCalculator .MaximumRestStrainIncrease - EdgeLengthProjectionMargin;
            float minimumRatio = Mathf.Max(0f, 1f - maximumAbsoluteStrain);
            float maximumRatio = 1f + maximumAbsoluteStrain;
            float currentRatio = currentLength / edge.RestLength;
            float proposedRatio = proposedLength / edge.RestLength;
            if (!TryResolveStrainTargetRatio( currentRatio, proposedRatio, minimumRatio, maximumRatio, out float targetRatio))
            {
                return false; }
            ApplyStrainCorrection( edge, proposedEdge, proposedLength, targetRatio, canMoveFirst, canMoveSecond, proposedLocalPositions);
            return true;
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
            else if (currentRatio <= minimumRatio + RestShapeActiveSetActivationMargin && proposedRatio < currentRatio)
            {
                targetRatio = currentRatio; } else
            {
                targetRatio = 0f;
                return false; }
            return true; }

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
            Vector3 opposite0 = ReadProposedPosition( pair.FirstOpposite, currentVertices, proposedLocalPositions, localIndexByVertex);
            Vector3 opposite1 = ReadProposedPosition( pair.SecondOpposite, currentVertices, proposedLocalPositions, localIndexByVertex);
            Vector3 edge0 = ReadProposedPosition( pair.FirstEdge, currentVertices, proposedLocalPositions, localIndexByVertex);
            Vector3 edge1 = ReadProposedPosition( pair.SecondEdge, currentVertices, proposedLocalPositions, localIndexByVertex);
            if (!MeshDihedralConstraintCalculator.TryCalculateCorrections( opposite0, opposite1, edge0, edge1, ResolveProjectionAngleDegrees( baselineVertices, contract, pairIndex), 1f,
                    out Vector3 correction0, out Vector3 correction1, out Vector3 correction2, out Vector3 correction3))
            {
                return 0;
            }
            float maximumCorrectionMagnitude = Mathf.Max( correction0.magnitude, correction1.magnitude, correction2.magnitude, correction3.magnitude);
            float correctionScale = maximumCorrectionMagnitude >
                                    RestShapeMaximumProjectionCorrection
                ? RestShapeMaximumProjectionCorrection /
                  maximumCorrectionMagnitude
                : 1f;
            ApplyPairCorrections( pair, correction0 * correctionScale, correction1 * correctionScale, correction2 * correctionScale, correction3 * correctionScale,
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
    }
}
