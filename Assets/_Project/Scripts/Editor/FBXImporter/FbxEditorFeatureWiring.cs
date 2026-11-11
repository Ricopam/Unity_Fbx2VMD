using UnityEditor;

namespace Fbx2Vmd.FBXImporter
{
    // 에디터 도메인의 구현을 런타임 파이프라인 훅에 연결한다.
    [InitializeOnLoad]
    internal static class FbxEditorFeatureWiring
    {
        static FbxEditorFeatureWiring()
        {
            FBXVmdPipeline.EditorMotionVideoRecorderFactory = () => new EditorMotionVideoRecorder();
            RetargetingEditorReferenceHooks.PoseReference = new Editor.EditorPoseReferenceDriver();
            RetargetingEditorReferenceHooks.LowerBodyDirection = new Editor.EditorLowerBodyDirectionDriver();
            RetargetingEditorReferenceHooks.FootReference = new Editor.EditorFootReferenceDriver();
            RetargetingEditorReferenceHooks.ThumbReference = new Editor.EditorThumbReferenceDriver();
            RetargetingEditorReferenceHooks.SleeveReference = new Editor.EditorRightSleeveSilhouetteDriver();
            RetargetingEditorReferenceHooks.FootHeightReference = new Editor.EditorFootHeightReferenceDriver();
        }
    }
}
