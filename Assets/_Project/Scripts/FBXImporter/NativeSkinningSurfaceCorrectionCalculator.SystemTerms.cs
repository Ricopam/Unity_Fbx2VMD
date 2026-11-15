using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 변위 시스템 구축·에너지 항·CG 솔버 — 본체에서 이관한 partial.
    /// </summary>
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
        private static void BuildPreconditioner(
            SolverBuffers buffers,
            int count,
            float[] diagonal,
            List<BendingLinearization> bendingTerms,
            List<EdgeStrainLinearization> edgeStrainTerms,
            NativeSkinningMaterialStrainConstraint[] materialStrainConstraints)
        {
            float[] preconditionerDiagonal = buffers.Preconditioner;
            Array.Copy(diagonal, preconditionerDiagonal, count);
            AccumulateBendingPreconditioner(
                bendingTerms, preconditionerDiagonal);
            AccumulateEdgeStrainPreconditioner(
                edgeStrainTerms, preconditionerDiagonal);
            AccumulateMaterialStrainPreconditioner(
                materialStrainConstraints, preconditionerDiagonal);
        }

        private static void AccumulateBendingPreconditioner(
            List<BendingLinearization> bendingTerms,
            float[] preconditionerDiagonal)
        {
            foreach (BendingLinearization term in bendingTerms)
            {
                for (int index = 0; index < 4; index++)
                {
                    int localIndex = term.LocalIndexAt(index);
                    if (localIndex >= 0)
                    {
                        preconditionerDiagonal[localIndex] +=
                            BendingWeight *
                            term.GradientAt(index).sqrMagnitude;
                    }
                }
            }
        }

        private static void AccumulateEdgeStrainPreconditioner(
            List<EdgeStrainLinearization> edgeStrainTerms,
            float[] preconditionerDiagonal)
        {
            foreach (EdgeStrainLinearization term in edgeStrainTerms)
            {
                if (term.FirstLocalIndex >= 0)
                {
                    preconditionerDiagonal[term.FirstLocalIndex] +=
                        EdgeStrainBarrierWeight *
                        term.FirstGradient.sqrMagnitude;
                }
                if (term.SecondLocalIndex >= 0)
                {
                    preconditionerDiagonal[term.SecondLocalIndex] +=
                        EdgeStrainBarrierWeight *
                        term.SecondGradient.sqrMagnitude;
                }
            }
        }

        private static void AccumulateMaterialStrainPreconditioner(
            NativeSkinningMaterialStrainConstraint[] constraints,
            float[] preconditionerDiagonal)
        {
            foreach (NativeSkinningMaterialStrainConstraint constraint in
                     constraints)
            {
                for (int index = 0;
                     index < constraint.LocalIndices.Length;
                     index++)
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
            float minimumNormalMagnitude =
                Mathf.Sqrt(MinimumNormalSquaredMagnitude);
            foreach (int faceIndex in faceIndices)
            {
                activeConstraintCount += TryAddOrientationFaceBarrier(
                    baselineVertices, vertices, contract, faceIndex,
                    minimumNormalMagnitude, localIndexByVertex,
                    diagonal, rightHandSide);
            }
        }

        private static int TryAddOrientationFaceBarrier(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> vertices,
            NativeSkinningSurfaceContract contract,
            int faceIndex,
            float minimumNormalMagnitude,
            IReadOnlyList<int> localIndexByVertex,
            IList<float> diagonal,
            IList<Vector3> rightHandSide)
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
                return 0;
            }

            Vector3 baselineNormal = baselineCross / baselineArea;
            return TryAddFaceBarrier(
                vertices, firstIndex, secondIndex, thirdIndex,
                baselineNormal, baselineArea,
                localIndexByVertex, diagonal, rightHandSide);
        }

        private static int TryAddFaceBarrier(
            IReadOnlyList<Vector3> vertices,
            int firstIndex,
            int secondIndex,
            int thirdIndex,
            Vector3 baselineNormal,
            float baselineArea,
            IReadOnlyList<int> localIndexByVertex,
            IList<float> diagonal,
            IList<Vector3> rightHandSide)
        {
            Vector3 first = vertices[firstIndex];
            Vector3 second = vertices[secondIndex];
            Vector3 third = vertices[thirdIndex];
            if (!TryMeasureOrientationViolation(
                    first, second, third, baselineNormal, baselineArea,
                    out float activationArea,
                    out float normalizedViolation))
            {
                return 0;
            }

            AccumulateFaceBarrier(
                first, second, third,
                firstIndex, secondIndex, thirdIndex,
                baselineNormal, activationArea, normalizedViolation,
                localIndexByVertex, diagonal, rightHandSide);
            return 1;
        }

        private static void AccumulateFaceBarrier(
            Vector3 first,
            Vector3 second,
            Vector3 third,
            int firstIndex,
            int secondIndex,
            int thirdIndex,
            Vector3 baselineNormal,
            float activationArea,
            float normalizedViolation,
            IReadOnlyList<int> localIndexByVertex,
            IList<float> diagonal,
            IList<Vector3> rightHandSide)
        {
            Vector3[] gradients =
            {
                Vector3.Cross(second - third, baselineNormal),
                Vector3.Cross(third - first, baselineNormal),
                Vector3.Cross(baselineNormal, second - first)
            };
            int[] faceVertices = { firstIndex, secondIndex, thirdIndex };
            for (int index = 0; index < faceVertices.Length; index++)
            {
                AccumulateVertexBarrier(
                    localIndexByVertex[faceVertices[index]],
                    gradients[index] / activationArea,
                    normalizedViolation, diagonal, rightHandSide);
            }
        }

        private static bool TryMeasureOrientationViolation(
            Vector3 first,
            Vector3 second,
            Vector3 third,
            Vector3 baselineNormal,
            float baselineArea,
            out float activationArea,
            out float normalizedViolation)
        {
            float signedArea = Vector3.Dot(
                Vector3.Cross(second - first, third - first),
                baselineNormal);
            activationArea = baselineArea * OrientationActivationRatio;
            normalizedViolation = 0f;
            if (signedArea >= activationArea)
            {
                return false;
            }
            normalizedViolation = Mathf.Max(
                0f,
                (activationArea - signedArea) / activationArea);
            return true;
        }

        private static void AccumulateVertexBarrier(
            int localIndex,
            Vector3 normalizedGradient,
            float normalizedViolation,
            IList<float> diagonal,
            IList<Vector3> rightHandSide)
        {
            if (localIndex < 0)
            {
                return;
            }
            diagonal[localIndex] +=
                OrientationBarrierWeight * normalizedGradient.sqrMagnitude;
            rightHandSide[localIndex] +=
                OrientationBarrierWeight *
                normalizedViolation * normalizedGradient;
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
                if (!TryMeasureEdgeStrain(
                        edge, vertices,
                        out Vector3 edgeVector, out float edgeLength,
                        out float signedStrain, out float excessMagnitude))
                {
                    continue;
                }

                AccumulateEdgeStrain(
                    edge, edgeVector, edgeLength, signedStrain,
                    excessMagnitude, rightHandSide, terms,
                    ref activeConstraintCount);
            }
        }

        private static void ApplyEdgeCorrection(
            LocalEdge edge,
            float signedExcess,
            Vector3 normalizedDirection,
            IList<Vector3> rightHandSide)
        {
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
        }

        private static bool TryMeasureEdgeStrain(
            LocalEdge edge,
            IReadOnlyList<Vector3> vertices,
            out Vector3 edgeVector,
            out float edgeLength,
            out float signedStrain,
            out float excessMagnitude)
        {
            edgeVector =
                vertices[edge.SecondVertex] - vertices[edge.FirstVertex];
            edgeLength = edgeVector.magnitude;
            signedStrain = 0f;
            excessMagnitude = 0f;
            if (edgeLength <= 0.00000001f ||
                edge.BaselineLength <= 0.00000001f)
            {
                return false;
            }
            signedStrain = edgeLength / edge.BaselineLength - 1f;
            excessMagnitude =
                Mathf.Abs(signedStrain) - EdgeStrainActivation;
            return excessMagnitude > 0f;
        }

        private static void AccumulateEdgeStrain(
            LocalEdge edge,
            Vector3 edgeVector,
            float edgeLength,
            float signedStrain,
            float excessMagnitude,
            IList<Vector3> rightHandSide,
            ICollection<EdgeStrainLinearization> terms,
            ref int activeConstraintCount)
        {
            float signedExcess = Mathf.Sign(signedStrain) * excessMagnitude;
            Vector3 normalizedDirection =
                edgeVector / (edgeLength * edge.BaselineLength);
            ApplyEdgeCorrection(
                edge, signedExcess, normalizedDirection, rightHandSide);
            terms.Add(new EdgeStrainLinearization(
                edge.FirstLocalIndex,
                edge.SecondLocalIndex,
                -normalizedDirection,
                normalizedDirection));
            activeConstraintCount++;
        }


    }
}

