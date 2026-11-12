using UnityEngine;
using Fbx2Vmd.Retargeting;

namespace Fbx2Vmd.FBXImporter
{
    // RetargetingSettings → 컴포넌트 필드 바인딩을 담당하는 partial 분할. Initialize의 설정 적용 블록을 이관함.
    public partial class PoseSpaceRetargeter
    {
        private void ApplyRetargetingSettings(RetargetingSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            ApplyCoreReferenceSettings(settings);
            ApplyManualFullBodyPoseSettings(settings);
            ApplyManualThumbHandSettings(settings);
            ApplyManualBodyPositionSettings(settings);
            ApplyManualLegSegmentSettings(settings);
            ApplyManualFootEndpointSettings(settings);
            ApplyPostSetEndpointSettings(settings);
            ApplyPreSetEndpointSettings(settings);
            ApplyThumbTuningAndClampSettings(settings);
            ApplyGroundingAndSmoothingSettings(settings);
        }

        private void ApplyCoreReferenceSettings(RetargetingSettings settings)
        {
            groundOffset = settings.HeightOffset;
            _movementScaleMultiplier = RootMotionGuard.NormalizeMovementScaleMultiplier(settings.MovementScaleMultiplier);
            ShouldPreserveFbxRootRotation = settings.ShouldPreserveFbxRootRotation && !settings.ShouldUseLegacyPoseSpaceFacingCorrection;
            preserveTargetBodyPosition = settings.ShouldPreserveRetargetBodyPosition;
            useBodyPositionXZRootMotion = settings.ShouldUseRetargetBodyPositionXZRootMotion;
            ShouldUseEditorHumanoidRootTranslationReference = settings.ShouldUseEditorHumanoidRootTranslationReference;
            editorHumanoidRootTranslationWeight = Mathf.Clamp01(settings.editorHumanoidRootTranslationWeight);
            editorHumanoidRootTranslationCurrentWeight = Mathf.Clamp(settings.editorHumanoidRootTranslationCurrentWeight, 0.05f, 1f);
            ShouldStabilizeGroundedFootXZ = settings.ShouldStabilizeGroundedFootXZ;
            groundedFootLockWeight = Mathf.Clamp01(settings.GroundedFootLockWeight);
            maxGroundedFootLockStep = Mathf.Max(0.001f, settings.MaxGroundedFootLockStep);
            clampMusclesToHumanRange = settings.clampRetargetMusclesToHumanRange;
            enableAnatomicalArmGuard = settings.enableAnatomicalArmGuard;
            armStretchMuscleLimit = settings.ArmStretchMuscleLimit;
            clampArmStretchMuscles = settings.clampRetargetArmStretchMuscles;
            upperArmTwistMuscleLimit = settings.UpperArmTwistMuscleLimit;
            lowerArmTwistMuscleLimit = settings.LowerArmTwistMuscleLimit;
            enableThumbAnatomicalGuard = settings.enableThumbAnatomicalGuard;
            thumbStretchMin = settings.ThumbStretchMin;
            thumbStretchMax = settings.ThumbStretchMax;
            thumbStretchOffset = settings.EffectiveThumbStretchOffset;
            preserveManualFingerReferenceThumbMuscles = settings.preserveManualFingerReferenceThumbMuscles;
        }

        private void ApplyManualFullBodyPoseSettings(RetargetingSettings settings)
        {
            ShouldUseManualAnimatorFullBodyPoseReference = settings.ShouldUseManualAnimatorFullBodyPoseReference;
            manualAnimatorFullBodyPoseReferenceWeight = Mathf.Clamp01(settings.manualAnimatorFullBodyPoseReferenceWeight);
            ShouldExcludeManualAnimatorFullBodyLowerMuscles =
                settings.ShouldExcludeManualAnimatorFullBodyLowerMuscles;
            ShouldApplyManualAnimatorFullBodyLowerMusclesOnly =
                settings.ShouldApplyManualAnimatorFullBodyLowerMusclesOnly;
            ShouldApplyManualAnimatorFullBodyLegTwistMusclesOnly =
                settings.ShouldApplyManualAnimatorFullBodyLegTwistMusclesOnly;
            manualAnimatorFullBodyPoseRightArmMusclesOnly =
                settings.manualAnimatorFullBodyPoseRightArmMusclesOnly;
            manualAnimatorFullBodyPoseLeftArmMusclesOnly =
                settings.manualAnimatorFullBodyPoseLeftArmMusclesOnly;
            manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly =
                settings.manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly;
            manualAnimatorFullBodyPoseFrameGateStart =
                Mathf.Max(0f, settings.manualAnimatorFullBodyPoseFrameGateStart);
            manualAnimatorFullBodyPoseFrameGateEnd =
                Mathf.Max(0f, settings.manualAnimatorFullBodyPoseFrameGateEnd);
            ShouldUseSetHumanPoseRightLegTwistOutputReference =
                settings.ShouldUseSetHumanPoseRightLegTwistOutputReference;
            setHumanPoseRightLegTwistOutputReferenceWeight =
                Mathf.Clamp01(settings.setHumanPoseRightLegTwistOutputReferenceWeight);
            setHumanPoseRightLegTwistOutputReferenceMaxDelta =
                Mathf.Max(0f, settings.setHumanPoseRightLegTwistOutputReferenceMaxDelta);
        }

        private void ApplyManualThumbHandSettings(RetargetingSettings settings)
        {
            useManualAnimatorThumbLocalRotationReference = settings.useManualAnimatorThumbLocalRotationReference;
            useManualAnimatorHandLocalRotationReference = settings.useManualAnimatorHandLocalRotationReference;
            useManualAnimatorThumbSegmentDirectionReference = settings.useManualAnimatorThumbSegmentDirectionReference;
            manualAnimatorThumbSegmentDirectionWeight = settings.manualAnimatorThumbSegmentDirectionWeight;
            useManualAnimatorThumbHandDirectionReference = settings.useManualAnimatorThumbHandDirectionReference;
            manualAnimatorThumbHandDirectionWeight = settings.manualAnimatorThumbHandDirectionWeight;
            useManualAnimatorHandPalmFrameReference = settings.useManualAnimatorHandPalmFrameReference;
            manualAnimatorHandPalmFrameWeight = settings.manualAnimatorHandPalmFrameWeight;
            useManualAnimatorThumbBasePositionReference = settings.useManualAnimatorThumbBasePositionReference;
            ShouldUseManualAnimatorHipsLocalPositionReference = settings.ShouldUseManualAnimatorHipsLocalPositionReference;
            ShouldUseManualAnimatorBodyRotationReference = settings.ShouldUseManualAnimatorBodyRotationReference;
            manualAnimatorBodyRotationReferenceWeight = Mathf.Clamp01(settings.manualAnimatorBodyRotationReferenceWeight);
            ShouldUseManualAnimatorBodyPositionYReference = settings.ShouldUseManualAnimatorBodyPositionYReference;
            ShouldUseManualAnimatorBodyPositionXzReference = settings.ShouldUseManualAnimatorBodyPositionXzReference;
        }

        private void ApplyManualBodyPositionSettings(RetargetingSettings settings)
        {
            manualAnimatorBodyPositionXzReferenceWeight =
                Mathf.Clamp01(settings.manualAnimatorBodyPositionXzReferenceWeight);
            manualAnimatorBodyPositionXzReferenceMaxOffset =
                Mathf.Max(0f, settings.manualAnimatorBodyPositionXzReferenceMaxOffset);
            manualAnimatorBodyPositionXzReferenceFrameGateStart =
                Mathf.Max(0f, settings.manualAnimatorBodyPositionXzReferenceFrameGateStart);
            manualAnimatorBodyPositionXzReferenceFrameGateEnd =
                Mathf.Max(0f, settings.manualAnimatorBodyPositionXzReferenceFrameGateEnd);
            manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames =
                Mathf.Max(0f, settings.manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames);
            manualAnimatorBodyPositionXzReferenceAxisXScale =
                Mathf.Clamp01(settings.manualAnimatorBodyPositionXzReferenceAxisXScale);
            manualAnimatorBodyPositionXzReferenceAxisZScale =
                Mathf.Clamp01(settings.manualAnimatorBodyPositionXzReferenceAxisZScale);
            manualAnimatorHipsLocalPositionWeight = Mathf.Clamp01(settings.manualAnimatorHipsLocalPositionWeight);
            manualAnimatorHipsLocalPositionMaxOffset = Mathf.Max(0.001f, settings.manualAnimatorHipsLocalPositionMaxOffset);
            ShouldUseManualAnimatorFootHeightGroundingReference = settings.ShouldUseManualAnimatorFootHeightGroundingReference;
            manualAnimatorFootHeightGroundingReferenceWeight = Mathf.Clamp01(settings.manualAnimatorFootHeightGroundingReferenceWeight);
            manualAnimatorFootHeightGroundingReferenceMaxLift = Mathf.Max(0f, settings.manualAnimatorFootHeightGroundingReferenceMaxLift);
            ShouldUseManualAnimatorFootLocalRotationReference = settings.ShouldUseManualAnimatorFootLocalRotationReference;
            manualAnimatorFootLocalRotationReferenceWeight = Mathf.Clamp01(settings.manualAnimatorFootLocalRotationReferenceWeight);
        }

        private void ApplyManualLegSegmentSettings(RetargetingSettings settings)
        {
            ShouldUseManualAnimatorLowerBodySegmentDirectionReference = settings.ShouldUseManualAnimatorLowerBodySegmentDirectionReference;
            manualAnimatorLowerBodySegmentDirectionReferenceWeight = Mathf.Clamp01(settings.manualAnimatorLowerBodySegmentDirectionReferenceWeight);
            manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle = Mathf.Max(0f, settings.manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle);
            ShouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference =
                settings.ShouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference;
            manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle =
                Mathf.Max(0f, settings.manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle);
            ShouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference =
                settings.ShouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference;
            manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle =
                Mathf.Max(0f, settings.manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle);
            manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle =
                Mathf.Max(0f, settings.manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle);
            manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle =
                Mathf.Max(0f, settings.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle);
        }

        private void ApplyManualFootEndpointSettings(RetargetingSettings settings)
        {
            manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale =
                Mathf.Clamp01(settings.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale);
            manualAnimatorRightLowerLegToFootSegmentDirectionReferenceBlendWeight =
                Mathf.Clamp01(settings.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceBlendWeight);
            manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateStart =
                Mathf.Max(0f, settings.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateStart);
            manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateEnd =
                Mathf.Max(0f, settings.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateEnd);
            manualAnimatorRightLowerLegToFootSegmentDirectionReferenceEndpointBlendWeight =
                Mathf.Clamp01(settings.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceEndpointBlendWeight);
            ShouldDisableManualAnimatorFootToToesSegmentDirectionReference =
                settings.ShouldDisableManualAnimatorFootToToesSegmentDirectionReference;
            manualAnimatorFootToToesSegmentDirectionReferenceMaxAngle =
                Mathf.Max(0f, settings.manualAnimatorFootToToesSegmentDirectionReferenceMaxAngle);
            ShouldUseManualAnimatorFootHipsAlignedResidualYawReference = settings.ShouldUseManualAnimatorFootHipsAlignedResidualYawReference;
            manualAnimatorFootHipsAlignedResidualYawReferenceWeight = Mathf.Clamp01(settings.manualAnimatorFootHipsAlignedResidualYawReferenceWeight);
            manualAnimatorFootHipsAlignedResidualYawReferenceMaxAngle = Mathf.Max(0f, settings.manualAnimatorFootHipsAlignedResidualYawReferenceMaxAngle);
            useManualAnimatorBipedIkFootPositionReference = settings.useManualAnimatorBipedIkFootPositionReference;
            manualAnimatorBipedIkFootPositionReferenceWeight = Mathf.Clamp01(settings.manualAnimatorBipedIkFootPositionReferenceWeight);
            manualAnimatorBipedIkFootPositionReferenceMaxOffset = Mathf.Max(0f, settings.manualAnimatorBipedIkFootPositionReferenceMaxOffset);
        }

        private void ApplyPostSetEndpointSettings(RetargetingSettings settings)
        {
            usePostSetHumanPoseRightEndpointPositionReference =
                settings.usePostSetHumanPoseRightEndpointPositionReference;
            postSetHumanPoseRightEndpointPositionReferenceWeight =
                Mathf.Clamp01(settings.postSetHumanPoseRightEndpointPositionReferenceWeight);
            postSetHumanPoseRightEndpointPositionReferenceMaxOffset =
                Mathf.Max(0f, settings.postSetHumanPoseRightEndpointPositionReferenceMaxOffset);
            postSetHumanPoseRightEndpointPositionReferencePositiveZScale =
                Mathf.Clamp01(settings.postSetHumanPoseRightEndpointPositionReferencePositiveZScale);
            postSetHumanPoseRightEndpointPositionReferenceToesBlendWeight =
                Mathf.Clamp01(settings.postSetHumanPoseRightEndpointPositionReferenceToesBlendWeight);
            postSetHumanPoseRightEndpointPositionReferenceFrameGateStart =
                Mathf.Max(0f, settings.postSetHumanPoseRightEndpointPositionReferenceFrameGateStart);
            postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd =
                Mathf.Max(0f, settings.postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd);
            ShouldUseLeftSideForPostSetHumanPoseEndpointPosition =
                settings.ShouldUseLeftSideForPostSetHumanPoseEndpointPosition;
            usePostSetHumanPoseRightFootEvaluatorXzReference =
                settings.usePostSetHumanPoseRightFootEvaluatorXzReference;
            postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude =
                Mathf.Max(0f, settings.postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude);
        }

        private void ApplyPreSetEndpointSettings(RetargetingSettings settings)
        {
            usePreSetHumanPoseRightEndpointPositionReference =
                settings.usePreSetHumanPoseRightEndpointPositionReference;
            preSetHumanPoseRightEndpointPositionReferenceWeight =
                Mathf.Clamp01(settings.preSetHumanPoseRightEndpointPositionReferenceWeight);
            preSetHumanPoseRightEndpointPositionReferenceMaxOffset =
                Mathf.Max(0f, settings.preSetHumanPoseRightEndpointPositionReferenceMaxOffset);
            preSetHumanPoseRightEndpointPositionReferencePositiveZScale =
                Mathf.Clamp01(settings.preSetHumanPoseRightEndpointPositionReferencePositiveZScale);
            preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight =
                Mathf.Clamp01(settings.preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight);
            preSetHumanPoseRightEndpointPositionReferenceFrameGateStart =
                Mathf.Max(0f, settings.preSetHumanPoseRightEndpointPositionReferenceFrameGateStart);
            preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd =
                Mathf.Max(0f, settings.preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd);
            ShouldUseLeftSideForPreSetHumanPoseEndpointPosition =
                settings.ShouldUseLeftSideForPreSetHumanPoseEndpointPosition;
            preSetHumanPoseEndpointPositionUseGhostCurrentBasis =
                settings.preSetHumanPoseEndpointPositionUseGhostCurrentBasis;
            ShouldInvertPreSetHumanPoseEndpointPositionBodyX =
                settings.ShouldInvertPreSetHumanPoseEndpointPositionBodyX;
            ShouldInvertPreSetHumanPoseEndpointPositionBodyZ =
                settings.ShouldInvertPreSetHumanPoseEndpointPositionBodyZ;
        }

        private void ApplyThumbTuningAndClampSettings(RetargetingSettings settings)
        {
            manualAnimatorThumbBasePositionWeight = settings.manualAnimatorThumbBasePositionWeight;
            manualAnimatorThumbBasePositionMaxOffset = settings.manualAnimatorThumbBasePositionMaxOffset;
            thumbSpreadMin = settings.ThumbSpreadMin;
            thumbSpreadMax = settings.ThumbSpreadMax;
            logThumbAnatomicalGuardCorrections = settings.logThumbAnatomicalGuardCorrections;
            enableThumbLocalRotationGuard = settings.EffectiveThumbLocalRotationGuard;
            thumbProximalMaxLocalAngle = settings.EffectiveThumbProximalMaxLocalAngle;
            thumbIntermediateMaxLocalAngle = settings.ThumbIntermediateMaxLocalAngle;
            thumbDistalMaxLocalAngle = settings.ThumbDistalMaxLocalAngle;
            logThumbLocalRotationGuardCorrections = settings.logThumbLocalRotationGuardCorrections;
            clampRootDeltaSpikes = settings.clampRetargetRootDeltaSpikes;
            maxRootDeltaPerFrame = settings.MaxRetargetRootDeltaPerFrame;
            logRootDeltaSpikes = settings.logRetargetRootDeltaSpikes;
            clampTargetHipsLocalPositionSpikes = settings.clampRetargetHipsLocalPositionSpikes;
            maxTargetHipsLocalPositionDeltaPerFrame = Mathf.Max(0.005f, settings.MaxRetargetHipsLocalPositionDeltaPerFrame);
        }

        private void ApplyGroundingAndSmoothingSettings(RetargetingSettings settings)
        {
            smoothGrounding = settings.smoothRetargetGrounding;
            maxGroundingVerticalStepPerFrame = Mathf.Max(0.001f, settings.MaxGroundingVerticalStepPerFrame);
            groundingSmoothing = Mathf.Clamp01(settings.GroundingSmoothing);
            groundingDeadZone = Mathf.Max(0f, settings.GroundingDeadZone);
            freezeRootYAfterInitialGrounding = settings.FreezeRootYAfterInitialGrounding;
            clampLegacyAnimationVisualStep = settings.clampRetargetVisualClipStep;
            legacyAnimationVisualFrameRate = Mathf.Clamp(settings.RetargetVisualClipFrameRate, 15f, 120f);
            smoothPoseOnLegacyAnimationStepSpike = settings.smoothRetargetPoseOnVisualStepSpike;
            poseVisualSpikeCurrentWeight = Mathf.Clamp(settings.RetargetPoseVisualSpikeCurrentWeight, 0.1f, 1f);
            poseVisualSpikeForearmStretchClampMaxOffset =
                Mathf.Clamp01(settings.RetargetPoseVisualSpikeForearmStretchClampMaxOffset);
            poseVisualMuscleDeltaThreshold = Mathf.Clamp(settings.RetargetPoseVisualMuscleDeltaThreshold, 0.05f, 1f);
            rejectRendererGroundingOutliers = settings.rejectRendererGroundingOutliers;
            maxRendererFootGroundingSeparation = Mathf.Max(0.02f, settings.MaxRendererFootGroundingSeparation);
            smoothLateVisualGroundingCorrection = settings.smoothLateVisualGroundingCorrection;
            lateVisualGroundingSnapThreshold = Mathf.Max(0.005f, settings.LateVisualGroundingSnapThreshold);
            lateVisualGroundingSmoothing = Mathf.Clamp01(settings.LateVisualGroundingSmoothing);
            maxLateVisualGroundingStepPerFrame = Mathf.Max(0.001f, settings.MaxLateVisualGroundingStepPerFrame);
            ShouldLockTargetHumanoidBonePositions = settings.ShouldLockTargetHumanoidBonePositions;
        }
    }
}
