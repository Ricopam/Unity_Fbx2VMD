using System;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 에디터/런타임 Humanoid 포즈 레퍼런스 구성·적용을 담당하는 partial 분할.
    public partial class PoseSpaceRetargeter
    {
#if UNITY_EDITOR
        public void ConfigureEditorHumanoidMuscleReference(
            AnimationClip referenceClip,
            bool shouldUseCompletePoseReference = false)
        {
            RetargetingEditorReferenceHooks.PoseReference?.ConfigureMuscleReference(this, referenceClip, shouldUseCompletePoseReference);
        }

        public void ConfigureEditorHumanoidRootTranslationReference(AnimationClip referenceClip)
        {
            RetargetingEditorReferenceHooks.PoseReference?.ConfigureRootTranslationReference(this, referenceClip);
        }

        public void ConfigureEditorHumanoidFingerPoseReference(
            GameObject referencePrefab,
            RuntimeAnimatorController referenceController,
            AnimationClip referenceClip,
            bool enableFingerPoseReference = true,
            bool enableFullBodyPoseReference = true,
            float fullBodyPoseReferenceWeight = 1f,
            bool fullBodyPoseExcludeLowerBodyMuscles = false,
            bool fullBodyPoseLowerBodyMusclesOnly = false,
            bool fullBodyPoseLegTwistMusclesOnly = false,
            bool fullBodyPoseRightArmMusclesOnly = false,
            bool fullBodyPoseLeftArmMusclesOnly = false,
            bool fullBodyPoseRightSleeveChainMusclesOnly = false,
            float fullBodyPoseFrameGateStart = 0f,
            float fullBodyPoseFrameGateEnd = 0f)
        {
            EditorFingerPoseReferenceOptions options = new EditorFingerPoseReferenceOptions
            {
                EnableFingerPoseReference = enableFingerPoseReference,
                EnableFullBodyPoseReference = enableFullBodyPoseReference,
                FullBodyPoseReferenceWeight = fullBodyPoseReferenceWeight,
                FullBodyPoseExcludeLowerBodyMuscles = fullBodyPoseExcludeLowerBodyMuscles,
                FullBodyPoseLowerBodyMusclesOnly = fullBodyPoseLowerBodyMusclesOnly,
                FullBodyPoseLegTwistMusclesOnly = fullBodyPoseLegTwistMusclesOnly,
                FullBodyPoseRightArmMusclesOnly = fullBodyPoseRightArmMusclesOnly,
                FullBodyPoseLeftArmMusclesOnly = fullBodyPoseLeftArmMusclesOnly,
                FullBodyPoseRightSleeveChainMusclesOnly = fullBodyPoseRightSleeveChainMusclesOnly,
                FullBodyPoseFrameGateStart = fullBodyPoseFrameGateStart,
                FullBodyPoseFrameGateEnd = fullBodyPoseFrameGateEnd,
            };
            RetargetingEditorReferenceHooks.PoseReference?.ConfigureFingerPoseReference(
                this,
                referencePrefab,
                referenceController,
                referenceClip,
                options);
        }

#endif

        // 빌드에서는 에디터 muscle 클립이 없어 임포트된 소스 모델을 HumanPoseHandler로 샘플링해 참조로 사용함.
        internal void ConfigureRuntimeHumanoidPoseReference(
            GameObject referenceSourceModel,
            AnimationClip referenceClip)
        {
            _editorHumanoidPoseReferencePlayer?.Dispose();
            _editorHumanoidPoseReferencePlayer = null;
            _useCompleteEditorHumanoidMuscleReference = false;
            _hasEditorHumanoidPoseReferenceForFrame = false;
            _hasEditorReferenceBodyPosition = false;

            if (referenceSourceModel == null || referenceClip == null)
            {
                return;
            }

            try
            {
                _editorHumanoidPoseReferencePlayer = new HumanoidPoseReferencePlayer();
                _editorHumanoidPoseReferencePlayer.InitializeFromSourceModel(
                    referenceSourceModel,
                    referenceClip);
                _useCompleteEditorHumanoidMuscleReference =
                    _editorHumanoidPoseReferencePlayer.IsInitialized;
            }
            catch (Exception exception)
            {
                _editorHumanoidPoseReferencePlayer?.Dispose();
                _editorHumanoidPoseReferencePlayer = null;
                Debug.LogWarning(
                    $"[PoseSpaceRetargeter] 런타임 Humanoid 포즈 참조를 준비하지 못했습니다: {exception.Message}");
            }
        }


        private void ApplyEditorHumanoidMuscleReference(ref HumanPose pose)
        {
#if UNITY_EDITOR
            ApplyEditorHumanoidMuscleReferenceEditor(ref pose);
#else
            ApplyRuntimeHumanoidPoseReference(ref pose);
#endif
        }

#if !UNITY_EDITOR
        private void ApplyRuntimeHumanoidPoseReference(ref HumanPose pose)
        {
            _hasEditorHumanoidPoseReferenceForFrame = false;
            if (pose.muscles == null ||
                !_useCompleteEditorHumanoidMuscleReference ||
                _editorHumanoidPoseReferencePlayer == null ||
                !_editorHumanoidPoseReferencePlayer.TryEvaluateAt(
                    _legacyAnimationDriver.CurrentTime,
                    ref _editorHumanoidPoseReference))
            {
                return;
            }

            int muscleCount = Mathf.Min(
                pose.muscles.Length,
                _editorHumanoidPoseReference.muscles.Length);
            Array.Copy(
                _editorHumanoidPoseReference.muscles,
                pose.muscles,
                muscleCount);
            pose.bodyRotation = _editorHumanoidPoseReference.bodyRotation;
            _editorReferenceBodyPosition = _editorHumanoidPoseReference.bodyPosition;
            _hasEditorReferenceBodyPosition = IsFinite(_editorReferenceBodyPosition);
            _hasEditorHumanoidPoseReferenceForFrame = true;
        }
#endif

        private void ApplyEditorHumanoidFingerPoseReference(ref HumanPose pose)
        {
#if UNITY_EDITOR
            ApplyEditorHumanoidFingerPoseReferenceEditor(ref pose);
#endif
        }

        private void ApplyEditorHumanoidBodyRotationReference(ref HumanPose pose)
        {
#if UNITY_EDITOR
            ApplyEditorHumanoidBodyRotationReferenceEditor(ref pose);
#endif
        }

        private void ResetLastEditorHipsLocalReferenceDiagnostics()
        {
            _footReferenceDiagnostics.ResetHipsLocal();
        }



#if UNITY_EDITOR
        private void ApplyEditorHumanoidMuscleReferenceEditor(ref HumanPose pose)
        {
            RetargetingEditorReferenceHooks.PoseReference?.ApplyMuscleReference(this, ref pose);
        }

        private void ApplyEditorHumanoidFingerPoseReferenceEditor(ref HumanPose pose)
        {
            RetargetingEditorReferenceHooks.PoseReference?.ApplyFingerPoseReference(this, ref pose);
        }

        private void ApplyEditorHumanoidBodyRotationReferenceEditor(ref HumanPose pose)
        {
            RetargetingEditorReferenceHooks.PoseReference?.ApplyBodyRotationReference(this, ref pose);
        }
#endif
    }
}
