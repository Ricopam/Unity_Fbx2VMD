using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidThumbDeformationGuard
    {
        public void Configure(
            Animator animator,
            PoseSpaceRetargeter poseSpaceRetargeter,
            float proximalLimit,
            float intermediateLimit,
            float distalLimit,
            Vector3 proximalOffset,
            bool mirrorRightOffset,
            Vector3 leftProximalOffset,
            Vector3 rightProximalOffset,
            bool logCorrectionMessages,
            bool clampHumanoidRotations = true,
            bool syncDetachedBaseHelpers = true,
            bool syncDetachedBaseHelperPositions = true,
            float detachedBaseHelperSyncWeight = 1f,
            float detachedBaseHelperMaxLocalAngle = 45f,
            float detachedBaseHelperMaxPositionOffset = 0.008f,
            Vector3 leftDetachedBaseHelperDeltaAxisOffset = default,
            Vector3 rightDetachedBaseHelperDeltaAxisOffset = default,
            Vector3 leftDetachedBaseHelperTargetRotationOffset = default,
            Vector3 rightDetachedBaseHelperTargetRotationOffset = default,
            bool stabilizeDetachedBasePalm = false,
            float detachedBasePalmStabilizeWeight = 0f,
            float detachedBasePalmMaxLocalAngle = 45f,
            bool enableVisualLengthGuard = true,
            float projectionMinPalmNormal = 0.32f,
            float projectionMaxPalmNormal = 0.58f,
            float projectionGuardWeight = 0.9f,
            float indexMaxSpreadAngle = 70f,
            float indexSpreadGuardWeight = 0.9f,
            float maxSegmentBendAngle = 10f,
            float segmentStraightenWeight = 0.9f,
            bool suppressPoseShapingWithManualReference = false,
            bool stabilizeWebbingCrease = true,
            float webbingCreaseStabilizeWeight = 0.35f,
            float webbingCreaseMaxLocalAngle = 18f,
            float webbingCreaseMaxPositionOffset = 0.005f)
        {
            ConfigureBase(
                animator, poseSpaceRetargeter, proximalLimit, intermediateLimit, distalLimit,
                proximalOffset, mirrorRightOffset, leftProximalOffset, rightProximalOffset,
                logCorrectionMessages, clampHumanoidRotations, suppressPoseShapingWithManualReference);
            ConfigureDetachedHelpers(
                syncDetachedBaseHelpers, syncDetachedBaseHelperPositions, detachedBaseHelperSyncWeight,
                detachedBaseHelperMaxLocalAngle, detachedBaseHelperMaxPositionOffset,
                leftDetachedBaseHelperDeltaAxisOffset, rightDetachedBaseHelperDeltaAxisOffset,
                leftDetachedBaseHelperTargetRotationOffset, rightDetachedBaseHelperTargetRotationOffset,
                stabilizeDetachedBasePalm, detachedBasePalmStabilizeWeight, detachedBasePalmMaxLocalAngle,
                stabilizeWebbingCrease, webbingCreaseStabilizeWeight,
                webbingCreaseMaxLocalAngle, webbingCreaseMaxPositionOffset);
            ConfigureThumbGuards(
                enableVisualLengthGuard, projectionMinPalmNormal, projectionMaxPalmNormal,
                projectionGuardWeight, indexMaxSpreadAngle, indexSpreadGuardWeight,
                maxSegmentBendAngle, segmentStraightenWeight, suppressPoseShapingWithManualReference);
            RecaptureBaseline();
        }

        private void ConfigureBase(
            Animator animator,
            PoseSpaceRetargeter poseSpaceRetargeter,
            float proximalLimit,
            float intermediateLimit,
            float distalLimit,
            Vector3 proximalOffset,
            bool mirrorRightOffset,
            Vector3 leftProximalOffset,
            Vector3 rightProximalOffset,
            bool logCorrectionMessages,
            bool clampHumanoidRotations,
            bool suppressPoseShapingWithManualReference)
        {
            targetAnimator = animator;
            linkedPoseSpaceRetargeter = poseSpaceRetargeter;
            _hasSearchedMissingRetargeter = false;
            proximalMaxLocalAngle = Mathf.Clamp(
                suppressPoseShapingWithManualReference
                    ? Mathf.Max(proximalLimit, ManualThumbReferenceMinProximalMaxLocalAngle)
                    : proximalLimit,
                0f,
                90f);
            intermediateMaxLocalAngle = Mathf.Clamp(intermediateLimit, 0f, 120f);
            distalMaxLocalAngle = Mathf.Clamp(distalLimit, 0f, 120f);
            proximalLocalRotationOffset = proximalOffset;
            mirrorRightProximalLocalRotationOffset = mirrorRightOffset;
            leftProximalLocalRotationOffset = leftProximalOffset;
            rightProximalLocalRotationOffset = rightProximalOffset;
            logCorrections = logCorrectionMessages;
            clampHumanoidThumbRotations = clampHumanoidRotations;
        }

        private void ConfigureDetachedHelpers(
            bool syncDetachedBaseHelpers,
            bool syncDetachedBaseHelperPositions,
            float detachedBaseHelperSyncWeight,
            float detachedBaseHelperMaxLocalAngle,
            float detachedBaseHelperMaxPositionOffset,
            Vector3 leftDetachedBaseHelperDeltaAxisOffset,
            Vector3 rightDetachedBaseHelperDeltaAxisOffset,
            Vector3 leftDetachedBaseHelperTargetRotationOffset,
            Vector3 rightDetachedBaseHelperTargetRotationOffset,
            bool stabilizeDetachedBasePalm,
            float detachedBasePalmStabilizeWeight,
            float detachedBasePalmMaxLocalAngle,
            bool stabilizeWebbingCrease,
            float webbingCreaseStabilizeWeight,
            float webbingCreaseMaxLocalAngle,
            float webbingCreaseMaxPositionOffset)
        {
            syncDetachedThumbBaseHelpers = syncDetachedBaseHelpers;
            syncDetachedThumbBaseHelperPositions = syncDetachedBaseHelperPositions;
            detachedThumbBaseHelperSyncWeight = Mathf.Clamp01(detachedBaseHelperSyncWeight);
            detachedThumbBaseHelperMaxLocalAngle = Mathf.Clamp(detachedBaseHelperMaxLocalAngle, 0f, 45f);
            detachedThumbBaseHelperMaxPositionOffset = Mathf.Clamp(detachedBaseHelperMaxPositionOffset, 0f, 0.02f);
            leftDetachedThumbBaseHelperDeltaAxisOffset = leftDetachedBaseHelperDeltaAxisOffset;
            rightDetachedThumbBaseHelperDeltaAxisOffset = rightDetachedBaseHelperDeltaAxisOffset;
            leftDetachedThumbBaseHelperTargetRotationOffset = leftDetachedBaseHelperTargetRotationOffset;
            rightDetachedThumbBaseHelperTargetRotationOffset = rightDetachedBaseHelperTargetRotationOffset;
            stabilizeDetachedThumbBasePalm = stabilizeDetachedBasePalm;
            detachedThumbBasePalmStabilizeWeight = Mathf.Clamp01(detachedBasePalmStabilizeWeight);
            detachedThumbBasePalmMaxLocalAngle = Mathf.Clamp(detachedBasePalmMaxLocalAngle, 0f, 45f);
            stabilizeThumbWebbingCrease = stabilizeWebbingCrease;
            thumbWebbingCreaseStabilizeWeight = Mathf.Clamp01(webbingCreaseStabilizeWeight);
            thumbWebbingCreaseMaxLocalAngle = Mathf.Clamp(webbingCreaseMaxLocalAngle, 0f, 45f);
            thumbWebbingCreaseMaxPositionOffset = Mathf.Clamp(webbingCreaseMaxPositionOffset, 0f, 0.02f);
        }

        private void ConfigureThumbGuards(
            bool enableVisualLengthGuard,
            float projectionMinPalmNormal,
            float projectionMaxPalmNormal,
            float projectionGuardWeight,
            float indexMaxSpreadAngle,
            float indexSpreadGuardWeight,
            float maxSegmentBendAngle,
            float segmentStraightenWeight,
            bool suppressPoseShapingWithManualReference)
        {
            enableThumbVisualLengthGuard = enableVisualLengthGuard;
            thumbProjectionMinPalmNormal = Mathf.Clamp01(projectionMinPalmNormal);
            thumbProjectionMaxPalmNormal = Mathf.Clamp01(projectionMaxPalmNormal);
            thumbProjectionGuardWeight = Mathf.Clamp01(projectionGuardWeight);
            thumbIndexMaxSpreadAngle = Mathf.Clamp(indexMaxSpreadAngle, 0f, 90f);
            thumbIndexSpreadGuardWeight = Mathf.Clamp01(indexSpreadGuardWeight);
            thumbMaxSegmentBendAngle = Mathf.Clamp(maxSegmentBendAngle, 0f, 60f);
            thumbSegmentStraightenWeight = Mathf.Clamp01(segmentStraightenWeight);
            suppressPoseShapingWithManualThumbReference = suppressPoseShapingWithManualReference;
        }

    }
}
