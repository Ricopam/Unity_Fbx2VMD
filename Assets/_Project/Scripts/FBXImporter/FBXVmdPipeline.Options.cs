using UnityEngine;
using System;

namespace Fbx2Vmd.FBXImporter
{
    public partial class FBXVmdPipeline
    {
        private HumanoidThumbDeformationGuardOptions CreateThumbDeformationGuardOptions()
        {
            var options = new HumanoidThumbDeformationGuardOptions
            {
                ProximalMaxLocalAngle = EffectiveThumbProximalMaxLocalAngle,
                IntermediateMaxLocalAngle = ThumbIntermediateMaxLocalAngle,
                DistalMaxLocalAngle = ThumbDistalMaxLocalAngle,
                ProximalRotationOffset = ThumbRotationOffset,
                MirrorRightProximalRotationOffset = mirrorRightThumbRotationOffset,
                LeftProximalRotationOffset = LeftThumbRotationOffset,
                RightProximalRotationOffset = RightThumbRotationOffset,
                LogCorrections = logThumbLocalRotationGuardCorrections,
                ClampHumanoidThumbRotations = EffectiveThumbLocalRotationGuard,
                SyncDetachedBaseHelpers =
                    syncDetachedThumbBaseHelpers && detachedThumbBaseHelperSyncWeight > 0f,
                SyncDetachedBaseHelperPositions = EffectiveDetachedThumbBaseHelperPositionSync,
                DetachedBaseHelperSyncWeight = detachedThumbBaseHelperSyncWeight,
                SuppressPoseShapingWithManualReference = PreserveManualThumbPoseWithReference
            };
            PopulateDetachedThumbGuardOptions(options);
            PopulateThumbWebbingOptions(options);
            return options;
        }

        private void PopulateDetachedThumbGuardOptions(
            HumanoidThumbDeformationGuardOptions options)
        {
            options.DetachedBaseHelperMaxLocalAngle = detachedThumbBaseHelperMaxLocalAngle;
            options.DetachedBaseHelperMaxPositionOffset = detachedThumbBaseHelperMaxPositionOffset;
            options.LeftDetachedBaseHelperDeltaAxisOffset = LeftDetachedThumbBaseHelperDeltaAxisOffset;
            options.RightDetachedBaseHelperDeltaAxisOffset = RightDetachedThumbBaseHelperDeltaAxisOffset;
            options.LeftDetachedBaseHelperTargetRotationOffset = LeftDetachedThumbBaseHelperTargetRotationOffset;
            options.RightDetachedBaseHelperTargetRotationOffset = RightDetachedThumbBaseHelperTargetRotationOffset;
            options.StabilizeDetachedBasePalm =
                stabilizeDetachedThumbBasePalm && detachedThumbBasePalmStabilizeWeight > 0f;
            options.DetachedBasePalmStabilizeWeight = detachedThumbBasePalmStabilizeWeight;
            options.DetachedBasePalmMaxLocalAngle = detachedThumbBasePalmMaxLocalAngle;
            options.EnableVisualLengthGuard = enableThumbVisualLengthGuard;
            options.ProjectionMinPalmNormal = EffectiveThumbProjectionMinPalmNormal;
            options.ProjectionMaxPalmNormal = ThumbProjectionMaxPalmNormal;
            options.ProjectionGuardWeight = ThumbProjectionGuardWeight;
            options.IndexMaxSpreadAngle = ThumbIndexMaxSpreadAngle;
            options.IndexSpreadGuardWeight = ThumbIndexSpreadGuardWeight;
            options.MaxSegmentBendAngle = ThumbMaxSegmentBendAngle;
            options.SegmentStraightenWeight = ThumbSegmentStraightenWeight;
        }

        private void PopulateThumbWebbingOptions(
            HumanoidThumbDeformationGuardOptions options)
        {
            options.StabilizeWebbingCrease =
                stabilizeThumbWebbingCrease && thumbWebbingCreaseStabilizeWeight > 0f;
            options.WebbingCreaseStabilizeWeight = thumbWebbingCreaseStabilizeWeight;
            options.WebbingCreaseMaxLocalAngle = thumbWebbingCreaseMaxLocalAngle;
            options.WebbingCreaseMaxPositionOffset = thumbWebbingCreaseMaxPositionOffset;
        }

        private EditorManualPoseReferenceOptions CreateEditorManualPoseReferenceOptions()
        {
            var options = new EditorManualPoseReferenceOptions
            {
                ReferencePrefab = manualFingerReferencePrefab,
                ReferenceController = manualFingerReferenceController,
                ShouldUseFingerPoseReference = _shouldUseManualAnimatorFingerPoseReference,
                ShouldUseFullBodyPoseReference = _shouldUseManualAnimatorFullBodyPoseReference,
                FullBodyPoseReferenceWeight = manualAnimatorFullBodyPoseReferenceWeight,
                ShouldExcludeFullBodyLowerMuscles = _shouldExcludeManualAnimatorFullBodyLowerMuscles,
                ShouldApplyFullBodyLowerMusclesOnly = _shouldApplyManualAnimatorFullBodyLowerMusclesOnly,
                ShouldApplyFullBodyLegTwistMusclesOnly = _shouldApplyManualAnimatorFullBodyLegTwistMusclesOnly,
                ShouldApplyFullBodyRightArmMusclesOnly = manualAnimatorFullBodyPoseRightArmMusclesOnly,
                ShouldApplyFullBodyLeftArmMusclesOnly = manualAnimatorFullBodyPoseLeftArmMusclesOnly,
                ShouldApplyFullBodyRightSleeveChainMusclesOnly = manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly,
                FullBodyPoseFrameGateStart = manualAnimatorFullBodyPoseFrameGateStart,
                FullBodyPoseFrameGateEnd = manualAnimatorFullBodyPoseFrameGateEnd,
                ShouldUseHipsLocalPositionReference = _shouldUseManualAnimatorHipsLocalPositionReference,
                ShouldUseBodyRotationReference = _shouldUseManualAnimatorBodyRotationReference,
                ShouldUseHandLocalRotationReference = useManualAnimatorHandLocalRotationReference,
                ShouldUseFootLocalRotationReference = _shouldUseManualAnimatorFootLocalRotationReference,
                ShouldUseLowerBodySegmentDirectionReference = _shouldUseManualAnimatorLowerBodySegmentDirectionReference,
                ShouldUseFootHipsAlignedResidualYawReference = _shouldUseManualAnimatorFootHipsAlignedResidualYawReference,
            };
            PopulateEndpointReferenceOptions(options);
            return options;
        }

        private void PopulateEndpointReferenceOptions(
            EditorManualPoseReferenceOptions options)
        {
            options.ShouldUsePostSetHumanPoseRightEndpointPositionReference = usePostSetHumanPoseRightEndpointPositionReference;
            options.ShouldUsePostSetHumanPoseRightFootEvaluatorXzReference = usePostSetHumanPoseRightFootEvaluatorXzReference;
            options.ShouldUsePreSetHumanPoseRightEndpointPositionReference = usePreSetHumanPoseRightEndpointPositionReference;
            options.ShouldUseBodyPositionXzReference = _shouldUseManualAnimatorBodyPositionXzReference;
            options.ShouldUseRightSleeveSilhouetteOffsetReference = useYybRightSleeveSilhouetteLocalOffsetReference;
            options.RightSleeveSilhouetteOffsetX = yybRightSleeveSilhouetteLocalOffsetX;
            options.RightSleeveSilhouetteFrameGateStart = yybRightSleeveSilhouetteLocalOffsetFrameGateStart;
            options.RightSleeveSilhouetteFrameGateEnd = yybRightSleeveSilhouetteLocalOffsetFrameGateEnd;
            options.ShouldUseBipedIkFootPositionReference = useManualAnimatorBipedIkFootPositionReference;
        }

    }
}
