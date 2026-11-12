using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 에디터 레퍼런스 프레임 게이트·슬리브 오프셋을 담당하는 partial 분할.
    public partial class PoseSpaceRetargeter
    {
#if UNITY_EDITOR
        internal bool ShouldApplyPostSetHumanPoseRightEndpointPositionFrameGate()
        {
            return ShouldApplyManualReferenceFrameGate(
                postSetHumanPoseRightEndpointPositionReferenceFrameGateStart,
                postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd);
        }

        internal bool ShouldApplyPreSetHumanPoseRightEndpointPositionFrameGate()
        {
            return ShouldApplyManualReferenceFrameGate(
                preSetHumanPoseRightEndpointPositionReferenceFrameGateStart,
                preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd);
        }

        internal bool ShouldApplyManualReferenceFrameGate(float startFrame, float endFrame)
        {
            if (!ManualPoseReferenceApplier.HasActiveFrameGate(startFrame, endFrame))
            {
                return true;
            }

            float frameRate = Mathf.Clamp(legacyAnimationVisualFrameRate, 15f, 120f);
            int currentFrame = Mathf.RoundToInt(_legacyAnimationDriver.CurrentTime * frameRate);
            return ManualPoseReferenceApplier.IsFrameWithinGate(
                currentFrame,
                startFrame,
                endFrame);
        }

        internal float ResolveManualAnimatorBodyPositionXzFrameGateWeight()
        {
            float start = Mathf.Max(0f, manualAnimatorBodyPositionXzReferenceFrameGateStart);
            float end = Mathf.Max(0f, manualAnimatorBodyPositionXzReferenceFrameGateEnd);
            float frameRate = Mathf.Clamp(legacyAnimationVisualFrameRate, 15f, 120f);
            float currentFrame = Mathf.RoundToInt(_legacyAnimationDriver.CurrentTime * frameRate);
            return ManualPoseReferenceApplier.CalculateBodyPositionXzFrameGateWeight(
                currentFrame,
                start,
                end,
                Mathf.Max(0f, manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames));
        }

        internal static bool TryNormalize(Vector3 value, out Vector3 normalized)
        {
            normalized = Vector3.zero;
            if (!IsFinite(value) || value.sqrMagnitude <= 0.00000001f)
            {
                return false;
            }

            normalized = value.normalized;
            return IsFinite(normalized);
        }

        private void DisposeEditorHumanoidFingerPoseReference()
        {
            RetargetingEditorReferenceHooks.PoseReference?.DisposeFingerPoseReference(this);
        }

        private bool ShouldApplyRightSleeveSilhouetteLocalOffsetFrameGate()
        {
            return RetargetingEditorReferenceHooks.SleeveReference?.ShouldApplyRightSleeveSilhouetteLocalOffsetFrameGate(this) ?? false;
        }

        internal bool ShouldApplySingleFrameFallbackReferenceFrameGate(float startFrame, float endFrame)
        {
            if (!ManualPoseReferenceApplier.HasConfiguredFrameGate(startFrame, endFrame))
            {
                return true;
            }

            float frameRate = Mathf.Clamp(legacyAnimationVisualFrameRate, 1f, 240f);
            int currentFrame = Mathf.RoundToInt(_legacyAnimationDriver.CurrentTime * frameRate);
            return ManualPoseReferenceApplier.IsFrameWithinSingleFrameFallbackGate(
                currentFrame,
                startFrame,
                endFrame);
        }

        private void ApplyRightSleeveSilhouetteLocalOffsetReference()
        {
            RetargetingEditorReferenceHooks.SleeveReference?.ApplyRightSleeveSilhouetteLocalOffsetReference(this);
        }

        private void ApplyRightSleeveSilhouetteLocalOffsetToTransform(Transform target, Vector3 offset)
        {
            RetargetingEditorReferenceHooks.SleeveReference?.ApplyRightSleeveSilhouetteLocalOffsetToTransform(this, target, offset);
        }

        private void RestoreRightSleeveSilhouetteLocalOffsetReference()
        {
            RetargetingEditorReferenceHooks.SleeveReference?.RestoreRightSleeveSilhouetteLocalOffsetReference(this);
        }

#endif
    }
}
