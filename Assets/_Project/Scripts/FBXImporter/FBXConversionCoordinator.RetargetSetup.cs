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
        internal static bool TryResolveTargetAnimator(
            GameObject targetObject,
            out Animator targetAnimator,
            out string errorMessage)
        {
            targetAnimator = null;
            if (targetObject == null)
            {
                errorMessage = "Target Character가 지정되어 있지 않습니다.";
                return false;
            }

            targetAnimator = targetObject.GetComponent<Animator>();
            if (targetAnimator == null ||
                targetAnimator.avatar == null ||
                !targetAnimator.avatar.isValid ||
                !targetAnimator.avatar.isHuman)
            {
                targetAnimator = null;
                errorMessage = "Target Character에 유효한 Humanoid Avatar가 없습니다.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        internal PoseSpaceRetargeter CreateRetargeter(
            GameObject importedModel,
            GameObject targetObject,
            Dictionary<string, string> boneMapping,
            AnimationClip targetClip)
        {
            PoseSpaceRetargeter retargeter = importedModel.AddComponent<PoseSpaceRetargeter>();
            RetargetingContext context = new RetargetingContext(
                importedModel,
                targetObject,
                boneMapping,
                targetClip);
            RetargetingSettings settings = RetargetingSettings.CreateSnapshot(_pipeline);
            retargeter.Initialize(context, settings);
            return retargeter;
        }

        internal void PrepareRetargetingTarget(
            GameObject targetObject,
            Animator targetAnimator,
            Animator ghostAnimator,
            bool shouldFaceTargetToCamera,
            bool shouldDisableMmdPostPoseCorrection,
            Action restoreIdlePoseBeforeRetargetBaselines)
        {
            PrepareTargetPlaybackState(
                targetObject,
                targetAnimator,
                shouldFaceTargetToCamera);
            DisableMmdPostPoseCorrectionForRetarget(
                targetObject,
                shouldDisableMmdPostPoseCorrection);
            ConfigureTargetRetargetGuards(
                targetObject,
                targetAnimator,
                ghostAnimator);

            restoreIdlePoseBeforeRetargetBaselines?.Invoke();

            RemoveLegacyIkControl(targetObject);
            ConfigureFinalIkFootGroundingExperiment(targetObject);
        }

        internal void ConfigureTargetRetargetGuards(
            GameObject targetObject,
            Animator targetAnimator,
            Animator ghostAnimator)
        {
            HumanoidArmTwistRiggingGuard twistRiggingGuard =
                ConfigureArmTwistRiggingGuard(targetObject, targetAnimator);
            ConfigureArmDirectionGuard(targetObject, targetAnimator, ghostAnimator);
            ConfigureArmSwingLimitGuard(targetObject, targetAnimator);
            HumanoidArmSleeveAnchorGuard sleeveAnchorGuard =
                ConfigureArmSleeveAnchorGuard(targetObject, targetAnimator);
            HumanoidArmVisualTwistGuard visualTwistGuard =
                ConfigureArmVisualTwistGuard(targetObject, targetAnimator);
            ConfigureArmDeformationGuard(
                targetObject,
                twistRiggingGuard,
                sleeveAnchorGuard,
                visualTwistGuard);
        }

        internal void PrepareTargetPlaybackState(
            GameObject targetObject,
            Animator targetAnimator,
            bool shouldFaceTargetToCamera)
        {
            if (targetObject == null || targetAnimator == null)
            {
                return;
            }

            targetObject.transform.position = Vector3.zero;
            if (shouldFaceTargetToCamera)
            {
                CameraFacingController.FaceTargetToCamera(targetObject, Camera.main);
            }
            else
            {
                targetObject.transform.rotation = Quaternion.identity;
            }

            targetAnimator.applyRootMotion = false;
            targetAnimator.runtimeAnimatorController = null;
        }

        internal static void RemoveLegacyIkControl(GameObject targetObject)
        {
            if (targetObject == null)
            {
                return;
            }

            IKControl ikControl = targetObject.GetComponent<IKControl>();
            if (ikControl == null)
            {
                return;
            }

            Debug.Log("[FBXImport] 자동 retarget 경로에서 IKControl을 제거합니다.");
            UnityEngine.Object.Destroy(ikControl);
        }

        internal void DisableMmdPostPoseCorrectionForRetarget(
            GameObject targetObject,
            bool shouldDisable)
        {
            if (!shouldDisable || targetObject == null)
            {
                return;
            }

            RestoreMmdPostPoseCorrectionForRetarget();

            int changed = DisableMmdShoulderFields(targetObject);

            if (changed > 0)
            {
                Debug.Log($"[FBXImport] Retarget 중 MMD4Mecanim 어깨 PPH 보정 {changed}개를 일시 비활성화했습니다.");
            }
        }

        private int DisableMmdShoulderFields(GameObject targetObject)
        {
            int changed = 0;
            Component[] components =
                targetObject.GetComponentsInChildren<Component>(true);
            foreach (Component component in components)
            {
                if (component == null)
                {
                    continue;
                }

                Type componentType = component.GetType();
                if (!componentType.Name.Contains("MMD4Mecanim"))
                {
                    continue;
                }

                if (TrySetBooleanField(component, "pphShoulderEnabled", false))
                {
                    changed++;
                }
            }
            return changed;
        }

        internal void RestoreMmdPostPoseCorrectionForRetarget()
        {
            for (int i = _retargetBooleanSnapshots.Count - 1; i >= 0; i--)
            {
                BooleanFieldSnapshot snapshot = _retargetBooleanSnapshots[i];
                if (snapshot.Target == null || snapshot.Field == null)
                {
                    continue;
                }

                snapshot.Field.SetValue(snapshot.Target, snapshot.Value);
            }

            _retargetBooleanSnapshots.Clear();
        }

        private bool TrySetBooleanField(object target, string fieldName, bool value)
        {
            FieldInfo field = FindFieldInHierarchy(target.GetType(), fieldName);
            if (field == null || field.FieldType != typeof(bool))
            {
                return false;
            }

            bool currentValue = (bool)field.GetValue(target);
            if (currentValue == value)
            {
                return false;
            }

            _retargetBooleanSnapshots.Add(new BooleanFieldSnapshot(target, field, currentValue));
            field.SetValue(target, value);
            return true;
        }

        private static FieldInfo FindFieldInHierarchy(Type type, string fieldName)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (Type currentType = type; currentType != null; currentType = currentType.BaseType)
            {
                FieldInfo field = currentType.GetField(fieldName, flags);
                if (field != null)
                {
                    return field;
                }
            }

            return null;
        }

    }
}
