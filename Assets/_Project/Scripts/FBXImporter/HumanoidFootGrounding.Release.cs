using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class HumanoidFootGrounding
    {
        private const float MaximumReleaseTargetShift = 0.01f;
        private const float MaximumReleaseKneeChange = 1f;
        private const float MaximumReleasePelvisChange = 0.001f;
        private readonly Dictionary<int, ReleaseSmoothingWindow> _releaseSmoothingWindows = new();

        private sealed class ReleaseSmoothingWindow
        {
            internal readonly Leg Leg;
            internal readonly int Start;
            internal readonly int End;
            internal readonly int Channel;
            internal readonly int Point;
            internal readonly RaycastHit Ground;
            internal readonly Matrix4x4 GroundMatrix;
            internal readonly Vector3 Direction;
            internal readonly float[] Offsets;

            internal ReleaseSmoothingWindow(Leg leg, int start, int end, int channel, int point,
                RaycastHit ground, Vector3 direction, float[] offsets)
            {
                Leg = leg;
                Start = start;
                End = end;
                Channel = channel;
                Point = point;
                Ground = ground;
                GroundMatrix = ground.collider.transform.localToWorldMatrix;
                Direction = direction;
                Offsets = offsets;
            }

            internal Vector3 GetShift(float frame)
            {
                int index = Mathf.Clamp(Mathf.FloorToInt(frame) - Start, 0, Offsets.Length - 2);
                return Direction * Mathf.SmoothStep(Offsets[index], Offsets[index + 1], frame - Start - index);
            }
        }

        internal void PrepareReleaseSmoothing(AnimationClip clip, Action<float> evaluate,
            HumanoidGroundResponse ground)
        {
            _releaseSmoothingWindows.Clear();
            try
            {
                PrepareLeg(_left, clip, evaluate, ground);
                PrepareLeg(_right, clip, evaluate, ground);
            }
            finally
            {
                RestoreAppliedPose();
                _left.ResetPreparationHistory();
                _right.ResetPreparationHistory();
                evaluate(0f);
            }
        }

        void PrepareLeg(Leg leg, AnimationClip clip, Action<float> evaluate, HumanoidGroundResponse ground)
        {
            for (int start = 0; start + 2 < leg.WeightFrameCount; start++)
            {
                if (!leg.TryGetReleaseRange(start, out int end, out int channel, out var contact) || !leg.HasAbruptRelease(start, end, _sourceScaleRatio))
                    continue;
                bool overlaps = false;
                for (int frame = start; frame < end && !overlaps; frame++)
                    overlaps = _releaseSmoothingWindows.ContainsKey(frame) || _touchdownWindows.ContainsKey(frame) || _left.HasReleaseReference(frame) || _right.HasReleaseReference(frame);
                if (overlaps)
                    continue;

                var targets = new Vector3[end - start + 1];
                bool valid = TryCollectReleaseTargets(
                    leg, start, end, clip, evaluate, ground, targets,
                    out Vector3 rawStart, out Vector3 rawEnd, out RaycastHit surface);

                Vector3 direction = Vector3.ProjectOnPlane(rawEnd - rawStart, Vector3.up);
                if (!valid || direction.sqrMagnitude < 0.000001f || !TryBuildReleaseOffsets(targets, direction.normalized, MaximumReleaseTargetShift, out var offsets))
                    continue;

                var candidate = new ReleaseSmoothingWindow(leg, start, end, channel, contact.FirstPoint, surface, direction.normalized, offsets);
                if (!PreservesReleaseSmoothing(candidate, clip, evaluate, ground))
                    continue;
                for (int frame = start; frame < end; frame++)
                    _releaseSmoothingWindows.Add(frame, candidate);
            }
        }

        // 릴리스 구간의 프레임별 보정 타깃을 샘플링해 실패 시 false를 반환함.
        private bool TryCollectReleaseTargets(
            Leg leg, int start, int end, AnimationClip clip, Action<float> evaluate,
            HumanoidGroundResponse ground, Vector3[] targets,
            out Vector3 rawStart, out Vector3 rawEnd, out RaycastHit surface)
        {
            rawStart = Vector3.zero;
            rawEnd = Vector3.zero;
            surface = default;
            _left.ResetPreparationHistory();
            _right.ResetPreparationHistory();
            for (int frame = start; frame <= end; frame++)
            {
                RestoreAppliedPose();
                float time = Mathf.Min(frame / _frameRate, clip.length);
                evaluate(time);
                if (frame == start) rawStart = leg.Foot.position;
                if (frame == end) rawEnd = leg.Foot.position;
                if (!TryApplyCore(time, ground, null, false, out _, out _) || (frame == start && !leg.TryReadReleaseSurface(out surface)))
                    return false;
                targets[frame - start] = leg.CaptureTarget().Position;
            }
            return true;
        }

        private bool PreservesReleaseSmoothing(ReleaseSmoothingWindow candidate, AnimationClip clip, Action<float> evaluate, HumanoidGroundResponse ground)
        {
            _left.ResetPreparationHistory();
            _right.ResetPreparationHistory();
            float maxIntegerBaseline = 0f, maxIntegerCandidate = 0f;
            float maxBaseline = 0f, maxCandidate = 0f;
            Vector3 previousBaseline = default, previousCandidate = default;
            Vector3 integerBaseline = default, integerCandidate = default;
            int startTick = candidate.Start * TouchdownSamplesPerFrame, endTick = candidate.End * TouchdownSamplesPerFrame;
            for (int tick = startTick; tick <= endTick; tick++)
            {
                float frame = (float)tick / TouchdownSamplesPerFrame;
                bool integer = tick % TouchdownSamplesPerFrame == 0;
                bool checkBoundary = integer && tick > startTick;
                TouchdownPose beforeBaseline = default, beforeCandidate = default;
                if (checkBoundary && !TrySampleReleasePose(frame - 0.001f, clip, evaluate, ground, candidate, out beforeBaseline, out beforeCandidate)) return false;
                if (!TrySampleReleasePose(frame, clip, evaluate, ground, candidate, out var baseline, out var correctedPose) || (checkBoundary && !correctedPose.PreservesBoundary(beforeCandidate, baseline, beforeBaseline))) return false;

                bool left = candidate.Leg == _left;
                Vector3 original = left ? baseline.LeftFoot : baseline.RightFoot;
                Vector3 corrected = left ? correctedPose.LeftFoot : correctedPose.RightFoot;
                AccumulateStepMaxima(original, corrected, tick > startTick, ref previousBaseline, ref previousCandidate, ref maxBaseline, ref maxCandidate);
                if (!integer) continue;
                AccumulateStepMaxima(original, corrected, tick > startTick, ref integerBaseline, ref integerCandidate, ref maxIntegerBaseline, ref maxIntegerCandidate);
            }
            return maxIntegerCandidate < maxIntegerBaseline - 0.001f && maxCandidate <= maxBaseline + 0.00001f;
        }

        // 프레임 재평가 후 보정 전후 포즈를 샘플링함 (기존 로컬 Sample 승격).
        private bool TrySampleReleasePose(
            float frame, AnimationClip clip, Action<float> evaluate,
            HumanoidGroundResponse ground, ReleaseSmoothingWindow candidate,
            out TouchdownPose baseline, out TouchdownPose corrected)
        {
            corrected = default;
            RestoreAppliedPose();
            float time = Mathf.Min(frame / _frameRate, clip.length);
            evaluate(time);
            if (!TryApplyCore(time, ground, null, false, out bool rejected, out baseline, candidate) || rejected)
                return false;
            corrected = ReadTouchdownPose();
            return true;
        }

        // 직전 샘플과의 발 이동량을 최댓값 누적기에 반영함.
        private static void AccumulateStepMaxima(
            Vector3 original, Vector3 corrected, bool hasPrevious,
            ref Vector3 previousBaseline, ref Vector3 previousCandidate,
            ref float maxBaseline, ref float maxCandidate)
        {
            if (hasPrevious)
            {
                maxBaseline = Mathf.Max(maxBaseline, Vector3.Distance(original, previousBaseline));
                maxCandidate = Mathf.Max(maxCandidate, Vector3.Distance(corrected, previousCandidate));
            }
            previousBaseline = original;
            previousCandidate = corrected;
        }

        private bool TryApplyReleaseSmoothing(ReleaseSmoothingWindow window, float frame, HumanoidGroundResponse ground,
            Leg.TargetState leftTarget, Leg.TargetState rightTarget, float offset, Vector2 footOffsets, Vector2 clearance, out bool rejected)
        {
            rejected = false;
            Vector3 shift = window.GetShift(frame);
            if (shift.sqrMagnitude < 0.000000000001f)
                return true;

            float contactLimit = _humanScale * SoleClearancePerHumanScale;
            Vector3 target = (window.Leg == _left ? leftTarget.Position : rightTarget.Position) + shift;
            if (!window.Leg.CanApplyReleaseSmoothing(window, ground, target, contactLimit) || !ReadRegions(_left, out var leftRegions) || !ReadRegions(_right, out var rightRegions))
            {
                rejected = true;
                return true;
            }

            TouchdownPose baseline = ReadTouchdownPose();
            bool usedPhysicalReach = UsedPhysicalReach;
            RestoreTargets(leftTarget, rightTarget);
            if (TrySolveSmoothedStep(window, target, clearance, contactLimit, baseline, leftRegions, rightRegions))
                return true;

            rejected = true;
            RestoreTargets(leftTarget, rightTarget);
            UsedPhysicalReach = usedPhysicalReach;
            return TrySolveGroundPose(offset, footOffsets, out _);
        }

        private void RestoreTargets(Leg.TargetState leftTarget, Leg.TargetState rightTarget)
        {
            RestoreAppliedPose();
            _left.RestoreTarget(leftTarget);
            _right.RestoreTarget(rightTarget);
        }

        // 스무딩 타깃 해결부터 접촉·골반·무릎 변화 한계 검증까지의 체인.
        private bool TrySolveSmoothedStep(
            ReleaseSmoothingWindow window, Vector3 target, Vector2 clearance,
            float contactLimit, TouchdownPose baseline, Vector2 leftRegions, Vector2 rightRegions)
        {
            return window.Leg.TrySetTouchdownTarget(target) && TryResolveReach(out float newOffset, out Vector2 newFootOffsets) &&
                TrySolveGroundPose(newOffset, newFootOffsets, out var newClearance) && RawGatePass && SameRegionContact(clearance, newClearance, contactLimit) &&
                ReadRegions(_left, out var newLeft) && ReadRegions(_right, out var newRight) && SameRegionContact(leftRegions, newLeft, contactLimit) &&
                SameRegionContact(rightRegions, newRight, contactLimit) && Mathf.Abs(_hips.position.y - baseline.HipsY) <= MaximumReleasePelvisChange &&
                Mathf.Abs(_left.KneeAngle - baseline.Knees.x) <= MaximumReleaseKneeChange && Mathf.Abs(_right.KneeAngle - baseline.Knees.y) <= MaximumReleaseKneeChange;
        }

        private static bool ReadRegions(Leg leg, out Vector2 heights)
        {
            heights = default;
            return !leg.HasGround || leg.TryGetRegionHeights(out heights);
        }

        // 원본 진행축에 한정해 최대 발 스텝을 줄일 수 있는 프레임별 오프셋을 찾음.
        private static bool TryBuildReleaseOffsets(Vector3[] targets, Vector3 direction, float bound, out float[] offsets)
        {
            offsets = null;
            if (targets.Length < 3 || bound <= 0f) return false;
            int last = targets.Length - 1;
            MeasureStepMetrics(targets, direction, last, out float maximum, out float[] parallel, out float[] normalSquared);
            var intervals = new Vector2[targets.Length];
            var stepIntervals = new Vector2[last];
            float low = 0f, high = maximum;
            for (int iteration = 0; iteration < 24; iteration++)
            {
                float middle = (low + high) * 0.5f;
                if (TryFitStepOffsets(middle, bound, parallel, normalSquared, stepIntervals, intervals)) high = middle;
                else low = middle; }
            if (maximum - high < 0.001f || !TryFitStepOffsets(high, bound, parallel, normalSquared, stepIntervals, intervals)) return false;
            offsets = new float[targets.Length];
            for (int i = last - 1; i > 0; i--)
            {
                float minimum = Mathf.Max(intervals[i].x, offsets[i + 1] - stepIntervals[i].y);
                float upper = Mathf.Min(intervals[i].y, offsets[i + 1] - stepIntervals[i].x);
                offsets[i] = Mathf.Clamp(0f, minimum, upper); }
            return true;
        }

        // 스텝 크기와 진행축 성분·수직 성분을 측정함.
        private static void MeasureStepMetrics(
            Vector3[] targets, Vector3 direction, int last,
            out float maximum, out float[] parallel, out float[] normalSquared)
        {
            maximum = 0f;
            parallel = new float[last];
            normalSquared = new float[last];
            for (int i = 0; i < last; i++)
            {
                Vector3 step = targets[i + 1] - targets[i];
                maximum = Mathf.Max(maximum, step.magnitude);
                parallel[i] = Vector3.Dot(step, direction);
                normalSquared[i] = Mathf.Max(0f, step.sqrMagnitude - parallel[i] * parallel[i]); }
        }

        // 스텝 상한 시 허용 구간을 전방 누적하며 실현 가능 여부를 판정함 (기존 로컬 TryFit 승격).
        private static bool TryFitStepOffsets(
            float stepLimit, float bound, float[] parallel, float[] normalSquared,
            Vector2[] stepIntervals, Vector2[] intervals)
        {
            int last = parallel.Length;
            for (int i = 0; i < last; i++)
            {
                float remaining = stepLimit * stepLimit - normalSquared[i];
                if (remaining < 0f) return false;
                float span = Mathf.Sqrt(remaining);
                var stepRange = new Vector2( Mathf.Max(-span, Mathf.Min(0f, parallel[i])) - parallel[i], span - parallel[i]);
                stepIntervals[i] = stepRange;
                float extent = i + 1 == last ? 0f : bound;
                intervals[i + 1] = new Vector2( Mathf.Max(-extent, intervals[i].x + stepRange.x), Mathf.Min(extent, intervals[i].y + stepRange.y));
                if (intervals[i + 1].x > intervals[i + 1].y) return false; }
            return true;
        }

        private sealed partial class Leg
        {
            internal bool TryGetReleaseRange(int frame, out int end, out int channel,
                out HumanoidFootAnchorPlan.Sample contact)
            {
                end = frame + 1;
                channel = GetSingleSupportChannel(_weights[frame]);
                contact = default;
                if (channel < 0 || _weights[frame][channel] < 0.999f ||
                    GetSingleSupportChannel(_weights[end]) != channel ||
                    _weights[end][channel] >= 0.999f)
                    return false;

                contact = _plan.GetSample(channel, frame);
                while (end < _weights.Length && GetSingleSupportChannel(_weights[end]) == channel &&
                    _weights[end][channel] < _weights[end - 1][channel] &&
                    _plan.GetSample(channel, end).FirstPoint == contact.FirstPoint)
                    end++;
                // 재획득과 접점 교체는 기존 지지 계획을 따름.
                return contact.HasValue && end > frame + 1 && end < _weights.Length &&
                    _weights[end].x <= 0f && _weights[end].y <= 0f;
            }


            internal bool HasReleaseReference(int frame) => _releaseReferences.ContainsKey(frame);

            internal bool HasAbruptRelease(int start, int end, float scale)
            {
                for (int frame = start; frame < end; frame++)
                {
                    Vector3 step = _sourcePoints[0][frame + 1] - _sourcePoints[0][frame];
                    if (Vector3.ProjectOnPlane(step, Vector3.up).sqrMagnitude * scale * scale >
                        16f * MaximumReleaseTargetShift * MaximumReleaseTargetShift)
                        return true;
                }
                return false;
            }

            internal bool TryReadReleaseSurface(out RaycastHit surface)
            {
                surface = _ground;
                return HasGround && !Diagnostic.mixed_ground_collider && _ground.collider != null;
            }

            internal bool CanApplyReleaseSmoothing(ReleaseSmoothingWindow window, HumanoidGroundResponse ground,
                Vector3 target, float contactLimit) =>
                HasGround && _support > 0f && _support < 0.999f && GetSingleSupportChannel(_activeWeights) == window.Channel &&
                _activeContacts[window.Channel].FirstPoint == window.Point && !Diagnostic.mixed_ground_collider &&
                MatchesReferenceGround(_ground, window.Ground, contactLimit) &&
                _ground.collider.transform.localToWorldMatrix == window.GroundMatrix &&
                ground.TryFindGround(target, out var hit) && MatchesReferenceGround(hit, window.Ground, contactLimit);
        }
    }
}
