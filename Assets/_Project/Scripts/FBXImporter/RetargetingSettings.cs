using System;

namespace Fbx2Vmd.FBXImporter
{
    public sealed class RetargetingSettings
    {
        public bool ShouldUseLegacyPoseSpaceFacingCorrection { get; internal set; }
        public float HeightOffset { get; internal set; }
        public float MovementScaleMultiplier { get; internal set; }
        public bool ShouldPreserveFbxRootRotation { get; internal set; }
        public bool ShouldPreserveRetargetBodyPosition { get; internal set; }
        public bool ShouldUseRetargetBodyPositionXZRootMotion { get; internal set; }
        public bool ShouldUseEditorHumanoidRootTranslationReference { get; internal set; }
        public float editorHumanoidRootTranslationWeight { get; internal set; }
        public float editorHumanoidRootTranslationCurrentWeight { get; internal set; }
        public bool ShouldStabilizeGroundedFootXZ { get; internal set; }
        public float GroundedFootLockWeight { get; internal set; }
        public float MaxGroundedFootLockStep { get; internal set; }
        public bool clampRetargetMusclesToHumanRange { get; internal set; }
        public bool enableAnatomicalArmGuard { get; internal set; }
        public float ArmStretchMuscleLimit { get; internal set; }
        public bool clampRetargetArmStretchMuscles { get; internal set; }
        public float UpperArmTwistMuscleLimit { get; internal set; }
        public float LowerArmTwistMuscleLimit { get; internal set; }
        public bool enableThumbAnatomicalGuard { get; internal set; }
        public float ThumbStretchMin { get; internal set; }
        public float ThumbStretchMax { get; internal set; }
        public float EffectiveThumbStretchOffset { get; internal set; }
        public bool preserveManualFingerReferenceThumbMuscles { get; internal set; }
        public bool ShouldUseManualAnimatorFullBodyPoseReference { get; internal set; }
        public float manualAnimatorFullBodyPoseReferenceWeight { get; internal set; }
        public bool ShouldExcludeManualAnimatorFullBodyLowerMuscles { get; internal set; }
        public bool ShouldApplyManualAnimatorFullBodyLowerMusclesOnly { get; internal set; }
        public bool ShouldApplyManualAnimatorFullBodyLegTwistMusclesOnly { get; internal set; }
        public bool manualAnimatorFullBodyPoseRightArmMusclesOnly { get; internal set; }
        public bool manualAnimatorFullBodyPoseLeftArmMusclesOnly { get; internal set; }
        public bool manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly { get; internal set; }
        public float manualAnimatorFullBodyPoseFrameGateStart { get; internal set; }
        public float manualAnimatorFullBodyPoseFrameGateEnd { get; internal set; }
        public bool ShouldUseSetHumanPoseRightLegTwistOutputReference { get; internal set; }
        public float setHumanPoseRightLegTwistOutputReferenceWeight { get; internal set; }
        public float setHumanPoseRightLegTwistOutputReferenceMaxDelta { get; internal set; }
        public bool useManualAnimatorThumbLocalRotationReference { get; internal set; }
        public bool useManualAnimatorHandLocalRotationReference { get; internal set; }
        public bool useManualAnimatorThumbSegmentDirectionReference { get; internal set; }
        public float manualAnimatorThumbSegmentDirectionWeight { get; internal set; }
        public bool useManualAnimatorThumbHandDirectionReference { get; internal set; }
        public float manualAnimatorThumbHandDirectionWeight { get; internal set; }
        public bool useManualAnimatorHandPalmFrameReference { get; internal set; }
        public float manualAnimatorHandPalmFrameWeight { get; internal set; }
        public bool useManualAnimatorThumbBasePositionReference { get; internal set; }
        public bool ShouldUseManualAnimatorHipsLocalPositionReference { get; internal set; }
        public bool ShouldUseManualAnimatorBodyRotationReference { get; internal set; }
        public float manualAnimatorBodyRotationReferenceWeight { get; internal set; }
        public bool ShouldUseManualAnimatorBodyPositionYReference { get; internal set; }
        public bool ShouldUseManualAnimatorBodyPositionXzReference { get; internal set; }
        public float manualAnimatorBodyPositionXzReferenceWeight { get; internal set; }
        public float manualAnimatorBodyPositionXzReferenceMaxOffset { get; internal set; }
        public float manualAnimatorBodyPositionXzReferenceFrameGateStart { get; internal set; }
        public float manualAnimatorBodyPositionXzReferenceFrameGateEnd { get; internal set; }
        public float manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames { get; internal set; }
        public float manualAnimatorBodyPositionXzReferenceAxisXScale { get; internal set; }
        public float manualAnimatorBodyPositionXzReferenceAxisZScale { get; internal set; }
        public float manualAnimatorHipsLocalPositionWeight { get; internal set; }
        public float manualAnimatorHipsLocalPositionMaxOffset { get; internal set; }
        public bool ShouldUseManualAnimatorFootHeightGroundingReference { get; internal set; }
        public float manualAnimatorFootHeightGroundingReferenceWeight { get; internal set; }
        public float manualAnimatorFootHeightGroundingReferenceMaxLift { get; internal set; }
        public bool ShouldUseManualAnimatorFootLocalRotationReference { get; internal set; }
        public float manualAnimatorFootLocalRotationReferenceWeight { get; internal set; }
        public bool ShouldUseManualAnimatorLowerBodySegmentDirectionReference { get; internal set; }
        public float manualAnimatorLowerBodySegmentDirectionReferenceWeight { get; internal set; }
        public float manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle { get; internal set; }
        public bool ShouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference { get; internal set; }
        public float manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle { get; internal set; }
        public bool ShouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference { get; internal set; }
        public float manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle { get; internal set; }
        public float manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle { get; internal set; }
        public float manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle { get; internal set; }
        public float manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale { get; internal set; }
        public float manualAnimatorRightLowerLegToFootSegmentDirectionReferenceBlendWeight { get; internal set; }
        public float manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateStart { get; internal set; }
        public float manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateEnd { get; internal set; }
        public float manualAnimatorRightLowerLegToFootSegmentDirectionReferenceEndpointBlendWeight { get; internal set; }
        public bool ShouldDisableManualAnimatorFootToToesSegmentDirectionReference { get; internal set; }
        public float manualAnimatorFootToToesSegmentDirectionReferenceMaxAngle { get; internal set; }
        public bool ShouldUseManualAnimatorFootHipsAlignedResidualYawReference { get; internal set; }
        public float manualAnimatorFootHipsAlignedResidualYawReferenceWeight { get; internal set; }
        public float manualAnimatorFootHipsAlignedResidualYawReferenceMaxAngle { get; internal set; }
        public bool useManualAnimatorBipedIkFootPositionReference { get; internal set; }
        public float manualAnimatorBipedIkFootPositionReferenceWeight { get; internal set; }
        public float manualAnimatorBipedIkFootPositionReferenceMaxOffset { get; internal set; }
        public bool usePostSetHumanPoseRightEndpointPositionReference { get; internal set; }
        public float postSetHumanPoseRightEndpointPositionReferenceWeight { get; internal set; }
        public float postSetHumanPoseRightEndpointPositionReferenceMaxOffset { get; internal set; }
        public float postSetHumanPoseRightEndpointPositionReferencePositiveZScale { get; internal set; }
        public float postSetHumanPoseRightEndpointPositionReferenceToesBlendWeight { get; internal set; }
        public float postSetHumanPoseRightEndpointPositionReferenceFrameGateStart { get; internal set; }
        public float postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd { get; internal set; }
        public bool ShouldUseLeftSideForPostSetHumanPoseEndpointPosition { get; internal set; }
        public bool usePostSetHumanPoseRightFootEvaluatorXzReference { get; internal set; }
        public float postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude { get; internal set; }
        public bool usePreSetHumanPoseRightEndpointPositionReference { get; internal set; }
        public float preSetHumanPoseRightEndpointPositionReferenceWeight { get; internal set; }
        public float preSetHumanPoseRightEndpointPositionReferenceMaxOffset { get; internal set; }
        public float preSetHumanPoseRightEndpointPositionReferencePositiveZScale { get; internal set; }
        public float preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight { get; internal set; }
        public float preSetHumanPoseRightEndpointPositionReferenceFrameGateStart { get; internal set; }
        public float preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd { get; internal set; }
        public bool ShouldUseLeftSideForPreSetHumanPoseEndpointPosition { get; internal set; }
        public bool preSetHumanPoseEndpointPositionUseGhostCurrentBasis { get; internal set; }
        public bool ShouldInvertPreSetHumanPoseEndpointPositionBodyX { get; internal set; }
        public bool ShouldInvertPreSetHumanPoseEndpointPositionBodyZ { get; internal set; }
        public float manualAnimatorThumbBasePositionWeight { get; internal set; }
        public float manualAnimatorThumbBasePositionMaxOffset { get; internal set; }
        public float ThumbSpreadMin { get; internal set; }
        public float ThumbSpreadMax { get; internal set; }
        public bool logThumbAnatomicalGuardCorrections { get; internal set; }
        public bool EffectiveThumbLocalRotationGuard { get; internal set; }
        public float EffectiveThumbProximalMaxLocalAngle { get; internal set; }
        public float ThumbIntermediateMaxLocalAngle { get; internal set; }
        public float ThumbDistalMaxLocalAngle { get; internal set; }
        public bool logThumbLocalRotationGuardCorrections { get; internal set; }
        public bool clampRetargetRootDeltaSpikes { get; internal set; }
        public float MaxRetargetRootDeltaPerFrame { get; internal set; }
        public bool logRetargetRootDeltaSpikes { get; internal set; }
        public bool clampRetargetHipsLocalPositionSpikes { get; internal set; }
        public float MaxRetargetHipsLocalPositionDeltaPerFrame { get; internal set; }
        public bool smoothRetargetGrounding { get; internal set; }
        public float MaxGroundingVerticalStepPerFrame { get; internal set; }
        public float GroundingSmoothing { get; internal set; }
        public float GroundingDeadZone { get; internal set; }
        public bool FreezeRootYAfterInitialGrounding { get; internal set; }
        public bool clampRetargetVisualClipStep { get; internal set; }
        public float RetargetVisualClipFrameRate { get; internal set; }
        public bool smoothRetargetPoseOnVisualStepSpike { get; internal set; }
        public float RetargetPoseVisualSpikeCurrentWeight { get; internal set; }
        public float RetargetPoseVisualSpikeForearmStretchClampMaxOffset { get; internal set; }
        public float RetargetPoseVisualMuscleDeltaThreshold { get; internal set; }
        public bool rejectRendererGroundingOutliers { get; internal set; }
        public float MaxRendererFootGroundingSeparation { get; internal set; }
        public bool smoothLateVisualGroundingCorrection { get; internal set; }
        public float LateVisualGroundingSnapThreshold { get; internal set; }
        public float LateVisualGroundingSmoothing { get; internal set; }
        public float MaxLateVisualGroundingStepPerFrame { get; internal set; }
        public bool ShouldLockTargetHumanoidBonePositions { get; internal set; }

        private RetargetingSettings(FBXVmdPipeline pipeline)
        {
            RetargetingSettingsCopier.CopyCoreRetargetSettings(this, pipeline);
            RetargetingSettingsCopier.CopyMuscleAndArmGuardSettings(this, pipeline);
            RetargetingSettingsCopier.CopyThumbGuardSettings(this, pipeline);
            RetargetingSettingsCopier.CopyManualAnimatorPoseSettings(this, pipeline);
            RetargetingSettingsCopier.CopyManualAnimatorHandSettings(this, pipeline);
            RetargetingSettingsCopier.CopyBodyPositionSettings(this, pipeline);
            RetargetingSettingsCopier.CopyFootAndLegSegmentSettings(this, pipeline);
            RetargetingSettingsCopier.CopyEndpointPositionSettings(this, pipeline);
            RetargetingSettingsCopier.CopyStabilityAndGroundingSettings(this, pipeline);
        }

        public static RetargetingSettings CreateSnapshot(FBXVmdPipeline pipeline)
        {
            if (pipeline == null) throw new ArgumentNullException(nameof(pipeline));
            return new RetargetingSettings(pipeline);
        }
    }
}
