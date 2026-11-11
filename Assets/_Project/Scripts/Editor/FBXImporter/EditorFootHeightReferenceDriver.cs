using Fbx2Vmd.Retargeting;

namespace Fbx2Vmd.FBXImporter.Editor
{
    /// <summary>
    /// 풋높이 접지 참조 목표값 평가자의 에디터 구현.
    /// 평가 상태(rest Y·허용 플래그·lift 진단)는 오너의 공유 필드에 둔다.
    /// </summary>
    internal sealed class EditorFootHeightReferenceDriver : IEditorFootHeightReferenceDriver
    {
        public float ResolveFootHeightGroundingReferenceTarget(PoseSpaceRetargeter owner, float baseTargetHeight)
        {
            owner._lastEditorFootHeightGroundingReferenceLift = 0f;
            if (!owner.ShouldUseManualAnimatorFootHeightGroundingReference ||
                !owner._allowEditorFootHeightGroundingReference ||
                owner.manualAnimatorFootHeightGroundingReferenceWeight <= 0f ||
                owner._editorFingerReferenceAnimator == null)
            {
                return baseTargetHeight;
            }

            bool animatorUpdated =
                RetargetingEditorReferenceHooks.PoseReference?.UpdateManualReferenceAnimator(owner) ?? false;
            if (!animatorUpdated ||
                !RetargetingAnimatorPoseReader.TryGetAnimatorLowestFootY(owner._editorFingerReferenceAnimator, out float referenceCurrentLowestFootY))
            {
                return baseTargetHeight;
            }

            if (!owner._hasEditorReferenceLowestFootRestY)
            {
                owner._editorReferenceLowestFootRestY = referenceCurrentLowestFootY;
                owner._hasEditorReferenceLowestFootRestY = true;
                return baseTargetHeight;
            }

            if (GroundingStabilizer.TryCalculateFootHeightReferenceTarget(
                    baseTargetHeight,
                    referenceCurrentLowestFootY,
                    owner._editorReferenceLowestFootRestY,
                    owner.manualAnimatorFootHeightGroundingReferenceWeight,
                    owner.manualAnimatorFootHeightGroundingReferenceMaxLift,
                    out float targetHeight))
            {
                owner._lastEditorFootHeightGroundingReferenceLift = targetHeight - baseTargetHeight;
                return targetHeight;
            }

            owner._lastEditorFootHeightGroundingReferenceLift = float.NaN;
            return baseTargetHeight;
        }
    }
}
