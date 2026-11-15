using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal enum HumanoidFootGroundingStatus
    {
        Disabled,
        Unavailable,
        NoGround,
        Applied,
        Fallback
    }

    /// <summary>
    /// 명시적 재생 시각의 지지·밑창 목표와 양다리 도달 보정을 조립함.
    /// </summary>
    internal sealed partial class HumanoidFootGrounding : IDisposable
    {
        private readonly Transform _hips;
        private readonly Leg _left;
        private readonly Leg _right;
        private readonly Quaternion _sourceToTargetRotation;
        private readonly float _humanScale;
        private float _frameRate;
        private float _sourceScaleRatio;
        private Vector3 _originalHipsPosition;
        private bool _hasAppliedPose;

        private HumanoidFootGrounding(Animator animator, Leg left, Leg right)
        {
            _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            _left = left;
            _right = right;
            _sourceToTargetRotation = animator.transform.rotation;
            _humanScale = animator.humanScale;
        }

        internal bool TryCaptureCurrentSurface(out HumanoidFootGroundingSnapshot left,
            out HumanoidFootGroundingSnapshot right)
        {
            left = null;
            right = null;
            return IsPrepared && _left.TryCaptureCurrentSurface(out left) &&
                _right.TryCaptureCurrentSurface(out right);
        }

        internal static bool TryCreate(Animator animator, out HumanoidFootGrounding grounding)
        {
            grounding = null;
            if (animator == null || animator.avatar == null || !animator.isHuman ||
                animator.GetBoneTransform(HumanBodyBones.Hips) == null ||
                !HumanoidLegBendCalibration.TryCapture(animator,
                    out Vector3 leftNormal, out Vector3 rightNormal))
                return false;
            SkinnedMeshRenderer[] renderers = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Leg left = null;
            Leg right = null;
            try
            {
                if (!Leg.TryCreate(animator, true, renderers, leftNormal, out left) ||
                    !Leg.TryCreate(animator, false, renderers, rightNormal, out right))
                    return false;
                grounding = new HumanoidFootGrounding(animator, left, right);
                return true;
            }
            finally
            {
                if (grounding == null)
                {
                    left?.Sampler.Dispose();
                    right?.Sampler.Dispose();
                }
            }
        }

        internal bool TryPrepare(IReadOnlyList<HumanoidFootContactSample> samples,
            float sourceHumanScale, AnimationClip clip, Action<float> evaluateReference,
            HumanoidFootContactIntentEstimate intents = null)
        {
            IsPrepared = false;
            _touchdownWindows.Clear();
            _releaseSmoothingWindows.Clear();
            if (samples == null || samples.Count < 2 || clip == null || evaluateReference == null || sourceHumanScale <= 0f || float.IsNaN(sourceHumanScale) || float.IsInfinity(sourceHumanScale))
                return false;
            _frameRate = HumanoidMotionFrameCalculator.NormalizeFrameRate(clip.frameRate);
            _sourceScaleRatio = _humanScale / sourceHumanScale;
            // 새 접촉 계획의 첫 프레임이 이전 클립의 마지막 게이트 판정을 재사용하지 않게 보류 상태를 초기화함.
            _gateHoldsFallback = false;
            _gateOppositeStreak = 0;
            _lastGateTimeSeconds = -1f;
            _lastGateDecision = false;
            try
            {
                IsPrepared = _left.TryBuildContacts(samples, true, sourceHumanScale, _frameRate, clip.length, evaluateReference, _sourceToTargetRotation, _sourceScaleRatio, intents?.Left) &&
                    _right.TryBuildContacts(samples, false, sourceHumanScale, _frameRate, clip.length, evaluateReference, _sourceToTargetRotation, _sourceScaleRatio, intents?.Right);
                if (IsPrepared && UnresolvedSupportPairCount > 0)
                    Debug.LogWarning($"공동 지지 계획 미해결 {UnresolvedSupportPairCount}건. " + $"왼발: {_left.FirstPlanIssue}, 오른발: {_right.FirstPlanIssue}. 해당 구간은 개별 목표를 유지함.");
                return IsPrepared;
            }
            finally
            {
                evaluateReference(0f);
            } }

        // 이전 접지의 골반 이동과 회전을 원본 평가 전에 해제하여 누적·정지 잔류를 막음.
        internal void RestoreAppliedPose()
        {
            if (!_hasAppliedPose)
                return;
            if (_hips != null)
                _hips.localPosition = _originalHipsPosition;
            _left.RestorePose();
            _right.RestorePose();
            _hasAppliedPose = false;
        }

        internal bool TryApply(float timeSeconds, HumanoidGroundResponse ground) =>
            TryApplyCore(timeSeconds, ground, null, true, out _, out _);

        private bool TryApplyCore(float timeSeconds, HumanoidGroundResponse ground,
            TouchdownWindow window, bool applyHold, out bool touchdownRejected, out TouchdownPose baselinePose,
            ReleaseSmoothingWindow releaseTrial = null)
        {
            touchdownRejected = false;
            baselinePose = default;
            ResetEvaluationState();
            if (!IsPrepared || _hips == null || ground == null || float.IsNaN(timeSeconds) || float.IsInfinity(timeSeconds) || timeSeconds < 0f) return false;
            if (!CapturePoseAndSolveTargets(timeSeconds, ground, out float frame, out float offset, out Vector2 footOffsets)) return false;
            bool isApplied = false;
            try
            {
                Leg.TargetState leftTarget = _left.CaptureTarget();
                Leg.TargetState rightTarget = _right.CaptureTarget();
                if (!TrySolveGroundPose(offset, footOffsets, out Vector2 clearance)) return false;
                if (RawGatePass && !SuppressReleaseCorrection)
                {
                    bool? shiftResult = TryApplyReleaseShift(frame, timeSeconds, ground, leftTarget, rightTarget, offset, footOffsets, clearance, applyHold, ref baselinePose);
                    if (shiftResult.HasValue)
                    {
                        isApplied = shiftResult.Value;
                        return isApplied; } }
                if (!applyHold) baselinePose = ReadTouchdownPose();
                if (RawGatePass && !SuppressReleaseCorrection && !TryApplyContactWindows(window, releaseTrial, frame, ground, leftTarget, rightTarget, offset, footOffsets, clearance, applyHold, ref touchdownRejected)) return false;
                isApplied = applyHold ? ApplyGateHold(RawGatePass, timeSeconds) : RawGatePass;
                return isApplied; } finally
            {
                if (!isApplied) RestoreAppliedPose(); } }

        private bool CapturePoseAndSolveTargets(float timeSeconds,
            HumanoidGroundResponse ground,
            out float frame, out float offset, out Vector2 footOffsets)
        {
            _originalHipsPosition = _hips.localPosition;
            _left.CapturePose();
            _right.CapturePose();
            Physics.SyncTransforms();
            frame = timeSeconds * _frameRate;
            EvaluationStage = "target";
            if (!_left.TryCalculateTarget(frame, ground, _humanScale) ||
                !_right.TryCalculateTarget(frame, ground, _humanScale))
            {
                offset = 0f;
                footOffsets = Vector2.zero;
                return false;
            }

            HasGround = _left.HasGround || _right.HasGround;
            // 고정 신장 비율로 도달 가능한 지지를 포기하지 않도록 실제 다리 크기 안에서 최소 이동을 찾음.
            float maximumOffset = Mathf.Max(_left.TotalBoneLength, _right.TotalBoneLength);
            PelvisMaximumOffset = maximumOffset;
            return TryResolveReach(out offset, out footOffsets);
        }

        private bool? TryApplyReleaseShift(float frame, float timeSeconds,
            HumanoidGroundResponse ground,
            Leg.TargetState leftTarget, Leg.TargetState rightTarget,
            float offset, Vector2 footOffsets, Vector2 clearance,
            bool applyHold, ref TouchdownPose baselinePose)
        {
            float contactLimit = _humanScale * SoleClearancePerHumanScale;
            bool shiftLeft = _left.TryGetReleaseTarget(frame, ground, leftTarget.Position, clearance.x, contactLimit, out Vector3 leftRelease);
            bool shiftRight = _right.TryGetReleaseTarget(frame, ground, rightTarget.Position, clearance.y, contactLimit, out Vector3 rightRelease);
            if (!shiftLeft && !shiftRight)
                return null;
            // 기존 접촉 상태가 달라지면 원래 목표를 다시 풀어 게이트 결과를 보존함.
            bool baselineUsedPhysicalReach = UsedPhysicalReach;
            RestoreAppliedPose();
            _left.RestoreTarget(leftTarget);
            _right.RestoreTarget(rightTarget);
            if ((!shiftLeft || _left.TrySetReleaseTarget(leftRelease)) && (!shiftRight || _right.TrySetReleaseTarget(rightRelease)) &&
                TryResolveReach(out float releaseOffset, out Vector2 releaseFootOffsets) && TrySolveGroundPose(releaseOffset, releaseFootOffsets, out Vector2 releaseClearance) && RawGatePass &&
                (releaseClearance.x <= contactLimit) == (clearance.x <= contactLimit) && (releaseClearance.y <= contactLimit) == (clearance.y <= contactLimit))
            {
                if (!applyHold) baselinePose = ReadTouchdownPose();
                return !applyHold || ApplyGateHold(true, timeSeconds); }
            RestoreAppliedPose();
            _left.RestoreTarget(leftTarget);
            _right.RestoreTarget(rightTarget);
            UsedPhysicalReach = baselineUsedPhysicalReach;
            if (!TrySolveGroundPose(offset, footOffsets, out _)) return false;
            return null; }

        private bool TryApplyContactWindows(TouchdownWindow window,
            ReleaseSmoothingWindow releaseTrial, float frame,
            HumanoidGroundResponse ground,
            Leg.TargetState leftTarget, Leg.TargetState rightTarget,
            float offset, Vector2 footOffsets, Vector2 clearance,
            bool applyHold, ref bool touchdownRejected)
        {
            if (window == null && applyHold)
                _touchdownWindows.TryGetValue(Mathf.FloorToInt(frame), out window);
            if (window == null)
            {
                if (releaseTrial == null && applyHold)
                    _releaseSmoothingWindows.TryGetValue(Mathf.FloorToInt(frame), out releaseTrial);
                if (releaseTrial != null && !TryApplyReleaseSmoothing(releaseTrial, frame, ground,
                        leftTarget, rightTarget, offset, footOffsets, clearance, out touchdownRejected))
                    return false;
            }
            if (window != null && frame >= window.Start && frame < window.Full &&
                !TryApplyTouchdown(window, ground, leftTarget, rightTarget, offset, footOffsets, out touchdownRejected))
                return false;
            return true;
        }

        internal bool SuppressReleaseCorrection { get; set; }

        internal void PrepareReleaseReferences(AnimationClip clip, Action<float> evaluateReference,
            HumanoidGroundResponse ground)
        {
            Physics.SyncTransforms();
            try
            {
                _left.PrepareReleaseReferences(_frameRate, clip.length, evaluateReference, ground, _humanScale);
                _right.PrepareReleaseReferences(_frameRate, clip.length, evaluateReference, ground, _humanScale);
            }
            finally
            {
                evaluateReference(0f);
            }
        }

        private bool TryResolveReach(out float offset, out Vector2 footOffsets)
        {
            offset = 0f;
            footOffsets = Vector2.zero;
            UsedPhysicalReach = false;
            EvaluationStage = "reach";
            if (!HasGround || HumanoidPelvisReachCalculator.TryCalculateOffset(
                    _left.BuildReach(true), _right.BuildReach(true), Vector3.up,
                    PelvisMaximumOffset, out offset, out footOffsets))
                return true;

            // 감쇠와 양발 지지가 충돌하면 본을 늘리지 않고 실제 길이 한도로 판단함.
            UsedPhysicalReach = true;
            return HumanoidPelvisReachCalculator.TryCalculateOffset(
                _left.BuildReach(false), _right.BuildReach(false), Vector3.up,
                PelvisMaximumOffset, out offset, out footOffsets);
        }

        private bool TrySolveGroundPose(float offset, Vector2 footOffsets, out Vector2 clearance)
        {
            clearance = Vector2.zero;
            RawGatePass = false;
            PelvisOffset = offset;
            _hasAppliedPose = true;
            _hips.position += Vector3.up * offset;
            EvaluationStage = "solve";
            if (!_left.TrySolve(footOffsets.x, out float leftError) ||
                !_right.TrySolve(footOffsets.y, out float rightError))
                return false;
            MaximumTargetError = Mathf.Max(leftError, rightError);
            EvaluationStage = "measure";
            if (!_left.TryMeasure(out clearance.x, out float leftContact, out int leftCount) ||
                !_right.TryMeasure(out clearance.y, out float rightContact, out int rightCount))
                return false;
            MinimumSoleClearance = Mathf.Min(clearance.x, clearance.y);
            MaximumSupportedContactError = Mathf.Max(leftContact, rightContact);
            FullySupportedContactCount = leftCount + rightCount;
            EvaluationStage = "gate";
            RawGatePass = ResolveApplied(MaximumTargetError, MinimumSoleClearance,
                MaximumSupportedContactError, _humanScale);
            return true;
        }

        public void Dispose()
        {
            RestoreAppliedPose();
            IsPrepared = false;
            _left.Sampler.Dispose();
            _right.Sampler.Dispose();
        }

    }
}