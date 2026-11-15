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
        // 재생 컨트롤 표시 요청은 상위(UI) 도메인이 구독해 처리한다 — 파이프라인이 뷰 타입을 알지 못하게 한다.
        public static event Action<FBXVmdPipeline> PlaybackControlsEnsureRequested;

        // 에디터 녹화기 구현은 에디터 도메인이 등록한다 — 파이프라인이 에디터 타입을 알지 못하게 한다.
        public static Func<IMotionVideoRecorder> EditorMotionVideoRecorderFactory;

        internal const string IMPORT_FBX_FOLDER = "Import_FBX";
        internal const string FBX_EXTENSION = "fbx";
        private const float THUMB_PROXIMAL_SAFE_MAX_LOCAL_ANGLE = 30f;
        private const float DEFAULT_THUMB_STRETCH_OFFSET = -0.1f;
        private const float LEGACY_THUMB_PROJECTION_MIN_PALM_NORMAL = 0.36f;
        private const float DEFAULT_THUMB_PROJECTION_MIN_PALM_NORMAL = 0.358f;
        internal const float MMD_REFERENCE_FRAME_RATE = 30f;
        internal const int MAX_RETARGET_PREWARM_FRAME_COUNT = 120;
#if UNITY_EDITOR
        internal const float EDITOR_DIAGNOSTIC_SMOKE_FRAME_RATE = 30f;
#endif
        private static Func<IFileBrowserService> fileBrowserServiceFactory = () => new FileBrowserService();
        private static Func<AssimpFBXImporter> fbxImporterFactory = () => new AssimpFBXImporter();

        internal enum FBXSessionState
        {
            Idle,
            Selected,
            Copied,
            LoadingFbx,
            AvatarReady,
            Ready,
            PreviewPlaying,
            PreviewPaused,
            GhostReady,
            Retargeting,
            Recording,
            Saving,
            Success,
            Failed,
            Cancelled
        }

        private enum NativeSkinningPendingPlaybackRequest
        {
            None,
            Play,
            Record
        }


        #region Public 필드
        [Header("FBX 임포트 설정")]
        [Tooltip("체크 시 선택한 FBX 파일을 Import_FBX 폴더에 복사하여 저장")]
        [FormerlySerializedAs("saveToImportFolder")] [SerializeField] private bool _shouldSaveToImportFolder = false;

        [Tooltip("진단 세션에서 FBX 임포트 후 VMD 녹화와 저장을 실행합니다. 일반 에디터 임포트는 이 값과 무관하게 첫 프레임에서 대기합니다.")]
        [FormerlySerializedAs("recordVmdAfterImport")] [SerializeField] private bool _shouldRecordVmdAfterImport;

        [Header("Ghost Retargeting 설정")]
        [Tooltip("애니메이션을 적용할 대상 캐릭터 (Humanoid Avatar 필요)")]
        [FormerlySerializedAs("targetCharacter")]
        [SerializeField] private GameObject _targetCharacter;
        public GameObject targetCharacter
        {
            get => _targetCharacter;
            set
            {
                _targetCharacter = value;
                DisableIndependentRecorderAutoStart();
            }
        }

        [Tooltip("이전 수동 프로젝트와 같은 180도 PoseSpace 방향 보정을 사용합니다. 현재 씬에서는 카메라 정면 조건을 깨므로 비교/롤백용으로만 켭니다.")]
        [FormerlySerializedAs("useLegacyPoseSpaceFacingCorrection")] [SerializeField] private bool _shouldUseLegacyPoseSpaceFacingCorrection = false;

        [Tooltip("자동 경로가 Sub_Manual 직접 Animator 재생처럼 FBX의 body/root 회전을 따르도록 합니다. 끄면 기존처럼 Ghost Transform 기준 회전 보정을 강제합니다.")]
        [FormerlySerializedAs("preserveFbxRootRotation")] [SerializeField] private bool _shouldPreserveFbxRootRotation = false;

        [Tooltip("HumanPose bodyPosition copied from FBX can jump on some clips. Keep the target body position stable like the manual Animator path.")]
        [FormerlySerializedAs("preserveRetargetBodyPosition")] [SerializeField] private bool _shouldPreserveRetargetBodyPosition = true;

        [Tooltip("Use FBX HumanPose bodyPosition X/Z delta as target root motion to reduce foot sliding.")]
        [FormerlySerializedAs("useRetargetBodyPositionXZRootMotion")] [SerializeField] private bool _shouldUseRetargetBodyPositionXZRootMotion = false;

        [Tooltip("When a foot is visually grounded, add a small X/Z root correction to reduce skating.")]
        [FormerlySerializedAs("stabilizeGroundedFootXZ")] [SerializeField] private bool _shouldStabilizeGroundedFootXZ = false;

        [Tooltip("Foot-lock correction strength. Lower values preserve dance motion, higher values reduce skating.")]
        [Range(0f, 1f)] [FormerlySerializedAs("GroundedFootLockWeight")]
        [SerializeField] private float _GroundedFootLockWeight= 0.45f;
        public float GroundedFootLockWeight { get => _GroundedFootLockWeight; private set => _GroundedFootLockWeight = value; }

        [Tooltip("Maximum X/Z root correction per frame for grounded foot lock.")]
        [Range(0.001f, 0.1f)] [FormerlySerializedAs("MaxGroundedFootLockStep")]
        [SerializeField] private float _MaxGroundedFootLockStep= 0.025f;
        public float MaxGroundedFootLockStep { get => _MaxGroundedFootLockStep; private set => _MaxGroundedFootLockStep = value; }

        [Tooltip("Editor 자동 경로에서 Unity가 임포트한 Humanoid clip의 muscle curve를 기준으로 사용합니다. Assimp Ghost 회전 curve가 수동 기준과 다를 때 팔/상체 포즈 차이를 줄이기 위한 안전 경로입니다.")]
        [FormerlySerializedAs("useEditorHumanoidClipMuscleReference")] [SerializeField] private bool _shouldUseEditorHumanoidClipMuscleReference = true;

        [Tooltip("Editor-only experimental RootT X/Z root motion reference. Keep disabled until visual_body_arc_jitter passes without increasing jitter.")]
        [FormerlySerializedAs("useEditorHumanoidRootTranslationReference")] [SerializeField] private bool _shouldUseEditorHumanoidRootTranslationReference = false;

        [Tooltip("Weight for Editor Humanoid RootT translation reference.")]
        [Range(0f, 1f)] [FormerlySerializedAs("editorHumanoidRootTranslationWeight")]
        [SerializeField] private float _editorHumanoidRootTranslationWeight= 0.25f;
        public float editorHumanoidRootTranslationWeight { get => _editorHumanoidRootTranslationWeight; private set => _editorHumanoidRootTranslationWeight = value; }

        [Tooltip("Current-frame blend for smoothed Editor Humanoid RootT translation delta.")]
        [Range(0.05f, 1f)] [FormerlySerializedAs("editorHumanoidRootTranslationCurrentWeight")]
        [SerializeField] private float _editorHumanoidRootTranslationCurrentWeight= 0.35f;
        public float editorHumanoidRootTranslationCurrentWeight { get => _editorHumanoidRootTranslationCurrentWeight; private set => _editorHumanoidRootTranslationCurrentWeight = value; }

        [Tooltip("손가락은 Sub_Manual/testPrefab Animator가 평가한 HumanPose 값을 기준으로 덮어씁니다.")]
        [FormerlySerializedAs("useManualAnimatorFingerPoseReference")] [SerializeField] private bool _shouldUseManualAnimatorFingerPoseReference = false;

        [FormerlySerializedAs("useManualAnimatorFullBodyPoseReference")] [SerializeField] private bool _shouldUseManualAnimatorFullBodyPoseReference = false;

        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorFullBodyPoseReferenceWeight")]
        [SerializeField] private float _manualAnimatorFullBodyPoseReferenceWeight= 1f;
        public float manualAnimatorFullBodyPoseReferenceWeight { get => _manualAnimatorFullBodyPoseReferenceWeight; set => _manualAnimatorFullBodyPoseReferenceWeight = value; }

        [Tooltip("Runtime diagnostic: keep the manual Animator full-body reference active but skip lower-body muscles.")]
        [FormerlySerializedAs("manualAnimatorFullBodyPoseExcludeLowerBodyMuscles")] [SerializeField] private bool _shouldExcludeManualAnimatorFullBodyLowerMuscles = false;

        [Tooltip("Runtime diagnostic: apply the manual Animator full-body reference only to lower-body muscles.")]
        [FormerlySerializedAs("manualAnimatorFullBodyPoseLowerBodyMusclesOnly")] [SerializeField] private bool _shouldApplyManualAnimatorFullBodyLowerMusclesOnly = false;

        [Tooltip("Runtime diagnostic: apply the manual Animator full-body reference only to leg in-out/twist muscles.")]
        [FormerlySerializedAs("manualAnimatorFullBodyPoseLegTwistMusclesOnly")] [SerializeField] private bool _shouldApplyManualAnimatorFullBodyLegTwistMusclesOnly = false;

        [Tooltip("Runtime diagnostic: apply the manual Animator full-body reference only to right arm and shoulder muscles.")]
        [FormerlySerializedAs("manualAnimatorFullBodyPoseRightArmMusclesOnly")]
        [SerializeField] private bool _manualAnimatorFullBodyPoseRightArmMusclesOnly= false;
        public bool manualAnimatorFullBodyPoseRightArmMusclesOnly { get => _manualAnimatorFullBodyPoseRightArmMusclesOnly; set => _manualAnimatorFullBodyPoseRightArmMusclesOnly = value; }

        [Tooltip("Runtime diagnostic: apply the manual Animator full-body reference only to left arm and shoulder muscles.")]
        [FormerlySerializedAs("manualAnimatorFullBodyPoseLeftArmMusclesOnly")]
        [SerializeField] private bool _manualAnimatorFullBodyPoseLeftArmMusclesOnly= false;
        public bool manualAnimatorFullBodyPoseLeftArmMusclesOnly { get => _manualAnimatorFullBodyPoseLeftArmMusclesOnly; set => _manualAnimatorFullBodyPoseLeftArmMusclesOnly = value; }

        [Tooltip("Runtime diagnostic: apply the manual Animator full-body reference only to spine and right sleeve chain muscles.")]
        [FormerlySerializedAs("manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly")]
        [SerializeField] private bool _manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly= false;
        public bool manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly { get => _manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly; set => _manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly = value; }

        [Tooltip("Runtime diagnostic: first recorder frame for manual Animator full-body pose reference. Zero with end zero keeps the full clip.")]
        [Range(0f, 6000f)] [FormerlySerializedAs("manualAnimatorFullBodyPoseFrameGateStart")]
        [SerializeField] private float _manualAnimatorFullBodyPoseFrameGateStart= 0f;
        public float manualAnimatorFullBodyPoseFrameGateStart { get => _manualAnimatorFullBodyPoseFrameGateStart; set => _manualAnimatorFullBodyPoseFrameGateStart = value; }

        [Tooltip("Runtime diagnostic: last recorder frame for manual Animator full-body pose reference. Zero with start zero keeps the full clip.")]
        [Range(0f, 6000f)] [FormerlySerializedAs("manualAnimatorFullBodyPoseFrameGateEnd")]
        [SerializeField] private float _manualAnimatorFullBodyPoseFrameGateEnd= 0f;
        public float manualAnimatorFullBodyPoseFrameGateEnd { get => _manualAnimatorFullBodyPoseFrameGateEnd; set => _manualAnimatorFullBodyPoseFrameGateEnd = value; }

        [Tooltip("Runtime diagnostic: after SetHumanPose, blend only right upper/lower leg twist output muscles back toward the solver input within a small cap.")]
        [FormerlySerializedAs("useSetHumanPoseRightLegTwistOutputReference")] [SerializeField] private bool _shouldUseSetHumanPoseRightLegTwistOutputReference = false;

        [Tooltip("Blend weight for bounded right leg twist output preservation after SetHumanPose.")]
        [Range(0f, 1f)] [FormerlySerializedAs("setHumanPoseRightLegTwistOutputReferenceWeight")]
        [SerializeField] private float _setHumanPoseRightLegTwistOutputReferenceWeight= 1f;
        public float setHumanPoseRightLegTwistOutputReferenceWeight { get => _setHumanPoseRightLegTwistOutputReferenceWeight; set => _setHumanPoseRightLegTwistOutputReferenceWeight = value; }

        [Tooltip("Maximum SetHumanPose right leg twist output correction per muscle.")]
        [Range(0f, 0.1f)] [FormerlySerializedAs("setHumanPoseRightLegTwistOutputReferenceMaxDelta")]
        [SerializeField] private float _setHumanPoseRightLegTwistOutputReferenceMaxDelta= 0.02f;
        public float setHumanPoseRightLegTwistOutputReferenceMaxDelta { get => _setHumanPoseRightLegTwistOutputReferenceMaxDelta; set => _setHumanPoseRightLegTwistOutputReferenceMaxDelta = value; }

        [Tooltip("Sub_Manual/testPrefab Animator의 HumanPose bodyRotation을 retarget pose 기준으로 사용해 팔꿈치 bend plane 기준축 차이를 줄입니다.")]
        [FormerlySerializedAs("useManualAnimatorBodyRotationReference")] [SerializeField] private bool _shouldUseManualAnimatorBodyRotationReference = false;

        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorBodyRotationReferenceWeight")]
        [SerializeField] private float _manualAnimatorBodyRotationReferenceWeight= 1f;
        public float manualAnimatorBodyRotationReferenceWeight { get => _manualAnimatorBodyRotationReferenceWeight; set => _manualAnimatorBodyRotationReferenceWeight = value; }

        [Tooltip("preserveRetargetBodyPosition=true 일 때 body Y를 수동 기준 Animator bodyPosition.y로 대체합니다. ghost Legacy-animation bodyPos.y 스파이크 없이 상체 높이를 애니메이션에 맞게 따라가도록 합니다.")]
        [FormerlySerializedAs("useManualAnimatorBodyPositionYReference")] [SerializeField] private bool _shouldUseManualAnimatorBodyPositionYReference = false;

        [Tooltip("Runtime diagnostic: blend HumanPose bodyPosition X/Z toward the manual Animator reference before SetHumanPose.")]
        [FormerlySerializedAs("useManualAnimatorBodyPositionXzReference")] [SerializeField] private bool _shouldUseManualAnimatorBodyPositionXzReference = false;

        [Tooltip("Blend weight for the manual Animator bodyPosition X/Z solver-input reference.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorBodyPositionXzReferenceWeight")]
        [SerializeField] private float _manualAnimatorBodyPositionXzReferenceWeight= 1f;
        public float manualAnimatorBodyPositionXzReferenceWeight { get => _manualAnimatorBodyPositionXzReferenceWeight; set => _manualAnimatorBodyPositionXzReferenceWeight = value; }

        [Tooltip("Maximum per-frame bodyPosition X/Z offset for the manual Animator solver-input reference.")]
        [Range(0f, 0.2f)] [FormerlySerializedAs("manualAnimatorBodyPositionXzReferenceMaxOffset")]
        [SerializeField] private float _manualAnimatorBodyPositionXzReferenceMaxOffset= 0.025f;
        public float manualAnimatorBodyPositionXzReferenceMaxOffset { get => _manualAnimatorBodyPositionXzReferenceMaxOffset; set => _manualAnimatorBodyPositionXzReferenceMaxOffset = value; }

        [Tooltip("Runtime diagnostic start recorder frame for manual Animator bodyPosition X/Z reference. Zero disables frame gating.")]
        [Range(0f, 6000f)] [FormerlySerializedAs("manualAnimatorBodyPositionXzReferenceFrameGateStart")]
        [SerializeField] private float _manualAnimatorBodyPositionXzReferenceFrameGateStart= 0f;
        public float manualAnimatorBodyPositionXzReferenceFrameGateStart { get => _manualAnimatorBodyPositionXzReferenceFrameGateStart; set => _manualAnimatorBodyPositionXzReferenceFrameGateStart = value; }

        [Tooltip("Runtime diagnostic end recorder frame for manual Animator bodyPosition X/Z reference. Zero disables frame gating.")]
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

        [Tooltip("수동 기준 Animator의 Hips localPosition을 target Hips에 선택적으로 적용해 자동 경로의 몸통 경로 편차를 A/B 검증합니다. 활성 시 testprefab Hips delta가 YYB에 전달되어 오히려 발 호 궤적이 심해지므로 기본 비활성화합니다.")]
        [FormerlySerializedAs("useManualAnimatorHipsLocalPositionReference")] [SerializeField] private bool _shouldUseManualAnimatorHipsLocalPositionReference = false;

        [Tooltip("수동 기준 Hips localPosition 보정 강도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorHipsLocalPositionWeight")]
        [SerializeField] private float _manualAnimatorHipsLocalPositionWeight= 1f;
        public float manualAnimatorHipsLocalPositionWeight { get => _manualAnimatorHipsLocalPositionWeight; set => _manualAnimatorHipsLocalPositionWeight = value; }

        [Tooltip("프레임당 수동 기준 Hips localPosition으로 이동할 수 있는 최대 보정 거리입니다.")]
        [Range(0.001f, 0.5f)] [FormerlySerializedAs("manualAnimatorHipsLocalPositionMaxOffset")]
        [SerializeField] private float _manualAnimatorHipsLocalPositionMaxOffset= 0.12f;
        public float manualAnimatorHipsLocalPositionMaxOffset { get => _manualAnimatorHipsLocalPositionMaxOffset; set => _manualAnimatorHipsLocalPositionMaxOffset = value; }

        [Tooltip("수동 기준 Animator의 lowest-foot 상승량을 접지 목표 높이에 반영해 자동 경로가 점프/발 높이 호를 바닥으로 평탄화하지 않도록 합니다.")]
        [FormerlySerializedAs("useManualAnimatorFootHeightGroundingReference")] [SerializeField] private bool _shouldUseManualAnimatorFootHeightGroundingReference = false;

        [Tooltip("수동 기준 lowest-foot 접지 높이 보정 강도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorFootHeightGroundingReferenceWeight")]
        [SerializeField] private float _manualAnimatorFootHeightGroundingReferenceWeight= 1f;
        public float manualAnimatorFootHeightGroundingReferenceWeight { get => _manualAnimatorFootHeightGroundingReferenceWeight; private set => _manualAnimatorFootHeightGroundingReferenceWeight = value; }

        [Tooltip("수동 기준 lowest-foot에서 접지 목표 높이로 반영할 수 있는 최대 양수 상승량입니다.")]
        [Range(0f, 0.12f)] [FormerlySerializedAs("manualAnimatorFootHeightGroundingReferenceMaxLift")]
        [SerializeField] private float _manualAnimatorFootHeightGroundingReferenceMaxLift= 0.08f;
        public float manualAnimatorFootHeightGroundingReferenceMaxLift { get => _manualAnimatorFootHeightGroundingReferenceMaxLift; private set => _manualAnimatorFootHeightGroundingReferenceMaxLift = value; }

        [Tooltip("Apply the manual Animator lower-body leg-chain localRotation to the target as an isolated runtime candidate.")]
        [FormerlySerializedAs("useManualAnimatorFootLocalRotationReference")] [SerializeField] private bool _shouldUseManualAnimatorFootLocalRotationReference = false;

        [Tooltip("Blend weight for the manual Animator lower-body leg-chain localRotation reference.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorFootLocalRotationReferenceWeight")]
        [SerializeField] private float _manualAnimatorFootLocalRotationReferenceWeight= 1f;
        public float manualAnimatorFootLocalRotationReferenceWeight { get => _manualAnimatorFootLocalRotationReferenceWeight; set => _manualAnimatorFootLocalRotationReferenceWeight = value; }

        [Tooltip("Apply manual Animator lower-body segment directions as an isolated runtime candidate without changing bone lengths or scale.")]
        [FormerlySerializedAs("useManualAnimatorLowerBodySegmentDirectionReference")] [SerializeField] private bool _shouldUseManualAnimatorLowerBodySegmentDirectionReference = false;

        [Tooltip("Blend weight for the manual Animator lower-body segment direction correction.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorLowerBodySegmentDirectionReferenceWeight")]
        [SerializeField] private float _manualAnimatorLowerBodySegmentDirectionReferenceWeight= 1f;
        public float manualAnimatorLowerBodySegmentDirectionReferenceWeight { get => _manualAnimatorLowerBodySegmentDirectionReferenceWeight; set => _manualAnimatorLowerBodySegmentDirectionReferenceWeight = value; }

        [Tooltip("Maximum per-frame lower-body segment direction correction angle in degrees.")]
        [Range(0f, 20f)] [FormerlySerializedAs("manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle")]
        [SerializeField] private float _manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle= 6.2f;
        public float manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle { get => _manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle; set => _manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle = value; }

        [Tooltip("Skip only the upper-leg-to-lower-leg segments from the manual Animator lower-body segment direction correction.")]
        [FormerlySerializedAs("disableManualAnimatorUpperLegToLowerLegSegmentDirectionReference")] [SerializeField] private bool _shouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference = false;

        [Tooltip("Optional upper-leg-to-lower-leg segment direction max angle in degrees. Zero keeps the shared lower-body segment cap.")]
        [Range(0f, 20f)] [FormerlySerializedAs("manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle")]
        [SerializeField] private float _manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle= 0f;
        public float manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle { get => _manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle; set => _manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle = value; }

        [Tooltip("Skip only the lower-leg-to-foot segments from the manual Animator lower-body segment direction correction.")]
        [FormerlySerializedAs("disableManualAnimatorLowerLegToFootSegmentDirectionReference")] [SerializeField] private bool _shouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference = false;

        [Tooltip("Optional lower-leg-to-foot segment direction max angle in degrees. Zero keeps the shared lower-body segment cap.")]
        [Range(0f, 20f)] [FormerlySerializedAs("manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle")]
        [SerializeField] private float _manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle= 0f;
        public float manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle { get => _manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle; set => _manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle = value; }

        [Tooltip("Optional left lower-leg-to-foot segment direction max angle in degrees. Zero keeps the lower-leg-to-foot segment cap.")]
        [Range(0f, 20f)] [FormerlySerializedAs("manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle")]
        [SerializeField] private float _manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle= 0f;
        public float manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle { get => _manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle; set => _manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle = value; }

        [Tooltip("Optional right lower-leg-to-foot segment direction max angle in degrees. Zero keeps the lower-leg-to-foot segment cap.")]
        [Range(0f, 20f)] [FormerlySerializedAs("manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle")]
        [SerializeField] private float _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle= 0f;
        public float manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle { get => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle; set => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle = value; }

        [Tooltip("Runtime diagnostic scale for right lower-leg-to-foot correction axis X/Z components. One keeps the original axis.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale")]
        [SerializeField] private float _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale= 1f;
        public float manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale { get => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale; set => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale = value; }

        [Tooltip("Blend for right lower-leg-to-foot correction strength. The measured default reduces right-foot X/Z residual without worsening hips-aligned foot residual.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorRightLowerLegToFootSegmentDirectionReferenceBlendWeight")]
        [SerializeField] private float _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceBlendWeight= 0.125f;
        public float manualAnimatorRightLowerLegToFootSegmentDirectionReferenceBlendWeight { get => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceBlendWeight; set => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceBlendWeight = value; }

        [Tooltip("Runtime diagnostic start recorder frame for right lower-leg-to-foot cap. Zero disables frame gating.")]
        [Range(0f, 2000f)] [FormerlySerializedAs("manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateStart")]
        [SerializeField] private float _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateStart= 0f;
        public float manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateStart { get => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateStart; set => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateStart = value; }

        [Tooltip("Runtime diagnostic end recorder frame for right lower-leg-to-foot cap. Zero disables frame gating.")]
        [Range(0f, 2000f)] [FormerlySerializedAs("manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateEnd")]
        [SerializeField] private float _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateEnd= 0f;
        public float manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateEnd { get => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateEnd; set => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateEnd = value; }

        [Tooltip("Runtime diagnostic blend for preserving right foot world rotation after lower-leg-to-foot correction. One keeps the existing endpoint drift.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorRightLowerLegToFootSegmentDirectionReferenceEndpointBlendWeight")]
        [SerializeField] private float _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceEndpointBlendWeight= 1f;
        public float manualAnimatorRightLowerLegToFootSegmentDirectionReferenceEndpointBlendWeight { get => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceEndpointBlendWeight; set => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceEndpointBlendWeight = value; }

        [Tooltip("Skip only the foot-to-toes segment from the manual Animator lower-body segment direction correction.")]
        [FormerlySerializedAs("disableManualAnimatorFootToToesSegmentDirectionReference")] [SerializeField] private bool _shouldDisableManualAnimatorFootToToesSegmentDirectionReference = false;

        [Tooltip("Optional foot-to-toes-only segment direction max angle in degrees. Zero keeps the shared lower-body segment cap.")]
        [Range(0f, 20f)] [FormerlySerializedAs("manualAnimatorFootToToesSegmentDirectionReferenceMaxAngle")]
        [SerializeField] private float _manualAnimatorFootToToesSegmentDirectionReferenceMaxAngle= 0f;
        public float manualAnimatorFootToToesSegmentDirectionReferenceMaxAngle { get => _manualAnimatorFootToToesSegmentDirectionReferenceMaxAngle; set => _manualAnimatorFootToToesSegmentDirectionReferenceMaxAngle = value; }

        [Tooltip("Apply a yaw-only upper-leg correction toward the manual Animator hips-relative foot X/Z path.")]
        [FormerlySerializedAs("useManualAnimatorFootHipsAlignedResidualYawReference")] [SerializeField] private bool _shouldUseManualAnimatorFootHipsAlignedResidualYawReference = false;

        [Tooltip("Blend weight for the hips-aligned foot X/Z residual yaw correction.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorFootHipsAlignedResidualYawReferenceWeight")]
        [SerializeField] private float _manualAnimatorFootHipsAlignedResidualYawReferenceWeight= 1f;
        public float manualAnimatorFootHipsAlignedResidualYawReferenceWeight { get => _manualAnimatorFootHipsAlignedResidualYawReferenceWeight; set => _manualAnimatorFootHipsAlignedResidualYawReferenceWeight = value; }

        [Tooltip("Maximum per-frame yaw correction angle for each upper leg in degrees.")]
        [Range(0f, 45f)] [FormerlySerializedAs("manualAnimatorFootHipsAlignedResidualYawReferenceMaxAngle")]
        [SerializeField] private float _manualAnimatorFootHipsAlignedResidualYawReferenceMaxAngle= 15f;
        public float manualAnimatorFootHipsAlignedResidualYawReferenceMaxAngle { get => _manualAnimatorFootHipsAlignedResidualYawReferenceMaxAngle; set => _manualAnimatorFootHipsAlignedResidualYawReferenceMaxAngle = value; }

        [Tooltip("Apply manual Animator hips-relative foot positions through BipedIK as an isolated runtime candidate.")]
        [FormerlySerializedAs("useManualAnimatorBipedIkFootPositionReference")]
        [SerializeField] private bool _useManualAnimatorBipedIkFootPositionReference= false;
        public bool useManualAnimatorBipedIkFootPositionReference { get => _useManualAnimatorBipedIkFootPositionReference; set => _useManualAnimatorBipedIkFootPositionReference = value; }

        [Tooltip("Blend weight for manual Animator BipedIK foot position targets.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorBipedIkFootPositionReferenceWeight")]
        [SerializeField] private float _manualAnimatorBipedIkFootPositionReferenceWeight= 0.65f;
        public float manualAnimatorBipedIkFootPositionReferenceWeight { get => _manualAnimatorBipedIkFootPositionReferenceWeight; set => _manualAnimatorBipedIkFootPositionReferenceWeight = value; }

        [Tooltip("Maximum per-frame BipedIK foot target correction distance from the current target foot position.")]
        [Range(0f, 0.2f)] [FormerlySerializedAs("manualAnimatorBipedIkFootPositionReferenceMaxOffset")]
        [SerializeField] private float _manualAnimatorBipedIkFootPositionReferenceMaxOffset= 0.12f;
        public float manualAnimatorBipedIkFootPositionReferenceMaxOffset { get => _manualAnimatorBipedIkFootPositionReferenceMaxOffset; set => _manualAnimatorBipedIkFootPositionReferenceMaxOffset = value; }

        [Tooltip("Runtime diagnostic: apply a bounded right foot/toes endpoint X/Z correction immediately after SetHumanPose.")]
        [FormerlySerializedAs("usePostSetHumanPoseRightEndpointPositionReference")]
        [SerializeField] private bool _usePostSetHumanPoseRightEndpointPositionReference= false;
        public bool usePostSetHumanPoseRightEndpointPositionReference { get => _usePostSetHumanPoseRightEndpointPositionReference; set => _usePostSetHumanPoseRightEndpointPositionReference = value; }

        [Tooltip("Blend weight for post-SetHumanPose right-foot endpoint X/Z correction.")]
        [Range(0f, 1f)] [FormerlySerializedAs("postSetHumanPoseRightEndpointPositionReferenceWeight")]
        [SerializeField] private float _postSetHumanPoseRightEndpointPositionReferenceWeight= 1f;
        public float postSetHumanPoseRightEndpointPositionReferenceWeight { get => _postSetHumanPoseRightEndpointPositionReferenceWeight; set => _postSetHumanPoseRightEndpointPositionReferenceWeight = value; }

        [Tooltip("Maximum per-frame post-SetHumanPose right-foot endpoint X/Z correction distance.")]
        [Range(0f, 0.2f)] [FormerlySerializedAs("postSetHumanPoseRightEndpointPositionReferenceMaxOffset")]
        [SerializeField] private float _postSetHumanPoseRightEndpointPositionReferenceMaxOffset= 0.04f;
        public float postSetHumanPoseRightEndpointPositionReferenceMaxOffset { get => _postSetHumanPoseRightEndpointPositionReferenceMaxOffset; set => _postSetHumanPoseRightEndpointPositionReferenceMaxOffset = value; }

        [Tooltip("Scale applied only to positive world-Z endpoint correction after SetHumanPose; 1 keeps existing behavior.")]
        [Range(0f, 1f)] [FormerlySerializedAs("postSetHumanPoseRightEndpointPositionReferencePositiveZScale")]
        [SerializeField] private float _postSetHumanPoseRightEndpointPositionReferencePositiveZScale= 1f;
        public float postSetHumanPoseRightEndpointPositionReferencePositiveZScale { get => _postSetHumanPoseRightEndpointPositionReferencePositiveZScale; set => _postSetHumanPoseRightEndpointPositionReferencePositiveZScale = value; }

        [Tooltip("Blend from foot-only endpoint delta to the existing foot/toes average after SetHumanPose; 1 keeps existing behavior.")]
        [Range(0f, 1f)] [FormerlySerializedAs("postSetHumanPoseRightEndpointPositionReferenceToesBlendWeight")]
        [SerializeField] private float _postSetHumanPoseRightEndpointPositionReferenceToesBlendWeight= 1f;
        public float postSetHumanPoseRightEndpointPositionReferenceToesBlendWeight { get => _postSetHumanPoseRightEndpointPositionReferenceToesBlendWeight; set => _postSetHumanPoseRightEndpointPositionReferenceToesBlendWeight = value; }

        [Tooltip("First legacy animation frame for post-SetHumanPose right-foot endpoint correction; 0 with end 0 keeps existing behavior.")]
        [Range(0f, 6000f)] [FormerlySerializedAs("postSetHumanPoseRightEndpointPositionReferenceFrameGateStart")]
        [SerializeField] private float _postSetHumanPoseRightEndpointPositionReferenceFrameGateStart= 0f;
        public float postSetHumanPoseRightEndpointPositionReferenceFrameGateStart { get => _postSetHumanPoseRightEndpointPositionReferenceFrameGateStart; set => _postSetHumanPoseRightEndpointPositionReferenceFrameGateStart = value; }

        [Tooltip("Last legacy animation frame for post-SetHumanPose right-foot endpoint correction; 0 with start 0 keeps existing behavior.")]
        [Range(0f, 6000f)] [FormerlySerializedAs("postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd")]
        [SerializeField] private float _postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd= 0f;
        public float postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd { get => _postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd; set => _postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd = value; }

    }
}
