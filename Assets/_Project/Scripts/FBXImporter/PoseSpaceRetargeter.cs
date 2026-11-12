using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using System;
using System.Collections.Generic;
using Fbx2Vmd.Retargeting;
using RootMotion;
using RootMotion.FinalIK;

namespace Fbx2Vmd.FBXImporter
{
    [DefaultExecutionOrder(CorrectionPassOrder.PoseSpaceRetarget)]
    public partial class PoseSpaceRetargeter : MonoBehaviour
    {
        private const float RecordingStartHipsBaselineFlipWarningThreshold = 0.02f;
        internal const float HipsLocalPositionTargetGapGuardMaxIncreaseMeters = 0.0005f;
        private const string RecordingStartHipsReferenceStagePrewarmComplete = "prewarm-complete";

        internal static Vector3 BuildNaNVector3()
        {
            return new Vector3(float.NaN, float.NaN, float.NaN);
        }

        private static Quaternion BuildNaNQuaternion()
        {
            return new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN);
        }

        public void Initialize(RetargetingContext context, RetargetingSettings settings)
        {
            GameObject ghostRoot = context.GhostRoot;
            GameObject targetRoot = context.TargetRoot;
            AnimationClip clip = context.Clip;

            _legacyAnimationDriver.Dispose();

            ghostAnimator = ghostRoot.GetComponent<Animator>();
            targetAnimator = targetRoot.GetComponent<Animator>();
            CaptureTargetInitialTransforms(targetRoot);
            _targetRootPoseGuardAnchorPosition = targetAnimator != null ? targetAnimator.transform.position : Vector3.zero;
            _hasTargetRootPoseGuardAnchorPosition = targetAnimator != null && IsFinite(_targetRootPoseGuardAnchorPosition);

            if (clip == null) return;

            _legacyAnimationDriver.Initialize(ghostRoot, ghostAnimator, clip);

            // 포즈 핸들러 초기화
            if (!ghostAnimator.avatar || !targetAnimator.avatar) return;
            _ghostHandler = new HumanPoseHandler(ghostAnimator.avatar, ghostAnimator.transform);
            _targetHandler = new HumanPoseHandler(targetAnimator.avatar, targetAnimator.transform);
            _humanPose = new HumanPose();
            _appliedTargetPose = new HumanPose();
            CacheTargetReferenceBodyPosition();
            _grounding.CalibrateTargetFootRadius(this);

            // 초기 위치 저장
            _prevGhostPos = ghostAnimator.transform.position;
            ResetEditorHumanoidRootTranslationReferenceState();
            ResetRecordingStartHipsBaselineDiagnostics();
            ResetLastEditorHipsLocalReferenceDiagnostics();
            CacheInitialHipHeights();
            _hasEditorReferenceLowestFootRestY = false;
            _allowEditorFootHeightGroundingReference = false;
            _facingCorrection = settings != null && settings.ShouldUseLegacyPoseSpaceFacingCorrection
                ? LegacyFacingCorrection
                : Quaternion.Inverse(ghostAnimator.transform.rotation) * targetAnimator.transform.rotation;
            _poseRootRotationCorrection = Quaternion.identity;
            _hasPoseRootRotationCorrection = false;
            ApplyRetargetingSettings(settings);

            _isInitialized = true;
            EnsureLateVisualGroundingCorrection();
            Debug.Log("[Master Stage] 시스템 초기화됨. 첫 Update 대기 중...");
        }

        private void OnDestroy()
        {
            _editorHumanoidPoseReferencePlayer?.Dispose();
#if UNITY_EDITOR
            DisposeEditorHumanoidFingerPoseReference();
#endif
            _legacyAnimationDriver.Dispose();
        }





        private static Vector3 FlattenXZ(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        internal void LogPoseWarning(string message)
        {
            if (_poseWarningLogged)
            {
                return;
            }

            Debug.LogWarning($"[PoseSpaceRetargeter] {message}");
            _poseWarningLogged = true;
        }

        internal static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        internal static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        internal static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        internal static bool IsFinite(HumanPose pose)
        {
            if (!IsFinite(pose.bodyPosition) || !IsFinite(pose.bodyRotation))
            {
                return false;
            }

            if (pose.muscles == null)
            {
                return true;
            }

            foreach (float muscle in pose.muscles)
            {
                if (!IsFinite(muscle))
                {
                    return false;
                }
            }

            return true;
        }

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
            if (!ShouldUseSetHumanPoseRightLegTwistOutputReference ||
                _targetHandler == null ||
                inputPose.muscles == null)
            {
                return;
            }

            _targetHandler.GetHumanPose(ref _appliedTargetPose);
            if (_appliedTargetPose.muscles == null)
            {
                return;
            }

            float weight = Mathf.Clamp01(setHumanPoseRightLegTwistOutputReferenceWeight);
            float maxDelta = Mathf.Max(0f, setHumanPoseRightLegTwistOutputReferenceMaxDelta);
            if (weight <= 0f || maxDelta <= 0f)
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
            if (!ShouldApplyRetargetSafetyGuards() ||
                (!clampMusclesToHumanRange && !enableAnatomicalArmGuard && !enableThumbAnatomicalGuard) ||
                _targetHandler == null)
            {
                return;
            }

            _targetHandler.GetHumanPose(ref _appliedTargetPose);
            if (!IsFinite(_appliedTargetPose))
            {
                LogPoseWarning("Applied target HumanPose contains non-finite values. Skipping applied pose clamp.");
                return;
            }

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

        private bool ShouldApplyRetargetSafetyGuards()
        {
#if UNITY_EDITOR
            return RetargetingMuscleReferencePolicy.ShouldApplyRetargetSafetyGuards(
                _useCompleteEditorHumanoidMuscleReference);
#else
            return true;
#endif
        }

        public bool IsManualThumbLocalRotationReferenceActive
        {
            get
            {
#if UNITY_EDITOR
                return useManualAnimatorThumbLocalRotationReference &&
                    _useEditorFingerPoseReference &&
                    _editorFingerReferenceAnimator != null;
#else
                return false;
#endif
            }
        }

        public bool ShouldSuppressThumbPoseShapingGuard
        {
            get
            {
#if UNITY_EDITOR
                return ShouldSuppressThumbPoseShapingGuardForHand(true) ||
                    ShouldSuppressThumbPoseShapingGuardForHand(false);
#else
                return false;
#endif
            }
        }

        public bool ShouldSuppressLeftThumbPoseShapingGuard
        {
            get
            {
#if UNITY_EDITOR
                return ShouldSuppressThumbPoseShapingGuardForHand(true);
#else
                return false;
#endif
            }
        }

        public bool ShouldSuppressRightThumbPoseShapingGuard
        {
            get
            {
#if UNITY_EDITOR
                return ShouldSuppressThumbPoseShapingGuardForHand(false);
#else
                return false;
#endif
            }
        }

        public bool ShouldSuppressThumbPoseShapingGuardForHand(bool leftHand)
        {
            return ThumbMuscleGuardPolicy.ShouldSuppressPoseShapingGuardForHand(this, leftHand);
        }


        public void ResetThumbWorldRotationPreserveDiagnostics()
        {
            _thumbGuardDiagnostics.ResetWorldRotationPreserve();
        }



        public bool ShouldPreserveManualThumbWorldRotationCorrection(
            HumanBodyBones thumbBone,
            Transform targetThumb,
            Quaternion candidateWorldRotation)
        {
            return ThumbWorldRotationPreservePolicy.ShouldPreserveManualWorldRotationCorrection(this, thumbBone, targetThumb, candidateWorldRotation);
        }


        internal bool HasDetachedThumbBaseHelperRelationship(HumanBodyBones thumbBone, bool leftHand)
        {
            return ThumbBaseHelperResolver.HasDetachedRelationship(this, thumbBone, leftHand);
        }

        internal static bool TryGetThumbBoneSide(HumanBodyBones thumbBone, out bool leftHand)
        {
            return ThumbGuardPolicy.TryGetBoneSide(thumbBone, out leftHand);
        }

        private void EnsureLateVisualGroundingCorrection()
        {
            if (!enableLateVisualGroundingCorrection)
            {
                return;
            }

            PoseSpaceLateVisualGroundingCorrection correction = GetComponent<PoseSpaceLateVisualGroundingCorrection>();
            if (correction == null)
            {
                correction = gameObject.AddComponent<PoseSpaceLateVisualGroundingCorrection>();
            }

            correction.Initialize(this);
        }

        public void ApplyLateVisualGroundingCorrection()
        {
            _grounding.ApplyLateVisualCorrection(this);
        }
    }

}
