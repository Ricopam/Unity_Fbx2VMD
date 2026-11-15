using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Fbx2Vmd.Character;
using RootMotion;
using RootMotion.FinalIK;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// FBX에서 VMD로 변환하는 흐름을 조정하기 위해 추출함.
    /// ConvertAsync는 요청을 검증하고 RunSessionAsync는 변환 use case 순서를 소유함.
    /// B-6d 리팩터링으로 분리함.
    /// </summary>
    public partial class FBXConversionCoordinator
    {
        private readonly FBXVmdPipeline _pipeline;
        private readonly FBXImportController _importController;
        private readonly List<BooleanFieldSnapshot> _retargetBooleanSnapshots =
            new List<BooleanFieldSnapshot>();

        private struct BooleanFieldSnapshot
        {
            public object Target;
            public FieldInfo Field;
            public bool Value;

            public BooleanFieldSnapshot(object target, FieldInfo field, bool value)
            {
                Target = target;
                Field = field;
                Value = value;
            }
        }

        public FBXConversionCoordinator(FBXVmdPipeline pipeline)
            : this(pipeline, null)
        {
        }

        internal FBXConversionCoordinator(
            FBXVmdPipeline pipeline,
            FBXImportController importController)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _importController = importController;
        }

        public async Task<FBXConversionResult> ConvertAsync(FBXConversionRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.SourcePath))
                return FBXConversionResult.Fail("FBX 변환 요청이 비어 있습니다.");

            if (_pipeline.IsProcessing)
                return FBXConversionResult.Fail("이미 FBX 처리 중입니다.");

            try
            {
                return await RunSessionAsync(request);
            }
            catch (Exception e)
            {
                return FBXConversionResult.Fail(e.Message);
            }
        }

        private FBXConversionResult RunEditorHumanoidPlaybackSession(
            FBXConversionRequest request,
            FBXImportController importController)
        {
            return ConversionEditorHooks.HumanoidPlaybackSession?.RunSession(
                _pipeline,
                request,
                importController)
                ?? FBXConversionResult.Fail("에디터 재생 세션 드라이버가 연결되지 않았습니다.");
        }

        /// <summary>
        /// import부터 녹화 시작까지의 변환 use case 순서를 조정함.
        /// </summary>
        public async Task<FBXConversionResult> RunSessionAsync(FBXConversionRequest request)
        {
            _pipeline.EnsureServicesInitialized();
            FBXConversionCoordinator registeredCoordinator = _pipeline.ConversionCoordinator;
            if (registeredCoordinator != null && !ReferenceEquals(registeredCoordinator, this))
            {
                return await registeredCoordinator.RunSessionAsync(request);
            }

            _pipeline.BeginConversionSession(System.IO.Path.GetFileName(request.SourcePath));
            FBXImportController importController = _importController ?? _pipeline.ImportController;

            try
            {
                if (_pipeline.ShouldUseEditorHumanoidPlaybackSession)
                {
                    return RunEditorHumanoidPlaybackSession(request, importController);
                }

                return await RunImportAndRetargetAsync(request, importController);
            }
            catch (Exception e)
            {
                string errorMessage = $"FBX 처리 실패: {e.Message}";
                _pipeline.FailSession(errorMessage, e);
                return FBXConversionResult.Fail(errorMessage);
            }
        }

        async Task<FBXConversionResult> RunImportAndRetargetAsync(
            FBXConversionRequest request, FBXImportController importController)
        {
            FBXModelImportResult importResult = await importController.ImportRuntimeModelAsync(
                request.SourcePath,
                _pipeline.ShouldRecordVmdAfterImport);
            if (!importResult.IsSuccess)
            {
                return FailWithSession(importResult.ErrorMessage);
            }

            return RunRetargetSession(
                request, importController, importResult.ImportedModel,
                importResult.ControlledImportPath, importResult.OutputBaseName);
        }

        private FBXConversionResult RunRetargetSession(
            FBXConversionRequest request,
            FBXImportController importController,
            GameObject importedModel,
            string targetPath,
            string outputBaseName)
        {
            GameObject targetObject = _pipeline.targetCharacter;
            if (!TryPrepareAnimationAndTarget(
                    importController,
                    importedModel,
                    targetObject,
                    out Dictionary<string, string> boneMapping,
                    out Animation ghostAnimation,
                    out AnimationClip targetClip,
                    out Animator targetAnimator,
                    out string errorMessage))
            {
                return FailWithSession(errorMessage);
            }

            if (TrySucceedRuntimePlayback(
                    targetAnimator, targetClip, outputBaseName,
                    importedModel, request.SourcePath))
            {
                return FBXConversionResult.Succeed(outputBaseName);
            }

            RunRetargetAndDispatch(
                request, importedModel, targetObject, targetAnimator,
                boneMapping, ghostAnimation, targetClip, targetPath,
                outputBaseName);
            return FBXConversionResult.Succeed(outputBaseName);
        }

        private bool TrySucceedRuntimePlayback(
            Animator targetAnimator,
            AnimationClip targetClip,
            string outputBaseName,
            GameObject importedModel,
            string sourcePath)
        {
#if !UNITY_EDITOR
            if (!_pipeline.ShouldUseRuntimeHumanoidPlaybackSession)
            {
                return false;
            }
            _pipeline.PrepareRuntimeHumanoidPlayback(
                targetAnimator,
                targetClip,
                outputBaseName,
                importedModel,
                sourcePath);
            return true;
#else
            return false;
#endif
        }

        private FBXConversionResult FailWithSession(string errorMessage)
        {
            _pipeline.FailSession(errorMessage);
            return FBXConversionResult.Fail(errorMessage);
        }

        private bool TryPrepareAnimationAndTarget(
            FBXImportController importController,
            GameObject importedModel,
            GameObject targetObject,
            out Dictionary<string, string> boneMapping,
            out Animation ghostAnimation,
            out AnimationClip targetClip,
            out Animator targetAnimator,
            out string errorMessage)
        {
            if (!TryPrepareGhostAnimation(
                    importController,
                    importedModel,
                    out boneMapping,
                    out ghostAnimation,
                    out targetClip,
                    out errorMessage))
            {
                targetAnimator = null;
                return false;
            }
            return TryResolveTargetAnimator(
                targetObject, out targetAnimator, out errorMessage);
        }

        private void ConfigureRetargeterForSession(
            PoseSpaceRetargeter retargeter,
            GameObject importedModel,
            GameObject targetObject,
            Animator targetAnimator,
            string targetPath,
            string sourcePath,
            AnimationClip targetClip)
        {
            _pipeline.PrepareRetargeterForRecording(
                retargeter,
                targetObject,
                targetAnimator,
                targetPath,
                sourcePath);
#if !UNITY_EDITOR
            // 빌드에서는 에디터 기준 클립이 없어 임포트된 모델 자체를 포즈 참조로 사용함.
            retargeter.ConfigureRuntimeHumanoidPoseReference(importedModel, targetClip);
#endif
        }

        bool TryPrepareGhostAnimation(
            FBXImportController importController,
            GameObject importedModel,
            out Dictionary<string, string> boneMapping,
            out Animation ghostAnimation,
            out AnimationClip targetClip,
            out string errorMessage)
        {
            _pipeline.PrepareGhostModel(importedModel);
            return importController.TryPrepareRuntimeAnimation(
                importedModel,
                _pipeline.showRuntimeAnimationLog,
                out boneMapping,
                out ghostAnimation,
                out targetClip,
                out errorMessage);
        }

        void RunRetargetAndDispatch(
            FBXConversionRequest request,
            GameObject importedModel,
            GameObject targetObject,
            Animator targetAnimator,
            Dictionary<string, string> boneMapping,
            Animation ghostAnimation,
            AnimationClip targetClip,
            string targetPath,
            string outputBaseName)
        {
            PrepareRetargetingTarget(
                targetObject,
                targetAnimator,
                importedModel.GetComponent<Animator>(),
                _pipeline.ShouldFaceTargetToCameraOnIdle,
                _pipeline.disableMmdShoulderPostPoseDuringRetarget,
                _pipeline.RestoreIdlePoseBeforeRetargetBaselines);

            PoseSpaceRetargeter retargeter = CreateRetargeter(
                importedModel,
                targetObject,
                boneMapping,
                targetClip);
            ConfigureRetargeterForSession(
                retargeter, importedModel, targetObject, targetAnimator,
                targetPath, request.SourcePath, targetClip);

            _pipeline.DispatchRecording(
                ghostAnimation,
                retargeter,
                targetObject,
                targetClip,
                outputBaseName);
        }
    }
}
