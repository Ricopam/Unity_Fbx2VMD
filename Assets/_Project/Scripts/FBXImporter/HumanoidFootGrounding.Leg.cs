using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class HumanoidFootGrounding
    {
        private sealed partial class Leg
        {
            // 앞꿈치 지지 발끝 하향각 — 25°부터 초과분을 완만히 눌러 35°에 점근시킴.
            private const float ToePitchSoftKneeDeg = 25f;
            private const float MaximumSupportedToePitch = 35f;
            internal readonly Transform Upper;
            internal readonly Transform Lower;
            internal readonly Transform Foot;
            internal readonly Transform Toes;
            internal readonly HumanoidFootSoleSampler Sampler;
            private readonly Vector3 _referenceNormal;
            private readonly Quaternion? _localFootFrame;
            private readonly Vector3[][] _sourcePoints = new Vector3[2][];
            private HumanoidFootAnchorPlan _plan;
            private readonly HumanoidFootAnchorPlan.Sample[] _activeContacts = new HumanoidFootAnchorPlan.Sample[2];
            private readonly Vector3[] _localContactPoints = new Vector3[2];
            private readonly Vector3[] _anchors = new Vector3[2];
            private readonly Dictionary<int, ReleaseReference> _releaseReferences = new();
            private Vector2[] _weights;
            private float[] _relativeHeights;
            private float _nearLevelHeight;
            private bool _hasSupportRoles;
            private Vector2 _activeWeights;
            private Quaternion _upperRotation, _lowerRotation, _footRotation, _toeRotation;
            private Quaternion _originalFootRotation, _originalUpperRotation, _targetFootRotation;
            private Vector3 _upperToKnee, _kneeToFoot, _originalFootPosition, _target;
            private float _support, _clearance, _maximumReach;
            private RaycastHit _ground;
            // 착지 램프 중 목표 발 회전의 프레임당 변화 상한에 사용하는 직전 상태임.
            private Quaternion _prevTargetFootRotation;
            private float _prevSupport;
            private bool _hasPrevTargetRot;

            internal readonly struct TargetState
            {
                internal readonly Vector3 Position;
                internal readonly float Clearance;
                internal readonly float MaximumReach;

                internal TargetState(Vector3 position, float clearance, float maximumReach)
                {
                    Position = position;
                    Clearance = clearance;
                    MaximumReach = maximumReach;
                }
            }

            private sealed class ReleaseReference
            {
                internal readonly int Frame;
                internal readonly int Channel;
                internal readonly int Point;
                internal readonly Vector3 Anchor;
                internal readonly Vector3 Correction;
                internal readonly RaycastHit Ground;
                internal readonly Matrix4x4 GroundMatrix;

                internal ReleaseReference(int frame, int channel, int point, Vector3 anchor,
                    Vector3 correction, RaycastHit ground)
                {
                    Frame = frame;
                    Channel = channel;
                    Point = point;
                    Anchor = anchor;
                    Correction = correction;
                    Ground = ground;
                    GroundMatrix = ground.collider.transform.localToWorldMatrix;
                }
            }

            internal bool HasGround { get; private set; }
            internal readonly HumanoidFootGroundingLegDiagnostic Diagnostic = new HumanoidFootGroundingLegDiagnostic();
            internal int UnresolvedPairCount => _plan?.UnresolvedPairCount ?? 0;
            internal string FirstPlanIssue => _plan?.FirstIssue;
            internal int InterpolatedContactCount =>
                (_activeWeights.x >= 0.999f && _activeContacts[0].IsInterpolatedPoint ? 1 : 0) +
                (_activeWeights.y >= 0.999f && _activeContacts[1].IsInterpolatedPoint ? 1 : 0);

            internal bool TryCaptureCurrentSurface(out HumanoidFootGroundingSnapshot sample)
            {
                sample = new HumanoidFootGroundingSnapshot
                {
                    has_ground = HasGround,
                    rear_weight = _activeWeights.x,
                    front_weight = _activeWeights.y,
                    rear_anchor = _anchors[0],
                    front_anchor = _anchors[1],
                    support_role = _activeWeights.x > 0f
                        ? (_activeWeights.y > 0f ? "both" : "rear")
                        : (_activeWeights.y > 0f ? "front" : "released")
                };
                if (!HasGround) return true;
                if (!Sampler.TrySample() ||
                    !Sampler.TrySelectContact(false, _ground.normal, out int rear, out Vector3 rearPoint) ||
                    !Sampler.TrySelectContact(true, _ground.normal, out int front, out Vector3 frontPoint) ||
                    !Sampler.TryGetPointIdentity(rear, out SkinnedMeshRenderer rearRenderer, out int rearVertex) ||
                    !Sampler.TryGetPointIdentity(front, out SkinnedMeshRenderer frontRenderer, out int frontVertex))
                    return false;

                sample.ground_point = _ground.point;
                sample.ground_normal = _ground.normal;
                sample.rear_point = rearPoint;
                sample.front_point = frontPoint;
                sample.rear_distance_mm = Vector3.Dot(rearPoint - _ground.point, _ground.normal) * 1000f;
                sample.front_distance_mm = Vector3.Dot(frontPoint - _ground.point, _ground.normal) * 1000f;
                sample.minimum_distance_mm = Mathf.Min(sample.rear_distance_mm, sample.front_distance_mm);
                sample.rear_renderer = rearRenderer.name;
                sample.front_renderer = frontRenderer.name;
                sample.rear_vertex = rearVertex;
                sample.front_vertex = frontVertex;
                return true;
            }

            private Leg(Transform upper, Transform lower, Transform foot, Transform toes,
                HumanoidFootSoleSampler sampler, Vector3 referenceNormal, Quaternion? localFootFrame)
            {
                Upper = upper;
                Lower = lower;
                Foot = foot;
                Toes = toes;
                Sampler = sampler;
                _referenceNormal = referenceNormal;
                _localFootFrame = localFootFrame;
            }

            internal static bool TryCreate(Animator animator, bool isLeft,
                SkinnedMeshRenderer[] renderers, Vector3 normal, out Leg leg)
            {
                leg = null;
                Transform upper = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                Transform lower = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                Transform foot = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                Transform toes = animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                Quaternion? localFootFrame = null;
                if (HumanoidFootRotationBinding.TryCreate(foot, toes, animator.transform.up, out var binding) &&
                    binding.TryCaptureWorldFrame(out Quaternion worldFrame))
                    localFootFrame = (Quaternion.Inverse(foot.rotation) * worldFrame).normalized;
                if (upper == null || lower == null || foot == null ||
                    !lower.IsChildOf(upper) || !foot.IsChildOf(lower) ||
                    !HumanoidFootSoleSampler.TryCreate(foot, toes, renderers, Vector3.up, out var sampler))
                    return false;
                leg = new Leg(upper, lower, foot, toes, sampler, normal, localFootFrame);
                return true;
            }

            internal bool TryBuildContacts(IReadOnlyList<HumanoidFootContactSample> samples,
                bool isLeft, float sourceScale, float frameRate, float clipLength, Action<float> evaluate,
                Quaternion sourceRotation, float scaleRatio,
                IReadOnlyList<HumanoidFootContactIntent> intents = null)
            {
                Vector2 referenceHeights = Vector2.zero;
                if (!CollectContactSamples(samples, isLeft,
                        out float[] relativeHeights, out Quaternion[] sourceFrames,
                        out bool hasSourceFrames, out bool hasRelativeHeights,
                        ref referenceHeights))
                    return false;
                if (!HumanoidFootSupportWeightCalculator.TryBuild(_sourcePoints[0], _sourcePoints[1],
                        frameRate, referenceHeights, sourceScale / 30f, sourceScale / 6f,
                        0.1f, 0.1f, out _weights))
                    return false;

                // 확정된 지지 의도 구간 안에서 감지된 접촉은 빠르게 잠금함(터치다운 비대칭 전이).
                HumanoidFootAnchorPolicyResolver.ApplyTouchdownLock(_weights, intents);

                // 역할 배열을 먼저 확정하여 임의 탐색 순서에도 같은 접촉 계획을 사용함.
                if (hasRelativeHeights)
                {
                    if (!HumanoidFootSupportRoleCalculator.TryBuild(_weights, relativeHeights,
                            frameRate, sourceScale / 300f, 0.1f, out Vector2[] roleWeights))
                        return false;
                    _weights = roleWeights;
                    _hasSupportRoles = true;
                }
                _relativeHeights = relativeHeights;
                _nearLevelHeight = sourceScale / 70f;

                // 의도 구간을 프레임별 앵커 방침으로 변환하고 자유 구간은 속도 상한을 둠.
                HumanoidFootAnchorPolicy[] policies =
                    HumanoidFootAnchorPolicyResolver.Rasterize(intents, _weights.Length);
                float pinRelease = sourceScale *
                    HumanoidFootContactIntentEstimator.SlideDisplacementPerHumanScale;
                float trackStep = sourceScale * scaleRatio *
                    HumanoidFootContactIntentEstimator.SupportSpeedPerHumanScale / frameRate;
                return HumanoidFootAnchorPlan.TryBuild(Foot, Sampler, _sourcePoints, _weights,
                    sourceRotation, scaleRatio, frameRate, clipLength, evaluate, out _plan,
                    sourceFrames, hasSourceFrames ? _localFootFrame : null,
                    policies, pinRelease, trackStep);
            }

            private bool CollectContactSamples(
                IReadOnlyList<HumanoidFootContactSample> samples, bool isLeft,
                out float[] relativeHeights, out Quaternion[] sourceFrames,
                out bool hasSourceFrames, out bool hasRelativeHeights,
                ref Vector2 referenceHeights)
            {
                hasRelativeHeights = samples[0].RelativeFootHeights.HasValue;
                hasSourceFrames = samples[0].LeftFootFrame.HasValue;
                _hasSupportRoles = false;
                relativeHeights = hasRelativeHeights ? new float[samples.Count] : null;
                sourceFrames = hasSourceFrames ? new Quaternion[samples.Count] : null;
                for (int i = 0; i < samples.Count; i++)
                {
                    if (samples[i].LeftFootFrame.HasValue != hasSourceFrames ||
                        samples[i].RightFootFrame.HasValue != hasSourceFrames)
                        return false;
                    if (hasSourceFrames)
                        sourceFrames[i] = (isLeft ? samples[i].LeftFootFrame : samples[i].RightFootFrame).Value;
                    Vector2? heights = samples[i].RelativeFootHeights;
                    if (heights.HasValue != hasRelativeHeights)
                        return false;
                    if (hasRelativeHeights)
                        relativeHeights[i] = isLeft ? heights.Value.x : heights.Value.y;
                }
                for (int channel = 0; channel < 2; channel++)
                {
                    var points = new Vector3[samples.Count];
                    var heights = new float[samples.Count];
                    for (int i = 0; i < samples.Count; i++)
                    {
                        HumanoidFootContactSample sample = samples[i];
                        points[i] = isLeft ? (channel == 0 ? sample.LeftFoot : sample.LeftToes) :
                            (channel == 0 ? sample.RightFoot : sample.RightToes);
                        heights[i] = points[i].y;
                    }
                    _sourcePoints[channel] = points;
                    Array.Sort(heights);
                    referenceHeights[channel] = heights[Mathf.Max(0, Mathf.CeilToInt(heights.Length * 0.02f) - 1)];
                }
                return true;
            }

            internal void PrepareReleaseReferences(float frameRate, float clipLength,
                Action<float> evaluate, HumanoidGroundResponse ground, float humanScale)
            {
                _releaseReferences.Clear();
                try
                {
                    for (int frame = 0; frame + 1 < _weights.Length; frame++)
                    {
                        if (!TryGetReleaseRange(frame, out int end, out int channel, out var contact))
                            continue;

                        evaluate(Mathf.Min(frame / frameRate, clipLength));
                        CapturePose();
                        if (!TryCalculateTarget(frame, ground, humanScale) || !HasGround ||
                            _support < 0.999f || Diagnostic.mixed_ground_collider)
                            continue;

                        // 마지막 지지에서 골반을 내릴 이유가 없는 다리의 해제 궤적은 보존함.
                        if (!NeedsPelvisLowering()) continue;

                        var reference = new ReleaseReference(frame, channel, contact.FirstPoint,
                            contact.Anchor, _target - _originalFootPosition, _ground);
                        for (int index = frame; index < end; index++)
                            _releaseReferences.Add(index, reference);
                    }
                }
                finally
                {
                    _hasPrevTargetRot = false;
                    _prevSupport = 0f;
                    Diagnostic.Reset();
                }
            }

            private static int GetSingleSupportChannel(Vector2 weights)
            {
                if (weights.x > 0f && weights.y <= 0f) return 0;
                if (weights.y > 0f && weights.x <= 0f) return 1;
                return -1;
            }

            internal TargetState CaptureTarget() => new TargetState(_target, _clearance, _maximumReach);

            internal void RestoreTarget(TargetState target)
            {
                _target = target.Position;
                _clearance = target.Clearance;
                _maximumReach = target.MaximumReach;
                Diagnostic.damped_reach_m = HasGround ? _maximumReach : 0f;
            }

            internal bool TryGetReleaseTarget(float frame, HumanoidGroundResponse ground,
                Vector3 target, float clearance, float contactLimit, out Vector3 releaseTarget)
            {
                releaseTarget = target;
                if (_support <= 0f || _support >= 0.999f ||
                    !_releaseReferences.TryGetValue(Mathf.FloorToInt(frame), out ReleaseReference reference) ||
                    GetSingleSupportChannel(_activeWeights) != reference.Channel ||
                    _activeContacts[reference.Channel].FirstPoint != reference.Point ||
                    Diagnostic.mixed_ground_collider || !MatchesReferenceGround(_ground, reference.Ground, contactLimit) ||
                    _ground.collider.transform.localToWorldMatrix != reference.GroundMatrix ||
                    !ground.TryFindGround(reference.Anchor, out RaycastHit anchorHit) ||
                    !MatchesReferenceGround(anchorHit, reference.Ground, contactLimit))
                    return false;

                Vector3 shift = _originalFootPosition + reference.Correction * _support - target;
                float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(frame - reference.Frame));
                // 현재 밑창 여유로 수직 보정을 제한해 해제 시점이 앞당겨지지 않게 함.
                float headroom = Mathf.Max(0f, clearance - contactLimit) * _ground.normal.y;
                Vector3 limitedShift = Vector3.ProjectOnPlane(shift, _ground.normal) +
                    _ground.normal * Mathf.Clamp(Vector3.Dot(shift, _ground.normal), -headroom, headroom);
                Vector3 candidate = target + blend * limitedShift;
                return candidate != target &&
                    ground.TryFindGround(candidate, out RaycastHit candidateHit) &&
                    MatchesReferenceGround(candidateHit, reference.Ground, contactLimit) &&
                    HumanoidSoleGroundConstraint.TryLiftAbovePlane(candidate, _targetFootRotation,
                        Sampler.LocalSolePoints, Foot.lossyScale.x, _ground.point, _ground.normal,
                        out releaseTarget, out _);
            }

            private static bool MatchesReferenceGround(RaycastHit hit, RaycastHit reference, float tolerance) =>
                hit.collider != null && hit.collider == reference.collider &&
                (hit.normal - reference.normal).sqrMagnitude <= 0.00000001f &&
                Mathf.Abs(Vector3.Dot(hit.point - reference.point, reference.normal)) <= tolerance;

            internal bool TrySetReleaseTarget(Vector3 target)
            {
                _target = target;
                return TryUpdateReach();
            }

            internal void CapturePose()
            {
                _upperRotation = Upper.localRotation;
                _lowerRotation = Lower.localRotation;
                _footRotation = Foot.localRotation;
                _toeRotation = Toes.localRotation;
                _originalFootRotation = Foot.rotation;
                _originalUpperRotation = Upper.rotation;
                _originalFootPosition = Foot.position;
                _upperToKnee = Lower.position - Upper.position;
                _kneeToFoot = Foot.position - Lower.position;
            }

            internal void RestorePose()
            {
                if (Upper != null) Upper.localRotation = _upperRotation;
                if (Lower != null) Lower.localRotation = _lowerRotation;
                if (Foot != null) Foot.localRotation = _footRotation;
                if (Toes != null) Toes.localRotation = _toeRotation;
            }

            internal bool TryCalculateTarget(float frame, HumanoidGroundResponse ground, float humanScale)
            {
                _target = _originalFootPosition;
                _targetFootRotation = _originalFootRotation;
                _support = 0f;
                _activeWeights = Vector2.zero;
                HasGround = ground.TryFindGround(_originalFootPosition, out _ground);
                Diagnostic.has_ground = HasGround;
                _clearance = humanScale;
                _maximumReach = _upperToKnee.magnitude + _kneeToFoot.magnitude;
                Diagnostic.physical_reach_m = _maximumReach;
                if (!HasGround)
                    return true;
                if (!Sampler.TrySample())
                    return false;

                frame = Mathf.Clamp(frame, 0f, _weights.Length - 1);
                int first = Mathf.FloorToInt(frame);
                int second = Mathf.Min(first + 1, _weights.Length - 1);
                float blend = frame - first;
                Vector2 weights = _hasSupportRoles
                    ? HumanoidFootSupportRoleCalculator.Interpolate(_weights[first], _weights[second], blend)
                    : Vector2.LerpUnclamped(_weights[first], _weights[second], blend);
                if (!CollectActiveContacts(frame, ground, weights))
                    return false;
                if (!AlignContactPairRotation())
                    return false;
                if (!TryAlignSupportSurface(frame)) return false;
                // 착지 램프 중 발 회전의 프레임당 변화를 제한해 발끝 팝 성분을 분산함.
                // 지지가 상승하는 부분 지지 구간에만 적용해 완전 지지·해제·슬라이드의
                // 정상 회전을 방해하지 않음.
                if (_hasPrevTargetRot && _support < 0.999f && _support > _prevSupport + 0.0001f)
                    _targetFootRotation = Quaternion.RotateTowards(_prevTargetFootRotation,
                        _targetFootRotation, 2.5f);
                _prevTargetFootRotation = _targetFootRotation;
                _hasPrevTargetRot = true;
                _prevSupport = _support;
                if (_activeWeights.x > 0f && _activeWeights.y > 0f)
                    Diagnostic.pair_span_error_m = Mathf.Abs(
                        (_localContactPoints[1] - _localContactPoints[0]).magnitude * Foot.lossyScale.x -
                        Vector3.Distance(_anchors[0], _anchors[1]));
                if (!ResolveWeightedTarget(humanScale))
                    return false;
                Diagnostic.weighted_contact_error_m = CalculatePlannedContactError(_target);
                if (!HumanoidSoleGroundConstraint.TryLiftAbovePlane(_target, _targetFootRotation,
                        Sampler.LocalSolePoints, Foot.lossyScale.x, _ground.point, _ground.normal,
                        out _target, out float lift))
                    return false;
                Diagnostic.lift_m = lift;
                Diagnostic.lifted_contact_error_m = CalculatePlannedContactError(_target);
                return TryUpdateReach();
            }

            private bool CollectActiveContacts(float frame,
                HumanoidGroundResponse ground, Vector2 weights)
            {
                for (int channel = 0; channel < 2; channel++)
                {
                    HumanoidFootAnchorPlan.Sample contact = _plan.GetSample(channel, frame);
                    _activeContacts[channel] = contact;
                    if (channel == 0) Diagnostic.rear_point_id = contact.FirstPoint;
                    else Diagnostic.front_point_id = contact.FirstPoint;
                    if (!contact.HasValue || weights[channel] <= 0f)
                        continue;
                    if (!ground.TryFindGround(contact.Anchor, out RaycastHit contactGround))
                    {
                        Diagnostic.contact_ground_misses++;
                        continue;
                    }
                    Diagnostic.mixed_ground_collider |= contactGround.collider != _ground.collider;
                    Diagnostic.maximum_ground_normal_angle_deg = Mathf.Max(
                        Diagnostic.maximum_ground_normal_angle_deg,
                        Vector3.Angle(_ground.normal, contactGround.normal));
                    Diagnostic.maximum_anchor_ground_shift_m = Mathf.Max(
                        Diagnostic.maximum_anchor_ground_shift_m,
                        Vector3.Distance(contact.Anchor, contactGround.point));
                    _anchors[channel] = contactGround.point;
                    _activeWeights[channel] = weights[channel];
                    if (!contact.TryGetLocalPoint(Sampler, out _localContactPoints[channel])) return false;
                }
                return true;
            }

            private bool AlignContactPairRotation()
            {
                if (_activeWeights.x > 0f && _activeWeights.y > 0f &&
                    _activeContacts[0].CanAlignPair && _activeContacts[1].CanAlignPair)
                {
                    Vector3 current = _originalFootRotation * (_localContactPoints[1] - _localContactPoints[0]);
                    Vector3 desired = _anchors[1] - _anchors[0];
                    if (current.sqrMagnitude < 0.00000001f || desired.sqrMagnitude < 0.00000001f ||
                        Vector3.Dot(current.normalized, desired.normalized) < -0.99f) return false;
                    _targetFootRotation = Quaternion.Slerp(_originalFootRotation,
                        Quaternion.FromToRotation(current, desired) * _originalFootRotation,
                        Mathf.Min(_activeWeights.x, _activeWeights.y));
                }
                return true;
            }

            private bool ResolveWeightedTarget(float humanScale)
            {
                Vector3 weightedTarget = Vector3.zero;
                Vector3 fullSupportTarget = Vector3.zero;
                float totalWeight = 0f;
                int fullSupportCount = 0;
                for (int channel = 0; channel < 2; channel++)
                {
                    if (_activeWeights[channel] <= 0f) continue;
                    if (!HumanoidFootPivotCalculator.TryCalculatePose(_originalFootPosition,
                            _originalFootRotation, _localContactPoints[channel], Foot.lossyScale.x, _anchors[channel],
                            _targetFootRotation, 1f, out Vector3 candidate, out _))
                        return false;
                    weightedTarget += candidate * _activeWeights[channel];
                    totalWeight += _activeWeights[channel];
                    if (_activeWeights[channel] < 0.999f) continue;
                    fullSupportTarget = candidate;
                    fullSupportCount++;
                }
                _support = Mathf.Max(_activeWeights.x, _activeWeights.y);
                if (totalWeight > 0f)
                {
                    Vector3 target = weightedTarget / totalWeight;
                    if (fullSupportCount == 1 && _activeWeights.x > 0f && _activeWeights.y > 0f &&
                        Diagnostic.pair_span_error_m <= humanScale * SupportedContactErrorPerHumanScale)
                    {
                        // 지지점 간격이 양립할 때만 부분 지지가 완전 지지 목표를 밀어내지 않게 함.
                        Vector3 partialPull = target - fullSupportTarget;
                        float maximumPull = humanScale * SupportedContactErrorPerHumanScale * 0.9f;
                        if (partialPull.magnitude > maximumPull)
                            target = fullSupportTarget + partialPull.normalized * maximumPull;
                    }
                    _target = Vector3.LerpUnclamped(_originalFootPosition, target, _support);
                }
                return true;
            }

            private bool TryUpdateReach()
            {
                _clearance = Mathf.Max(0f, CalculateSoleClearance(_target, _targetFootRotation));
                float originalAngle = Vector3.Angle(-_upperToKnee, _kneeToFoot) * Mathf.Deg2Rad;
                Diagnostic.hip_to_target_m = Vector3.Distance(Upper.position, _target);
                bool hasReach = HumanoidPelvisReachCalculator.TryCalculateExtensionReach(
                    _upperToKnee.magnitude, _kneeToFoot.magnitude, originalAngle,
                    Diagnostic.hip_to_target_m, _support, HumanoidPelvisReachCalculator.DefaultDampingThresholdRadians,
                    out _maximumReach);
                Diagnostic.damped_reach_m = _maximumReach;
                return hasReach;
            }

            private bool TryAlignSupportSurface(float frame)
            {
                if ((_activeWeights.x <= 0f && _activeWeights.y <= 0f) || !_localFootFrame.HasValue)
                    return true;
                Vector3 queryNormal = _originalFootRotation *
                    (Quaternion.Inverse(_targetFootRotation) * _ground.normal);
                if (!Sampler.TrySelectContact(false, queryNormal.normalized, out int rear, out _) ||
                    !Sampler.TrySelectContact(true, queryNormal.normalized, out int front, out _) ||
                    !Sampler.TryGetLocalPoint(rear, out Vector3 rearPoint) ||
                    !Sampler.TryGetLocalPoint(front, out Vector3 frontPoint)) return false;

                float difference = Vector3.Dot(_targetFootRotation * (rearPoint - frontPoint), _ground.normal);
                float weight = difference > 0f ? _activeWeights.x : _activeWeights.y;
                if (weight <= 0f && _relativeHeights != null)
                {
                    int first = Mathf.FloorToInt(frame);
                    int second = Mathf.Min(first + 1, _relativeHeights.Length - 1);
                    float sourceHeight = Mathf.Lerp(_relativeHeights[first], _relativeHeights[second], frame - first);
                    float oppositeWeight = difference > 0f ? _activeWeights.y : _activeWeights.x;
                    // 거의 평평한 자세는 단일 지지로 분류되어도 밑창의 들린 쪽을 함께 내림.
                    weight = oppositeWeight * Mathf.Clamp01(2f - Mathf.Abs(sourceHeight) / _nearLevelHeight);
                }
                Vector3 axis = Vector3.Cross(_ground.normal,
                    _targetFootRotation * (_localFootFrame.Value * Vector3.forward)).normalized;
                if (axis.sqrMagnitude < 0.5f) return true;
                if (weight > 0f && HumanoidFootAnchorPlan.TryFindSupportPose(Foot, Sampler, axis,
                        _targetFootRotation, _ground.normal, out Quaternion rotation, out _, out _))
                {
                    // 들리는 영역의 지지 강도만 반영하여 해제 중인 발을 수평으로 강제하지 않음.
                    _targetFootRotation = Quaternion.Slerp(_targetFootRotation, rotation, weight);
                }
                if (difference > 0f && _activeWeights.x <= 0f && _activeWeights.y > 0f)
                {
                    Vector3 forward = _targetFootRotation * (_localFootFrame.Value * Vector3.forward);
                    float pitch = Mathf.Asin(Mathf.Clamp(Vector3.Dot(forward, _ground.normal), -1f, 1f)) *
                        Mathf.Rad2Deg;
                    if (pitch < -ToePitchSoftKneeDeg)
                    {
                        // 앞꿈치 지지는 유지하되 과도한 발끝 하향만 완만히 압축 — tanh로
                        // 상한에 점근시켜 캡 경계의 각도 단절을 없앰.
                        float span = MaximumSupportedToePitch - ToePitchSoftKneeDeg;
                        float limited = -(ToePitchSoftKneeDeg +
                            span * (float)System.Math.Tanh((-pitch - ToePitchSoftKneeDeg) / span));
                        Quaternion corrected = Quaternion.AngleAxis(pitch - limited, axis) *
                            _targetFootRotation;
                        _targetFootRotation = Quaternion.Slerp(_targetFootRotation, corrected, _activeWeights.y);
                    }
                }
                return true;
            }

            internal HumanoidPelvisReachCalculator.Leg BuildReach(bool dampExtension) =>
                new HumanoidPelvisReachCalculator.Leg(Upper.position - _target,
                    _upperToKnee.magnitude, _kneeToFoot.magnitude, _support, _clearance,
                    dampExtension ? _maximumReach : (double)_upperToKnee.magnitude + _kneeToFoot.magnitude);

            internal float TotalBoneLength => _upperToKnee.magnitude + _kneeToFoot.magnitude;

            internal bool TrySolve(float footOffset, out float error)
            {
                _target += Vector3.up * footOffset;
                Diagnostic.foot_offset_m = footOffset;
                Diagnostic.offset_contact_error_m = CalculatePlannedContactError(_target);
                Diagnostic.hip_to_target_m = Vector3.Distance(Upper.position, _target);
                error = Vector3.Distance(Foot.position, _target);
                // 목표 변위가 0에 가까워져도 무릎 방향 보정을 계속 적용하여 자세가 튀지 않게 함.
                if (!HumanoidLegPoseSolver.TryCalculateBendNormal(_upperToKnee, _kneeToFoot,
                        _target - Upper.position, _originalUpperRotation * _referenceNormal,
                        out Vector3 normal, out _) ||
                    !HumanoidLegPoseSolver.TrySolve(Upper, Lower, Foot, _target, normal, out error))
                    return false;
                Foot.rotation = _targetFootRotation;
                Toes.localRotation = _toeRotation;
                Diagnostic.target_error_m = error;
                return true;
            }

            internal bool TryMeasure(out float clearance, out float contactError, out int contactCount)
            {
                clearance = 0f;
                contactError = 0f;
                contactCount = 0;
                if (!HasGround)
                    return true;
                if (!Sampler.TrySample())
                    return false;
                clearance = CalculateSoleClearance(Foot.position, Foot.rotation);
                for (int channel = 0; channel < 2; channel++)
                {
                    if (!_activeContacts[channel].HasValue || _activeWeights[channel] < 0.999f)
                        continue;
                    if (!_activeContacts[channel].TryGetLocalPoint(Sampler, out Vector3 point))
                        return false;
                    if (!Sampler.TrySelectContact(channel == 1, _ground.normal, out _,
                            out Vector3 currentContact))
                        return false;
                    Vector3 plannedContact = Foot.TransformPoint(point);
                    // 계획 지지점의 수평 고정과 현재 밑창 접촉의 높이를 따로 검사함.
                    // 발 구르기 중 접촉 정점이 바뀌어도 실제 닿은 지지 영역을 거절하지 않음.
                    Vector3 planarError = Vector3.ProjectOnPlane(plannedContact - _anchors[channel], _ground.normal);
                    float heightError = Vector3.Dot(currentContact - _anchors[channel], _ground.normal);
                    contactCount++;
                    contactError = Mathf.Max(contactError,
                        Mathf.Sqrt(planarError.sqrMagnitude + heightError * heightError));
                }
                Diagnostic.supported_contact_error_m = contactError;
                Diagnostic.supported_contact_count = contactCount;
                return true;
            }

            private float CalculatePlannedContactError(Vector3 position)
            {
                float error = 0f;
                for (int channel = 0; channel < 2; channel++)
                {
                    if (_activeWeights[channel] < 0.999f) continue;
                    Vector3 point = position + _targetFootRotation *
                        (_localContactPoints[channel] * Foot.lossyScale.x);
                    error = Mathf.Max(error, Vector3.Distance(point, _anchors[channel]));
                }
                return error;
            }

            private float CalculateSoleClearance(Vector3 position, Quaternion rotation)
            {
                float minimum = float.PositiveInfinity;
                foreach (Vector3 point in Sampler.LocalSolePoints)
                    minimum = Mathf.Min(minimum, Vector3.Dot(position + rotation * (point * Foot.lossyScale.x) -
                        _ground.point, _ground.normal) / _ground.normal.y);
                return minimum;
            }
        }
    }
}
