using UnityEngine;
using UnityEngine.Serialization;
using System;
using System.IO;
using System.Linq;
using Fbx2Vmd.FileSystem;
using Fbx2Vmd.Recording;
using Fbx2Vmd.Character;
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

        /// Legacy reflection entry point for FBXVmdPipelineEditorSmokePathTests.
        /// </summary>
        internal static bool ShouldApplyTargetIdlePoseGuardThisFrame(bool isProcessing, bool hasActiveRetargeter)
        {
            return !isProcessing && !hasActiveRetargeter;
        }

        private HumanoidThumbDeformationGuardOptions CreateThumbDeformationGuardOptions()
        {
            return new HumanoidThumbDeformationGuardOptions
            {
                ProximalMaxLocalAngle = EffectiveThumbProximalMaxLocalAngle,
                IntermediateMaxLocalAngle = ThumbIntermediateMaxLocalAngle,
                DistalMaxLocalAngle = ThumbDistalMaxLocalAngle,
                ProximalRotationOffset = ThumbRotationOffset,
                MirrorRightProximalRotationOffset = mirrorRightThumbRotationOffset,
                LeftProximalRotationOffset = LeftThumbRotationOffset,
                RightProximalRotationOffset = RightThumbRotationOffset,
                LogCorrections = logThumbLocalRotationGuardCorrections,
                ClampHumanoidThumbRotations = EffectiveThumbLocalRotationGuard,
                SyncDetachedBaseHelpers =
                    syncDetachedThumbBaseHelpers && detachedThumbBaseHelperSyncWeight > 0f,
                SyncDetachedBaseHelperPositions = EffectiveDetachedThumbBaseHelperPositionSync,
                DetachedBaseHelperSyncWeight = detachedThumbBaseHelperSyncWeight,
                DetachedBaseHelperMaxLocalAngle = detachedThumbBaseHelperMaxLocalAngle,
                DetachedBaseHelperMaxPositionOffset = detachedThumbBaseHelperMaxPositionOffset,
                LeftDetachedBaseHelperDeltaAxisOffset = LeftDetachedThumbBaseHelperDeltaAxisOffset,
                RightDetachedBaseHelperDeltaAxisOffset = RightDetachedThumbBaseHelperDeltaAxisOffset,
                LeftDetachedBaseHelperTargetRotationOffset = LeftDetachedThumbBaseHelperTargetRotationOffset,
                RightDetachedBaseHelperTargetRotationOffset = RightDetachedThumbBaseHelperTargetRotationOffset,
                StabilizeDetachedBasePalm =
                    stabilizeDetachedThumbBasePalm && detachedThumbBasePalmStabilizeWeight > 0f,
                DetachedBasePalmStabilizeWeight = detachedThumbBasePalmStabilizeWeight,
                DetachedBasePalmMaxLocalAngle = detachedThumbBasePalmMaxLocalAngle,
                EnableVisualLengthGuard = enableThumbVisualLengthGuard,
                ProjectionMinPalmNormal = EffectiveThumbProjectionMinPalmNormal,
                ProjectionMaxPalmNormal = ThumbProjectionMaxPalmNormal,
                ProjectionGuardWeight = ThumbProjectionGuardWeight,
                IndexMaxSpreadAngle = ThumbIndexMaxSpreadAngle,
                IndexSpreadGuardWeight = ThumbIndexSpreadGuardWeight,
                MaxSegmentBendAngle = ThumbMaxSegmentBendAngle,
                SegmentStraightenWeight = ThumbSegmentStraightenWeight,
                SuppressPoseShapingWithManualReference = PreserveManualThumbPoseWithReference,
                StabilizeWebbingCrease =
                    stabilizeThumbWebbingCrease && thumbWebbingCreaseStabilizeWeight > 0f,
                WebbingCreaseStabilizeWeight = thumbWebbingCreaseStabilizeWeight,
                WebbingCreaseMaxLocalAngle = thumbWebbingCreaseMaxLocalAngle,
                WebbingCreaseMaxPositionOffset = thumbWebbingCreaseMaxPositionOffset
            };
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
            bool completedRecording = false;
            if (_humanoidMotionRecordingController?.IsRecording == true)
            {
                // 녹화 시작 프레임에는 deltaTime이 갱신 전일 수 있어 현재 정지 상태도 확인함.
                completedRecording = _humanoidMotionRecordingController.Tick(
                    Time.timeScale > 0f ? Time.deltaTime : 0f);
                if (!completedRecording)
                {
                    UpdateRecordingSessionProgress();
                }
            }
            else
            {
                _humanoidMotionPlaybackController.Tick(deltaTimeSeconds);
            }
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

        private bool TryPrepareNativeSkinningCorrection(
            NativeSkinningPendingPlaybackRequest request,
            out bool shouldStartImmediately)
        {
            shouldStartImmediately = false;
            if (!IsNativeSkinningCorrectionEnabled)
            {
                shouldStartImmediately = request == NativeSkinningPendingPlaybackRequest.Play;
                if (!shouldStartImmediately)
                {
                    SetSessionState(FBXSessionState.Ready, "비교 재생 상태입니다. 녹화하려면 표면 보정을 켜 주세요.", 0f);
                }
                return shouldStartImmediately;
            }
            if (!string.IsNullOrWhiteSpace(_nativeSkinningCorrectionSetupError))
            {
                SetSessionState(
                    FBXSessionState.Failed,
                    _nativeSkinningCorrectionSetupError,
                    0f);
                return false;
            }
            if (_nativeSkinningCorrectionPlaybackDriver == null ||
                _nativeSkinningCorrectionPlaybackDriver.IsReady)
            {
                shouldStartImmediately = true;
                return true;
            }
            if (_nativeSkinningCorrectionPlaybackDriver.IsFaulted)
            {
                SetSessionState(
                    FBXSessionState.Failed,
                    _nativeSkinningCorrectionPlaybackDriver.FailureMessage,
                    0f);
                return false;
            }
            if (_nativeSkinningCorrectionPlaybackDriver.IsPreparing)
            {
                return _pendingNativeSkinningPlaybackRequest == request;
            }
            if (!_nativeSkinningCorrectionPlaybackDriver.BeginPreparation())
            {
                SetSessionState(
                    FBXSessionState.Failed,
                    _nativeSkinningCorrectionPlaybackDriver.FailureMessage,
                    0f);
                return false;
            }

            _pendingNativeSkinningPlaybackRequest = request;
            SetSessionState(
                FBXSessionState.Retargeting,
                "FBX 팔 표면 보정 준비 중",
                0f);
            return true;
        }

        private bool TickNativeSkinningCorrectionPreparation()
        {
            using var perfScope = PerfScope.Measure("FBXVmdPipeline.TickNativeSkinningCorrectionPreparation");
            if (_nativeSkinningCorrectionPlaybackDriver == null)
            {
                return false;
            }
            if (_nativeSkinningCorrectionPlaybackDriver.IsPreparing)
            {
                return HandlePreparationInProgress();
            }
            if (_pendingNativeSkinningPlaybackRequest ==
                NativeSkinningPendingPlaybackRequest.None)
            {
                return false;
            }
            return DispatchPendingPlaybackRequest();
        }

        private bool HandlePreparationInProgress()
        {
            // 방어적 불변식: 정상 호출 경로에선 녹화 중 재준비가 성립하지 않지만,
            // 성립하면 재생 틱만 스킵된 채 Recorder가 렌더러 숨김 상태를 계속
            // 캡처해 영상이 오염되므로 조용히 두지 않고 중단함.
            if (IsImportedMotionRecording)
            {
                Debug.LogWarning(
                    "[FBXImport] 영상 녹화 중 보정 재준비가 발생해 녹화를 중단합니다.");
                TryStopImportedMotionRecording();
            }
            SetSessionState(
                FBXSessionState.Retargeting,
                $"FBX 팔 표면 보정 준비 중: " +
                $"{ImportedMotionCorrectionProcessedFrameCount} / " +
                $"{ImportedMotionCorrectionTotalFrameCount}",
                ImportedMotionCorrectionPreparationProgress,
                shouldLog: false);
            return true;
        }

        private bool DispatchPendingPlaybackRequest()
        {
            if (_nativeSkinningCorrectionPlaybackDriver.IsFaulted)
            {
                string failureMessage =
                    _nativeSkinningCorrectionPlaybackDriver.FailureMessage;
                _pendingNativeSkinningPlaybackRequest =
                    NativeSkinningPendingPlaybackRequest.None;
                SetSessionState(FBXSessionState.Failed, failureMessage, 0f);
                return true;
            }
            if (!_nativeSkinningCorrectionPlaybackDriver.IsReady)
            {
                return true;
            }

            NativeSkinningPendingPlaybackRequest request =
                _pendingNativeSkinningPlaybackRequest;
            _pendingNativeSkinningPlaybackRequest =
                NativeSkinningPendingPlaybackRequest.None;
            bool started = request == NativeSkinningPendingPlaybackRequest.Record
#if UNITY_EDITOR
                ? TryStartImportedMotionRecordingNow()
#else
                ? false
#endif
                : TryStartImportedMotionPlaybackNow();
            if (!started)
            {
                SetSessionState(
                    FBXSessionState.Failed,
                    "표면 보정 완료 후 FBX 모션을 시작하지 못했습니다.",
                    0f);
            }
            return true;
        }

        private float ResolveHumanoidPlaybackProgress()
        {
            if (_humanoidMotionPlaybackController == null ||
                _humanoidMotionPlaybackController.ClipLengthSeconds <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(
                _humanoidMotionPlaybackController.CurrentTimeSeconds /
                _humanoidMotionPlaybackController.ClipLengthSeconds);
        }

        private bool TryValidateImportedMotionPoseCorrectionCompatibility(
            HumanoidPoseCorrectionDocument document,
            out string errorMessage)
        {
            if (document == null)
            {
                errorMessage = "불러온 Humanoid 보정 문서가 없습니다.";
                return false;
            }

            if (!string.Equals(
                    document.MotionName,
                    _preparedMotionName,
                    StringComparison.OrdinalIgnoreCase))
            {
                errorMessage =
                    $"현재 모션과 보정 문서의 모션 이름이 다릅니다: " +
                    $"{_preparedMotionName} / {document.MotionName}";
                return false;
            }

            if (Mathf.Abs(
                    document.SourceFrameRate -
                    _humanoidMotionPlaybackController.ClipFrameRate) > 0.001f)
            {
                errorMessage =
                    $"현재 모션과 보정 문서의 프레임률이 다릅니다: " +
                    $"{_humanoidMotionPlaybackController.ClipFrameRate:F3} / " +
                    $"{document.SourceFrameRate:F3}";
                return false;
            }

            if (document.LastCorrectionFrameIndex >
                _humanoidMotionPlaybackController.LastFrameIndex)
            {
                errorMessage =
                    $"보정 문서의 마지막 프레임이 현재 모션 범위를 벗어납니다: " +
                    $"{document.LastCorrectionFrameIndex} / " +
                    $"{_humanoidMotionPlaybackController.LastFrameIndex}";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private void CleanupHumanoidMotionPlayback()
        {
            _nativeSkinningCorrectionPlaybackDriver?.Release();
            _nativeSkinningCorrectionPlaybackDriver = null;
            _pendingNativeSkinningPlaybackRequest =
                NativeSkinningPendingPlaybackRequest.None;
            _nativeSkinningCorrectionSetupError = string.Empty;
            _humanoidMotionRecordingController?.Dispose();
            _humanoidMotionRecordingController = null;
            _humanoidMotionPlaybackController?.Dispose();
            _poseCorrectionDocument = null;
            _preparedMotionName = string.Empty;
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


        private EditorManualPoseReferenceOptions CreateEditorManualPoseReferenceOptions()
        {
            return new EditorManualPoseReferenceOptions
            {
                ReferencePrefab = manualFingerReferencePrefab,
                ReferenceController = manualFingerReferenceController,
                ShouldUseFingerPoseReference = _shouldUseManualAnimatorFingerPoseReference,
                ShouldUseFullBodyPoseReference = _shouldUseManualAnimatorFullBodyPoseReference,
                FullBodyPoseReferenceWeight = manualAnimatorFullBodyPoseReferenceWeight,
                ShouldExcludeFullBodyLowerMuscles = _shouldExcludeManualAnimatorFullBodyLowerMuscles,
                ShouldApplyFullBodyLowerMusclesOnly = _shouldApplyManualAnimatorFullBodyLowerMusclesOnly,
                ShouldApplyFullBodyLegTwistMusclesOnly = _shouldApplyManualAnimatorFullBodyLegTwistMusclesOnly,
                ShouldApplyFullBodyRightArmMusclesOnly = manualAnimatorFullBodyPoseRightArmMusclesOnly,
                ShouldApplyFullBodyLeftArmMusclesOnly = manualAnimatorFullBodyPoseLeftArmMusclesOnly,
                ShouldApplyFullBodyRightSleeveChainMusclesOnly = manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly,
                FullBodyPoseFrameGateStart = manualAnimatorFullBodyPoseFrameGateStart,
                FullBodyPoseFrameGateEnd = manualAnimatorFullBodyPoseFrameGateEnd,
                ShouldUseHipsLocalPositionReference = _shouldUseManualAnimatorHipsLocalPositionReference,
                ShouldUseBodyRotationReference = _shouldUseManualAnimatorBodyRotationReference,
                ShouldUseHandLocalRotationReference = useManualAnimatorHandLocalRotationReference,
                ShouldUseFootLocalRotationReference = _shouldUseManualAnimatorFootLocalRotationReference,
                ShouldUseLowerBodySegmentDirectionReference = _shouldUseManualAnimatorLowerBodySegmentDirectionReference,
                ShouldUseFootHipsAlignedResidualYawReference = _shouldUseManualAnimatorFootHipsAlignedResidualYawReference,
                ShouldUsePostSetHumanPoseRightEndpointPositionReference = usePostSetHumanPoseRightEndpointPositionReference,
                ShouldUsePostSetHumanPoseRightFootEvaluatorXzReference = usePostSetHumanPoseRightFootEvaluatorXzReference,
                ShouldUsePreSetHumanPoseRightEndpointPositionReference = usePreSetHumanPoseRightEndpointPositionReference,
                ShouldUseBodyPositionXzReference = _shouldUseManualAnimatorBodyPositionXzReference,
                ShouldUseRightSleeveSilhouetteOffsetReference = useYybRightSleeveSilhouetteLocalOffsetReference,
                RightSleeveSilhouetteOffsetX = yybRightSleeveSilhouetteLocalOffsetX,
                RightSleeveSilhouetteFrameGateStart = yybRightSleeveSilhouetteLocalOffsetFrameGateStart,
                RightSleeveSilhouetteFrameGateEnd = yybRightSleeveSilhouetteLocalOffsetFrameGateEnd,
                ShouldUseBipedIkFootPositionReference = useManualAnimatorBipedIkFootPositionReference
            };
        }

    }
}
