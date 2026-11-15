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
        internal void ConfigureFinalIkFootGroundingExperiment(GameObject targetObject)
        {
            if (targetObject == null)
            {
                return;
            }

            GrounderBipedIK existingGrounder = targetObject.GetComponent<GrounderBipedIK>();
            BipedIK existingBipedIk = targetObject.GetComponent<BipedIK>();
            if (!_pipeline.enableFinalIkFootGroundingExperiment)
            {
                DisableGroundingExperiment(existingGrounder, existingBipedIk);
                return;
            }

            BipedIK bipedIk = EnsureBipedIk(targetObject, existingBipedIk);

            GrounderBipedIK grounder = existingGrounder;
            grounder ??= targetObject.AddComponent<GrounderBipedIK>();

            grounder.ik = bipedIk;
            ConfigureGrounderSolver(grounder);

            if (_pipeline.logFinalIkFootGroundingExperiment)
            {
                Debug.Log(
                    $"[FBXImport] Final IK foot grounding experiment configured: " +
                    $"weight={grounder.weight:F3}, maxStep={grounder.solver.maxStep:F3}, " +
                    $"footRadius={grounder.solver.footRadius:F3}");
            }
        }

        private BipedIK EnsureBipedIk(GameObject targetObject, BipedIK existing)
        {
            BipedIK bipedIk = existing;
            if (bipedIk == null)
            {
                bipedIk = targetObject.AddComponent<BipedIK>();
            }

            Animator animator = targetObject.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                BipedReferences references = bipedIk.references;
                BipedReferences.AutoDetectReferences(
                    ref references,
                    targetObject.transform,
                    BipedReferences.AutoDetectParams.Default);
                bipedIk.references = references;
            }

            bipedIk.SetToDefaults();
            bipedIk.fixTransforms = true;
            bipedIk.enabled = true;
            return bipedIk;
        }

        static void DisableGroundingExperiment(GrounderBipedIK grounder, BipedIK bipedIk)
        {
            if (grounder != null)
            {
                grounder.weight = 0f;
                grounder.enabled = false;
            }

            if (bipedIk != null)
            {
                bipedIk.fixTransforms = false;
                bipedIk.enabled = false;
            }
        }

        void ConfigureGrounderSolver(GrounderBipedIK grounder)
        {
            grounder.weight = Mathf.Clamp(_pipeline.finalIkFootGroundingWeight, 0f, 0.25f);
            grounder.spineBend = 0f;
            grounder.spineSpeed = 0f;
            grounder.solver.maxStep = Mathf.Clamp(_pipeline.finalIkFootGroundingMaxStep, 0f, 0.08f);
            grounder.solver.footRadius = Mathf.Clamp(_pipeline.finalIkFootGroundingFootRadius, 0.01f, 0.2f);
            grounder.solver.prediction = Mathf.Clamp(_pipeline.finalIkFootGroundingPrediction, 0f, 0.2f);
            grounder.solver.footRotationWeight = Mathf.Clamp01(_pipeline.finalIkFootGroundingFootRotationWeight);
            grounder.solver.pelvisDamper = Mathf.Clamp01(_pipeline.finalIkFootGroundingPelvisDamper);
            grounder.enabled = grounder.weight > 0f;
        }

        internal static void RecaptureTargetGuardBaselines(
            GameObject targetObject,
            bool shouldRecapture)
        {
            if (!shouldRecapture || targetObject == null)
            {
                return;
            }

            HumanoidArmDeformationGuard armGuard =
                targetObject.GetComponent<HumanoidArmDeformationGuard>();
            if (armGuard != null && armGuard.enabled)
            {
                armGuard.RecaptureBaseline();
            }

            HumanoidThumbDeformationGuard thumbGuard =
                targetObject.GetComponent<HumanoidThumbDeformationGuard>();
            if (thumbGuard != null && thumbGuard.enabled)
            {
                thumbGuard.RecaptureBaseline();
            }
        }

        internal HumanoidArmTwistRiggingGuard ConfigureArmTwistRiggingGuard(
            GameObject targetObject,
            Animator targetAnimator)
        {
            if (targetObject == null)
            {
                return null;
            }

            HumanoidArmTwistRiggingGuard twistRiggingGuard =
                targetObject.GetComponent<HumanoidArmTwistRiggingGuard>();
            if (!_pipeline.enableAnimationRiggingArmTwistCorrection)
            {
                DisableTwistRiggingGuard(targetObject, twistRiggingGuard);
                return null;
            }

            twistRiggingGuard ??=
                targetObject.AddComponent<HumanoidArmTwistRiggingGuard>();

            twistRiggingGuard.enableTwistRigging = true;
            twistRiggingGuard.enabled = true;
            bool configured = twistRiggingGuard.Configure(
                targetAnimator,
                _pipeline.AnimationRiggingArmTwistRigWeight,
                _pipeline.AnimationRiggingUpperArmTwistWeight,
                _pipeline.AnimationRiggingForearmTwistWeight,
                _pipeline.logAnimationRiggingArmTwistCorrection);

            return configured ? twistRiggingGuard : null;
        }

        private static void DisableTargetRigBuilder(GameObject targetObject)
        {
            if (targetObject == null)
            {
                return;
            }

            var rigBuilder =
                targetObject.GetComponent<UnityEngine.Animations.Rigging.RigBuilder>();
            if (rigBuilder == null)
            {
                return;
            }

            if (rigBuilder.graph.IsValid())
            {
                rigBuilder.Clear();
            }

            rigBuilder.enabled = false;
        }

        private static void DisableDirectionGuard(
            HumanoidArmDirectionRetargetGuard directionGuard)
        {
            if (directionGuard != null)
            {
                directionGuard.DisableCorrection();
                directionGuard.enabled = false;
            }
        }

        private bool ConfigureDirectionGuardParameters(
            HumanoidArmDirectionRetargetGuard directionGuard,
            Animator ghostAnimator,
            Animator targetAnimator)
        {
            return directionGuard.Configure(
                ghostAnimator,
                targetAnimator,
                _pipeline.YybArmDirectionUpperArmWeight,
                _pipeline.YybArmDirectionForearmWeight,
                _pipeline.YybArmDirectionUpperArmMaxDegrees,
                _pipeline.YybArmDirectionForearmMaxDegrees,
                _pipeline.YybArmDirectionLeftSideWeightScale,
                _pipeline.YybArmDirectionRightSideWeightScale,
                _pipeline.logYybArmDirectionRetargetCorrection);
        }

        internal HumanoidArmDirectionRetargetGuard ConfigureArmDirectionGuard(
            GameObject targetObject,
            Animator targetAnimator,
            Animator ghostAnimator)
        {
            if (targetObject == null)
            {
                return null;
            }

            HumanoidArmDirectionRetargetGuard directionGuard =
                targetObject.GetComponent<HumanoidArmDirectionRetargetGuard>();
            if (!_pipeline.enableYybArmDirectionRetargetCorrection)
            {
                DisableDirectionGuard(directionGuard);
                return null;
            }

            directionGuard ??=
                targetObject.AddComponent<HumanoidArmDirectionRetargetGuard>();

            directionGuard.enableDirectionRetarget = true;
            directionGuard.enabled = true;
            bool configured = ConfigureDirectionGuardParameters(
                directionGuard, ghostAnimator, targetAnimator);

            if (!configured)
            {
                directionGuard.enabled = false;
                return null;
            }

            return directionGuard;
        }

        private void DisableTwistRiggingGuard(
            GameObject targetObject,
            HumanoidArmTwistRiggingGuard twistRiggingGuard)
        {
            if (twistRiggingGuard != null)
            {
                twistRiggingGuard.DisableRigging();
                twistRiggingGuard.enabled = false;
            }

            DisableTargetRigBuilder(targetObject);
        }

        private void ApplySwingLimitParameters(
            HumanoidArmSwingLimitGuard swingLimitGuard,
            Animator targetAnimator)
        {
            swingLimitGuard.Configure(
                targetAnimator,
                true,
                _pipeline.YybArmSwingLimitWeight,
                _pipeline.YybArmSwingMaxDownDot,
                _pipeline.YybArmSwingMinHandHorizontalRatio,
                _pipeline.YybArmSwingMaxHandBelowShoulderRatio,
                _pipeline.YybArmSwingHorizontalReachLimitWeight,
                _pipeline.YybArmSwingMaxHandHorizontalReachRatio,
                _pipeline.YybArmSwingHorizontalReachMaxHandBelowShoulderRatio,
                _pipeline.YybArmSwingHorizontalReachMinElbowAngleAfterApply,
                _pipeline.YybArmSwingRaisedPoseHorizontalReachLimitWeight,
                _pipeline.YybArmSwingRaisedPoseMinUpperArmDownDot,
                _pipeline.YybArmSwingRaisedPoseMaxHandBelowShoulderRatio,
                _pipeline.YybArmSwingRaisedPoseMaxHandHorizontalReachRatio,
                _pipeline.logYybArmSwingLimitCorrection);
        }

        private ArmDeformationSettings BuildArmDeformationSettings()
        {
            return new ArmDeformationSettings(
                clampMusclesToHumanRange: false,
                enableAnatomicalArmGuard: _pipeline.targetGuardClampAnatomicalArmMuscles,
                stretchMuscleLimit: _pipeline.ArmStretchMuscleLimit,
                upperArmTwistMuscleLimit: _pipeline.UpperArmTwistMuscleLimit,
                lowerArmTwistMuscleLimit: _pipeline.LowerArmTwistMuscleLimit,
                lockHumanoidBonePositions: _pipeline.ShouldLockTargetHumanoidBonePositions,
                logCorrections: _pipeline.logArmDeformationGuardCorrections,
                clampArmStretchMuscles: _pipeline.targetGuardClampArmStretchMuscles,
                lockLimbChildLocalPositions: _pipeline.lockTargetLimbChildLocalPositions,
                lockLimbChildLocalRotations: _pipeline.lockTargetLimbChildLocalRotations);
        }

        internal HumanoidArmSwingLimitGuard ConfigureArmSwingLimitGuard(
            GameObject targetObject,
            Animator targetAnimator)
        {
            if (targetObject == null)
            {
                return null;
            }

            HumanoidArmSwingLimitGuard swingLimitGuard =
                targetObject.GetComponent<HumanoidArmSwingLimitGuard>();
            if (!_pipeline.enableYybArmSwingLimitCorrection)
            {
                if (swingLimitGuard != null)
                {
                    swingLimitGuard.enableSwingLimit = false;
                    swingLimitGuard.enabled = false;
                }

                return null;
            }

            swingLimitGuard ??=
                targetObject.AddComponent<HumanoidArmSwingLimitGuard>();

            ApplySwingLimitParameters(swingLimitGuard, targetAnimator);

            return swingLimitGuard;
        }



        internal void ConfigureArmDeformationGuard(
            GameObject targetObject,
            HumanoidArmTwistRiggingGuard twistRiggingGuard,
            HumanoidArmSleeveAnchorGuard sleeveAnchorGuard,
            HumanoidArmVisualTwistGuard visualTwistGuard)
        {
            if (!_pipeline.attachTargetArmDeformationGuard || targetObject == null)
            {
                return;
            }

            HumanoidArmDeformationGuard guard =
                targetObject.GetComponent<HumanoidArmDeformationGuard>();
            guard ??= targetObject.AddComponent<HumanoidArmDeformationGuard>();

            guard.Configure(BuildArmDeformationSettings());
            guard.SetLimbChildRotationExclusions(BuildLimbChildRotationExclusions(
                twistRiggingGuard?.ControlledTransforms,
                sleeveAnchorGuard?.ControlledTransforms,
                visualTwistGuard?.ControlledTransforms));
            guard.enabled = true;
            guard.RecaptureBaseline();
        }

        private static IEnumerable<Transform> BuildLimbChildRotationExclusions(
            params IEnumerable<Transform>[] controlledTransformGroups)
        {
            foreach (IEnumerable<Transform> controlledTransforms in controlledTransformGroups)
            {
                if (controlledTransforms == null)
                {
                    continue;
                }

                foreach (Transform controlledTransform in controlledTransforms)
                {
                    yield return controlledTransform;
                }
            }
        }

    }
}
