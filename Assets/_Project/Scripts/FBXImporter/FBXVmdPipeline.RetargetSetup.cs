using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public partial class FBXVmdPipeline
    {
        internal void PrepareRetargeterForRecording(
            PoseSpaceRetargeter retargeter,
            GameObject targetObject,
            Animator targetAnimator,
            string importedFilePath,
            string sourceFilePath)
        {
            _activeRetargeter = retargeter;
            BindRetargetGuards(targetObject, retargeter);
            ApplyThumbGuardToRetargeter(
                targetObject,
                targetAnimator,
                retargeter);
            ApplyHumanoidReferenceHooks(
                retargeter,
                importedFilePath,
                sourceFilePath);
            SetSessionState(FBXSessionState.GhostReady, "Ghost Retarget 준비 완료", 0.6f);
        }

        internal void DispatchRecording(
            Animation ghostAnimation,
            PoseSpaceRetargeter retargeter,
            GameObject targetObject,
            AnimationClip targetClip,
            string outputBaseName)
        {
            if (ghostAnimation != null)
            {
                ghostAnimation.Stop();
            }

            StartCoroutine(_recordingController.RecordAsync(
                ghostAnimation,
                retargeter,
                targetObject,
                targetClip,
                outputBaseName));
        }

        internal static bool PrepareRetargeterRecordingStartPose(PoseSpaceRetargeter retargeter, float sampleTime, float playbackSpeed, bool holdPose)
        {
            return retargeter != null && retargeter.PrepareRecordingStartPose(sampleTime, playbackSpeed, holdPose);
        }

        internal static object YieldRetargetPrewarmFrame()
        {
#if UNITY_EDITOR
            if (Application.isBatchMode)
            {
                // WaitForEndOfFrame can stall in batchmode because no render loop is advancing.
                return null;
            }
#endif
            return new WaitForEndOfFrame();
        }

        private void ApplyThumbGuardToRetargeter(
            GameObject targetObject,
            Animator targetAnimator,
            PoseSpaceRetargeter retargeter)
        {
#if UNITY_EDITOR
            bool thumbGuardApplied =
#endif
                HumanoidThumbDeformationGuardApplier.Apply(
                    targetObject,
                    targetAnimator,
                    retargeter,
                    CreateThumbDeformationGuardOptions());
#if UNITY_EDITOR
            if (thumbGuardApplied && EditorDiagnosticSession.IsRecordingOverrideActive)
            {
                LogEditorSmokeThumbState("thumb-guard-bound");
            }
#endif
        }

        private void ApplyHumanoidReferenceHooks(
            PoseSpaceRetargeter retargeter,
            string importedFilePath,
            string sourceFilePath)
        {
            AnimationClip editorHumanoidReferenceClip = HumanoidReferenceEditorHooks.Apply?.Invoke(
                retargeter,
                importedFilePath,
                sourceFilePath,
                _shouldUseEditorHumanoidClipMuscleReference,
                _shouldUseEditorHumanoidRootTranslationReference);
            HumanoidReferenceEditorHooks.ApplyManualPoseReference?.Invoke(
                retargeter,
                editorHumanoidReferenceClip,
                CreateEditorManualPoseReferenceOptions());
        }

        private void BindRetargetGuards(
            GameObject targetObject,
            PoseSpaceRetargeter retargeter)
        {
            HumanoidArmDeformationGuard armDeformationGuard =
                targetObject != null
                    ? targetObject.GetComponent<HumanoidArmDeformationGuard>()
                    : null;
            armDeformationGuard?.BindRetargeter(retargeter);
            HumanoidArmSwingLimitGuard armSwingLimitGuard =
                targetObject != null
                    ? targetObject.GetComponent<HumanoidArmSwingLimitGuard>()
                    : null;
            armSwingLimitGuard?.BindRetargeter(retargeter);
        }

    }
}
