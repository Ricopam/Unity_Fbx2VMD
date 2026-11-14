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

        public bool TryLoadImportedMotionPoseCorrections(
            string filePath,
            out string errorMessage)
        {
            if (IsImportedMotionRecording ||
                IsPreparingImportedMotionCorrection)
            {
                errorMessage = "재생 준비 또는 녹화 중에는 Humanoid 보정 문서를 불러올 수 없습니다.";
                return false;
            }

            if (_humanoidMotionPlaybackController == null ||
                !_humanoidMotionPlaybackController.IsPrepared)
            {
                errorMessage = "보정 문서를 적용할 Humanoid 모션이 준비되지 않았습니다.";
                return false;
            }

            if (!HumanoidPoseCorrectionFileStore.TryLoad(
                    filePath,
                    out HumanoidPoseCorrectionDocument loadedDocument,
                    out errorMessage) ||
                !TryValidateImportedMotionPoseCorrectionCompatibility(
                    loadedDocument,
                    out errorMessage))
            {
                return false;
            }

            if (!_humanoidMotionPlaybackController.TryPreviewPoseCorrection(
                    loadedDocument))
            {
                errorMessage = "현재 프레임에 Humanoid 보정 문서를 적용하지 못했습니다.";
                return false;
            }

            _poseCorrectionDocument = loadedDocument;
            _nativeSkinningCorrectionPlaybackDriver?.Invalidate();
            SetSessionState(
                FBXSessionState.PreviewPaused,
                $"FBX 자세 수정 문서 불러오기: {loadedDocument.FrameCount} 프레임",
                ResolveHumanoidPlaybackProgress());
            errorMessage = string.Empty;
            return true;
        }

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
            var settings = new MotionVideoRecordingSettings(
                _preparedMotionName,
                Screen.width,
                Screen.height,
                (float)_motionVideoFrameRate,
                outputDirectory: _motionVideoOutputFolder,
                format: _motionVideoFormat,
                startTimeSeconds: rangeStartSeconds,
                endTimeSeconds: rangeEndSeconds);
            if (!_humanoidMotionRecordingController.TryStart(
                    settings,
                    out string errorMessage))
            {
                Debug.LogWarning($"[FBXImport] {errorMessage}");
                SetSessionState(FBXSessionState.Ready, errorMessage, 1f);
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
            float clipLength = playback?.ClipLengthSeconds ?? 0f;
            float clipFrameRate = playback?.ClipFrameRate ?? 0f;
            if (clipLength <= 0f || clipFrameRate <= 0f)
            {
                errorMessage = "재생할 FBX 모션이 준비되지 않았습니다.";
                return false;
            }

            if (!_motionVideoRangeEnabled)
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

        private bool TryResolveEnabledRangeSeconds(
            HumanoidMotionPlaybackController playback,
            float clipLength,
            float clipFrameRate,
            ref float startTimeSeconds,
            ref float endTimeSeconds,
            ref string errorMessage)
        {
            if (_motionVideoRangeStartFrame < 0 || _motionVideoRangeEndFrame < 0)
            {
                errorMessage = "녹화 구간 프레임은 0 이상이어야 합니다.";
                return false;
            }

            int lastFrameIndex = playback.LastFrameIndex;
            if (_motionVideoRangeStartFrame >= lastFrameIndex)
            {
                errorMessage =
                    $"녹화 시작 프레임({_motionVideoRangeStartFrame})이 " +
                    $"클립 마지막 프레임({lastFrameIndex}) 이상입니다.";
                return false;
            }

            startTimeSeconds = HumanoidMotionFrameCalculator.CalculateTimeSeconds(
                _motionVideoRangeStartFrame,
                clipLength,
                clipFrameRate);
            // 종료 프레임이 0이거나 클립 범위를 넘으면 클립 끝까지 녹화함.
            if (_motionVideoRangeEndFrame > 0 &&
                _motionVideoRangeEndFrame <= lastFrameIndex)
            {
                endTimeSeconds = HumanoidMotionFrameCalculator.CalculateTimeSeconds(
                    _motionVideoRangeEndFrame,
                    clipLength,
                    clipFrameRate);
            }

            if (endTimeSeconds > 0f && startTimeSeconds >= endTimeSeconds)
            {
                errorMessage =
                    $"녹화 시작 프레임({_motionVideoRangeStartFrame})이 " +
                    $"종료 프레임({_motionVideoRangeEndFrame}) 이상입니다.";
                return false;
            }

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

        #region 파일 처리 로직
        internal async void ProcessFBXAsync(string sourcePath)
        {
            // 영상 녹화 중 임포트는 재생 컨트롤러를 해제해 Recorder가 정지 프레임만
            // 캡처하게 되므로 차단함.
            if (IsImportedMotionRecording)
            {
                SetSessionState(
                    FBXSessionState.Recording,
                    "영상 녹화 중에는 새 FBX를 불러올 수 없습니다.",
                    _humanoidMotionRecordingController.NormalizedRecordingProgress);
                // 진행률 갱신이 곧바로 거부 메시지를 덮지 않게 잠시 보류함.
                _recordingProgressHoldUntilUnscaledTime = Time.unscaledTime + 1f;
                return;
            }

            if (_isProcessing)
            {
                SetSessionState(FBXSessionState.LoadingFbx, "이미 FBX 처리 중입니다.", 0.1f);
                return;
            }

            EnsureServicesInitialized();
            await _conversionCoordinator.ConvertAsync(new FBXConversionRequest(sourcePath));
        }

        internal void BeginConversionSession(string sourceLabel = null)
        {
            PipelineRunProfiler.BeginRun(sourceLabel ?? string.Empty);
            EnsureServicesInitialized();
            CleanupHumanoidMotionPlayback();
            _idlePoseGuard?.Apply();
            _isProcessing = true;
            _recordingController.ClearActiveRecordingSubscription();
            CleanupActiveGhost();
        }

#if UNITY_EDITOR
        internal void PrepareEditorHumanoidPlayback(
            Animator targetAnimator,
            AnimationClip clip,
            string motionName,
            GameObject sourceModelAsset)
        {
            IsNativeSkinningCorrectionEnabled = true;
            LastNativeSkinningCorrectionFailure = string.Empty;
            CleanupActiveGhost();
            _nativeSkinningCorrectionPlaybackDriver?.Release();
            _pendingNativeSkinningPlaybackRequest =
                NativeSkinningPendingPlaybackRequest.None;
            _nativeSkinningCorrectionSetupError = string.Empty;
            InitializePlaybackController(
                targetAnimator,
                clip,
                sourceModelAsset);
            _poseCorrectionDocument = new HumanoidPoseCorrectionDocument(
                motionName,
                _humanoidMotionPlaybackController.ClipFrameRate);
            _humanoidMotionRecordingController?.Dispose();
            _humanoidMotionRecordingController = new HumanoidMotionRecordingController(
                _humanoidMotionPlaybackController,
                EditorMotionVideoRecorderFactory?.Invoke() ?? new NoOpMotionVideoRecorder(),
                CollectAlphaHiddenRenderers());
            _preparedMotionName = motionName;
            _isProcessing = false;
            if (!TryAttachSkinningDriverOrFail(targetAnimator))
            {
                return;
            }
            _nativeSkinningCorrectionPlaybackDriver?.ConfigureCacheIdentity(
                clip,
                motionName,
                sourceModelAsset);
            SetSessionState(
                FBXSessionState.Ready,
                $"FBX 임포트 완료 · 재생 대기: {motionName}",
                1f);
            PlaybackControlsEnsureRequested?.Invoke(this);
        }
#endif

        private void InitializePlaybackController(
            Animator targetAnimator,
            AnimationClip clip,
            GameObject referenceModel)
        {
            _humanoidMotionPlaybackController ??=
                new HumanoidMotionPlaybackController();
            if (referenceModel == null)
            {
                _humanoidMotionPlaybackController.Prepare(targetAnimator, clip);
            }
            else
            {
                _humanoidMotionPlaybackController.PrepareWithArmDirectionReference(
                    targetAnimator,
                    clip,
                    referenceModel);
            }
            _humanoidMotionPlaybackController.SetGroundResponseEnabled(true);
            // goal IK 경로 기본 활성 — 발 모임 등 근육 리타겟 왜곡을 소스 목표로 직접 구동해 차단한다.
            _humanoidMotionPlaybackController.SetIkGoalFootDrive(true, 1f);
            // 정지 발 순간이동 확산 기본 활성 — 소스 클립의 단발 불연속이 지지 발을
            // 순간이동시키는 스냅을 수 프레임에 걸쳐 완화한다.
            _humanoidMotionPlaybackController.SetFootStepDiffusion(true);
            _humanoidMotionPlaybackController.PrepareSupportCurves(targetAnimator);
        }

        private bool TryAttachSkinningDriverOrFail(Animator targetAnimator)
        {
            if (NativeSkinningCorrectionPlaybackDriver.TryAttach(
                    gameObject,
                    targetAnimator,
                    _humanoidMotionPlaybackController,
                    out _nativeSkinningCorrectionPlaybackDriver,
                    out _nativeSkinningCorrectionSetupError))
            {
                return true;
            }
            SetSessionState(
                FBXSessionState.Failed,
                _nativeSkinningCorrectionSetupError,
                0f);
            PlaybackControlsEnsureRequested?.Invoke(this);
            return false;
        }

        /// <summary>
        /// 빌드 환경의 Humanoid 재생 세션 준비 — 에디터 경로와 같은 보정 스택을 쓰되
        /// AssetDatabase·Recorder 등 에디터 전용 의존성은 쓰지 않는다.
        /// 런타임 임포트 모델은 Ghost 컨테이너가 소유하므로 여기서 정리하지 않는다.
        /// </summary>
        internal void PrepareRuntimeHumanoidPlayback(
            Animator targetAnimator,
            AnimationClip clip,
            string motionName,
            GameObject importedModel,
            string sourceFilePath)
        {
            IsNativeSkinningCorrectionEnabled = true;
            LastNativeSkinningCorrectionFailure = string.Empty;
            _nativeSkinningCorrectionPlaybackDriver?.Release();
            _pendingNativeSkinningPlaybackRequest =
                NativeSkinningPendingPlaybackRequest.None;
            _nativeSkinningCorrectionSetupError = string.Empty;
            InitializePlaybackController(
                targetAnimator,
                clip,
                importedModel);
            _poseCorrectionDocument = new HumanoidPoseCorrectionDocument(
                motionName,
                _humanoidMotionPlaybackController.ClipFrameRate);
            _humanoidMotionRecordingController?.Dispose();
            _humanoidMotionRecordingController = new HumanoidMotionRecordingController(
                _humanoidMotionPlaybackController,
                new NoOpMotionVideoRecorder());
            _preparedMotionName = motionName;
            _isProcessing = false;
            if (!TryAttachSkinningDriverOrFail(targetAnimator))
            {
                return;
            }
            // 빌드는 에셋 식별자가 없어 소스 FBX 경로+내용 해시로 캐시 키를 잡는다.
            _nativeSkinningCorrectionPlaybackDriver?.ConfigureRuntimeCacheIdentity(
                sourceFilePath,
                motionName);
            SetSessionState(
                FBXSessionState.Ready,
                $"FBX 임포트 완료 · 재생 대기: {motionName}",
                1f);
            PlaybackControlsEnsureRequested?.Invoke(this);
        }

        internal void PrepareGhostModel(GameObject importedModel)
        {
            GameObject ghostContainer = GhostModelPresenter.CreateContainer(importedModel);
            _activeGhostContainer = ghostContainer;
            GhostModelPresenter.SetVisibility(
                importedModel,
                showGhostModel,
                showGhostSkeletonWhenNoRenderers);
        }

        internal void PrepareRetargeterForRecording(
            PoseSpaceRetargeter retargeter,
            GameObject targetObject,
            Animator targetAnimator,
            string importedFilePath,
            string sourceFilePath)
        {
            _activeRetargeter = retargeter;
            HumanoidArmDeformationGuard armDeformationGuard =
                targetObject != null
                    ? targetObject.GetComponent<HumanoidArmDeformationGuard>()
                    : null;
            armDeformationGuard?.BindRetargeter(retargeter);
            HumanoidArmSwingLimitGuard armSwingLimitGuard =
                targetObject != null
                    ? targetObject.GetComponent<HumanoidArmSwingLimitGuard>()
                    : null;
            armSwingLimitGuard?.BindRetargeter(retargeter);
#if UNITY_EDITOR
            bool thumbGuardApplied =
#endif
                HumanoidThumbDeformationGuardApplier.Apply(
                    targetObject,
                    targetAnimator,
                    retargeter,
                    CreateThumbDeformationGuardOptions());
#if UNITY_EDITOR
            if (thumbGuardApplied && EditorDiagnosticSession.IsRecordingOverrideActive)
            {
                LogEditorSmokeThumbState("thumb-guard-bound");
            }
#endif
            AnimationClip editorHumanoidReferenceClip = HumanoidReferenceEditorHooks.Apply?.Invoke(
                retargeter,
                importedFilePath,
                sourceFilePath,
                _shouldUseEditorHumanoidClipMuscleReference,
                _shouldUseEditorHumanoidRootTranslationReference);
            HumanoidReferenceEditorHooks.ApplyManualPoseReference?.Invoke(
                retargeter,
                editorHumanoidReferenceClip,
                CreateEditorManualPoseReferenceOptions());
            SetSessionState(FBXSessionState.GhostReady, "Ghost Retarget 준비 완료", 0.6f);
        }

        internal void DispatchRecording(
            Animation ghostAnimation,
            PoseSpaceRetargeter retargeter,
            GameObject targetObject,
            AnimationClip targetClip,
            string outputBaseName)
        {
            if (ghostAnimation != null)
            {
                ghostAnimation.Stop();
            }

            StartCoroutine(_recordingController.RecordAsync(
                ghostAnimation,
                retargeter,
                targetObject,
                targetClip,
                outputBaseName));
        }

        internal static bool PrepareRetargeterRecordingStartPose(PoseSpaceRetargeter retargeter, float sampleTime, float playbackSpeed, bool holdPose)
        {
            return retargeter != null && retargeter.PrepareRecordingStartPose(sampleTime, playbackSpeed, holdPose);
        }

        internal static object YieldRetargetPrewarmFrame()
        {
#if UNITY_EDITOR
            if (Application.isBatchMode)
            {
                // WaitForEndOfFrame can stall in batchmode because no render loop is advancing.
                return null;
            }
#endif
            return new WaitForEndOfFrame();
        }

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
#else
            return result;
#endif
        }

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
            return input;
        }
#endif
        #endregion
    }
}
