using UnityEngine;
using UnityEngine.Serialization;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidArmDeformationGuard
    {
            [Header("팔 변형 방지")]
            [Tooltip("Humanoid muscle 값을 Unity 기본 안전 범위인 -1~1로 제한합니다.")]
            [FormerlySerializedAs("clampMusclesToHumanRange")]
            [SerializeField] private bool _clampMusclesToHumanRange= false;
            public bool clampMusclesToHumanRange { get => _clampMusclesToHumanRange; private set => _clampMusclesToHumanRange = value; }

            [Tooltip("팔이 늘어나거나 비정상적으로 비틀리는 Humanoid arm muscle 값을 제한합니다.")]
            [FormerlySerializedAs("enableAnatomicalArmGuard")]
            [SerializeField] private bool _enableAnatomicalArmGuard= false;
            public bool enableAnatomicalArmGuard { get => _enableAnatomicalArmGuard; set => _enableAnatomicalArmGuard = value; }

            [Tooltip("직접 Animator 재생에도 Forearm Stretch muscle을 제한합니다. 이 값은 팔꿈치 굽힘에 가까워 모션이 굳을 수 있으므로 기본값은 끕니다.")]
            [FormerlySerializedAs("clampArmStretchMuscles")]
            [SerializeField] private bool _clampArmStretchMuscles= false;
            public bool clampArmStretchMuscles { get => _clampArmStretchMuscles; private set => _clampArmStretchMuscles = value; }

            [Tooltip("Humanoid arm stretch muscle 허용치입니다. Forearm Stretch는 팔꿈치 굽힘에 가까우므로 직접 켤 때만 사용합니다.")]
            [Range(0f, 0.5f)]
            [FormerlySerializedAs("armStretchMuscleLimit")]
            [SerializeField] private float _armStretchMuscleLimit= 0f;
            public float armStretchMuscleLimit { get => _armStretchMuscleLimit; set => _armStretchMuscleLimit = value; }

            [Tooltip("상완 twist muscle 허용치입니다.")]
            [Range(0.1f, 1f)]
            [FormerlySerializedAs("upperArmTwistMuscleLimit")]
            [SerializeField] private float _upperArmTwistMuscleLimit= 0.75f;
            public float upperArmTwistMuscleLimit { get => _upperArmTwistMuscleLimit; private set => _upperArmTwistMuscleLimit = value; }

            [Tooltip("전완 twist muscle 허용치입니다.")]
            [Range(0.1f, 1f)]
            [FormerlySerializedAs("lowerArmTwistMuscleLimit")]
            [SerializeField] private float _lowerArmTwistMuscleLimit= 0.65f;
            public float lowerArmTwistMuscleLimit { get => _lowerArmTwistMuscleLimit; private set => _lowerArmTwistMuscleLimit = value; }

            [Tooltip("Humanoid 본 localPosition을 시작 값으로 복구해 팔/다리 길이 변형을 막습니다.")]
            [FormerlySerializedAs("lockHumanoidBonePositions")]
            [SerializeField] private bool _lockHumanoidBonePositions= true;
            public bool lockHumanoidBonePositions { get => _lockHumanoidBonePositions; private set => _lockHumanoidBonePositions = value; }

            [Tooltip("소매/팔 보조본처럼 Humanoid 매핑 밖의 팔/다리 하위 Transform localPosition을 시작 값으로 복구합니다.")]
            [FormerlySerializedAs("lockLimbChildLocalPositions")]
            [SerializeField] private bool _lockLimbChildLocalPositions= true;
            public bool lockLimbChildLocalPositions { get => _lockLimbChildLocalPositions; private set => _lockLimbChildLocalPositions = value; }

            [Tooltip("소매/팔 보조본처럼 Humanoid 매핑 밖의 팔/다리 하위 Transform localRotation을 시작 값으로 복구합니다.")]
            [FormerlySerializedAs("lockLimbChildLocalRotations")]
            [SerializeField] private bool _lockLimbChildLocalRotations= false;
            public bool lockLimbChildLocalRotations { get => _lockLimbChildLocalRotations; private set => _lockLimbChildLocalRotations = value; }

            [Tooltip("모델 전체 Transform localScale을 시작 값으로 복구합니다.")]
            [FormerlySerializedAs("restoreLocalScales")]
            [SerializeField] private bool _restoreLocalScales= true;
            public bool restoreLocalScales { get => _restoreLocalScales; private set => _restoreLocalScales = value; }

            [Tooltip("처음 보정이 발생했을 때 진단 로그를 출력합니다.")]
            [FormerlySerializedAs("logCorrections")]
            [SerializeField] private bool _logCorrections= false;
            public bool logCorrections { get => _logCorrections; private set => _logCorrections = value; }
    }
}
