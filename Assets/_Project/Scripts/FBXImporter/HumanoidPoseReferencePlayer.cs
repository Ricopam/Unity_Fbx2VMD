using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 기준 Humanoid 모델에서 같은 시간의 원본 자세를 샘플링함.
    /// </summary>
    internal sealed partial class HumanoidPoseReferencePlayer : IDisposable
    {
        private readonly NativeHumanoidAnimationPlayer _animationPlayer =
            new NativeHumanoidAnimationPlayer();

        private GameObject _referenceInstance;
        private Animator _referenceAnimator;
        private HumanPoseHandler _poseHandler;
        private Transform _hips;
        private Transform _leftFoot;
        private Transform _leftToes;
        private Transform _rightFoot;
        private Transform _rightToes;
        private HumanoidFootRotationBinding _leftFootRotation;
        private HumanoidFootRotationBinding _rightFootRotation;
        private Vector2 _footForwardLengths;

        internal bool HasFootRotationReference => _leftFootRotation != null && _rightFootRotation != null;
        internal Quaternion InitialRootRotation { get; private set; } = Quaternion.identity;

        internal bool IsInitialized =>
            _referenceInstance != null &&
            _referenceAnimator != null &&
            _poseHandler != null &&
            _animationPlayer.IsInitialized;

        internal float SourceHumanScale =>
            IsInitialized ? _referenceAnimator.humanScale : 0f;

        internal void Initialize(Animator targetAnimator, AnimationClip clip)
        {
            if (targetAnimator == null)
            {
                throw new ArgumentNullException(nameof(targetAnimator));
            }

            GameObject referenceSource = targetAnimator.gameObject;
#if UNITY_EDITOR
            // 에디터에서는 프리팹 원본을 기준 모델로 우선 사용함.
            referenceSource =
                PrefabUtility.GetCorrespondingObjectFromOriginalSource(referenceSource) ??
                referenceSource;
#endif
            InitializeFromSourceModel(referenceSource, clip);
        }

        internal void InitializeFromSourceModel(
            GameObject referenceSource,
            AnimationClip clip)
        {
            if (referenceSource == null)
            {
                throw new ArgumentNullException(nameof(referenceSource));
            }

            Dispose();

            _referenceInstance = UnityEngine.Object.Instantiate(referenceSource);
            _referenceInstance.name = $"EditorHumanoidPoseReference_{referenceSource.name}";
            _referenceInstance.hideFlags = HideFlags.HideAndDontSave;
            _referenceInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            DisableRuntimeComponents(_referenceInstance);
            _referenceInstance.SetActive(true);

            ResolveReferenceBones();
            InitialRootRotation = _referenceAnimator.transform.rotation;
            if (!TryInitializeAvatarFootReference(_referenceAnimator.avatar.humanDescription.skeleton))
                TryInitializeFootReference();
            _animationPlayer.Initialize(_referenceAnimator, clip);
            _poseHandler = new HumanPoseHandler( _referenceAnimator.avatar, _referenceAnimator.transform);
        }

        // 기준 인스턴스의 Animator와 골반·양발 본을 해석한다.
        private void ResolveReferenceBones()
        {
            _referenceAnimator = _referenceInstance.GetComponent<Animator>() ?? _referenceInstance.GetComponentInChildren<Animator>(true);
            if (_referenceAnimator == null)
            {
                throw new InvalidOperationException( "Native Humanoid 기준 모델에 Animator가 없습니다.");
            }

            _referenceAnimator.runtimeAnimatorController = null;
            _referenceAnimator.enabled = true;
            _hips = _referenceAnimator.GetBoneTransform(HumanBodyBones.Hips);
            _leftFoot = _referenceAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            _leftToes = _referenceAnimator.GetBoneTransform(HumanBodyBones.LeftToes);
            _rightFoot = _referenceAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
            _rightToes = _referenceAnimator.GetBoneTransform(HumanBodyBones.RightToes);
            if (_leftFoot == null || _rightFoot == null)
            {
                throw new InvalidOperationException( "Native Humanoid 기준 모델에 양발 본이 필요합니다.");
            }
        }

        private bool TryInitializeAvatarFootReference(SkeletonBone[] skeleton)
        {
            Transform root = _referenceAnimator.transform;
            if (skeleton == null || skeleton.Length == 0 || _leftToes == null || _rightToes == null) return false;
            if (!TryCollectToeBoneChain(root, out List<Transform> bones, out HashSet<string> names)) return false;
            var reference = new Dictionary<string, SkeletonBone>(StringComparer.Ordinal);
            foreach (SkeletonBone pose in skeleton)
            {
                if (pose.name == null || !names.Contains(pose.name)) continue;
                if (reference.ContainsKey(pose.name) || !IsFinite(pose.position) || !IsFinite(pose.rotation) || !IsFinite(pose.scale) || pose.scale.x <= 0f || pose.scale.y <= 0f || pose.scale.z <= 0f || Mathf.Abs(Quaternion.Dot(pose.rotation, pose.rotation) - 1f) > 0.0001f) return false;
                reference.Add(pose.name, pose); }
            if (reference.Count != bones.Count) return false;
            SkeletonBone[] original = CaptureBoneLocals(bones);
            try
            {
                // 첫 동작의 기울기를 중립으로 삼지 않도록 Avatar 기준에서 발 축과 길이를 함께 취득함.
                ApplySkeletonPose(bones, reference);
                return TryInitializeFootReference(); } finally
            {
                // 재생기와 HumanPoseHandler가 임시 자세를 초기 상태로 기억하지 않도록 먼저 복원함.
                RestoreBoneLocals(bones, original); } }

        // 양 발가락부터 루트까지의 본 체인을 수집하고 이름 충돌을 검증한다.
        private bool TryCollectToeBoneChain(Transform root, out List<Transform> bones, out HashSet<string> names)
        {
            bones = new List<Transform>();
            names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Transform toes in new[] { _leftToes, _rightToes })
            {
                if (!toes.IsChildOf(root)) return false;
                for (Transform bone = toes; bone != root; bone = bone.parent)
                {
                    if (bones.Contains(bone)) continue;
                    if (!names.Add(bone.name)) return false;
                    bones.Add(bone); } }
            foreach (Transform bone in root.GetComponentsInChildren<Transform>(true)) if (bone != root && names.Contains(bone.name) && !bones.Contains(bone)) return false;
            return true;
        }

        static SkeletonBone[] CaptureBoneLocals(List<Transform> bones)
        {
            var original = new SkeletonBone[bones.Count];
            for (int i = 0; i < bones.Count; i++) original[i] = new SkeletonBone { position = bones[i].localPosition, rotation = bones[i].localRotation, scale = bones[i].localScale };
            return original;
        }

        static void ApplySkeletonPose(List<Transform> bones, Dictionary<string, SkeletonBone> reference)
        {
            foreach (Transform bone in bones)
            {
                SkeletonBone pose = reference[bone.name];
                bone.localPosition = pose.position;
                bone.localRotation = pose.rotation;
                bone.localScale = pose.scale; }
        }

        static void RestoreBoneLocals(List<Transform> bones, SkeletonBone[] original)
        {
            for (int i = 0; i < bones.Count; i++)
            {
                bones[i].localPosition = original[i].position;
                bones[i].localRotation = original[i].rotation;
                bones[i].localScale = original[i].scale; } }

        private bool TryInitializeFootReference()
        {
            Vector3 up = InitialRootRotation * Vector3.up;
            if (!HumanoidFootRotationBinding.TryCreate(_leftFoot, _leftToes, up, out var left) ||
                !HumanoidFootRotationBinding.TryCreate(_rightFoot, _rightToes, up, out var right))
                return false;
            _leftFootRotation = left;
            _rightFootRotation = right;
            _footForwardLengths = new Vector2(
                Vector3.ProjectOnPlane(_leftToes.position - _leftFoot.position, up).magnitude,
                Vector3.ProjectOnPlane(_rightToes.position - _rightFoot.position, up).magnitude);
            return true;
        }



        internal bool TryApplyHumanoidBoneLocalRotationsTo(Animator targetAnimator)
        {
            if (!IsInitialized ||
                targetAnimator == null ||
                targetAnimator.avatar == null ||
                !targetAnimator.avatar.isValid ||
                !targetAnimator.avatar.isHuman)
            {
                return false;
            }
            bool applied = false;
            for (HumanBodyBones bone = HumanBodyBones.Hips;
                 bone < HumanBodyBones.LastBone;
                 bone++)
            {
                Transform referenceBone = _referenceAnimator.GetBoneTransform(bone);
                Transform targetBone = targetAnimator.GetBoneTransform(bone);
                if (referenceBone == null ||
                    targetBone == null ||
                    !IsFinite(referenceBone.localRotation))
                {
                    continue;
                }
                targetBone.localRotation = referenceBone.localRotation;
                applied = true;
            }
            return applied;
        }

        public void Dispose()
        {
            _animationPlayer.Dispose();
            _poseHandler?.Dispose();
            _poseHandler = null;
            _referenceAnimator = null;
            _leftFoot = null;
            _leftToes = null;
            _rightFoot = null;
            _rightToes = null;
            _leftFootRotation = _rightFootRotation = null;
            _footForwardLengths = Vector2.zero;
            InitialRootRotation = Quaternion.identity;

            if (_referenceInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(_referenceInstance);
                _referenceInstance = null;
            }
        }

        private static void DisableRuntimeComponents(GameObject root)
        {
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                behaviour.enabled = false;
            }

            foreach (Animation animation in root.GetComponentsInChildren<Animation>(true))
            {
                animation.enabled = false;
            }

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
            }
        }

        private static bool TryCaptureArmDirectionReference(
            Animator animator,
            out HumanoidArmDirectionReference reference)
        {
            reference = default;
            if (!TryGetDirection( animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, out Vector3 leftUpperArm) || !TryGetDirection( animator, HumanBodyBones.LeftLowerArm,
                    HumanBodyBones.LeftHand, out Vector3 leftForearm) || !TryGetDirection( animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, out Vector3 rightUpperArm) ||
                !TryGetDirection( animator, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, out Vector3 rightForearm))
            {
                return false;
            }

            reference = new HumanoidArmDirectionReference( leftUpperArm, leftForearm, rightUpperArm, rightForearm);
            return true;
        }

        private static bool TryGetDirection(
            Animator animator,
            HumanBodyBones startBone,
            HumanBodyBones endBone,
            out Vector3 direction)
        {
            direction = Vector3.zero;
            if (animator == null)
            {
                return false;
            }

            Transform start = animator.GetBoneTransform(startBone);
            Transform end = animator.GetBoneTransform(endBone);
            if (start == null || end == null)
            {
                return false;
            }
            direction = animator.transform.InverseTransformDirection(
                end.position - start.position);
            if (!IsFinite(direction) || direction.sqrMagnitude <= 0.000001f)
            {
                direction = Vector3.zero;
                return false;
            }
            direction.Normalize();
            return true;
        }

    }
}
