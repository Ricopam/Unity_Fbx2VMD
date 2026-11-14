using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
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
    }
}
