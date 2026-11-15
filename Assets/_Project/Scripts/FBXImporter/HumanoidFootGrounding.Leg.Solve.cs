using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class HumanoidFootGrounding
    {
        private sealed partial class Leg
        {
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
                if (!ResolveFrameContacts(frame, ground)) return false;
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
                        (_localContactPoints[1] - _localContactPoints[0]).magnitude * Foot.lossyScale.x - Vector3.Distance(_anchors[0], _anchors[1]));
                return ResolveTargetLiftAndReach(humanScale); }

            private bool ResolveTargetLiftAndReach(float humanScale)
            {
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

            private bool ResolveFrameContacts(float frame, HumanoidGroundResponse ground)
            {
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
                return TryAlignSupportSurface(frame);
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
                return true; }

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
                    Vector3 target = ClampPartialSupportPull(
                        weightedTarget / totalWeight, fullSupportTarget,
                        fullSupportCount, humanScale);
                    _target = Vector3.LerpUnclamped(_originalFootPosition, target, _support);
                }
                return true; }

            // 지지점 간격이 양립할 때만 부분 지지가 완전 지지 목표를 밀어내지 않게 함.
            private Vector3 ClampPartialSupportPull(
                Vector3 target, Vector3 fullSupportTarget,
                int fullSupportCount, float humanScale)
            {
                if (fullSupportCount == 1 && _activeWeights.x > 0f && _activeWeights.y > 0f &&
                    Diagnostic.pair_span_error_m <= humanScale * SupportedContactErrorPerHumanScale)
                {
                    Vector3 partialPull = target - fullSupportTarget;
                    float maximumPull = humanScale * SupportedContactErrorPerHumanScale * 0.9f;
                    if (partialPull.magnitude > maximumPull)
                        return fullSupportTarget + partialPull.normalized * maximumPull;
                }
                return target;
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
                float weight = ResolveSurfaceWeight(frame, difference);
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
                    ApplyToePitchSoftClamp(axis);
                }
                return true;
            }

            private float ResolveSurfaceWeight(float frame, float difference)
            {
                float weight = difference > 0f ? _activeWeights.x : _activeWeights.y;
                if (weight > 0f || _relativeHeights == null)
                    return weight;
                int first = Mathf.FloorToInt(frame);
                int second = Mathf.Min(first + 1, _relativeHeights.Length - 1);
                float sourceHeight = Mathf.Lerp(_relativeHeights[first], _relativeHeights[second], frame - first);
                float oppositeWeight = difference > 0f ? _activeWeights.y : _activeWeights.x;
                // 거의 평평한 자세는 단일 지지로 분류되어도 밑창의 들린 쪽을 함께 내림.
                return oppositeWeight * Mathf.Clamp01(2f - Mathf.Abs(sourceHeight) / _nearLevelHeight);
            }

            // 앞꿈치 지지는 유지하되 과도한 발끝 하향만 완만히 압축 — tanh로
            // 상한에 점근시켜 캡 경계의 각도 단절을 없앰.
            private void ApplyToePitchSoftClamp(Vector3 axis)
            {
                Vector3 forward = _targetFootRotation * (_localFootFrame.Value * Vector3.forward);
                float pitch = Mathf.Asin(Mathf.Clamp(Vector3.Dot(forward, _ground.normal), -1f, 1f)) *
                    Mathf.Rad2Deg;
                if (pitch >= -ToePitchSoftKneeDeg)
                    return;
                float span = MaximumSupportedToePitch - ToePitchSoftKneeDeg;
                float limited = -(ToePitchSoftKneeDeg +
                    span * (float)System.Math.Tanh((-pitch - ToePitchSoftKneeDeg) / span));
                Quaternion corrected = Quaternion.AngleAxis(pitch - limited, axis) *
                    _targetFootRotation;
                _targetFootRotation = Quaternion.Slerp(_targetFootRotation, corrected, _activeWeights.y);
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
                    if (!Sampler.TrySelectContact(channel == 1, _ground.normal, out _, out Vector3 currentContact))
                        return false;
                    Vector3 plannedContact = Foot.TransformPoint(point);
                    // 계획 지지점의 수평 고정과 현재 밑창 접촉의 높이를 따로 검사함.
                    // 발 구르기 중 접촉 정점이 바뀌어도 실제 닿은 지지 영역을 거절하지 않음.
                    Vector3 planarError = Vector3.ProjectOnPlane(plannedContact - _anchors[channel], _ground.normal);
                    float heightError = Vector3.Dot(currentContact - _anchors[channel], _ground.normal);
                    contactCount++;
                    contactError = Mathf.Max(contactError, Mathf.Sqrt(planarError.sqrMagnitude + heightError * heightError)); }
                Diagnostic.supported_contact_error_m = contactError;
                Diagnostic.supported_contact_count = contactCount;
                return true; }

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

            internal bool TryGetRegionHeights(out Vector2 heights)
            {
                heights = default;
                if (!HasGround || !Sampler.TrySelectContact(false, _ground.normal, out _, out Vector3 rear) ||
                    !Sampler.TrySelectContact(true, _ground.normal, out _, out Vector3 front))
                    return false;
                heights = new Vector2(Vector3.Dot(rear - _ground.point, _ground.normal),
                    Vector3.Dot(front - _ground.point, _ground.normal));
                return true;
            }

            private bool NeedsPelvisLowering()
            {
                Vector3 targetToHip = Upper.position - _target;
                float reachSquared = _maximumReach * _maximumReach;
                float horizontalSquared = targetToHip.x * targetToHip.x + targetToHip.z * targetToHip.z;
                return targetToHip.y > 0f && horizontalSquared < reachSquared && targetToHip.sqrMagnitude > reachSquared;
            }

            private float _support, _clearance, _maximumReach;
            private RaycastHit _ground;
            // 착지 램프 중 목표 발 회전의 프레임당 변화 상한에 사용하는 직전 상태임.
            private Quaternion _prevTargetFootRotation;
            private float _prevSupport;
            private bool _hasPrevTargetRot;

        }
    }
}
