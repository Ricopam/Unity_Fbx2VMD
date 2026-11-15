using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 변위 시스템 구축·에너지 항·CG 솔버 — 본체에서 이관한 partial.
    /// </summary>
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
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
            RunManagedConjugateGradient(
                buffers, edges, materialStrainConstraints,
                count, maximumIterations, warmStart, timing);
            return buffers.Solution;
        }

        private static void RunManagedConjugateGradient(
            SolverBuffers buffers,
            IReadOnlyList<LocalEdge> edges,
            IReadOnlyList<NativeSkinningMaterialStrainConstraint>
                materialStrainConstraints,
            int count,
            int maximumIterations,
            bool warmStart,
            NativeSkinningSolverTiming timing)
        {
            InitializeResidual(
                warmStart, count, buffers.Solution, buffers.RightHandSide,
                buffers.Residual, buffers.Diagonal, edges,
                buffers.BendingTerms, buffers.EdgeStrainTerms,
                materialStrainConstraints, buffers.MatrixDirection);
            ApplyDiagonalPreconditionerInto(
                buffers.Residual,
                buffers.Preconditioner,
                buffers.Direction,
                count);
            RunConjugateGradientIterations(
                count, maximumIterations, timing,
                buffers.Solution, buffers.Residual, buffers.Direction,
                buffers.PreconditionedResidual, buffers.MatrixDirection,
                buffers.Diagonal, buffers.Preconditioner, edges,
                buffers.BendingTerms, buffers.EdgeStrainTerms,
                materialStrainConstraints);
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
                ComputeWarmStartResidual(
                    solution, rightHandSide, residual, diagonal, edges,
                    bendingLinearizations, edgeStrainLinearizations,
                    materialStrainConstraints, matrixDirection, count);
            }
            else
            {
                // x0=0이면 A·x0=0이라 잔차는 우변 그대로 — 첫 ApplyMatrix를 생략한다.
                ResetColdStart(solution, rightHandSide, residual, count);
            }
        }

        private static void ComputeWarmStartResidual(
            Vector3[] solution,
            Vector3[] rightHandSide,
            Vector3[] residual,
            float[] diagonal,
            IReadOnlyList<LocalEdge> edges,
            List<BendingLinearization> bendingLinearizations,
            List<EdgeStrainLinearization> edgeStrainLinearizations,
            IReadOnlyList<NativeSkinningMaterialStrainConstraint>
                materialStrainConstraints,
            Vector3[] matrixDirection,
            int count)
        {
            ApplyMatrixInto(
                solution,
                diagonal,
                edges,
                bendingLinearizations,
                edgeStrainLinearizations,
                materialStrainConstraints,
                matrixDirection,
                count);
            SubtractMatrixProduct(
                rightHandSide, matrixDirection, residual, count);
        }

        private static void ResetColdStart(
            Vector3[] solution,
            Vector3[] rightHandSide,
            Vector3[] residual,
            int count)
        {
            Array.Clear(solution, 0, count);
            Array.Copy(rightHandSide, residual, count);
        }

        private static void SubtractMatrixProduct(
            Vector3[] rightHandSide,
            Vector3[] matrixProduct,
            Vector3[] residual,
            int count)
        {
            for (int index = 0; index < count; index++)
            {
                residual[index] = rightHandSide[index] - matrixProduct[index];
            }
        }

        private static void AdvanceAlongDirection(
            Vector3[] solution,
            Vector3[] residual,
            Vector3[] direction,
            Vector3[] matrixDirection,
            float step,
            int count)
        {
            for (int index = 0; index < count; index++)
            {
                solution[index] += direction[index] * step;
                residual[index] -= matrixDirection[index] * step;
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
                if (!TryAdvanceConjugateIteration(
                        count, residual, direction,
                        preconditionedResidual, matrixDirection,
                        preconditionerDiagonal, solution,
                        ref residualDot))
                {
                    break;
                }
            }
        }

        private static bool TryAdvanceConjugateIteration(
            int count,
            Vector3[] residual,
            Vector3[] direction,
            Vector3[] preconditionedResidual,
            Vector3[] matrixDirection,
            float[] preconditionerDiagonal,
            Vector3[] solution,
            ref double residualDot)
        {
            double denominator = Dot(direction, matrixDirection, count);
            if (Math.Abs(denominator) <= 1e-20d)
            {
                return false;
            }
            float step = (float)(residualDot / denominator);
            AdvanceAlongDirection(
                solution, residual, direction, matrixDirection,
                step, count);
            if (Dot(residual, residual, count) <= 1e-18d)
            {
                return false;
            }
            residualDot = UpdateSearchDirection(
                count, residualDot, residual, direction,
                preconditionedResidual, preconditionerDiagonal);
            return true;
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
            var native = new CgNativeBuffers();
            try
            {
                return TrySolveBurstPath(
                    ref native, buffers, edges, materialStrainConstraints,
                    count, maximumIterations, warmStart, timing)
                    ? buffers.Solution
                    : SolveConjugateGradient(
                        buffers, edges, materialStrainConstraints,
                        count, maximumIterations, warmStart, timing);
            }
            finally
            {
                native.Dispose();
            }
        }

        private static void AllocateAndPackNative(
            ref CgNativeBuffers native,
            SolverBuffers buffers,
            IReadOnlyList<LocalEdge> edges,
            IReadOnlyList<NativeSkinningMaterialStrainConstraint>
                materialStrainConstraints,
            bool warmStart,
            int count)
        {
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

            native.Allocate(
                count, edgeCount, bendCount, strainCount,
                materialCount, materialTermTotal);
            native.Pack(
                buffers, edges, materialStrainConstraints,
                warmStart, count, edgeCount, bendCount,
                strainCount, materialCount);
        }

        private static CgSolveJob CreateSolveJob(
            in CgNativeBuffers native,
            int count,
            int maximumIterations,
            bool warmStart)
        {
            return new CgSolveJob
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
            };
        }

        private static bool TrySolveBurstPath(
            ref CgNativeBuffers native,
            SolverBuffers buffers,
            IReadOnlyList<LocalEdge> edges,
            IReadOnlyList<NativeSkinningMaterialStrainConstraint>
                materialStrainConstraints,
            int count,
            int maximumIterations,
            bool warmStart,
            NativeSkinningSolverTiming timing)
        {
            try
            {
                AllocateAndPackNative(
                    ref native, buffers, edges, materialStrainConstraints,
                    warmStart, count);
                RunBurstJob(
                    native, buffers, count, maximumIterations,
                    warmStart, timing);
                return true;
            }
            catch (Exception)
            {
                // 네이티브 경로 실패 시 managed 솔버로 동일 입력을 다시 푼다.
                return false;
            }
        }

        private static void RunBurstJob(
            in CgNativeBuffers native,
            SolverBuffers buffers,
            int count,
            int maximumIterations,
            bool warmStart,
            NativeSkinningSolverTiming timing)
        {
            CreateSolveJob(native, count, maximumIterations, warmStart)
                .Schedule()
                .Complete();
            timing?.AddCgIterations(native.IterationsRun[0]);
            NativeArray<Vector3>.Copy(
                native.Solution, 0, buffers.Solution, 0, count);
        }

        // Burst CG 한 호출에 필요한 네이티브 버퍼 묶음. 포장·해제 책임을 한 곳에 둔다.

    }
}
