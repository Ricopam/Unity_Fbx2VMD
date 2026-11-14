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
            Vector3[] rightHandSide = buffers.RightHandSide;
            List<BendingLinearization> bendingTerms = buffers.BendingTerms;
            List<EdgeStrainLinearization> edgeStrainTerms = buffers.EdgeStrainTerms;
            InitializeAttachmentTerms(
                baselineVertices, vertices, workset, shouldRecoverRestShape,
                count, diagonal, rightHandSide, bendingTerms, edgeStrainTerms);

            activeConstraintCount = AccumulateBendingTerms(
                baselineVertices, vertices, contract, workset, activePairIndices,
                rightHandSide, bendingTerms);
            materialStrainConstraints = shouldRecoverRestShape
                ? NativeSkinningRestShapeRecoveryCalculator.BuildConstraints(
                    baselineVertices,
                    contract.RestVertices,
                    vertices,
                    contract.Triangles,
                    workset.FaceIndices,
                    workset.LocalIndexByVertex)
                : Array.Empty<NativeSkinningMaterialStrainConstraint>();
            activeConstraintCount += AccumulateMaterialStrainTerms(
                materialStrainConstraints, diagonal, rightHandSide);

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
            BuildPreconditioner(
                buffers, count, diagonal, bendingTerms, edgeStrainTerms,
                materialStrainConstraints);
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

                activeConstraintCount += TryAddBendingTerm(
                    vertices, pair, targetAngleDegrees, angle,
                    correction0, correction1, correction2, correction3,
                    workset.LocalIndexByVertex, rightHandSide, bendingTerms);
            }
            return activeConstraintCount;
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
            float violationRadians =
                (angle - targetAngleDegrees) * Mathf.Deg2Rad;
            float correctionSquaredSum =
                correction0.sqrMagnitude +
                correction1.sqrMagnitude +
                correction2.sqrMagnitude +
                correction3.sqrMagnitude;
            if (correctionSquaredSum <= MinimumNormalSquaredMagnitude)
            {
                return 0;
            }
            float gradientSquaredSum =
                violationRadians * violationRadians / correctionSquaredSum;
            float gradientScale = -gradientSquaredSum / violationRadians;
            AccumulateBendingRhs(
                pair, correction0, correction1, correction2, correction3,
                gradientSquaredSum, localIndexByVertex, rightHandSide);
            bendingTerms.Add(new BendingLinearization(
                localIndexByVertex[pair.FirstOpposite],
                localIndexByVertex[pair.SecondOpposite],
                localIndexByVertex[pair.FirstEdge],
                localIndexByVertex[pair.SecondEdge],
                correction0 * gradientScale,
                correction1 * gradientScale,
                correction2 * gradientScale,
                correction3 * gradientScale));
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
            AddBendingRightHandSide(
                pair.FirstOpposite,
                correction0,
                gradientSquaredSum,
                localIndexByVertex,
                rightHandSide);
            AddBendingRightHandSide(
                pair.SecondOpposite,
                correction1,
                gradientSquaredSum,
                localIndexByVertex,
                rightHandSide);
            AddBendingRightHandSide(
                pair.FirstEdge,
                correction2,
                gradientSquaredSum,
                localIndexByVertex,
                rightHandSide);
            AddBendingRightHandSide(
                pair.SecondEdge,
                correction3,
                gradientSquaredSum,
                localIndexByVertex,
                rightHandSide);
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
                activeConstraintCount += TryAddFaceBarrier(
                    vertices, firstIndex, secondIndex, thirdIndex,
                    baselineNormal, baselineArea,
                    localIndexByVertex, diagonal, rightHandSide);
            }
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
            float signedArea = Vector3.Dot(
                Vector3.Cross(second - first, third - first),
                baselineNormal);
            float activationArea = baselineArea * OrientationActivationRatio;
            if (signedArea >= activationArea)
            {
                return 0;
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
            return 1;
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
            InitializeResidual(
                warmStart, count, solution, rightHandSide, residual,
                diagonal, edges, bendingLinearizations, edgeStrainLinearizations,
                materialStrainConstraints, matrixDirection);
            ApplyDiagonalPreconditionerInto(
                residual,
                preconditionerDiagonal,
                direction,
                count);
            RunConjugateGradientIterations(
                count, maximumIterations, timing,
                solution, residual, direction, preconditionedResidual, matrixDirection,
                diagonal, preconditionerDiagonal, edges,
                bendingLinearizations, edgeStrainLinearizations, materialStrainConstraints);
            return solution;
        }

        private static void InitializeResidual(
            bool warmStart,
            int count,
            Vector3[] solution,
            Vector3[] rightHandSide,
            Vector3[] residual,
            float[] diagonal,
            IReadOnlyList<LocalEdge> edges,
            List<BendingLinearization> bendingLinearizations,
            List<EdgeStrainLinearization> edgeStrainLinearizations,
            IReadOnlyList<NativeSkinningMaterialStrainConstraint> materialStrainConstraints,
            Vector3[] matrixDirection)
        {
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
        }

        private static void RunConjugateGradientIterations(
            int count,
            int maximumIterations,
            NativeSkinningSolverTiming timing,
            Vector3[] solution,
            Vector3[] residual,
            Vector3[] direction,
            Vector3[] preconditionedResidual,
            Vector3[] matrixDirection,
            float[] diagonal,
            float[] preconditionerDiagonal,
            IReadOnlyList<LocalEdge> edges,
            List<BendingLinearization> bendingLinearizations,
            List<EdgeStrainLinearization> edgeStrainLinearizations,
            IReadOnlyList<NativeSkinningMaterialStrainConstraint> materialStrainConstraints)
        {
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
                residualDot = UpdateSearchDirection(
                    count, residualDot, residual, direction,
                    preconditionedResidual, preconditionerDiagonal);
            }
        }

        private static double UpdateSearchDirection(
            int count,
            double residualDot,
            Vector3[] residual,
            Vector3[] direction,
            Vector3[] preconditionedResidual,
            float[] preconditionerDiagonal)
        {
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
            return nextResidualDot;
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

            var native = new CgNativeBuffers();
            try
            {
                native.Allocate(
                    count, edgeCount, bendCount, strainCount,
                    materialCount, materialTermTotal);
                native.Pack(
                    buffers, edges, materialStrainConstraints,
                    warmStart, count, edgeCount, bendCount,
                    strainCount, materialCount);
                RunBurstJob(native, buffers, count, maximumIterations, warmStart, timing);
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
                native.Dispose();
            }
            return buffers.Solution;
        }

        private static void RunBurstJob(
            in CgNativeBuffers native,
            SolverBuffers buffers,
            int count,
            int maximumIterations,
            bool warmStart,
            NativeSkinningSolverTiming timing)
        {
            new CgSolveJob
            {
                Count = count,
                MaximumIterations = maximumIterations,
                WarmStart = warmStart ? (byte)1 : (byte)0,
                Diagonal = native.Diagonal,
                Preconditioner = native.Preconditioner,
                RightHandSide = native.RightHandSide,
                Solution = native.Solution,
                IterationsRun = native.IterationsRun,
                EdgeIndices = native.EdgeIndices,
                BendIndices = native.BendIndices,
                BendGradients = native.BendGradients,
                StrainIndices = native.StrainIndices,
                StrainFirstGradients = native.StrainFirstGradients,
                StrainSecondGradients = native.StrainSecondGradients,
                MaterialOffsets = native.MaterialOffsets,
                MaterialIndices = native.MaterialIndices,
                MaterialCoefficients = native.MaterialCoefficients,
                MaterialWeights = native.MaterialWeights,
            }.Schedule().Complete();
            timing?.AddCgIterations(native.IterationsRun[0]);
            NativeArray<Vector3>.Copy(
                native.Solution, 0, buffers.Solution, 0, count);
        }

        // Burst CG 한 호출에 필요한 네이티브 버퍼 묶음. 포장·해제 책임을 한 곳에 둔다.
        private struct CgNativeBuffers
        {
            internal NativeArray<float> Diagonal;
            internal NativeArray<float> Preconditioner;
            internal NativeArray<Vector3> RightHandSide;
            internal NativeArray<Vector3> Solution;
            internal NativeArray<int> IterationsRun;
            internal NativeArray<int2> EdgeIndices;
            internal NativeArray<int> BendIndices;
            internal NativeArray<Vector3> BendGradients;
            internal NativeArray<int2> StrainIndices;
            internal NativeArray<Vector3> StrainFirstGradients;
            internal NativeArray<Vector3> StrainSecondGradients;
            internal NativeArray<int> MaterialOffsets;
            internal NativeArray<int> MaterialIndices;
            internal NativeArray<float> MaterialCoefficients;
            internal NativeArray<float> MaterialWeights;

            internal void Allocate(
                int count, int edgeCount, int bendCount, int strainCount,
                int materialCount, int materialTermTotal)
            {
                Diagonal = new NativeArray<float>(count, Allocator.TempJob);
                Preconditioner = new NativeArray<float>(count, Allocator.TempJob);
                RightHandSide = new NativeArray<Vector3>(count, Allocator.TempJob);
                Solution = new NativeArray<Vector3>(count, Allocator.TempJob);
                IterationsRun = new NativeArray<int>(1, Allocator.TempJob);
                EdgeIndices = new NativeArray<int2>(edgeCount, Allocator.TempJob);
                BendIndices = new NativeArray<int>(bendCount * 4, Allocator.TempJob);
                BendGradients = new NativeArray<Vector3>(bendCount * 4, Allocator.TempJob);
                StrainIndices = new NativeArray<int2>(strainCount, Allocator.TempJob);
                StrainFirstGradients = new NativeArray<Vector3>(strainCount, Allocator.TempJob);
                StrainSecondGradients = new NativeArray<Vector3>(strainCount, Allocator.TempJob);
                MaterialOffsets = new NativeArray<int>(materialCount + 1, Allocator.TempJob);
                MaterialIndices = new NativeArray<int>(materialTermTotal, Allocator.TempJob);
                MaterialCoefficients = new NativeArray<float>(materialTermTotal, Allocator.TempJob);
                MaterialWeights = new NativeArray<float>(materialCount, Allocator.TempJob);
            }

            internal void Pack(
                SolverBuffers buffers,
                IReadOnlyList<LocalEdge> edges,
                IReadOnlyList<NativeSkinningMaterialStrainConstraint> materialStrainConstraints,
                bool warmStart,
                int count, int edgeCount, int bendCount,
                int strainCount, int materialCount)
            {
                NativeArray<float>.Copy(buffers.Diagonal, 0, Diagonal, 0, count);
                NativeArray<float>.Copy(buffers.Preconditioner, 0, Preconditioner, 0, count);
                NativeArray<Vector3>.Copy(buffers.RightHandSide, 0, RightHandSide, 0, count);
                if (warmStart)
                {
                    NativeArray<Vector3>.Copy(buffers.Solution, 0, Solution, 0, count);
                }
                for (int index = 0; index < edgeCount; index++)
                {
                    LocalEdge edge = edges[index];
                    EdgeIndices[index] = new int2(edge.FirstLocalIndex, edge.SecondLocalIndex);
                }
                for (int index = 0; index < bendCount; index++)
                {
                    BendingLinearization term = buffers.BendingTerms[index];
                    BendIndices[index * 4] = term.Index0;
                    BendIndices[index * 4 + 1] = term.Index1;
                    BendIndices[index * 4 + 2] = term.Index2;
                    BendIndices[index * 4 + 3] = term.Index3;
                    BendGradients[index * 4] = term.Gradient0;
                    BendGradients[index * 4 + 1] = term.Gradient1;
                    BendGradients[index * 4 + 2] = term.Gradient2;
                    BendGradients[index * 4 + 3] = term.Gradient3;
                }
                for (int index = 0; index < strainCount; index++)
                {
                    EdgeStrainLinearization term = buffers.EdgeStrainTerms[index];
                    StrainIndices[index] = new int2(term.FirstLocalIndex, term.SecondLocalIndex);
                    StrainFirstGradients[index] = term.FirstGradient;
                    StrainSecondGradients[index] = term.SecondGradient;
                }
                int materialOffset = 0;
                for (int index = 0; index < materialCount; index++)
                {
                    NativeSkinningMaterialStrainConstraint constraint = materialStrainConstraints[index];
                    MaterialOffsets[index] = materialOffset;
                    MaterialWeights[index] = constraint.Weight;
                    for (int term = 0; term < constraint.LocalIndices.Length; term++)
                    {
                        MaterialIndices[materialOffset + term] = constraint.LocalIndices[term];
                        MaterialCoefficients[materialOffset + term] = constraint.Coefficients[term];
                    }
                    materialOffset += constraint.LocalIndices.Length;
                }
                MaterialOffsets[materialCount] = materialOffset;
            }

            internal void Dispose()
            {
                if (Diagonal.IsCreated) Diagonal.Dispose();
                if (Preconditioner.IsCreated) Preconditioner.Dispose();
                if (RightHandSide.IsCreated) RightHandSide.Dispose();
                if (Solution.IsCreated) Solution.Dispose();
                if (IterationsRun.IsCreated) IterationsRun.Dispose();
                if (EdgeIndices.IsCreated) EdgeIndices.Dispose();
                if (BendIndices.IsCreated) BendIndices.Dispose();
                if (BendGradients.IsCreated) BendGradients.Dispose();
                if (StrainIndices.IsCreated) StrainIndices.Dispose();
                if (StrainFirstGradients.IsCreated) StrainFirstGradients.Dispose();
                if (StrainSecondGradients.IsCreated) StrainSecondGradients.Dispose();
                if (MaterialOffsets.IsCreated) MaterialOffsets.Dispose();
                if (MaterialIndices.IsCreated) MaterialIndices.Dispose();
                if (MaterialCoefficients.IsCreated) MaterialCoefficients.Dispose();
                if (MaterialWeights.IsCreated) MaterialWeights.Dispose();
            }
        }
    }
}
