using System;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 지지점 신원과 공통 이동을 미리 계산하여 탐색 순서와 무관하게 평가함.
    /// </summary>
    internal sealed partial class HumanoidFootAnchorPlan
    {
        // 방출 앵커가 목표에 뒤처진 갭을 비율로 회수하되 절대 상한을 두어
        // 방침 전환·핀 시작 경계에서도 프레임당 이동이 제한된 채 유지되게 함.
        private const float AnchorCatchUpFraction = 0.34f;
        private const float AnchorCatchUpStepFactor = 4f;
        // 이 프레임 수를 넘는 공백 뒤의 재획득은 새 착지로 보고 앵커를 재샘플함.
        private const int MaxReacquireGapFrames = 6;
        // 재획득 시드는 소멸 직전 발이 실제로 앵커에 붙어 있던 경우에만 유효함.
        // 감쇠 중 소멸하면 발은 이미 원시 위치로 돌아간 뒤라 옛 앵커 부활이 팝이 됨.
        private const float MinReacquireSeedWeight = 0.9f;
        private readonly Frame[][] _frames;
        internal int UnresolvedPairCount { get; private set; }
        internal string FirstIssue { get; private set; }

        private HumanoidFootAnchorPlan(int count)
        {
            _frames = new[] { new Frame[count], new Frame[count] };
        }

        internal readonly struct Sample
        {
            private readonly int _firstPoint;
            private readonly int _secondPoint;
            private readonly float _blend;
            internal readonly Vector3 Anchor;
            internal readonly bool CanAlignPair;
            internal int FirstPoint => _firstPoint;
            internal bool HasValue => _firstPoint >= 0;
            internal bool IsInterpolatedPoint => _firstPoint != _secondPoint && _blend > 0f && _blend < 1f;

            internal Sample(int firstPoint, int secondPoint, float blend, Vector3 anchor, bool canAlignPair)
            {
                _firstPoint = firstPoint;
                _secondPoint = secondPoint;
                _blend = blend;
                Anchor = anchor;
                CanAlignPair = canAlignPair;
            }

            internal bool TryGetLocalPoint(HumanoidFootSoleSampler sampler, out Vector3 point)
            {
                point = Vector3.zero;
                if (!sampler.TryGetLocalPoint(_firstPoint, out Vector3 first) ||
                    !sampler.TryGetLocalPoint(_secondPoint, out Vector3 second)) return false;
                // 신원 전환 사이의 가상 접촉점도 앵커와 같은 계수로 보간함.
                point = Vector3.LerpUnclamped(first, second, _blend);
                return true;
            }
        }

        internal Sample GetSample(int channel, float frame)
        {
            frame = Mathf.Clamp(frame, 0f, _frames[channel].Length - 1);
            int firstIndex = Mathf.FloorToInt(frame);
            Frame first = _frames[channel][firstIndex];
            Frame second = _frames[channel][Mathf.Min(firstIndex + 1, _frames[channel].Length - 1)];
            if (first.Point < 0) first = second;
            if (second.Point < 0) second = first;
            float blend = frame - firstIndex;
            // 접촉점 신원이 갈리는 경계는 지면과 무관한 가상점 보간 대신
            // 해당 프레임의 실제 피벗과 앵커를 그대로 교체함.
            if (first.Point >= 0 && second.Point >= 0 && first.Point != second.Point)
            {
                Frame chosen = blend < 0.5f ? first : second;
                return new Sample(chosen.Point, chosen.Point, 0f, chosen.Anchor,
                    first.CanAlignPair || second.CanAlignPair);
            }
            return new Sample(first.Point, second.Point, blend,
                Vector3.LerpUnclamped(first.Anchor, second.Anchor, blend), first.CanAlignPair || second.CanAlignPair);
        }

        internal static bool TryBuild(Transform foot, HumanoidFootSoleSampler sampler,
            Vector3[][] source, Vector2[] weights, Quaternion sourceRotation, float scaleRatio,
            float frameRate, float clipLength, Action<float> evaluate, out HumanoidFootAnchorPlan plan,
            Quaternion[] sourceFrames = null, Quaternion? localFootFrame = null,
            HumanoidFootAnchorPolicy[] anchorPolicies = null,
            float pinReleaseSourceDistance = 0f, float anchorTrackStep = 0f)
        {
            plan = null;
            if (foot == null || sampler == null || source == null || source.Length != 2 || weights == null ||
                weights.Length == 0 || source[0].Length != weights.Length || source[1].Length != weights.Length ||
                evaluate == null || frameRate <= 0f || scaleRatio <= 0f ||
                !HasValidFrames(sourceFrames, localFootFrame, weights.Length) ||
                (anchorPolicies != null && anchorPolicies.Length != weights.Length)) return false;
            var session = new BuildSession(
                foot, sampler, source, weights, sourceRotation, scaleRatio,
                frameRate, clipLength, evaluate, sourceFrames, localFootFrame,
                anchorPolicies, pinReleaseSourceDistance, anchorTrackStep);
            if (!session.Run()) return false;
            plan = session.Result;
            return true;
        }


        private static bool HasValidFrames(Quaternion[] sourceFrames, Quaternion? localFootFrame, int count)
        {
            if ((sourceFrames != null) != localFootFrame.HasValue) return false;
            if (sourceFrames == null) return true;
            if (sourceFrames.Length != count || !IsUnitRotation(localFootFrame.Value)) return false;
            foreach (Quaternion frame in sourceFrames)
                if (!IsUnitRotation(frame)) return false;
            return true;
        }

        private static bool IsUnitRotation(Quaternion rotation)
        {
            float length = Quaternion.Dot(rotation, rotation);
            return !float.IsNaN(length) && !float.IsInfinity(length) && Mathf.Abs(length - 1f) < 0.0001f;
        }

        private static bool TryCreatePair(Transform foot, HumanoidFootSoleSampler sampler,
            Contact[] states, Vector3[][] source, int frame, Quaternion sourceRotation, out Pair pair)
        {
            pair = null;
            Vector3 direction = Vector3.ProjectOnPlane(sourceRotation * (source[1][frame] - source[0][frame]), Vector3.up);
            if (direction.sqrMagnitude < 0.00000001f ||
                !sampler.TryGetLocalPoint(states[0].Point, out Vector3 originalRear) ||
                !sampler.TryGetLocalPoint(states[1].Point, out Vector3 originalFront)) return false;
            Vector3 axis = Vector3.Cross(Vector3.up, foot.TransformVector(originalFront - originalRear)).normalized;
            if (axis.sqrMagnitude < 0.5f || !TryFindSupportPose(foot, sampler, axis, foot.rotation, Vector3.up,
                    out Quaternion rotation, out int rear, out int front)) return false;
            if (!sampler.TryGetLocalPoint(rear, out Vector3 newRear) ||
                !sampler.TryGetLocalPoint(front, out Vector3 newFront)) return false;
            int primary = states[0].StartFrame <= states[1].StartFrame ? 0 : 1;
            Vector3 offset = Vector3.ProjectOnPlane(rotation * ((newFront - newRear) * foot.lossyScale.x), Vector3.up);
            if (offset.sqrMagnitude < 0.00000001f) return false;
            // 접촉 정점 신원이 바뀌면 앵커를 실제 정점 이동량만큼 옮겨 발 자세 연속을 유지함.
            // 핀 고정 접촉도 보정하지 않으면 구 정점 위치의 앵커에 새 정점이 끌려가 팝이 됨.
            // 정점이 그대로인 채널은 이동량이 0이라 영향이 없음.
            for (int channel = 0; channel < 2; channel++)
            {
                Vector3 vertexShift = channel == 0 ? newRear - originalRear : newFront - originalFront;
                states[channel].Anchor += Vector3.ProjectOnPlane(
                    rotation * (vertexShift * foot.lossyScale.x), Vector3.up);
            }
            states[0].Point = rear;
            states[1].Point = front;
            pair = new Pair(primary, direction, primary == 0 ? offset : -offset);
            return true;
        }

        internal static bool TryFindSupportPose(Transform foot, HumanoidFootSoleSampler sampler, Vector3 axis, Quaternion referenceRotation, Vector3 groundNormal, out Quaternion rotation, out int rear, out int front)
        {
            rotation = referenceRotation;
            rear = front = -1;
            Quaternion original = rotation;
            float Evaluate(float angle, out int rearPoint, out int frontPoint)
            {
                rearPoint = frontPoint = -1;
                Quaternion candidate = Quaternion.AngleAxis(angle, axis) * original;
                Vector3 normal = foot.rotation * (Quaternion.Inverse(candidate) * groundNormal);
                if (!sampler.TrySelectContact(false, normal.normalized, out rearPoint, out _) || !sampler.TrySelectContact(true, normal.normalized, out frontPoint, out _) || !sampler.TryGetLocalPoint(rearPoint, out Vector3 rearLocal) || !sampler.TryGetLocalPoint(frontPoint, out Vector3 frontLocal)) return float.NaN;
                return Vector3.Dot(candidate * ((rearLocal - frontLocal) * foot.lossyScale.x), groundNormal); }
            float best = float.PositiveInfinity;
            float leftValue = Evaluate(-45f, out _, out _);
            // 단조성을 가정하지 않고 짧은 구간별로 해를 찾은 뒤 원본에 가장 가까운 각도를 선택함.
            for (int step = -44; step <= 45; step++)
            {
                float rightValue = Evaluate(step, out _, out _);
                if (float.IsNaN(leftValue) || float.IsNaN(rightValue)) return false;
                if (leftValue * rightValue <= 0f)
                {
                    float left = step - 1f, right = step, value = leftValue;
                    for (int iteration = 0; iteration < 18; iteration++)
                    {
                        float middle = (left + right) * 0.5f;
                        float middleValue = Evaluate(middle, out _, out _);
                        if (value * middleValue <= 0f) right = middle;
                        else { left = middle; value = middleValue; } }
                    float angle = (left + right) * 0.5f;
                    if (Mathf.Abs(angle) < Mathf.Abs(best)) best = angle; }
                leftValue = rightValue; }
            if (float.IsInfinity(best)) return false;
            Evaluate(best, out rear, out front);
            rotation = Quaternion.AngleAxis(best, axis) * original;
            return true; }

        // 추종 지연은 갭 비율로 회수하되 프레임 이동 절대 상한을 넘기지 않음.
        private static Vector3 MoveEmittedAnchor(Vector3 emitted, Vector3 target, float trackStep)
        {
            float step = Mathf.Clamp(Vector3.Distance(emitted, target) * AnchorCatchUpFraction,
                trackStep, trackStep * AnchorCatchUpStepFactor);
            return Vector3.MoveTowards(emitted, target, step);
        }

        // 독립 이동 제한으로 줄어든 쌍 간격을 복원하되 보조점은 직전 위치의 이동 상한 안에 둠.
        private static Vector3 ConstrainPairAnchor(Vector3 primary, Vector3 secondary, float span,
            Vector3? start, float maximumStep)
        {
            Vector3 planar = Vector3.ProjectOnPlane(secondary - primary, Vector3.up);
            if (planar.sqrMagnitude < 0.00000001f) return secondary;
            Vector3 center = secondary - planar;
            Vector3 offset = planar.normalized * span;
            Vector3 aligned = center + offset;
            if (!start.HasValue || (aligned - start.Value).sqrMagnitude <= maximumStep * maximumStep)
                return aligned;

            Vector3 fromCenter = start.Value - center;
            Vector3 previous = Vector3.ProjectOnPlane(fromCenter, Vector3.up);
            float previousRadius = previous.magnitude;
            float planarBudgetSquared = maximumStep * maximumStep - fromCenter.y * fromCenter.y;
            // 두 원이 접할 때 float 반올림만으로 교집합이 사라지지 않게 함.
            float closestDistance = Mathf.Max(0f, Mathf.Abs(previousRadius - span) - 0.000001f);
            // 두 제한의 교집합이 없으면 기존 제한 이동을 유지함.
            if (previousRadius < 0.0001f || planarBudgetSquared < closestDistance * closestDistance)
                return secondary;
            float cosine = (previousRadius * previousRadius + span * span - planarBudgetSquared) /
                (2f * previousRadius * span);
            float maximumAngle = Mathf.Acos(Mathf.Clamp(cosine, -1f, 1f)) * Mathf.Rad2Deg;
            float desiredAngle = Vector3.SignedAngle(previous, offset, Vector3.up);
            Quaternion turn = Quaternion.AngleAxis(Mathf.Clamp(desiredAngle, -maximumAngle, maximumAngle), Vector3.up);
            return center + turn * (previous / previousRadius * span);
        }

        private void RecordIssue(int frame, string reason)
        {
            UnresolvedPairCount++;
            if (FirstIssue == null) FirstIssue = $"프레임 {frame}: {reason}";
        }

        private readonly struct Frame
        {
            internal readonly int Point;
            internal readonly Vector3 Anchor;
            internal readonly bool CanAlignPair;
            internal Frame(int point, Vector3 anchor, bool canAlignPair)
            { Point = point; Anchor = anchor; CanAlignPair = canAlignPair; }
        }

        private sealed class Contact
        {
            internal readonly int StartFrame;
            internal int Point;
            internal Vector3 Anchor;
            internal Vector3 SourcePoint;
            internal bool Pinned { get; private set; }
            private Vector3 _pinSource;
            private bool _pinBlocked;

            internal Contact(int startFrame, int point, Vector3 anchor, Vector3 sourcePoint)
            { StartFrame = startFrame; Point = point; Anchor = anchor; SourcePoint = sourcePoint; }

            internal Vector3 GetAnchor(Vector3 source, Quaternion rotation, float scaleRatio) =>
                Anchor + Vector3.ProjectOnPlane(rotation * (source - SourcePoint), Vector3.up) * scaleRatio;

            // 확정 Plant 구간에서는 지금까지 추종한 위치를 진입 시점에 접어 앵커를 고정함.
            // 원본이 핀 시작점에서 임계 이상 움직이면 의도 추정 오류로 보고 남은 수명 동안 핀을 해제함.
            internal void ApplyAnchorPolicy(Vector3 source, Quaternion rotation, float scaleRatio,
                HumanoidFootAnchorPolicy policy, float pinReleaseSourceDistance)
            {
                if (policy != HumanoidFootAnchorPolicy.Pinned || _pinBlocked)
                {
                    Pinned = false;
                    return;
                }
                if (!Pinned)
                {
                    Anchor = GetAnchor(source, rotation, scaleRatio);
                    _pinSource = source;
                    Pinned = true;
                }
                else if (pinReleaseSourceDistance > 0f &&
                    Vector3.ProjectOnPlane(source - _pinSource, Vector3.up).magnitude >=
                        pinReleaseSourceDistance)
                {
                    Pinned = false;
                    _pinBlocked = true;
                    // 해제 순간에도 앵커가 튀지 않게 기준점을 현재 원본으로 넘김.
                    SourcePoint = source;
                }
                // 핀 동안 기준점을 매 프레임 갱신해 GetAnchor가 고정 앵커를 반환하게 함.
                if (Pinned) SourcePoint = source;
            }
        }

        private sealed class Pair
        {
            internal readonly int Primary;
            internal readonly Vector3 SourceDirection;
            internal readonly Vector3 Offset;
            internal Pair(int primary, Vector3 sourceDirection, Vector3 offset)
            { Primary = primary; SourceDirection = sourceDirection; Offset = offset; }
        }
    }
}
