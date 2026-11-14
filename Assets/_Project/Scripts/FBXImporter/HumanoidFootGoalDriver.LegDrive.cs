using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class HumanoidFootGoalDriver
    {
        private sealed partial class LegDrive
        {
            internal bool IsLeft { get; private set; }
            internal Transform Upper { get; private set; }
            internal Transform Lower { get; private set; }
            internal Transform Foot { get; private set; }
            internal float UpperLength { get; private set; }
            internal float LowerLength { get; private set; }
            internal float Reach => UpperLength + LowerLength;
            internal float Weight { get; private set; }
            // 이번 프레임 solve의 실제 적용 weight — 핀 weight와 발 모임 복구
            // weight의 큰 쪽. 핀 앵커·감쇠 블렌드는 Weight 기준을 유지하고,
            // solve 발화·결과 블렌드만 이 값을 씀. TryApply가 매 프레임 계산함.
            internal float SolveWeight { get; set; }
            internal float SourceSolveWeight { get; set; }
            internal bool HasValidatedSupport { get; private set; }
            internal Vector3 SupportedPosition { get; private set; }
            private Quaternion _supportedFrame;
            internal float SupportWeight { get; private set; }
            internal Vector3 EffectiveGoal { get; private set; }
            private Quaternion _effectiveFrame;
            internal bool[] Pin = Array.Empty<bool>();
            internal bool[] Grounded = Array.Empty<bool>();

            private HumanoidFootRotationBinding _binding;
            internal void CaptureValidatedSupport(bool supported)
            {
                HasValidatedSupport = false;
                if (supported && _binding != null &&
                    _binding.TryCaptureWorldFrame(out Quaternion frame))
                {
                    HasValidatedSupport = true;
                    SupportedPosition = Foot.position;
                    _supportedFrame = frame;
                }
            }

            internal void UpdateSupportWeight(float separationWeight, bool reset, float dt)
            {
                float target = HasValidatedSupport ? 1f - separationWeight : 0f;
                SupportWeight = reset ? target : Mathf.MoveTowards(
                    SupportWeight, target, PinWeightChangePerSecond * dt);
            }
            // 이번 solve가 실제로 쫓은 목표(핀 앵커·해제 블렌드 반영 후, 평활 전) —
            // 해제 전이의 목표 불연속 검증용.
            internal Vector3 LastSolveGoal { get; private set; }
            // 핀 구간의 수평 앵커 — reach 계산이 solve와 동일 목표를 보게 공개함.
            // 해제 감쇠부터는 solve 목표가 앵커에서 원본으로 이행 중이므로 원본
            // 목표를 보게 함 — 오래된 앵커가 거리를 부풀려 단발 골반 드롭을 만들지 않게 함.
            internal bool TryGetPinAnchor(out Vector3 anchor)
            {
                anchor = _pinAnchor;
                return _pinAnchorValid && !_pinReleased;
            }
            // solve와 reach가 공유하는 유효 목표 — 핀 중은 앵커, 해제 감쇠·감쇠 중
            // 재핀은 앵커↔원본 블렌드, 그 외는 원본 목표를 반환함. 상태를 바꾸지
            // 않으므로 solve보다 먼저 실행되는 골반 계산도 같은 목표를 볼 수 있음.
            internal Vector3 ApplyPinBlend(int frame, Vector3 rawGoal)
            {
                if (!_pinAnchorValid || Weight <= 0f)
                {
                    return rawGoal;
                }
                bool pinned = frame >= 0 && frame < Pin.Length && Pin[frame];
                if (pinned && !_pinReleased)
                {
                    rawGoal.x = _pinAnchor.x;
                    rawGoal.z = _pinAnchor.z;
                    return rawGoal;
                }
                // 해제 감쇠 또는 감쇠 중 재핀 — solve가 블렌드 위치를 목표·재앵커로 씀.
                rawGoal.x = Mathf.Lerp(rawGoal.x, _pinAnchor.x, Weight);
                rawGoal.z = Mathf.Lerp(rawGoal.z, _pinAnchor.z, Weight);
                return rawGoal;
            }

            internal void SetSoleProbes(Vector3[] localPoints)
            {
                _soleProbesLocal = localPoints ?? Array.Empty<Vector3>();
            }

            // 허벅지 로컬 공간의 해부학 기준 법선 — HumanoidLegBendCalibration 결과를
            // 그대로 받음(게이트의 _referenceNormal과 같은 규약).
            internal void SetBendReference(Vector3 referenceNormal)
            {
                _referenceNormal = referenceNormal;
            }

            internal bool Initialize(Animator animator, bool isLeft)
            {
                Clear();
                IsLeft = isLeft;
                Upper = animator.GetBoneTransform(
                    isLeft ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
                Lower = animator.GetBoneTransform(
                    isLeft ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                Foot = animator.GetBoneTransform(
                    isLeft ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                Transform toes = animator.GetBoneTransform(
                    isLeft ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                if (Upper == null || Lower == null || Foot == null ||
                    !Lower.IsChildOf(Upper) || !Foot.IsChildOf(Lower))
                {
                    return false;
                }
                UpperLength = Vector3.Distance(Upper.position, Lower.position);
                LowerLength = Vector3.Distance(Lower.position, Foot.position);
                _stableNormalSquaredThreshold =
                    UpperLength * LowerLength * MinimumStableBendSine;
                _stableNormalSquaredThreshold *= _stableNormalSquaredThreshold;
                HumanoidFootRotationBinding.TryCreate(Foot, toes, animator.transform.up,
                    out _binding);
                _lastBendNormal = Vector3.Cross(
                    Lower.position - Upper.position, Foot.position - Lower.position);
                if (!IsStableBendNormal(_lastBendNormal))
                {
                    _lastBendNormal = animator.transform.right;
                }
                _referenceNormal = Vector3.zero;
                return UpperLength > 0f && LowerLength > 0f;
            }

            internal void UpdateWeights(float alpha, bool pinned,
                bool reset, float dt)
            {
                float target = alpha * (pinned ? 1f : 0f);
                if (reset)
                {
                    Weight = target;
                    // 시드 자체는 TrySolve가 담당 — 여기서 true로 두면 평활 목표가
                    // Vector3.zero로 남아 IK가 월드 원점으로 접힘.
                    _hasSmoothedGoal = false;
                    // 시간 점프는 핀 에피소드도 끊음 — 이전 위치의 앵커가
                    // 새 위치의 골반 계산·해제 블렌드로 번지지 않게 함.
                    _pinAnchorValid = false;
                    _pinReleased = false;
                    return;
                }
                if (dt <= 0f)
                {
                    // 같은 시각 재평가(Seek 동일 시각·자세 복원 재적용) — 경과 시간이
                    // 없으므로 상태를 유지해 멱등 재적용이 되게 함. 여기서 스냅하면
                    // 핀 앵커 파괴·weight 급변으로 재평가마다 발이 점프함
                    // (gate의 ApplyGateHold도 같은 규약으로 멱등 처리함).
                    return;
                }
                // 선형 전이 — 프레임당 변화량이 일정해 해제·진입 시의 자세 갭
                // 노출 스텝이 갭/전이 프레임 수로 상한 아래에 머묾.
                Weight = Mathf.MoveTowards(Weight, target, PinWeightChangePerSecond * dt);
                // weight가 소실되면 다음 pin 재진입 때 새 목표에서 다시 정박함 —
                // 이전 구간의 평활 잔여가 슬라이드로 보이지 않게 함.
                if (Weight <= 0f && target <= 0f)
                {
                    Weight = 0f;
                    _hasSmoothedGoal = false;
                    // 핀 에피소드 종료 — weight 소실 후 TrySolve가 다시 안 불려도
                    // 앵커가 남아 다음 재진입의 골반 계산을 오염하지 않게 함.
                    _pinAnchorValid = false;
                    _pinReleased = false;
                }
            }


            internal void Clear()
            {
                Upper = Lower = Foot = null;
                _binding = null;
                Weight = 0f;
                SolveWeight = 0f;
                SourceSolveWeight = 0f;
                HasValidatedSupport = false;
                SupportedPosition = Vector3.zero;
                _supportedFrame = Quaternion.identity;
                SupportWeight = 0f;
                EffectiveGoal = Vector3.zero;
                _effectiveFrame = Quaternion.identity;
                Pin = Grounded = Array.Empty<bool>();
                _hasSmoothedGoal = false;
                _smoothedGoal = Vector3.zero;
                _pinAnchorValid = false;
                _pinAnchor = Vector3.zero;
                _pinAnchorFrame = Quaternion.identity;
                _pinReleased = false;
                _soleProbesLocal = Array.Empty<Vector3>();
                _lastBendNormal = Vector3.zero;
                _referenceNormal = Vector3.zero;
                _stableNormalSquaredThreshold = 0f;
            }
        }
    }
}
