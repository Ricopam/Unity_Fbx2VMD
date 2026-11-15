using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
        private const int JointProjectionRingCount = 2;
        private const int JointProjectionOuterIterations = 24;
        private const int JointProjectionInnerIterations = 2000;
        private const int JointProjectionLineSearchSteps = 6;
        private const double JointProjectionPositionScale = 0.001;
        private const double JointProjectionAngleMargin = 0.1;
        private const double JointProjectionTolerance = 1e-7;

        private static bool TryProjectJointConstraints(
            IReadOnlyList<Vector3> baseline,
            NativeSkinningSurfaceContract contract,
            NativeSkinningSurfaceCorrectionResult initial,
            HashSet<int> originalPairs,
            int[] recoveryPairs,
            FrameEdge[] frameEdges,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            result = null;
            LocalWorkset workset = BuildLocalWorkset(
                baseline, contract, originalPairs, StrongExtendedConfiguration.WorksetRingCount,
                frameEdges, new SolverBuffers());
            var session = new ProjectionSession(
                baseline, contract, initial.CorrectedVertices, workset);
            if (!session.TrySetup())
            {
                return false;
            }
            session.Solve();
            Vector3[] corrected = session.BuildCorrectedVertices();
            if (!AreVerticesFinite(corrected))
            {
                return false;
            }
            NativeSkinningSurfaceCorrectionResult candidate = EvaluateResult(
                baseline, corrected, contract, frameEdges, originalPairs.Count, recoveryPairs);
            if (!IsWithinStrongQuality(candidate, contract) || !HasDisplayStrainMargin(candidate))
            {
                return false;
            }
            result = candidate;
            return true;
        }

        private static bool ProjectLinearConstraints( JointProjectionRow[] rows, double[] multipliers, double[] search, double3[] proposal, double3[] columnSums)
        {
            if (!TryComputeRowUpperBound(rows, columnSums, out double upperBound))
            {
                return false; }
            // 희소 제약의 쌍대 문제에서 가속 투영을 수행하고 행합 상한으로 스텝을 제한함.
            Array.Copy(multipliers, search, search.Length);
            RunAcceleratedProjection(rows, multipliers, search, proposal, upperBound);
            AccumulateProposal(rows, proposal, multipliers);
            return true; }

        static bool TryComputeRowUpperBound(JointProjectionRow[] rows, double3[] columnSums, out double upperBound)
        {
            Array.Clear(columnSums, 0, columnSums.Length);
            foreach (JointProjectionRow row in rows)
            {
                row.AddColumnMagnitudes(columnSums); }
            upperBound = 0;
            foreach (JointProjectionRow row in rows)
            {
                upperBound = Math.Max(upperBound, row.Dot(columnSums, absolute: true)); }
            return double.IsFinite(upperBound) && upperBound > 0;
        }

        static void RunAcceleratedProjection(JointProjectionRow[] rows, double[] multipliers,
            double[] search, double3[] proposal, double upperBound)
        {
            for (int iteration = 0; iteration < JointProjectionInnerIterations; iteration++)
            {
                AccumulateProposal(rows, proposal, search);
                double acceleration = (double)iteration / (iteration + 3);
                double maximumChange = 0;
                for (int index = 0; index < rows.Length; index++)
                {
                    JointProjectionRow row = rows[index];
                    double next = row.HasGradient
                        ? Math.Max(0, search[index] - (row.Dot(proposal) - row.RightHandSide) / upperBound) : 0;
                    double change = next - multipliers[index];
                    maximumChange = Math.Max(maximumChange, Math.Abs(change));
                    search[index] = next + acceleration * change;
                    multipliers[index] = next; }
                if (maximumChange < 1e-9)
                {
                    break; } }
        }

        static void AccumulateProposal(JointProjectionRow[] rows, double3[] proposal, double[] values)
        {
            Array.Clear(proposal, 0, proposal.Length);
            for (int index = 0; index < rows.Length; index++)
            {
                if (values[index] != 0)
                {
                    rows[index].Accumulate(values[index], proposal); } } }

        private static double3 ToDouble(Vector3 value) => new double3(value.x, value.y, value.z);

        private static double3 FaceNormal(double3 first, double3 second, double3 third) =>
            math.cross(second - first, third - first);

        private static double MeasureFoldCosine(
            double3 first, double3 second, double3 edge0, double3 edge1,
            out double3 firstGradient, out double3 secondGradient)
        {
            double3 firstNormal = FaceNormal(first, edge0, edge1);
            double3 secondNormal = FaceNormal(second, edge1, edge0);
            double firstLength = Math.Max(math.length(firstNormal), 1e-16);
            double secondLength = Math.Max(math.length(secondNormal), 1e-16);
            double3 firstUnit = firstNormal / firstLength;
            double3 secondUnit = secondNormal / secondLength;
            double cosine = math.dot(firstUnit, secondUnit);
            firstGradient = (secondUnit - cosine * firstUnit) / firstLength;
            secondGradient = (firstUnit - cosine * secondUnit) / secondLength;
            return cosine;
        }

        private static void AddNormalGradient(
            JointProjectionRow row, int firstIndex, int secondIndex, int thirdIndex,
            double3 first, double3 second, double3 third, double3 gradient)
        {
            double3 secondGradient = math.cross(third - first, gradient);
            double3 thirdGradient = math.cross(gradient, second - first);
            row.Add(firstIndex, -secondGradient - thirdGradient);
            row.Add(secondIndex, secondGradient);
            row.Add(thirdIndex, thirdGradient);
        }

        private sealed class JointProjectionRow
        {
            private readonly int[] _indices = new int[4];
            private readonly double3[] _gradients = new double3[4];
            private int _count;

            internal double RightHandSide { get; private set; }
            internal bool HasGradient => _count > 0;

            internal void Clear()
            {
                _count = 0;
                RightHandSide = 0;
            }

            internal void Add(int index, double3 gradient)
            {
                if (index < 0)
                {
                    return;
                }
                for (int entry = 0; entry < _count; entry++)
                {
                    if (_indices[entry] == index)
                    {
                        _gradients[entry] += gradient;
                        return;
                    }
                }
                _indices[_count] = index;
                _gradients[_count++] = gradient;
            }

            internal void Normalize(double value, double3[] current)
            {
                double lengthSquared = 0;
                for (int entry = 0; entry < _count; entry++)
                {
                    lengthSquared += math.lengthsq(_gradients[entry]);
                }
                double norm = Math.Sqrt(lengthSquared);
                if (!double.IsFinite(norm) || norm * JointProjectionPositionScale <= 1e-10)
                {
                    Clear();
                    return;
                }
                for (int entry = 0; entry < _count; entry++)
                {
                    _gradients[entry] /= norm;
                }
                RightHandSide = Dot(current) - value / (norm * JointProjectionPositionScale);
            }

            internal double Dot(double3[] point, bool absolute = false)
            {
                double total = 0;
                for (int entry = 0; entry < _count; entry++)
                {
                    double3 gradient = _gradients[entry];
                    total += math.dot(absolute ? math.abs(gradient) : gradient, point[_indices[entry]]);
                }
                return total;
            }

            internal void AddColumnMagnitudes(double3[] columns)
            {
                for (int entry = 0; entry < _count; entry++)
                {
                    columns[_indices[entry]] += math.abs(_gradients[entry]);
                }
            }

            internal void Accumulate(double amount, double3[] point)
            {
                for (int entry = 0; entry < _count; entry++)
                {
                    point[_indices[entry]] += amount * _gradients[entry];
                }
            }
        }
    }
}
