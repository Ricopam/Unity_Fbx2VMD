using System;
using System.Collections.Generic;

namespace Fbx2Vmd.FBXImporter
{
    internal static class ReferenceVideoClipCoverageCalculator
    {
        private const float BoundaryEpsilonSeconds = 0.0001f;

        internal static ReferenceVideoClipCoverageData Calculate(
            ReferenceMp4FrameMetricRow[] rows,
            float startSeconds,
            float durationSeconds)
        {
            float safeStartSeconds = Math.Max(0f, startSeconds);
            float safeDurationSeconds = Math.Max(0f, durationSeconds);
            ReferenceVideoClipCoverageData data = new ReferenceVideoClipCoverageData
            {
                EndSeconds = safeStartSeconds + safeDurationSeconds,
                SampleGapSeconds = safeDurationSeconds
            };
            if (rows == null || safeDurationSeconds <= 0f)
            {
                return data;
            }

            SampleAccumulator accumulator = new SampleAccumulator();
            CollectSamples(rows, safeStartSeconds, safeDurationSeconds, data.EndSeconds, accumulator);

            data.Rows = accumulator.SelectedRows.ToArray();
            data.SampleSeconds = accumulator.SampleSeconds.ToArray();
            data.SampleCount = data.Rows.Length;
            if (data.SampleCount <= 0)
            {
                return data;
            }

            accumulator.ApplyAggregates(data, safeDurationSeconds);
            return data;
        }

        private static void CollectSamples(
            ReferenceMp4FrameMetricRow[] rows,
            float safeStartSeconds,
            float safeDurationSeconds,
            float endSeconds,
            SampleAccumulator accumulator)
        {
            foreach (ReferenceMp4FrameMetricRow row in rows)
            {
                if (row == null ||
                    float.IsNaN(row.seconds) ||
                    row.seconds < safeStartSeconds - BoundaryEpsilonSeconds ||
                    row.seconds > endSeconds + BoundaryEpsilonSeconds)
                {
                    continue;
                }

                float localSeconds = Math.Min(
                    safeDurationSeconds,
                    Math.Max(0f, row.seconds - safeStartSeconds));
                accumulator.Add(row, localSeconds);
            }
        }

        private sealed class SampleAccumulator
        {
            internal float FirstSeconds = float.PositiveInfinity;
            internal float LastSeconds = float.NegativeInfinity;
            private float sumBBoxHeight;
            private float sumBBoxWidth;
            private float sumBrightArea;
            private float maxBottomGap = float.NegativeInfinity;
            private float minCenterX = float.PositiveInfinity;
            private float maxCenterX = float.NegativeInfinity;
            internal readonly List<float> SampleSeconds = new List<float>();
            internal readonly List<ReferenceMp4FrameMetricRow> SelectedRows =
                new List<ReferenceMp4FrameMetricRow>();

            internal void Add(ReferenceMp4FrameMetricRow row, float localSeconds)
            {
                SelectedRows.Add(row);
                SampleSeconds.Add(localSeconds);
                FirstSeconds = Math.Min(FirstSeconds, localSeconds);
                LastSeconds = Math.Max(LastSeconds, localSeconds);
                sumBBoxHeight += row.bboxHeightRatio;
                sumBBoxWidth += row.bboxWidthRatio;
                sumBrightArea += row.brightAreaRatio;
                maxBottomGap = Math.Max(maxBottomGap, row.bottomGapRatio);
                minCenterX = Math.Min(minCenterX, row.centerXRatio);
                maxCenterX = Math.Max(maxCenterX, row.centerXRatio);
            }

            internal void ApplyAggregates(ReferenceVideoClipCoverageData data, float safeDurationSeconds)
            {
                data.FirstSampleSeconds = FirstSeconds;
                data.LastSampleSeconds = LastSeconds;
                data.SampleCoverageRatio = Clamp01(LastSeconds / safeDurationSeconds);
                data.SampleGapSeconds = Math.Max(0f, safeDurationSeconds - LastSeconds);
                data.AverageBBoxHeightRatio = sumBBoxHeight / data.SampleCount;
                data.AverageBBoxWidthRatio = sumBBoxWidth / data.SampleCount;
                data.CenterXRangeRatio = maxCenterX - minCenterX;
                data.MaxBottomGapRatio = maxBottomGap;
                data.AverageBrightAreaRatio = sumBrightArea / data.SampleCount;
            }
        }

        private static float Clamp01(float value)
        {
            return Math.Min(1f, Math.Max(0f, value));
        }
    }
}
