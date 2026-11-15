using UnityEngine;
using UnityEngine.Serialization;
using System;
using System.IO;
using System.Linq;
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
            EnsureImportDependencies();
            EnsureControllerServices();
        }

        private void EnsureImportDependencies()
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
        }

        private void EnsureControllerServices()
        {
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


#if UNITY_EDITOR

#endif


        #endregion
    }
}
