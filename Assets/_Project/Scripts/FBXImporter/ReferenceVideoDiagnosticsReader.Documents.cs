using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class ReferenceVideoDiagnosticsReader
    {

        [Serializable]
        private sealed class AnalysisDocument
        {
            public string schema = string.Empty;
            public int extractedFrameCount = 0;
            public VideoStreamDocument video = null;
        }

        [Serializable]
        private sealed class VideoStreamDocument
        {
            public int width = 0;
            public int height = 0;
            public string avg_frame_rate = string.Empty;
            public string stream_duration = string.Empty;
            public string nb_frames = string.Empty;
        }

        [Serializable]
        private sealed class FrameMetricsDocument
        {
            public string schema = string.Empty;
            public int sampleCount = 0;
            public int extractedFrameCount = 0;
            public float avgBBoxHeightRatio = 0f;
            public float avgBBoxWidthRatio = 0f;
            public float centerXRangeRatio = 0f;
            public float maxBottomGapRatio = 0f;
            public float avgBrightAreaRatio = 0f;
            public ReferenceMp4FrameMetricRow[] rows = null;
        }

    }
}