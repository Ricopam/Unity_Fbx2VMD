using UnityEngine;

namespace Fbx2Vmd.FBXImporter.Editor
{
    /// <summary>
    /// 에디터 Humanoid 재생 세션 본체. [InitializeOnLoad] 시점에 FbxEditorFeatureWiring이
    /// ConversionEditorHooks.HumanoidPlaybackSession에 할당한다.
    /// </summary>
    internal sealed class EditorHumanoidPlaybackSessionDriver : IEditorHumanoidPlaybackSessionDriver
    {
        public FBXConversionResult RunSession(
            FBXVmdPipeline pipeline,
            FBXConversionRequest request,
            FBXImportController importController)
        {
            var editorImportController = new EditorHumanoidMotionImportController(
                pipeline,
                importController);
            EditorHumanoidMotionImportResult importResult =
                editorImportController.Prepare(request.SourcePath);
            if (!importResult.IsSuccess)
            {
                pipeline.FailSession(importResult.ErrorMessage);
                return FBXConversionResult.Fail(importResult.ErrorMessage);
            }

            if (!FBXConversionCoordinator.TryResolveTargetAnimator(
                    pipeline.targetCharacter,
                    out Animator targetAnimator,
                    out string targetErrorMessage))
            {
                pipeline.FailSession(targetErrorMessage);
                return FBXConversionResult.Fail(targetErrorMessage);
            }

            pipeline.PrepareEditorHumanoidPlayback(
                targetAnimator,
                importResult.Clip,
                importResult.OutputBaseName,
                importResult.SourceModelAsset);
            return FBXConversionResult.Succeed(importResult.OutputBaseName);
        }
    }
}
