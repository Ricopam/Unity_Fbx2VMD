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

    [Serializable]
    internal sealed class HumanoidFootGroundingSnapshot
    {
        public bool has_ground;
        public string support_role;
        public float rear_weight;
        public float front_weight;
        public Vector3 ground_point;
        public Vector3 ground_normal;
        public Vector3 rear_anchor;
        public Vector3 front_anchor;
        public Vector3 rear_point;
        public Vector3 front_point;
        public float rear_distance_mm;
        public float front_distance_mm;
        public float minimum_distance_mm;
        public string rear_renderer;
        public string front_renderer;
        public int rear_vertex;
        public int front_vertex;
    }

    [Serializable]
    internal sealed class HumanoidFootGroundingGate
    {
        public bool measured;
        public float target_error_m;
        public float sole_clearance_m;
        public float supported_contact_error_m;
        public int supported_contact_count;
        public string evaluation_stage;
        public bool raw_pass;
        public bool held_pass;
        public bool used_physical_reach;
        public float pelvis_offset_m;
        public float pelvis_maximum_offset_m;
        public HumanoidFootGroundingLegDiagnostic left;
        public HumanoidFootGroundingLegDiagnostic right;
    }

    // Fallback에서 후보 자세를 복원하기 전에 발별 원인 수치를 보존함.
    internal sealed class HumanoidFootGroundingLegDiagnostic
    {
        public bool has_ground;
        public int rear_point_id = -1;
        public int front_point_id = -1;
        public bool mixed_ground_collider;
        public int contact_ground_misses;
        public float maximum_ground_normal_angle_deg;
        public float maximum_anchor_ground_shift_m;
        public float pair_span_error_m;
        public float weighted_contact_error_m;
        public float lift_m;
        public float lifted_contact_error_m;
        public float foot_offset_m;
        public float offset_contact_error_m;
        public float hip_to_target_m;
        public float damped_reach_m;
        public float physical_reach_m;
        public float target_error_m;
        public float supported_contact_error_m;
        public int supported_contact_count;

        internal void Reset()
        {
            has_ground = false;
            rear_point_id = front_point_id = -1;
            mixed_ground_collider = false;
            contact_ground_misses = 0;
            maximum_ground_normal_angle_deg = maximum_anchor_ground_shift_m = 0f;
            pair_span_error_m = weighted_contact_error_m = 0f;
            lift_m = lifted_contact_error_m = foot_offset_m = offset_contact_error_m = 0f;
            hip_to_target_m = damped_reach_m = physical_reach_m = target_error_m = 0f;
            supported_contact_error_m = 0f;
            supported_contact_count = 0;
        }
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
        // 게이트 임계 근처의 프레임 단위 번복(교정·원본 자세 교번 출력 = 다리 떨림)을 막기 위한
        // 디바운스 상태. 연속 GateTransitionFrames프레임 동일 결과일 때만 상태를 전환한다.
        private bool _gateHoldsFallback;
        private int _gateOppositeStreak;
        private float _lastGateTimeSeconds = -1f;
        private bool _lastGateDecision;

        private HumanoidFootGrounding(Animator animator, Leg left, Leg right)
        {
            _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            _left = left;
            _right = right;
            _sourceToTargetRotation = animator.transform.rotation;
            _humanScale = animator.humanScale;
        }

        internal bool IsPrepared { get; private set; }
        internal bool HasGround { get; private set; }
        internal bool UsedPhysicalReach { get; private set; }
        internal float MaximumTargetError { get; private set; }
        internal float MinimumSoleClearance { get; private set; }
        internal float MaximumSupportedContactError { get; private set; }
        internal int FullySupportedContactCount { get; private set; }
        internal string EvaluationStage { get; private set; } = "not_started";
        internal bool RawGatePass { get; private set; }
        internal float PelvisOffset { get; private set; }
        internal float PelvisMaximumOffset { get; private set; }
        internal HumanoidFootGroundingLegDiagnostic LeftDiagnostic => _left.Diagnostic;
        internal HumanoidFootGroundingLegDiagnostic RightDiagnostic => _right.Diagnostic;
        internal int UnresolvedSupportPairCount => _left.UnresolvedPairCount + _right.UnresolvedPairCount;
        internal int InterpolatedSupportContactCount => _left.InterpolatedContactCount + _right.InterpolatedContactCount;

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
            if (samples == null || samples.Count < 2 || clip == null || evaluateReference == null ||
                sourceHumanScale <= 0f || float.IsNaN(sourceHumanScale) || float.IsInfinity(sourceHumanScale))
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
                IsPrepared = _left.TryBuildContacts(samples, true, sourceHumanScale,
                    _frameRate, clip.length, evaluateReference, _sourceToTargetRotation, _sourceScaleRatio,
                    intents?.Left) &&
                    _right.TryBuildContacts(samples, false, sourceHumanScale,
                    _frameRate, clip.length, evaluateReference, _sourceToTargetRotation, _sourceScaleRatio,
                    intents?.Right);
                if (IsPrepared && UnresolvedSupportPairCount > 0)
                    Debug.LogWarning($"공동 지지 계획 미해결 {UnresolvedSupportPairCount}건. " +
                        $"왼발: {_left.FirstPlanIssue}, 오른발: {_right.FirstPlanIssue}. 해당 구간은 개별 목표를 유지함.");
                return IsPrepared;
            }
            finally
            {
                evaluateReference(0f);
            }
        }

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
            if (!IsPrepared || _hips == null || ground == null ||
                float.IsNaN(timeSeconds) || float.IsInfinity(timeSeconds) || timeSeconds < 0f)
                return false;

            if (!CapturePoseAndSolveTargets(timeSeconds, ground,
                    out float frame, out float offset, out Vector2 footOffsets))
                return false;

            bool isApplied = false;
            try
            {
                Leg.TargetState leftTarget = _left.CaptureTarget();
                Leg.TargetState rightTarget = _right.CaptureTarget();
                if (!TrySolveGroundPose(offset, footOffsets, out Vector2 clearance))
                    return false;

                if (RawGatePass && !SuppressReleaseCorrection)
                {
                    bool? shiftResult = TryApplyReleaseShift(frame, timeSeconds, ground,
                        leftTarget, rightTarget, offset, footOffsets, clearance,
                        applyHold, ref baselinePose);
                    if (shiftResult.HasValue)
                    {
                        isApplied = shiftResult.Value;
                        return isApplied;
                    }
                }

                if (!applyHold) baselinePose = ReadTouchdownPose();
                if (RawGatePass && !SuppressReleaseCorrection &&
                    !TryApplyContactWindows(window, releaseTrial, frame, ground,
                        leftTarget, rightTarget, offset, footOffsets, clearance,
                        applyHold, ref touchdownRejected))
                    return false;

                isApplied = applyHold ? ApplyGateHold(RawGatePass, timeSeconds) : RawGatePass;
                return isApplied;
            }
            finally
            {
                if (!isApplied)
                    RestoreAppliedPose();
            }
        }

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
            bool shiftLeft = _left.TryGetReleaseTarget(frame, ground, leftTarget.Position,
                clearance.x, contactLimit, out Vector3 leftRelease);
            bool shiftRight = _right.TryGetReleaseTarget(frame, ground, rightTarget.Position,
                clearance.y, contactLimit, out Vector3 rightRelease);
            if (!shiftLeft && !shiftRight)
                return null;
            // 기존 접촉 상태가 달라지면 원래 목표를 다시 풀어 게이트 결과를 보존함.
            bool baselineUsedPhysicalReach = UsedPhysicalReach;
            RestoreAppliedPose();
            _left.RestoreTarget(leftTarget);
            _right.RestoreTarget(rightTarget);
            if ((!shiftLeft || _left.TrySetReleaseTarget(leftRelease)) &&
                (!shiftRight || _right.TrySetReleaseTarget(rightRelease)) &&
                TryResolveReach(out float releaseOffset, out Vector2 releaseFootOffsets) &&
                TrySolveGroundPose(releaseOffset, releaseFootOffsets, out Vector2 releaseClearance) &&
                RawGatePass &&
                (releaseClearance.x <= contactLimit) == (clearance.x <= contactLimit) &&
                (releaseClearance.y <= contactLimit) == (clearance.y <= contactLimit))
            {
                if (!applyHold) baselinePose = ReadTouchdownPose();
                return !applyHold || ApplyGateHold(true, timeSeconds);
            }

            RestoreAppliedPose();
            _left.RestoreTarget(leftTarget);
            _right.RestoreTarget(rightTarget);
            UsedPhysicalReach = baselineUsedPhysicalReach;
            if (!TrySolveGroundPose(offset, footOffsets, out _)) return false;
            return null;
        }

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

        // 본을 움직이지 않고 현재 프레임의 지면·지지 가중치·밑창 측정값만 갱신함.
        // gate 보정을 적용하지 않는 경로(goal 드라이브)에서 표면 스냅샷과 진단이
        // 마지막 gate 평가나 초기값을 재사용하지 않게 하는 평가 전용 단계임.
        internal bool TryEvaluate(float timeSeconds, HumanoidGroundResponse ground)
        {
            ResetEvaluationState();
            if (!IsPrepared || _hips == null || ground == null ||
                float.IsNaN(timeSeconds) || float.IsInfinity(timeSeconds) || timeSeconds < 0f)
                return false;

            _left.CapturePose();
            _right.CapturePose();
            Physics.SyncTransforms();
            float frame = timeSeconds * _frameRate;
            EvaluationStage = "target";
            if (!_left.TryCalculateTarget(frame, ground, _humanScale) ||
                !_right.TryCalculateTarget(frame, ground, _humanScale))
                return false;

            HasGround = _left.HasGround || _right.HasGround;
            EvaluationStage = "measure";
            if (!_left.TryMeasure(out float leftClearance, out float leftContact, out int leftCount) ||
                !_right.TryMeasure(out float rightClearance, out float rightContact, out int rightCount))
                return false;

            MinimumSoleClearance = Mathf.Min(leftClearance, rightClearance);
            MaximumSupportedContactError = Mathf.Max(leftContact, rightContact);
            FullySupportedContactCount = leftCount + rightCount;
            return true;
        }

        // 발목 로컬 좌표의 밑창 후보 정점 — goal 드라이브의 밑창 인식 지면 스냅이 재사용함.
        // 샘플러의 대여 버퍼를 복제해 넘기므로 이후 갱신과 무관하게 안정적임.
        internal bool TryGetSoleProbeLocalOffsets(bool isLeft, out Vector3[] localPoints)
        {
            localPoints = null;
            if (!IsPrepared)
            {
                return false;
            }
            Vector3[] points = (isLeft ? _left : _right).Sampler.LocalSolePoints;
            if (points == null || points.Length == 0)
            {
                return false;
            }
            localPoints = (Vector3[])points.Clone();
            return true;
        }

        internal bool TryGetSoleLiftTarget(bool isLeft, Vector3 candidateWorld, Vector3 planePoint,
            Vector3 planeNormal, out Vector3 target)
        {
            target = default;
            if (!IsPrepared)
                return false;

            var leg = isLeft ? _left : _right;
            // gate 롤백이나 goal 적용 이후의 실제 밑창으로 관통을 판단함.
            if (!leg.Sampler.TrySample())
                return false;

            return HumanoidSoleGroundConstraint.TryLiftAbovePlane(
                candidateWorld, leg.Foot.rotation, leg.Sampler.LocalSolePoints,
                leg.Foot.lossyScale.x, planePoint, planeNormal, out target, out _);
        }

        private void ResetEvaluationState()
        {
            HasGround = false;
            UsedPhysicalReach = false;
            RawGatePass = false;
            PelvisOffset = 0f;
            PelvisMaximumOffset = 0f;
            EvaluationStage = "preconditions";
            MaximumTargetError = 0f;
            MinimumSoleClearance = 0f;
            MaximumSupportedContactError = 0f;
            FullySupportedContactCount = 0;
            _left.Diagnostic.Reset();
            _right.Diagnostic.Reset();
        }

        public void Dispose()
        {
            RestoreAppliedPose();
            IsPrepared = false;
            _left.Sampler.Dispose();
            _right.Sampler.Dispose();
        }

        // 완전 지지 중에는 앵커와 실측 접촉점의 괴리도 게이트 조건임.
        // 지지 중에 부유·관통이 남은 프레임을 Applied로 보고하지 않음.
        // 계측 CSV가 같은 임계로 거절 사유를 재현할 수 있게 비율을 공개함.
        internal const float TargetErrorPerHumanScale = 0.0001f;
        internal const float SoleClearancePerHumanScale = 0.0001f;
        internal const float SupportedContactErrorPerHumanScale = 0.005f;

        internal static bool ResolveApplied(float targetError, float soleClearance,
            float supportedContactError, float humanScale)
        {
            return targetError <= humanScale * TargetErrorPerHumanScale &&
                soleClearance >= -humanScale * SoleClearancePerHumanScale &&
                supportedContactError <= humanScale * SupportedContactErrorPerHumanScale;
        }

        // 전환에 필요한 연속 반대 결과 프레임 수. 측정상 잔여 번복 대역은 전부
        // 정확히 3프레임 연속 실패(최소 섬)이므로 4로 올려 흡수한다.
        private const int GateTransitionFrames = 4;

        // 순차 프레임에서만 상태를 보류한다. 1프레임을 넘는 시간 점프(프레임 탐색)나
        // 무지면 프레임은 즉시 게이트 결과를 사용하고 보류 상태를 현재 결과로 맞춘다.
        private bool ApplyGateHold(bool gatePass, float timeSeconds)
        {
            // 캡처처럼 같은 시각을 재평가하는 경로는 첫 판정을 재사용해
            // 한 프레임이 스트릭을 두 번 올리지 않게 한다.
            if (timeSeconds == _lastGateTimeSeconds) return _lastGateDecision;
            bool jumped = _frameRate > 0f &&
                Mathf.Abs(timeSeconds - _lastGateTimeSeconds) > 1.5f / _frameRate;
            _lastGateTimeSeconds = timeSeconds;
            bool decision;
            if (jumped || !HasGround)
            {
                _gateHoldsFallback = !gatePass;
                _gateOppositeStreak = 0;
                decision = gatePass;
            }
            else if (gatePass == !_gateHoldsFallback)
            {
                _gateOppositeStreak = 0;
                decision = gatePass;
            }
            else
            {
                _gateOppositeStreak++;
                if (_gateOppositeStreak < GateTransitionFrames)
                {
                    decision = !_gateHoldsFallback;
                }
                else
                {
                    _gateHoldsFallback = !_gateHoldsFallback;
                    _gateOppositeStreak = 0;
                    decision = gatePass;
                }
            }
            _lastGateDecision = decision;
            return decision;
        }

    }
}