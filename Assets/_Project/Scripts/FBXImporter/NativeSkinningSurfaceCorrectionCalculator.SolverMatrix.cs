using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
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
    }
}
