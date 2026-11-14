using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class HumanoidFootGoalDriver
    {
        private sealed partial class LegDrive
        {
            internal void PrepareGoal(int frame,
                IReadOnlyList<HumanoidFootGoalStream.GoalSample> samples,
                Vector3 hipsPosition, Quaternion hipsRotation,
                float floorRootSpaceY, float floorWorldY,
                float dt, float separationWeight, HumanoidGroundResponse ground)
            {
                if (SolveWeight <= 0f ||
                    !TryResolveGoalSample(frame, samples, floorRootSpaceY,
                        out HumanoidFootGoalStream.GoalSample goal))
                {
                    return;
                }

                Quaternion goalFrame = ResolveGoalFrame(frame, goal, hipsRotation);
                // rootSpace는 소스 골반 기준으로 구워짐 — 소스 골반으로 복원하면
                // 원본 월드 발 위치가 정확히 나오고 별도의 신장 스케일이 불필요함.
                Vector3 goalWorld = ApplySoleProbeLift(
                    hipsPosition + hipsRotation * goal.RootSpaceFootPosition,
                    goalFrame, floorWorldY, ground);
                goalWorld = ApplyPinAnchor(frame, goalWorld);
                UpdateSmoothedGoal(goalWorld, dt);
                ResolveEffectiveGoal(goalWorld, goalFrame, separationWeight, ground);
            }

            private void ResolveEffectiveGoal(Vector3 goalWorld, Quaternion goalFrame,
                float separationWeight, HumanoidGroundResponse ground)
            {
                // 원본 핀 상태와 지지 상태를 분리하고 도달 계산과 IK가 같은 목표를 사용함.
                float sourceFraction = SourceSolveWeight / SolveWeight;
                Vector3 sourceGoal = Vector3.Lerp(Foot.position, _smoothedGoal, sourceFraction);
                Vector3 rawSourceGoal = Vector3.Lerp(Foot.position, goalWorld, sourceFraction);
                Quaternion currentFrame = goalFrame;
                if (_binding != null && _binding.TryCaptureWorldFrame(out Quaternion capturedFrame))
                    currentFrame = capturedFrame;
                if (!HasValidatedSupport)
                {
                    // 지지 해제는 이번 프레임의 접지 결과로 넘기며 과거 위치에 발을 붙잡지 않음.
                    SupportedPosition = Foot.position;
                    _supportedFrame = currentFrame;
                }
                if (HasValidatedSupport && separationWeight > 0f)
                {
                    float separationFraction = 1f - Weight / SourceSolveWeight;
                    Vector3 supportNormal = Vector3.up;
                    if (ground != null && ground.TryFindGround(SupportedPosition, out RaycastHit supportGround))
                    {
                        supportNormal = supportGround.normal;
                        Vector3 projectedGoal = SupportedPosition + Vector3.ProjectOnPlane(
                            sourceGoal - SupportedPosition, supportNormal);
                        // 같은 지지면에서는 핀이 접지 자세를 덮지 않도록 발 모임 가중치만큼 보호함.
                        if (ground.TryFindGround(projectedGoal, out RaycastHit goalGround) &&
                            (goalGround.normal - supportNormal).sqrMagnitude <= 0.00000001f &&
                            Mathf.Abs(Vector3.Dot(goalGround.point - supportGround.point, supportNormal)) <=
                            Reach * HumanoidFootGrounding.SoleClearancePerHumanScale)
                            separationFraction = Mathf.Max(separationFraction, separationWeight);
                    }
                    if (separationFraction > 0f)
                    {
                        sourceGoal = Vector3.Lerp(sourceGoal, SupportedPosition + Vector3.ProjectOnPlane(
                            sourceGoal - SupportedPosition, supportNormal), separationFraction);
                        rawSourceGoal = Vector3.Lerp(rawSourceGoal, SupportedPosition + Vector3.ProjectOnPlane(
                            rawSourceGoal - SupportedPosition, supportNormal), separationFraction);
                        goalFrame = Quaternion.Slerp(goalFrame, _supportedFrame, separationFraction);
                    }
                }
                Quaternion sourceFrame = Quaternion.Slerp(currentFrame, goalFrame, sourceFraction);
                LastSolveGoal = Vector3.Lerp(rawSourceGoal, SupportedPosition, SupportWeight);
                EffectiveGoal = Vector3.Lerp(sourceGoal, SupportedPosition, SupportWeight);
                _effectiveFrame = Quaternion.Slerp(sourceFrame, _supportedFrame, SupportWeight);
            }

            private bool TryResolveGoalSample(int frame,
                IReadOnlyList<HumanoidFootGoalStream.GoalSample> samples,
                float floorRootSpaceY,
                out HumanoidFootGoalStream.GoalSample goal)
            {
                int index = frame * 2 + (IsLeft ? 0 : 1);
                goal = default;
                if (index < 0 || index >= samples.Count)
                {
                    return false;
                }
                goal = samples[index];
                if (Grounded[frame])
                {
                    // pin 구간의 지면 스냅 — 목표 발끝이 추정 지면 아래로 가지 않음.
                    goal = HumanoidFootGoalStream.SnapAboveGround(goal, floorRootSpaceY);
                }
                return true;
            }

            private Quaternion ResolveGoalFrame(int frame,
                HumanoidFootGoalStream.GoalSample goal, Quaternion hipsRotation)
            {
                // 밑창 인식 지면 스냅 — 발끝 본 스냅만으로는 회전된 발의 밑창 정점이
                // 지면 아래로 파고듦. 목표 회전 기준으로 가장 낮은 밑창 정점이
                // 지면에 닿도록 목표 발목을 들어올림. pin 여부와 무관하게 적용함.
                Quaternion goalFrame = GoalFrameToWorld(goal, hipsRotation);
                // 지면 정렬·soft-cap을 먼저 반영 — 뒤의 밑창 프로브 평가와 최종
                // 적용 회전이 같은 목표 프레임을 기준으로 해야 lift가 정확해짐.
                if (Grounded[frame])
                {
                    goalFrame = AlignSoleToGround(goalFrame, Vector3.up);
                    goalFrame = ApplyToePitchSoftCap(goalFrame, Vector3.up, 1f);
                }
                // 핀 해제 감쇠 중 회전 불연속 방지 — 목표 프레임도 위치와 같은
                // weight 비례로 마지막 핀 프레임에서 이행해 발 회전이 단발로
                // 점프하지 않게 함. 감쇠 중 재핀이면 현재 유효 프레임으로 이어 잡음.
                if (Pin[frame])
                {
                    _pinAnchorFrame = _pinReleased && _pinAnchorValid && Weight > 0f
                        ? Quaternion.Slerp(goalFrame, _pinAnchorFrame, Weight)
                        : goalFrame;
                }
                else if (_pinAnchorValid && Weight > 0f)
                {
                    goalFrame = Quaternion.Slerp(goalFrame, _pinAnchorFrame, Weight);
                }
                return goalFrame;
            }

            private Vector3 ApplySoleProbeLift(Vector3 goalWorld,
                Quaternion goalFrame, float floorWorldY,
                HumanoidGroundResponse ground)
            {
                if (_soleProbesLocal.Length == 0 ||
                    _binding == null ||
                    !_binding.TryResolveAppliedRotation(goalFrame, out Quaternion appliedFootRotation))
                {
                    return goalWorld;
                }
                // 바인딩이 뼈에 실제로 쓸 회전으로 밑창 정점을 평가함 —
                // goalFrame은 canonical frame 규약이라 발목 뼈 로컬 좌표의
                // 프로브에 그대로 곱하면 축이 어긋나 높이가 틀어짐.
                // 원본 발끝 기준은 의도 판정에 유지하고 실제 밑창 상승은 대상 지면을 기준으로 함.
                float soleFloorY = ground != null && ground.TryFindGround(goalWorld, out RaycastHit hit)
                    ? hit.point.y : floorWorldY;
                float soleLift = soleFloorY - LowestSoleProbeHeight(
                    goalWorld, appliedFootRotation);
                if (soleLift > 0f)
                {
                    goalWorld.y += soleLift;
                }
                return goalWorld;
            }

            private Vector3 ApplyPinAnchor(int frame, Vector3 goalWorld)
            {
                // pin 구간의 수평 앵커 고정 — 핀은 발이 심어진 상태인데 VMD 목표를
                // 계속 추종하면 접지 발이 프레임마다 미끄러짐(잔차 슬라이드). 재진입
                // 프레임의 목표를 수평 앵커로 삼아 정박하고, 수직은 지면 스냅·
                // 리프트를 계속 반영함.
                if (Pin[frame])
                {
                    if (!_pinAnchorValid || _pinReleased)
                    {
                        // 감쇠 중 재핀이면 현재 유효 목표(블렌드 위치)로 앵커를
                        // 이어 잡아 정박 지점이 생목표로 단발 점프하지 않게 함.
                        _pinAnchor = _pinReleased && _pinAnchorValid
                            ? new Vector3(
                                Mathf.Lerp(goalWorld.x, _pinAnchor.x, Weight),
                                goalWorld.y,
                                Mathf.Lerp(goalWorld.z, _pinAnchor.z, Weight))
                            : goalWorld;
                        _pinAnchorValid = true;
                        _pinReleased = false;
                    }
                    goalWorld.x = _pinAnchor.x;
                    goalWorld.z = _pinAnchor.z;
                }
                else if (_pinAnchorValid && Weight > 0f)
                {
                    // 핀 해제 감쇠 중 목표 불연속 방지 — 앵커와 원본 목표 사이를
                    // weight 비례로 이행해 solve 목표가 프레임 단위로 점프하지 않게 함.
                    // ApplyPinBlend와 동일 수식 — 골반 reach 계산도 같은 목표를 봄.
                    _pinReleased = true;
                    goalWorld = ApplyPinBlend(frame, goalWorld);
                }
                else
                {
                    _pinAnchorValid = false;
                    _pinReleased = false;
                }
                return goalWorld;
            }

            private void UpdateSmoothedGoal(Vector3 goalWorld, float dt)
            {
                LastSolveGoal = goalWorld;
                if (!_hasSmoothedGoal)
                {
                    _smoothedGoal = goalWorld;
                    _hasSmoothedGoal = true;
                }
                else
                {
                    _smoothedGoal = Smooth(_smoothedGoal, goalWorld,
                        GoalPositionSmoothingSpeed, dt);
                }
            }

            internal void SolvePreparedGoal(Vector3 bendNormal)
            {
                if (SolveWeight <= 0f) return;
                // 지지 해제 중 목표 변화가 없으면 앞 단계의 무릎 자세까지 보존함.
                // 골반이 이동하면 현재 발과 목표가 달라지므로 지지 보존 IK를 계속 적용함.
                if (!HasValidatedSupport && SourceSolveWeight <= 0f &&
                    EffectiveGoal.Equals(Foot.position))
                {
                    LastUsedBendNormal = bendNormal;
                    return;
                }
                Quaternion goalFrame = _effectiveFrame;
                Quaternion upperRotation = Upper.rotation;
                Quaternion lowerRotation = Lower.rotation;
                Quaternion footRotation = Foot.rotation;
                Quaternion inputFrame = Quaternion.identity;
                bool hasInputFrame = _binding != null && _binding.TryCaptureWorldFrame(out inputFrame);
                bendNormal = ResolveBendNormal(upperRotation, bendNormal);
                LastUsedBendNormal = bendNormal;
                bool solved = HumanoidLegPoseSolver.TrySolve(Upper, Lower, Foot,
                    EffectiveGoal, bendNormal, out float targetError);
                CaptureDebugSolveInputs(bendNormal);
                DebugSolveSucceeded = solved;
                DebugTargetError = targetError;
                if (solved && SolveWeight < 1f)
                {
                    // 부모 회전 적용이 자식의 보간 목표에 다시 섞이지 않도록 IK 결과를 고정함.
                    Quaternion solvedUpperRotation = Upper.rotation;
                    Quaternion solvedLowerRotation = Lower.rotation;
                    Quaternion solvedFootRotation = Foot.rotation;
                    Upper.rotation = Quaternion.Slerp(upperRotation, solvedUpperRotation, SolveWeight);
                    Lower.rotation = Quaternion.Slerp(lowerRotation, solvedLowerRotation, SolveWeight);
                    Foot.rotation = Quaternion.Slerp(footRotation, solvedFootRotation, SolveWeight);
                }
                // 발 회전 — 목표 프레임(grounded 시 정렬+soft-cap 반영됨)을 가중 적용.
                if (!hasInputFrame)
                {
                    return;
                }
                // 부모 IK가 만든 회전을 보간 시작값에 섞지 않도록 입력 발 회전을 유지함.
                Quaternion blended = Quaternion.Slerp(inputFrame, goalFrame, SolveWeight);
                _binding.TryApplyWorldFrame(blended);
            }

            private Vector3 ResolveBendNormal(Quaternion upperRotation, Vector3 bendNormal)
            {
                // gate와 같은 해부학 기준 정합 — 무릎이 거의 펴진 구간의 자세 cross는
                // 잡음성이고 부호가 뒤집혀 무릎을 반대쪽으로 붕괴시키므로 기준 법선과
                // 가중 정합해 굽힘면을 고정함. 기준이 없거나 정합이 퇴화하면
                // 기존 입력·캐시 경로로 폴백함.
                Vector3 referenceNormal = upperRotation * _referenceNormal;
                bool bendAligned = HumanoidLegPoseSolver.TryCalculateBendNormal(
                    Lower.position - Upper.position,
                    Foot.position - Lower.position,
                    EffectiveGoal - Upper.position,
                    referenceNormal, out bendNormal, out float referenceWeight);
                if (bendAligned)
                {
                    _lastBendNormal = bendNormal;
                }
                else if (IsStableBendNormal(referenceNormal))
                {
                    bendNormal = referenceNormal;
                    _lastBendNormal = bendNormal;
                }
                else
                {
                    // 정합 실패 시 out은 zero — 캐시 유지가 우선. 경계값(1~3°)의
                    // 현재 자세 cross는 잡음성이라 캐시를 덮어쓰면 굽힘면이
                    // 프레임마다 흔들리므로 자세 cross는 캐시가 비어 있을 때만 시드로 씀.
                    if (!IsStableBendNormal(_lastBendNormal))
                    {
                        Vector3 currentBendNormal = Vector3.Cross(
                            Lower.position - Upper.position,
                            Foot.position - Lower.position);
                        if (IsStableBendNormal(currentBendNormal))
                        {
                            _lastBendNormal = currentBendNormal;
                        }
                    }
                    bendNormal = _lastBendNormal;
                }
                return bendNormal;
            }

            private void CaptureDebugSolveInputs(Vector3 bendNormal)
            {
                Vector3 direction = EffectiveGoal - Upper.position;
                float distance = direction.magnitude;
                float along = (distance * distance + UpperLength * UpperLength -
                    LowerLength * LowerLength) / (2f * distance);
                float radius = Mathf.Sqrt(Mathf.Max(0f,
                    UpperLength * UpperLength - along * along));
                Vector3 bendDir = Vector3.Cross(direction,
                    bendNormal.normalized).normalized;
                DebugSolveInputs = string.Format(
                    "dir={0:F4} d={1:F4} along={2:F4} r={3:F4} bendDir={4:F4} UL={5:F4} LL={6:F4}",
                    direction, distance, along, radius, bendDir, UpperLength, LowerLength);
            }

            // 목표 발 피치로 평지 정렬을 가중 — 급하게 기운 발끝 지지(롤 스탠스)는
            // 발끝 방향을 유지하고, 평평한 목표일수록 밑창을 지면에 맞춤.
            // (이전 발목-발끝 높이차 기준은 모델의 고유 밑창 오프셋에 스케일 종속이라
            //  이 모델에서는 가중치가 항상 0이 되어 정렬이 사실상 동작하지 않았음.)
            private static Quaternion AlignSoleToGround(Quaternion frame, Vector3 groundNormal)
            {
                Vector3 forward = frame * Vector3.forward;
                float vertical = Vector3.Dot(forward, groundNormal);
                float horizontal = (forward - groundNormal * vertical).magnitude;
                float pitchDeg = Mathf.Atan2(vertical, horizontal) * Mathf.Rad2Deg;
                float flatWeight = Mathf.Clamp01(1f - Mathf.Abs(pitchDeg) / SoleAlignPitchSpanDeg);
                if (flatWeight <= 0f)
                {
                    return frame;
                }
                Vector3 up = frame * Vector3.up;
                Quaternion aligned = Quaternion.FromToRotation(up, groundNormal) * frame;
                return Quaternion.Slerp(frame, aligned, flatWeight);
            }

            // 목표 발목·회전에서 가장 낮은 밑창 정점의 월드 높이를 구함.
            private float LowestSoleProbeHeight(Vector3 ankleWorld, Quaternion footFrame)
            {
                float lowest = float.PositiveInfinity;
                float scale = Foot.lossyScale.x;
                foreach (Vector3 probe in _soleProbesLocal)
                {
                    Vector3 world = ankleWorld + footFrame * (probe * scale);
                    lowest = Mathf.Min(lowest, world.y);
                }
                return lowest;
            }

            // 무릎 굽힘 각이 MinimumStableBendSine 이하면 cross 정규화 방향이
            // 노이즈에 지배되므로 불안정으로 판정함 — 임계는 다리 길이로 정규화됨.
            private bool IsStableBendNormal(Vector3 bendNormal)
            {
                return bendNormal.sqrMagnitude > _stableNormalSquaredThreshold;
            }
        }
    }
}
