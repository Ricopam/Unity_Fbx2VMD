using System;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class HumanoidSupportCurveProblem
    {
        private const int SamplesPerFrame = 4;
        private const double DegreesPerRadian = 180d / Math.PI;
        private const double MaximumShiftMm = 20;
        private readonly Sample[] _samples;
        private readonly double[,] _basis;
        private readonly double[] _baselineRms;
        private readonly double[] _stepLimits;
        private readonly double[] _accelerationLimits;
        private readonly double[] _hipLimits;
        private readonly int _first;
        private readonly int _last;
        private readonly int _controls;
        private readonly HumanoidSupportCurveBasis _curveBasis;
        private readonly int _startFrame;
        private readonly int _endFrame;
        private bool _balanceAbsoluteErrors;

        internal readonly struct LegGeometry
        {
            internal LegGeometry(double3 upper, double3 knee, double3 foot, double referenceDegrees)
            {
                HipToAnkle = upper - foot;
                double upperLength = math.distance(upper, knee);
                double lowerLength = math.distance(knee, foot);
                LengthProduct = upperLength * lowerLength;
                SquaredLengths = upperLength * upperLength + lowerLength * lowerLength;
                ReferenceDegrees = referenceDegrees;
            }

            internal double3 HipToAnkle { get; }
            internal double LengthProduct { get; }
            internal double SquaredLengths { get; }
            internal double ReferenceDegrees { get; }
            internal bool IsValid => LengthProduct > 0 && double.IsFinite(LengthProduct) &&
                math.all(math.isfinite(HipToAnkle)) && double.IsFinite(ReferenceDegrees);

            internal double MeasureFlexion(double3 reach, out double cosine)
            {
                cosine = (math.lengthsq(reach) - SquaredLengths) / (2 * LengthProduct);
                return Math.Acos(Math.Clamp(cosine, -1, 1)) * DegreesPerRadian;
            }
        }

        internal sealed class Curve
        {
            private readonly HumanoidSupportCurveBasis _basis;
            private readonly double[] _values;

            internal Curve(int start, int end, HumanoidSupportCurveBasis basis, double[] values)
            {
                Start = start;
                End = end;
                _basis = basis;
                _values = values;
            }

            internal int Start { get; }
            internal int End { get; }

            internal void WriteCacheIdentity(BinaryWriter writer)
            {
                writer.Write(Start);
                writer.Write(End);
                writer.Write(_basis.Spacing);
                writer.Write(_values.Length);
                foreach (double value in _values) writer.Write(value);
            }

            internal double3 GetShiftMm(double frame)
            {
                Span<double> weights = stackalloc double[4];
                int control = _basis.Evaluate(frame, weights);
                double3 shift = double3.zero;
                for (int index = 0; index < weights.Length; index++)
                {
                    int offset = (control + index) * 3;
                    shift += weights[index] * new double3(_values[offset], _values[offset + 1], _values[offset + 2]);
                }
                double length = math.length(shift);
                return length > MaximumShiftMm ? shift * (MaximumShiftMm / length) : shift;
            }
        }

        internal readonly struct Sample
        {
            internal Sample(double3 hip, LegGeometry left, LegGeometry right)
            {
                Hip = hip;
                Left = left;
                Right = right;
            }

            internal double3 Hip { get; }
            internal LegGeometry Left { get; }
            internal LegGeometry Right { get; }
            internal LegGeometry Leg(int side) => side == 0 ? Left : Right;
        }

        internal HumanoidSupportCurveProblem(Sample[] samples, int captureStart, int first, int last, int spacing, IReadOnlyList<int> boundaries)
        {
            _samples = samples;
            _startFrame = first;
            _endFrame = last;
            _first = (first - captureStart) * SamplesPerFrame;
            _last = (last - captureStart) * SamplesPerFrame;
            _curveBasis = new HumanoidSupportCurveBasis(first, last, spacing);
            _controls = _curveBasis.ControlCount;
            _basis = new double[samples.Length, _controls];
            var baseline = new double[samples.Length, 2];
            var sourceError = new double[2];
            var steps = new double[(samples.Length - 1) * 2];
            var acceleration = new double[(samples.Length - 2) * 2];
            var hipAcceleration = new double[samples.Length - 2];
            MeasureBaselines(captureStart, baseline, sourceError, steps, acceleration, hipAcceleration);
            _baselineRms = Array.ConvertAll(sourceError, value => Math.Sqrt(value / (_last - _first + 1)));
            _stepLimits = Limits(steps, 2, 1, .00125, captureStart, boundaries);
            _accelerationLimits = Limits(acceleration, 2, 2, .0003125, captureStart, boundaries);
            _hipLimits = Limits(hipAcceleration, 1, 2, .0005, captureStart, boundaries); }

        // 표본별 기저선 굴곡·스텝·가속 상한 입력을 계산해 기저 행렬과 함께 채운다.
        void MeasureBaselines(int captureStart, double[,] baseline, double[] sourceError,
            double[] steps, double[] acceleration, double[] hipAcceleration)
        {
            Span<double> local = stackalloc double[4];
            for (int index = 0; index < _samples.Length; index++)
            {
                int control = _curveBasis.Evaluate(captureStart + index / (double)SamplesPerFrame, local);
                for (int offset = 0; offset < local.Length; offset++) _basis[index, control + offset] = local[offset];
                for (int side = 0; side < 2; side++)
                {
                    var leg = _samples[index].Leg(side);
                    baseline[index, side] = leg.MeasureFlexion(leg.HipToAnkle, out _);
                    double error = baseline[index, side] - leg.ReferenceDegrees;
                    if (index >= _first && index <= _last) sourceError[side] += error * error;
                    if (index >= 1) steps[2 * (index - 1) + side] = baseline[index, side] - baseline[index - 1, side];
                    if (index >= 2) acceleration[2 * (index - 2) + side] = baseline[index, side] - 2 * baseline[index - 1, side] + baseline[index - 2, side]; }
                if (index >= 2) hipAcceleration[index - 2] = math.length(_samples[index].Hip - 2 * _samples[index - 1].Hip + _samples[index - 2].Hip); }
        }

        internal bool TryGenerate(out Curve curve)
        {
            curve = null;
            if (_baselineRms[0] <= 1e-6 || _baselineRms[1] <= 1e-6) return false;
            if (!TryGenerateControls(out var controls))
            {
                _balanceAbsoluteErrors = true;
                if (!TryGenerateControls(out controls)) return false;
            }
            curve = new Curve(_startFrame, _endFrame, _curveBasis, controls);
            return true;
        }

        private bool TryGenerateControls(out double[] controls)
        {
            if (!HumanoidSupportCurveOptimizer.TrySolve(_controls * 3, Evaluate, out controls)) return false;
            var rms = ReadRms(controls);
            return rms[0] < _baselineRms[0] - .001 && rms[1] < _baselineRms[1] - .001;
        }

        private double3 ShiftAt(int index, double[] controls)
        {
            double3 shift = double3.zero;
            for (int control = 0; control < _controls; control++)
            {
                int offset = 3 * control;
                shift += _basis[index, control] * new double3(controls[offset], controls[offset + 1], controls[offset + 2]);
            }
            return shift;
        }

        private double[] ReadRms(double[] controls)
        {
            var squared = new double[2];
            for (int index = _first; index <= _last; index++)
            {
                double3 shift = ShiftAt(index, controls);
                for (int side = 0; side < 2; side++)
                {
                    var leg = _samples[index].Leg(side);
                    double error = leg.MeasureFlexion(leg.HipToAnkle + shift, out _) - leg.ReferenceDegrees;
                    squared[side] += error * error;
                }
            }
            return Array.ConvertAll(squared, value => Math.Sqrt(value / (_last - _first + 1)));
        }

        private HumanoidSupportCurveOptimizer.Evaluation Evaluate(double[] controls, bool derivatives)
        {
            return new EvaluationSession(this, controls, derivatives).Run();
        }

        private void Add(double[] row, int index, double3 gradient)
        {
            for (int control = 0; control < _controls; control++)
            {
                double weight = _basis[index, control];
                if (weight == 0) continue;
                int offset = 3 * control;
                row[offset] += weight * gradient.x;
                row[offset + 1] += weight * gradient.y;
                row[offset + 2] += weight * gradient.z;
            }
        }

        private static double[] Limits(double[] values, int channels, int order, double tolerance,
            int captureStart, IReadOnlyList<int> boundaries)
        {
            var limits = new double[values.Length];
            Apply(0, values.Length / channels, false);
            foreach (int boundary in boundaries)
            {
                int first = (boundary - 1 - captureStart) * SamplesPerFrame;
                int end = (boundary + 1 - captureStart) * SamplesPerFrame + 1 - order;
                if (first >= 0 && end <= values.Length / channels) Apply(first, end, true);
            }
            return limits;

            void Apply(int first, int end, bool tighten)
            {
                for (int channel = 0; channel < channels; channel++)
                {
                    double maximum = 0;
                    for (int index = first; index < end; index++) maximum = Math.Max(maximum, Math.Abs(values[index * channels + channel]));
                    for (int index = first; index < end; index++)
                    {
                        int offset = index * channels + channel;
                        limits[offset] = tighten ? Math.Min(limits[offset], maximum + tolerance) : maximum + tolerance;
                    }
                }
            }
        }
    }
}
