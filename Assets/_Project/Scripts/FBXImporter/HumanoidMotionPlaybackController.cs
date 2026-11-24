using System;
using Fbx2Vmd.Profiling;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal enum HumanoidMotionPlaybackState
    {
        Empty,
        Ready,
        Playing,
        Paused
    }

    /// <summary>
    /// Humanoid 모션의 명시적 재생 상태와 시간을 관리함.
    /// </summary>
    internal sealed partial class HumanoidMotionPlaybackController : IDisposable
    {
        private readonly NativeHumanoidAnimationPlayer _player =
            new NativeHumanoidAnimationPlayer();
        private readonly HumanoidPoseFrameEditor _poseFrameEditor =
            new HumanoidPoseFrameEditor();
        private readonly HumanoidArmSupportPoseApplier _armSupportPoseApplier =
            new HumanoidArmSupportPoseApplier();
        private HumanoidPoseCorrectionDocument _poseCorrectionDocument;
        private readonly HumanoidPoseReferencePlayer _poseReferencePlayer =
            new HumanoidPoseReferencePlayer();
        private readonly HumanoidFootContactStabilizer _footContactStabilizer =
            new HumanoidFootContactStabilizer();
        private readonly HumanoidGroundResponse _groundResponse =
            new HumanoidGroundResponse();
        private readonly HumanoidFootGoalDriver _footGoalDriver =
            new HumanoidFootGoalDriver();
        private readonly HumanoidFootStepDiffuser _footStepDiffuser =
            new HumanoidFootStepDiffuser();
        internal bool HasFootRotationReference => _leftFootRotation != null &&
            _rightFootRotation != null && _poseReferencePlayer.HasFootRotationReference;
        internal HumanoidFootGroundingStatus LastGroundingStatus { get; private set; } =
            HumanoidFootGroundingStatus.Disabled;
        // 마지막 프레임의 게이트 입력 수치. 계측 경로가 교정 실패 원인을 프레임별로 재현함.
        internal HumanoidFootGroundingGate LastGroundingGate { get; } =
            new HumanoidFootGroundingGate();

        internal HumanoidMotionPlaybackState State { get; private set; } =
            HumanoidMotionPlaybackState.Empty;

        internal float CurrentTimeSeconds { get; private set; }

        internal float ClipLengthSeconds { get; private set; }

        internal float ClipFrameRate { get; private set; }
        internal string ClipName { get; private set; } = string.Empty;

        // 선두 정지 구간 탐지처럼 준비된 클립 자체를 읽는 경로에서 사용함.
        internal AnimationClip PreparedClip { get; private set; }

        internal int CurrentFrameIndex =>
            HumanoidMotionFrameCalculator.CalculateFrameIndex(
                CurrentTimeSeconds,
                ClipLengthSeconds,
                ClipFrameRate);

        internal int LastFrameIndex =>
            HumanoidMotionFrameCalculator.CalculateLastFrameIndex(
                ClipLengthSeconds,
                ClipFrameRate);

        internal bool IsPrepared => State != HumanoidMotionPlaybackState.Empty;

        internal void Prepare(Animator targetAnimator, AnimationClip clip)
        {
            PrepareCore(targetAnimator, clip, null);
        }

        // 시연 녹화 전 하체 보정이 모두 켜졌는지 확인하는 읽기 전용 상태.
        internal bool IsGroundResponseEnabled => _isGroundResponseEnabled;
        internal bool IsFootStepDiffusionEnabled => _useFootStepDiffusion;

        internal void SetGroundResponseEnabled(bool isEnabled)
        {
            _isGroundResponseEnabled = isEnabled;
            if (IsPrepared)
                EvaluateCurrentPoseWithCorrection();
        }

        // goal IK 실험 경로 스위치. alpha=0 또는 데이터 부족이면 기존 접지 경로가 그대로 동작함.
        internal void SetIkGoalFootDrive(bool isEnabled, float alpha)
        {
            _useIkGoalFootDrive = isEnabled;
            _footGoalAlpha = Mathf.Clamp01(alpha);
            if (IsPrepared)
                EvaluateCurrentPoseWithCorrection();
        }

        internal bool IsIkGoalFootDriveActive =>
            _useIkGoalFootDrive && _footGoalAlpha > 0f && _footGoalDriver.IsInitialized;

        // 정지 발 순간이동 확산 스위치 — 파이프라인 준비 경로가 기본 활성하고
        // A/B 비교나 롤백 시 해제함.
        internal void SetFootStepDiffusion(bool isEnabled)
        {
            _useFootStepDiffusion = isEnabled;
            if (IsPrepared)
                EvaluateCurrentPoseWithCorrection();
        }

        internal void PrepareWithArmDirectionReference(
            Animator targetAnimator,
            AnimationClip clip,
            GameObject sourceModelAsset)
        {
            if (sourceModelAsset == null)
            {
                throw new ArgumentNullException(nameof(sourceModelAsset));
            }

            PrepareCore(
                targetAnimator,
                clip,
                () => InitializeEditorReferenceCorrections(
                    targetAnimator,
                    clip,
                    sourceModelAsset));
        }

        private void PrepareCore(
            Animator targetAnimator,
            AnimationClip clip,
            Action initializeCanonicalPoseReference)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }

            Dispose();

            try
            {
                InitializePlayback(targetAnimator, clip, initializeCanonicalPoseReference);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        // 최초 원본 평가 전에 기준 밑창과 Avatar 굽힘 방향을 확보함.
        private void InitializePlayback(
            Animator targetAnimator, AnimationClip clip,
            Action initializeCanonicalPoseReference)
        {
            if (initializeCanonicalPoseReference != null)
            {
                InitializeFootRotationBindings(targetAnimator);
                HumanoidFootGrounding.TryCreate(targetAnimator, out _footGrounding);
            }
            _armSupportPoseApplier.Initialize(targetAnimator, clip);
            _player.Initialize(targetAnimator, clip);
            _groundResponse.Initialize(targetAnimator);
            _footStepDiffuser.Initialize(targetAnimator);
            ClipLengthSeconds = Mathf.Max(0f, clip.length);
            ClipFrameRate = HumanoidMotionFrameCalculator.NormalizeFrameRate(
                clip.frameRate);
            ClipName = clip.name;
            PreparedClip = clip;
            CurrentTimeSeconds = 0f;
            _player.EvaluateAt(CurrentTimeSeconds);
            _poseFrameEditor.Initialize(targetAnimator);
            initializeCanonicalPoseReference?.Invoke();
            if (!EvaluateCurrentPoseWithCorrection())
            {
                throw new InvalidOperationException("Humanoid 첫 프레임 보정을 적용하지 못했습니다.");
            }
            State = HumanoidMotionPlaybackState.Ready;
        }

        internal bool Play()
        {
            if (!IsPrepared || State == HumanoidMotionPlaybackState.Playing)
            {
                return false;
            }

            if (CurrentTimeSeconds >= ClipLengthSeconds)
            {
                CurrentTimeSeconds = 0f;
            }

            EvaluateCurrentPoseWithCorrection();
            State = HumanoidMotionPlaybackState.Playing;
            return true;
        }

        internal bool Pause()
        {
            if (State != HumanoidMotionPlaybackState.Playing)
            {
                return false;
            }

            State = HumanoidMotionPlaybackState.Paused;
            return true;
        }

        internal bool Stop()
        {
            if (!IsPrepared)
            {
                return false;
            }

            CurrentTimeSeconds = 0f;
            EvaluateCurrentPoseWithCorrection();
            State = HumanoidMotionPlaybackState.Ready;
            return true;
        }

        internal bool Seek(float timeSeconds)
        {
            if (!IsPrepared)
            {
                return false;
            }

            ValidateTime(timeSeconds, nameof(timeSeconds));
            CurrentTimeSeconds = Mathf.Clamp(timeSeconds, 0f, ClipLengthSeconds);
            EvaluateCurrentPoseWithCorrection();
            return true;
        }

        internal bool SeekFrame(int frameIndex)
        {
            if (!IsPrepared)
            {
                return false;
            }

            return Seek(HumanoidMotionFrameCalculator.CalculateTimeSeconds(
                frameIndex,
                ClipLengthSeconds,
                ClipFrameRate));
        }

        internal bool TryCaptureCurrentPose(out HumanPose pose)
        {
            pose = default;
            return IsPrepared && _poseFrameEditor.TryCapture(out pose);
        }

        internal bool TryCaptureCurrentFootSurface(out HumanoidFootGroundingSnapshot left,
            out HumanoidFootGroundingSnapshot right)
        {
            left = null;
            right = null;
            // 폴백 프레임도 접지 평가가 실행된 상태이므로 표면 측정값을 기록해야 한다.
            // Unavailable/Disabled는 평가 자체가 없어 측정값이 없으므로 제외한다.
            // goal 경로(evaluation_stage=="ik_goal_drive")는 TryEvaluate로 지면·앵커·
            // 가중치를 프레임별 갱신하되 gate 판정(핀 보정 목표·통과 여부)은 평가하지
            // 않으므로 gate 진단 열 해석은 stage로 구분해야 함.
            return IsPrepared &&
                (LastGroundingStatus == HumanoidFootGroundingStatus.Applied ||
                 LastGroundingStatus == HumanoidFootGroundingStatus.NoGround ||
                 LastGroundingStatus == HumanoidFootGroundingStatus.Fallback) &&
                _footGrounding != null && _footGrounding.TryCaptureCurrentSurface(out left, out right);
        }

        internal bool TryCapturePoseBeforeFootStabilization(Action capture)
        {
            if (!IsPrepared || capture == null) return false;
            try
            {
                if (!EvaluateCurrentPoseWithCorrection(applyFootStabilization: false)) return false;
                capture();
                return true;
            }
            finally
            {
                if (!EvaluateCurrentPoseWithCorrection())
                    throw new InvalidOperationException("계측 후 접지 자세를 복원하지 못했습니다.");
            }
        }

        internal bool TryPreviewPoseCorrection(
            HumanoidPoseCorrectionDocument document)
        {
            if (!IsPrepared || document == null)
            {
                return false;
            }

            _poseCorrectionDocument = document;
            if (_footGrounding != null)
                _footGrounding.SuppressReleaseCorrection = document.FrameCount > 0;
            return EvaluateCurrentPoseWithCorrection();
        }

        internal bool RestoreCurrentPose()
        {
            if (!IsPrepared)
            {
                return false;
            }

            _supportCurvePose?.Restore();
            _footGrounding?.RestoreAppliedPose();
            RestoreFootRotationBindings();
            _player.EvaluateAt(CurrentTimeSeconds);
            if (!TryCaptureFootBendNormals(
                    out Vector3 leftBendNormal,
                    out Vector3 rightBendNormal))
            {
                return false;
            }

            bool isApplied = TryApplyArmDirectionCorrection();
            ApplyFootRotationReference(CurrentTimeSeconds);
            _armSupportPoseApplier.Apply();
            return isApplied && TryApplyFootContactStabilization(
                leftBendNormal,
                rightBendNormal);
        }

        internal void Tick(float deltaTimeSeconds)
        {
            using var perfScope = PerfScope.Measure("HumanoidMotionPlaybackController.Tick");
            ValidateTime(deltaTimeSeconds, nameof(deltaTimeSeconds));
            if (State != HumanoidMotionPlaybackState.Playing)
            {
                return;
            }

            CurrentTimeSeconds = Mathf.Min(
                CurrentTimeSeconds + deltaTimeSeconds,
                ClipLengthSeconds);
            EvaluateCurrentPoseWithCorrection();

            if (CurrentTimeSeconds >= ClipLengthSeconds)
            {
                State = HumanoidMotionPlaybackState.Ready;
            }
        }

        public void Dispose()
        {
            _supportCurvePose?.Restore();
            _supportCurvePose = null;
            _supportCurves.Clear();
            _isGroundResponseEnabled = false;
            _useIkGoalFootDrive = false;
            _footGoalAlpha = 0f;
            LastGroundingStatus = HumanoidFootGroundingStatus.Disabled;
            LastGroundingGate.measured = false;
            _footGrounding?.Dispose();
            _footGrounding = null;
            RestoreFootRotationBindings();
            _leftFootRotation = _rightFootRotation = null;
            _sourceToTargetRotation = Quaternion.identity;
            _groundResponse.Clear();
            _footGoalDriver.Clear();
            _footStepDiffuser.Clear();
            _useFootStepDiffusion = false;
            _footContactStabilizer.Clear();
            _poseReferencePlayer.Dispose();
            _poseFrameEditor.Dispose();
            _player.Dispose();
            _armSupportPoseApplier.Dispose();
            CurrentTimeSeconds = 0f;
            ClipLengthSeconds = 0f;
            ClipFrameRate = 0f;
            ClipName = string.Empty;
            PreparedClip = null;
            _poseCorrectionDocument = null;
            State = HumanoidMotionPlaybackState.Empty; }
    }
}
