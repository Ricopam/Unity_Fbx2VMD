using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidArmDeformationGuard
    {

        private void CaptureLimbChildLocalPositions(HashSet<Transform> humanoidBones)
        {
            if (_animator == null)
            {
                return;
            }

            foreach (HumanBodyBones rootBone in LimbPositionRoots)
            {
                Transform root = _animator.GetBoneTransform(rootBone);
                if (root == null)
                {
                    continue;
                }

                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    if (child == null || child == transform || humanoidBones.Contains(child) || _initialLimbChildLocalPositions.ContainsKey(child))
                    {
                        continue;
                    }

                    _initialLimbChildLocalPositions[child] = child.localPosition;
                    _initialLimbChildLocalRotations[child] = child.localRotation;
                }
            }
        }

        private void RestoreHumanoidLocalPositions()
        {
            if (!lockHumanoidBonePositions)
            {
                return;
            }

            foreach (KeyValuePair<Transform, Vector3> snapshot in _initialHumanoidLocalPositions)
            {
                Transform bone = snapshot.Key;
                if (bone == null || (bone.localPosition - snapshot.Value).sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                LogOnce(ref _positionWarningLogged, $"Humanoid 본 localPosition 변형을 복구했습니다. 첫 본: {bone.name}");
                bone.localPosition = snapshot.Value;
            }
        }

        private void RestoreLimbChildLocalPositions()
        {
            if (!lockLimbChildLocalPositions)
            {
                return;
            }

            foreach (KeyValuePair<Transform, Vector3> snapshot in _initialLimbChildLocalPositions)
            {
                Transform child = snapshot.Key;
                if (child == null || (child.localPosition - snapshot.Value).sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                LogOnce(ref _limbChildPositionWarningLogged, $"Limb child Transform localPosition 변형을 복구했습니다. 첫 Transform: {child.name}");
                child.localPosition = snapshot.Value;
            }
        }

        private void RestoreLimbChildLocalRotations()
        {
            if (!lockLimbChildLocalRotations)
            {
                return;
            }

            foreach (KeyValuePair<Transform, Quaternion> snapshot in _initialLimbChildLocalRotations)
            {
                Transform child = snapshot.Key;
                if (child == null ||
                    _limbChildRotationExclusions.Contains(child) ||
                    Mathf.Abs(Quaternion.Dot(child.localRotation, snapshot.Value)) >= 0.999999f)
                {
                    continue;
                }

                LogOnce(ref _limbChildRotationWarningLogged, $"Limb child Transform localRotation 변형을 복구했습니다. 첫 Transform: {child.name}");
                child.localRotation = snapshot.Value;
            }
        }

        private void RestoreLocalScales()
        {
            if (!restoreLocalScales)
            {
                return;
            }

            foreach (KeyValuePair<Transform, Vector3> snapshot in _initialLocalScales)
            {
                Transform targetTransform = snapshot.Key;
                if (targetTransform == null || (targetTransform.localScale - snapshot.Value).sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                LogOnce(ref _scaleWarningLogged, $"Transform localScale 변형을 복구했습니다. 첫 Transform: {targetTransform.name}");
                targetTransform.localScale = snapshot.Value;
            }
        }

        private void LogOnce(ref bool flag, string message)
        {
            if (flag || !logCorrections)
            {
                flag = true;
                return;
            }

            Debug.LogWarning($"[HumanoidArmDeformationGuard] {message}");
            flag = true;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(HumanPose pose)
        {
            if (!IsFinite(pose.bodyPosition) || !IsFinite(pose.bodyRotation))
            {
                return false;
            }

            if (pose.muscles == null)
            {
                return true;
            }

            foreach (float muscle in pose.muscles)
            {
                if (!IsFinite(muscle))
                {
                    return false;
                }
            }

            return true;
        }

    }

    public sealed class ArmDeformationSettings
    {
        public bool ClampMusclesToHumanRange { get; }
        public bool EnableAnatomicalArmGuard { get; }
        public float StretchMuscleLimit { get; }
        public float UpperArmTwistMuscleLimit { get; }
        public float LowerArmTwistMuscleLimit { get; }
        public bool LockHumanoidBonePositions { get; }
        public bool LogCorrections { get; }
        public bool ClampArmStretchMuscles { get; }
        public bool LockLimbChildLocalPositions { get; }
        public bool LockLimbChildLocalRotations { get; }

        public ArmDeformationSettings(
            bool clampMusclesToHumanRange,
            bool enableAnatomicalArmGuard,
            float stretchMuscleLimit,
            float upperArmTwistMuscleLimit,
            float lowerArmTwistMuscleLimit,
            bool lockHumanoidBonePositions,
            bool logCorrections,
            bool clampArmStretchMuscles,
            bool lockLimbChildLocalPositions,
            bool lockLimbChildLocalRotations)
        {
            ClampMusclesToHumanRange = clampMusclesToHumanRange;
            EnableAnatomicalArmGuard = enableAnatomicalArmGuard;
            StretchMuscleLimit = stretchMuscleLimit;
            UpperArmTwistMuscleLimit = upperArmTwistMuscleLimit;
            LowerArmTwistMuscleLimit = lowerArmTwistMuscleLimit;
            LockHumanoidBonePositions = lockHumanoidBonePositions;
            LogCorrections = logCorrections;
            ClampArmStretchMuscles = clampArmStretchMuscles;
            LockLimbChildLocalPositions = lockLimbChildLocalPositions;
            LockLimbChildLocalRotations = lockLimbChildLocalRotations;
        }
    }

}
