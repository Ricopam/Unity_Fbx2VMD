using UnityEngine;
using UnityEngine.Serialization;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidArmSwingLimitGuard
    {
            [Header("휴머노이드 팔 스윙 제한")]
            [Tooltip("손이 몸 밖/어깨 근처에 있는데 상완만 과도하게 아래로 떨어지는 포즈를 제한합니다.")]
            [FormerlySerializedAs("enableSwingLimit")]
            [SerializeField] private bool _enableSwingLimit= true;
            public bool enableSwingLimit { get => _enableSwingLimit; set => _enableSwingLimit = value; }

            [Tooltip("보정 강도입니다. 0이면 적용하지 않고, 1이면 제한 방향까지 즉시 보정합니다.")]
            [Range(0f, 1f)]
            [FormerlySerializedAs("correctionWeight")]
            [SerializeField] private float _correctionWeight= 0.85f;
            public float correctionWeight { get => _correctionWeight; private set => _correctionWeight = value; }

            [Tooltip("상완 방향이 캐릭터 아래 방향과 이 값보다 더 가까우면 보정 후보로 봅니다.")]
            [Range(0f, 1f)]
            [FormerlySerializedAs("maxUpperArmDownDot")]
            [SerializeField] private float _maxUpperArmDownDot= 0.68f;
            public float maxUpperArmDownDot { get => _maxUpperArmDownDot; private set => _maxUpperArmDownDot = value; }

            [Tooltip("손이 어깨에서 팔 길이 대비 이 비율 이상 옆/앞으로 떨어져 있을 때만 보정합니다. 몸 가까이에서 소매가 무너지는 체형도 감지할 수 있도록 기본값을 낮게 둡니다.")]
            [Range(0f, 1.5f)]
            [FormerlySerializedAs("minHandHorizontalRatio")]
            [SerializeField] private float _minHandHorizontalRatio= 0.05f;
            public float minHandHorizontalRatio { get => _minHandHorizontalRatio; private set => _minHandHorizontalRatio = value; }

            [Tooltip("손이 어깨보다 팔 길이 대비 이 비율보다 더 낮으면 자연스럽게 내려간 팔로 보고 보정하지 않습니다.")]
            [Range(0f, 1.5f)]
            [FormerlySerializedAs("maxHandBelowShoulderRatio")]
            [SerializeField] private float _maxHandBelowShoulderRatio= 0.75f;
            public float maxHandBelowShoulderRatio { get => _maxHandBelowShoulderRatio; private set => _maxHandBelowShoulderRatio = value; }

            [Tooltip("손이 몸 밖으로 과하게 벌어진 경우 수평 도달 거리를 제한하는 보정 강도입니다. 0이면 비활성화합니다.")]
            [Range(0f, 1f)]
            [FormerlySerializedAs("horizontalReachLimitWeight")]
            [SerializeField] private float _horizontalReachLimitWeight= 0f;
            public float horizontalReachLimitWeight { get => _horizontalReachLimitWeight; private set => _horizontalReachLimitWeight = value; }

            [Tooltip("팔 길이 대비 허용할 최대 손 수평 도달 거리 비율입니다. 0이면 수평 도달 거리 제한을 사용하지 않습니다.")]
            [Range(0f, 1.5f)]
            [FormerlySerializedAs("maxHandHorizontalReachRatio")]
            [SerializeField] private float _maxHandHorizontalReachRatio= 0f;
            public float maxHandHorizontalReachRatio { get => _maxHandHorizontalReachRatio; private set => _maxHandHorizontalReachRatio = value; }

            [Tooltip("수평 도달 거리 제한에만 적용하는 손 하강 비율입니다. 0이면 기본 손 하강 비율 설정을 사용합니다.")]
            [Range(0f, 1.5f)]
            [FormerlySerializedAs("horizontalReachMaxHandBelowShoulderRatio")]
            [SerializeField] private float _horizontalReachMaxHandBelowShoulderRatio= 0f;
            public float horizontalReachMaxHandBelowShoulderRatio { get => _horizontalReachMaxHandBelowShoulderRatio; private set => _horizontalReachMaxHandBelowShoulderRatio = value; }

            [Tooltip("수평 도달 거리 보정 뒤 팔꿈치 각도가 이 값보다 작으면 해당 보정을 되돌립니다. 0이면 비활성화합니다.")]
            [Range(0f, 180f)]
            [FormerlySerializedAs("horizontalReachMinElbowAngleAfterApply")]
            [SerializeField] private float _horizontalReachMinElbowAngleAfterApply= 0f;
            public float horizontalReachMinElbowAngleAfterApply { get => _horizontalReachMinElbowAngleAfterApply; private set => _horizontalReachMinElbowAngleAfterApply = value; }

            [Tooltip("기존 상완 하강 제한이 동작하지 않는 들어 올린 팔 자세에서 수평 도달 거리를 제한하는 보정 강도입니다.")]
            [Range(0f, 1f)]
            [FormerlySerializedAs("raisedPoseHorizontalReachLimitWeight")]
            [SerializeField] private float _raisedPoseHorizontalReachLimitWeight= 0f;
            public float raisedPoseHorizontalReachLimitWeight { get => _raisedPoseHorizontalReachLimitWeight; private set => _raisedPoseHorizontalReachLimitWeight = value; }

            [Tooltip("들어 올린 팔 자세의 수평 도달 거리 제한을 적용할 최소 상완 하강 내적값입니다.")]
            [Range(0f, 1f)]
            [FormerlySerializedAs("raisedPoseMinUpperArmDownDot")]
            [SerializeField] private float _raisedPoseMinUpperArmDownDot= 0.55f;
            public float raisedPoseMinUpperArmDownDot { get => _raisedPoseMinUpperArmDownDot; private set => _raisedPoseMinUpperArmDownDot = value; }

            [Tooltip("손이 어깨 아래로 이 비율보다 더 내려가 있으면 들어 올린 팔 자세의 수평 도달 거리 제한을 건너뜁니다.")]
            [Range(0f, 1.5f)]
            [FormerlySerializedAs("raisedPoseMaxHandBelowShoulderRatio")]
            [SerializeField] private float _raisedPoseMaxHandBelowShoulderRatio= 0.05f;
            public float raisedPoseMaxHandBelowShoulderRatio { get => _raisedPoseMaxHandBelowShoulderRatio; private set => _raisedPoseMaxHandBelowShoulderRatio = value; }

            [Tooltip("들어 올린 팔 자세에서 허용할 최대 손 수평 도달 거리 비율입니다.")]
            [Range(0f, 1.5f)]
            [FormerlySerializedAs("raisedPoseMaxHandHorizontalReachRatio")]
            [SerializeField] private float _raisedPoseMaxHandHorizontalReachRatio= 0f;
            public float raisedPoseMaxHandHorizontalReachRatio { get => _raisedPoseMaxHandHorizontalReachRatio; private set => _raisedPoseMaxHandHorizontalReachRatio = value; }

            [Tooltip("상완 보정 후 전완을 다시 손 방향으로 맞춰 손이 크게 밀리는 현상을 줄입니다.")]
            [FormerlySerializedAs("preserveHandTarget")]
            [SerializeField] private bool _preserveHandTarget= true;
            public bool preserveHandTarget { get => _preserveHandTarget; private set => _preserveHandTarget = value; }

            [Tooltip("팔 스윙 제한 보정 로그를 출력합니다.")]
            [FormerlySerializedAs("logCorrections")]
            [SerializeField] private bool _logCorrections= false;
            public bool logCorrections { get => _logCorrections; private set => _logCorrections = value; }

        private static float CalculateElbowAngleDegrees(Transform upper, Transform lower, Transform hand)
        {
            if (upper == null || lower == null || hand == null)
            {
                return float.NaN;
            }

            Vector3 toUpper = upper.position - lower.position;
            Vector3 toHand = hand.position - lower.position;
            if (toUpper.sqrMagnitude <= 0.000001f || toHand.sqrMagnitude <= 0.000001f)
            {
                return float.NaN;
            }

            return Vector3.Angle(toUpper, toHand);
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
