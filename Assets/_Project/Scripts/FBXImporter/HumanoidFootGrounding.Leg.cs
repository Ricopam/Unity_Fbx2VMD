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

                FillGroundSample(ref sample, rearPoint, frontPoint,
                    rearRenderer, frontRenderer, rearVertex, frontVertex);
                return true;
            }

            private void FillGroundSample(ref HumanoidFootGroundingSnapshot sample,
                Vector3 rearPoint, Vector3 frontPoint,
                SkinnedMeshRenderer rearRenderer, SkinnedMeshRenderer frontRenderer,
                int rearVertex, int frontVertex)
            {
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
                    _hasSupportRoles = true; }
                _relativeHeights = relativeHeights;
                _nearLevelHeight = sourceScale / 70f;
                return BuildAnchorPlan( intents, sourceScale, scaleRatio, frameRate, clipLength, evaluate, sourceRotation, sourceFrames, hasSourceFrames); }

            // 의도 구간을 프레임별 앵커 방침으로 변환하고 자유 구간은 속도 상한을 둠.
            private bool BuildAnchorPlan(
                IReadOnlyList<HumanoidFootContactIntent> intents,
                float sourceScale, float scaleRatio, float frameRate,
                float clipLength, Action<float> evaluate,
                Quaternion sourceRotation, Quaternion[] sourceFrames,
                bool hasSourceFrames)
            {
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
                CollectChannelSamples(samples, isLeft, ref referenceHeights);
                return true;
            }

            private void CollectChannelSamples(
                IReadOnlyList<HumanoidFootContactSample> samples, bool isLeft,
                ref Vector2 referenceHeights)
            {
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
                    Diagnostic.Reset(); } }

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
        }
    }
}
