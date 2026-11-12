using UnityEngine;
using UnityEngine.Serialization;

namespace Fbx2Vmd.FBXImporter
{
    // 핵심 컴포넌트·파이널 튜닝·아나토미 가드 직렬화 표면을 담당하는 partial 분할.
    public partial class PoseSpaceRetargeter
    {
        [Header("--- CORE COMPONENTS ---")]
        [FormerlySerializedAs("ghostAnimator")]
        [SerializeField] private Animator _ghostAnimator;  // (Container 내부의 모델)
        public Animator ghostAnimator { get => _ghostAnimator; private set => _ghostAnimator = value; }
        [FormerlySerializedAs("targetAnimator")]
        [SerializeField] private Animator _targetAnimator; // 내 캐릭터
        public Animator targetAnimator { get => _targetAnimator; set => _targetAnimator = value; }

        [Header("--- FINAL TUNING ---")]
        [Tooltip("캐릭터가 뒤를 보고 있다면 체크 (180도 회전)")]
        [FormerlySerializedAs("fixReverseRotation")]
        [SerializeField] private bool _fixReverseRotation= true;
        public bool fixReverseRotation { get => _fixReverseRotation; private set => _fixReverseRotation = value; }

        [Tooltip("Sub_Manual 직접 Animator 재생처럼 FBX HumanPose의 body/root 회전을 보존합니다.")]
        [FormerlySerializedAs("ShouldPreserveFbxRootRotation")]
        [SerializeField] private bool _ShouldPreserveFbxRootRotation= false;
        public bool ShouldPreserveFbxRootRotation { get => _ShouldPreserveFbxRootRotation; private set => _ShouldPreserveFbxRootRotation = value; }

        [Tooltip("Keep target HumanPose bodyPosition Y stable while preserving FBX X/Z body sway.")]
        [FormerlySerializedAs("preserveTargetBodyPosition")]
        [SerializeField] private bool _preserveTargetBodyPosition= true;
        public bool preserveTargetBodyPosition { get => _preserveTargetBodyPosition; private set => _preserveTargetBodyPosition = value; }

        [Tooltip("Use HumanPose bodyPosition X/Z delta as target root motion to reduce visible foot sliding.")]
        [FormerlySerializedAs("useBodyPositionXZRootMotion")]
        [SerializeField] private bool _useBodyPositionXZRootMotion= false;
        public bool useBodyPositionXZRootMotion { get => _useBodyPositionXZRootMotion; private set => _useBodyPositionXZRootMotion = value; }

        [Tooltip("Editor-only experimental RootT X/Z root motion reference. Keep disabled until visual_body_arc_jitter passes without increasing jitter.")]
        [FormerlySerializedAs("ShouldUseEditorHumanoidRootTranslationReference")]
        [SerializeField] private bool _ShouldUseEditorHumanoidRootTranslationReference= false;
        public bool ShouldUseEditorHumanoidRootTranslationReference { get => _ShouldUseEditorHumanoidRootTranslationReference; private set => _ShouldUseEditorHumanoidRootTranslationReference = value; }

        [Tooltip("Weight for Editor Humanoid RootT translation reference.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("editorHumanoidRootTranslationWeight")]
        [SerializeField] private float _editorHumanoidRootTranslationWeight= 0.25f;
        public float editorHumanoidRootTranslationWeight { get => _editorHumanoidRootTranslationWeight; private set => _editorHumanoidRootTranslationWeight = value; }

        [Tooltip("Current-frame blend for smoothed Editor Humanoid RootT translation delta.")]
        [Range(0.05f, 1f)]
        [FormerlySerializedAs("editorHumanoidRootTranslationCurrentWeight")]
        [SerializeField] private float _editorHumanoidRootTranslationCurrentWeight= 0.35f;
        public float editorHumanoidRootTranslationCurrentWeight { get => _editorHumanoidRootTranslationCurrentWeight; private set => _editorHumanoidRootTranslationCurrentWeight = value; }

        [Tooltip("When a foot is visually grounded, add a small X/Z root correction to reduce skating.")]
        [FormerlySerializedAs("ShouldStabilizeGroundedFootXZ")]
        [SerializeField] private bool _ShouldStabilizeGroundedFootXZ= false;
        public bool ShouldStabilizeGroundedFootXZ { get => _ShouldStabilizeGroundedFootXZ; private set => _ShouldStabilizeGroundedFootXZ = value; }

        [Tooltip("Foot-lock correction strength. Lower values preserve dance motion, higher values reduce skating.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("groundedFootLockWeight")]
        [SerializeField] private float _groundedFootLockWeight= 0.45f;
        public float groundedFootLockWeight { get => _groundedFootLockWeight; private set => _groundedFootLockWeight = value; }

        [Tooltip("Maximum X/Z root correction per frame for grounded foot lock.")]
        [Range(0.001f, 0.1f)]
        [FormerlySerializedAs("maxGroundedFootLockStep")]
        [SerializeField] private float _maxGroundedFootLockStep= 0.025f;
        public float maxGroundedFootLockStep { get => _maxGroundedFootLockStep; private set => _maxGroundedFootLockStep = value; }

        [Tooltip("체크 시 공중 부양/박힘을 모두 해결 (Raycast 사용)")]
        [FormerlySerializedAs("useSmartGrounding")]
        [SerializeField] private bool _useSmartGrounding= true;
        public bool useSmartGrounding { get => _useSmartGrounding; private set => _useSmartGrounding = value; }

        [Tooltip("발바닥 높이 미세 조절 (양수: 띄움, 음수: 박음)")]
        [Range(-0.1f, 0.1f)]
        [FormerlySerializedAs("groundOffset")]
        [SerializeField] private float _groundOffset= 0.0f;
        public float groundOffset { get => _groundOffset; private set => _groundOffset = value; }

        [Tooltip("FBX Avatar에서 비정상적으로 튀는 Humanoid muscle 값을 안전 범위로 제한합니다.")]
        [FormerlySerializedAs("clampMusclesToHumanRange")]
        [SerializeField] private bool _clampMusclesToHumanRange= false;
        public bool clampMusclesToHumanRange { get => _clampMusclesToHumanRange; private set => _clampMusclesToHumanRange = value; }

        [Header("--- ANATOMY GUARD ---")]
        [Tooltip("Target 팔이 늘어나거나 비정상적으로 비틀리는 Humanoid muscle 값을 제한합니다.")]
        [FormerlySerializedAs("enableAnatomicalArmGuard")]
        [SerializeField] private bool _enableAnatomicalArmGuard= true;
        public bool enableAnatomicalArmGuard { get => _enableAnatomicalArmGuard; set => _enableAnatomicalArmGuard = value; }

        [Tooltip("Humanoid 팔 Stretch muscle 허용치입니다. Forearm Stretch는 팔꿈치 굽힘에 가까우므로 기본적으로 제한하지 않습니다.")]
        [Range(0f, 0.5f)]
        [FormerlySerializedAs("armStretchMuscleLimit")]
        [SerializeField] private float _armStretchMuscleLimit= 0f;
        public float armStretchMuscleLimit { get => _armStretchMuscleLimit; set => _armStretchMuscleLimit = value; }

        [Tooltip("Forearm Stretch muscle 제한 여부입니다. Unity Humanoid에서는 팔꿈치 굽힘에 가까우므로 기본값은 꺼야 합니다.")]
        [FormerlySerializedAs("clampArmStretchMuscles")]
        [SerializeField] private bool _clampArmStretchMuscles= false;
        public bool clampArmStretchMuscles { get => _clampArmStretchMuscles; private set => _clampArmStretchMuscles = value; }

        [Tooltip("상완 Twist muscle 허용치입니다.")]
        [Range(0.1f, 1f)]
        [FormerlySerializedAs("upperArmTwistMuscleLimit")]
        [SerializeField] private float _upperArmTwistMuscleLimit= 0.75f;
        public float upperArmTwistMuscleLimit { get => _upperArmTwistMuscleLimit; private set => _upperArmTwistMuscleLimit = value; }

        [Tooltip("전완 Twist muscle 허용치입니다.")]
        [Range(0.1f, 1f)]
        [FormerlySerializedAs("lowerArmTwistMuscleLimit")]
        [SerializeField] private float _lowerArmTwistMuscleLimit= 0.65f;
        public float lowerArmTwistMuscleLimit { get => _lowerArmTwistMuscleLimit; private set => _lowerArmTwistMuscleLimit = value; }

        [Header("--- THUMB ANATOMY GUARD ---")]
        [Tooltip("수동 기준 손가락 pose를 유지하되, YYB 손 구조에서 엄지가 과하게 꺾이는 범위만 제한합니다.")]
        [FormerlySerializedAs("enableThumbAnatomicalGuard")]
        [SerializeField] private bool _enableThumbAnatomicalGuard= true;
        public bool enableThumbAnatomicalGuard { get => _enableThumbAnatomicalGuard; private set => _enableThumbAnatomicalGuard = value; }

        [Tooltip("엄지 굽힘 muscle 최소값입니다.")]
        [Range(-2.5f, 0f)]
        [FormerlySerializedAs("thumbStretchMin")]
        [SerializeField] private float _thumbStretchMin= -2.1f;
        public float thumbStretchMin { get => _thumbStretchMin; private set => _thumbStretchMin = value; }

        [Tooltip("엄지 굽힘 muscle 최대값입니다.")]
        [Range(0f, 2.5f)]
        [FormerlySerializedAs("thumbStretchMax")]
        [SerializeField] private float _thumbStretchMax= 1.0f;
        public float thumbStretchMax { get => _thumbStretchMax; private set => _thumbStretchMax = value; }

        [Tooltip("엄지 굽힘 muscle에 더하는 offset입니다. YYB 엄지 rest pose가 수동 기준보다 과하게 펴져 보일 때만 사용합니다.")]
        [Range(-0.5f, 0.5f)]
        [FormerlySerializedAs("thumbStretchOffset")]
        [SerializeField] private float _thumbStretchOffset= 0f;
        public float thumbStretchOffset { get => _thumbStretchOffset; private set => _thumbStretchOffset = value; }

        [Tooltip("Manual Animator finger reference를 사용할 때는 엄지 stretch offset을 추가하지 않고 수동 기준 엄지 muscle을 보존합니다.")]
        [FormerlySerializedAs("preserveManualFingerReferenceThumbMuscles")]
        [SerializeField] private bool _preserveManualFingerReferenceThumbMuscles= true;
        public bool preserveManualFingerReferenceThumbMuscles { get => _preserveManualFingerReferenceThumbMuscles; private set => _preserveManualFingerReferenceThumbMuscles = value; }

        [FormerlySerializedAs("ShouldUseManualAnimatorFullBodyPoseReference")]
        [SerializeField] private bool _ShouldUseManualAnimatorFullBodyPoseReference= false;
        public bool ShouldUseManualAnimatorFullBodyPoseReference { get => _ShouldUseManualAnimatorFullBodyPoseReference; set => _ShouldUseManualAnimatorFullBodyPoseReference = value; }

        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorFullBodyPoseReferenceWeight")]
        [SerializeField] private float _manualAnimatorFullBodyPoseReferenceWeight= 1f;
        public float manualAnimatorFullBodyPoseReferenceWeight { get => _manualAnimatorFullBodyPoseReferenceWeight; set => _manualAnimatorFullBodyPoseReferenceWeight = value; }

        [Tooltip("Runtime diagnostic: keep manual full-body reference active but skip lower-body muscles.")]
        [FormerlySerializedAs("ShouldExcludeManualAnimatorFullBodyLowerMuscles")]
        [SerializeField] private bool _ShouldExcludeManualAnimatorFullBodyLowerMuscles= false;
        public bool ShouldExcludeManualAnimatorFullBodyLowerMuscles { get => _ShouldExcludeManualAnimatorFullBodyLowerMuscles; set => _ShouldExcludeManualAnimatorFullBodyLowerMuscles = value; }

        [Tooltip("Runtime diagnostic: apply manual full-body reference only to lower-body muscles.")]
        [FormerlySerializedAs("ShouldApplyManualAnimatorFullBodyLowerMusclesOnly")]
        [SerializeField] private bool _ShouldApplyManualAnimatorFullBodyLowerMusclesOnly= false;
        public bool ShouldApplyManualAnimatorFullBodyLowerMusclesOnly { get => _ShouldApplyManualAnimatorFullBodyLowerMusclesOnly; set => _ShouldApplyManualAnimatorFullBodyLowerMusclesOnly = value; }

        [Tooltip("Runtime diagnostic: apply manual full-body reference only to leg in-out/twist muscles.")]
        [FormerlySerializedAs("ShouldApplyManualAnimatorFullBodyLegTwistMusclesOnly")]
        [SerializeField] private bool _ShouldApplyManualAnimatorFullBodyLegTwistMusclesOnly= false;
        public bool ShouldApplyManualAnimatorFullBodyLegTwistMusclesOnly { get => _ShouldApplyManualAnimatorFullBodyLegTwistMusclesOnly; set => _ShouldApplyManualAnimatorFullBodyLegTwistMusclesOnly = value; }

        [Tooltip("Runtime diagnostic: apply manual full-body reference only to right arm and shoulder muscles.")]
        [FormerlySerializedAs("manualAnimatorFullBodyPoseRightArmMusclesOnly")]
        [SerializeField] private bool _manualAnimatorFullBodyPoseRightArmMusclesOnly= false;
        public bool manualAnimatorFullBodyPoseRightArmMusclesOnly { get => _manualAnimatorFullBodyPoseRightArmMusclesOnly; set => _manualAnimatorFullBodyPoseRightArmMusclesOnly = value; }

        [Tooltip("Runtime diagnostic: apply manual full-body reference only to left arm and shoulder muscles.")]
        [FormerlySerializedAs("manualAnimatorFullBodyPoseLeftArmMusclesOnly")]
        [SerializeField] private bool _manualAnimatorFullBodyPoseLeftArmMusclesOnly= false;
        public bool manualAnimatorFullBodyPoseLeftArmMusclesOnly { get => _manualAnimatorFullBodyPoseLeftArmMusclesOnly; set => _manualAnimatorFullBodyPoseLeftArmMusclesOnly = value; }

        [Tooltip("Runtime diagnostic: apply manual full-body reference only to spine and right sleeve chain muscles.")]
        [FormerlySerializedAs("manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly")]
        [SerializeField] private bool _manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly= false;
        public bool manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly { get => _manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly; set => _manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly = value; }

        [Range(0f, 6000f)]
        [FormerlySerializedAs("manualAnimatorFullBodyPoseFrameGateStart")]
        [SerializeField] private float _manualAnimatorFullBodyPoseFrameGateStart= 0f;
        public float manualAnimatorFullBodyPoseFrameGateStart { get => _manualAnimatorFullBodyPoseFrameGateStart; set => _manualAnimatorFullBodyPoseFrameGateStart = value; }

        [Range(0f, 6000f)]
        [FormerlySerializedAs("manualAnimatorFullBodyPoseFrameGateEnd")]
        [SerializeField] private float _manualAnimatorFullBodyPoseFrameGateEnd= 0f;
        public float manualAnimatorFullBodyPoseFrameGateEnd { get => _manualAnimatorFullBodyPoseFrameGateEnd; set => _manualAnimatorFullBodyPoseFrameGateEnd = value; }

        [Tooltip("Runtime diagnostic: after SetHumanPose, blend only right upper/lower leg twist output muscles back toward the solver input within a small cap.")]
        [FormerlySerializedAs("ShouldUseSetHumanPoseRightLegTwistOutputReference")]
        [SerializeField] private bool _ShouldUseSetHumanPoseRightLegTwistOutputReference= false;
        public bool ShouldUseSetHumanPoseRightLegTwistOutputReference { get => _ShouldUseSetHumanPoseRightLegTwistOutputReference; set => _ShouldUseSetHumanPoseRightLegTwistOutputReference = value; }

        [Range(0f, 1f)]
        [FormerlySerializedAs("setHumanPoseRightLegTwistOutputReferenceWeight")]
        [SerializeField] private float _setHumanPoseRightLegTwistOutputReferenceWeight= 1f;
        public float setHumanPoseRightLegTwistOutputReferenceWeight { get => _setHumanPoseRightLegTwistOutputReferenceWeight; set => _setHumanPoseRightLegTwistOutputReferenceWeight = value; }

        [Range(0f, 0.1f)]
        [FormerlySerializedAs("setHumanPoseRightLegTwistOutputReferenceMaxDelta")]
        [SerializeField] private float _setHumanPoseRightLegTwistOutputReferenceMaxDelta= 0.02f;
        public float setHumanPoseRightLegTwistOutputReferenceMaxDelta { get => _setHumanPoseRightLegTwistOutputReferenceMaxDelta; set => _setHumanPoseRightLegTwistOutputReferenceMaxDelta = value; }

        [Tooltip("Manual Animator finger reference의 엄지 체인 localRotation도 Target에 적용해 모델별 엄지 축 차이를 줄입니다.")]
        [FormerlySerializedAs("useManualAnimatorThumbLocalRotationReference")]
        [SerializeField] private bool _useManualAnimatorThumbLocalRotationReference= true;
        public bool useManualAnimatorThumbLocalRotationReference { get => _useManualAnimatorThumbLocalRotationReference; set => _useManualAnimatorThumbLocalRotationReference = value; }

        [Tooltip("손목 localRotation을 Sub_Manual/testPrefab Animator가 같은 FBX clip에서 평가한 값을 기준으로 덮어씁니다. t13.2 hand pose parity 회귀 보호용입니다.")]
        [FormerlySerializedAs("useManualAnimatorHandLocalRotationReference")]
        [SerializeField] private bool _useManualAnimatorHandLocalRotationReference= true;
        public bool useManualAnimatorHandLocalRotationReference { get => _useManualAnimatorHandLocalRotationReference; set => _useManualAnimatorHandLocalRotationReference = value; }

        [Tooltip("Manual Animator finger reference의 엄지 세그먼트 방향을 Target 손 기준 방향에 맞춰 모델별 bind axis 차이를 줄입니다.")]
        [FormerlySerializedAs("useManualAnimatorThumbSegmentDirectionReference")]
        [SerializeField] private bool _useManualAnimatorThumbSegmentDirectionReference= true;
        public bool useManualAnimatorThumbSegmentDirectionReference { get => _useManualAnimatorThumbSegmentDirectionReference; private set => _useManualAnimatorThumbSegmentDirectionReference = value; }

        [Tooltip("엄지 세그먼트 방향 보정 강도입니다.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorThumbSegmentDirectionWeight")]
        [SerializeField] private float _manualAnimatorThumbSegmentDirectionWeight= 1f;
        public float manualAnimatorThumbSegmentDirectionWeight { get => _manualAnimatorThumbSegmentDirectionWeight; private set => _manualAnimatorThumbSegmentDirectionWeight = value; }

        [Tooltip("Manual Animator finger reference의 손바닥 기준 Hand->ThumbIntermediate 방향을 Target에 적용합니다.")]
        [FormerlySerializedAs("useManualAnimatorThumbHandDirectionReference")]
        [SerializeField] private bool _useManualAnimatorThumbHandDirectionReference= true;
        public bool useManualAnimatorThumbHandDirectionReference { get => _useManualAnimatorThumbHandDirectionReference; private set => _useManualAnimatorThumbHandDirectionReference = value; }

        [Tooltip("손바닥 기준 엄지 시작 방향 보정 강도입니다.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorThumbHandDirectionWeight")]
        [SerializeField] private float _manualAnimatorThumbHandDirectionWeight= 1f;
        public float manualAnimatorThumbHandDirectionWeight { get => _manualAnimatorThumbHandDirectionWeight; private set => _manualAnimatorThumbHandDirectionWeight = value; }

        [Tooltip("Manual Animator finger reference의 손바닥 전체 프레임을 Target 손에 적용합니다.")]
        [FormerlySerializedAs("useManualAnimatorHandPalmFrameReference")]
        [SerializeField] private bool _useManualAnimatorHandPalmFrameReference= true;
        public bool useManualAnimatorHandPalmFrameReference { get => _useManualAnimatorHandPalmFrameReference; set => _useManualAnimatorHandPalmFrameReference = value; }

        [Tooltip("손바닥 프레임 보정 강도입니다.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorHandPalmFrameWeight")]
        [SerializeField] private float _manualAnimatorHandPalmFrameWeight= 1f;
        public float manualAnimatorHandPalmFrameWeight { get => _manualAnimatorHandPalmFrameWeight; set => _manualAnimatorHandPalmFrameWeight = value; }

        [Tooltip("Manual Animator finger reference의 손 기준 엄지 시작 위치를 Target에 적용합니다.")]
        [FormerlySerializedAs("useManualAnimatorThumbBasePositionReference")]
        [SerializeField] private bool _useManualAnimatorThumbBasePositionReference= true;
        public bool useManualAnimatorThumbBasePositionReference { get => _useManualAnimatorThumbBasePositionReference; private set => _useManualAnimatorThumbBasePositionReference = value; }

        [Tooltip("수동 기준 Animator의 Hips localPosition을 target Hips에 선택적으로 적용합니다. testprefab Hips delta가 YYB에 전달되어 발 호 궤적이 심해지므로 기본 비활성화합니다.")]
        [FormerlySerializedAs("ShouldUseManualAnimatorHipsLocalPositionReference")]
        [SerializeField] private bool _ShouldUseManualAnimatorHipsLocalPositionReference= false;
        public bool ShouldUseManualAnimatorHipsLocalPositionReference { get => _ShouldUseManualAnimatorHipsLocalPositionReference; set => _ShouldUseManualAnimatorHipsLocalPositionReference = value; }

        [Tooltip("Sub_Manual/testPrefab Animator의 HumanPose bodyRotation을 retarget pose 기준으로 사용해 팔꿈치 bend plane 기준축 차이를 줄입니다.")]
        [FormerlySerializedAs("ShouldUseManualAnimatorBodyRotationReference")]
        [SerializeField] private bool _ShouldUseManualAnimatorBodyRotationReference= true;
        public bool ShouldUseManualAnimatorBodyRotationReference { get => _ShouldUseManualAnimatorBodyRotationReference; set => _ShouldUseManualAnimatorBodyRotationReference = value; }

        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorBodyRotationReferenceWeight")]
        [SerializeField] private float _manualAnimatorBodyRotationReferenceWeight= 1f;
        public float manualAnimatorBodyRotationReferenceWeight { get => _manualAnimatorBodyRotationReferenceWeight; set => _manualAnimatorBodyRotationReferenceWeight = value; }

        [Tooltip("preserveTargetBodyPosition=true 일 때 body Y 높이를 수동 기준 Animator의 HumanPose bodyPosition.y로 대체합니다. ghost Legacy-animation bodyPos 스파이크 없이 상체 높이를 애니메이션에 맞게 따라가도록 합니다.")]
        [FormerlySerializedAs("ShouldUseManualAnimatorBodyPositionYReference")]
        [SerializeField] private bool _ShouldUseManualAnimatorBodyPositionYReference= false;
        public bool ShouldUseManualAnimatorBodyPositionYReference { get => _ShouldUseManualAnimatorBodyPositionYReference; private set => _ShouldUseManualAnimatorBodyPositionYReference = value; }

        [Tooltip("Runtime diagnostic: blend HumanPose bodyPosition X/Z toward the manual Animator reference before SetHumanPose.")]
        [FormerlySerializedAs("ShouldUseManualAnimatorBodyPositionXzReference")]
        [SerializeField] private bool _ShouldUseManualAnimatorBodyPositionXzReference= false;
        public bool ShouldUseManualAnimatorBodyPositionXzReference { get => _ShouldUseManualAnimatorBodyPositionXzReference; set => _ShouldUseManualAnimatorBodyPositionXzReference = value; }

        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorBodyPositionXzReferenceWeight")]
        [SerializeField] private float _manualAnimatorBodyPositionXzReferenceWeight= 1f;
        public float manualAnimatorBodyPositionXzReferenceWeight { get => _manualAnimatorBodyPositionXzReferenceWeight; set => _manualAnimatorBodyPositionXzReferenceWeight = value; }

        [Range(0f, 0.2f)] [FormerlySerializedAs("manualAnimatorBodyPositionXzReferenceMaxOffset")]
        [SerializeField] private float _manualAnimatorBodyPositionXzReferenceMaxOffset= 0.025f;
        public float manualAnimatorBodyPositionXzReferenceMaxOffset { get => _manualAnimatorBodyPositionXzReferenceMaxOffset; set => _manualAnimatorBodyPositionXzReferenceMaxOffset = value; }

        [Range(0f, 6000f)] [FormerlySerializedAs("manualAnimatorBodyPositionXzReferenceFrameGateStart")]
        [SerializeField] private float _manualAnimatorBodyPositionXzReferenceFrameGateStart= 0f;
        public float manualAnimatorBodyPositionXzReferenceFrameGateStart { get => _manualAnimatorBodyPositionXzReferenceFrameGateStart; set => _manualAnimatorBodyPositionXzReferenceFrameGateStart = value; }

        [Range(0f, 6000f)] [FormerlySerializedAs("manualAnimatorBodyPositionXzReferenceFrameGateEnd")]
        [SerializeField] private float _manualAnimatorBodyPositionXzReferenceFrameGateEnd= 0f;
        public float manualAnimatorBodyPositionXzReferenceFrameGateEnd { get => _manualAnimatorBodyPositionXzReferenceFrameGateEnd; set => _manualAnimatorBodyPositionXzReferenceFrameGateEnd = value; }

        [Tooltip("Runtime diagnostic blend width in recorder frames for manual Animator bodyPosition X/Z frame gates. Zero keeps the legacy hard gate.")]
        [Range(0f, 600f)] [FormerlySerializedAs("manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames")]
        [SerializeField] private float _manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames= 0f;
        public float manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames { get => _manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames; set => _manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames = value; }

        [Tooltip("Runtime diagnostic scale for the manual Animator bodyPosition X solver-input basis. One keeps the legacy X contribution.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorBodyPositionXzReferenceAxisXScale")]
        [SerializeField] private float _manualAnimatorBodyPositionXzReferenceAxisXScale= 1f;
        public float manualAnimatorBodyPositionXzReferenceAxisXScale { get => _manualAnimatorBodyPositionXzReferenceAxisXScale; set => _manualAnimatorBodyPositionXzReferenceAxisXScale = value; }

        [Tooltip("Runtime diagnostic scale for the manual Animator bodyPosition Z solver-input basis. One keeps the legacy Z contribution.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorBodyPositionXzReferenceAxisZScale")]
        [SerializeField] private float _manualAnimatorBodyPositionXzReferenceAxisZScale= 1f;
        public float manualAnimatorBodyPositionXzReferenceAxisZScale { get => _manualAnimatorBodyPositionXzReferenceAxisZScale; set => _manualAnimatorBodyPositionXzReferenceAxisZScale = value; }

        [Tooltip("Runtime diagnostic: apply a frame-local local-X offset to the right sleeve helper silhouette after SetHumanPose.")]
        [FormerlySerializedAs("useYybRightSleeveSilhouetteLocalOffsetReference")]
        [SerializeField] private bool _useYybRightSleeveSilhouetteLocalOffsetReference= false;
        public bool useYybRightSleeveSilhouetteLocalOffsetReference { get => _useYybRightSleeveSilhouetteLocalOffsetReference; set => _useYybRightSleeveSilhouetteLocalOffsetReference = value; }

        [Tooltip("Local X offset in meters for the frame-local right sleeve silhouette probe.")]
        [Range(-0.2f, 0.2f)] [FormerlySerializedAs("yybRightSleeveSilhouetteLocalOffsetX")]
        [SerializeField] private float _yybRightSleeveSilhouetteLocalOffsetX= 0f;
        public float yybRightSleeveSilhouetteLocalOffsetX { get => _yybRightSleeveSilhouetteLocalOffsetX; set => _yybRightSleeveSilhouetteLocalOffsetX = value; }

        [Tooltip("Runtime diagnostic start recorder frame for the right sleeve silhouette local-X offset. Zero with end zero keeps the full clip.")]
        [Range(0f, 6000f)] [FormerlySerializedAs("yybRightSleeveSilhouetteLocalOffsetFrameGateStart")]
        [SerializeField] private float _yybRightSleeveSilhouetteLocalOffsetFrameGateStart= 0f;
        public float yybRightSleeveSilhouetteLocalOffsetFrameGateStart { get => _yybRightSleeveSilhouetteLocalOffsetFrameGateStart; set => _yybRightSleeveSilhouetteLocalOffsetFrameGateStart = value; }

        [Tooltip("Runtime diagnostic end recorder frame for the right sleeve silhouette local-X offset. Zero with start zero keeps the full clip.")]
        [Range(0f, 6000f)] [FormerlySerializedAs("yybRightSleeveSilhouetteLocalOffsetFrameGateEnd")]
        [SerializeField] private float _yybRightSleeveSilhouetteLocalOffsetFrameGateEnd= 0f;
        public float yybRightSleeveSilhouetteLocalOffsetFrameGateEnd { get => _yybRightSleeveSilhouetteLocalOffsetFrameGateEnd; set => _yybRightSleeveSilhouetteLocalOffsetFrameGateEnd = value; }

        [Tooltip("수동 기준 Hips localPosition 보정 강도입니다.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorHipsLocalPositionWeight")]
        [SerializeField] private float _manualAnimatorHipsLocalPositionWeight= 1f;
        public float manualAnimatorHipsLocalPositionWeight { get => _manualAnimatorHipsLocalPositionWeight; set => _manualAnimatorHipsLocalPositionWeight = value; }

        [Tooltip("프레임당 수동 기준 Hips localPosition으로 이동할 수 있는 최대 보정 거리입니다.")]
        [Range(0.001f, 0.5f)]
        [FormerlySerializedAs("manualAnimatorHipsLocalPositionMaxOffset")]
        [SerializeField] private float _manualAnimatorHipsLocalPositionMaxOffset= 0.12f;
        public float manualAnimatorHipsLocalPositionMaxOffset { get => _manualAnimatorHipsLocalPositionMaxOffset; set => _manualAnimatorHipsLocalPositionMaxOffset = value; }

        [Tooltip("Use the manual Animator lowest-foot lift as the grounding target height so jump/foot-height arcs are not flattened to the floor.")]
        [FormerlySerializedAs("ShouldUseManualAnimatorFootHeightGroundingReference")]
        [SerializeField] private bool _ShouldUseManualAnimatorFootHeightGroundingReference= false;
        public bool ShouldUseManualAnimatorFootHeightGroundingReference { get => _ShouldUseManualAnimatorFootHeightGroundingReference; set => _ShouldUseManualAnimatorFootHeightGroundingReference = value; }

        [Tooltip("Blend weight for the manual Animator lowest-foot grounding height reference.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorFootHeightGroundingReferenceWeight")]
        [SerializeField] private float _manualAnimatorFootHeightGroundingReferenceWeight= 1f;
        public float manualAnimatorFootHeightGroundingReferenceWeight { get => _manualAnimatorFootHeightGroundingReferenceWeight; private set => _manualAnimatorFootHeightGroundingReferenceWeight = value; }

        [Tooltip("Maximum positive grounding target lift from the manual Animator lowest-foot reference.")]
        [Range(0f, 0.12f)]
        [FormerlySerializedAs("manualAnimatorFootHeightGroundingReferenceMaxLift")]
        [SerializeField] private float _manualAnimatorFootHeightGroundingReferenceMaxLift= 0.08f;
        public float manualAnimatorFootHeightGroundingReferenceMaxLift { get => _manualAnimatorFootHeightGroundingReferenceMaxLift; private set => _manualAnimatorFootHeightGroundingReferenceMaxLift = value; }

        [Tooltip("Apply the manual Animator lower-body leg-chain localRotation to the target as an isolated runtime candidate.")]
        [FormerlySerializedAs("ShouldUseManualAnimatorFootLocalRotationReference")]
        [SerializeField] private bool _ShouldUseManualAnimatorFootLocalRotationReference= false;
        public bool ShouldUseManualAnimatorFootLocalRotationReference { get => _ShouldUseManualAnimatorFootLocalRotationReference; set => _ShouldUseManualAnimatorFootLocalRotationReference = value; }

        [Tooltip("Blend weight for the manual Animator lower-body leg-chain localRotation reference.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorFootLocalRotationReferenceWeight")]
        [SerializeField] private float _manualAnimatorFootLocalRotationReferenceWeight= 1f;
        public float manualAnimatorFootLocalRotationReferenceWeight { get => _manualAnimatorFootLocalRotationReferenceWeight; set => _manualAnimatorFootLocalRotationReferenceWeight = value; }

        [Tooltip("Apply manual Animator lower-body segment directions as an isolated runtime candidate without changing bone lengths or scale.")]
        [FormerlySerializedAs("ShouldUseManualAnimatorLowerBodySegmentDirectionReference")]
        [SerializeField] private bool _ShouldUseManualAnimatorLowerBodySegmentDirectionReference= false;
        public bool ShouldUseManualAnimatorLowerBodySegmentDirectionReference { get => _ShouldUseManualAnimatorLowerBodySegmentDirectionReference; set => _ShouldUseManualAnimatorLowerBodySegmentDirectionReference = value; }

        [Tooltip("Blend weight for the manual Animator lower-body segment direction correction.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorLowerBodySegmentDirectionReferenceWeight")]
        [SerializeField] private float _manualAnimatorLowerBodySegmentDirectionReferenceWeight= 1f;
        public float manualAnimatorLowerBodySegmentDirectionReferenceWeight { get => _manualAnimatorLowerBodySegmentDirectionReferenceWeight; set => _manualAnimatorLowerBodySegmentDirectionReferenceWeight = value; }

        [Tooltip("Maximum per-frame lower-body segment direction correction angle in degrees.")]
        [Range(0f, 20f)]
        [FormerlySerializedAs("manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle")]
        [SerializeField] private float _manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle= 6.2f;
        public float manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle { get => _manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle; set => _manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle = value; }

        [Tooltip("Skip only the upper-leg-to-lower-leg segments from the manual Animator lower-body segment direction correction.")]
        [FormerlySerializedAs("ShouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference")]
        [SerializeField] private bool _ShouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference= false;
        public bool ShouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference { get => _ShouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference; set => _ShouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference = value; }

        [Tooltip("Optional upper-leg-to-lower-leg segment direction max angle in degrees. Zero keeps the shared lower-body segment cap.")]
        [Range(0f, 20f)]
        [FormerlySerializedAs("manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle")]
        [SerializeField] private float _manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle= 0f;
        public float manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle { get => _manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle; set => _manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle = value; }

        [Tooltip("Skip only the lower-leg-to-foot segments from the manual Animator lower-body segment direction correction.")]
        [FormerlySerializedAs("ShouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference")]
        [SerializeField] private bool _ShouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference= false;
        public bool ShouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference { get => _ShouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference; set => _ShouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference = value; }

        [Tooltip("Optional lower-leg-to-foot segment direction max angle in degrees. Zero keeps the shared lower-body segment cap.")]
        [Range(0f, 20f)]
        [FormerlySerializedAs("manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle")]
        [SerializeField] private float _manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle= 0f;
        public float manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle { get => _manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle; set => _manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle = value; }

        [Tooltip("Optional left lower-leg-to-foot segment direction max angle in degrees. Zero keeps the lower-leg-to-foot segment cap.")]
        [Range(0f, 20f)]
        [FormerlySerializedAs("manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle")]
        [SerializeField] private float _manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle= 0f;
        public float manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle { get => _manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle; set => _manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle = value; }

        [Tooltip("Optional right lower-leg-to-foot segment direction max angle in degrees. Zero keeps the lower-leg-to-foot segment cap.")]
        [Range(0f, 20f)]
        [FormerlySerializedAs("manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle")]
        [SerializeField] private float _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle= 0f;
        public float manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle { get => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle; set => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle = value; }

        [Tooltip("Runtime diagnostic scale for right lower-leg-to-foot correction axis X/Z components. One keeps the original axis.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale")]
        [SerializeField] private float _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale= 1f;
        public float manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale { get => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale; set => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale = value; }
    }
}
