using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Animations.Rigging;

namespace Fbx2Vmd.FBXImporter
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(CorrectionPassOrder.ArmTwistRigging)]
    public class HumanoidArmTwistRiggingGuard : MonoBehaviour
    {
        private const string RigRootName = "__AutoArmTwistRig";

        [Header("Animation Rigging Arm Twist")]
        [FormerlySerializedAs("enableTwistRigging")]
        [SerializeField] private bool _enableTwistRigging= true;
        public bool enableTwistRigging { get => _enableTwistRigging; set => _enableTwistRigging = value; }
        [Range(0f, 1f)] [FormerlySerializedAs("rigWeight")]
        [SerializeField] private float _rigWeight= 0.65f;
        public float rigWeight { get => _rigWeight; private set => _rigWeight = value; }
        [Range(0f, 1f)] [FormerlySerializedAs("upperArmTwistWeight")]
        [SerializeField] private float _upperArmTwistWeight= 0.45f;
        public float upperArmTwistWeight { get => _upperArmTwistWeight; private set => _upperArmTwistWeight = value; }
        [Range(0f, 1f)] [FormerlySerializedAs("forearmTwistWeight")]
        [SerializeField] private float _forearmTwistWeight= 0.85f;
        public float forearmTwistWeight { get => _forearmTwistWeight; private set => _forearmTwistWeight = value; }
        [FormerlySerializedAs("fallbackTwistAxis")]
        [SerializeField] private TwistCorrectionData.Axis _fallbackTwistAxis= TwistCorrectionData.Axis.X;
        public TwistCorrectionData.Axis fallbackTwistAxis { get => _fallbackTwistAxis; private set => _fallbackTwistAxis = value; }
        [FormerlySerializedAs("logConfiguration")]
        [SerializeField] private bool _logConfiguration= false;
        public bool logConfiguration { get => _logConfiguration; private set => _logConfiguration = value; }

        private readonly List<Transform> _controlledTransforms = new List<Transform>();
        private Animator _animator;
        private RigBuilder _rigBuilder;
        private Rig _rig;
        private bool _configured;
        private bool _buildWarningLogged;

        public IReadOnlyList<Transform> ControlledTransforms => _controlledTransforms;

        private void LateUpdate()
        {
            if (!enableTwistRigging || !_configured || _rigBuilder == null)
            {
                return;
            }

            if (!_rigBuilder.graph.IsValid() && !_rigBuilder.Build())
            {
                LogBuildWarningOnce();
                return;
            }

            _rigBuilder.Evaluate(Time.deltaTime);
        }

        private void OnDisable()
        {
            ClearRigBuilderGraph();
        }

        public bool Configure(
            Animator animator, float nextRigWeight, float nextUpperArmTwistWeight,
            float nextForearmTwistWeight, bool nextLogConfiguration)
        {
            _animator = animator != null ? animator : GetComponent<Animator>();
            rigWeight = Mathf.Clamp01(nextRigWeight);
            upperArmTwistWeight = Mathf.Clamp01(nextUpperArmTwistWeight);
            forearmTwistWeight = Mathf.Clamp01(nextForearmTwistWeight);
            logConfiguration = nextLogConfiguration;
            _controlledTransforms.Clear();
            _configured = false;
            _buildWarningLogged = false;

            if (ShouldSkipTwistRigging())
            {
                return false;
            }

            if (!TryResolveArmBones(out ArmBones bones))
            {
                Debug.LogWarning("[HumanoidArmTwistRiggingGuard] Humanoid 팔 본을 찾지 못해 Animation Rigging 보정을 건너뜁니다.");
                return false;
            }

            return CompleteTwistRigSetup(bones);
        }

        // 리깅 비활성·Animator 부재·자동 리타겟 경로 등 보정 적용 불가 조건.
        private bool ShouldSkipTwistRigging()
        {
            if (!enableTwistRigging || _animator == null || !_animator.isHuman)
            {
                return true;
            }

            if (Application.isPlaying && _animator.runtimeAnimatorController == null)
            {
                ClearRigBuilderGraph();
                if (logConfiguration)
                {
                    Debug.LogWarning("[HumanoidArmTwistRiggingGuard] Target Animator Controller가 없는 자동 리타겟 경로에서는 RigBuilder가 SetHumanPose 포즈를 초기화할 수 있어 Animation Rigging 보정을 건너뜁니다.");
                }

                return true;
            }

            return false;
        }

        // 리그 생성·constraint 구성·완료 로그까지의 구성 단계.
        private bool CompleteTwistRigSetup(ArmBones bones)
        {
            _rig = GetOrCreateRig();
            _rig.weight = rigWeight;

            int configuredCount = ConfigureTwistCorrections(bones);
            if (configuredCount == 0)
            {
                Debug.LogWarning("[HumanoidArmTwistRiggingGuard] YYB 팔 twist 보조본을 찾지 못해 Animation Rigging 보정을 구성하지 못했습니다.");
                return false;
            }

            ConfigureRigBuilder();
            _configured = true;

            if (logConfiguration)
            {
                Debug.Log($"[HumanoidArmTwistRiggingGuard] Animation Rigging twist 보정 구성 완료. constraints={configuredCount}, controlledBones={_controlledTransforms.Count}");
            }

            return true;
        }

        // Humanoid 팔 6본 묶음 — twist 보조본 탐색의 입력.
        struct ArmBones
        {
            public Transform leftUpperArm;
            public Transform leftLowerArm;
            public Transform leftHand;
            public Transform rightUpperArm;
            public Transform rightLowerArm;
            public Transform rightHand;
        }

        bool TryResolveArmBones(out ArmBones bones)
        {
            bones = new ArmBones
            {
                leftUpperArm = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm),
                leftLowerArm = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm),
                leftHand = _animator.GetBoneTransform(HumanBodyBones.LeftHand),
                rightUpperArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm),
                rightLowerArm = _animator.GetBoneTransform(HumanBodyBones.RightLowerArm),
                rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand)
            };
            return bones.leftUpperArm != null && bones.leftLowerArm != null &&
                bones.leftHand != null && bones.rightUpperArm != null &&
                bones.rightLowerArm != null && bones.rightHand != null;
        }

        int ConfigureTwistCorrections(ArmBones bones)
        {
            int configuredCount = 0;
            configuredCount += ConfigureTwistCorrection( "LeftUpperArmTwistCorrection", bones.leftUpperArm, bones.leftLowerArm, FindExactChildren(bones.leftUpperArm, "joint_LeftArmTwist"),
                upperArmTwistWeight, 1);
            configuredCount += ConfigureTwistCorrection( "RightUpperArmTwistCorrection", bones.rightUpperArm, bones.rightLowerArm, FindExactChildren(bones.rightUpperArm, "joint_RightArmTwist"),
                upperArmTwistWeight, 1);
            configuredCount += ConfigureTwistCorrection( "LeftForearmTwistCorrection", bones.leftLowerArm, bones.leftHand,
                FindExactChildren(bones.leftLowerArm, "joint_LeftHandTwist", "joint_LeftHandTwist1", "!joint_LeftHandTwist2", "joint_LeftHandTwist3"), forearmTwistWeight, 4);
            configuredCount += ConfigureTwistCorrection( "RightForearmTwistCorrection", bones.rightLowerArm, bones.rightHand,
                FindExactChildren(bones.rightLowerArm, "joint_RightHandTwist", "joint_RightHandTwist1", "!joint_RightHandTwist2", "joint_RightHandTwist3"), forearmTwistWeight, 4);
            return configuredCount;
        }

        public void DisableRigging()
        {
            enableTwistRigging = false;
            _configured = false;
            _controlledTransforms.Clear();

            if (_rig != null)
            {
                _rig.weight = 0f;
            }

            ClearRigBuilderGraph();
        }

        private void ClearRigBuilderGraph()
        {
            if (_rigBuilder == null)
            {
                _rigBuilder = GetComponent<RigBuilder>();
            }

            if (_rigBuilder == null)
            {
                return;
            }

            if (_rigBuilder.graph.IsValid())
            {
                _rigBuilder.Clear();
            }

            _rigBuilder.enabled = false;
        }

        private int ConfigureTwistCorrection( string constraintName, Transform source, Transform endBone, List<Transform> twistNodes, float maxWeight, int expectedNodeCount)
        {
            if (source == null || endBone == null || twistNodes == null || twistNodes.Count == 0 || maxWeight <= 0f)
            {
                return 0; }
            Transform constraintTransform = GetOrCreateChild(_rig.transform, constraintName);
            TwistCorrection constraint = constraintTransform.GetComponent<TwistCorrection>();
            if (constraint == null)
            {
                constraint = constraintTransform.gameObject.AddComponent<TwistCorrection>(); }
            WeightedTransformArray weightedTransforms = CollectTwistTransforms(twistNodes, maxWeight, expectedNodeCount);
            if (weightedTransforms.Count == 0)
            {
                return 0; }
            TwistCorrectionData data = constraint.data;
            data.sourceObject = source;
            data.twistAxis = DetectTwistAxis(source, endBone);
            data.twistNodes = weightedTransforms;
            constraint.data = data;
            constraint.weight = 1f;
            if (logConfiguration)
            {
                Debug.Log($"[HumanoidArmTwistRiggingGuard] {constraintName}: source={source.name}, axis={data.twistAxis}, nodes={weightedTransforms.Count}"); }
            return 1; }

        // twist 보조본 목록에서 가중치 배열을 만들고 제어 대상을 등록함.
        private WeightedTransformArray CollectTwistTransforms(
            List<Transform> twistNodes, float maxWeight, int expectedNodeCount)
        {
            WeightedTransformArray weightedTransforms = new WeightedTransformArray(0);
            int count = Mathf.Min(twistNodes.Count, WeightedTransformArray.k_MaxLength);
            for (int i = 0; i < count; i++)
            {
                Transform twistNode = twistNodes[i];
                if (twistNode == null)
                {
                    continue; }
                float normalizedStep = expectedNodeCount <= 1 ? 1f : (i + 1f) / expectedNodeCount;
                float nodeWeight = Mathf.Clamp01(maxWeight * normalizedStep);
                weightedTransforms.Add(new WeightedTransform(twistNode, nodeWeight));
                AddControlledTransform(twistNode); }
            return weightedTransforms;
        }

        private void ConfigureRigBuilder()
        {
            _rigBuilder = GetComponent<RigBuilder>();
            if (_rigBuilder == null)
            {
                _rigBuilder = gameObject.AddComponent<RigBuilder>();
            }

            bool hasLayer = false;
            foreach (RigLayer layer in _rigBuilder.layers)
            {
                if (layer != null && layer.rig == _rig)
                {
                    layer.active = true;
                    hasLayer = true;
                    break;
                }
            }
            if (!hasLayer)
            {
                _rigBuilder.layers.Add(new RigLayer(_rig, true));
            }
            _rigBuilder.enabled = true;
            if (Application.isPlaying)
            {
                _rigBuilder.Build();
            }
        }

        private Rig GetOrCreateRig()
        {
            Transform rigTransform = transform.Find(RigRootName);
            if (rigTransform == null)
            {
                GameObject rigObject = new GameObject(RigRootName);
                rigTransform = rigObject.transform;
                rigTransform.SetParent(transform, false);
            }

            Rig nextRig = rigTransform.GetComponent<Rig>();
            if (nextRig == null)
            {
                nextRig = rigTransform.gameObject.AddComponent<Rig>();
            }

            return nextRig;
        }

        private static Transform GetOrCreateChild(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            if (child != null)
            {
                return child;
            }

            GameObject childObject = new GameObject(childName);
            child = childObject.transform;
            child.SetParent(parent, false);
            return child;
        }

        private List<Transform> FindExactChildren(Transform root, params string[] names)
        {
            var results = new List<Transform>();
            if (root == null || names == null)
            {
                return results;
            }

            foreach (string exactName in names)
            {
                Transform found = FindDescendantByName(root, exactName);
                if (found != null && !results.Contains(found))
                {
                    results.Add(found);
                }
            }

            return results;
        }

        private static Transform FindDescendantByName(Transform root, string targetName)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child != root && IsMatchingBoneName(child.name, targetName))
                {
                    return child;
                }
            }

            return null;
        }

        private static bool IsMatchingBoneName(string boneName, string targetName)
        {
            if (string.IsNullOrEmpty(boneName) || string.IsNullOrEmpty(targetName))
            {
                return false;
            }

            return boneName == targetName ||
                   boneName.EndsWith("." + targetName, System.StringComparison.Ordinal) ||
                   boneName.EndsWith(targetName, System.StringComparison.Ordinal);
        }

        private TwistCorrectionData.Axis DetectTwistAxis(Transform source, Transform endBone)
        {
            if (source == null || endBone == null)
            {
                return fallbackTwistAxis;
            }

            Vector3 localDirection = source.InverseTransformDirection(endBone.position - source.position).normalized;
            if (localDirection.sqrMagnitude <= 0.000001f)
            {
                return fallbackTwistAxis;
            }

            float x = Mathf.Abs(localDirection.x);
            float y = Mathf.Abs(localDirection.y);
            float z = Mathf.Abs(localDirection.z);

            if (x >= y && x >= z)
            {
                return TwistCorrectionData.Axis.X;
            }

            if (y >= z)
            {
                return TwistCorrectionData.Axis.Y;
            }
            return TwistCorrectionData.Axis.Z;
        }

        private void AddControlledTransform(Transform target)
        {
            if (target != null && !_controlledTransforms.Contains(target))
            {
                _controlledTransforms.Add(target);
            }
        }

        private void LogBuildWarningOnce()
        {
            if (_buildWarningLogged)
            {
                return;
            }

            Debug.LogWarning("[HumanoidArmTwistRiggingGuard] RigBuilder graph를 만들지 못해 이 프레임의 Animation Rigging 팔 twist 보정을 건너뜁니다.");
            _buildWarningLogged = true;
        }
    }
}
