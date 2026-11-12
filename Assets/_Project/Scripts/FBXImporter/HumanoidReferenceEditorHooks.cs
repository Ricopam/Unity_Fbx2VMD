using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 에디터 전용 Humanoid 참조 적용이 런타임으로부터 호출받는 경계 계약.
    /// 런타임 어셈블리는 에디터 구현을 참조할 수 없으므로, 런타임이 계약을 선언하고
    /// 에디터가 [InitializeOnLoad] 시점에 델리게이트를 할당한다 (RetargetingEditorReferenceHooks 패턴).
    /// 스탠드얼론에서는 슬롯이 null이며 호출부는 기존 #if 제거와 동일한 무동작/기본값을 반환한다.
    /// </summary>
    internal delegate AnimationClip EditorHumanoidReferenceApplyDelegate(
        PoseSpaceRetargeter retargeter,
        string importedFilePath,
        string sourceFilePath,
        bool shouldUseClipMuscleReference,
        bool shouldUseRootTranslationReference);

    internal delegate void EditorManualPoseReferenceApplyDelegate(
        PoseSpaceRetargeter retargeter,
        AnimationClip referenceClip,
        EditorManualPoseReferenceOptions options);

    internal static class HumanoidReferenceEditorHooks
    {
        internal static EditorHumanoidReferenceApplyDelegate Apply;
        internal static EditorManualPoseReferenceApplyDelegate ApplyManualPoseReference;
    }
}
