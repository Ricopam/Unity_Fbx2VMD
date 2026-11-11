using System;
using System.Collections.Generic;
using UnityEngine;
using Fbx2Vmd.Retargeting;

namespace Fbx2Vmd.FBXImporter.Editor
{
    /// <summary>
    /// PoseSpaceRetargeter의 에디터 전용 포즈 참조 구현.
    /// [InitializeOnLoad] 시점에 FbxEditorFeatureWiring이 RetargetingEditorReferenceHooks.PoseReference에 할당한다.
    /// 인스턴스 상태는 소유자(PoseSpaceRetargeter) 단위 세션으로 격리하고, 런타임 공유 상태는 owner 경유로만 접근한다.
    /// </summary>
    internal sealed class EditorPoseReferenceDriver : IEditorPoseReferenceDriver
    {
        private sealed class Session
        {
            internal AnimationCurve RootTranslationX;
            internal AnimationCurve RootTranslationZ;
            internal bool UseRootTranslationReference;
            internal Vector3 PreviousRootTranslation;
            internal Vector3 SmoothedRootTranslationDelta;
            internal bool HasPreviousRootTranslation;
            internal bool HasSmoothedRootTranslationDelta;
            internal readonly List<int> FingerReferenceMuscleIndices = new List<int>();
            internal GameObject FingerReferenceInstance;
            internal HumanPoseHandler FingerReferenceHandler;
            internal HumanPose FingerReferencePose;
            internal int FingerReferenceStateHash;
            internal float FingerReferenceClipLength;
        }

        private static Session GetSession(PoseSpaceRetargeter owner)
        {
            Session session = owner._editorPoseReferenceSession as Session;
            if (session == null)
            {
                session = new Session();
                owner._editorPoseReferenceSession = session;
            }
            return session;
        }

        public void ConfigureMuscleReference(
            PoseSpaceRetargeter owner,
            AnimationClip referenceClip,
            bool shouldUseCompletePoseReference)
        {
            owner._editorHumanoidMuscleCurves.Clear();
            owner._useEditorHumanoidMuscleReference = false;
            owner._useCompleteEditorHumanoidMuscleReference = false;
            owner._editorHumanoidMuscleReferenceLogged = false;
            owner._hasEditorHumanoidPoseReferenceForFrame = false;
            owner._editorHumanoidPoseReferencePlayer?.Dispose();
            owner._editorHumanoidPoseReferencePlayer = null;

            if (referenceClip == null || !referenceClip.humanMotion)
            {
                return;
            }

            UnityEditor.EditorCurveBinding[] bindings = UnityEditor.AnimationUtility.GetCurveBindings(referenceClip);
            for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                UnityEditor.EditorCurveBinding binding = bindings[bindingIndex];
                if (binding.type != typeof(Animator) || !string.IsNullOrEmpty(binding.path))
                {
                    continue;
                }

                int muscleIndex = RetargetingMuscleReferencePolicy.FindHumanMuscleIndex(binding.propertyName);
                if (muscleIndex < 0)
                {
                    continue;
                }

                AnimationCurve curve = UnityEditor.AnimationUtility.GetEditorCurve(referenceClip, binding);
                if (curve != null)
                {
                    owner._editorHumanoidMuscleCurves[muscleIndex] = curve;
                }
            }

            owner._useEditorHumanoidMuscleReference = owner._editorHumanoidMuscleCurves.Count > 0;
            owner._useCompleteEditorHumanoidMuscleReference =
                shouldUseCompletePoseReference &&
                owner._editorHumanoidMuscleCurves.Count == HumanTrait.MuscleCount;
            if (owner._useCompleteEditorHumanoidMuscleReference && owner.targetAnimator != null)
            {
                try
                {
                    owner._editorHumanoidPoseReferencePlayer = new HumanoidPoseReferencePlayer();
                    owner._editorHumanoidPoseReferencePlayer.Initialize(owner.targetAnimator, referenceClip);
                }
                catch (Exception exception)
                {
                    owner._editorHumanoidPoseReferencePlayer?.Dispose();
                    owner._editorHumanoidPoseReferencePlayer = null;
                    Debug.LogWarning(
                        $"[PoseSpaceRetargeter] Native Humanoid pose reference를 준비하지 못해 muscle curve 기준을 사용합니다: {exception.Message}");
                }
            }
            Debug.Log(
                $"[PoseSpaceRetargeter] Editor Humanoid muscle reference curves: " +
                $"{owner._editorHumanoidMuscleCurves.Count} from {referenceClip.name}, " +
                $"complete={owner._useCompleteEditorHumanoidMuscleReference}");
        }

        public void ConfigureRootTranslationReference(PoseSpaceRetargeter owner, AnimationClip referenceClip)
        {
            Session s = GetSession(owner);
            s.RootTranslationX = null;
            s.RootTranslationZ = null;
            s.UseRootTranslationReference = false;
            owner._editorRootTranslationReferenceLogged = false;
            ResetRootTranslationState(owner);

            if (referenceClip == null || !referenceClip.humanMotion)
            {
                return;
            }

            UnityEditor.EditorCurveBinding[] bindings = UnityEditor.AnimationUtility.GetCurveBindings(referenceClip);
            for (int bindingIndex = 0; bindingIndex < bindings.Length; bindingIndex++)
            {
                UnityEditor.EditorCurveBinding binding = bindings[bindingIndex];
                if (binding.type != typeof(Animator) || !string.IsNullOrEmpty(binding.path))
                {
                    continue;
                }

                if (binding.propertyName == "RootT.x")
                {
                    s.RootTranslationX = UnityEditor.AnimationUtility.GetEditorCurve(referenceClip, binding);
                }
                else if (binding.propertyName == "RootT.z")
                {
                    s.RootTranslationZ = UnityEditor.AnimationUtility.GetEditorCurve(referenceClip, binding);
                }
            }

            s.UseRootTranslationReference = s.RootTranslationX != null && s.RootTranslationZ != null;
            if (s.UseRootTranslationReference)
            {
                Debug.Log($"[PoseSpaceRetargeter] Editor Humanoid RootT translation reference ready from {referenceClip.name}");
            }
        }

        public void ConfigureFingerPoseReference(
            PoseSpaceRetargeter owner,
            GameObject referencePrefab,
            RuntimeAnimatorController referenceController,
            AnimationClip referenceClip,
            EditorFingerPoseReferenceOptions options)
        {
            Session s = GetSession(owner);
            DisposeFingerPoseReference(owner);
            owner._useEditorFingerPoseReference = false;
            owner._editorFingerPoseReferenceLogged = false;
            owner._editorBodyRotationReferenceLogged = false;
            owner._hasEditorReferenceBodyPosition = false;
            owner._hasEditorReferenceHipsRestLocalPosition = false;
            owner._hasEditorReferenceLowestFootRestY = false;
            owner._allowEditorFootHeightGroundingReference = false;
            owner._editorHandLocalRotationReferenceLogged = false;
            owner._editorFootLocalRotationReferenceLogged = false;
            owner._editorLowerBodySegmentDirectionReferenceLogged = false;
            owner._editorFootHipsAlignedResidualYawReferenceLogged = false;
            owner._editorThumbLocalRotationReferenceLogged = false;
            owner._editorThumbSegmentDirectionReferenceLogged = false;
            owner._editorHandPalmFrameReferenceLogged = false;
            owner._editorThumbBasePositionReferenceLogged = false;
            owner._editorHipsLocalPositionReferenceLogged = false;
            owner._editorBodyPositionXzReferenceLogged = false;
            s.FingerReferenceMuscleIndices.Clear();

            if (referencePrefab == null || referenceController == null || referenceClip == null || !referenceClip.humanMotion)
            {
                return;
            }

            string stateName = ResolveFirstAnimatorStateName(referenceController);
            if (string.IsNullOrEmpty(stateName))
            {
                stateName = referenceClip.name;
            }

            s.FingerReferenceStateHash = Animator.StringToHash(stateName);
            s.FingerReferenceClipLength = Mathf.Max(0.0001f, referenceClip.length);
            s.FingerReferenceInstance = UnityEngine.Object.Instantiate(referencePrefab);
            s.FingerReferenceInstance.name = $"EditorFingerReference_{referencePrefab.name}";
            s.FingerReferenceInstance.hideFlags = HideFlags.HideAndDontSave;
            s.FingerReferenceInstance.transform.SetParent(owner.transform, false);
            DisableReferenceRecordingComponents(s.FingerReferenceInstance);
            s.FingerReferenceInstance.SetActive(true);

            foreach (Renderer renderer in s.FingerReferenceInstance.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
            }

            owner._editorFingerReferenceAnimator = s.FingerReferenceInstance.GetComponentInChildren<Animator>(true);
            if (owner._editorFingerReferenceAnimator == null ||
                owner._editorFingerReferenceAnimator.avatar == null ||
                !owner._editorFingerReferenceAnimator.avatar.isValid ||
                !owner._editorFingerReferenceAnimator.avatar.isHuman)
            {
                Debug.LogWarning("[PoseSpaceRetargeter] 수동 기준 손가락 Reference에 유효한 Humanoid Animator가 없습니다.");
                DisposeFingerPoseReference(owner);
                return;
            }

            AnimatorOverrideController overrideController = new AnimatorOverrideController(referenceController);
            List<KeyValuePair<AnimationClip, AnimationClip>> overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            overrideController.GetOverrides(overrides);
            if (overrides.Count > 0 && overrides[0].Key != null)
            {
                overrideController[overrides[0].Key] = referenceClip;
            }

            owner._editorFingerReferenceAnimator.runtimeAnimatorController = overrideController;
            owner._editorFingerReferenceAnimator.applyRootMotion = false;
            owner._editorFingerReferenceAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            owner._editorFingerReferenceAnimator.enabled = true;
            owner._editorFingerReferenceAnimator.speed = 0f;
            owner._editorFingerReferenceAnimator.Rebind();
            owner._editorFingerReferenceAnimator.Update(0f);

            if (!owner._editorFingerReferenceAnimator.HasState(0, s.FingerReferenceStateHash))
            {
                Debug.LogWarning($"[PoseSpaceRetargeter] 수동 기준 손가락 Reference state를 찾지 못했습니다: {stateName}");
                DisposeFingerPoseReference(owner);
                return;
            }

            for (int i = 0; i < HumanTrait.MuscleCount; i++)
            {
                if (RetargetingMuscleReferencePolicy.IsFingerMuscle(HumanTrait.MuscleName[i]))
                {
                    s.FingerReferenceMuscleIndices.Add(i);
                }
            }

            s.FingerReferenceHandler = new HumanPoseHandler(owner._editorFingerReferenceAnimator.avatar, owner._editorFingerReferenceAnimator.transform);
            s.FingerReferencePose = new HumanPose();
            owner.ShouldUseManualAnimatorFullBodyPoseReference = options.EnableFullBodyPoseReference;
            owner.manualAnimatorFullBodyPoseReferenceWeight = Mathf.Clamp01(options.FullBodyPoseReferenceWeight);
            owner.ShouldExcludeManualAnimatorFullBodyLowerMuscles = options.FullBodyPoseExcludeLowerBodyMuscles;
            owner.ShouldApplyManualAnimatorFullBodyLowerMusclesOnly = options.FullBodyPoseLowerBodyMusclesOnly;
            owner.ShouldApplyManualAnimatorFullBodyLegTwistMusclesOnly = options.FullBodyPoseLegTwistMusclesOnly;
            owner.manualAnimatorFullBodyPoseRightArmMusclesOnly = options.FullBodyPoseRightArmMusclesOnly;
            owner.manualAnimatorFullBodyPoseLeftArmMusclesOnly = options.FullBodyPoseLeftArmMusclesOnly;
            owner.manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly = options.FullBodyPoseRightSleeveChainMusclesOnly;
            owner.manualAnimatorFullBodyPoseFrameGateStart = Mathf.Max(0f, options.FullBodyPoseFrameGateStart);
            owner.manualAnimatorFullBodyPoseFrameGateEnd = Mathf.Max(0f, options.FullBodyPoseFrameGateEnd);
            owner._useEditorFingerPoseReference = RetargetingMuscleReferencePolicy.ShouldUsePoseReference(
                options.EnableFingerPoseReference,
                owner.ShouldUseManualAnimatorFullBodyPoseReference,
                s.FingerReferenceMuscleIndices.Count);

            // testprefab의 clip 시작(frame 0) Hips localPosition을 캐시 — delta 기반 보정의 기준점
            owner._hasEditorReferenceHipsRestLocalPosition = false;
            owner._editorFingerReferenceAnimator.Play(s.FingerReferenceStateHash, 0, 0f);
            owner._editorFingerReferenceAnimator.Update(0f);
            Transform refHipsRest = owner._editorFingerReferenceAnimator.GetBoneTransform(HumanBodyBones.Hips);
            if (refHipsRest != null && IsFinite(refHipsRest.localPosition))
            {
                owner._editorReferenceHipsRestLocalPosition = refHipsRest.localPosition;
                owner._hasEditorReferenceHipsRestLocalPosition = true;
            }
            Debug.Log($"[PoseSpaceRetargeter] Manual Animator finger reference ready: prefab={referencePrefab.name}, controller={referenceController.name}, state={stateName}, clip={referenceClip.name}, muscles={s.FingerReferenceMuscleIndices.Count}, hipsRest={(owner._hasEditorReferenceHipsRestLocalPosition ? owner._editorReferenceHipsRestLocalPosition.y.ToString("F4") : "N/A")}");
        }

        private static void DisableReferenceRecordingComponents(GameObject referenceInstance)
        {
            if (referenceInstance == null)
            {
                return;
            }

            int disabledSampleCodeCount = 0;
            foreach (global::HumanoidSampleCode sampleCode in referenceInstance.GetComponentsInChildren<global::HumanoidSampleCode>(true))
            {
                sampleCode.AutoStartRecording = false;
                sampleCode.enabled = false;
                disabledSampleCodeCount++;
            }

            int disabledRecorderCount = 0;
            foreach (global::UnityHumanoidVMDRecorder recorder in referenceInstance.GetComponentsInChildren<global::UnityHumanoidVMDRecorder>(true))
            {
                if (recorder.IsRecording)
                {
                    recorder.StopRecording();
                }

                recorder.enabled = false;
                disabledRecorderCount++;
            }

            if (disabledSampleCodeCount > 0 || disabledRecorderCount > 0)
            {
                Debug.Log($"[PoseSpaceRetargeter] Manual finger reference recording components disabled. sampleCode={disabledSampleCodeCount}, recorder={disabledRecorderCount}");
            }
        }

        public void ApplyMuscleReference(PoseSpaceRetargeter owner, ref HumanPose pose)
        {
            owner._hasEditorHumanoidPoseReferenceForFrame = false;
            if (!owner._useEditorHumanoidMuscleReference || pose.muscles == null || owner._editorHumanoidMuscleCurves.Count == 0)
            {
                return;
            }

            float time = owner._legacyAnimationDriver.CurrentTime;
            if (owner._useCompleteEditorHumanoidMuscleReference &&
                owner._editorHumanoidPoseReferencePlayer != null &&
                owner._editorHumanoidPoseReferencePlayer.TryEvaluateAt(
                    time,
                    ref owner._editorHumanoidPoseReference))
            {
                int muscleCount = Mathf.Min(
                    pose.muscles.Length,
                    owner._editorHumanoidPoseReference.muscles.Length);
                Array.Copy(
                    owner._editorHumanoidPoseReference.muscles,
                    pose.muscles,
                    muscleCount);
                pose.bodyRotation = owner._editorHumanoidPoseReference.bodyRotation;
                owner._editorReferenceBodyPosition = owner._editorHumanoidPoseReference.bodyPosition;
                owner._hasEditorReferenceBodyPosition = IsFinite(owner._editorReferenceBodyPosition);
                owner._hasEditorHumanoidPoseReferenceForFrame = true;
            }
            else
            {
                RetargetingPoseInputTransformer.ApplyReferenceCurvesInPlace(
                    pose.muscles,
                    owner._editorHumanoidMuscleCurves,
                    time,
                    owner._useCompleteEditorHumanoidMuscleReference);
            }

            if (!owner._editorHumanoidMuscleReferenceLogged)
            {
                Debug.Log($"[PoseSpaceRetargeter] Editor Humanoid muscle reference applied at t={time:F3}s.");
                owner._editorHumanoidMuscleReferenceLogged = true;
            }
        }

        public void ApplyFingerPoseReference(PoseSpaceRetargeter owner, ref HumanPose pose)
        {
            Session s = GetSession(owner);
            if (!owner._useEditorFingerPoseReference ||
                pose.muscles == null ||
                owner._editorFingerReferenceAnimator == null ||
                s.FingerReferenceHandler == null ||
                s.FingerReferenceMuscleIndices.Count == 0)
            {
                return;
            }

            if (!UpdateManualReferenceAnimator(owner))
            {
                return;
            }

            s.FingerReferenceHandler.GetHumanPose(ref s.FingerReferencePose);

            if (s.FingerReferencePose.muscles == null)
            {
                return;
            }

            if (owner.ShouldUseManualAnimatorFullBodyPoseReference)
            {
                float weight = Mathf.Clamp01(owner.manualAnimatorFullBodyPoseReferenceWeight);
                if (weight <= 0f)
                {
                    return;
                }

                if (!ShouldApplyManualFullBodyPoseReferenceFrameGate(owner))
                {
                    return;
                }

                int count = Mathf.Min(pose.muscles.Length, s.FingerReferencePose.muscles.Length);
                for (int i = 0; i < count; i++)
                {
                    if (!RetargetingMuscleReferencePolicy.ShouldApplyManualFullBodyMuscle(
                        i,
                        owner.manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly,
                        owner.manualAnimatorFullBodyPoseRightArmMusclesOnly,
                        owner.manualAnimatorFullBodyPoseLeftArmMusclesOnly,
                        owner.ShouldApplyManualAnimatorFullBodyLegTwistMusclesOnly,
                        owner.ShouldApplyManualAnimatorFullBodyLowerMusclesOnly,
                        owner.ShouldExcludeManualAnimatorFullBodyLowerMuscles))
                    {
                        continue;
                    }

                    pose.muscles[i] = Mathf.Lerp(pose.muscles[i], s.FingerReferencePose.muscles[i], weight);
                }
            }
            else
            {
                foreach (int muscleIndex in s.FingerReferenceMuscleIndices)
                {
                    if (muscleIndex < 0 || muscleIndex >= pose.muscles.Length || muscleIndex >= s.FingerReferencePose.muscles.Length)
                    {
                        continue;
                    }

                    pose.muscles[muscleIndex] = s.FingerReferencePose.muscles[muscleIndex];
                }

            }

            if (!owner._editorFingerPoseReferenceLogged)
            {
                float time = owner._legacyAnimationDriver.CurrentTime;
                string scope = owner.ShouldUseManualAnimatorFullBodyPoseReference ? "full-body muscle" : "finger";
                string weightSuffix = owner.ShouldUseManualAnimatorFullBodyPoseReference
                    ? $", weight={Mathf.Clamp01(owner.manualAnimatorFullBodyPoseReferenceWeight):F2}"
                    : string.Empty;
                Debug.Log($"[PoseSpaceRetargeter] Manual Animator {scope} reference applied at t={time:F3}s{weightSuffix}.");
                owner._editorFingerPoseReferenceLogged = true;
            }
        }

        public void ApplyBodyRotationReference(PoseSpaceRetargeter owner, ref HumanPose pose)
        {
            Session s = GetSession(owner);
            if (owner._hasEditorHumanoidPoseReferenceForFrame)
            {
                return;
            }

            if (!owner.ShouldUseManualAnimatorBodyRotationReference ||
                owner.manualAnimatorBodyRotationReferenceWeight <= 0f ||
                owner._editorFingerReferenceAnimator == null ||
                s.FingerReferenceHandler == null)
            {
                return;
            }

            if (!UpdateManualReferenceAnimator(owner))
            {
                return;
            }

            s.FingerReferenceHandler.GetHumanPose(ref s.FingerReferencePose);
            Quaternion referenceBodyRotation = s.FingerReferencePose.bodyRotation;
            if (!IsFinite(referenceBodyRotation))
            {
                return;
            }

            float weight = Mathf.Clamp01(owner.manualAnimatorBodyRotationReferenceWeight);
            pose.bodyRotation = Quaternion.Slerp(pose.bodyRotation, referenceBodyRotation, weight);

            Vector3 refBodyPos = s.FingerReferencePose.bodyPosition;
            if (IsFinite(refBodyPos) && refBodyPos.y > 0.01f)
            {
                owner._editorReferenceBodyPosition = refBodyPos;
                owner._hasEditorReferenceBodyPosition = true;
            }
            if (!owner._editorBodyRotationReferenceLogged)
            {
                float time = owner._legacyAnimationDriver.CurrentTime;
                Debug.Log($"[PoseSpaceRetargeter] Manual Animator bodyRotation reference applied at t={time:F3}s, weight={weight:F2}.");
                owner._editorBodyRotationReferenceLogged = true;
            }
        }

        public bool UpdateManualReferenceAnimator(PoseSpaceRetargeter owner)
        {
            Session s = GetSession(owner);
            if (owner._editorFingerReferenceAnimator == null || s.FingerReferenceClipLength <= 0f)
            {
                return false;
            }

            float time = owner._legacyAnimationDriver.CurrentTime;
            float normalizedTime = Mathf.Clamp01(time / s.FingerReferenceClipLength);
            owner._editorFingerReferenceAnimator.Play(s.FingerReferenceStateHash, 0, normalizedTime);
            owner._editorFingerReferenceAnimator.Update(0f);
            return true;
        }

        public void DisposeFingerPoseReference(PoseSpaceRetargeter owner)
        {
            Session s = GetSession(owner);
            if (s.FingerReferenceHandler != null)
            {
                s.FingerReferenceHandler.Dispose();
                s.FingerReferenceHandler = null;
            }

            if (s.FingerReferenceInstance != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(s.FingerReferenceInstance);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(s.FingerReferenceInstance);
                }
            }

            s.FingerReferenceInstance = null;
            owner._editorFingerReferenceAnimator = null;
            s.FingerReferenceMuscleIndices.Clear();
            owner._useEditorFingerPoseReference = false;
            owner._editorBodyRotationReferenceLogged = false;
            owner._editorHandLocalRotationReferenceLogged = false;
            owner._editorFootLocalRotationReferenceLogged = false;
            owner._editorLowerBodySegmentDirectionReferenceLogged = false;
            owner._editorFootHipsAlignedResidualYawReferenceLogged = false;
            owner._editorHipsLocalPositionReferenceLogged = false;
            owner._editorBodyPositionXzReferenceLogged = false;
            owner._hasEditorReferenceBodyPosition = false;
            owner._hasEditorReferenceHipsRestLocalPosition = false;
            owner._hasEditorReferenceLowestFootRestY = false;
            owner._allowEditorFootHeightGroundingReference = false;
        }

        private static string ResolveFirstAnimatorStateName(RuntimeAnimatorController controller)
        {
            if (controller == null)
            {
                return "";
            }

            AnimatorOverrideController overrideController = controller as AnimatorOverrideController;
            if (overrideController != null && overrideController.runtimeAnimatorController != null)
            {
                controller = overrideController.runtimeAnimatorController;
            }

            UnityEditor.Animations.AnimatorController animatorController = controller as UnityEditor.Animations.AnimatorController;
            if (animatorController == null || animatorController.layers == null || animatorController.layers.Length == 0)
            {
                return "";
            }

            UnityEditor.Animations.ChildAnimatorState[] states = animatorController.layers[0].stateMachine.states;
            return states != null && states.Length > 0 ? states[0].state.name : "";
        }

        public void ResetRootTranslationState(PoseSpaceRetargeter owner)
        {
            Session s = GetSession(owner);
            s.HasPreviousRootTranslation = false;
            s.HasSmoothedRootTranslationDelta = false;
            s.PreviousRootTranslation = Vector3.zero;
            s.SmoothedRootTranslationDelta = Vector3.zero;
        }

        public Vector3 ExtractRootTranslationDelta(PoseSpaceRetargeter owner, Vector3 ghostDelta)
        {
            Session s = GetSession(owner);
            if (!owner.ShouldUseEditorHumanoidRootTranslationReference ||
                !s.UseRootTranslationReference ||
                s.RootTranslationX == null ||
                s.RootTranslationZ == null)
            {
                ResetRootTranslationState(owner);
                return Vector3.zero;
            }

            float time = owner._legacyAnimationDriver.CurrentTime;
            Vector3 current = new Vector3(
                s.RootTranslationZ.Evaluate(time),
                0f,
                s.RootTranslationX.Evaluate(time));
            if (!IsFinite(current))
            {
                ResetRootTranslationState(owner);
                return Vector3.zero;
            }

            if (!s.HasPreviousRootTranslation)
            {
                s.PreviousRootTranslation = current;
                s.HasPreviousRootTranslation = true;
                return Vector3.zero;
            }

            Vector3 delta = current - s.PreviousRootTranslation;
            s.PreviousRootTranslation = current;
            Vector3 editorRootDelta = RootMotionGuard.CalculateEditorRootTranslationReferenceDelta(
                delta,
                ghostDelta,
                owner.editorHumanoidRootTranslationWeight,
                owner.editorHumanoidRootTranslationCurrentWeight,
                s.HasSmoothedRootTranslationDelta,
                s.SmoothedRootTranslationDelta,
                out Vector3 smoothedDelta,
                out bool hasSmoothedDelta,
                out bool skippedByGhostDelta,
                out bool skippedByNonFinite);
            s.SmoothedRootTranslationDelta = smoothedDelta;
            s.HasSmoothedRootTranslationDelta = hasSmoothedDelta;
            if (skippedByGhostDelta || skippedByNonFinite)
            {
                return Vector3.zero;
            }

            if (!owner._editorRootTranslationReferenceLogged)
            {
                Debug.Log($"[PoseSpaceRetargeter] Editor Humanoid RootT translation reference applied at t={time:F3}s.");
                owner._editorRootTranslationReferenceLogged = true;
            }

            return editorRootDelta;
        }

        private bool ShouldApplyManualFullBodyPoseReferenceFrameGate(PoseSpaceRetargeter owner)
        {
            return owner.ShouldApplySingleFrameFallbackReferenceFrameGate(
                owner.manualAnimatorFullBodyPoseFrameGateStart,
                owner.manualAnimatorFullBodyPoseFrameGateEnd);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
