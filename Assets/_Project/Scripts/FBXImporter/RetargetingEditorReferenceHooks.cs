using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 에디터 전용 포즈 참조 기능이 런타임으로부터 호출받는 경계 계약.
    /// 런타임 어셈블리는 에디터 구현을 참조할 수 없으므로, 런타임이 계약을 선언하고
    /// 에디터가 [InitializeOnLoad] 시점에 구현을 할당한다 (FBXVmdPipeline 훅 패턴).
    /// 스탠드얼론에서는 슬롯이 null이며 호출부는 기존 #if 제거와 동일한 무동작/기본값을 반환한다.
    /// </summary>
    internal interface IEditorPoseReferenceDriver
    {
        void ConfigureMuscleReference(
            PoseSpaceRetargeter owner,
            AnimationClip referenceClip,
            bool shouldUseCompletePoseReference);

        void ConfigureRootTranslationReference(
            PoseSpaceRetargeter owner,
            AnimationClip referenceClip);

        void ConfigureFingerPoseReference(
            PoseSpaceRetargeter owner,
            GameObject referencePrefab,
            RuntimeAnimatorController referenceController,
            AnimationClip referenceClip,
            EditorFingerPoseReferenceOptions options);

        void ApplyMuscleReference(PoseSpaceRetargeter owner, ref HumanPose pose);

        void ApplyFingerPoseReference(PoseSpaceRetargeter owner, ref HumanPose pose);

        void ApplyBodyRotationReference(PoseSpaceRetargeter owner, ref HumanPose pose);

        void DisposeFingerPoseReference(PoseSpaceRetargeter owner);

        void ResetRootTranslationState(PoseSpaceRetargeter owner);

        Vector3 ExtractRootTranslationDelta(PoseSpaceRetargeter owner, Vector3 ghostDelta);

        bool UpdateManualReferenceAnimator(PoseSpaceRetargeter owner);
    }

    /// <summary>손가락/전신 참조 구성의 선택 인자 묶음 — 런타임이 선언해 에디터 구현에 전달한다.</summary>
    internal struct EditorFingerPoseReferenceOptions
    {
        internal bool EnableFingerPoseReference;
        internal bool EnableFullBodyPoseReference;
        internal float FullBodyPoseReferenceWeight;
        internal bool FullBodyPoseExcludeLowerBodyMuscles;
        internal bool FullBodyPoseLowerBodyMusclesOnly;
        internal bool FullBodyPoseLegTwistMusclesOnly;
        internal bool FullBodyPoseRightArmMusclesOnly;
        internal bool FullBodyPoseLeftArmMusclesOnly;
        internal bool FullBodyPoseRightSleeveChainMusclesOnly;
        internal float FullBodyPoseFrameGateStart;
        internal float FullBodyPoseFrameGateEnd;
    }

    /// <summary>하체 세그먼트 방향 보정의 에디터 구현 계약 — 진입점은 Apply 하나다.</summary>
    internal interface IEditorLowerBodyDirectionDriver
    {
        void Apply(PoseSpaceRetargeter owner);
    }

    /// <summary>
    /// 발 참조군(hips local·손/발 로컬회전·FootHips yaw·Pre/PostSet 엔드포인트·BipedIK)의
    /// 에디터 구현 계약 — 적용 파이프라인의 각 호출 지점이 개별 진입점이다.
    /// </summary>
    internal interface IEditorFootReferenceDriver
    {
        void ApplyHipsLocalPositionReference(PoseSpaceRetargeter owner);

        void ApplyHandLocalRotationReference(PoseSpaceRetargeter owner);

        void ApplyFootLocalRotationReference(PoseSpaceRetargeter owner);

        void ApplyFootHipsAlignedResidualYawReference(PoseSpaceRetargeter owner);

        void ApplyPreSetEndpointPositionReference(PoseSpaceRetargeter owner);

        void ApplyPreSetSignCorrectedRowLocalBodyPositionReference(PoseSpaceRetargeter owner, ref HumanPose pose);

        void ApplyPostSetEndpointPositionReference(PoseSpaceRetargeter owner);

        void ApplyBipedIkFootPositionReference(PoseSpaceRetargeter owner);
    }

    internal static class RetargetingEditorReferenceHooks
    {
        internal static IEditorPoseReferenceDriver PoseReference;
        internal static IEditorLowerBodyDirectionDriver LowerBodyDirection;
        internal static IEditorFootReferenceDriver FootReference;
    }
}
