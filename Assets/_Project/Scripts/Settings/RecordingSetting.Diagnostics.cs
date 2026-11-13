using System;
using System.IO;
using Fbx2Vmd.FBXImporter;
using Fbx2Vmd.Settings;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 녹화 진단 설정 동기화 — 본체에서 이관한 partial.
/// </summary>
public sealed partial class RecordingSetting
{
    public void ApplyDiagnosticsToFBXVmdPipeline()
    {
        FBXVmdPipeline fileManager = recordingFBXVmdPipeline;
        ApplyDiagnosticsToResolvedFBXVmdPipeline(fileManager);
    }

    public RecordingCaptureResolutionPlan CreateRecordingCaptureResolutionPlan()
    {
        return CreateDiagnosticsSettings().CreateCaptureResolutionPlan();
    }

    private void PullDiagnosticsFromFBXVmdPipeline()
    {
        FBXVmdPipeline fileManager = recordingFBXVmdPipeline;
        if (fileManager == null)
        {
            return;
        }

        ApplyDiagnosticsSettings(fileManager.DiagnosticsSettings);
    }

    private void ApplyDiagnosticsToResolvedFBXVmdPipeline(FBXVmdPipeline fileManager)
    {
        if (fileManager == null)
        {
            return;
        }

#if UNITY_EDITOR
        // 진단 smoke 세션 진행 중에는 세션이 소유한 진단 플래그를 공유 설정이 덮어쓰지 않는다.
        if (fileManager.EditorDiagnosticSession.IsRecordingOverrideActive)
        {
            return;
        }
#endif

        fileManager.DiagnosticsSettings = CreateDiagnosticsSettings();
    }

    private RecordingDiagnosticsSettings CreateDiagnosticsSettings()
    {
        return new RecordingDiagnosticsSettings(
            enableRecordingDiagnostics,
            useDeterministicCaptureFramerateForDiagnostics,
            enableDiagnosticFingerCloseups,
            recordingCaptureQuality,
            customRecordingCaptureWidth,
            customRecordingCaptureHeight);
    }

    private void ApplyDiagnosticsSettings(RecordingDiagnosticsSettings settings)
    {
        enableRecordingDiagnostics = settings.EnableRecordingDiagnostics;
        useDeterministicCaptureFramerateForDiagnostics =
            settings.UseDeterministicCaptureFramerateForDiagnostics;
        enableDiagnosticFingerCloseups = settings.EnableDiagnosticFingerCloseups;
        recordingCaptureQuality = settings.CaptureQuality;
        customRecordingCaptureWidth = settings.CustomCaptureWidth;
        customRecordingCaptureHeight = settings.CustomCaptureHeight;
    }
}
