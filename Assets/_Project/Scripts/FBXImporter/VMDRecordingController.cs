using UnityEngine;
using System;
using System.Collections;
using System.IO;
using static Fbx2Vmd.FBXImporter.FBXVmdPipeline;
using Fbx2Vmd.Recording;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// FBXVmdPipeline의 VMD 녹화 수명주기를 담당하는 컴패니언 컨트롤러입니다.
    /// 프리웜, 녹화 실행, 완료 처리와 이벤트 구독 해제를 캡슐화함.
    /// </summary>
    public partial class VMDRecordingController
    {
        private readonly FBXVmdPipeline _pipeline;
        private HumanoidSampleCode _activeRecorderController;

        public VMDRecordingController(FBXVmdPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        /// <summary>
        /// 안정적인 녹화 시퀀스를 시작함. 지연 대기 → 프리웜 → VMD 녹화 순서로 진행함.
        /// </summary>
        public IEnumerator RecordAsync(
            Animation ghostAnim, PoseSpaceRetargeter retargeter,
            GameObject targetObject, AnimationClip clip, string outputBaseName)
        {
            yield return AwaitStartDelay();

            if (ghostAnim == null || clip == null)
            {
                _pipeline.FailSession("녹화 시작에 필요한 Ghost 애니메이션이 없습니다.");
                yield break;
            }

            RecordingPlan plan = CreateRecordingPlan(clip, outputBaseName);
            bool shouldStartVmdRecording = ShouldStartVmdRecording( _pipeline.ShouldRecordVmdAfterImport);
            if (!TryPrepareRecording( targetObject, ref plan, clip, outputBaseName, shouldStartVmdRecording, out HumanoidSampleCode recorderController))
            {
                yield break; }

            yield return PrewarmAndCaptureBaseline( ghostAnim, clip, retargeter, plan, shouldStartVmdRecording);

            if (!shouldStartVmdRecording)
            {
                CompleteImportWithoutVmdRecording(outputBaseName);
                yield break;
            }

            StartAutoRecordingOrFail(plan, recorderController);
        }

        // 재생 속도·길이·비교 라벨이 채워진 녹화 계획을 만듦.
        private RecordingPlan CreateRecordingPlan(AnimationClip clip, string outputBaseName)
        {
            var plan = new RecordingPlan
            {
                playbackSpeed = ResolveVmdRecordingPlaybackSpeed(_pipeline.vmdRecordingPlaybackSpeed), outputBaseName = outputBaseName, };
            plan.length = ResolveRecordingLengthForPlaybackSpeed(clip.length, plan.playbackSpeed);
            plan.comparisonLabel = $"auto_{plan.outputBaseName}";
            return plan;
        }

        // VMD 녹화 예정이면 레코더와 계획을 준비함. 생략 경로는 즉시 성공.
        private bool TryPrepareRecording(
            GameObject targetObject, ref RecordingPlan plan, AnimationClip clip,
            string outputBaseName, bool shouldStartVmdRecording,
            out HumanoidSampleCode recorderController)
        {
            recorderController = null;
            return !shouldStartVmdRecording ||
                PrepareRecorderAndPlan(targetObject, ref plan, clip, outputBaseName,
                    out recorderController
#if UNITY_EDITOR
                    , _pipeline.EditorDiagnosticSession
#endif
                    );
        }

        // VMD 자동 녹화 생략 경로의 성공 상태·로그·플래그 정리.
        private void CompleteImportWithoutVmdRecording(string outputBaseName)
        {
            _pipeline.SetSessionState(FBXVmdPipeline.FBXSessionState.Success, $"FBX 임포트/촬영 준비 완료: {outputBaseName}", 1f);
            Debug.Log($"[Recording] FBX 임포트 및 Unity 촬영 준비 완료됨. 출력={outputBaseName}, VMD 자동 녹화=생략");
            _pipeline._isProcessing = false;
        }

        // 녹화 파라미터 가변 묶음 — 스모크 오버라이드·참조 타이밍이 부분 갱신한다.

        void ApplyRecorderDiagnostics(
            HumanoidSampleCode recorderController, float[] diagnosticSampleTimesOverride
#if UNITY_EDITOR
            , FBXEditorDiagnosticSession editorDiagnosticSession
#endif
            )
        {
            RecordingCaptureResolutionPlan recordingCapturePlan;
            float diagnosticScreenshotPadding = 1.8f;
            float diagnosticScreenshotVerticalViewportCenter = 0.28f;
#if UNITY_EDITOR
            recordingCapturePlan = editorDiagnosticSession.HasCaptureResolutionOverride
                ? RecordingCaptureResolution.CreateCustomPlan( editorDiagnosticSession.CaptureWidth, editorDiagnosticSession.CaptureHeight)
                : RecordingCaptureResolution.CreatePlan( _pipeline.recordingCaptureQuality, _pipeline.customRecordingCaptureWidth, _pipeline.customRecordingCaptureHeight);
            diagnosticScreenshotPadding = float.IsNaN( editorDiagnosticSession.DiagnosticScreenshotPaddingOverride)
                ? diagnosticScreenshotPadding
                : editorDiagnosticSession.DiagnosticScreenshotPaddingOverride;
            diagnosticScreenshotVerticalViewportCenter = float.IsNaN( editorDiagnosticSession.DiagnosticScreenshotVerticalViewportCenterOverride)
                ? diagnosticScreenshotVerticalViewportCenter
                : editorDiagnosticSession.DiagnosticScreenshotVerticalViewportCenterOverride;
#else
            recordingCapturePlan = RecordingCaptureResolution.CreatePlan( _pipeline.recordingCaptureQuality, _pipeline.customRecordingCaptureWidth, _pipeline.customRecordingCaptureHeight);
#endif
            recorderController.SetRecordingDiagnostics( _pipeline.enableRecordingDiagnostics, _pipeline.enableRecordingDiagnostics && _pipeline.enableDiagnosticFingerCloseups,
                _pipeline.enableRecordingDiagnostics && _pipeline.useDeterministicCaptureFramerateForDiagnostics, diagnosticSampleTimesOverride, recordingCapturePlan.Width,
                recordingCapturePlan.Height, diagnosticScreenshotPadding, diagnosticScreenshotVerticalViewportCenter);
        }

#if UNITY_EDITOR

#endif

        private void OnRecordingFinished(VmdSaveResult result)
        {
            MotionComparisonProbe probe = _activeRecorderController != null
                ? _activeRecorderController.GetComponent<MotionComparisonProbe>()
                : null;
            VmdSaveResult effectiveResult = _pipeline.ApplyEditorSmokeThumbRiskFailure(result, probe);
            TryAppendVmdArtifactToComparisonSessionManifest(probe, effectiveResult);
            TryCopyVmdToAdditionalFolder(effectiveResult);
            ClearActiveRecordingSubscription();
            _pipeline.LogRetargetPlaybackStabilitySummary();
            SetSaveResultState(effectiveResult);
            _pipeline.CleanupActiveGhost();
            _pipeline.ResetTargetStateAfterSession(recaptureGuardBaselines: false);
#if UNITY_EDITOR
            _pipeline.NotifyEditorSmokeFinished(effectiveResult);
            _pipeline.ClearEditorSmokeOverride();
#endif
            _pipeline._isProcessing = false;
        }

        // 저장 결과에 따라 세션 상태를 성공 또는 실패로 반영함.
        private void SetSaveResultState(VmdSaveResult effectiveResult)
        {
            if (effectiveResult.Success)
            {
                _pipeline.SetSessionState( FBXSessionState.Success, $"VMD 저장 완료: {Path.GetFileName(effectiveResult.FilePath)}", 1f);
                return;
            }

            string errorMessage = string.IsNullOrWhiteSpace(effectiveResult.ErrorMessage)
                ? "VMD 저장 실패"
                : effectiveResult.ErrorMessage;
            _pipeline.SetSessionState(FBXSessionState.Failed, errorMessage, 0f);
        }

        private static void TryAppendVmdArtifactToComparisonSessionManifest(
            MotionComparisonProbe probe,
            VmdSaveResult result)
        {
            if (probe == null ||
                result.Success == false ||
                string.IsNullOrWhiteSpace(probe.LastSessionManifestPath) ||
                string.IsNullOrWhiteSpace(result.FilePath))
            {
                return;
            }

            MotionComparisonProbeSessionManifestPatcher.TryAppendExportedVmdToSessionManifest(
                probe.LastSessionManifestPath,
                MakeProjectRelativePath(result.FilePath),
                result.FrameCount,
                result.FileSizeBytes);
        }

        private void TryCopyVmdToAdditionalFolder(VmdSaveResult result)
        {
            if (!result.Success || string.IsNullOrWhiteSpace(_pipeline.additionalVmdCopyFolder) ||
                string.IsNullOrWhiteSpace(result.FilePath) || !File.Exists(result.FilePath))
            {
                return;
            }

            try
            {
                string targetFolder = _pipeline.additionalVmdCopyFolder.Trim();
                if (!Path.IsPathRooted(targetFolder))
                {
                    string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                    targetFolder = Path.Combine(projectRoot, targetFolder); }

                Directory.CreateDirectory(targetFolder);
                string targetPath = Path.Combine(targetFolder, Path.GetFileName(result.FilePath));
                File.Copy(result.FilePath, targetPath, overwrite: true);
                Debug.Log($"[Recording] VMD 추가 복사 완료됨. 경로={targetPath}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Recording] VMD 추가 복사 실패함. 오류={ex.Message}"); }
        }

        internal void ClearActiveRecordingSubscription()
        {
            if (_activeRecorderController == null)
            {
                return;
            }

            _activeRecorderController.RecordingFinished -= OnRecordingFinished;
            _activeRecorderController = null;
        }

        /// <summary>
        /// 녹화 시작 포즈를 프리웜함. Ghost 애니메이터를 시작 시간으로 설정하고 프리웜 프레임만큼 샘플링함.
        /// </summary>
        public IEnumerator PrewarmStartPose( Animation ghostAnim, AnimationClip clip, PoseSpaceRetargeter retargeter, float startTimeSeconds, float playbackSpeed, int configuredPrewarmFrameCount, int visiblePrewarmYieldFrameCount)
        {
            ghostAnim.clip = clip;
            if (ghostAnim.GetClip(clip.name) == null)
            {
                ghostAnim.AddClip(clip, clip.name); }
            ghostAnim.Play(clip.name);
            AnimationState state = ghostAnim[clip.name];
            if (state == null)
            {
                yield return new WaitForEndOfFrame();
                yield break; }
            float sampleTime = Mathf.Clamp(startTimeSeconds, 0f, Mathf.Max(0f, state.length));
            float safePlaybackSpeed = Mathf.Max(0.0001f, playbackSpeed);
            int prewarmFrames = ResolvePrewarmFrameCount(configuredPrewarmFrameCount);
            int visibleYieldFrames = Mathf.Max(0, visiblePrewarmYieldFrameCount);
            if (prewarmFrames <= 0)
            {
                yield return ApplyImmediateStartPose( ghostAnim, state, retargeter, sampleTime, safePlaybackSpeed, visibleYieldFrames);
                yield break; }
            yield return RunPrewarmFrames( ghostAnim, clip, state, retargeter, sampleTime, safePlaybackSpeed, prewarmFrames); }

        // 프리웜 프레임이 없을 때 시작 포즈를 즉시 적용하고 접지 보정을 반영함.
        IEnumerator ApplyImmediateStartPose(
            Animation ghostAnim, AnimationState state, PoseSpaceRetargeter retargeter,
            float sampleTime, float safePlaybackSpeed, int visibleYieldFrames)
        {
            if (!FBXVmdPipeline.PrepareRetargeterRecordingStartPose(retargeter, sampleTime, safePlaybackSpeed, holdPose: false))
            {
                state.time = sampleTime;
                state.speed = safePlaybackSpeed;
                ghostAnim.Sample(); }
            retargeter?.ApplyLateVisualGroundingCorrection();
            if (visibleYieldFrames > 0)
            {
                yield return FBXVmdPipeline.YieldRetargetPrewarmFrame(); }
        }

        IEnumerator RunPrewarmFrames(
            Animation ghostAnim, AnimationClip clip, AnimationState state,
            PoseSpaceRetargeter retargeter,
            float sampleTime, float safePlaybackSpeed, int prewarmFrames)
        {
            state.enabled = true;
            state.wrapMode = WrapMode.Once;
            state.time = sampleTime;
            state.speed = 0f;
            for (int i = 0; i < prewarmFrames; i++)
            {
                if (!FBXVmdPipeline.PrepareRetargeterRecordingStartPose(retargeter, sampleTime, safePlaybackSpeed, holdPose: true))
                {
                    state.time = sampleTime;
                    ghostAnim.Sample();
                }
                yield return FBXVmdPipeline.YieldRetargetPrewarmFrame(); }
            if (!FBXVmdPipeline.PrepareRetargeterRecordingStartPose(retargeter, sampleTime, safePlaybackSpeed, holdPose: false))
            {
                state.time = sampleTime;
                state.speed = safePlaybackSpeed;
                ghostAnim.Sample();
                ghostAnim.Play(clip.name);
                state.time = sampleTime;
                state.speed = safePlaybackSpeed;
                ghostAnim.Sample(); }
            retargeter?.ApplyLateVisualGroundingCorrection();
            Debug.Log($"[Retargeting] 리타게팅 프리웜 완료됨. 프레임={prewarmFrames}, 클립 시간={sampleTime:F2}초"); }

        public static float ResolveVmdRecordingPlaybackSpeed(float configuredPlaybackSpeed)
        {
            if (configuredPlaybackSpeed <= 0f ||
                float.IsNaN(configuredPlaybackSpeed) ||
                float.IsInfinity(configuredPlaybackSpeed))
            {
                return 1f;
            }

            return Mathf.Max(0.0001f, configuredPlaybackSpeed);
        }

        public static float ResolveRecordingLengthForPlaybackSpeed(float clipLengthSeconds, float playbackSpeed)
        {
            if (clipLengthSeconds <= 0f ||
                float.IsNaN(clipLengthSeconds) ||
                float.IsInfinity(clipLengthSeconds))
            {
                return 0f;
            }

            return clipLengthSeconds / ResolveVmdRecordingPlaybackSpeed(playbackSpeed);
        }

        public static bool TryBuildKnownMmdReferenceRecordingPlan(
            string outputBaseName,
            float clipLengthSeconds,
            float recordingFrameRate,
            out float recordingLengthSeconds,
            out int targetFrameCount,
            out float playbackSpeed)
        {
            return TryBuildKnownMmdReferenceRecordingPlan(
                outputBaseName,
                clipLengthSeconds,
                recordingFrameRate,
                useKnownReferenceTiming: true,
                out recordingLengthSeconds,
                out targetFrameCount,
                out playbackSpeed);
        }

        public static bool TryBuildKnownMmdReferenceRecordingPlan(
            string outputBaseName,
            float clipLengthSeconds,
            float recordingFrameRate, bool useKnownReferenceTiming, out float recordingLengthSeconds, out int targetFrameCount, out float playbackSpeed)
        {
            recordingLengthSeconds = clipLengthSeconds;
            targetFrameCount = 0;
            playbackSpeed = 1f;
            if (!useKnownReferenceTiming)
            {
                return false; }
            if (recordingFrameRate <= 0f || float.IsNaN(recordingFrameRate) || float.IsInfinity(recordingFrameRate) || clipLengthSeconds <= 0f || float.IsNaN(clipLengthSeconds) || float.IsInfinity(clipLengthSeconds))
            {
                return false; }
            string cleanBaseName = Path.GetFileNameWithoutExtension(outputBaseName ?? string.Empty);
            if (!string.Equals(cleanBaseName, VMDOutputNamePolicy.SatisfactionReferenceBaseName, StringComparison.OrdinalIgnoreCase))
            {
                return false; }
            int referenceFrameCount = VMDOutputNamePolicy.SatisfactionReferenceMaxMmdFrame + 1;
            float referenceDurationSeconds = referenceFrameCount / recordingFrameRate;
            float frameToleranceSeconds = 0.5f / recordingFrameRate;
            if (clipLengthSeconds + frameToleranceSeconds < referenceDurationSeconds)
            {
                return false; }
            recordingLengthSeconds = referenceDurationSeconds;
            targetFrameCount = referenceFrameCount;
            playbackSpeed = Mathf.Max(0.0001f, clipLengthSeconds / referenceDurationSeconds);
            return true; }

        /// <summary>
        /// 설정된 프리웜 프레임 수를 최대치로 클램프하여 해석함.
        /// </summary>
    }
}
