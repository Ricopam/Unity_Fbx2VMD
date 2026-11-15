using UnityEngine;
using UnityEngine.Serialization;
using System;
using System.IO;
using System.Linq;
using Fbx2Vmd.FileSystem;
using Fbx2Vmd.Recording;


namespace Fbx2Vmd.FBXImporter
{
    public partial class FBXVmdPipeline
{

        [Tooltip("팔 스윙 제한 보정 로그를 출력합니다.")]
        [FormerlySerializedAs("logYybArmSwingLimitCorrection")]
        [SerializeField] private bool _logYybArmSwingLimitCorrection= false;
        public bool logYybArmSwingLimitCorrection { get => _logYybArmSwingLimitCorrection; private set => _logYybArmSwingLimitCorrection = value; }

        [Header("YYB Arm Sleeve Anchor Correction")]
        [Tooltip("YYB 소매/어깨 보조본이 상완 본을 따라가지 않아 소매가 어깨에서 무너져 보이는 현상을 줄입니다.")]
        [FormerlySerializedAs("enableYybArmSleeveAnchorCorrection")]
        [SerializeField] private bool _enableYybArmSleeveAnchorCorrection= true;
        public bool enableYybArmSleeveAnchorCorrection { get => _enableYybArmSleeveAnchorCorrection; set => _enableYybArmSleeveAnchorCorrection = value; }

        [Tooltip("소매 상단 보조본이 상완 회전을 따라가는 강도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmSleeveAnchorInfluence")]
        [SerializeField] private float _YybArmSleeveAnchorInfluence= 0.85f;
        public float YybArmSleeveAnchorInfluence { get => _YybArmSleeveAnchorInfluence; set => _YybArmSleeveAnchorInfluence = value; }

        [Tooltip("어깨 캡 보조본이 상완 회전을 따라가는 강도입니다. MMD4Mecanim PPH와 겹치면 어깨가 둥글게 무너질 수 있어 기본값은 0입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmShoulderCapAnchorInfluence")]
        [SerializeField] private float _YybArmShoulderCapAnchorInfluence= 0f;
        public float YybArmShoulderCapAnchorInfluence { get => _YybArmShoulderCapAnchorInfluence; set => _YybArmShoulderCapAnchorInfluence = value; }

        [Tooltip("소매/어깨 보조본이 한 프레임에 따라갈 수 있는 최대 회전각입니다.")]
        [Range(0f, 120f)] [FormerlySerializedAs("YybArmSleeveAnchorMaxDegrees")]
        [SerializeField] private float _YybArmSleeveAnchorMaxDegrees= 85f;
        public float YybArmSleeveAnchorMaxDegrees { get => _YybArmSleeveAnchorMaxDegrees; set => _YybArmSleeveAnchorMaxDegrees = value; }

        [Tooltip("YYB 소매/어깨 보조본 anchor 보정 로그를 출력합니다.")]
        [FormerlySerializedAs("logYybArmSleeveAnchorCorrection")]
        [SerializeField] private bool _logYybArmSleeveAnchorCorrection= false;
        public bool logYybArmSleeveAnchorCorrection { get => _logYybArmSleeveAnchorCorrection; private set => _logYybArmSleeveAnchorCorrection = value; }

        [Header("디버그 설정")]
        [Tooltip("본 매핑 관련 디버그 로그 출력")]
        [FormerlySerializedAs("showBoneMappingLog")]
        [SerializeField] private bool _showBoneMappingLog= false;
        public bool showBoneMappingLog { get => _showBoneMappingLog; private set => _showBoneMappingLog = value; }
        [Tooltip("런타임 애니메이션 디버그 로그 출력")]
        [FormerlySerializedAs("showRuntimeAnimationLog")]
        [SerializeField] private bool _showRuntimeAnimationLog= false;
        public bool showRuntimeAnimationLog { get => _showRuntimeAnimationLog; private set => _showRuntimeAnimationLog = value; }

        [Tooltip("Ghost 모델 보이기 (디버깅용)")]
        [FormerlySerializedAs("showGhostModel")]
        [SerializeField] private bool _showGhostModel= false;
        public bool showGhostModel { get => _showGhostModel; internal set => _showGhostModel = value; }

        [Tooltip("When the imported Ghost has no renderers, draw a simple skeleton fallback so the debug display is still visible.")]
        [FormerlySerializedAs("showGhostSkeletonWhenNoRenderers")]
        [SerializeField] private bool _showGhostSkeletonWhenNoRenderers= false;
        public bool showGhostSkeletonWhenNoRenderers { get => _showGhostSkeletonWhenNoRenderers; private set => _showGhostSkeletonWhenNoRenderers = value; }

        [Header("Golden Hand Settings")]
        [Tooltip("Finger Stretch Scale (Default 1.0)")]
        [Range(0.0f, 1.0f)] [FormerlySerializedAs("FingerStretchScale")]
        [SerializeField] private float _FingerStretchScale= 1.0f;
        public float FingerStretchScale { get => _FingerStretchScale; private set => _FingerStretchScale = value; }
        [Tooltip("Finger Spread Scale (Default 1.0)")]
        [Range(0.0f, 1.0f)] [FormerlySerializedAs("FingerSpreadScale")]
        [SerializeField] private float _FingerSpreadScale= 1.0f;
        public float FingerSpreadScale { get => _FingerSpreadScale; private set => _FingerSpreadScale = value; }

        [Space(5)]
        [Tooltip("Thumb Stretch Scale (Default 1.0)")]
        [Range(0.0f, 1.0f)] [FormerlySerializedAs("ThumbStretchScale")]
        [SerializeField] private float _ThumbStretchScale= 1.0f;
        public float ThumbStretchScale { get => _ThumbStretchScale; private set => _ThumbStretchScale = value; }
        [Tooltip("Thumb Spread Scale (Default 1.0)")]
        [Range(0.0f, 1.0f)] [FormerlySerializedAs("ThumbSpreadScale")]
        [SerializeField] private float _ThumbSpreadScale= 1.0f;
        public float ThumbSpreadScale { get => _ThumbSpreadScale; private set => _ThumbSpreadScale = value; }

        [Header("Thumb Anatomical Guard")]
        [Tooltip("YYB 모델에서 엄지 관절이 손바닥 뒤로 꺾이거나 과하게 벌어지는 현상을 막기 위해 엄지 Humanoid muscle만 제한합니다.")]
        [FormerlySerializedAs("enableThumbAnatomicalGuard")]
        [SerializeField] private bool _enableThumbAnatomicalGuard= true;
        public bool enableThumbAnatomicalGuard { get => _enableThumbAnatomicalGuard; private set => _enableThumbAnatomicalGuard = value; }

        [Tooltip("Manual Animator finger reference를 사용할 때는 엄지 stretch offset을 추가하지 않고 수동 기준 엄지 muscle을 보존합니다.")]
        [FormerlySerializedAs("preserveManualFingerReferenceThumbMuscles")]
        [SerializeField] private bool _preserveManualFingerReferenceThumbMuscles= false;
        public bool preserveManualFingerReferenceThumbMuscles { get => _preserveManualFingerReferenceThumbMuscles; private set => _preserveManualFingerReferenceThumbMuscles = value; }

        [Tooltip("엄지 굽힘 muscle 최소값입니다. 값이 너무 낮으면 엄지가 손바닥 안쪽으로 과하게 접힐 수 있습니다.")]
        [Range(-2.5f, 0f)] [FormerlySerializedAs("ThumbStretchMin")]
        [SerializeField] private float _ThumbStretchMin= -2.1f;
        public float ThumbStretchMin { get => _ThumbStretchMin; private set => _ThumbStretchMin = value; }

        [Tooltip("엄지 굽힘 muscle 최대값입니다. 값이 너무 높으면 엄지가 뒤로 젖혀질 수 있습니다.")]
        [Range(0f, 2.5f)] [FormerlySerializedAs("ThumbStretchMax")]
        [SerializeField] private float _ThumbStretchMax= 1.0f;
        public float ThumbStretchMax { get => _ThumbStretchMax; private set => _ThumbStretchMax = value; }

        [Tooltip("엄지 벌림 muscle 최소값입니다. 값이 너무 낮으면 엄지가 손 구조상 불가능한 방향으로 벌어질 수 있습니다.")]
        [Range(-1.5f, 0f)] [FormerlySerializedAs("ThumbSpreadMin")]
        [SerializeField] private float _ThumbSpreadMin= -0.9f;
        public float ThumbSpreadMin { get => _ThumbSpreadMin; private set => _ThumbSpreadMin = value; }

        [Tooltip("엄지 벌림 muscle 최대값입니다. 값이 너무 높으면 엄지가 손바닥 바깥 방향으로 과하게 벌어질 수 있습니다.")]
        [Range(0f, 1.5f)] [FormerlySerializedAs("ThumbSpreadMax")]
        [SerializeField] private float _ThumbSpreadMax= 0.9f;
        public float ThumbSpreadMax { get => _ThumbSpreadMax; private set => _ThumbSpreadMax = value; }

        [Tooltip("엄지 해부학적 제한이 실제로 값을 바꿨을 때 최초 1회 진단 로그를 출력합니다.")]
        [FormerlySerializedAs("logThumbAnatomicalGuardCorrections")]
        [SerializeField] private bool _logThumbAnatomicalGuardCorrections= false;
        public bool logThumbAnatomicalGuardCorrections { get => _logThumbAnatomicalGuardCorrections; private set => _logThumbAnatomicalGuardCorrections = value; }

        [Tooltip("엄지 muscle 제한 이후에도 YYB 엄지 본이 손 구조상 이상하게 꺾이면, 실제 엄지 본 localRotation을 기준 자세 근처로 제한합니다.")]
        [FormerlySerializedAs("enableThumbLocalRotationGuard")]
        [SerializeField] private bool _enableThumbLocalRotationGuard= true;
        public bool enableThumbLocalRotationGuard { get => _enableThumbLocalRotationGuard; private set => _enableThumbLocalRotationGuard = value; }

        [Tooltip("Manual Animator finger reference를 사용할 때는 최종 엄지 localRotation 가드를 끄고 수동 기준 손 모양을 우선합니다.")]
        [FormerlySerializedAs("disableThumbLocalRotationGuardWithManualFingerReference")]
        [SerializeField] private bool _disableThumbLocalRotationGuardWithManualFingerReference= true;
        public bool disableThumbLocalRotationGuardWithManualFingerReference { get => _disableThumbLocalRotationGuardWithManualFingerReference; private set => _disableThumbLocalRotationGuardWithManualFingerReference = value; }

        [Tooltip("엄지 첫 번째 관절이 기준 자세에서 벗어날 수 있는 최대 각도입니다. 너무 낮으면 정상적인 엄지 벌림까지 잘려 reference보다 엄지가 덜 펼쳐질 수 있습니다.")]
        [Range(0f, 90f)] [FormerlySerializedAs("ThumbProximalMaxLocalAngle")]
        [SerializeField] private float _ThumbProximalMaxLocalAngle= 28f;
        public float ThumbProximalMaxLocalAngle { get => _ThumbProximalMaxLocalAngle; private set => _ThumbProximalMaxLocalAngle = value; }

        [Tooltip("엄지 두 번째 관절이 기준 자세에서 벗어날 수 있는 최대 각도입니다.")]
        [Range(0f, 120f)] [FormerlySerializedAs("ThumbIntermediateMaxLocalAngle")]
        [SerializeField] private float _ThumbIntermediateMaxLocalAngle= 55f;
        public float ThumbIntermediateMaxLocalAngle { get => _ThumbIntermediateMaxLocalAngle; private set => _ThumbIntermediateMaxLocalAngle = value; }

        [Tooltip("엄지 끝 관절이 기준 자세에서 벗어날 수 있는 최대 각도입니다.")]
        [Range(0f, 120f)] [FormerlySerializedAs("ThumbDistalMaxLocalAngle")]
        [SerializeField] private float _ThumbDistalMaxLocalAngle= 55f;
        public float ThumbDistalMaxLocalAngle { get => _ThumbDistalMaxLocalAngle; private set => _ThumbDistalMaxLocalAngle = value; }

        [Tooltip("엄지 본 localRotation 제한이 실제로 값을 바꿨을 때 최초 1회 진단 로그를 출력합니다.")]
        [FormerlySerializedAs("logThumbLocalRotationGuardCorrections")]
        [SerializeField] private bool _logThumbLocalRotationGuardCorrections= false;
        public bool logThumbLocalRotationGuardCorrections { get => _logThumbLocalRotationGuardCorrections; private set => _logThumbLocalRotationGuardCorrections = value; }

        [Tooltip("YYB처럼 스킨용 Thumb0 보조 본과 Humanoid Thumb0M 본이 분리된 모델에서 보조 본을 실제 엄지 본 회전에 맞춰 손바닥/엄지 뿌리 메시 변형을 줄입니다.")]
        [FormerlySerializedAs("syncDetachedThumbBaseHelpers")]
        [SerializeField] private bool _syncDetachedThumbBaseHelpers= true;
        public bool syncDetachedThumbBaseHelpers { get => _syncDetachedThumbBaseHelpers; private set => _syncDetachedThumbBaseHelpers = value; }

        [Tooltip("분리된 Thumb0 보조본 위치를 실제 Humanoid Thumb0M 본 쪽으로 제한적으로 맞춥니다. YYB 엄지 뿌리 메시가 손바닥에서 분리되어 보이는 현상을 줄입니다.")]
        [FormerlySerializedAs("syncDetachedThumbBaseHelperPositions")]
        [SerializeField] private bool _syncDetachedThumbBaseHelperPositions= true;
        public bool syncDetachedThumbBaseHelperPositions { get => _syncDetachedThumbBaseHelperPositions; private set => _syncDetachedThumbBaseHelperPositions = value; }

        [Tooltip("분리된 Thumb0 보조 본이 실제 엄지 본을 따라가는 비율입니다. 너무 높으면 엄지 뿌리 스킨이 눌려 짧아 보일 수 있어 YYB 기본값은 제한 추종입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("detachedThumbBaseHelperSyncWeight")]
        [SerializeField] private float _detachedThumbBaseHelperSyncWeight= 0.8f;
        public float detachedThumbBaseHelperSyncWeight { get => _detachedThumbBaseHelperSyncWeight; private set => _detachedThumbBaseHelperSyncWeight = value; }

        [Tooltip("Thumb0 보조본이 실제 엄지 구동본을 따라갈 수 있는 최대 각도입니다. 낮출수록 손꿈치 스킨을 덜 움직입니다.")]
        [Range(0f, 45f)] [FormerlySerializedAs("detachedThumbBaseHelperMaxLocalAngle")]
        [SerializeField] private float _detachedThumbBaseHelperMaxLocalAngle= 28f;
        public float detachedThumbBaseHelperMaxLocalAngle { get => _detachedThumbBaseHelperMaxLocalAngle; private set => _detachedThumbBaseHelperMaxLocalAngle = value; }

        [Tooltip("Thumb0 보조본 위치가 기본 손바닥 앵커에서 벗어날 수 있는 최대 거리입니다. 높이면 실제 엄지 구동본 위치를 더 따르고, 낮추면 손바닥 실루엣을 더 보존합니다.")]
        [Range(0f, 0.02f)] [FormerlySerializedAs("detachedThumbBaseHelperMaxPositionOffset")]
        [SerializeField] private float _detachedThumbBaseHelperMaxPositionOffset= 0.008f;
        public float detachedThumbBaseHelperMaxPositionOffset { get => _detachedThumbBaseHelperMaxPositionOffset; private set => _detachedThumbBaseHelperMaxPositionOffset = value; }

        [Tooltip("YYB처럼 joint_*Thumb0 보조본과 !joint_*Thumb0M 실제 엄지 구동본의 로컬 기준축이 어긋난 모델에서, 왼손 Thumb0 보조본이 source delta를 자기 축에 맞게 다시 해석하도록 추가 축 보정을 적용합니다.")]
        [FormerlySerializedAs("LeftDetachedThumbBaseHelperDeltaAxisOffset")]
        [SerializeField] private Vector3 _LeftDetachedThumbBaseHelperDeltaAxisOffset= Vector3.zero;
        public Vector3 LeftDetachedThumbBaseHelperDeltaAxisOffset { get => _LeftDetachedThumbBaseHelperDeltaAxisOffset; private set => _LeftDetachedThumbBaseHelperDeltaAxisOffset = value; }

        [Tooltip("YYB처럼 joint_*Thumb0 보조본과 !joint_*Thumb0M 실제 엄지 구동본의 로컬 기준축이 어긋난 모델에서, 오른손 Thumb0 보조본이 source delta를 자기 축에 맞게 다시 해석하도록 추가 축 보정을 적용합니다.")]
        [FormerlySerializedAs("RightDetachedThumbBaseHelperDeltaAxisOffset")]
        [SerializeField] private Vector3 _RightDetachedThumbBaseHelperDeltaAxisOffset= Vector3.zero;
        public Vector3 RightDetachedThumbBaseHelperDeltaAxisOffset { get => _RightDetachedThumbBaseHelperDeltaAxisOffset; private set => _RightDetachedThumbBaseHelperDeltaAxisOffset = value; }

        [Tooltip("YYB처럼 Thumb0 보조본 기본 자세가 넓게 벌어진 모델에서, 왼손 Thumb0 helper 목표 회전에 정적 보정치를 직접 더해 webbing 벌어짐/각짐을 줄입니다.")]
        [FormerlySerializedAs("LeftDetachedThumbBaseHelperTargetRotationOffset")]
        [SerializeField] private Vector3 _LeftDetachedThumbBaseHelperTargetRotationOffset= Vector3.zero;
        public Vector3 LeftDetachedThumbBaseHelperTargetRotationOffset { get => _LeftDetachedThumbBaseHelperTargetRotationOffset; private set => _LeftDetachedThumbBaseHelperTargetRotationOffset = value; }

        [Tooltip("YYB처럼 Thumb0 보조본 기본 자세가 넓게 벌어진 모델에서, 오른손 Thumb0 helper 목표 회전에 정적 보정치를 직접 더해 webbing 벌어짐/각짐을 줄입니다.")]
        [FormerlySerializedAs("RightDetachedThumbBaseHelperTargetRotationOffset")]
        [SerializeField] private Vector3 _RightDetachedThumbBaseHelperTargetRotationOffset= Vector3.zero;
        public Vector3 RightDetachedThumbBaseHelperTargetRotationOffset { get => _RightDetachedThumbBaseHelperTargetRotationOffset; private set => _RightDetachedThumbBaseHelperTargetRotationOffset = value; }

        [Tooltip("YYB 손꿈치/엄지 뿌리 스킨용 Thumb0 보조본을 기본 손바닥 자세 쪽으로 안정화합니다. 엄지 움직임보다 손바닥 실루엣 보존을 우선합니다.")]
        [FormerlySerializedAs("stabilizeDetachedThumbBasePalm")]
        [SerializeField] private bool _stabilizeDetachedThumbBasePalm= false;
        public bool stabilizeDetachedThumbBasePalm { get => _stabilizeDetachedThumbBasePalm; private set => _stabilizeDetachedThumbBasePalm = value; }

        [Tooltip("Thumb0 보조본을 기본 자세로 되돌리는 강도입니다. 높이면 손꿈치가 고정되고, 낮추면 엄지 뿌리가 실제 엄지 구동본을 더 따라갑니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("detachedThumbBasePalmStabilizeWeight")]
        [SerializeField] private float _detachedThumbBasePalmStabilizeWeight= 0f;
        public float detachedThumbBasePalmStabilizeWeight { get => _detachedThumbBasePalmStabilizeWeight; private set => _detachedThumbBasePalmStabilizeWeight = value; }

        [Tooltip("손꿈치 안정화 상태에서 Thumb0 보조본이 기본 자세에서 벗어날 수 있는 최대 각도입니다. 낮을수록 엄지 뿌리 메시가 덜 벌어집니다.")]
        [Range(0f, 45f)] [FormerlySerializedAs("detachedThumbBasePalmMaxLocalAngle")]
        [SerializeField] private float _detachedThumbBasePalmMaxLocalAngle= 45f;
        public float detachedThumbBasePalmMaxLocalAngle { get => _detachedThumbBasePalmMaxLocalAngle; private set => _detachedThumbBasePalmMaxLocalAngle = value; }

        [Tooltip("YYB 엄지와 손바닥 경계선이 딱딱하게 찢겨 보일 때 Thumb0 보조본을 기본 손바닥 웹빙 형태 쪽으로 약하게 안정화합니다.")]
        [FormerlySerializedAs("stabilizeThumbWebbingCrease")]
        [SerializeField] private bool _stabilizeThumbWebbingCrease= true;
        public bool stabilizeThumbWebbingCrease { get => _stabilizeThumbWebbingCrease; private set => _stabilizeThumbWebbingCrease = value; }

        [Tooltip("엄지 웹빙 라인을 안정화하는 강도입니다. 높이면 엄지-손바닥 경계가 덜 찢기지만 엄지 뿌리 움직임이 둔해질 수 있습니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("thumbWebbingCreaseStabilizeWeight")]
        [SerializeField] private float _thumbWebbingCreaseStabilizeWeight= 0.35f;
        public float thumbWebbingCreaseStabilizeWeight { get => _thumbWebbingCreaseStabilizeWeight; private set => _thumbWebbingCreaseStabilizeWeight = value; }

        [Tooltip("엄지 웹빙 안정화 상태에서 Thumb0 보조본이 기본 자세에서 벗어날 수 있는 최대 회전각입니다.")]
        [Range(0f, 45f)] [FormerlySerializedAs("thumbWebbingCreaseMaxLocalAngle")]
        [SerializeField] private float _thumbWebbingCreaseMaxLocalAngle= 18f;
        public float thumbWebbingCreaseMaxLocalAngle { get => _thumbWebbingCreaseMaxLocalAngle; private set => _thumbWebbingCreaseMaxLocalAngle = value; }

        [Tooltip("엄지 웹빙 안정화 상태에서 Thumb0 보조본 위치가 기본 위치에서 벗어날 수 있는 최대 거리입니다.")]
        [Range(0f, 0.02f)] [FormerlySerializedAs("thumbWebbingCreaseMaxPositionOffset")]
        [SerializeField] private float _thumbWebbingCreaseMaxPositionOffset= 0.005f;
        public float thumbWebbingCreaseMaxPositionOffset { get => _thumbWebbingCreaseMaxPositionOffset; private set => _thumbWebbingCreaseMaxPositionOffset = value; }

        [Tooltip("엄지가 손바닥 normal 방향으로 너무 서거나 중간 관절이 과하게 말려 화면상 짧아 보이는 현상을 줄입니다.")]
        [FormerlySerializedAs("enableThumbVisualLengthGuard")]
        [SerializeField] private bool _enableThumbVisualLengthGuard= true;
        public bool enableThumbVisualLengthGuard { get => _enableThumbVisualLengthGuard; private set => _enableThumbVisualLengthGuard = value; }

        [Tooltip("엄지 첫 마디가 손바닥 normal 방향으로 최소한 앞으로 나와야 하는 성분입니다. 너무 낮으면 엄지가 손바닥 뒤로 누운 것처럼 보입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("ThumbProjectionMinPalmNormal")]
        [SerializeField] private float _ThumbProjectionMinPalmNormal= DEFAULT_THUMB_PROJECTION_MIN_PALM_NORMAL;
        public float ThumbProjectionMinPalmNormal { get => _ThumbProjectionMinPalmNormal; private set => _ThumbProjectionMinPalmNormal = value; }

        [Tooltip("엄지 첫 마디가 손바닥 normal 방향으로 나갈 수 있는 최대 성분입니다. 너무 높으면 카메라 정면에서 엄지가 짧게 보입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("ThumbProjectionMaxPalmNormal")]
        [SerializeField] private float _ThumbProjectionMaxPalmNormal= 0.58f;
        public float ThumbProjectionMaxPalmNormal { get => _ThumbProjectionMaxPalmNormal; private set => _ThumbProjectionMaxPalmNormal = value; }

        [Tooltip("엄지 첫 마디 투영 보정 강도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("ThumbProjectionGuardWeight")]
        [SerializeField] private float _ThumbProjectionGuardWeight= 1f;
        public float ThumbProjectionGuardWeight { get => _ThumbProjectionGuardWeight; private set => _ThumbProjectionGuardWeight = value; }

        [Tooltip("엄지 첫 마디와 검지 시작 방향 사이의 최대 벌어짐 각도입니다. 높으면 엄지가 손바닥 바깥으로 과하게 벌어질 수 있습니다.")]
        [Range(0f, 90f)] [FormerlySerializedAs("ThumbIndexMaxSpreadAngle")]
        [SerializeField] private float _ThumbIndexMaxSpreadAngle= 70f;
        public float ThumbIndexMaxSpreadAngle { get => _ThumbIndexMaxSpreadAngle; private set => _ThumbIndexMaxSpreadAngle = value; }

        [Tooltip("엄지-검지 벌어짐 제한 강도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("ThumbIndexSpreadGuardWeight")]
        [SerializeField] private float _ThumbIndexSpreadGuardWeight= 1f;
        public float ThumbIndexSpreadGuardWeight { get => _ThumbIndexSpreadGuardWeight; private set => _ThumbIndexSpreadGuardWeight = value; }

        [Tooltip("엄지 첫 마디와 둘째 마디 사이 허용 굽힘 각도입니다. 이 값을 넘으면 끝 마디를 더 펴서 짧아 보이는 현상을 줄입니다.")]
        [Range(0f, 60f)] [FormerlySerializedAs("ThumbMaxSegmentBendAngle")]
        [SerializeField] private float _ThumbMaxSegmentBendAngle= 10f;
        public float ThumbMaxSegmentBendAngle { get => _ThumbMaxSegmentBendAngle; private set => _ThumbMaxSegmentBendAngle = value; }

        [Tooltip("엄지 둘째 마디를 첫 마디 방향에 맞춰 펴는 강도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("ThumbSegmentStraightenWeight")]
        [SerializeField] private float _ThumbSegmentStraightenWeight= 0.9f;
        public float ThumbSegmentStraightenWeight { get => _ThumbSegmentStraightenWeight; private set => _ThumbSegmentStraightenWeight = value; }

        [Header("VMD Recording Orchestration")]
        [Tooltip("FBX 로드 후 녹화 시작까지의 대기 시간 (초)")]
        [Range(0f, 10f)] [FormerlySerializedAs("startDelay")]
        [SerializeField] private float _startDelay= 3.0f;
        public float startDelay { get => _startDelay; set => _startDelay = value; }

        [Tooltip("VMD recording playback speed. 1 is normal speed.")]
        [Range(0.1f, 4f)] [FormerlySerializedAs("vmdRecordingPlaybackSpeed")]
        [SerializeField] private float _vmdRecordingPlaybackSpeed= 1f;
        public float vmdRecordingPlaybackSpeed { get => _vmdRecordingPlaybackSpeed; private set => _vmdRecordingPlaybackSpeed = value; }

        [Tooltip("Opt-in legacy reference timing for satisfaction_2 regression evidence. Off by default so normal VMD export records at 1x.")]
        [FormerlySerializedAs("useKnownMmdReferenceTiming")]
        [SerializeField] private bool _useKnownMmdReferenceTiming= false;
        public bool useKnownMmdReferenceTiming { get => _useKnownMmdReferenceTiming; private set => _useKnownMmdReferenceTiming = value; }

        [Tooltip("녹화 시작 직전에 clip time 0을 고정 샘플링해 retarget/grounding 첫 프레임을 안정화하는 프레임 수입니다.")]
        [Range(0, MAX_RETARGET_PREWARM_FRAME_COUNT)] [FormerlySerializedAs("RetargetPrewarmFrameCount")]
        [SerializeField] private int _RetargetPrewarmFrameCount= 6;
        public int RetargetPrewarmFrameCount { get => _RetargetPrewarmFrameCount; private set => _RetargetPrewarmFrameCount = value; }

        [Tooltip("VMD 저장이 성공하면 추가로 이 폴더에도 같은 VMD 파일을 복사합니다. 비워두면 복사하지 않습니다. (예: C:/Users/flzhv/Desktop/MMD/MikuMikuDance_v932x64/SaveFile)")]
        [FormerlySerializedAs("additionalVmdCopyFolder")]
        [SerializeField] private string _additionalVmdCopyFolder= "";
        public string additionalVmdCopyFolder { get => _additionalVmdCopyFolder; private set => _additionalVmdCopyFolder = value; }

        [Header("영상 녹화 설정")]
        [Tooltip("녹화 버튼으로 저장하는 영상의 출력 폴더입니다. 비워두면 프로젝트 루트의 Recordings 폴더를 사용합니다.")]
        [SerializeField] private string _motionVideoOutputFolder= "";
        public string motionVideoOutputFolder { get => _motionVideoOutputFolder; private set => _motionVideoOutputFolder = value; }

        [Tooltip("녹화 영상의 컨테이너/코덱입니다. WebM은 투명 배경(알파)을 지원해 영상 편집 시 누끼 작업 없이 캐릭터만 합성할 수 있습니다.")]
        [SerializeField] private MotionVideoFileFormat _motionVideoFormat= MotionVideoFileFormat.WebM;
        public MotionVideoFileFormat motionVideoFormat { get => _motionVideoFormat; private set => _motionVideoFormat = value; }

        [Tooltip("녹화 영상의 프레임률입니다. 녹화 타이밍과 모션 진행 단위에 함께 사용됩니다.")]
        [SerializeField] private MotionVideoFrameRate _motionVideoFrameRate= MotionVideoFrameRate.Fps60;
        public MotionVideoFrameRate motionVideoFrameRate { get => _motionVideoFrameRate; private set => _motionVideoFrameRate = value; }

        [Tooltip("MOV/WebM(배경 투명) 녹화 중에만 숨길 오브젝트입니다. 캐릭터만 남기려면 바닥 Plane 등을 지정합니다.")]
        [SerializeField] private GameObject[] _alphaRecordingHiddenObjects= null;
        public GameObject[] alphaRecordingHiddenObjects { get => _alphaRecordingHiddenObjects; private set => _alphaRecordingHiddenObjects = value; }

        [Tooltip("켜면 클립 시작부의 완전 정지(준비 자세) 구간을 자동으로 건너뛰고 첫 움직임부터 녹화합니다. 인트로 홀드를 영상에 남기려면 끕니다. 구간 녹화가 켜져 있으면 지정 프레임이 우선합니다.")]
        [SerializeField] private bool _motionVideoSkipLeadingStill= true;
        public bool motionVideoSkipLeadingStill { get => _motionVideoSkipLeadingStill; private set => _motionVideoSkipLeadingStill = value; }

        [Tooltip("켜면 시작~종료 프레임 구간만 영상으로 녹화합니다. 프레임 번호는 클립 FPS 기준 모션 프레임입니다.")]
        [SerializeField] private bool _motionVideoRangeEnabled= false;
        public bool motionVideoRangeEnabled { get => _motionVideoRangeEnabled; private set => _motionVideoRangeEnabled = value; }

        [Tooltip("녹화 시작 모션 프레임입니다(포함). 0이면 클립 처음부터입니다.")]
        [Min(0)]
        [SerializeField] private int _motionVideoRangeStartFrame= 0;
        public int motionVideoRangeStartFrame { get => _motionVideoRangeStartFrame; private set => _motionVideoRangeStartFrame = value; }

        [Tooltip("녹화 종료 모션 프레임입니다(포함). 0이거나 클립 끝을 넘으면 끝까지 녹화합니다.")]
        [Min(0)]
        [SerializeField] private int _motionVideoRangeEndFrame= 0;
        public int motionVideoRangeEndFrame { get => _motionVideoRangeEndFrame; private set => _motionVideoRangeEndFrame = value; }

        [Tooltip("비교 CSV/프레임 캡처 Probe를 켭니다. 일반 변환에서는 미세 멈춤을 줄이기 위해 끄고, 회귀 테스트 때만 켭니다.")]
        [HideInInspector]
        [FormerlySerializedAs("enableRecordingDiagnostics")]
        [SerializeField] private bool _enableRecordingDiagnostics= false;
        public bool enableRecordingDiagnostics { get => _enableRecordingDiagnostics; set => _enableRecordingDiagnostics = value; }

        [Tooltip("회귀 테스트 때 녹화 중 Unity 시간을 30fps로 고정합니다. 일반 GameView 재생에서는 배속/멈칫 체감이 생길 수 있어 끕니다.")]
        [HideInInspector]
        [FormerlySerializedAs("useDeterministicCaptureFramerateForDiagnostics")]
        [SerializeField] private bool _useDeterministicCaptureFramerateForDiagnostics= false;
        public bool useDeterministicCaptureFramerateForDiagnostics { get => _useDeterministicCaptureFramerateForDiagnostics; set => _useDeterministicCaptureFramerateForDiagnostics = value; }

        [Tooltip("Recording Diagnostics를 켰을 때 손 close-up 캡처도 함께 남깁니다.")]
        [HideInInspector]
        [FormerlySerializedAs("enableDiagnosticFingerCloseups")]
        [SerializeField] private bool _enableDiagnosticFingerCloseups= true;
        public bool enableDiagnosticFingerCloseups { get => _enableDiagnosticFingerCloseups; set => _enableDiagnosticFingerCloseups = value; }

        [Tooltip("MotionComparisonProbe PNG 캡처 해상도 preset입니다. VMD frame/key 품질에는 영향을 주지 않습니다.")]
        [HideInInspector]
        [FormerlySerializedAs("recordingCaptureQuality")]
        [SerializeField] private RecordingCaptureQualityPreset _recordingCaptureQuality= RecordingCaptureQualityPreset.Existing960Square;
        public RecordingCaptureQualityPreset recordingCaptureQuality { get => _recordingCaptureQuality; set => _recordingCaptureQuality = value; }

        [Tooltip("Custom preset을 선택했을 때 사용할 PNG 캡처 폭입니다.")]
        [HideInInspector]
        [FormerlySerializedAs("customRecordingCaptureWidth")]
        [SerializeField] private int _customRecordingCaptureWidth= 3840;
        public int customRecordingCaptureWidth { get => _customRecordingCaptureWidth; set => _customRecordingCaptureWidth = value; }

        [Tooltip("Custom preset을 선택했을 때 사용할 PNG 캡처 높이입니다.")]
        [HideInInspector]
        [FormerlySerializedAs("customRecordingCaptureHeight")]
        [SerializeField] private int _customRecordingCaptureHeight= 2160;
        public int customRecordingCaptureHeight { get => _customRecordingCaptureHeight; set => _customRecordingCaptureHeight = value; }

        public RecordingDiagnosticsSettings DiagnosticsSettings
        {
            get => new RecordingDiagnosticsSettings(
                enableRecordingDiagnostics,
                useDeterministicCaptureFramerateForDiagnostics,
                enableDiagnosticFingerCloseups,
                recordingCaptureQuality,
                customRecordingCaptureWidth,
                customRecordingCaptureHeight);
            set
            {
                enableRecordingDiagnostics = value.EnableRecordingDiagnostics;
                useDeterministicCaptureFramerateForDiagnostics =
                    value.UseDeterministicCaptureFramerateForDiagnostics;
                enableDiagnosticFingerCloseups = value.EnableDiagnosticFingerCloseups;
                recordingCaptureQuality = value.CaptureQuality;
                customRecordingCaptureWidth = value.CustomCaptureWidth;
                customRecordingCaptureHeight = value.CustomCaptureHeight;
            }
        }

        [Tooltip("Editor smoke에서 MotionComparisonProbe 엄지 리스크가 임계치를 넘으면 VMD 저장 성공도 smoke 실패로 승격합니다.")]
        [FormerlySerializedAs("failEditorSmokeOnThumbRisk")]
        [SerializeField] private bool _failEditorSmokeOnThumbRisk= true;
        public bool failEditorSmokeOnThumbRisk { get => _failEditorSmokeOnThumbRisk; private set => _failEditorSmokeOnThumbRisk = value; }

        [Tooltip("Editor smoke에서 일반 FBX용 엄지 해부학 리스크의 허용 최대값입니다. 이 값을 넘으면 smoke를 실패로 봅니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("editorSmokeMaxGenericThumbAnatomyRisk")]
        [SerializeField] private float _editorSmokeMaxGenericThumbAnatomyRisk= 0.4f;
        public float editorSmokeMaxGenericThumbAnatomyRisk { get => _editorSmokeMaxGenericThumbAnatomyRisk; private set => _editorSmokeMaxGenericThumbAnatomyRisk = value; }

        [Tooltip("Editor smoke에서 YYB 전용 변형 리스크의 허용 최대값입니다. YYB 타깃일 때만 사용합니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("editorSmokeMaxYybDeformationRisk")]
        [SerializeField] private float _editorSmokeMaxYybDeformationRisk= 0.35f;
        public float editorSmokeMaxYybDeformationRisk { get => _editorSmokeMaxYybDeformationRisk; private set => _editorSmokeMaxYybDeformationRisk = value; }

        [Header("Visual Jitter Guard")]
        [Tooltip("Editor/GameView 프레임이 밀려도 Ghost clip time이 한 프레임에 크게 건너뛰지 않게 제한합니다.")]
        [FormerlySerializedAs("clampRetargetVisualClipStep")]
        [SerializeField] private bool _clampRetargetVisualClipStep= false;
        public bool clampRetargetVisualClipStep { get => _clampRetargetVisualClipStep; private set => _clampRetargetVisualClipStep = value; }

        [Tooltip("Ghost clip time이 한 렌더 프레임에 전진할 수 있는 기준 FPS입니다. 30이면 한 번에 1/30초 이상 건너뛰지 않습니다.")]
        [Range(15f, 120f)] [FormerlySerializedAs("RetargetVisualClipFrameRate")]
        [SerializeField] private float _RetargetVisualClipFrameRate= 30f;
        public float RetargetVisualClipFrameRate { get => _RetargetVisualClipFrameRate; private set => _RetargetVisualClipFrameRate = value; }

        [Tooltip("프레임 지연으로 retarget pose가 한 번에 크게 바뀔 때 애니메이션 시간은 보존하고 target pose만 부드럽게 따라가게 합니다.")]
        [FormerlySerializedAs("smoothRetargetPoseOnVisualStepSpike")]
        [SerializeField] private bool _smoothRetargetPoseOnVisualStepSpike= true;
        public bool smoothRetargetPoseOnVisualStepSpike { get => _smoothRetargetPoseOnVisualStepSpike; set => _smoothRetargetPoseOnVisualStepSpike = value; }

        [Tooltip("pose spike smoothing 때 현재 FBX pose를 반영하는 비율입니다. 1에 가까울수록 원본 모션을 더 보존하고, 낮을수록 pop을 더 줄입니다.")]
        [Range(0.1f, 1f)] [FormerlySerializedAs("RetargetPoseVisualSpikeCurrentWeight")]
        [SerializeField] private float _RetargetPoseVisualSpikeCurrentWeight= 0.65f;
        public float RetargetPoseVisualSpikeCurrentWeight { get => _RetargetPoseVisualSpikeCurrentWeight; set => _RetargetPoseVisualSpikeCurrentWeight = value; }

        [Tooltip("Optional forearm stretch clamp around the current pose during visual spike smoothing. 0 disables the clamp.")]
        [Range(0f, 1f)] [FormerlySerializedAs("RetargetPoseVisualSpikeForearmStretchClampMaxOffset")]
        [SerializeField] private float _RetargetPoseVisualSpikeForearmStretchClampMaxOffset= 0f;
        public float RetargetPoseVisualSpikeForearmStretchClampMaxOffset { get => _RetargetPoseVisualSpikeForearmStretchClampMaxOffset; set => _RetargetPoseVisualSpikeForearmStretchClampMaxOffset = value; }

        [Tooltip("이 값보다 큰 muscle delta가 발생하면 frame-time spike가 아니어도 pose smoothing을 적용합니다.")]
        [Range(0.05f, 1f)] [FormerlySerializedAs("RetargetPoseVisualMuscleDeltaThreshold")]
        [SerializeField] private float _RetargetPoseVisualMuscleDeltaThreshold= 0.35f;
        public float RetargetPoseVisualMuscleDeltaThreshold { get => _RetargetPoseVisualMuscleDeltaThreshold; private set => _RetargetPoseVisualMuscleDeltaThreshold = value; }

    }
}
