using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 에디터 smoke 진단 세션의 시작·배치 진행을 담당함. 파이프라인 런타임 코드에서 분리됨.
    internal static class EditorDiagnosticSmokeDriver
    {
        internal static bool StartEditorDiagnosticSmoke(
            FBXVmdPipeline pipeline,
            string fbxFileName,
            float durationSeconds,
            int targetFrameCount,
            bool enableDiagnostics,
            bool enableFingerCloseups,
            bool useDeterministicCaptureFramerate,
            float diagnosticStartDelay,
            FBXVmdPipeline.EditorDiagnosticSmokeSegment segment =
                FBXVmdPipeline.EditorDiagnosticSmokeSegment.Head,
            float[] sampleTimesOverride = null,
            int captureWidthOverride = 0,
            int captureHeightOverride = 0,
            float diagnosticScreenshotPaddingOverride = float.NaN,
            float diagnosticScreenshotVerticalViewportCenterOverride = float.NaN,
            float recordingStartTimeOverrideSeconds = float.NaN,
            float recordingPlaybackSpeedOverride = float.NaN,
            bool useInputBaseName = false)
        {
            // 녹화 중 시작하면 변환 가드에 막혀 진단 세션과 덮어쓴 설정이 복원되지 않고 남음.
            if (pipeline.IsImportedMotionRecording)
            {
                Debug.LogWarning("[FBXImport] 영상 녹화 중이라 smoke 진단을 시작하지 않았습니다.");
                return false;
            }

            if (pipeline._isProcessing)
            {
                Debug.LogWarning("[FBXImport] 다른 FBX 처리가 진행 중이라 smoke 진단을 시작하지 않았습니다.");
                return false;
            }

            if (!pipeline.ShouldRecordVmdAfterImport)
            {
                Debug.LogWarning(
                    "[FBXImport] VMD 자동 변환이 꺼져 있어 VMD 산출물이 필요한 smoke 진단을 시작하지 않았습니다. " +
                    "FBXVmdPipeline 인스펙터의 VMD 녹화에서 자동 변환을 켜세요.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(fbxFileName))
            {
                Debug.LogError("[FBXImport] smoke 진단 FBX 파일명이 비어 있습니다.");
                return false;
            }

            string sourcePath = FBXEditorDiagnosticPlanner.ResolveFbxPath(
                fbxFileName,
                FBXImportController.GetControlledImportDirectory(),
                Application.dataPath,
                File.Exists);
            if (!File.Exists(sourcePath))
            {
                Debug.LogError($"[FBXImport] smoke 진단 FBX를 찾을 수 없습니다: {sourcePath}");
                return false;
            }

            if (!TargetRendererIsolationValidator.TryValidateLoadedObjects(
                    pipeline.targetCharacter,
                    out string rendererIsolationFailure))
            {
                Debug.LogError($"[FBXImport] {rendererIsolationFailure}");
                return false;
            }

            float safeDuration = Mathf.Max(0.1f, durationSeconds);
            int safeTargetFrameCount = targetFrameCount > 0
                ? targetFrameCount
                : Mathf.CeilToInt(safeDuration * FBXVmdPipeline.EDITOR_DIAGNOSTIC_SMOKE_FRAME_RATE);

            pipeline.CaptureEditorSmokeSettings();
            pipeline.enableRecordingDiagnostics = enableDiagnostics;
            pipeline.enableDiagnosticFingerCloseups = enableFingerCloseups;
            pipeline.useDeterministicCaptureFramerateForDiagnostics = useDeterministicCaptureFramerate;
            pipeline.startDelay = Mathf.Clamp(diagnosticStartDelay, 0f, 10f);
            pipeline.showGhostModel = false;

            float[] clonedSampleTimes =
                FBXEditorDiagnosticPlanner.CloneSampleTimes(sampleTimesOverride);
            bool hasCaptureResolutionOverride =
                FBXEditorDiagnosticPlanner.TryBuildCaptureResolutionOverride(
                captureWidthOverride,
                captureHeightOverride,
                out int captureWidth,
                out int captureHeight);
            float screenshotPaddingOverride =
                FBXEditorDiagnosticPlanner.NormalizeScreenshotPaddingOverride(
                    diagnosticScreenshotPaddingOverride);
            float screenshotVerticalViewportCenterOverride =
                FBXEditorDiagnosticPlanner.NormalizeScreenshotVerticalViewportCenterOverride(
                    diagnosticScreenshotVerticalViewportCenterOverride);
            bool useKnownReferenceTiming =
                FBXEditorDiagnosticPlanner.ShouldUseKnownReferenceTiming(
                Path.GetFileNameWithoutExtension(sourcePath),
                safeDuration,
                safeTargetFrameCount,
                FBXVmdPipeline.EDITOR_DIAGNOSTIC_SMOKE_FRAME_RATE,
                pipeline.useKnownMmdReferenceTiming);
            float startTimeOverride =
                FBXEditorDiagnosticPlanner.NormalizeStartTimeOverride(
                    recordingStartTimeOverrideSeconds);
            float playbackSpeedOverride =
                FBXEditorDiagnosticPlanner.NormalizePlaybackSpeedOverride(
                    recordingPlaybackSpeedOverride);
            pipeline.EditorDiagnosticSession.Begin(new FBXEditorDiagnosticSession.Plan
            {
                DurationSeconds = safeDuration,
                TargetFrameCount = safeTargetFrameCount,
                SampleTimesOverride = clonedSampleTimes,
                Segment = segment,
                CurrentFbxFileName = Path.GetFileName(sourcePath),
                HasCaptureResolutionOverride = hasCaptureResolutionOverride,
                CaptureWidth = captureWidth,
                CaptureHeight = captureHeight,
                DiagnosticScreenshotPaddingOverride = screenshotPaddingOverride,
                DiagnosticScreenshotVerticalViewportCenterOverride =
                    screenshotVerticalViewportCenterOverride,
                UseKnownReferenceTiming = useKnownReferenceTiming,
                UseInputBaseName = useInputBaseName,
                RecordingStartTimeOverrideSeconds = startTimeOverride,
                RecordingPlaybackSpeedOverride = playbackSpeedOverride
            });

            Debug.Log(
                $"[FBXImport] Editor smoke 진단 시작: FBX={Path.GetFileName(sourcePath)}, " +
                $"duration={safeDuration:F2}s, targetFrameCount={safeTargetFrameCount}, " +
                $"segment={FBXEditorDiagnosticPlanner.GetSegmentLabel(segment)}, diagnostics={enableDiagnostics}");
            pipeline.LogEditorSmokeThumbState("smoke-start-before-process");
            if (pipeline.EditorDiagnosticSession.HasCaptureResolutionOverride)
            {
                Debug.Log(
                    $"[FBXImport] Editor smoke capture override: " +
                    $"{pipeline.EditorDiagnosticSession.CaptureWidth}x{pipeline.EditorDiagnosticSession.CaptureHeight}");
            }

            pipeline.ProcessFBXAsync(sourcePath);
            return true;
        }

        internal static void ScheduleEditorDiagnosticBatchAdvance(
            FBXVmdPipeline pipeline, Action continuation)
        {
            if (pipeline._editorDiagnosticBatchAdvanceCoroutine != null)
            {
                pipeline.StopCoroutine(pipeline._editorDiagnosticBatchAdvanceCoroutine);
                pipeline._editorDiagnosticBatchAdvanceCoroutine = null;
            }

            if (!Application.isPlaying || !pipeline.isActiveAndEnabled)
            {
                pipeline.ResetTargetStateAfterSession(recaptureGuardBaselines: false);
                continuation?.Invoke();
                return;
            }

            pipeline._editorDiagnosticBatchAdvanceCoroutine =
                pipeline.StartCoroutine(EditorDiagnosticBatchAdvanceRoutine(pipeline, continuation));
        }

        private static IEnumerator EditorDiagnosticBatchAdvanceRoutine(
            FBXVmdPipeline pipeline, Action continuation)
        {
            Debug.Log("[FBXImport] Editor smoke batch advance reset: target idle 상태를 다음 FBX 시작 전 다시 고정합니다.");
            pipeline.LogEditorSmokeThumbState("batch-advance-before-reset");
            pipeline.ResetTargetStateAfterSession(recaptureGuardBaselines: false);
            yield return null;
            pipeline.ResetTargetStateAfterSession(recaptureGuardBaselines: false);
            yield return null;
            pipeline.ResetTargetStateAfterSession(recaptureGuardBaselines: false);
            pipeline.LogEditorSmokeThumbState("batch-advance-after-reset");
            pipeline._editorDiagnosticBatchAdvanceCoroutine = null;
            continuation?.Invoke();
        }
    }
}
