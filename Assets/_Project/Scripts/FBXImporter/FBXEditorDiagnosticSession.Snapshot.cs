using System;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class FBXEditorDiagnosticSession
    {

        internal readonly struct SettingsSnapshot
        {
            internal SettingsSnapshot(
                bool enableRecordingDiagnostics,
                bool enableDiagnosticFingerCloseups,
                bool useDeterministicCaptureFramerateForDiagnostics,
                float startDelay,
                bool showGhostModel)
            {
                EnableRecordingDiagnostics = enableRecordingDiagnostics;
                EnableDiagnosticFingerCloseups = enableDiagnosticFingerCloseups;
                UseDeterministicCaptureFramerateForDiagnostics =
                    useDeterministicCaptureFramerateForDiagnostics;
                StartDelay = startDelay;
                ShowGhostModel = showGhostModel;
            }

            internal bool EnableRecordingDiagnostics { get; }
            internal bool EnableDiagnosticFingerCloseups { get; }
            internal bool UseDeterministicCaptureFramerateForDiagnostics { get; }
            internal float StartDelay { get; }
            internal bool ShowGhostModel { get; }
        }


    }
}