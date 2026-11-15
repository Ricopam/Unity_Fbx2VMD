using UnityEngine;
using UnityEngine.Serialization;
using System;
using System.IO;
using System.Linq;
using Fbx2Vmd.Recording;
using Fbx2Vmd.Profiling;


namespace Fbx2Vmd.FBXImporter
{
    public partial class FBXVmdPipeline
{

        public bool TryLoadImportedMotionPoseCorrections(
            string filePath,
            out string errorMessage)
        {
            if (!TryReadPoseCorrectionDocument(
                    filePath,
                    out HumanoidPoseCorrectionDocument loadedDocument,
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

        private bool TryReadPoseCorrectionDocument(
            string filePath,
            out HumanoidPoseCorrectionDocument loadedDocument,
            out string errorMessage)
        {
            loadedDocument = null;
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

            return HumanoidPoseCorrectionFileStore.TryLoad(
                    filePath,
                    out loadedDocument,
                    out errorMessage) &&
                TryValidateImportedMotionPoseCorrectionCompatibility(
                    loadedDocument,
                    out errorMessage);
        }

#if UNITY_EDITOR
        internal void PrepareEditorHumanoidPlayback(
            Animator targetAnimator,
            AnimationClip clip,
            string motionName,
            GameObject sourceModelAsset)
        {
            CleanupActiveGhost();
            InitializePlaybackSession(
                targetAnimator,
                clip,
                motionName,
                sourceModelAsset);
            _humanoidMotionRecordingController = new HumanoidMotionRecordingController(
                _humanoidMotionPlaybackController,
                EditorMotionVideoRecorderFactory?.Invoke() ?? new NoOpMotionVideoRecorder(),
                CollectAlphaHiddenRenderers());
            FinalizeEditorPlaybackPreparation(
                targetAnimator,
                clip,
                motionName,
                sourceModelAsset);
        }

        private void FinalizeEditorPlaybackPreparation(
            Animator targetAnimator,
            AnimationClip clip,
            string motionName,
            GameObject sourceModelAsset)
        {
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
            InitializePlaybackSession(
                targetAnimator,
                clip,
                motionName,
                importedModel);
            _humanoidMotionRecordingController = new HumanoidMotionRecordingController(
                _humanoidMotionPlaybackController,
                new NoOpMotionVideoRecorder());
            FinalizeRuntimePlaybackPreparation(
                targetAnimator,
                motionName,
                sourceFilePath);
        }

        private void InitializePlaybackSession(
            Animator targetAnimator,
            AnimationClip clip,
            string motionName,
            GameObject sourceModel)
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
                sourceModel);
            _poseCorrectionDocument = new HumanoidPoseCorrectionDocument(
                motionName,
                _humanoidMotionPlaybackController.ClipFrameRate);
            _humanoidMotionRecordingController?.Dispose();
            _preparedMotionName = motionName;
            _isProcessing = false;
        }

        private void FinalizeRuntimePlaybackPreparation(
            Animator targetAnimator,
            string motionName,
            string sourceFilePath)
        {
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


#endif
        #endregion
    }
}
