using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // HumanPose 클램프·해부 가드·트위스트 레퍼런스를 담당하는 partial 분할.
    public partial class PoseSpaceRetargeter
    {
        private void ClampPoseMuscles(ref HumanPose pose)
        {
            if (!ShouldApplyRetargetSafetyGuards() ||
                !clampMusclesToHumanRange ||
                pose.muscles == null)
            {
                return;
            }

            int clampedCount =
                HumanoidMuscleRangeLimiter.ClampNonFingerMusclesInPlace(pose.muscles);

            if (clampedCount > 0 && !_muscleClampWarningLogged)
            {
                Debug.LogWarning($"[PoseSpaceRetargeter] Humanoid muscle 값 {clampedCount}개가 안전 범위를 벗어나 [-1, 1]로 제한되었습니다.");
                _muscleClampWarningLogged = true;
            }
        }

        private void ApplySetHumanPoseRightLegTwistOutputReference(HumanPose inputPose)
        {
            if (!TryReadRightLegTwistOutputReferenceRequest(inputPose, out float weight, out float maxDelta))
            {
                return;
            }

            bool changed = ManualPoseReferenceApplier.TryApplyBoundedMuscleOutputReference(
                ref _appliedTargetPose,
                inputPose,
                _muscleIndexMap.Get(HumanMuscleSlot.RightUpperLegTwistInOut),
                weight,
                maxDelta);
            changed |= ManualPoseReferenceApplier.TryApplyBoundedMuscleOutputReference(
                ref _appliedTargetPose,
                inputPose,
                _muscleIndexMap.Get(HumanMuscleSlot.RightLowerLegTwistInOut),
                weight,
                maxDelta);

            if (changed)
            {
                _targetHandler.SetHumanPose(ref _appliedTargetPose);
            }
        }

        private bool TryReadRightLegTwistOutputReferenceRequest(HumanPose inputPose, out float weight, out float maxDelta)
        {
            weight = 0f;
            maxDelta = 0f;
            if (!ShouldUseSetHumanPoseRightLegTwistOutputReference ||
                _targetHandler == null ||
                inputPose.muscles == null)
            {
                return false;
            }

            _targetHandler.GetHumanPose(ref _appliedTargetPose);
            if (_appliedTargetPose.muscles == null)
            {
                return false;
            }

            weight = Mathf.Clamp01(setHumanPoseRightLegTwistOutputReferenceWeight);
            maxDelta = Mathf.Max(0f, setHumanPoseRightLegTwistOutputReferenceMaxDelta);
            return weight > 0f && maxDelta > 0f;
        }

        private void ApplyEditorHumanoidBoneLocalRotationReference()
        {
#if UNITY_EDITOR
            if (!IsCompleteEditorHumanoidPoseReferenceActive)
            {
                return;
            }

            // 완전한 Native 기준 포즈는 HumanPose 재해석 과정에서 범위를 벗어난 muscle이 손실될 수 있음.
            // 동일 모델의 Humanoid 본 회전만 복구하고 root 이동, 본 위치와 scale은 기존 경로가 소유함.
            _editorHumanoidPoseReferencePlayer.TryApplyHumanoidBoneLocalRotationsTo(targetAnimator);
#endif
        }

        private void ApplyAnatomicalArmGuard(ref HumanPose pose)
        {
            if (!ShouldApplyRetargetSafetyGuards() ||
                !enableAnatomicalArmGuard ||
                pose.muscles == null)
            {
                return;
            }

            int changed = HumanoidArmDeformationGuard.ClampAnatomicalArmMuscles(
                ref pose,
                armStretchMuscleLimit,
                upperArmTwistMuscleLimit,
                lowerArmTwistMuscleLimit,
                clampArmStretchMuscles);

            if (changed > 0 && !_anatomyGuardWarningLogged)
            {
                Debug.LogWarning($"[PoseSpaceRetargeter] 팔 변형 방지를 위해 Humanoid arm muscle {changed}개를 제한했습니다.");
                _anatomyGuardWarningLogged = true;
            }
        }

        private void ClampAppliedTargetPose()
        {
            if (!TryReadClampableAppliedPose())
            {
                return;
            }

            int changed = CountAppliedPoseClamps();
            if (changed <= 0)
            {
                return;
            }

            _targetHandler.SetHumanPose(ref _appliedTargetPose);
            if (!_appliedPoseClampWarningLogged)
            {
                Debug.LogWarning($"[PoseSpaceRetargeter] Target 적용 후 범위를 벗어난 Humanoid muscle {changed}개를 추가 보정했습니다.");
                _appliedPoseClampWarningLogged = true;
            }
        }

        private bool TryReadClampableAppliedPose()
        {
            if (!ShouldApplyRetargetSafetyGuards() ||
                (!clampMusclesToHumanRange && !enableAnatomicalArmGuard && !enableThumbAnatomicalGuard) ||
                _targetHandler == null)
            {
                return false;
            }

            _targetHandler.GetHumanPose(ref _appliedTargetPose);
            if (!IsFinite(_appliedTargetPose))
            {
                LogPoseWarning("Applied target HumanPose contains non-finite values. Skipping applied pose clamp.");
                return false;
            }

            return true;
        }

        private int CountAppliedPoseClamps()
        {
            int changed = 0;
            if (clampMusclesToHumanRange)
            {
                changed += HumanoidMuscleRangeLimiter.ClampNonFingerMusclesInPlace(
                    _appliedTargetPose.muscles);
            }

            if (enableAnatomicalArmGuard)
            {
                changed += HumanoidArmDeformationGuard.ClampAnatomicalArmMuscles(
                    ref _appliedTargetPose,
                    armStretchMuscleLimit,
                    upperArmTwistMuscleLimit,
                    lowerArmTwistMuscleLimit,
                    clampArmStretchMuscles);
            }

            changed += ThumbMuscleGuardPolicy.ApplyAnatomicalGuard(this, ref _appliedTargetPose, false);
            return changed;
        }

        private bool ShouldApplyRetargetSafetyGuards()
        {
#if UNITY_EDITOR
            return RetargetingMuscleReferencePolicy.ShouldApplyRetargetSafetyGuards(
                _useCompleteEditorHumanoidMuscleReference);
#else
            return true;
#endif
        }
    }
}
