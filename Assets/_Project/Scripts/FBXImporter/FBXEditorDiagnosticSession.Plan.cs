namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class FBXEditorDiagnosticSession
    {
            internal sealed class Plan
            {
                internal float DurationSeconds { get; set; }
                internal int TargetFrameCount { get; set; }
                internal float[] SampleTimesOverride { get; set; }
                internal FBXVmdPipeline.EditorDiagnosticSmokeSegment Segment { get; set; }
                internal string CurrentFbxFileName { get; set; }
                internal bool HasCaptureResolutionOverride { get; set; }
                internal int CaptureWidth { get; set; }
                internal int CaptureHeight { get; set; }
                internal float DiagnosticScreenshotPaddingOverride { get; set; } = float.NaN;
                internal float DiagnosticScreenshotVerticalViewportCenterOverride { get; set; } = float.NaN;
                internal bool UseKnownReferenceTiming { get; set; }
                internal bool UseInputBaseName { get; set; }
                internal float RecordingStartTimeOverrideSeconds { get; set; } = float.NaN;
                internal float RecordingPlaybackSpeedOverride { get; set; } = float.NaN;
            }
    }
}
