using System;

namespace Fbx2Vmd.FBXImporter
{
    internal static class HumanoidSupportConstraintProjection
    {
        private const double DirectionTolerance = 1e-9;
        private const double RankTolerance = 1e-10;
        private const double FeasibilityTolerance = 2e-8;
        private const int MaximumIterations = 160;

        // 정규화한 선형 제약에서 가능한 방향을 고르고, 독립 활성 제약만 직교화함.
        internal static bool TryProject(double[] desired, double[][] normals, double[] distance,
            double[] feasible, out double[] projected)
        {
            projected = (double[])feasible.Clone();
            var workspace = new Workspace(desired.Length);
            for (int iteration = 0; iteration < MaximumIterations; iteration++)
            {
                if (!workspace.BuildOrthonormalDirection(desired, projected, normals))
                {
                    return false;
                }

                if (Dot(workspace.direction, workspace.direction) < DirectionTolerance * DirectionTolerance)
                {
                    int release = workspace.ResolveReleaseConstraint();
                    if (release < 0) return IsFeasible(projected, normals, distance);
                    workspace.ReleaseAt(release);
                    continue;
                }

                int blocking = FindBlockingConstraint(
                    normals, distance, projected, workspace.direction, out double fraction);

                Add(projected, workspace.direction, fraction);
                if (blocking < 0) continue;
                if (!workspace.TryActivate(blocking)) return false;
            }
            return false;
        }

        private sealed class Workspace
        {
            private readonly int count;
            internal int activeCount;
            private readonly int[] active;
            private readonly double[][] axes;
            private readonly double[,] triangular;
            private readonly double[] toward;
            internal readonly double[] direction;
            private readonly double[] coordinates;
            private readonly double[] multipliers;

            internal Workspace(int dimension)
            {
                count = dimension;
                active = new int[count];
                axes = new double[count][];
                for (int index = 0; index < count; index++) axes[index] = new double[count];
                triangular = new double[count, count];
                toward = new double[count];
                direction = new double[count];
                coordinates = new double[count];
                multipliers = new double[count];
            }

            internal bool BuildOrthonormalDirection(double[] desired, double[] projected, double[][] normals)
            {
                for (int axis = 0; axis < count; axis++) toward[axis] = desired[axis] - projected[axis];
                Array.Copy(toward, direction, count);
                for (int constraint = 0; constraint < activeCount; constraint++)
                {
                    double[] unit = axes[constraint];
                    Array.Copy(normals[active[constraint]], unit, count);
                    for (int previous = 0; previous < constraint; previous++)
                    {
                        double weight = Dot(axes[previous], unit);
                        triangular[previous, constraint] = weight;
                        Add(unit, axes[previous], -weight);
                    }

                    double length = Math.Sqrt(Dot(unit, unit));
                    if (!double.IsFinite(length) || length <= RankTolerance) return false;
                    triangular[constraint, constraint] = length;
                    for (int axis = 0; axis < count; axis++) unit[axis] /= length;
                    coordinates[constraint] = Dot(unit, toward);
                    Add(direction, unit, -coordinates[constraint]);
                }

                return true;
            }

            internal int ResolveReleaseConstraint()
            {
                int release = -1;
                double minimum = -DirectionTolerance;
                for (int constraint = activeCount - 1; constraint >= 0; constraint--)
                {
                    double value = -coordinates[constraint];
                    for (int later = constraint + 1; later < activeCount; later++)
                        value -= triangular[constraint, later] * multipliers[later];
                    value /= triangular[constraint, constraint];
                    multipliers[constraint] = value;
                    if (value < minimum)
                    {
                        minimum = value;
                        release = constraint;
                    }
                }

                return release;
            }

            internal void ReleaseAt(int release)
            {
                activeCount--;
                Array.Copy(active, release + 1, active, release, activeCount - release);
            }

            internal bool TryActivate(int blocking)
            {
                if (activeCount == active.Length || Array.IndexOf(active, blocking, 0, activeCount) >= 0)
                    return false;
                active[activeCount++] = blocking;
                return true;
            }
        }

        private static int FindBlockingConstraint(
            double[][] normals, double[] distance, double[] projected, double[] direction,
            out double fraction)
        {
            int blocking = -1;
            fraction = 1;
            for (int constraint = 0; constraint < normals.Length; constraint++)
            {
                double slope = Dot(normals[constraint], direction);
                if (slope >= -DirectionTolerance) continue;
                double available = Math.Max(0, distance[constraint] + Dot(normals[constraint], projected));
                double step = available / -slope;
                if (step >= fraction) continue;
                fraction = step;
                blocking = constraint;
            }

            return blocking;
        }

        private static bool IsFeasible(double[] point, double[][] normals, double[] distance)
        {
            for (int index = 0; index < normals.Length; index++)
                if (!(distance[index] + Dot(normals[index], point) >= -FeasibilityTolerance)) return false;
            return true;
        }

        private static double Dot(double[] first, double[] second)
        {
            double sum = 0;
            for (int index = 0; index < first.Length; index++) sum += first[index] * second[index];
            return sum;
        }

        private static void Add(double[] target, double[] value, double weight)
        {
            for (int index = 0; index < target.Length; index++) target[index] += value[index] * weight;
        }
    }
}
