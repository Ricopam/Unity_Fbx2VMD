using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
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
                NativeArray<Vector3> residual, NativeArray<Vector3> direction, NativeArray<Vector3> preconditionedResidual, NativeArray<Vector3> matrixDirection)
            {
                double residualDot = Dot(residual, direction);
                int run = 0;
                for (int iteration = 0; iteration < MaximumIterations && residualDot > 1e-20d; iteration++) {
                    run++;
                    ApplyMatrix(direction, matrixDirection);
                    double denominator = Dot(direction, matrixDirection);
                    if (math.abs(denominator) <= 1e-20d)
                    {
                        break; }
                    float step = (float)(residualDot / denominator);
                    for (int index = 0; index < Count; index++)
                    {
                        Solution[index] += direction[index] * step;
                        residual[index] -= matrixDirection[index] * step; }
                    if (Dot(residual, residual) <= 1e-18d)
                    {
                        break; }
                    ApplyPreconditioner(residual, preconditionedResidual);
                    double nextResidualDot = Dot(residual, preconditionedResidual);
                    float beta = (float)(nextResidualDot / residualDot);
                    for (int index = 0; index < Count; index++)
                    {
                        direction[index] = preconditionedResidual[index] + direction[index] * beta; }
                    residualDot = nextResidualDot; }
                return run; }

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
                    } } }

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
                            projectedDisplacement; }
                    if (indices.y >= 0)
                    {
                        result[indices.y] += EdgeStrainBarrierWeight * StrainSecondGradients[term] * projectedDisplacement; } } }

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
    }
}
