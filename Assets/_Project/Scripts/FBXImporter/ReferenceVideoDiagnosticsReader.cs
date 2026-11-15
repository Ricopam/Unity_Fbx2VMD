using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class ReferenceVideoDiagnosticsReader
    {
        internal static ReferenceVideoDiagnosticsData Read(
            string analysisPath,
            string frameMetricsPath)
        {
            ReferenceVideoDiagnosticsData data = new ReferenceVideoDiagnosticsData
            {
                AnalysisFileExists = File.Exists(analysisPath),
                FrameMetricsFileExists = File.Exists(frameMetricsPath)
            };

            ReadAnalysis(analysisPath, data);
            ReadFrameMetrics(frameMetricsPath, data);
            return data;
        }

        private static void ReadAnalysis(string path, ReferenceVideoDiagnosticsData data)
        {
            if (!data.AnalysisFileExists)
            {
                return;
            }

            try
            {
                AnalysisDocument document = JsonUtility.FromJson<AnalysisDocument>(
                    File.ReadAllText(path, Encoding.UTF8));
                if (document == null)
                {
                    return;
                }

                ApplyAnalysisDocument(document, data);
            }
            catch (Exception ex)
            {
                data.AnalysisError = ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static void ApplyAnalysisDocument(AnalysisDocument document, ReferenceVideoDiagnosticsData data)
        {
            data.AnalysisSchema = document.schema ?? string.Empty;
            data.ExtractedFrameCount = Math.Max(0, document.extractedFrameCount);
            if (document.video == null)
            {
                return;
            }

            data.VideoWidth = Math.Max(0, document.video.width);
            data.VideoHeight = Math.Max(0, document.video.height);
            data.AverageFrameRate = document.video.avg_frame_rate ?? string.Empty;
            data.StreamDurationSeconds = ParseInvariantFloat(document.video.stream_duration);
            data.TotalVideoFrames = ParseInvariantInt(document.video.nb_frames);
        }

        private static void ReadFrameMetrics(string path, ReferenceVideoDiagnosticsData data)
        {
            if (!data.FrameMetricsFileExists)
            {
                return;
            }

            try
            {
                FrameMetricsDocument document = JsonUtility.FromJson<FrameMetricsDocument>(
                    File.ReadAllText(path, Encoding.UTF8));
                if (document == null)
                {
                    return;
                }

                ApplyFrameMetricsDocument(document, data);
            }
            catch (Exception ex)
            {
                data.FrameMetricsError = ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static void ApplyFrameMetricsDocument(FrameMetricsDocument document, ReferenceVideoDiagnosticsData data)
        {
            data.FrameMetricsSchema = document.schema ?? string.Empty;
            data.FrameMetricsSampleCount = Math.Max(0, document.sampleCount);
            data.FrameMetricsExtractedFrameCount = Math.Max(0, document.extractedFrameCount);
            data.AverageBBoxHeightRatio = document.avgBBoxHeightRatio;
            data.AverageBBoxWidthRatio = document.avgBBoxWidthRatio;
            data.CenterXRangeRatio = document.centerXRangeRatio;
            data.MaxBottomGapRatio = document.maxBottomGapRatio;
            data.AverageBrightAreaRatio = document.avgBrightAreaRatio;
            data.FrameMetricRows = document.rows ?? Array.Empty<ReferenceMp4FrameMetricRow>();
        }

        private static int ParseInvariantInt(string value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : 0;
        }

        private static float ParseInvariantFloat(string value)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                ? parsed
                : float.NaN;
        }

    }
}
