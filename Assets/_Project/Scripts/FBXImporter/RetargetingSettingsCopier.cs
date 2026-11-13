namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// RetargetingSettings 스냅샷에 파이프라인 설정을 복사한다.
    /// 설정값 소유는 RetargetingSettings, 복사 책임만 이 클래스가 담당한다.
    /// </summary>
    internal static class RetargetingSettingsCopier
    {
        internal static void CopyCoreRetargetSettings(RetargetingSettings settings, FBXVmdPipeline pipeline)
        {
            settings.ShouldUseLegacyPoseSpaceFacingCorrection = pipeline.ShouldUseLegacyPoseSpaceFacingCorrection;
            settings.HeightOffset = pipeline.HeightOffset;
            settings.MovementScaleMultiplier = pipeline.MovementScaleMultiplier;
            settings.ShouldPreserveFbxRootRotation = pipeline.ShouldPreserveFbxRootRotation;
            settings.ShouldPreserveRetargetBodyPosition = pipeline.ShouldPreserveRetargetBodyPosition;
            settings.ShouldUseRetargetBodyPositionXZRootMotion = pipeline.ShouldUseRetargetBodyPositionXZRootMotion;
            settings.ShouldUseEditorHumanoidRootTranslationReference = pipeline.ShouldUseEditorHumanoidRootTranslationReference;
            settings.editorHumanoidRootTranslationWeight = pipeline.editorHumanoidRootTranslationWeight;
            settings.editorHumanoidRootTranslationCurrentWeight = pipeline.editorHumanoidRootTranslationCurrentWeight;
            settings.ShouldStabilizeGroundedFootXZ = pipeline.ShouldStabilizeGroundedFootXZ;
            settings.GroundedFootLockWeight = pipeline.GroundedFootLockWeight;
            settings.MaxGroundedFootLockStep = pipeline.MaxGroundedFootLockStep;
        }

        internal static void CopyMuscleAndArmGuardSettings(RetargetingSettings settings, FBXVmdPipeline pipeline)
        {
            settings.clampRetargetMusclesToHumanRange = pipeline.clampRetargetMusclesToHumanRange;
            settings.enableAnatomicalArmGuard = pipeline.enableAnatomicalArmGuard;
            settings.ArmStretchMuscleLimit = pipeline.ArmStretchMuscleLimit;
            settings.clampRetargetArmStretchMuscles = pipeline.clampRetargetArmStretchMuscles;
            settings.UpperArmTwistMuscleLimit = pipeline.UpperArmTwistMuscleLimit;
            settings.LowerArmTwistMuscleLimit = pipeline.LowerArmTwistMuscleLimit;
        }

        internal static void CopyThumbGuardSettings(RetargetingSettings settings, FBXVmdPipeline pipeline)
        {
            settings.enableThumbAnatomicalGuard = pipeline.enableThumbAnatomicalGuard;
            settings.ThumbStretchMin = pipeline.ThumbStretchMin;
            settings.ThumbStretchMax = pipeline.ThumbStretchMax;
            settings.EffectiveThumbStretchOffset = pipeline.EffectiveThumbStretchOffset;
            settings.preserveManualFingerReferenceThumbMuscles = pipeline.preserveManualFingerReferenceThumbMuscles;
        }

        internal static void CopyManualAnimatorPoseSettings(RetargetingSettings settings, FBXVmdPipeline pipeline)
        {
            settings.ShouldUseManualAnimatorFullBodyPoseReference = pipeline.ShouldUseManualAnimatorFullBodyPoseReference;
            settings.manualAnimatorFullBodyPoseReferenceWeight = pipeline.manualAnimatorFullBodyPoseReferenceWeight;
            settings.ShouldExcludeManualAnimatorFullBodyLowerMuscles = pipeline.ShouldExcludeManualAnimatorFullBodyLowerMuscles;
            settings.ShouldApplyManualAnimatorFullBodyLowerMusclesOnly = pipeline.ShouldApplyManualAnimatorFullBodyLowerMusclesOnly;
            settings.ShouldApplyManualAnimatorFullBodyLegTwistMusclesOnly = pipeline.ShouldApplyManualAnimatorFullBodyLegTwistMusclesOnly;
            settings.manualAnimatorFullBodyPoseRightArmMusclesOnly = pipeline.manualAnimatorFullBodyPoseRightArmMusclesOnly;
            settings.manualAnimatorFullBodyPoseLeftArmMusclesOnly = pipeline.manualAnimatorFullBodyPoseLeftArmMusclesOnly;
            settings.manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly = pipeline.manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly;
            settings.manualAnimatorFullBodyPoseFrameGateStart = pipeline.manualAnimatorFullBodyPoseFrameGateStart;
            settings.manualAnimatorFullBodyPoseFrameGateEnd = pipeline.manualAnimatorFullBodyPoseFrameGateEnd;
            settings.ShouldUseSetHumanPoseRightLegTwistOutputReference = pipeline.ShouldUseSetHumanPoseRightLegTwistOutputReference;
            settings.setHumanPoseRightLegTwistOutputReferenceWeight = pipeline.setHumanPoseRightLegTwistOutputReferenceWeight;
            settings.setHumanPoseRightLegTwistOutputReferenceMaxDelta = pipeline.setHumanPoseRightLegTwistOutputReferenceMaxDelta;
        }

        internal static void CopyManualAnimatorHandSettings(RetargetingSettings settings, FBXVmdPipeline pipeline)
        {
            settings.useManualAnimatorThumbLocalRotationReference = pipeline.useManualAnimatorThumbLocalRotationReference;
            settings.useManualAnimatorHandLocalRotationReference = pipeline.useManualAnimatorHandLocalRotationReference;
            settings.useManualAnimatorThumbSegmentDirectionReference = pipeline.useManualAnimatorThumbSegmentDirectionReference;
            settings.manualAnimatorThumbSegmentDirectionWeight = pipeline.manualAnimatorThumbSegmentDirectionWeight;
            settings.useManualAnimatorThumbHandDirectionReference = pipeline.useManualAnimatorThumbHandDirectionReference;
            settings.manualAnimatorThumbHandDirectionWeight = pipeline.manualAnimatorThumbHandDirectionWeight;
            settings.useManualAnimatorHandPalmFrameReference = pipeline.useManualAnimatorHandPalmFrameReference;
            settings.manualAnimatorHandPalmFrameWeight = pipeline.manualAnimatorHandPalmFrameWeight;
            settings.useManualAnimatorThumbBasePositionReference = pipeline.useManualAnimatorThumbBasePositionReference;
        }

        internal static void CopyBodyPositionSettings(RetargetingSettings settings, FBXVmdPipeline pipeline)
        {
            settings.ShouldUseManualAnimatorHipsLocalPositionReference = pipeline.ShouldUseManualAnimatorHipsLocalPositionReference;
            settings.ShouldUseManualAnimatorBodyRotationReference = pipeline.ShouldUseManualAnimatorBodyRotationReference;
            settings.manualAnimatorBodyRotationReferenceWeight = pipeline.manualAnimatorBodyRotationReferenceWeight;
            settings.ShouldUseManualAnimatorBodyPositionYReference = pipeline.ShouldUseManualAnimatorBodyPositionYReference;
            settings.ShouldUseManualAnimatorBodyPositionXzReference = pipeline.ShouldUseManualAnimatorBodyPositionXzReference;
            settings.manualAnimatorBodyPositionXzReferenceWeight = pipeline.manualAnimatorBodyPositionXzReferenceWeight;
            settings.manualAnimatorBodyPositionXzReferenceMaxOffset = pipeline.manualAnimatorBodyPositionXzReferenceMaxOffset;
            settings.manualAnimatorBodyPositionXzReferenceFrameGateStart = pipeline.manualAnimatorBodyPositionXzReferenceFrameGateStart;
            settings.manualAnimatorBodyPositionXzReferenceFrameGateEnd = pipeline.manualAnimatorBodyPositionXzReferenceFrameGateEnd;
            settings.manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames = pipeline.manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames;
            settings.manualAnimatorBodyPositionXzReferenceAxisXScale = pipeline.manualAnimatorBodyPositionXzReferenceAxisXScale;
            settings.manualAnimatorBodyPositionXzReferenceAxisZScale = pipeline.manualAnimatorBodyPositionXzReferenceAxisZScale;
            settings.manualAnimatorHipsLocalPositionWeight = pipeline.manualAnimatorHipsLocalPositionWeight;
            settings.manualAnimatorHipsLocalPositionMaxOffset = pipeline.manualAnimatorHipsLocalPositionMaxOffset;
        }

        internal static void CopyFootAndLegSegmentSettings(RetargetingSettings settings, FBXVmdPipeline pipeline)
        {
            settings.ShouldUseManualAnimatorFootHeightGroundingReference = pipeline.ShouldUseManualAnimatorFootHeightGroundingReference;
            settings.manualAnimatorFootHeightGroundingReferenceWeight = pipeline.manualAnimatorFootHeightGroundingReferenceWeight;
            settings.manualAnimatorFootHeightGroundingReferenceMaxLift = pipeline.manualAnimatorFootHeightGroundingReferenceMaxLift;
            settings.ShouldUseManualAnimatorFootLocalRotationReference = pipeline.ShouldUseManualAnimatorFootLocalRotationReference;
            settings.manualAnimatorFootLocalRotationReferenceWeight = pipeline.manualAnimatorFootLocalRotationReferenceWeight;
            settings.ShouldUseManualAnimatorLowerBodySegmentDirectionReference = pipeline.ShouldUseManualAnimatorLowerBodySegmentDirectionReference;
            settings.manualAnimatorLowerBodySegmentDirectionReferenceWeight = pipeline.manualAnimatorLowerBodySegmentDirectionReferenceWeight;
            settings.manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle = pipeline.manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle;
            settings.ShouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference = pipeline.ShouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference;
            settings.manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle = pipeline.manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle;
            settings.ShouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference = pipeline.ShouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference;
            settings.manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle = pipeline.manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle;
            CopyLowerLegAndFootDetailSettings(settings, pipeline);
        }

        private static void CopyLowerLegAndFootDetailSettings(RetargetingSettings settings, FBXVmdPipeline pipeline)
        {
            settings.manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle = pipeline.manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle;
            settings.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle = pipeline.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle;
            settings.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale = pipeline.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale;
            settings.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceBlendWeight = pipeline.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceBlendWeight;
            settings.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateStart = pipeline.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateStart;
            settings.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateEnd = pipeline.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateEnd;
            settings.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceEndpointBlendWeight = pipeline.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceEndpointBlendWeight;
            settings.ShouldDisableManualAnimatorFootToToesSegmentDirectionReference = pipeline.ShouldDisableManualAnimatorFootToToesSegmentDirectionReference;
            settings.manualAnimatorFootToToesSegmentDirectionReferenceMaxAngle = pipeline.manualAnimatorFootToToesSegmentDirectionReferenceMaxAngle;
            settings.ShouldUseManualAnimatorFootHipsAlignedResidualYawReference = pipeline.ShouldUseManualAnimatorFootHipsAlignedResidualYawReference;
            settings.manualAnimatorFootHipsAlignedResidualYawReferenceWeight = pipeline.manualAnimatorFootHipsAlignedResidualYawReferenceWeight;
            settings.manualAnimatorFootHipsAlignedResidualYawReferenceMaxAngle = pipeline.manualAnimatorFootHipsAlignedResidualYawReferenceMaxAngle;
            settings.useManualAnimatorBipedIkFootPositionReference = pipeline.useManualAnimatorBipedIkFootPositionReference;
            settings.manualAnimatorBipedIkFootPositionReferenceWeight = pipeline.manualAnimatorBipedIkFootPositionReferenceWeight;
            settings.manualAnimatorBipedIkFootPositionReferenceMaxOffset = pipeline.manualAnimatorBipedIkFootPositionReferenceMaxOffset;
        }

        internal static void CopyEndpointPositionSettings(RetargetingSettings settings, FBXVmdPipeline pipeline)
        {
            CopyPostEndpointSettings(settings, pipeline);
            CopyPreEndpointAndThumbGuardSettings(settings, pipeline);
        }

        private static void CopyPostEndpointSettings(RetargetingSettings settings, FBXVmdPipeline pipeline)
        {
            settings.usePostSetHumanPoseRightEndpointPositionReference = pipeline.usePostSetHumanPoseRightEndpointPositionReference;
            settings.postSetHumanPoseRightEndpointPositionReferenceWeight = pipeline.postSetHumanPoseRightEndpointPositionReferenceWeight;
            settings.postSetHumanPoseRightEndpointPositionReferenceMaxOffset = pipeline.postSetHumanPoseRightEndpointPositionReferenceMaxOffset;
            settings.postSetHumanPoseRightEndpointPositionReferencePositiveZScale = pipeline.postSetHumanPoseRightEndpointPositionReferencePositiveZScale;
            settings.postSetHumanPoseRightEndpointPositionReferenceToesBlendWeight = pipeline.postSetHumanPoseRightEndpointPositionReferenceToesBlendWeight;
            settings.postSetHumanPoseRightEndpointPositionReferenceFrameGateStart = pipeline.postSetHumanPoseRightEndpointPositionReferenceFrameGateStart;
            settings.postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd = pipeline.postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd;
            settings.ShouldUseLeftSideForPostSetHumanPoseEndpointPosition = pipeline.ShouldUseLeftSideForPostSetHumanPoseEndpointPosition;
            settings.usePostSetHumanPoseRightFootEvaluatorXzReference = pipeline.usePostSetHumanPoseRightFootEvaluatorXzReference;
            settings.postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude = pipeline.postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude;
        }

        private static void CopyPreEndpointAndThumbGuardSettings(RetargetingSettings settings, FBXVmdPipeline pipeline)
        {
            settings.usePreSetHumanPoseRightEndpointPositionReference = pipeline.usePreSetHumanPoseRightEndpointPositionReference;
            settings.preSetHumanPoseRightEndpointPositionReferenceWeight = pipeline.preSetHumanPoseRightEndpointPositionReferenceWeight;
            settings.preSetHumanPoseRightEndpointPositionReferenceMaxOffset = pipeline.preSetHumanPoseRightEndpointPositionReferenceMaxOffset;
            settings.preSetHumanPoseRightEndpointPositionReferencePositiveZScale = pipeline.preSetHumanPoseRightEndpointPositionReferencePositiveZScale;
            settings.preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight = pipeline.preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight;
            settings.preSetHumanPoseRightEndpointPositionReferenceFrameGateStart = pipeline.preSetHumanPoseRightEndpointPositionReferenceFrameGateStart;
            settings.preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd = pipeline.preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd;
            settings.ShouldUseLeftSideForPreSetHumanPoseEndpointPosition = pipeline.ShouldUseLeftSideForPreSetHumanPoseEndpointPosition;
            settings.preSetHumanPoseEndpointPositionUseGhostCurrentBasis = pipeline.preSetHumanPoseEndpointPositionUseGhostCurrentBasis;
            settings.ShouldInvertPreSetHumanPoseEndpointPositionBodyX = pipeline.ShouldInvertPreSetHumanPoseEndpointPositionBodyX;
            settings.ShouldInvertPreSetHumanPoseEndpointPositionBodyZ = pipeline.ShouldInvertPreSetHumanPoseEndpointPositionBodyZ;
            settings.manualAnimatorThumbBasePositionWeight = pipeline.manualAnimatorThumbBasePositionWeight;
            settings.manualAnimatorThumbBasePositionMaxOffset = pipeline.manualAnimatorThumbBasePositionMaxOffset;
            settings.ThumbSpreadMin = pipeline.ThumbSpreadMin;
            settings.ThumbSpreadMax = pipeline.ThumbSpreadMax;
            settings.logThumbAnatomicalGuardCorrections = pipeline.logThumbAnatomicalGuardCorrections;
            settings.EffectiveThumbLocalRotationGuard = pipeline.EffectiveThumbLocalRotationGuard;
            settings.EffectiveThumbProximalMaxLocalAngle = pipeline.EffectiveThumbProximalMaxLocalAngle;
            settings.ThumbIntermediateMaxLocalAngle = pipeline.ThumbIntermediateMaxLocalAngle;
            settings.ThumbDistalMaxLocalAngle = pipeline.ThumbDistalMaxLocalAngle;
            settings.logThumbLocalRotationGuardCorrections = pipeline.logThumbLocalRotationGuardCorrections;
        }

        internal static void CopyStabilityAndGroundingSettings(RetargetingSettings settings, FBXVmdPipeline pipeline)
        {
            settings.clampRetargetRootDeltaSpikes = pipeline.clampRetargetRootDeltaSpikes;
            settings.MaxRetargetRootDeltaPerFrame = pipeline.MaxRetargetRootDeltaPerFrame;
            settings.logRetargetRootDeltaSpikes = pipeline.logRetargetRootDeltaSpikes;
            settings.clampRetargetHipsLocalPositionSpikes = pipeline.clampRetargetHipsLocalPositionSpikes;
            settings.MaxRetargetHipsLocalPositionDeltaPerFrame = pipeline.MaxRetargetHipsLocalPositionDeltaPerFrame;
            settings.smoothRetargetGrounding = pipeline.smoothRetargetGrounding;
            settings.MaxGroundingVerticalStepPerFrame = pipeline.MaxGroundingVerticalStepPerFrame;
            settings.GroundingSmoothing = pipeline.GroundingSmoothing;
            settings.GroundingDeadZone = pipeline.GroundingDeadZone;
            settings.FreezeRootYAfterInitialGrounding = pipeline.FreezeRootYAfterInitialGrounding;
            settings.clampRetargetVisualClipStep = pipeline.clampRetargetVisualClipStep;
            settings.RetargetVisualClipFrameRate = pipeline.RetargetVisualClipFrameRate;
            settings.smoothRetargetPoseOnVisualStepSpike = pipeline.smoothRetargetPoseOnVisualStepSpike;
            settings.RetargetPoseVisualSpikeCurrentWeight = pipeline.RetargetPoseVisualSpikeCurrentWeight;
            settings.RetargetPoseVisualSpikeForearmStretchClampMaxOffset = pipeline.RetargetPoseVisualSpikeForearmStretchClampMaxOffset;
            settings.RetargetPoseVisualMuscleDeltaThreshold = pipeline.RetargetPoseVisualMuscleDeltaThreshold;
            settings.rejectRendererGroundingOutliers = pipeline.rejectRendererGroundingOutliers;
            settings.MaxRendererFootGroundingSeparation = pipeline.MaxRendererFootGroundingSeparation;
            settings.smoothLateVisualGroundingCorrection = pipeline.smoothLateVisualGroundingCorrection;
            settings.LateVisualGroundingSnapThreshold = pipeline.LateVisualGroundingSnapThreshold;
            settings.LateVisualGroundingSmoothing = pipeline.LateVisualGroundingSmoothing;
            settings.MaxLateVisualGroundingStepPerFrame = pipeline.MaxLateVisualGroundingStepPerFrame;
            settings.ShouldLockTargetHumanoidBonePositions = pipeline.ShouldLockTargetHumanoidBonePositions;
        }
    }
}
