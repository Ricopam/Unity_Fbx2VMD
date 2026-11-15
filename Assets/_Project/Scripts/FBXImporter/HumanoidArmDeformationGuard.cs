using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
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

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(CorrectionPassOrder.ArmDeformation)]
    public partial class HumanoidArmDeformationGuard : MonoBehaviour
    {

        private readonly Dictionary<Transform, Vector3> _initialLocalScales = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<Transform, Vector3> _initialHumanoidLocalPositions = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<Transform, Vector3> _initialLimbChildLocalPositions = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<Transform, Quaternion> _initialLimbChildLocalRotations = new Dictionary<Transform, Quaternion>();
        private readonly HashSet<Transform> _limbChildRotationExclusions = new HashSet<Transform>();
        private static readonly HumanBodyBones[] LimbPositionRoots =
        {
            HumanBodyBones.LeftShoulder,
            HumanBodyBones.RightShoulder,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftHand,
            HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.RightLowerLeg,
            HumanBodyBones.LeftFoot,
            HumanBodyBones.RightFoot,
            HumanBodyBones.LeftToes,
            HumanBodyBones.RightToes
        };
        private Animator _animator;
        private PoseSpaceRetargeter _linkedRetargeter;
        private Avatar _cachedAvatar;
        private Transform _cachedRoot;
        private HumanPoseHandler _poseHandler;
        private HumanPose _pose;
        private bool _initialized;
        private bool _poseWarningLogged;
        private bool _scaleWarningLogged;
        private bool _positionWarningLogged;
        private bool _limbChildPositionWarningLogged;
        private bool _limbChildRotationWarningLogged;
        private bool _muscleWarningLogged;
        private bool _armWarningLogged;

        private void Awake()
        {
            InitializeIfNeeded();
        }

        private void OnEnable()
        {
            InitializeIfNeeded();
        }

        private void LateUpdate()
        {
            if (!InitializeIfNeeded() || _poseHandler == null)
            {
                return;
            }

            if (ShouldPreserveCompleteEditorHumanoidPoseReference())
            {
                RestoreLimbBaseline();
                RestoreLocalScales();
                return;
            }

            _poseHandler.GetHumanPose(ref _pose);
            if (!IsFinite(_pose))
            {
                LogOnce(ref _poseWarningLogged, "HumanPose에 유효하지 않은 값이 있어 이 프레임의 팔 변형 가드를 건너뜁니다.");
                RestoreLocalScales();
                RestoreLimbBaseline();
                return;
            }

            ClampMusclesIfEnabled();
            RestoreLimbBaseline();
            RestoreLocalScales();
        }

        private void RestoreLimbBaseline()
        {
            RestoreHumanoidLocalPositions();
            RestoreLimbChildLocalPositions();
            RestoreLimbChildLocalRotations();
        }

        void ClampMusclesIfEnabled()
        {
            int rangeClampCount = clampMusclesToHumanRange
                ? HumanoidMuscleRangeLimiter.ClampNonFingerMusclesInPlace(_pose.muscles)
                : 0;
            int armClampCount = enableAnatomicalArmGuard
                ? ClampAnatomicalArmMuscles(
                    ref _pose,
                    armStretchMuscleLimit,
                    upperArmTwistMuscleLimit,
                    lowerArmTwistMuscleLimit,
                    clampArmStretchMuscles)
                : 0;

            if (rangeClampCount <= 0 && armClampCount <= 0)
            {
                return;
            }

            _poseHandler.SetHumanPose(ref _pose);
            if (rangeClampCount > 0)
            {
                LogOnce(ref _muscleWarningLogged, $"Humanoid muscle {rangeClampCount}개를 -1~1 범위로 제한했습니다.");
            }

            if (armClampCount > 0)
            {
                LogOnce(ref _armWarningLogged, $"팔 변형 방지를 위해 arm muscle {armClampCount}개를 제한했습니다.");
            }
        }

        public void Configure(ArmDeformationSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            clampMusclesToHumanRange = settings.ClampMusclesToHumanRange;
            enableAnatomicalArmGuard = settings.EnableAnatomicalArmGuard;
            clampArmStretchMuscles = settings.ClampArmStretchMuscles;
            armStretchMuscleLimit = Mathf.Clamp(settings.StretchMuscleLimit, 0f, 0.5f);
            upperArmTwistMuscleLimit = Mathf.Clamp(settings.UpperArmTwistMuscleLimit, 0.1f, 1f);
            lowerArmTwistMuscleLimit = Mathf.Clamp(settings.LowerArmTwistMuscleLimit, 0.1f, 1f);
            lockHumanoidBonePositions = settings.LockHumanoidBonePositions;
            lockLimbChildLocalPositions = settings.LockLimbChildLocalPositions;
            lockLimbChildLocalRotations = settings.LockLimbChildLocalRotations;
            logCorrections = settings.LogCorrections;
        }

        internal void BindRetargeter(PoseSpaceRetargeter retargeter)
        {
            _linkedRetargeter = retargeter;
        }

        public void RecaptureBaseline()
        {
            if (!InitializeIfNeeded())
            {
                return;
            }

            CaptureBaseline();
        }

        public void SetLimbChildRotationExclusions(IEnumerable<Transform> excludedTransforms)
        {
            _limbChildRotationExclusions.Clear();

            if (excludedTransforms == null)
            {
                return;
            }

            foreach (Transform excludedTransform in excludedTransforms)
            {
                if (excludedTransform == null)
                {
                    continue;
                }

                _limbChildRotationExclusions.Add(excludedTransform);
            }
        }

        public static int ClampAnatomicalArmMuscles(
            ref HumanPose pose,
            float stretchLimit,
            float upperTwistLimit,
            float lowerTwistLimit,
            bool clampStretchMuscles = false)
        {
            if (pose.muscles == null)
            {
                return 0;
            }

            int count = Mathf.Min(pose.muscles.Length, HumanTrait.MuscleCount);
            int changed = 0;
            float safeStretchLimit = Mathf.Clamp(stretchLimit, 0f, 0.5f);
            float safeUpperTwistLimit = Mathf.Clamp(upperTwistLimit, 0.1f, 1f);
            float safeLowerTwistLimit = Mathf.Clamp(lowerTwistLimit, 0.1f, 1f);

            changed = ClampArmMuscleRange(
                pose.muscles, count, safeStretchLimit, safeUpperTwistLimit,
                safeLowerTwistLimit, clampStretchMuscles);
            return changed;
        }

        private static int ClampArmMuscleRange(
            float[] muscles, int count, float safeStretchLimit,
            float safeUpperTwistLimit, float safeLowerTwistLimit,
            bool clampStretchMuscles)
        {
            int changed = 0;
            for (int i = 0; i < count; i++)
            {
                string muscleName = HumanTrait.MuscleName[i];
                if (string.IsNullOrEmpty(muscleName))
                {
                    continue;
                }

                string normalizedName = muscleName.Replace(" ", "").ToLowerInvariant();
                if (!normalizedName.Contains("arm") && !normalizedName.Contains("forearm"))
                {
                    continue;
                }

                float before = muscles[i];
                if (!TryResolveArmMuscleClamp(normalizedName, before,
                        safeStretchLimit, safeUpperTwistLimit, safeLowerTwistLimit,
                        clampStretchMuscles, out float after) ||
                    Mathf.Approximately(before, after))
                {
                    continue;
                }

                muscles[i] = after;
                changed++;
            }
            return changed;
        }

        private static bool TryResolveArmMuscleClamp(
            string normalizedName, float before, float safeStretchLimit,
            float safeUpperTwistLimit, float safeLowerTwistLimit,
            bool clampStretchMuscles, out float after)
        {
            after = before;
            if (normalizedName.Contains("stretch"))
            {
                if (!clampStretchMuscles)
                {
                    return false;
                }
                after = Mathf.Clamp(before, -safeStretchLimit, safeStretchLimit);
                return true;
            }
            if (normalizedName.Contains("forearm") && normalizedName.Contains("twist"))
            {
                after = Mathf.Clamp(before, -safeLowerTwistLimit, safeLowerTwistLimit);
                return true;
            }
            if (normalizedName.Contains("arm") && normalizedName.Contains("twist"))
            {
                after = Mathf.Clamp(before, -safeUpperTwistLimit, safeUpperTwistLimit);
                return true;
            }
            return false;
        }

        private bool InitializeIfNeeded()
        {
            if (_animator == null)
            {
                _animator = GetComponent<Animator>();
            }

            if (_animator == null || _animator.avatar == null || !_animator.avatar.isValid || !_animator.avatar.isHuman)
            {
                return false;
            }

            if (_initialized && _poseHandler != null && _cachedAvatar == _animator.avatar && _cachedRoot == _animator.transform)
            {
                return true;
            }

            _cachedAvatar = _animator.avatar;
            _cachedRoot = _animator.transform;
            _poseHandler = new HumanPoseHandler(_cachedAvatar, _cachedRoot);
            _pose = new HumanPose();
            _initialized = true;
            CaptureBaseline();
            return true;
        }

        private bool ShouldPreserveCompleteEditorHumanoidPoseReference()
        {
            return _linkedRetargeter != null &&
                _linkedRetargeter.targetAnimator == _animator &&
                _linkedRetargeter.IsCompleteEditorHumanoidPoseReferenceActive;
        }

        private void CaptureBaseline()
        {
            _initialLocalScales.Clear();
            _initialHumanoidLocalPositions.Clear();
            _initialLimbChildLocalPositions.Clear();
            _initialLimbChildLocalRotations.Clear();
            ResetWarningFlags();

            foreach (Transform targetTransform in GetComponentsInChildren<Transform>(true))
            {
                _initialLocalScales[targetTransform] = targetTransform.localScale;
            }

            if (_animator == null || !_animator.isHuman)
            {
                return;
            }

            var humanoidBones = CaptureHumanoidBonePositions();
            CaptureLimbChildLocalPositions(humanoidBones);
        }

        private void ResetWarningFlags()
        {
            _poseWarningLogged = false;
            _scaleWarningLogged = false;
            _positionWarningLogged = false;
            _limbChildPositionWarningLogged = false;
            _limbChildRotationWarningLogged = false;
            _muscleWarningLogged = false;
            _armWarningLogged = false;
        }

        private HashSet<Transform> CaptureHumanoidBonePositions()
        {
            var humanoidBones = new HashSet<Transform>();
            for (int i = (int)HumanBodyBones.Hips; i < (int)HumanBodyBones.LastBone; i++)
            {
                HumanBodyBones bone = (HumanBodyBones)i;
                if (bone == HumanBodyBones.Hips)
                {
                    continue;
                }

                Transform boneTransform = _animator.GetBoneTransform(bone);
                if (boneTransform == null || _initialHumanoidLocalPositions.ContainsKey(boneTransform))
                {
                    continue;
                }

                _initialHumanoidLocalPositions[boneTransform] = boneTransform.localPosition;
                humanoidBones.Add(boneTransform);
            }
            return humanoidBones;
        }

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

}
