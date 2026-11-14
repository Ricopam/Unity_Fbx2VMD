using UnityEngine;
using UnityEngine.Serialization;
using System;
using System.IO;
using System.Linq;
using Fbx2Vmd.FileSystem;
using Fbx2Vmd.Recording;
using Fbx2Vmd.Character;
using Fbx2Vmd.Profiling;


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

        [Tooltip("Runtime diagnostic: apply the post-SetHumanPose endpoint correction to the left foot row instead of the right foot row.")]
        [FormerlySerializedAs("postSetHumanPoseEndpointPositionUseLeftSide")] [SerializeField] private bool _shouldUseLeftSideForPostSetHumanPoseEndpointPosition = false;

        [Tooltip("Use the first matched reference foot X/Z offset as the post-SetHumanPose right-foot correction basis.")]
        [FormerlySerializedAs("usePostSetHumanPoseRightFootEvaluatorXzReference")]
        [SerializeField] private bool _usePostSetHumanPoseRightFootEvaluatorXzReference= false;
        public bool usePostSetHumanPoseRightFootEvaluatorXzReference { get => _usePostSetHumanPoseRightFootEvaluatorXzReference; set => _usePostSetHumanPoseRightFootEvaluatorXzReference = value; }

        [Tooltip("Target normalized right-foot X/Z magnitude for the first-offset evaluator-basis post-SetHumanPose prototype.")]
        [Range(0f, 0.2f)] [FormerlySerializedAs("postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude")]
        [SerializeField] private float _postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude= 0.049f;
        public float postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude { get => _postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude; set => _postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude = value; }

        [Tooltip("Apply a right-foot endpoint X/Z correction immediately before SetHumanPose as an isolated runtime candidate.")]
        [FormerlySerializedAs("usePreSetHumanPoseRightEndpointPositionReference")]
        [SerializeField] private bool _usePreSetHumanPoseRightEndpointPositionReference= false;
        public bool usePreSetHumanPoseRightEndpointPositionReference { get => _usePreSetHumanPoseRightEndpointPositionReference; set => _usePreSetHumanPoseRightEndpointPositionReference = value; }

        [Tooltip("Blend weight for pre-SetHumanPose right-foot endpoint X/Z correction.")]
        [Range(0f, 1f)] [FormerlySerializedAs("preSetHumanPoseRightEndpointPositionReferenceWeight")]
        [SerializeField] private float _preSetHumanPoseRightEndpointPositionReferenceWeight= 1f;
        public float preSetHumanPoseRightEndpointPositionReferenceWeight { get => _preSetHumanPoseRightEndpointPositionReferenceWeight; set => _preSetHumanPoseRightEndpointPositionReferenceWeight = value; }

        [Tooltip("Maximum per-frame pre-SetHumanPose right-foot endpoint X/Z correction distance.")]
        [Range(0f, 0.2f)] [FormerlySerializedAs("preSetHumanPoseRightEndpointPositionReferenceMaxOffset")]
        [SerializeField] private float _preSetHumanPoseRightEndpointPositionReferenceMaxOffset= 0.025f;
        public float preSetHumanPoseRightEndpointPositionReferenceMaxOffset { get => _preSetHumanPoseRightEndpointPositionReferenceMaxOffset; set => _preSetHumanPoseRightEndpointPositionReferenceMaxOffset = value; }

        [Tooltip("Scale applied only to positive world-Z endpoint correction before SetHumanPose; 1 keeps existing behavior.")]
        [Range(0f, 1f)] [FormerlySerializedAs("preSetHumanPoseRightEndpointPositionReferencePositiveZScale")]
        [SerializeField] private float _preSetHumanPoseRightEndpointPositionReferencePositiveZScale= 1f;
        public float preSetHumanPoseRightEndpointPositionReferencePositiveZScale { get => _preSetHumanPoseRightEndpointPositionReferencePositiveZScale; set => _preSetHumanPoseRightEndpointPositionReferencePositiveZScale = value; }

        [Tooltip("Blend from foot-only endpoint delta to the foot/toes average before SetHumanPose.")]
        [Range(0f, 1f)] [FormerlySerializedAs("preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight")]
        [SerializeField] private float _preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight= 1f;
        public float preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight { get => _preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight; set => _preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight = value; }

        [Tooltip("First legacy animation frame for pre-SetHumanPose right-foot endpoint correction; 0 with end 0 keeps existing behavior.")]
        [Range(0f, 6000f)] [FormerlySerializedAs("preSetHumanPoseRightEndpointPositionReferenceFrameGateStart")]
        [SerializeField] private float _preSetHumanPoseRightEndpointPositionReferenceFrameGateStart= 0f;
        public float preSetHumanPoseRightEndpointPositionReferenceFrameGateStart { get => _preSetHumanPoseRightEndpointPositionReferenceFrameGateStart; set => _preSetHumanPoseRightEndpointPositionReferenceFrameGateStart = value; }

        [Tooltip("Last legacy animation frame for pre-SetHumanPose right-foot endpoint correction; 0 with start 0 keeps existing behavior.")]
        [Range(0f, 6000f)] [FormerlySerializedAs("preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd")]
        [SerializeField] private float _preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd= 0f;
        public float preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd { get => _preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd; set => _preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd = value; }

        [Tooltip("Runtime diagnostic: apply the pre-SetHumanPose endpoint correction to the left foot row instead of the right foot row.")]
        [FormerlySerializedAs("preSetHumanPoseEndpointPositionUseLeftSide")] [SerializeField] private bool _shouldUseLeftSideForPreSetHumanPoseEndpointPosition = false;

        [Tooltip("Runtime diagnostic: use ghost/current endpoint rows as a sign-corrected bodyPosition X/Z translation basis before SetHumanPose.")]
        [FormerlySerializedAs("preSetHumanPoseEndpointPositionUseGhostCurrentBasis")]
        [SerializeField] private bool _preSetHumanPoseEndpointPositionUseGhostCurrentBasis= false;
        public bool preSetHumanPoseEndpointPositionUseGhostCurrentBasis { get => _preSetHumanPoseEndpointPositionUseGhostCurrentBasis; set => _preSetHumanPoseEndpointPositionUseGhostCurrentBasis = value; }

        [Tooltip("Runtime diagnostic: invert the pre-SetHumanPose endpoint bodyPosition X input delta.")]
        [FormerlySerializedAs("preSetHumanPoseEndpointPositionInvertBodyPositionX")] [SerializeField] private bool _shouldInvertPreSetHumanPoseEndpointPositionBodyX = false;

        [Tooltip("Runtime diagnostic: invert the pre-SetHumanPose endpoint bodyPosition Z input delta.")]
        [FormerlySerializedAs("preSetHumanPoseEndpointPositionInvertBodyPositionZ")] [SerializeField] private bool _shouldInvertPreSetHumanPoseEndpointPositionBodyZ = false;

        [Tooltip("엄지 체인의 localRotation도 Sub_Manual/testPrefab Animator가 같은 FBX clip에서 평가한 값을 기준으로 덮어씁니다. YYB와 testPrefab의 Humanoid muscle은 같지만 엄지 로컬 축 해석이 달라 보일 때 사용합니다.")]
        [FormerlySerializedAs("useManualAnimatorThumbLocalRotationReference")]
        [SerializeField] private bool _useManualAnimatorThumbLocalRotationReference= false;
        public bool useManualAnimatorThumbLocalRotationReference { get => _useManualAnimatorThumbLocalRotationReference; set => _useManualAnimatorThumbLocalRotationReference = value; }

        [Tooltip("손목 localRotation을 Sub_Manual/testPrefab Animator가 같은 FBX clip에서 평가한 값을 기준으로 덮어씁니다. t13.2 hand pose parity 회귀 보호용입니다.")]
        [FormerlySerializedAs("useManualAnimatorHandLocalRotationReference")]
        [SerializeField] private bool _useManualAnimatorHandLocalRotationReference= false;
        public bool useManualAnimatorHandLocalRotationReference { get => _useManualAnimatorHandLocalRotationReference; set => _useManualAnimatorHandLocalRotationReference = value; }

        [Tooltip("엄지 세그먼트 방향을 Sub_Manual/testPrefab의 손 기준 방향에 맞춥니다. 모델별 bind axis 차이 때문에 localRotation 숫자가 같아도 손 모양이 달라질 때 사용합니다.")]
        [FormerlySerializedAs("useManualAnimatorThumbSegmentDirectionReference")]
        [SerializeField] private bool _useManualAnimatorThumbSegmentDirectionReference= false;
        public bool useManualAnimatorThumbSegmentDirectionReference { get => _useManualAnimatorThumbSegmentDirectionReference; private set => _useManualAnimatorThumbSegmentDirectionReference = value; }

        [Tooltip("엄지 세그먼트 방향 보정 강도입니다. 1이면 testPrefab 손 기준 방향에 맞추고, 0이면 보정하지 않습니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorThumbSegmentDirectionWeight")]
        [SerializeField] private float _manualAnimatorThumbSegmentDirectionWeight= 1f;
        public float manualAnimatorThumbSegmentDirectionWeight { get => _manualAnimatorThumbSegmentDirectionWeight; private set => _manualAnimatorThumbSegmentDirectionWeight = value; }

        [Tooltip("손바닥 기준 Hand->ThumbIntermediate 방향을 Sub_Manual/testPrefab의 손 기준 방향에 맞춥니다. 엄지 시작 방향이 손바닥 밖으로 탈골된 것처럼 보일 때 사용합니다.")]
        [FormerlySerializedAs("useManualAnimatorThumbHandDirectionReference")]
        [SerializeField] private bool _useManualAnimatorThumbHandDirectionReference= false;
        public bool useManualAnimatorThumbHandDirectionReference { get => _useManualAnimatorThumbHandDirectionReference; private set => _useManualAnimatorThumbHandDirectionReference = value; }

        [Tooltip("손바닥 기준 엄지 시작 방향 보정 강도입니다. 1이면 testPrefab의 Hand->ThumbIntermediate 방향에 맞춥니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorThumbHandDirectionWeight")]
        [SerializeField] private float _manualAnimatorThumbHandDirectionWeight= 1f;
        public float manualAnimatorThumbHandDirectionWeight { get => _manualAnimatorThumbHandDirectionWeight; private set => _manualAnimatorThumbHandDirectionWeight = value; }

        [Tooltip("손바닥 전체 프레임을 Sub_Manual/testPrefab의 손바닥 방향에 맞춥니다. MMD 기준과 손목/엄지 뿌리 실루엣이 다를 때 사용합니다.")]
        [FormerlySerializedAs("useManualAnimatorHandPalmFrameReference")]
        [SerializeField] private bool _useManualAnimatorHandPalmFrameReference= false;
        public bool useManualAnimatorHandPalmFrameReference { get => _useManualAnimatorHandPalmFrameReference; set => _useManualAnimatorHandPalmFrameReference = value; }

        [Tooltip("손바닥 프레임 보정 강도입니다. MMD 396프레임 직접 비교 기준으로 기본값은 1.00입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorHandPalmFrameWeight")]
        [SerializeField] private float _manualAnimatorHandPalmFrameWeight= 1f;
        public float manualAnimatorHandPalmFrameWeight { get => _manualAnimatorHandPalmFrameWeight; set => _manualAnimatorHandPalmFrameWeight = value; }

        [Tooltip("엄지 첫 본 위치를 Sub_Manual/testPrefab의 손 기준 위치 비율로 맞춥니다. YYB 엄지 시작부가 손바닥 안쪽으로 붙어 보일 때 사용합니다.")]
        [FormerlySerializedAs("useManualAnimatorThumbBasePositionReference")]
        [SerializeField] private bool _useManualAnimatorThumbBasePositionReference= false;
        public bool useManualAnimatorThumbBasePositionReference { get => _useManualAnimatorThumbBasePositionReference; private set => _useManualAnimatorThumbBasePositionReference = value; }

        [Tooltip("엄지 첫 본 위치 보정 강도입니다. 1이면 testPrefab 손 기준 위치 비율을 그대로 적용합니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorThumbBasePositionWeight")]
        [SerializeField] private float _manualAnimatorThumbBasePositionWeight= 1f;
        public float manualAnimatorThumbBasePositionWeight { get => _manualAnimatorThumbBasePositionWeight; private set => _manualAnimatorThumbBasePositionWeight = value; }

        [Tooltip("엄지 첫 본 위치가 원본 위치에서 한 프레임에 벗어날 수 있는 최대 거리입니다.")]
        [Range(0f, 0.03f)] [FormerlySerializedAs("manualAnimatorThumbBasePositionMaxOffset")]
        [SerializeField] private float _manualAnimatorThumbBasePositionMaxOffset= 0.03f;
        public float manualAnimatorThumbBasePositionMaxOffset { get => _manualAnimatorThumbBasePositionMaxOffset; private set => _manualAnimatorThumbBasePositionMaxOffset = value; }

        [Tooltip("손가락 기준 평가에 사용할 수동 기준 프리팹입니다. 비워두면 VMDRecorderSample testPrefab을 자동으로 찾습니다.")]
        [FormerlySerializedAs("manualFingerReferencePrefab")]
        [SerializeField] private GameObject _manualFingerReferencePrefab;
        public GameObject manualFingerReferencePrefab { get => _manualFingerReferencePrefab; private set => _manualFingerReferencePrefab = value; }

        [Tooltip("손가락 기준 평가에 사용할 수동 Animator Controller입니다. 비워두면 TestAnimator1_Manual 또는 TestAnimator1을 자동으로 찾습니다.")]
        [FormerlySerializedAs("manualFingerReferenceController")]
        [SerializeField] private RuntimeAnimatorController _manualFingerReferenceController;
        public RuntimeAnimatorController manualFingerReferenceController { get => _manualFingerReferenceController; private set => _manualFingerReferenceController = value; }

        [Tooltip("Retarget/녹화 중 MMD4Mecanim의 어깨 PPH 보정을 잠시 끕니다. 수동 기준 모션과 어깨 형태가 달라질 수 있어 기본값은 끕니다.")]
        [FormerlySerializedAs("disableMmdShoulderPostPoseDuringRetarget")]
        [SerializeField] private bool _disableMmdShoulderPostPoseDuringRetarget= false;
        public bool disableMmdShoulderPostPoseDuringRetarget { get => _disableMmdShoulderPostPoseDuringRetarget; private set => _disableMmdShoulderPostPoseDuringRetarget = value; }

        [Header("Anatomical Retarget Guard")]
        [Tooltip("FBX Humanoid muscle 값을 [-1, 1] 범위로 제한합니다.")]
        [FormerlySerializedAs("clampRetargetMusclesToHumanRange")]
        [SerializeField] private bool _clampRetargetMusclesToHumanRange= true;
        public bool clampRetargetMusclesToHumanRange { get => _clampRetargetMusclesToHumanRange; private set => _clampRetargetMusclesToHumanRange = value; }

        [Tooltip("팔이 늘어나거나 비정상적으로 비틀리는 Humanoid arm muscle 값을 제한합니다.")]
        [FormerlySerializedAs("enableAnatomicalArmGuard")]
        [SerializeField] private bool _enableAnatomicalArmGuard= true;
        public bool enableAnatomicalArmGuard { get => _enableAnatomicalArmGuard; set => _enableAnatomicalArmGuard = value; }

        [Tooltip("Humanoid 팔 Stretch muscle 허용치입니다. Forearm Stretch는 팔꿈치 굽힘에 가까우므로 기본적으로 제한하지 않습니다.")]
        [Range(0f, 0.5f)] [FormerlySerializedAs("ArmStretchMuscleLimit")]
        [SerializeField] private float _ArmStretchMuscleLimit= 0f;
        public float ArmStretchMuscleLimit { get => _ArmStretchMuscleLimit; set => _ArmStretchMuscleLimit = value; }

        [Tooltip("Retarget 단계에서 Forearm Stretch muscle을 제한합니다. 팔 길이가 아니라 팔꿈치 굽힘에 가까워 기본값은 꺼야 합니다.")]
        [FormerlySerializedAs("clampRetargetArmStretchMuscles")]
        [SerializeField] private bool _clampRetargetArmStretchMuscles= false;
        public bool clampRetargetArmStretchMuscles { get => _clampRetargetArmStretchMuscles; set => _clampRetargetArmStretchMuscles = value; }

        [Tooltip("상완 Twist muscle 허용치입니다.")]
        [Range(0.1f, 1f)] [FormerlySerializedAs("UpperArmTwistMuscleLimit")]
        [SerializeField] private float _UpperArmTwistMuscleLimit= 0.75f;
        public float UpperArmTwistMuscleLimit { get => _UpperArmTwistMuscleLimit; private set => _UpperArmTwistMuscleLimit = value; }

        [Tooltip("전완 Twist muscle 허용치입니다.")]
        [Range(0.1f, 1f)] [FormerlySerializedAs("LowerArmTwistMuscleLimit")]
        [SerializeField] private float _LowerArmTwistMuscleLimit= 0.65f;
        public float LowerArmTwistMuscleLimit { get => _LowerArmTwistMuscleLimit; private set => _LowerArmTwistMuscleLimit = value; }

        [Tooltip("Retarget 중 Target Humanoid 본의 localPosition을 초기값으로 복구해 팔/다리 길이 변형을 막습니다.")]
        [FormerlySerializedAs("lockTargetHumanoidBonePositions")] [SerializeField] private bool _shouldLockTargetHumanoidBonePositions = true;

        [Tooltip("팔/다리 하위의 비-Humanoid 보조본 localPosition도 초기값으로 복구해 소매/팔 메시가 가늘어지는 변형을 막습니다.")]
        [FormerlySerializedAs("lockTargetLimbChildLocalPositions")]
        [SerializeField] private bool _lockTargetLimbChildLocalPositions= true;
        public bool lockTargetLimbChildLocalPositions { get => _lockTargetLimbChildLocalPositions; private set => _lockTargetLimbChildLocalPositions = value; }

        [Tooltip("팔/다리 하위의 비-Humanoid 보조본 localRotation을 초기값으로 복구합니다. YYB 소매/팔 twist 보조본 움직임을 막을 수 있어 기본값은 끕니다.")]
        [FormerlySerializedAs("lockTargetLimbChildLocalRotations")]
        [SerializeField] private bool _lockTargetLimbChildLocalRotations= false;
        public bool lockTargetLimbChildLocalRotations { get => _lockTargetLimbChildLocalRotations; private set => _lockTargetLimbChildLocalRotations = value; }

        [Tooltip("Target 캐릭터에 HumanoidArmDeformationGuard를 자동 부착해 Retarget 외 경로에서도 팔 가늘어짐을 막습니다.")]
        [FormerlySerializedAs("attachTargetArmDeformationGuard")]
        [SerializeField] private bool _attachTargetArmDeformationGuard= true;
        public bool attachTargetArmDeformationGuard { get => _attachTargetArmDeformationGuard; private set => _attachTargetArmDeformationGuard = value; }

        [Tooltip("자동 부착된 Target 가드에서 HumanPose arm muscle도 제한합니다. 기본 Retargeter 보정과 중복될 수 있어 기본값은 끕니다.")]
        [FormerlySerializedAs("targetGuardClampAnatomicalArmMuscles")]
        [SerializeField] private bool _targetGuardClampAnatomicalArmMuscles= false;
        public bool targetGuardClampAnatomicalArmMuscles { get => _targetGuardClampAnatomicalArmMuscles; set => _targetGuardClampAnatomicalArmMuscles = value; }

        [Tooltip("자동 부착된 Target 가드도 arm stretch muscle을 제한합니다. 직접 Animator 모션이 굳을 수 있어 기본값은 끕니다.")]
        [FormerlySerializedAs("targetGuardClampArmStretchMuscles")]
        [SerializeField] private bool _targetGuardClampArmStretchMuscles= false;
        public bool targetGuardClampArmStretchMuscles { get => _targetGuardClampArmStretchMuscles; set => _targetGuardClampArmStretchMuscles = value; }

        [Tooltip("팔 변형 가드가 제한/복구를 처음 수행할 때 진단 로그를 출력합니다.")]
        [FormerlySerializedAs("logArmDeformationGuardCorrections")]
        [SerializeField] private bool _logArmDeformationGuardCorrections= false;
        public bool logArmDeformationGuardCorrections { get => _logArmDeformationGuardCorrections; private set => _logArmDeformationGuardCorrections = value; }

        [Header("Animation Rigging Arm Twist Correction")]
        [Tooltip("YYB 팔 twist 보조본을 Animation Rigging TwistCorrection으로 보정합니다. 현재 자동 리타겟 경로에서는 RigBuilder가 SetHumanPose 포즈를 초기화할 수 있어 기본 비활성화합니다.")]
        [FormerlySerializedAs("enableAnimationRiggingArmTwistCorrection")]
        [SerializeField] private bool _enableAnimationRiggingArmTwistCorrection= false;
        public bool enableAnimationRiggingArmTwistCorrection { get => _enableAnimationRiggingArmTwistCorrection; private set => _enableAnimationRiggingArmTwistCorrection = value; }

        [Tooltip("Animation Rigging 팔 twist 보정 전체 영향도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("AnimationRiggingArmTwistRigWeight")]
        [SerializeField] private float _AnimationRiggingArmTwistRigWeight= 0.65f;
        public float AnimationRiggingArmTwistRigWeight { get => _AnimationRiggingArmTwistRigWeight; private set => _AnimationRiggingArmTwistRigWeight = value; }

        [Tooltip("상완 twist 보조본 영향도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("AnimationRiggingUpperArmTwistWeight")]
        [SerializeField] private float _AnimationRiggingUpperArmTwistWeight= 0.45f;
        public float AnimationRiggingUpperArmTwistWeight { get => _AnimationRiggingUpperArmTwistWeight; private set => _AnimationRiggingUpperArmTwistWeight = value; }

        [Tooltip("전완/손목 twist 보조본 영향도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("AnimationRiggingForearmTwistWeight")]
        [SerializeField] private float _AnimationRiggingForearmTwistWeight= 0.85f;
        public float AnimationRiggingForearmTwistWeight { get => _AnimationRiggingForearmTwistWeight; private set => _AnimationRiggingForearmTwistWeight = value; }

        [Tooltip("Animation Rigging 팔 twist 보정 구성 로그를 출력합니다.")]
        [FormerlySerializedAs("logAnimationRiggingArmTwistCorrection")]
        [SerializeField] private bool _logAnimationRiggingArmTwistCorrection= false;
        public bool logAnimationRiggingArmTwistCorrection { get => _logAnimationRiggingArmTwistCorrection; private set => _logAnimationRiggingArmTwistCorrection = value; }

        [Header("Humanoid Arm Direction Retarget Correction")]
        [Tooltip("실험 옵션입니다. Ghost 팔 세그먼트 방향을 Target Character의 Humanoid 팔 방향에 제한적으로 다시 맞춥니다. 현재는 일부 정상 프레임을 망가뜨릴 수 있어 기본 비활성화합니다.")]
        [FormerlySerializedAs("enableYybArmDirectionRetargetCorrection")]
        [SerializeField] private bool _enableYybArmDirectionRetargetCorrection= false;
        public bool enableYybArmDirectionRetargetCorrection { get => _enableYybArmDirectionRetargetCorrection; set => _enableYybArmDirectionRetargetCorrection = value; }

        [Tooltip("상완 방향 보정 영향도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmDirectionUpperArmWeight")]
        [SerializeField] private float _YybArmDirectionUpperArmWeight= 0.65f;
        public float YybArmDirectionUpperArmWeight { get => _YybArmDirectionUpperArmWeight; set => _YybArmDirectionUpperArmWeight = value; }

        [Tooltip("전완 방향 보정 영향도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmDirectionForearmWeight")]
        [SerializeField] private float _YybArmDirectionForearmWeight= 0.75f;
        public float YybArmDirectionForearmWeight { get => _YybArmDirectionForearmWeight; set => _YybArmDirectionForearmWeight = value; }

        [Tooltip("상완이 한 프레임에 따라갈 수 있는 최대 각도입니다.")]
        [Range(0f, 120f)] [FormerlySerializedAs("YybArmDirectionUpperArmMaxDegrees")]
        [SerializeField] private float _YybArmDirectionUpperArmMaxDegrees= 65f;
        public float YybArmDirectionUpperArmMaxDegrees { get => _YybArmDirectionUpperArmMaxDegrees; set => _YybArmDirectionUpperArmMaxDegrees = value; }

        [Tooltip("전완이 한 프레임에 따라갈 수 있는 최대 각도입니다.")]
        [Range(0f, 120f)] [FormerlySerializedAs("YybArmDirectionForearmMaxDegrees")]
        [SerializeField] private float _YybArmDirectionForearmMaxDegrees= 85f;
        public float YybArmDirectionForearmMaxDegrees { get => _YybArmDirectionForearmMaxDegrees; set => _YybArmDirectionForearmMaxDegrees = value; }

        [Tooltip("Humanoid 팔 방향 보정의 왼쪽 팔 영향도 배율입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmDirectionLeftSideWeightScale")]
        [SerializeField] private float _YybArmDirectionLeftSideWeightScale= 1f;
        public float YybArmDirectionLeftSideWeightScale { get => _YybArmDirectionLeftSideWeightScale; set => _YybArmDirectionLeftSideWeightScale = value; }

        [Tooltip("Humanoid 팔 방향 보정의 오른쪽 팔 영향도 배율입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmDirectionRightSideWeightScale")]
        [SerializeField] private float _YybArmDirectionRightSideWeightScale= 1f;
        public float YybArmDirectionRightSideWeightScale { get => _YybArmDirectionRightSideWeightScale; set => _YybArmDirectionRightSideWeightScale = value; }

        [Tooltip("Humanoid 팔 방향 보정 구성 로그를 출력합니다.")]
        [FormerlySerializedAs("logYybArmDirectionRetargetCorrection")]
        [SerializeField] private bool _logYybArmDirectionRetargetCorrection= false;
        public bool logYybArmDirectionRetargetCorrection { get => _logYybArmDirectionRetargetCorrection; private set => _logYybArmDirectionRetargetCorrection = value; }

        [Header("YYB Arm Visual Twist Correction")]
        [Tooltip("RigBuilder 없이 YYB 팔/소매 보조본을 전완-손목 회전에 맞춰 직접 분배해 소매가 가늘어 보이는 현상을 줄입니다.")]
        [FormerlySerializedAs("enableYybArmVisualTwistCorrection")]
        [SerializeField] private bool _enableYybArmVisualTwistCorrection= true;
        public bool enableYybArmVisualTwistCorrection { get => _enableYybArmVisualTwistCorrection; set => _enableYybArmVisualTwistCorrection = value; }

        [Tooltip("상완 보조본 회전 분배 영향도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmVisualUpperArmInfluence")]
        [SerializeField] private float _YybArmVisualUpperArmInfluence= 0.35f;
        public float YybArmVisualUpperArmInfluence { get => _YybArmVisualUpperArmInfluence; set => _YybArmVisualUpperArmInfluence = value; }

        [Tooltip("전완/손목 보조본 회전 분배 영향도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmVisualForearmInfluence")]
        [SerializeField] private float _YybArmVisualForearmInfluence= 0.75f;
        public float YybArmVisualForearmInfluence { get => _YybArmVisualForearmInfluence; set => _YybArmVisualForearmInfluence = value; }

        [Tooltip("상완 보조본에 허용할 최대 회전각입니다.")]
        [Range(0f, 120f)] [FormerlySerializedAs("YybArmVisualUpperArmMaxDegrees")]
        [SerializeField] private float _YybArmVisualUpperArmMaxDegrees= 45f;
        public float YybArmVisualUpperArmMaxDegrees { get => _YybArmVisualUpperArmMaxDegrees; set => _YybArmVisualUpperArmMaxDegrees = value; }

        [Tooltip("전완/손목 보조본에 허용할 최대 회전각입니다.")]
        [Range(0f, 120f)] [FormerlySerializedAs("YybArmVisualForearmMaxDegrees")]
        [SerializeField] private float _YybArmVisualForearmMaxDegrees= 75f;
        public float YybArmVisualForearmMaxDegrees { get => _YybArmVisualForearmMaxDegrees; set => _YybArmVisualForearmMaxDegrees = value; }

        [Tooltip("YYB 팔 시각 보정 구성 로그를 출력합니다.")]
        [FormerlySerializedAs("logYybArmVisualTwistCorrection")]
        [SerializeField] private bool _logYybArmVisualTwistCorrection= false;
        public bool logYybArmVisualTwistCorrection { get => _logYybArmVisualTwistCorrection; private set => _logYybArmVisualTwistCorrection = value; }

        [Header("휴머노이드 팔 해부학적 스윙 보정")]
        [Tooltip("손이 몸 밖/어깨 근처에 있는데 상완만 아래로 크게 떨어지는 포즈를 제한합니다.")]
        [FormerlySerializedAs("enableYybArmSwingLimitCorrection")]
        [SerializeField] private bool _enableYybArmSwingLimitCorrection= false;
        public bool enableYybArmSwingLimitCorrection { get => _enableYybArmSwingLimitCorrection; set => _enableYybArmSwingLimitCorrection = value; }

        [Tooltip("상완 하강 제한 보정 강도입니다. Target Humanoid 포즈를 직접 바꾸므로 기본 자동 경로에서는 끄고, 진단/긴급 보정 때만 사용합니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmSwingLimitWeight")]
        [SerializeField] private float _YybArmSwingLimitWeight= 0.85f;
        public float YybArmSwingLimitWeight { get => _YybArmSwingLimitWeight; set => _YybArmSwingLimitWeight = value; }

        [Tooltip("상완 방향이 아래 방향과 이 값보다 더 가까우면 보정 후보로 봅니다. 0.68은 수동 기준 모션과의 차이를 줄이는 균형값입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmSwingMaxDownDot")]
        [SerializeField] private float _YybArmSwingMaxDownDot= 0.68f;
        public float YybArmSwingMaxDownDot { get => _YybArmSwingMaxDownDot; set => _YybArmSwingMaxDownDot = value; }

        [Tooltip("손이 어깨에서 팔 길이 대비 이 비율 이상 옆/앞으로 떨어져 있을 때만 보정합니다. 몸 가까이에서 소매가 무너지는 체형도 감지할 수 있도록 기본값을 낮게 둡니다.")]
        [Range(0f, 1.5f)] [FormerlySerializedAs("YybArmSwingMinHandHorizontalRatio")]
        [SerializeField] private float _YybArmSwingMinHandHorizontalRatio= 0.05f;
        public float YybArmSwingMinHandHorizontalRatio { get => _YybArmSwingMinHandHorizontalRatio; set => _YybArmSwingMinHandHorizontalRatio = value; }

        [Tooltip("손이 어깨보다 팔 길이 대비 이 비율보다 더 낮으면 자연스럽게 내려간 팔로 보고 보정하지 않습니다.")]
        [Range(0f, 1.5f)] [FormerlySerializedAs("YybArmSwingMaxHandBelowShoulderRatio")]
        [SerializeField] private float _YybArmSwingMaxHandBelowShoulderRatio= 0.75f;
        public float YybArmSwingMaxHandBelowShoulderRatio { get => _YybArmSwingMaxHandBelowShoulderRatio; set => _YybArmSwingMaxHandBelowShoulderRatio = value; }

        [Tooltip("손이 몸 밖으로 과하게 벌어진 경우 수평 도달 거리를 제한하는 보정 강도입니다. 0이면 비활성화합니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmSwingHorizontalReachLimitWeight")]
        [SerializeField] private float _YybArmSwingHorizontalReachLimitWeight= 0f;
        public float YybArmSwingHorizontalReachLimitWeight { get => _YybArmSwingHorizontalReachLimitWeight; set => _YybArmSwingHorizontalReachLimitWeight = value; }

        [Tooltip("팔 길이 대비 허용할 최대 손 수평 도달 거리 비율입니다. 0이면 수평 도달 거리 제한을 사용하지 않습니다.")]
        [Range(0f, 1.5f)] [FormerlySerializedAs("YybArmSwingMaxHandHorizontalReachRatio")]
        [SerializeField] private float _YybArmSwingMaxHandHorizontalReachRatio= 0f;
        public float YybArmSwingMaxHandHorizontalReachRatio { get => _YybArmSwingMaxHandHorizontalReachRatio; set => _YybArmSwingMaxHandHorizontalReachRatio = value; }

        [Tooltip("수평 도달 거리 제한에만 적용하는 손 하강 비율입니다. 0이면 기본 손 하강 비율 설정을 사용합니다.")]
        [Range(0f, 1.5f)] [FormerlySerializedAs("YybArmSwingHorizontalReachMaxHandBelowShoulderRatio")]
        [SerializeField] private float _YybArmSwingHorizontalReachMaxHandBelowShoulderRatio= 0f;
        public float YybArmSwingHorizontalReachMaxHandBelowShoulderRatio { get => _YybArmSwingHorizontalReachMaxHandBelowShoulderRatio; set => _YybArmSwingHorizontalReachMaxHandBelowShoulderRatio = value; }

        [Tooltip("수평 도달 거리 보정 뒤 팔꿈치 각도가 이 값보다 작으면 해당 보정을 되돌립니다. 0이면 비활성화합니다.")]
        [Range(0f, 180f)] [FormerlySerializedAs("YybArmSwingHorizontalReachMinElbowAngleAfterApply")]
        [SerializeField] private float _YybArmSwingHorizontalReachMinElbowAngleAfterApply= 0f;
        public float YybArmSwingHorizontalReachMinElbowAngleAfterApply { get => _YybArmSwingHorizontalReachMinElbowAngleAfterApply; set => _YybArmSwingHorizontalReachMinElbowAngleAfterApply = value; }

        [Tooltip("기존 상완 하강 제한이 동작하지 않는 들어 올린 팔 자세에서 수평 도달 거리를 제한하는 보정 강도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmSwingRaisedPoseHorizontalReachLimitWeight")]
        [SerializeField] private float _YybArmSwingRaisedPoseHorizontalReachLimitWeight= 0f;
        public float YybArmSwingRaisedPoseHorizontalReachLimitWeight { get => _YybArmSwingRaisedPoseHorizontalReachLimitWeight; set => _YybArmSwingRaisedPoseHorizontalReachLimitWeight = value; }

        [Tooltip("들어 올린 팔 자세의 수평 도달 거리 제한을 적용할 최소 상완 하강 내적값입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmSwingRaisedPoseMinUpperArmDownDot")]
        [SerializeField] private float _YybArmSwingRaisedPoseMinUpperArmDownDot= 0.55f;
        public float YybArmSwingRaisedPoseMinUpperArmDownDot { get => _YybArmSwingRaisedPoseMinUpperArmDownDot; set => _YybArmSwingRaisedPoseMinUpperArmDownDot = value; }

        [Tooltip("손이 어깨 아래로 이 비율보다 더 내려가 있으면 들어 올린 팔 자세의 수평 도달 거리 제한을 건너뜁니다.")]
        [Range(0f, 1.5f)] [FormerlySerializedAs("YybArmSwingRaisedPoseMaxHandBelowShoulderRatio")]
        [SerializeField] private float _YybArmSwingRaisedPoseMaxHandBelowShoulderRatio= 0.05f;
        public float YybArmSwingRaisedPoseMaxHandBelowShoulderRatio { get => _YybArmSwingRaisedPoseMaxHandBelowShoulderRatio; set => _YybArmSwingRaisedPoseMaxHandBelowShoulderRatio = value; }

        [Tooltip("들어 올린 팔 자세에서 허용할 최대 손 수평 도달 거리 비율입니다.")]
        [Range(0f, 1.5f)] [FormerlySerializedAs("YybArmSwingRaisedPoseMaxHandHorizontalReachRatio")]
        [SerializeField] private float _YybArmSwingRaisedPoseMaxHandHorizontalReachRatio= 0f;
        public float YybArmSwingRaisedPoseMaxHandHorizontalReachRatio { get => _YybArmSwingRaisedPoseMaxHandHorizontalReachRatio; set => _YybArmSwingRaisedPoseMaxHandHorizontalReachRatio = value; }

    }
}
