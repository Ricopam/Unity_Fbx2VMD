using UnityEngine;
using UnityEngine.Serialization;

namespace Fbx2Vmd.FBXImporter
{
    // 스파이크·그라운딩 가드 직렬화 설정 표면을 담당하는 partial 분할.
    public partial class PoseSpaceRetargeter
    {
        [Header("--- ROOT MOTION SPIKE GUARD ---")]
        [Tooltip("Ghost root delta가 한 프레임에 과도하게 튀면 순간이동으로 보고 해당 프레임의 추가 root 이동을 무시합니다.")]
        [FormerlySerializedAs("clampRootDeltaSpikes")]
        [SerializeField] private bool _clampRootDeltaSpikes= true;
        public bool clampRootDeltaSpikes { get => _clampRootDeltaSpikes; private set => _clampRootDeltaSpikes = value; }

        [Tooltip("한 프레임에 허용할 최대 root 이동량입니다.")]
        [Range(0.001f, 1.0f)]
        [FormerlySerializedAs("maxRootDeltaPerFrame")]
        [SerializeField] private float _maxRootDeltaPerFrame= 0.25f;
        public float maxRootDeltaPerFrame { get => _maxRootDeltaPerFrame; private set => _maxRootDeltaPerFrame = value; }

        [Tooltip("root delta spike를 무시했을 때 최초 1회 진단 로그를 출력합니다.")]
        [FormerlySerializedAs("logRootDeltaSpikes")]
        [SerializeField] private bool _logRootDeltaSpikes= false;
        public bool logRootDeltaSpikes { get => _logRootDeltaSpikes; private set => _logRootDeltaSpikes = value; }

        [Header("--- HIPS LOCAL POSITION SPIKE GUARD ---")]
        [Tooltip("Clamp one-frame target Hips localPosition outliers after SetHumanPose.")]
        [FormerlySerializedAs("clampTargetHipsLocalPositionSpikes")]
        [SerializeField] private bool _clampTargetHipsLocalPositionSpikes= false;
        public bool clampTargetHipsLocalPositionSpikes { get => _clampTargetHipsLocalPositionSpikes; private set => _clampTargetHipsLocalPositionSpikes = value; }

        [Tooltip("Maximum target Hips localPosition delta allowed per frame.")]
        [Range(0.005f, 0.25f)]
        [FormerlySerializedAs("maxTargetHipsLocalPositionDeltaPerFrame")]
        [SerializeField] private float _maxTargetHipsLocalPositionDeltaPerFrame= 0.02f;
        public float maxTargetHipsLocalPositionDeltaPerFrame { get => _maxTargetHipsLocalPositionDeltaPerFrame; private set => _maxTargetHipsLocalPositionDeltaPerFrame = value; }

        [Header("--- GROUNDING STABILITY GUARD ---")]
        [Tooltip("발바닥 접지 보정이 한 프레임에 크게 튀지 않도록 부드럽게 반영합니다.")]
        [FormerlySerializedAs("smoothGrounding")]
        [SerializeField] private bool _smoothGrounding= true;
        public bool smoothGrounding { get => _smoothGrounding; private set => _smoothGrounding = value; }

        [Tooltip("한 프레임에 허용할 최대 수직 접지 보정값입니다.")]
        [Range(0.001f, 0.2f)]
        [FormerlySerializedAs("maxGroundingVerticalStepPerFrame")]
        [SerializeField] private float _maxGroundingVerticalStepPerFrame= 0.01f;
        public float maxGroundingVerticalStepPerFrame { get => _maxGroundingVerticalStepPerFrame; private set => _maxGroundingVerticalStepPerFrame = value; }

        [Tooltip("접지 보정 목표값을 현재 위치에 반영하는 비율입니다.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("groundingSmoothing")]
        [SerializeField] private float _groundingSmoothing= 0.25f;
        public float groundingSmoothing { get => _groundingSmoothing; private set => _groundingSmoothing = value; }

        [Tooltip("이 값보다 작은 발바닥 떨림은 무시합니다.")]
        [Range(0f, 0.05f)]
        [FormerlySerializedAs("groundingDeadZone")]
        [SerializeField] private float _groundingDeadZone= 0.005f;
        public float groundingDeadZone { get => _groundingDeadZone; private set => _groundingDeadZone = value; }

        [Tooltip("초기 접지 확정 뒤에는 root Y를 고정합니다. MMD VMD export에서는 이후 프레임의 발 빠짐을 막기 위해 기본 비활성화합니다.")]
        [FormerlySerializedAs("freezeRootYAfterInitialGrounding")]
        [SerializeField] private bool _freezeRootYAfterInitialGrounding= false;
        public bool freezeRootYAfterInitialGrounding { get => _freezeRootYAfterInitialGrounding; private set => _freezeRootYAfterInitialGrounding = value; }

        [Tooltip("Editor/GameView 프레임이 밀려도 Ghost clip time이 한 프레임에 크게 건너뛰지 않게 제한합니다.")]
        [FormerlySerializedAs("clampLegacyAnimationVisualStep")]
        [SerializeField] private bool _clampLegacyAnimationVisualStep= false;
        public bool clampLegacyAnimationVisualStep { get => _clampLegacyAnimationVisualStep; private set => _clampLegacyAnimationVisualStep = value; }

        [Tooltip("Ghost clip time이 한 렌더 프레임에 전진할 수 있는 기준 FPS입니다.")]
        [Range(15f, 120f)]
        [FormerlySerializedAs("legacyAnimationVisualFrameRate")]
        [SerializeField] private float _legacyAnimationVisualFrameRate= 30f;
        public float legacyAnimationVisualFrameRate { get => _legacyAnimationVisualFrameRate; private set => _legacyAnimationVisualFrameRate = value; }

        [Tooltip("프레임 지연으로 pose가 한 번에 크게 바뀌면 clip time은 보존하고 target pose만 부드럽게 따라가게 합니다.")]
        [FormerlySerializedAs("smoothPoseOnLegacyAnimationStepSpike")]
        [SerializeField] private bool _smoothPoseOnLegacyAnimationStepSpike= true;
        public bool smoothPoseOnLegacyAnimationStepSpike { get => _smoothPoseOnLegacyAnimationStepSpike; private set => _smoothPoseOnLegacyAnimationStepSpike = value; }

        [Tooltip("pose spike smoothing 때 현재 FBX pose를 반영하는 비율입니다.")]
        [Range(0.1f, 1f)]
        [FormerlySerializedAs("poseVisualSpikeCurrentWeight")]
        [SerializeField] private float _poseVisualSpikeCurrentWeight= 0.65f;
        public float poseVisualSpikeCurrentWeight { get => _poseVisualSpikeCurrentWeight; private set => _poseVisualSpikeCurrentWeight = value; }

        [Tooltip("Optional forearm stretch clamp around the current pose during visual spike smoothing. 0 disables the clamp.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("poseVisualSpikeForearmStretchClampMaxOffset")]
        [SerializeField] private float _poseVisualSpikeForearmStretchClampMaxOffset= 0f;
        public float poseVisualSpikeForearmStretchClampMaxOffset { get => _poseVisualSpikeForearmStretchClampMaxOffset; private set => _poseVisualSpikeForearmStretchClampMaxOffset = value; }

        [Tooltip("이 값보다 큰 muscle delta가 발생하면 frame-time spike가 아니어도 pose smoothing을 적용합니다.")]
        [Range(0.05f, 1f)]
        [FormerlySerializedAs("poseVisualMuscleDeltaThreshold")]
        [SerializeField] private float _poseVisualMuscleDeltaThreshold= 0.35f;
        public float poseVisualMuscleDeltaThreshold { get => _poseVisualMuscleDeltaThreshold; private set => _poseVisualMuscleDeltaThreshold = value; }

        [Tooltip("Renderer bounds 하단이 발바닥 추정치에서 과하게 멀면 접지 기준에서 제외합니다.")]
        [FormerlySerializedAs("rejectRendererGroundingOutliers")]
        [SerializeField] private bool _rejectRendererGroundingOutliers= true;
        public bool rejectRendererGroundingOutliers { get => _rejectRendererGroundingOutliers; private set => _rejectRendererGroundingOutliers = value; }

        [Tooltip("Renderer bounds 하단과 발바닥 추정치 사이에 허용할 최대 거리입니다.")]
        [Range(0.02f, 0.3f)]
        [FormerlySerializedAs("maxRendererFootGroundingSeparation")]
        [SerializeField] private float _maxRendererFootGroundingSeparation= 0.12f;
        public float maxRendererFootGroundingSeparation { get => _maxRendererFootGroundingSeparation; private set => _maxRendererFootGroundingSeparation = value; }

        [Tooltip("LateUpdate 후반의 손/팔 보호 로직이 끝난 뒤 메시 bounds 기준으로 루트 Y만 한 번 더 보정합니다.")]
        [FormerlySerializedAs("enableLateVisualGroundingCorrection")]
        [SerializeField] private bool _enableLateVisualGroundingCorrection= true;
        public bool enableLateVisualGroundingCorrection { get => _enableLateVisualGroundingCorrection; private set => _enableLateVisualGroundingCorrection = value; }

        [Tooltip("최종 메시 bounds 보정이 한 프레임에 적용할 수 있는 최대 Y 이동량입니다.")]
        [Range(0.01f, 0.2f)]
        [FormerlySerializedAs("maxLateVisualGroundingCorrection")]
        [SerializeField] private float _maxLateVisualGroundingCorrection= 0.2f;
        public float maxLateVisualGroundingCorrection { get => _maxLateVisualGroundingCorrection; private set => _maxLateVisualGroundingCorrection = value; }

        [Tooltip("최종 메시 bounds 접지 보정의 작은 잔여 오차를 부드럽게 반영해 모델 전체 떨림을 줄입니다.")]
        [FormerlySerializedAs("smoothLateVisualGroundingCorrection")]
        [SerializeField] private bool _smoothLateVisualGroundingCorrection= true;
        public bool smoothLateVisualGroundingCorrection { get => _smoothLateVisualGroundingCorrection; private set => _smoothLateVisualGroundingCorrection = value; }

        [Tooltip("Late visual grounding 잔여 오차가 이 값보다 작으면 smoothing 대상으로 봅니다. 큰 오차는 공중 부유 방지를 위해 즉시 보정합니다.")]
        [Range(0.005f, 0.1f)]
        [FormerlySerializedAs("lateVisualGroundingSnapThreshold")]
        [SerializeField] private float _lateVisualGroundingSnapThreshold= 0.03f;
        public float lateVisualGroundingSnapThreshold { get => _lateVisualGroundingSnapThreshold; private set => _lateVisualGroundingSnapThreshold = value; }

        [Tooltip("작은 late visual grounding 잔여 오차를 현재 위치에 반영하는 비율입니다.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("lateVisualGroundingSmoothing")]
        [SerializeField] private float _lateVisualGroundingSmoothing= 0.25f;
        public float lateVisualGroundingSmoothing { get => _lateVisualGroundingSmoothing; private set => _lateVisualGroundingSmoothing = value; }

        [Tooltip("작은 late visual grounding smoothing 보정이 한 프레임에 움직일 수 있는 최대 Y 이동량입니다.")]
        [Range(0.001f, 0.05f)]
        [FormerlySerializedAs("maxLateVisualGroundingStepPerFrame")]
        [SerializeField] private float _maxLateVisualGroundingStepPerFrame= 0.003f;
        public float maxLateVisualGroundingStepPerFrame { get => _maxLateVisualGroundingStepPerFrame; private set => _maxLateVisualGroundingStepPerFrame = value; }

        [Tooltip("Target Humanoid 본의 localPosition을 초기값으로 되돌려 팔/다리 길이 변형을 막습니다.")]
        [FormerlySerializedAs("ShouldLockTargetHumanoidBonePositions")]
        [SerializeField] private bool _ShouldLockTargetHumanoidBonePositions= true;
        public bool ShouldLockTargetHumanoidBonePositions { get => _ShouldLockTargetHumanoidBonePositions; set => _ShouldLockTargetHumanoidBonePositions = value; }
    }
}
