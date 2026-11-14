using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class HumanoidSupportCurveProblem
    {
        // Evaluate 실행 워크스페이스: 프레임 데이터·제약·도함수 상태를 보유해
        // 한 평가 호출 안에서만 공유되는 지역 상태를 메서드 경계로 나눔.
        private sealed class EvaluationSession
        {
            private readonly HumanoidSupportCurveProblem owner;
            private readonly double[] controls;
            private readonly bool derivatives;
            private readonly int frameCount;
            private readonly int count;
            private readonly int activeCount;
            private readonly double3[] shifts;
            private readonly double[,] angles;
            private readonly double[,] cosines;
            private readonly double3[,] angleGradient;
            private readonly double3[,] cosineGradient;
            private readonly double[] errors = new double[2];
            private readonly double leftWeight;
            private readonly double rightWeight;
            private readonly double[] gradient;
            private readonly double[,] metric;
            private readonly double[][] sideGradient;
            private readonly List<double> slack = new List<double>();
            private readonly List<double[]> jacobian;

            internal EvaluationSession(
                HumanoidSupportCurveProblem owner, double[] controls, bool derivatives)
            {
                this.owner = owner;
                this.controls = controls;
                this.derivatives = derivatives;
                frameCount = owner._samples.Length;
                count = controls.Length;
                activeCount = owner._last - owner._first + 1;
                shifts = new double3[frameCount];
                angles = new double[frameCount, 2];
                cosines = new double[frameCount, 2];
                angleGradient = new double3[frameCount, 2];
                cosineGradient = new double3[frameCount, 2];
                double leftSquared = owner._baselineRms[0] * owner._baselineRms[0];
                double rightSquared = owner._baselineRms[1] * owner._baselineRms[1];
                leftWeight = owner._balanceAbsoluteErrors
                    ? 2 * leftSquared / (leftSquared + rightSquared) : 1;
                rightWeight = 2 - leftWeight;
                gradient = derivatives ? new double[count] : null;
                metric = derivatives ? new double[count, count] : null;
                sideGradient = derivatives
                    ? new[] { new double[count], new double[count] } : null;
                jacobian = derivatives ? new List<double[]>() : null;
            }

            internal HumanoidSupportCurveOptimizer.Evaluation Run()
            {
                ComputeFrameData();
                AddAngularConstraints(owner._stepLimits, 1);
                AddAngularConstraints(owner._accelerationLimits, 2);
                AddHipAccelerationConstraints();
                AddShiftLimitConstraints();
                AddCosineConstraints();
                AddErrorConstraints();
                FinalizeDerivatives();
                return new HumanoidSupportCurveOptimizer.Evaluation(
                    (leftWeight * errors[0] + rightWeight * errors[1]) / (2 * activeCount),
                    slack.ToArray(), gradient, metric, jacobian?.ToArray());
            }

            private void ComputeFrameData()
            {
                for (int index = 0; index < frameCount; index++)
                {
                    shifts[index] = owner.ShiftAt(index, controls);
                    for (int side = 0; side < 2; side++)
                    {
                        var leg = owner._samples[index].Leg(side);
                        double3 reach = leg.HipToAnkle + shifts[index];
                        angles[index, side] = leg.MeasureFlexion(reach, out double cosine);
                        cosines[index, side] = cosine;
                        if (derivatives)
                        {
                            cosineGradient[index, side] = reach / leg.LengthProduct;
                            angleGradient[index, side] = -cosineGradient[index, side] *
                                (DegreesPerRadian / Math.Sqrt(Math.Max(1e-12, 1 - cosine * cosine)));
                        }
                        if (index < owner._first || index > owner._last) continue;

                        double relative = (angles[index, side] - leg.ReferenceDegrees) / owner._baselineRms[side];
                        errors[side] += relative * relative;
                        if (!derivatives) continue;
                        var row = new double[count];
                        owner.Add(row, index, angleGradient[index, side] / owner._baselineRms[side]);
                        for (int axis = 0; axis < count; axis++)
                        {
                            if (row[axis] == 0) continue;
                            sideGradient[side][axis] += 2 * relative * row[axis] / activeCount;
                            double weight = side == 0 ? leftWeight : rightWeight;
                            for (int column = 0; column <= axis; column++)
                                metric[axis, column] += weight * row[axis] * row[column] / activeCount;
                        }
                    }
                }
            }

            private void AddAngularConstraints(double[] limits, int order)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                for (int index = 0; index + order < frameCount; index++)
                for (int side = 0; side < 2; side++)
                {
                    double difference = order == 1 ? angles[index + 1, side] - angles[index, side] :
                        angles[index + 2, side] - 2 * angles[index + 1, side] + angles[index, side];
                    double[] row = MakeRow(limits[2 * index + side] + sign * difference);
                    if (!derivatives) continue;
                    owner.Add(row, index, (order == 1 ? -sign : sign) * angleGradient[index, side]);
                    owner.Add(row, index + 1, (order == 1 ? sign : -2 * sign) * angleGradient[index + 1, side]);
                    if (order == 2) owner.Add(row, index + 2, sign * angleGradient[index + 2, side]);
                }
            }

            private void AddHipAccelerationConstraints()
            {
                for (int index = 0; index + 2 < frameCount; index++)
                {
                    double3 acceleration = owner._samples[index + 2].Hip - 2 * owner._samples[index + 1].Hip + owner._samples[index].Hip +
                        shifts[index + 2] - 2 * shifts[index + 1] + shifts[index];
                    double length = math.length(acceleration);
                    double[] row = MakeRow(owner._hipLimits[index] - length);
                    if (derivatives)
                    {
                        double3 normal = -acceleration / Math.Max(1e-12, length);
                        owner.Add(row, index, normal);
                        owner.Add(row, index + 1, -2 * normal);
                        owner.Add(row, index + 2, normal);
                    }
                }
            }

            private void AddShiftLimitConstraints()
            {
                double maximumSquared = MaximumShiftMm * MaximumShiftMm;
                for (int index = 0; index < frameCount; index++)
                {
                    double[] row = MakeRow(1 - math.lengthsq(shifts[index]) / maximumSquared);
                    if (derivatives) owner.Add(row, index, -2 * shifts[index] / maximumSquared);
                }
            }

            private void AddCosineConstraints()
            {
                for (int sign = -1; sign <= 1; sign += 2)
                for (int index = owner._first; index <= owner._last; index++)
                for (int side = 0; side < 2; side++)
                {
                    double[] row = MakeRow(1 + sign * cosines[index, side]);
                    if (derivatives) owner.Add(row, index, sign * cosineGradient[index, side]);
                }
            }

            private void AddErrorConstraints()
            {
                for (int side = 0; side < 2; side++)
                {
                    double[] row = MakeRow(1 - errors[side] / activeCount);
                    if (derivatives)
                        for (int axis = 0; axis < count; axis++) row[axis] = -sideGradient[side][axis];
                }
            }

            private void FinalizeDerivatives()
            {
                if (!derivatives) return;
                for (int axis = 0; axis < count; axis++)
                {
                    gradient[axis] = (leftWeight * sideGradient[0][axis] + rightWeight * sideGradient[1][axis]) * .5;
                    metric[axis, axis] += 1e-5;
                }
            }

            private double[] MakeRow(double value)
            {
                slack.Add(value);
                if (!derivatives) return null;
                var row = new double[count];
                jacobian.Add(row);
                return row;
            }
        }
    }
}
