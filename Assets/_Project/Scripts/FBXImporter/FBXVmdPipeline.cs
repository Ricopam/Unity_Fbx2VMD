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
    public partial class FBXVmdPipeline : MonoBehaviour
{

        // e2e 녹화 증거 수집이 직렬화 녹화 옵션을 강제할 때 사용함 — Play 모드 인스턴스
        // 변경이라 종료 시 직렬화 값이 유지됨.
        internal void SetMotionVideoRecordingOptionsForEvidence(
            bool skipLeadingStill,
            bool rangeEnabled,
            int startFrame,
            int endFrame,
            string outputDirectory)
        {
            _motionVideoSkipLeadingStill = skipLeadingStill;
            _motionVideoRangeEnabled = rangeEnabled;
            _motionVideoRangeStartFrame = startFrame;
            _motionVideoRangeEndFrame = endFrame;
            _motionVideoOutputFolder = outputDirectory;
        }

#if UNITY_EDITOR
        internal void CaptureEditorSmokeSettings()
        {
            EditorDiagnosticSession.CaptureSettings(
                new FBXEditorDiagnosticSession.SettingsSnapshot(
                    enableRecordingDiagnostics,
                    enableDiagnosticFingerCloseups,
                    useDeterministicCaptureFramerateForDiagnostics,
                    startDelay,
                    showGhostModel));
        }

        internal void ClearEditorSmokeOverride()
        {
            if (EditorDiagnosticSession.Clear(
                    out FBXEditorDiagnosticSession.SettingsSnapshot settingsSnapshot))
            {
                enableRecordingDiagnostics = settingsSnapshot.EnableRecordingDiagnostics;
                enableDiagnosticFingerCloseups = settingsSnapshot.EnableDiagnosticFingerCloseups;
                useDeterministicCaptureFramerateForDiagnostics =
                    settingsSnapshot.UseDeterministicCaptureFramerateForDiagnostics;
                startDelay = settingsSnapshot.StartDelay;
                showGhostModel = settingsSnapshot.ShowGhostModel;
            }
        }

        internal void NotifyEditorSmokeFinished(VmdSaveResult result)
        {
            EditorDiagnosticSession.NotifyFinished(result);
        }
#endif

        #region Unity 생명주기
        private void Awake()
        {
            EnsureServicesInitialized();
            DisableIndependentRecorderAutoStart();
            EnsureIdlePoseGuardInitialized();
            PlaybackControlsEnsureRequested?.Invoke(this);
        }

        private void EnsureIdlePoseGuardInitialized()
        {
            _idlePoseGuard = GetComponent<TargetIdlePoseGuard>();
            if (_idlePoseGuard == null)
            {
                _idlePoseGuard = gameObject.AddComponent<TargetIdlePoseGuard>();
            }

            _idlePoseGuard.SetTargetCharacter(targetCharacter);
            _idlePoseGuard.Initialize();
        }

        /// <summary>
        /// targetCharacter가 Awake 이후 외부 경로(캐릭터 라이브러리 등)로 교체됐을 때
        /// Idle Pose Guard를 새 대상으로 다시 초기화한다.
        /// 편집 모드에서는 컴포넌트가 없으면 임의로 추가하지 않고 건너뛴다.
        /// </summary>
        public void RebindIdlePoseGuard()
        {
            if (_idlePoseGuard == null)
            {
                _idlePoseGuard = GetComponent<TargetIdlePoseGuard>();
            }

            if (_idlePoseGuard == null)
            {
                if (!Application.isPlaying)
                {
                    return;
                }
                _idlePoseGuard = gameObject.AddComponent<TargetIdlePoseGuard>();
            }

            _idlePoseGuard.SetTargetCharacter(targetCharacter);
            _idlePoseGuard.Initialize();
        }

        private void LateUpdate()
        {
            PipelineRunProfiler.SampleFrame(Time.unscaledDeltaTime);
            if (!TickNativeSkinningCorrectionPreparation())
            {
                TickHumanoidMotionPlayback(Time.unscaledDeltaTime);
            }
            bool hasActiveMotion =
                _activeRetargeter != null ||
                (_humanoidMotionPlaybackController?.IsPrepared ?? false);
            _idlePoseGuard?.TryApply(_isProcessing, hasActiveMotion);
        }

        private void OnDestroy()
        {
            _recordingController?.ClearActiveRecordingSubscription();
            CleanupHumanoidMotionPlayback();
            _conversionCoordinator?.RestoreMmdPostPoseCorrectionForRetarget();
            _idlePoseGuard?.RestoreAnimatorController();
        }
        #endregion

        #region 초기화
        private void InitializeServices()
        {
            if (_fileBrowserService == null)
            {
                _fileBrowserService = fileBrowserServiceFactory?.Invoke();
            }

            if (_fbxImporter == null)
            {
                _fbxImporter = fbxImporterFactory?.Invoke();
            }

            if (_fileBrowserService == null || _fbxImporter == null)
            {
                throw new InvalidOperationException("FBX 임포트 의존성 초기화에 실패했습니다.");
            }

            if (_importController == null)
            {
                _importController = new FBXImportController(
                    this,
                    _fileBrowserService,
                    _fbxImporter.ImportAsync);
            }

            if (_conversionCoordinator == null)
            {
                _conversionCoordinator = new FBXConversionCoordinator(this, _importController);
            }

            if (_recordingController == null)
            {
                _recordingController = new VMDRecordingController(this);
            }
        }

        internal void EnsureServicesInitialized()
        {
            if (_fileBrowserService == null ||
                _fbxImporter == null ||
                _importController == null ||
                _conversionCoordinator == null ||
                _recordingController == null)
            {
                InitializeServices();
            }
        }
        #endregion


        #region 이벤트 핸들러
        public void OnClickImportButton()
        {
            EnsureServicesInitialized();
            _importController.ImportFromDialog();
        }

        /// <summary>
        /// Import_FBX 폴더에 있는 FBX 파일 목록 로드
        /// 에디터와 빌드 환경 모두에서 작동
        /// </summary>
        public void OnClickLoadFromImportFolder()
        {
            EnsureServicesInitialized();
            _importController.LoadFromImportFolder();
        }

        public bool TryStartFbxImportFromSharedSettings(string sourcePath)
        {
            EnsureServicesInitialized();
            return _importController.TryImportFromSharedSettings(sourcePath);
        }

        public bool TrySetNativeSkinningCorrectionEnabled(bool enabled)
        {
            if (IsImportedMotionRecording || IsProcessing || !HasPreparedImportedMotion)
            {
                return false;
            }
            if (IsNativeSkinningCorrectionEnabled == enabled)
            {
                return true;
            }
            if (!string.IsNullOrEmpty(_nativeSkinningCorrectionSetupError))
            {
                LastNativeSkinningCorrectionFailure = _nativeSkinningCorrectionSetupError;
            }
            else if (_nativeSkinningCorrectionPlaybackDriver?.IsFaulted ?? false)
            {
                LastNativeSkinningCorrectionFailure = _nativeSkinningCorrectionPlaybackDriver.FailureMessage;
            }
            _pendingNativeSkinningPlaybackRequest = NativeSkinningPendingPlaybackRequest.None;
            _nativeSkinningCorrectionPlaybackDriver?.Invalidate();
            _humanoidMotionPlaybackController.Stop();
            IsNativeSkinningCorrectionEnabled = enabled;
            SetSessionState(FBXSessionState.Ready,
                enabled ? "표면 보정 사용 · 재생 대기" : "표면 보정 끔 · 비교 재생 대기", 1f);
            return true;
        }

        public bool TryPlayImportedMotion()
        {
            if (IsImportedMotionRecording ||
                _humanoidMotionPlaybackController == null ||
                !TryPrepareNativeSkinningCorrection(
                    NativeSkinningPendingPlaybackRequest.Play,
                    out bool shouldStartImmediately))
            {
                return false;
            }

            return !shouldStartImmediately ||
                TryStartImportedMotionPlaybackNow();
        }

        private bool TryStartImportedMotionPlaybackNow()
        {
            if (!_humanoidMotionPlaybackController.Play())
            {
                return false;
            }

            SetSessionState(
                FBXSessionState.PreviewPlaying,
                "FBX 모션 재생 중",
                ResolveHumanoidPlaybackProgress());
            return true;
        }

        public bool TryPauseImportedMotion()
        {
            if (IsImportedMotionRecording ||
                IsPreparingImportedMotionCorrection ||
                _humanoidMotionPlaybackController == null ||
                !_humanoidMotionPlaybackController.Pause())
            {
                return false;
            }

            SetSessionState(
                FBXSessionState.PreviewPaused,
                "FBX 모션 일시정지",
                ResolveHumanoidPlaybackProgress());
            return true;
        }

        public bool TryStopImportedMotion()
        {
            if (_pendingNativeSkinningPlaybackRequest !=
                    NativeSkinningPendingPlaybackRequest.None)
            {
                _pendingNativeSkinningPlaybackRequest =
                    NativeSkinningPendingPlaybackRequest.None;
                if (IsPreparingImportedMotionCorrection)
                {
                    _nativeSkinningCorrectionPlaybackDriver.CancelPreparation();
                }
                else if (_nativeSkinningCorrectionPlaybackDriver?.IsFaulted ?? false)
                {
                    _nativeSkinningCorrectionPlaybackDriver.Invalidate();
                }

                _humanoidMotionPlaybackController?.Stop();
                SetSessionState(
                    FBXSessionState.Ready,
                    "FBX 표면 보정 준비 취소 · 재생 대기",
                    1f);
                return true;
            }

            if (IsImportedMotionRecording)
            {
                return TryStopImportedMotionRecording();
            }

            if (_humanoidMotionPlaybackController == null ||
                !_humanoidMotionPlaybackController.Stop())
            {
                return false;
            }

            SetSessionState(FBXSessionState.Ready, "FBX 모션 재생 준비 완료", 1f);
            return true;
        }

        public bool TrySeekImportedMotion(float timeSeconds)
        {
            if (IsImportedMotionRecording ||
                IsPreparingImportedMotionCorrection ||
                _humanoidMotionPlaybackController == null ||
                !_humanoidMotionPlaybackController.Seek(timeSeconds))
            {
                return false;
            }

            SetSessionState(
                _humanoidMotionPlaybackController.State == HumanoidMotionPlaybackState.Playing
                    ? FBXSessionState.PreviewPlaying
                    : FBXSessionState.PreviewPaused,
                $"FBX 모션 위치: {_humanoidMotionPlaybackController.CurrentTimeSeconds:F2}초",
                ResolveHumanoidPlaybackProgress());
            return true;
        }

        public bool TrySeekImportedMotionFrame(int frameIndex)
        {
            if (IsImportedMotionRecording ||
                IsPreparingImportedMotionCorrection ||
                _humanoidMotionPlaybackController == null)
            {
                return false;
            }

            if (_humanoidMotionPlaybackController.State ==
                HumanoidMotionPlaybackState.Playing)
            {
                _humanoidMotionPlaybackController.Pause();
            }

            if (!_humanoidMotionPlaybackController.SeekFrame(frameIndex))
            {
                return false;
            }

            if (_poseCorrectionDocument != null)
            {
                _humanoidMotionPlaybackController.TryPreviewPoseCorrection(
                    _poseCorrectionDocument);
            }

            SetSessionState(
                FBXSessionState.PreviewPaused,
                $"FBX 모션 프레임: {_humanoidMotionPlaybackController.CurrentFrameIndex}",
                ResolveHumanoidPlaybackProgress());
            return true;
        }

        public bool TryCaptureImportedMotionPose(out HumanPose pose)
        {
            pose = default;
            return !IsImportedMotionRecording &&
                !IsPreparingImportedMotionCorrection &&
                _humanoidMotionPlaybackController != null &&
                _humanoidMotionPlaybackController.TryCaptureCurrentPose(out pose);
        }

#if UNITY_EDITOR
        internal bool TryCaptureImportedMotionFootSurface(out HumanoidFootGroundingSnapshot left,
            out HumanoidFootGroundingSnapshot right, out HumanoidFootGroundingStatus status,
            out HumanoidFootGroundingGate gate)
        {
            left = null;
            right = null;
            status = _humanoidMotionPlaybackController?.LastGroundingStatus ??
                HumanoidFootGroundingStatus.Unavailable;
            gate = _humanoidMotionPlaybackController?.LastGroundingGate;
            return !IsImportedMotionRecording && !IsPreparingImportedMotionCorrection &&
                _humanoidMotionPlaybackController != null &&
                _humanoidMotionPlaybackController.TryCaptureCurrentFootSurface(out left, out right);
        }
#endif

        public bool TryApplyImportedMotionMuscleDelta(
            string muscleName,
            float delta)
        {
            if (IsImportedMotionRecording ||
                IsPreparingImportedMotionCorrection ||
                _humanoidMotionPlaybackController == null ||
                !_humanoidMotionPlaybackController.TryCaptureCurrentPose(out _))
            {
                return false;
            }

            if (_humanoidMotionPlaybackController.State ==
                HumanoidMotionPlaybackState.Playing)
            {
                _humanoidMotionPlaybackController.Pause();
            }

            _poseCorrectionDocument ??= new HumanoidPoseCorrectionDocument(
                _preparedMotionName,
                _humanoidMotionPlaybackController.ClipFrameRate);
            int frameIndex = _humanoidMotionPlaybackController.CurrentFrameIndex;
            if (!_poseCorrectionDocument.TrySetMuscleDelta(
                    frameIndex,
                    muscleName,
                    delta) ||
                !_humanoidMotionPlaybackController.TryPreviewPoseCorrection(
                    _poseCorrectionDocument))
            {
                return false;
            }

            _nativeSkinningCorrectionPlaybackDriver?.Invalidate();

            SetSessionState(
                FBXSessionState.PreviewPaused,
                $"FBX 자세 수정 미리보기: {frameIndex} 프레임",
                ResolveHumanoidPlaybackProgress());
            return true;
        }

        public bool TryRestoreImportedMotionPoseFrame()
        {
            if (IsImportedMotionRecording ||
                IsPreparingImportedMotionCorrection ||
                _humanoidMotionPlaybackController == null)
            {
                return false;
            }

            if (_humanoidMotionPlaybackController.State ==
                HumanoidMotionPlaybackState.Playing)
            {
                _humanoidMotionPlaybackController.Pause();
            }

            int frameIndex = _humanoidMotionPlaybackController.CurrentFrameIndex;
            if (!_humanoidMotionPlaybackController.RestoreCurrentPose())
            {
                return false;
            }

            _poseCorrectionDocument?.TryRemoveFrame(frameIndex);
            _nativeSkinningCorrectionPlaybackDriver?.Invalidate();
            SetSessionState(
                FBXSessionState.PreviewPaused,
                $"FBX 원본 자세 복원: {frameIndex} 프레임",
                ResolveHumanoidPlaybackProgress());
            return true;
        }

        public bool TrySaveImportedMotionPoseCorrections(
            string filePath,
            out string errorMessage)
        {
            if (IsImportedMotionRecording)
            {
                errorMessage = "녹화 중에는 Humanoid 보정 문서를 저장할 수 없습니다.";
                return false;
            }

            if (_humanoidMotionPlaybackController == null ||
                !_humanoidMotionPlaybackController.IsPrepared ||
                _poseCorrectionDocument == null)
            {
                errorMessage = "저장할 Humanoid 보정 문서가 없습니다.";
                return false;
            }

            return HumanoidPoseCorrectionFileStore.TrySave(
                filePath,
                _poseCorrectionDocument,
                out errorMessage);
        }
        #endregion
    }
}
