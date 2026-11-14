using System;
using System.IO;
using Fbx2Vmd.FBXImporter;
using Fbx2Vmd.Settings;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 공유 설정 파일 세션 상태와 명령 처리 — 본체에서 이관한 partial.
/// </summary>
public sealed partial class RecordingSetting
{
    private RecordingSharedSettingsFileSession sharedSettingsFileSession;
    private float nextSharedSettingsPollTime;
    private readonly MainRecordingSettingsFbxImportCommandProcessor sharedSettingsFbxImportCommandProcessor =
        new MainRecordingSettingsFbxImportCommandProcessor();
    private readonly MainRecordingSettingsMotionCommandProcessor sharedSettingsMotionCommandProcessor =
        new MainRecordingSettingsMotionCommandProcessor();
    private static readonly Func<FBXVmdPipeline, string, bool> DefaultSharedSettingsFbxImportStarter =
        (fileManager, path) => fileManager.TryStartFbxImportFromSharedSettings(path);
    private Func<FBXVmdPipeline, string, bool> sharedSettingsFbxImportStarter =
        DefaultSharedSettingsFbxImportStarter;

    public MainRecordingSettingsActionResult LoadSharedSettings()
    {
        try
        {
            sharedSettingsFileSession =
                new RecordingSharedSettingsFileSession(sharedSettingsFilePathOverride);
            MainRecordingSettingsDocument document = sharedSettingsFileSession.LoadCurrent();
            return ApplySharedSettingsDocument(
                document,
                recordingFBXVmdPipeline,
                startFbxImport: false,
                clearPendingCommandWhenSkipped: true);
        }
        catch (Exception exception)
        {
            string message = "공유 설정 로드에 실패했습니다.";
            Debug.LogWarning($"[RecordingSetting] {message} {exception.Message}");
            return MainRecordingSettingsActionResult.Failure(message);
        }
    }

    public MainRecordingSettingsActionResult PollSharedSettingsIfChanged()
    {
        try
        {
            EnsureSharedSettingsFileSession();
            if (!sharedSettingsFileSession.TryLoadChanged(
                    out MainRecordingSettingsDocument document))
            {
                return MainRecordingSettingsActionResult.Success("공유 설정 변경 없음");
            }

            return ApplySharedSettingsDocument(document, recordingFBXVmdPipeline, true);
        }
        catch (Exception exception)
        {
            string message = "공유 설정 갱신에 실패했습니다.";
            Debug.LogWarning($"[RecordingSetting] {message} {exception.Message}");
            return MainRecordingSettingsActionResult.Failure(message);
        }
    }

    private void WriteRuntimePlayModeStateQuietly(string playMode)
    {
        MainRecordingSettingsActionResult result = WriteRuntimePlayModeState(playMode);
        if (!result.Succeeded)
        {
            Debug.LogWarning($"[RecordingSetting] {result.UserMessage}");
        }
        WriteMotionStateQuietly(playMode == MainRecordingSettingsState.Playing);
    }

    private void WriteMotionStateQuietly(bool isPlaying = true)
    {
        try
        {
            EnsureSharedSettingsFileSession();
            var state = new MainRecordingSettingsState
            {
                playMode = isPlaying ? MainRecordingSettingsState.Playing : MainRecordingSettingsState.Stopped
            };
            FBXVmdPipeline pipeline = recordingFBXVmdPipeline;
            if (isPlaying && pipeline != null)
            {
                state.hasPreparedMotion = pipeline.HasPreparedImportedMotion;
                state.isPlayingMotion = pipeline.IsImportedMotionPlaying;
                state.isPreparingMotion = pipeline.IsPreparingImportedMotionCorrection;
                state.isRecording = pipeline.IsImportedMotionRecording;
                state.motionTimeSeconds = pipeline.ImportedMotionCurrentTimeSeconds;
                state.motionError = pipeline.SessionState == FBXVmdPipeline.FBXSessionState.Failed ? pipeline.LastSessionMessage : string.Empty;
                state.isSurfaceCorrectionEnabled = pipeline.IsNativeSkinningCorrectionEnabled;
                state.surfaceCorrectionFailure = pipeline.LastNativeSkinningCorrectionFailure;
            }
            sharedSettingsFileSession.WriteMotionState(state);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[RecordingSetting] 모션 상태 기록 실패: {exception.Message}");
        }
    }

    private MainRecordingSettingsActionResult WriteRuntimePlayModeState(string playMode)
    {
        try
        {
            EnsureSharedSettingsFileSession();
            sharedSettingsFileSession.WriteRuntimePlayModeState(playMode);
            return MainRecordingSettingsActionResult.Success("Play Mode 상태를 기록했습니다.");
        }
        catch (Exception exception)
        {
            return MainRecordingSettingsActionResult.Failure($"Play Mode 상태 기록에 실패했습니다: {exception.Message}");
        }
    }

    public MainRecordingSettingsActionResult ApplySharedSettingsDocument(
        MainRecordingSettingsDocument document,
        FBXVmdPipeline fileManager = null,
        bool startFbxImport = true,
        bool clearPendingCommandWhenSkipped = false)
    {
        if (document == null)
        {
            return MainRecordingSettingsActionResult.Failure("공유 설정 문서가 비어 있습니다.");
        }

        openSettingsPopupOnStart = document.openSettingsOnStart;
        RecordingCaptureResolutionPlan capturePlan = RecordingCaptureResolution.CreateCustomPlan(
            document.captureWidth,
            document.captureHeight);
        recordingCaptureQuality = RecordingCaptureQualityPreset.Custom;
        customRecordingCaptureWidth = capturePlan.Width;
        customRecordingCaptureHeight = capturePlan.Height;

        FBXVmdPipeline resolvedFBXVmdPipeline = fileManager != null ? fileManager : recordingFBXVmdPipeline;
        if (resolvedFBXVmdPipeline != null)
        {
            ApplyDiagnosticsToResolvedFBXVmdPipeline(resolvedFBXVmdPipeline);
        }

        if (TryProcessMotionCommand(
                document, resolvedFBXVmdPipeline, startFbxImport,
                clearPendingCommandWhenSkipped,
                out MainRecordingSettingsActionResult motionResult))
        {
            return motionResult;
        }

        Func<string, bool> tryStartFbxImport = resolvedFBXVmdPipeline == null
            ? null
            : path => sharedSettingsFbxImportStarter(resolvedFBXVmdPipeline, path);
        if (sharedSettingsFbxImportCommandProcessor.TryProcess(
                document,
                startFbxImport,
                clearPendingCommandWhenSkipped,
                File.Exists,
                tryStartFbxImport,
                PersistConsumedSharedSettingsDocumentQuietly,
                out MainRecordingSettingsActionResult commandResult))
        {
            return commandResult;
        }

        return MainRecordingSettingsActionResult.Success("공유 설정을 적용했습니다.");
    }

    bool TryProcessMotionCommand(
        MainRecordingSettingsDocument document,
        FBXVmdPipeline resolvedFBXVmdPipeline,
        bool startFbxImport,
        bool clearPendingCommandWhenSkipped,
        out MainRecordingSettingsActionResult motionResult)
    {
        Func<string, bool> tryExecuteMotion = resolvedFBXVmdPipeline == null
            ? null
            : action => action switch
            {
                MainRecordingSettingsCommandEnvelope.PlayImportedMotionAction => resolvedFBXVmdPipeline.TryPlayImportedMotion(),
                MainRecordingSettingsCommandEnvelope.PauseImportedMotionAction => resolvedFBXVmdPipeline.TryPauseImportedMotion(),
                MainRecordingSettingsCommandEnvelope.StopImportedMotionAction => resolvedFBXVmdPipeline.TryStopImportedMotion(),
                MainRecordingSettingsCommandEnvelope.EnableNativeSkinningCorrectionAction => resolvedFBXVmdPipeline.TrySetNativeSkinningCorrectionEnabled(true),
                MainRecordingSettingsCommandEnvelope.DisableNativeSkinningCorrectionAction => resolvedFBXVmdPipeline.TrySetNativeSkinningCorrectionEnabled(false),
                _ => false,
            };
        return sharedSettingsMotionCommandProcessor.TryProcess(
            document, startFbxImport, clearPendingCommandWhenSkipped,
            resolvedFBXVmdPipeline != null && resolvedFBXVmdPipeline.IsImportedMotionRecording,
            tryExecuteMotion, PersistConsumedSharedSettingsDocumentQuietly,
            out motionResult);
    }

    private void PersistConsumedSharedSettingsDocumentQuietly(MainRecordingSettingsDocument document)
    {
        if (sharedSettingsFileSession == null)
        {
            return;
        }

        try
        {
            sharedSettingsFileSession.Save(document);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[RecordingSetting] 처리한 공유 설정 명령을 지우지 못했습니다: {exception.Message}");
        }
    }

    private void EnsureSharedSettingsFileSession()
    {
        if (sharedSettingsFileSession != null)
        {
            return;
        }

        sharedSettingsFileSession =
            new RecordingSharedSettingsFileSession(sharedSettingsFilePathOverride);
    }
}
