using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 우측 슬리브 실루엣 로컬 오프셋의 에디터 구현 계약.
    /// PSR에는 reflection 테스트가 강제하는 동명 private 래퍼가 남아 훅으로 위임한다.
    /// </summary>
    internal interface IEditorRightSleeveSilhouetteDriver
    {
        bool ShouldApplyRightSleeveSilhouetteLocalOffsetFrameGate(PoseSpaceRetargeter owner);

        void ApplyRightSleeveSilhouetteLocalOffsetReference(PoseSpaceRetargeter owner);

        void ApplyRightSleeveSilhouetteLocalOffsetToTransform(PoseSpaceRetargeter owner, Transform target, Vector3 offset);

        void RestoreRightSleeveSilhouetteLocalOffsetReference(PoseSpaceRetargeter owner);
    }

    /// <summary>풋높이 접지 참조 목표값 평가자의 에디터 구현 계약.</summary>
    internal interface IEditorFootHeightReferenceDriver
    {
        float ResolveFootHeightGroundingReferenceTarget(PoseSpaceRetargeter owner, float baseTargetHeight);
    }
}
