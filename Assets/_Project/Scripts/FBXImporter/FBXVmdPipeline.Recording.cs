using UnityEngine;
using UnityEngine.Serialization;
using System;
using System.IO;
using System.Linq;

namespace Fbx2Vmd.FBXImporter
{
    public partial class FBXVmdPipeline
    {
        public bool TryStartImportedMotionRecording()
        {
#if UNITY_EDITOR
            // 녹화 중 재시작은 실패 로그로 Ready 상태가 잠시 표시되는 것을 막기 위해 선제 차단함.
            if (IsImportedMotionRecording)
            {
                return false;
            }

            if (_humanoidMotionRecordingController == null ||
                !TryPrepareNativeSkinningCorrection(
                    NativeSkinningPendingPlaybackRequest.Record,
                    out bool shouldStartImmediately))
            {
                return false;
            }

            return !shouldStartImmediately ||
                TryStartImportedMotionRecordingNow();
#else
            return false;
#endif
        }

#if UNITY_EDITOR
        private bool TryStartImportedMotionRecordingNow()
        {
            if (!TryResolveMotionVideoRangeSeconds(
                    out float rangeStartSeconds,
                    out float rangeEndSeconds,
                    out string rangeErrorMessage))
            {
                Debug.LogWarning($"[FBXImport] {rangeErrorMessage}");
                SetSessionState(FBXSessionState.Ready, rangeErrorMessage, 1f);
                return false;
            }

            // GameView 현재 크기로 기록해 녹화 중 화면 해상도가 바뀌지 않게 함.
            var settings = CreateMotionVideoRecordingSettings(
                rangeStartSeconds,
                rangeEndSeconds);
            if (!TryStartHumanoidRecording(settings))
            {
                return false;
            }

            SetSessionState(
                FBXSessionState.Recording,
                "FBX 모션 영상 녹화 중",
                0f);
            return true;
        }

        // 모션 프레임 기준 녹화 구간을 초 단위로 환산함. 구간 녹화가 꺼져 있으면
        // 선두 정지 스킵만 적용하고, 둘 다 아니면 클립 전체를 녹화함.
        private bool TryResolveMotionVideoRangeSeconds(
            out float startTimeSeconds,
            out float endTimeSeconds,
            out string errorMessage)
        {
            startTimeSeconds = 0f;
            endTimeSeconds = 0f;
            errorMessage = string.Empty;

            HumanoidMotionPlaybackController playback = _humanoidMotionPlaybackController;
            if (!TryGetClipTiming(
                    playback,
                    out float clipLength,
                    out float clipFrameRate,
                    out errorMessage))
            {
                return false;
            }

            return ResolveRecordingRange(
                playback,
                clipLength,
                clipFrameRate,
                ref startTimeSeconds,
                ref endTimeSeconds,
                ref errorMessage);
        }

        private bool TryGetClipTiming(
            HumanoidMotionPlaybackController playback,
            out float clipLength,
            out float clipFrameRate,
            out string errorMessage)
        {
            clipLength = playback?.ClipLengthSeconds ?? 0f;
            clipFrameRate = playback?.ClipFrameRate ?? 0f;
            if (clipLength <= 0f || clipFrameRate <= 0f)
            {
                errorMessage = "재생할 FBX 모션이 준비되지 않았습니다.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        private bool ResolveRecordingRange(
            HumanoidMotionPlaybackController playback,
            float clipLength,
            float clipFrameRate,
            ref float startTimeSeconds,
            ref float endTimeSeconds,
            ref string errorMessage)
        {
            if (!_motionVideoRangeEnabled)
            {
                ApplySkipLeadingStill(playback, ref startTimeSeconds);
                return true;
            }

            return TryResolveEnabledRangeSeconds(
                playback,
                clipLength,
                clipFrameRate,
                ref startTimeSeconds,
                ref endTimeSeconds,
                ref errorMessage);
        }

        private void ApplySkipLeadingStill(
            HumanoidMotionPlaybackController playback,
            ref float startTimeSeconds)
        {
            if (_motionVideoSkipLeadingStill &&
                (MotionVideoEditorHooks.TryFindMotionStartTime?.Invoke(
                    playback.PreparedClip,
                    out float stillEndSeconds) ?? false))
            {
                Debug.Log(
                    $"[FBXImport] 클립 선두 정지 구간을 건너뛰어 " +
                    $"{stillEndSeconds:F2}s부터 녹화합니다.");
                startTimeSeconds = stillEndSeconds;
            }
        }

        private bool TryResolveEnabledRangeSeconds(
            HumanoidMotionPlaybackController playback,
            float clipLength,
            float clipFrameRate,
            ref float startTimeSeconds,
            ref float endTimeSeconds,
            ref string errorMessage)
        {
            int lastFrameIndex = playback.LastFrameIndex;
            if (!ValidateRangeFrames(lastFrameIndex, out errorMessage))
            {
                return false;
            }

            startTimeSeconds = HumanoidMotionFrameCalculator.CalculateTimeSeconds(
                _motionVideoRangeStartFrame,
                clipLength,
                clipFrameRate);
            ResolveRangeEndSeconds(
                lastFrameIndex,
                clipLength,
                clipFrameRate,
                ref endTimeSeconds);

            return ValidateRangeOrder(
                startTimeSeconds,
                endTimeSeconds,
                out errorMessage);
        }

        private void ResolveRangeEndSeconds(
            int lastFrameIndex,
            float clipLength,
            float clipFrameRate,
            ref float endTimeSeconds)
        {
            // 종료 프레임이 0이거나 클립 범위를 넘으면 클립 끝까지 녹화함.
            if (_motionVideoRangeEndFrame > 0 &&
                _motionVideoRangeEndFrame <= lastFrameIndex)
            {
                endTimeSeconds = HumanoidMotionFrameCalculator.CalculateTimeSeconds(
                    _motionVideoRangeEndFrame,
                    clipLength,
                    clipFrameRate);
            }
        }

        private bool ValidateRangeOrder(
            float startTimeSeconds,
            float endTimeSeconds,
            out string errorMessage)
        {
            if (endTimeSeconds > 0f && startTimeSeconds >= endTimeSeconds)
            {
                errorMessage =
                    $"녹화 시작 프레임({_motionVideoRangeStartFrame})이 " +
                    $"종료 프레임({_motionVideoRangeEndFrame}) 이상입니다.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        private bool ValidateRangeFrames(
            int lastFrameIndex,
            out string errorMessage)
        {
            if (_motionVideoRangeStartFrame < 0 || _motionVideoRangeEndFrame < 0)
            {
                errorMessage = "녹화 구간 프레임은 0 이상이어야 합니다.";
                return false;
            }

            if (_motionVideoRangeStartFrame >= lastFrameIndex)
            {
                errorMessage =
                    $"녹화 시작 프레임({_motionVideoRangeStartFrame})이 " +
                    $"클립 마지막 프레임({lastFrameIndex}) 이상입니다.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        // 투명 배경(MOV/WebM) 녹화 중 숨길 오브젝트들의 렌더러를 수집함.
        private Renderer[] CollectAlphaHiddenRenderers()
        {
            if (_alphaRecordingHiddenObjects == null || _alphaRecordingHiddenObjects.Length == 0)
            {
                return null;
            }

            var renderers = new System.Collections.Generic.List<Renderer>();
            foreach (GameObject target in _alphaRecordingHiddenObjects)
            {
                if (target != null)
                {
                    renderers.AddRange(target.GetComponentsInChildren<Renderer>(true));
                }
            }

            return renderers.Count == 0 ? null : renderers.ToArray();
        }
#endif

        public bool TryStopImportedMotionRecording()
        {
            if (_humanoidMotionRecordingController == null ||
                !_humanoidMotionRecordingController.Stop())
            {
                return false;
            }

            string outputPath = _humanoidMotionRecordingController.OutputFilePath;
            SetSessionState(
                FBXSessionState.Ready,
                string.IsNullOrWhiteSpace(outputPath)
                    ? "FBX 모션 영상 녹화 종료"
                    : $"FBX 모션 영상 저장 완료: {outputPath}",
                1f);
            return true;
        }

#if UNITY_EDITOR
        private MotionVideoRecordingSettings CreateMotionVideoRecordingSettings(
            float rangeStartSeconds,
            float rangeEndSeconds)
        {
            return new MotionVideoRecordingSettings(
                _preparedMotionName,
                Screen.width,
                Screen.height,
                (float)_motionVideoFrameRate,
                outputDirectory: _motionVideoOutputFolder,
                format: _motionVideoFormat,
                startTimeSeconds: rangeStartSeconds,
                endTimeSeconds: rangeEndSeconds);
        }
#endif

#if UNITY_EDITOR
        private bool TryStartHumanoidRecording(MotionVideoRecordingSettings settings)
        {
            if (_humanoidMotionRecordingController.TryStart(
                    settings,
                    out string errorMessage))
            {
                return true;
            }

            Debug.LogWarning($"[FBXImport] {errorMessage}");
            SetSessionState(FBXSessionState.Ready, errorMessage, 1f);
            return false;
        }
#endif

        internal VmdSaveResult ApplyEditorSmokeThumbRiskFailure(VmdSaveResult result, MotionComparisonProbe probe)
        {
#if UNITY_EDITOR
            if (!EditorDiagnosticSession.IsRecordingOverrideActive ||
                !result.Success)
            {
                return result;
            }

            if (probe != null && probe.HasRendererIsolationFailure)
            {
                return BuildEditorSmokeFailureResult(
                    result,
                    probe.RendererIsolationFailureMessage);
            }

            if (!failEditorSmokeOnThumbRisk)
            {
                return result;
            }

            return ApplyThumbRiskEvaluation(result, probe);
#else
            return result;
#endif
        }

#if UNITY_EDITOR
        private VmdSaveResult ApplyThumbRiskEvaluation(
            VmdSaveResult result,
            MotionComparisonProbe probe)
        {
            FBXEditorDiagnosticRiskEvaluator.Evaluation evaluation =
                FBXEditorDiagnosticRiskEvaluator.Evaluate(
                    CreateEditorDiagnosticRiskInput(probe));
            if (evaluation.Outcome == FBXEditorDiagnosticRiskEvaluator.Outcome.Fail)
            {
                return BuildEditorSmokeFailureResult(result, evaluation.Message);
            }
            if (evaluation.Outcome == FBXEditorDiagnosticRiskEvaluator.Outcome.Warn)
            {
                Debug.LogWarning(
                    $"[FBXImport] Editor smoke diagnostic only: {evaluation.Message}");
            }

            return result;
        }
#endif

        private VmdSaveResult BuildEditorSmokeFailureResult(VmdSaveResult result, string errorMessage)
        {
            Debug.LogWarning($"[FBXImport] {errorMessage}");
            return new VmdSaveResult
            {
                Success = false,
                FilePath = result.FilePath,
                ErrorMessage = errorMessage,
                FrameCount = result.FrameCount,
                FileSizeBytes = result.FileSizeBytes
            };
        }

#if UNITY_EDITOR
        private FBXEditorDiagnosticRiskEvaluator.Input CreateEditorDiagnosticRiskInput(
            MotionComparisonProbe probe)
        {
            var input = new FBXEditorDiagnosticRiskEvaluator.Input
            {
                FbxName = EditorDiagnosticSession.CurrentFbxFileName,
                HasProbe = probe != null,
                MaxGenericThumbAnatomyRiskThreshold = editorSmokeMaxGenericThumbAnatomyRisk,
                MaxYybDeformationRiskThreshold = editorSmokeMaxYybDeformationRisk
            };
            if (probe == null)
            {
                return input;
            }

            CopyProbeRiskFields(input, probe);
            return input;
        }

#if UNITY_EDITOR
        private void CopyProbeRiskFields(
            FBXEditorDiagnosticRiskEvaluator.Input input,
            MotionComparisonProbe probe)
        {
            input.RiskDiagnosticsEnabled = probe.RiskDiagnosticsEnabled;
            input.RiskEvaluationFrameCount = probe.RiskEvaluationFrameCount;
            input.HasFullThumbAnatomyCoverage = probe.HasFullThumbAnatomyCoverage;
            input.HasResolvedThumbHelperCoverage = probe.HasResolvedThumbHelperCoverage;
            input.LeftThumbCoreAnatomyObserved = probe.LeftThumbCoreAnatomyObserved;
            input.RightThumbCoreAnatomyObserved = probe.RightThumbCoreAnatomyObserved;
            input.LeftThumbHelperCoverageRequired = probe.LeftThumbHelperCoverageRequired;
            input.RightThumbHelperCoverageRequired = probe.RightThumbHelperCoverageRequired;
            input.LeftThumbHelperCoverageSatisfied = probe.LeftThumbHelperCoverageSatisfied;
            input.RightThumbHelperCoverageSatisfied = probe.RightThumbHelperCoverageSatisfied;
            input.MaxGenericThumbAnatomyRisk = probe.MaxGenericThumbAnatomyRisk;
            input.MaxYybDeformationRisk = probe.MaxYybDeformationRisk;
            input.MaxThumbSpreadRisk = probe.MaxThumbSpreadRisk;
            input.MaxThumbProjectionRisk = probe.MaxThumbProjectionRisk;
            input.MaxThumbHelperSeparationRisk = probe.MaxThumbHelperSeparationRisk;
            input.MaxThumbWebbingRisk = probe.MaxThumbWebbingRisk;
            input.NonBlankScreenshotCount = probe.NonBlankScreenshotCount;
        }
#endif


    }
}
