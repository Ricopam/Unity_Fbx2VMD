using UnityEngine;
using UnityEngine.Serialization;
using System;
using System.IO;
using System.Linq;
using Fbx2Vmd.Profiling;

namespace Fbx2Vmd.FBXImporter
{
    public partial class FBXVmdPipeline
{

        internal void ResetTargetStateAfterSession(bool recaptureGuardBaselines)
        {
            _conversionCoordinator?.RestoreMmdPostPoseCorrectionForRetarget();
            _idlePoseGuard?.Apply();
            FBXConversionCoordinator.RecaptureTargetGuardBaselines(
                targetCharacter,
                recaptureGuardBaselines);
        }

        internal void RestoreIdlePoseBeforeRetargetBaselines()
        {
            // 배치 검증 뒤 남을 수 있는 분리형 엄지 helper 위치를 다음 기준 캡처 전에 복원함.
            _idlePoseGuard?.Apply();
#if UNITY_EDITOR
            if (EditorDiagnosticSession.IsRecordingOverrideActive)
            {
                LogEditorSmokeThumbState("prepare-target-after-idle-restore");
            }
#endif
        }

        /// <summary>
        /// Legacy reflection entry point for FBXVmdPipelineEditorSmokePathTests.
        /// </summary>
        internal static bool ShouldApplyTargetIdlePoseGuardThisFrame(bool isProcessing, bool hasActiveRetargeter)
        {
            return !isProcessing && !hasActiveRetargeter;
        }

        private bool EffectiveDetachedThumbBaseHelperPositionSync
        {
            get
            {
                return syncDetachedThumbBaseHelperPositions &&
                    syncDetachedThumbBaseHelpers &&
                    detachedThumbBaseHelperSyncWeight > 0f;
            }
        }

        internal void FailSession(string message, Exception exception = null)
        {
            string exceptionDetails = exception == null ? string.Empty : $"\n{exception}";
            Debug.LogError($"[FBXImport] FBX 처리 실패함. 메시지={message}{exceptionDetails}");

            SetSessionState(FBXSessionState.Failed, message, 0f, shouldLog: false);
            _recordingController?.ClearActiveRecordingSubscription();
            CleanupHumanoidMotionPlayback();
            CleanupActiveGhost();
            ResetTargetStateAfterSession(recaptureGuardBaselines: false);
#if UNITY_EDITOR
            NotifyEditorSmokeFinished(VmdSaveResult.Fail("", message));
            ClearEditorSmokeOverride();
#endif
            _isProcessing = false;
        }

        internal void LogRetargetPlaybackStabilitySummary()
        {
            RetargetingDiagnosticReporter.LogPlaybackStability(_activeRetargeter);
        }

#if UNITY_EDITOR
        internal void LogEditorSmokeThumbState(string stage)
        {
            if (!EditorDiagnosticSession.IsRecordingOverrideActive || targetCharacter == null)
            {
                return;
            }

            RetargetingDiagnosticReporter.LogEditorSmokeThumbState(
                stage,
                EditorDiagnosticSession.CurrentFbxFileName,
                EditorDiagnosticSession.Segment,
                EffectiveThumbProjectionMinPalmNormal,
                targetCharacter,
                _activeRetargeter);
        }
#endif

        internal void SetSessionState(FBXSessionState state, string message, float progress, bool shouldLog = true)
        {
            _sessionState = state;
            PipelineRunProfiler.NoteStage(
                state.ToString(),
                message,
                state == FBXSessionState.Success
                || state == FBXSessionState.Failed
                || state == FBXSessionState.Cancelled);
            LastSessionMessage = message ?? string.Empty;
            if (shouldLog)
            {
                Debug.Log($"[FBXImport] 상태 변경됨. 상태={state}, 메시지={message}");
            }

            ApplyRecorderSessionState(state, message, progress);
        }

        private void ApplyRecorderSessionState(
            FBXSessionState state,
            string message,
            float progress)
        {
            HumanoidSampleCode recorder = GetRecorderController();
            if (recorder == null)
            {
                return;
            }

            switch (state)
            {
                case FBXSessionState.Failed:
                    recorder.SetError(message);
                    break;
                case FBXSessionState.Cancelled:
                case FBXSessionState.Ready:
                case FBXSessionState.PreviewPaused:
                    recorder.SetReady(message);
                    break;
                case FBXSessionState.Success:
                    recorder.SetProcessingStatus(message, 1f);
                    break;
                default:
                    recorder.SetProcessingStatus(message, progress);
                    break;
            }
        }

        private HumanoidSampleCode GetRecorderController()
        {
            return targetCharacter != null ? targetCharacter.GetComponent<HumanoidSampleCode>() : null;
        }

        private void TickHumanoidMotionPlayback(float deltaTimeSeconds)
        {
            if (_humanoidMotionPlaybackController == null)
            {
                return;
            }

            HumanoidMotionPlaybackState previousState =
                _humanoidMotionPlaybackController.State;
            bool completedRecording = TickRecordingOrPlayback(deltaTimeSeconds);
            if (completedRecording ||
                (previousState == HumanoidMotionPlaybackState.Playing &&
                 _humanoidMotionPlaybackController.State == HumanoidMotionPlaybackState.Ready))
            {
                string outputPath = _humanoidMotionRecordingController?.OutputFilePath;
                SetSessionState(
                    FBXSessionState.Ready,
                    completedRecording && !string.IsNullOrWhiteSpace(outputPath)
                        ? $"FBX 모션 영상 저장 완료: {outputPath}"
                        : "FBX 모션 재생 완료",
                    1f);
            }
        }

        private bool TickRecordingOrPlayback(float deltaTimeSeconds)
        {
            if (_humanoidMotionRecordingController?.IsRecording == true)
            {
                // 녹화 시작 프레임에는 deltaTime이 갱신 전일 수 있어 현재 정지 상태도 확인함.
                bool completedRecording = _humanoidMotionRecordingController.Tick(
                    Time.timeScale > 0f ? Time.deltaTime : 0f);
                if (!completedRecording)
                {
                    UpdateRecordingSessionProgress();
                }
                return completedRecording;
            }

            _humanoidMotionPlaybackController.Tick(deltaTimeSeconds);
            return false;
        }

        // 프레임 정확도 녹화는 실시간보다 느리게 보이므로 진행률을 주기 갱신해
        // 멈춘 것처럼 보이지 않게 함. 갱신 빈도를 제한해 매 틱 UI 호출 비용을 피함.
        private void UpdateRecordingSessionProgress()
        {
            if (Time.unscaledTime < _recordingProgressHoldUntilUnscaledTime ||
                Time.unscaledTime - _lastRecordingProgressUpdateTime < 0.25f)
            {
                return;
            }

            _lastRecordingProgressUpdateTime = Time.unscaledTime;
            SetSessionState(
                FBXSessionState.Recording,
                "FBX 모션 영상 녹화 중",
                _humanoidMotionRecordingController.NormalizedRecordingProgress,
                shouldLog: false);
        }

        private bool DispatchPendingPlaybackRequest()
        {
            if (TryHandleSkinningDriverFault())
            {
                return true;
            }
            if (!_nativeSkinningCorrectionPlaybackDriver.IsReady)
            {
                return true;
            }

            NativeSkinningPendingPlaybackRequest request =
                ConsumePendingPlaybackRequest();
            bool started = StartPendingPlaybackRequest(request);
            if (!started)
            {
                SetSessionState(
                    FBXSessionState.Failed,
                    "표면 보정 완료 후 FBX 모션을 시작하지 못했습니다.",
                    0f);
            }
            return true;
        }

        private bool TryHandleSkinningDriverFault()
        {
            if (!_nativeSkinningCorrectionPlaybackDriver.IsFaulted)
            {
                return false;
            }

            string failureMessage =
                _nativeSkinningCorrectionPlaybackDriver.FailureMessage;
            _pendingNativeSkinningPlaybackRequest =
                NativeSkinningPendingPlaybackRequest.None;
            SetSessionState(FBXSessionState.Failed, failureMessage, 0f);
            return true;
        }

        private NativeSkinningPendingPlaybackRequest ConsumePendingPlaybackRequest()
        {
            NativeSkinningPendingPlaybackRequest request =
                _pendingNativeSkinningPlaybackRequest;
            _pendingNativeSkinningPlaybackRequest =
                NativeSkinningPendingPlaybackRequest.None;
            return request;
        }

        private bool StartPendingPlaybackRequest(
            NativeSkinningPendingPlaybackRequest request)
        {
            return request == NativeSkinningPendingPlaybackRequest.Record
#if UNITY_EDITOR
                ? TryStartImportedMotionRecordingNow()
#else
                ? false
#endif
                : TryStartImportedMotionPlaybackNow();
        }

        private void DisableIndependentRecorderAutoStart()
        {
            // 파이프라인이 자동 녹화 수명주기를 소유하므로 recorder의 독립 Start() 녹화를 차단함.
            HumanoidSampleCode recorder = GetRecorderController();
            if (recorder != null)
            {
                recorder.AutoStartRecording = false;
            }
        }

        internal void CleanupActiveGhost()
        {
            if (_activeGhostContainer == null)
            {
                _activeRetargeter = null;
                return;
            }

            bool destroyImmediately = false;
#if UNITY_EDITOR
            // Diagnostic smokes chain multiple sessions in one play session. Deferred
            // destroy leaves the previous ghost alive until end-of-frame, which can
            // contaminate the next session's thumb/helper baselines.
            destroyImmediately = EditorDiagnosticSession.IsRecordingOverrideActive;
#endif

            if (destroyImmediately)
            {
                DestroyImmediate(_activeGhostContainer);
            }
            else
            {
                Destroy(_activeGhostContainer);
            }
            _activeGhostContainer = null;
            _activeRetargeter = null;
        }

    }
}
