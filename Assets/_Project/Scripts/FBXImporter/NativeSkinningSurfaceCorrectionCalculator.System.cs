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
            Array.Clear(diagonal, 0, count);
            Vector3[] rightHandSide = buffers.RightHandSide;
            Array.Clear(rightHandSide, 0, count);
            List<BendingLinearization> bendingTerms = buffers.BendingTerms;
            bendingTerms.Clear();
            List<EdgeStrainLinearization> edgeStrainTerms = buffers.EdgeStrainTerms;
            edgeStrainTerms.Clear();
            for (int localIndex = 0; localIndex < count; localIndex++)
            {
                diagonal[localIndex] = shouldRecoverRestShape
                    ? RestShapeRecoveryAttachmentWeight
                    : DisplacementAttachmentWeight;
                if (shouldRecoverRestShape)
                {
                    int vertexIndex = workset.ConstrainedVertices[localIndex];
                    rightHandSide[localIndex] =
                        (baselineVertices[vertexIndex] - vertices[vertexIndex]) *
                        RestShapeRecoveryAttachmentWeight;
                }
            }
            if (!shouldRecoverRestShape)
            {
                foreach (LocalEdge edge in workset.Edges)
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

            activeConstraintCount = 0;
            foreach (int pairIndex in workset.FacePairIndices)
            {
                if (!activePairIndices.Contains(pairIndex) ||
                    !IsSharpFoldCorrectionEligible(
                        baselineVertices,
                        contract,
                        pairIndex))
                {
                    continue;
                }
                NativeSkinningFacePair pair = contract.FacePairs[pairIndex];
                if (!NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                        vertices,
                        pair,
                        out float angle))
                {
                    continue;
                }
                float targetAngleDegrees = ResolveProjectionAngleDegrees(
                    baselineVertices,
                    contract,
                    pairIndex);
                if (angle <= targetAngleDegrees)
                {
                    continue;
                }
                if (!MeshDihedralConstraintCalculator.TryCalculateCorrections(
                        vertices[pair.FirstOpposite],
                        vertices[pair.SecondOpposite],
                        vertices[pair.FirstEdge],
                        vertices[pair.SecondEdge],
                        targetAngleDegrees,
                        1f,
                        out Vector3 correction0,
                        out Vector3 correction1,
                        out Vector3 correction2,
                        out Vector3 correction3))
                {
                    continue;
                }

                float violationRadians =
                    (angle - targetAngleDegrees) * Mathf.Deg2Rad;
                float correctionSquaredSum =
                    correction0.sqrMagnitude +
                    correction1.sqrMagnitude +
                    correction2.sqrMagnitude +
                    correction3.sqrMagnitude;
                if (correctionSquaredSum <= MinimumNormalSquaredMagnitude)
                {
                    continue;
                }
                float gradientSquaredSum =
                    violationRadians * violationRadians / correctionSquaredSum;
                float gradientScale = -gradientSquaredSum / violationRadians;
                AddBendingRightHandSide(
                    pair.FirstOpposite,
                    correction0,
                    gradientSquaredSum,
                    workset.LocalIndexByVertex,
                    rightHandSide);
                AddBendingRightHandSide(
                    pair.SecondOpposite,
                    correction1,
                    gradientSquaredSum,
                    workset.LocalIndexByVertex,
                    rightHandSide);
                AddBendingRightHandSide(
                    pair.FirstEdge,
                    correction2,
                    gradientSquaredSum,
                    workset.LocalIndexByVertex,
                    rightHandSide);
                AddBendingRightHandSide(
                    pair.SecondEdge,
                    correction3,
                    gradientSquaredSum,
                    workset.LocalIndexByVertex,
                    rightHandSide);
                bendingTerms.Add(new BendingLinearization(
                    workset.LocalIndexByVertex[pair.FirstOpposite],
                    workset.LocalIndexByVertex[pair.SecondOpposite],
                    workset.LocalIndexByVertex[pair.FirstEdge],
                    workset.LocalIndexByVertex[pair.SecondEdge],
                    correction0 * gradientScale,
                    correction1 * gradientScale,
                    correction2 * gradientScale,
                    correction3 * gradientScale));
                activeConstraintCount++;
            }

            materialStrainConstraints = shouldRecoverRestShape
                ? NativeSkinningRestShapeRecoveryCalculator.BuildConstraints(
                    baselineVertices,
                    contract.RestVertices,
                    vertices,
                    contract.Triangles,
                    workset.FaceIndices,
                    workset.LocalIndexByVertex)
                : Array.Empty<NativeSkinningMaterialStrainConstraint>();
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
            float[] preconditionerDiagonal = buffers.Preconditioner;
            Array.Copy(diagonal, preconditionerDiagonal, count);
            foreach (BendingLinearization term in bendingTerms)
            {
                for (int index = 0; index < 4; index++)
                {
                    int localIndex = term.LocalIndexAt(index);
                    if (localIndex >= 0)
                    {
                        preconditionerDiagonal[localIndex] +=
                            BendingWeight * term.GradientAt(index).sqrMagnitude;
                    }
                }
            }
            foreach (EdgeStrainLinearization term in edgeStrainTerms)
            {
                if (term.FirstLocalIndex >= 0)
                {
                    preconditionerDiagonal[term.FirstLocalIndex] +=
                        EdgeStrainBarrierWeight * term.FirstGradient.sqrMagnitude;
                }
                if (term.SecondLocalIndex >= 0)
                {
                    preconditionerDiagonal[term.SecondLocalIndex] +=
                        EdgeStrainBarrierWeight * term.SecondGradient.sqrMagnitude;
                }
            }
            foreach (NativeSkinningMaterialStrainConstraint constraint in
                     materialStrainConstraints)
            {
                for (int index = 0; index < constraint.LocalIndices.Length; index++)
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
                Vector3 first = vertices[firstIndex];
                Vector3 second = vertices[secondIndex];
                Vector3 third = vertices[thirdIndex];
                float signedArea = Vector3.Dot(
                    Vector3.Cross(second - first, third - first),
                    baselineNormal);
                float activationArea = baselineArea * OrientationActivationRatio;
                if (signedArea >= activationArea)
                {
                    continue;
                }

                float normalizedViolation = Mathf.Max(
                    0f,
                    (activationArea - signedArea) / activationArea);
                Vector3[] gradients =
                {
                    Vector3.Cross(second - third, baselineNormal),
                    Vector3.Cross(third - first, baselineNormal),
                    Vector3.Cross(baselineNormal, second - first)
                };
                int[] faceVertices = { firstIndex, secondIndex, thirdIndex };
                for (int index = 0; index < faceVertices.Length; index++)
                {
                    int localIndex = localIndexByVertex[faceVertices[index]];
                    if (localIndex < 0)
                    {
                        continue;
                    }
                    Vector3 normalizedGradient = gradients[index] / activationArea;
                    diagonal[localIndex] +=
                        OrientationBarrierWeight * normalizedGradient.sqrMagnitude;
                    rightHandSide[localIndex] +=
                        OrientationBarrierWeight *
                        normalizedViolation * normalizedGradient;
                }
                activeConstraintCount++;
            }
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
                Vector3 edgeVector =
                    vertices[edge.SecondVertex] - vertices[edge.FirstVertex];
                float edgeLength = edgeVector.magnitude;
                if (edgeLength <= 0.00000001f ||
                    edge.BaselineLength <= 0.00000001f)
                {
                    continue;
                }
                float signedStrain = edgeLength / edge.BaselineLength - 1f;
                float excessMagnitude =
                    Mathf.Abs(signedStrain) - EdgeStrainActivation;
                if (excessMagnitude <= 0f)
                {
                    continue;
                }

                float signedExcess = Mathf.Sign(signedStrain) * excessMagnitude;
                Vector3 normalizedDirection =
                    edgeVector / (edgeLength * edge.BaselineLength);
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
                terms.Add(new EdgeStrainLinearization(
                    edge.FirstLocalIndex,
                    edge.SecondLocalIndex,
                    -normalizedDirection,
                    normalizedDirection));
                activeConstraintCount++;
            }
        }

        private static Vector3[] SolveConjugateGradient(
            SolverBuffers buffers,
            IReadOnlyList<LocalEdge> edges,
            IReadOnlyList<NativeSkinningMaterialStrainConstraint>
                materialStrainConstraints,
            int count,
            int maximumIterations,
            bool warmStart,
            NativeSkinningSolverTiming timing)
        {
            timing?.AddCgCall();
            float[] diagonal = buffers.Diagonal;
            float[] preconditionerDiagonal = buffers.Preconditioner;
            Vector3[] rightHandSide = buffers.RightHandSide;
            Vector3[] solution = buffers.Solution;
            Vector3[] residual = buffers.Residual;
            Vector3[] direction = buffers.Direction;
            Vector3[] preconditionedResidual = buffers.PreconditionedResidual;
            Vector3[] matrixDirection = buffers.MatrixDirection;
            List<BendingLinearization> bendingLinearizations =
                buffers.BendingTerms;
            List<EdgeStrainLinearization> edgeStrainLinearizations =
                buffers.EdgeStrainTerms;
            if (warmStart)
            {
                // 직전 반복 해법을 x0로 — 잔차는 우변−A·x0를 실제 계산한다.
                ApplyMatrixInto(
                    solution,
                    diagonal,
                    edges,
                    bendingLinearizations,
                    edgeStrainLinearizations,
                    materialStrainConstraints,
                    matrixDirection,
                    count);
                for (int index = 0; index < count; index++)
                {
                    residual[index] =
                        rightHandSide[index] - matrixDirection[index];
                }
            }
            else
            {
                // x0=0이면 A·x0=0이라 잔차는 우변 그대로 — 첫 ApplyMatrix를 생략한다.
                Array.Clear(solution, 0, count);
                Array.Copy(rightHandSide, residual, count);
            }
            ApplyDiagonalPreconditionerInto(
                residual,
                preconditionerDiagonal,
                direction,
                count);
            double residualDot = Dot(residual, direction, count);
            for (int iteration = 0;
                 iteration < maximumIterations && residualDot > 1e-20d;
                 iteration++)
            {
                timing?.AddCgIteration();
                ApplyMatrixInto(
                    direction,
                    diagonal,
                    edges,
                    bendingLinearizations,
                    edgeStrainLinearizations,
                    materialStrainConstraints,
                    matrixDirection,
                    count);
                double denominator = Dot(direction, matrixDirection, count);
                if (Math.Abs(denominator) <= 1e-20d)
                {
                    break;
                }
                float step = (float)(residualDot / denominator);
                for (int index = 0; index < count; index++)
                {
                    solution[index] += direction[index] * step;
                    residual[index] -= matrixDirection[index] * step;
                }
                if (Dot(residual, residual, count) <= 1e-18d)
                {
                    break;
                }
                ApplyDiagonalPreconditionerInto(
                    residual,
                    preconditionerDiagonal,
                    preconditionedResidual,
                    count);
                double nextResidualDot = Dot(
                    residual,
                    preconditionedResidual,
                    count);
                float beta = (float)(nextResidualDot / residualDot);
                for (int index = 0; index < count; index++)
                {
                    direction[index] = preconditionedResidual[index] +
                        direction[index] * beta;
                }
                residualDot = nextResidualDot;
            }
            return solution;
        }

        // fast 전용 Burst 경로 — CG 한 호출을 하나의 Job으로 실행해
        // 반복마다 스케줄 비용을 치르지 않는다. 포장·해제는 호출당 1회.
        // 포장이나 실행이 실패하면 managed 경로로 되돌아가 결과 계약을 유지한다.
        private static Vector3[] SolveConjugateGradientBurst(
            SolverBuffers buffers,
            IReadOnlyList<LocalEdge> edges,
            IReadOnlyList<NativeSkinningMaterialStrainConstraint>
                materialStrainConstraints,
            int count,
            int maximumIterations,
            bool warmStart,
            NativeSkinningSolverTiming timing)
        {
            timing?.AddCgCall();
            int edgeCount = edges.Count;
            int bendCount = buffers.BendingTerms.Count;
            int strainCount = buffers.EdgeStrainTerms.Count;
            int materialCount = materialStrainConstraints.Count;
            int materialTermTotal = 0;
            for (int index = 0; index < materialCount; index++)
            {
                materialTermTotal +=
                    materialStrainConstraints[index].LocalIndices.Length;
            }

            NativeArray<float> diagonal = default;
            NativeArray<float> preconditioner = default;
            NativeArray<Vector3> rightHandSide = default;
            NativeArray<Vector3> solution = default;
            NativeArray<int> iterationsRun = default;
            NativeArray<int2> edgeIndices = default;
            NativeArray<int> bendIndices = default;
            NativeArray<Vector3> bendGradients = default;
            NativeArray<int2> strainIndices = default;
            NativeArray<Vector3> strainFirstGradients = default;
            NativeArray<Vector3> strainSecondGradients = default;
            NativeArray<int> materialOffsets = default;
            NativeArray<int> materialIndices = default;
            NativeArray<float> materialCoefficients = default;
            NativeArray<float> materialWeights = default;
            try
            {
                diagonal = new NativeArray<float>(count, Allocator.TempJob);
                preconditioner =
                    new NativeArray<float>(count, Allocator.TempJob);
                rightHandSide =
                    new NativeArray<Vector3>(count, Allocator.TempJob);
                solution = new NativeArray<Vector3>(count, Allocator.TempJob);
                iterationsRun = new NativeArray<int>(1, Allocator.TempJob);
                edgeIndices =
                    new NativeArray<int2>(edgeCount, Allocator.TempJob);
                bendIndices =
                    new NativeArray<int>(bendCount * 4, Allocator.TempJob);
                bendGradients = new NativeArray<Vector3>(
                    bendCount * 4,
                    Allocator.TempJob);
                strainIndices =
                    new NativeArray<int2>(strainCount, Allocator.TempJob);
                strainFirstGradients = new NativeArray<Vector3>(
                    strainCount,
                    Allocator.TempJob);
                strainSecondGradients = new NativeArray<Vector3>(
                    strainCount,
                    Allocator.TempJob);
                materialOffsets =
                    new NativeArray<int>(materialCount + 1, Allocator.TempJob);
                materialIndices =
                    new NativeArray<int>(materialTermTotal, Allocator.TempJob);
                materialCoefficients =
                    new NativeArray<float>(materialTermTotal, Allocator.TempJob);
                materialWeights =
                    new NativeArray<float>(materialCount, Allocator.TempJob);

                NativeArray<float>.Copy(
                    buffers.Diagonal, 0, diagonal, 0, count);
                NativeArray<float>.Copy(
                    buffers.Preconditioner, 0, preconditioner, 0, count);
                NativeArray<Vector3>.Copy(
                    buffers.RightHandSide, 0, rightHandSide, 0, count);
                if (warmStart)
                {
                    NativeArray<Vector3>.Copy(
                        buffers.Solution, 0, solution, 0, count);
                }
                for (int index = 0; index < edgeCount; index++)
                {
                    LocalEdge edge = edges[index];
                    edgeIndices[index] = new int2(
                        edge.FirstLocalIndex,
                        edge.SecondLocalIndex);
                }
                for (int index = 0; index < bendCount; index++)
                {
                    BendingLinearization term = buffers.BendingTerms[index];
                    bendIndices[index * 4] = term.Index0;
                    bendIndices[index * 4 + 1] = term.Index1;
                    bendIndices[index * 4 + 2] = term.Index2;
                    bendIndices[index * 4 + 3] = term.Index3;
                    bendGradients[index * 4] = term.Gradient0;
                    bendGradients[index * 4 + 1] = term.Gradient1;
                    bendGradients[index * 4 + 2] = term.Gradient2;
                    bendGradients[index * 4 + 3] = term.Gradient3;
                }
                for (int index = 0; index < strainCount; index++)
                {
                    EdgeStrainLinearization term =
                        buffers.EdgeStrainTerms[index];
                    strainIndices[index] = new int2(
                        term.FirstLocalIndex,
                        term.SecondLocalIndex);
                    strainFirstGradients[index] = term.FirstGradient;
                    strainSecondGradients[index] = term.SecondGradient;
                }
                int materialOffset = 0;
                for (int index = 0; index < materialCount; index++)
                {
                    NativeSkinningMaterialStrainConstraint constraint =
                        materialStrainConstraints[index];
                    materialOffsets[index] = materialOffset;
                    materialWeights[index] = constraint.Weight;
                    for (int term = 0;
                         term < constraint.LocalIndices.Length;
                         term++)
                    {
                        materialIndices[materialOffset + term] =
                            constraint.LocalIndices[term];
                        materialCoefficients[materialOffset + term] =
                            constraint.Coefficients[term];
                    }
                    materialOffset += constraint.LocalIndices.Length;
                }
                materialOffsets[materialCount] = materialOffset;

                new CgSolveJob
                {
                    Count = count,
                    MaximumIterations = maximumIterations,
                    WarmStart = warmStart ? (byte)1 : (byte)0,
                    Diagonal = diagonal,
                    Preconditioner = preconditioner,
                    RightHandSide = rightHandSide,
                    Solution = solution,
                    IterationsRun = iterationsRun,
                    EdgeIndices = edgeIndices,
                    BendIndices = bendIndices,
                    BendGradients = bendGradients,
                    StrainIndices = strainIndices,
                    StrainFirstGradients = strainFirstGradients,
                    StrainSecondGradients = strainSecondGradients,
                    MaterialOffsets = materialOffsets,
                    MaterialIndices = materialIndices,
                    MaterialCoefficients = materialCoefficients,
                    MaterialWeights = materialWeights,
                }.Schedule().Complete();
                timing?.AddCgIterations(iterationsRun[0]);
                NativeArray<Vector3>.Copy(
                    solution, 0, buffers.Solution, 0, count);
            }
            catch (Exception)
            {
                // 네이티브 경로 실패 시 managed 솔버로 동일 입력을 다시 푼다.
                return SolveConjugateGradient(
                    buffers,
                    edges,
                    materialStrainConstraints,
                    count,
                    maximumIterations,
                    warmStart,
                    timing);
            }
            finally
            {
                if (diagonal.IsCreated) diagonal.Dispose();
                if (preconditioner.IsCreated) preconditioner.Dispose();
                if (rightHandSide.IsCreated) rightHandSide.Dispose();
                if (solution.IsCreated) solution.Dispose();
                if (iterationsRun.IsCreated) iterationsRun.Dispose();
                if (edgeIndices.IsCreated) edgeIndices.Dispose();
                if (bendIndices.IsCreated) bendIndices.Dispose();
                if (bendGradients.IsCreated) bendGradients.Dispose();
                if (strainIndices.IsCreated) strainIndices.Dispose();
                if (strainFirstGradients.IsCreated)
                {
                    strainFirstGradients.Dispose();
                }
                if (strainSecondGradients.IsCreated)
                {
                    strainSecondGradients.Dispose();
                }
                if (materialOffsets.IsCreated) materialOffsets.Dispose();
                if (materialIndices.IsCreated) materialIndices.Dispose();
                if (materialCoefficients.IsCreated)
                {
                    materialCoefficients.Dispose();
                }
                if (materialWeights.IsCreated) materialWeights.Dispose();
            }
            return buffers.Solution;
        }
    }
}
