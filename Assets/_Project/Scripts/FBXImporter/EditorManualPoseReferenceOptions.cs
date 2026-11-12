using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed class EditorManualPoseReferenceOptions
    {
        internal GameObject ReferencePrefab { get; set; }
        internal RuntimeAnimatorController ReferenceController { get; set; }
        internal bool ShouldUseFingerPoseReference { get; set; }
        internal bool ShouldUseFullBodyPoseReference { get; set; }
        internal float FullBodyPoseReferenceWeight { get; set; }
        internal bool ShouldExcludeFullBodyLowerMuscles { get; set; }
        internal bool ShouldApplyFullBodyLowerMusclesOnly { get; set; }
        internal bool ShouldApplyFullBodyLegTwistMusclesOnly { get; set; }
        internal bool ShouldApplyFullBodyRightArmMusclesOnly { get; set; }
        internal bool ShouldApplyFullBodyLeftArmMusclesOnly { get; set; }
        internal bool ShouldApplyFullBodyRightSleeveChainMusclesOnly { get; set; }
        internal float FullBodyPoseFrameGateStart { get; set; }
        internal float FullBodyPoseFrameGateEnd { get; set; }
        internal bool ShouldUseHipsLocalPositionReference { get; set; }
        internal bool ShouldUseBodyRotationReference { get; set; }
        internal bool ShouldUseHandLocalRotationReference { get; set; }
        internal bool ShouldUseFootLocalRotationReference { get; set; }
        internal bool ShouldUseLowerBodySegmentDirectionReference { get; set; }
        internal bool ShouldUseFootHipsAlignedResidualYawReference { get; set; }
        internal bool ShouldUsePostSetHumanPoseRightEndpointPositionReference { get; set; }
        internal bool ShouldUsePostSetHumanPoseRightFootEvaluatorXzReference { get; set; }
        internal bool ShouldUsePreSetHumanPoseRightEndpointPositionReference { get; set; }
        internal bool ShouldUseBodyPositionXzReference { get; set; }
        internal bool ShouldUseRightSleeveSilhouetteOffsetReference { get; set; }
        internal float RightSleeveSilhouetteOffsetX { get; set; }
        internal float RightSleeveSilhouetteFrameGateStart { get; set; }
        internal float RightSleeveSilhouetteFrameGateEnd { get; set; }
        internal bool ShouldUseBipedIkFootPositionReference { get; set; }

        internal bool ShouldApply =>
            ShouldUseFingerPoseReference ||
            ShouldUseFullBodyPoseReference ||
            ShouldUseHipsLocalPositionReference ||
            ShouldUseBodyRotationReference ||
            ShouldUseHandLocalRotationReference ||
            ShouldUseFootLocalRotationReference ||
            ShouldUseLowerBodySegmentDirectionReference ||
            ShouldUseFootHipsAlignedResidualYawReference ||
            ShouldUsePostSetHumanPoseRightEndpointPositionReference ||
            ShouldUsePostSetHumanPoseRightFootEvaluatorXzReference ||
            ShouldUsePreSetHumanPoseRightEndpointPositionReference ||
            ShouldUseBodyPositionXzReference ||
            ShouldUseRightSleeveSilhouetteOffsetReference ||
            ShouldUseBipedIkFootPositionReference;
    }
}
