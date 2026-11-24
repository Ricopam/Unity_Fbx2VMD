using UnityEngine;
using UnityEngine.Serialization;
using System;
using System.IO;
using System.Linq;
using Fbx2Vmd.FileSystem;


namespace Fbx2Vmd.FBXImporter
{
    public partial class FBXVmdPipeline
    {
        [Header("Final Tuning")]
        [Tooltip("높이 보정 (미터 단위). 0.02 = 2cm 올림")]
        [Range(-0.5f, 0.5f)] [FormerlySerializedAs("HeightOffset")]
        [SerializeField] private float _HeightOffset= 0.0f;
        public float HeightOffset { get => _HeightOffset; private set => _HeightOffset = value; }

        [Tooltip("보폭 비율 (1.0 = 자동, 미끄러지면 조절)")]
        [Range(0f, 1.5f)] [FormerlySerializedAs("MovementScaleMultiplier")]
        [SerializeField] private float _MovementScaleMultiplier= 1.0f;
        public float MovementScaleMultiplier { get => _MovementScaleMultiplier; private set => _MovementScaleMultiplier = value; }

        [Header("Root Motion Spike Guard")]
        [Tooltip("FBX root delta가 한 프레임에 과도하게 튀면 순간이동으로 보고 해당 프레임의 추가 root 이동을 무시합니다.")]
        [FormerlySerializedAs("clampRetargetRootDeltaSpikes")]
        [SerializeField] private bool _clampRetargetRootDeltaSpikes= true;
        public bool clampRetargetRootDeltaSpikes { get => _clampRetargetRootDeltaSpikes; private set => _clampRetargetRootDeltaSpikes = value; }

        [Tooltip("한 프레임에 허용할 최대 root 이동량입니다. 일반 춤 동작보다 훨씬 큰 값만 순간이동 후보로 처리합니다.")]
        [Range(0.001f, 1.0f)] [FormerlySerializedAs("MaxRetargetRootDeltaPerFrame")]
        [SerializeField] private float _MaxRetargetRootDeltaPerFrame= 0.25f;
        public float MaxRetargetRootDeltaPerFrame { get => _MaxRetargetRootDeltaPerFrame; private set => _MaxRetargetRootDeltaPerFrame = value; }

        [Tooltip("root delta spike를 무시했을 때 최초 1회 진단 로그를 출력합니다.")]
        [FormerlySerializedAs("logRetargetRootDeltaSpikes")]
        [SerializeField] private bool _logRetargetRootDeltaSpikes= false;
        public bool logRetargetRootDeltaSpikes { get => _logRetargetRootDeltaSpikes; private set => _logRetargetRootDeltaSpikes = value; }

        [Header("Hips Local Position Spike Guard")]
        [Tooltip("Target Hips localPosition outliers are clamped per frame to prevent one-frame body teleport artifacts in exported VMD.")]
        [FormerlySerializedAs("clampRetargetHipsLocalPositionSpikes")]
        [SerializeField] private bool _clampRetargetHipsLocalPositionSpikes= false;
        public bool clampRetargetHipsLocalPositionSpikes { get => _clampRetargetHipsLocalPositionSpikes; private set => _clampRetargetHipsLocalPositionSpikes = value; }

        [Tooltip("Maximum allowed target Hips localPosition movement per frame before it is treated as a visual teleport artifact.")]
        [Range(0.005f, 0.25f)] [FormerlySerializedAs("MaxRetargetHipsLocalPositionDeltaPerFrame")]
        [SerializeField] private float _MaxRetargetHipsLocalPositionDeltaPerFrame= 0.02f;
        public float MaxRetargetHipsLocalPositionDeltaPerFrame { get => _MaxRetargetHipsLocalPositionDeltaPerFrame; private set => _MaxRetargetHipsLocalPositionDeltaPerFrame = value; }

        [Header("Grounding Stability Guard")]
        [Tooltip("발바닥 접지 보정이 한 프레임에 크게 튀지 않도록 부드럽게 반영합니다.")]
        [FormerlySerializedAs("smoothRetargetGrounding")]
        [SerializeField] private bool _smoothRetargetGrounding= true;
        public bool smoothRetargetGrounding { get => _smoothRetargetGrounding; private set => _smoothRetargetGrounding = value; }

        [Tooltip("한 프레임에 허용할 최대 수직 접지 보정값입니다.")]
        [Range(0.001f, 0.2f)] [FormerlySerializedAs("MaxGroundingVerticalStepPerFrame")]
        [SerializeField] private float _MaxGroundingVerticalStepPerFrame= 0.01f;
        public float MaxGroundingVerticalStepPerFrame { get => _MaxGroundingVerticalStepPerFrame; private set => _MaxGroundingVerticalStepPerFrame = value; }

        [Tooltip("접지 보정 목표값을 현재 위치에 반영하는 비율입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("GroundingSmoothing")]
        [SerializeField] private float _GroundingSmoothing= 0.25f;
        public float GroundingSmoothing { get => _GroundingSmoothing; private set => _GroundingSmoothing = value; }

        [Tooltip("이 값보다 작은 발바닥 떨림은 무시합니다.")]
        [Range(0f, 0.05f)] [FormerlySerializedAs("GroundingDeadZone")]
        [SerializeField] private float _GroundingDeadZone= 0.005f;
        public float GroundingDeadZone { get => _GroundingDeadZone; private set => _GroundingDeadZone = value; }

        [Tooltip("초기 접지 확정 뒤에 타깃 root Y를 고정합니다. MMD VMD export에서는 이후 프레임의 발 빠짐을 막기 위해 기본 비활성화합니다.")]
        [FormerlySerializedAs("FreezeRootYAfterInitialGrounding")]
        [SerializeField] private bool _FreezeRootYAfterInitialGrounding= true;
        public bool FreezeRootYAfterInitialGrounding { get => _FreezeRootYAfterInitialGrounding; private set => _FreezeRootYAfterInitialGrounding = value; }

        [Tooltip("전체 renderer bounds 하단이 발바닥 추정치에서 과하게 멀어지면 옷/머리카락/소매 outlier로 보고 접지 기준에서 제외합니다.")]
        [FormerlySerializedAs("rejectRendererGroundingOutliers")]
        [SerializeField] private bool _rejectRendererGroundingOutliers= true;
        public bool rejectRendererGroundingOutliers { get => _rejectRendererGroundingOutliers; private set => _rejectRendererGroundingOutliers = value; }

        [Tooltip("renderer bounds 하단과 발바닥 추정치 사이에 허용할 최대 거리입니다. 이 값을 넘으면 foot 기준 접지로 되돌립니다.")]
        [Range(0.02f, 0.3f)] [FormerlySerializedAs("MaxRendererFootGroundingSeparation")]
        [SerializeField] private float _MaxRendererFootGroundingSeparation= 0.12f;
        public float MaxRendererFootGroundingSeparation { get => _MaxRendererFootGroundingSeparation; private set => _MaxRendererFootGroundingSeparation = value; }

        [Tooltip("최종 메시 bounds 접지 보정의 작은 잔여 오차를 부드럽게 반영해 모델 전체 떨림을 줄입니다.")]
        [FormerlySerializedAs("smoothLateVisualGroundingCorrection")]
        [SerializeField] private bool _smoothLateVisualGroundingCorrection= true;
        public bool smoothLateVisualGroundingCorrection { get => _smoothLateVisualGroundingCorrection; private set => _smoothLateVisualGroundingCorrection = value; }

        [Tooltip("Late visual grounding 잔여 오차가 이 값보다 작으면 smoothing 대상으로 봅니다. 큰 오차는 공중 부유 방지를 위해 즉시 보정합니다.")]
        [Range(0.005f, 0.1f)] [FormerlySerializedAs("LateVisualGroundingSnapThreshold")]
        [SerializeField] private float _LateVisualGroundingSnapThreshold= 0.03f;
        public float LateVisualGroundingSnapThreshold { get => _LateVisualGroundingSnapThreshold; private set => _LateVisualGroundingSnapThreshold = value; }

        [Tooltip("작은 late visual grounding 잔여 오차를 현재 위치에 반영하는 비율입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("LateVisualGroundingSmoothing")]
        [SerializeField] private float _LateVisualGroundingSmoothing= 0.25f;
        public float LateVisualGroundingSmoothing { get => _LateVisualGroundingSmoothing; private set => _LateVisualGroundingSmoothing = value; }

        [Tooltip("작은 late visual grounding smoothing 보정이 한 프레임에 움직일 수 있는 최대 Y 이동량입니다.")]
        [Range(0.001f, 0.05f)] [FormerlySerializedAs("MaxLateVisualGroundingStepPerFrame")]
        [SerializeField] private float _MaxLateVisualGroundingStepPerFrame= 0.003f;
        public float MaxLateVisualGroundingStepPerFrame { get => _MaxLateVisualGroundingStepPerFrame; private set => _MaxLateVisualGroundingStepPerFrame = value; }

        [Header("Final IK Foot Grounding Experiment")]
        [Tooltip("Opt-in only. Adds BipedIK + GrounderBipedIK as a low-weight foot contact experiment after PoseSpaceRetargeter.")]
        [FormerlySerializedAs("enableFinalIkFootGroundingExperiment")]
        [SerializeField] private bool _enableFinalIkFootGroundingExperiment= false;
        public bool enableFinalIkFootGroundingExperiment { get => _enableFinalIkFootGroundingExperiment; set => _enableFinalIkFootGroundingExperiment = value; }

        [Tooltip("GrounderBipedIK master weight. Keep low so it cannot replace PoseSpaceRetargeter output.")]
        [Range(0f, 0.25f)] [FormerlySerializedAs("finalIkFootGroundingWeight")]
        [SerializeField] private float _finalIkFootGroundingWeight= 0.15f;
        public float finalIkFootGroundingWeight { get => _finalIkFootGroundingWeight; private set => _finalIkFootGroundingWeight = value; }

        [Tooltip("Maximum vertical step searched by Final IK grounding.")]
        [Range(0f, 0.08f)] [FormerlySerializedAs("finalIkFootGroundingMaxStep")]
        [SerializeField] private float _finalIkFootGroundingMaxStep= 0.05f;
        public float finalIkFootGroundingMaxStep { get => _finalIkFootGroundingMaxStep; private set => _finalIkFootGroundingMaxStep = value; }

        [Tooltip("Approximate foot radius for Final IK grounding ray/capsule casts.")]
        [Range(0.01f, 0.2f)] [FormerlySerializedAs("finalIkFootGroundingFootRadius")]
        [SerializeField] private float _finalIkFootGroundingFootRadius= 0.06f;
        public float finalIkFootGroundingFootRadius { get => _finalIkFootGroundingFootRadius; private set => _finalIkFootGroundingFootRadius = value; }

        [Tooltip("Velocity prediction for Final IK grounding. Keep zero until visual evidence proves pre-echo is safe.")]
        [Range(0f, 0.2f)] [FormerlySerializedAs("finalIkFootGroundingPrediction")]
        [SerializeField] private float _finalIkFootGroundingPrediction= 0f;
        public float finalIkFootGroundingPrediction { get => _finalIkFootGroundingPrediction; private set => _finalIkFootGroundingPrediction = value; }

        [Tooltip("Foot rotation correction weight. Keep zero for the first contact-only experiment.")]
        [Range(0f, 1f)] [FormerlySerializedAs("finalIkFootGroundingFootRotationWeight")]
        [SerializeField] private float _finalIkFootGroundingFootRotationWeight= 0f;
        public float finalIkFootGroundingFootRotationWeight { get => _finalIkFootGroundingFootRotationWeight; private set => _finalIkFootGroundingFootRotationWeight = value; }

        [Tooltip("Pelvis smoothing damper used by the Final IK grounding solver.")]
        [Range(0f, 1f)] [FormerlySerializedAs("finalIkFootGroundingPelvisDamper")]
        [SerializeField] private float _finalIkFootGroundingPelvisDamper= 0.1f;
        public float finalIkFootGroundingPelvisDamper { get => _finalIkFootGroundingPelvisDamper; private set => _finalIkFootGroundingPelvisDamper = value; }

        [Tooltip("Log when the opt-in Final IK foot grounding experiment is configured.")]
        [FormerlySerializedAs("logFinalIkFootGroundingExperiment")]
        [SerializeField] private bool _logFinalIkFootGroundingExperiment= false;
        public bool logFinalIkFootGroundingExperiment { get => _logFinalIkFootGroundingExperiment; private set => _logFinalIkFootGroundingExperiment = value; }

        [Header("Thumb Digital Orthopedics (Offset)")]
        [Tooltip("양손 공통 엄지 회전 Offset입니다. YYB 엄지 기준축이 수동 기준과 다를 때 최종 렌더 포즈의 엄지 첫 관절 기준축을 보정합니다.")]
        [FormerlySerializedAs("ThumbRotationOffset")]
        [SerializeField] private Vector3 _ThumbRotationOffset= new Vector3(-10f, -30f, 0f);
        public Vector3 ThumbRotationOffset { get => _ThumbRotationOffset; private set => _ThumbRotationOffset = value; }

        [Tooltip("오른손 엄지에는 공통 Offset의 Y/Z축을 반전해 좌우 mirror 축 차이를 보정합니다.")]
        [FormerlySerializedAs("mirrorRightThumbRotationOffset")]
        [SerializeField] private bool _mirrorRightThumbRotationOffset= true;
        public bool mirrorRightThumbRotationOffset { get => _mirrorRightThumbRotationOffset; private set => _mirrorRightThumbRotationOffset = value; }

        [Tooltip("왼손 엄지에 공통 Offset 이후 추가로 더하는 회전 Offset입니다.")]
        [FormerlySerializedAs("LeftThumbRotationOffset")]
        [SerializeField] private Vector3 _LeftThumbRotationOffset= Vector3.zero;
        public Vector3 LeftThumbRotationOffset { get => _LeftThumbRotationOffset; private set => _LeftThumbRotationOffset = value; }

        [Tooltip("오른손 엄지에 공통 Offset 이후 추가로 더하는 회전 Offset입니다.")]
        [FormerlySerializedAs("RightThumbRotationOffset")]
        [SerializeField] private Vector3 _RightThumbRotationOffset= Vector3.zero;
        public Vector3 RightThumbRotationOffset { get => _RightThumbRotationOffset; private set => _RightThumbRotationOffset = value; }

        [Tooltip("켜면 오래된 씬에서 ThumbStretchOffset이 0으로 저장되어 있어도 기본 보정값(-0.1)을 사용합니다.")]
        [FormerlySerializedAs("useDefaultThumbStretchOffsetWhenUnset")]
        [SerializeField] private bool _useDefaultThumbStretchOffsetWhenUnset= true;
        public bool useDefaultThumbStretchOffsetWhenUnset { get => _useDefaultThumbStretchOffsetWhenUnset; private set => _useDefaultThumbStretchOffsetWhenUnset = value; }

        [Tooltip("Muscle Offset (Stretch). Default: -0.1")]
        [Range(-0.5f, 0.5f)] [FormerlySerializedAs("ThumbStretchOffset")]
        [SerializeField] private float _ThumbStretchOffset= DEFAULT_THUMB_STRETCH_OFFSET;
        public float ThumbStretchOffset { get => _ThumbStretchOffset; private set => _ThumbStretchOffset = value; }

        [Header("Smart Curve (Dynamics)")]
        [FormerlySerializedAs("EnableSmartCurve")]
        [SerializeField] private bool _EnableSmartCurve= true;
        public bool EnableSmartCurve { get => _EnableSmartCurve; private set => _EnableSmartCurve = value; }
        [Tooltip("Standard Finger Dampen Strength (0.1 ~ 0.5)")]
        [Range(0.0f, 1.0f)] [FormerlySerializedAs("SmartCurveStrength")]
        [SerializeField] private float _SmartCurveStrength= 0.5f;
        public float SmartCurveStrength { get => _SmartCurveStrength; private set => _SmartCurveStrength = value; }
        [FormerlySerializedAs("StretchThreshold")]
        [SerializeField] private float _StretchThreshold= 0.7f;
        public float StretchThreshold { get => _StretchThreshold; private set => _StretchThreshold = value; }

        [FormerlySerializedAs("EnableThumbSmartCurve")]
        [SerializeField] private bool _EnableThumbSmartCurve= true;
        public bool EnableThumbSmartCurve { get => _EnableThumbSmartCurve; private set => _EnableThumbSmartCurve = value; }
        [Range(0.0f, 1.0f)] [FormerlySerializedAs("ThumbSmartCurveStrength")]
        [SerializeField] private float _ThumbSmartCurveStrength= 0.5f;
        public float ThumbSmartCurveStrength { get => _ThumbSmartCurveStrength; private set => _ThumbSmartCurveStrength = value; }

        [Space(10)]


        #region Private 필드
        internal IFileBrowserService _fileBrowserService;
        private AssimpFBXImporter _fbxImporter;
        private FBXImportController _importController;
        private FBXConversionCoordinator _conversionCoordinator;
        private VMDRecordingController _recordingController;
        private HumanoidMotionPlaybackController _humanoidMotionPlaybackController;
        private HumanoidMotionRecordingController _humanoidMotionRecordingController;
        private NativeSkinningCorrectionPlaybackDriver
            _nativeSkinningCorrectionPlaybackDriver;
        private NativeSkinningPendingPlaybackRequest
            _pendingNativeSkinningPlaybackRequest;
        private string _nativeSkinningCorrectionSetupError = string.Empty;
        public bool IsNativeSkinningCorrectionEnabled { get; private set; } = true;
        public string LastNativeSkinningCorrectionFailure { get; private set; } = string.Empty;
        private HumanoidPoseCorrectionDocument _poseCorrectionDocument;
        private string _preparedMotionName = string.Empty;
        private float _lastRecordingProgressUpdateTime = -999f;
        private float _recordingProgressHoldUntilUnscaledTime;
        private FBXSessionState _sessionState = FBXSessionState.Idle;
        internal bool _isProcessing;
        private GameObject _activeGhostContainer;
        private PoseSpaceRetargeter _activeRetargeter;
        private TargetIdlePoseGuard _idlePoseGuard;
#if UNITY_EDITOR
        private FBXEditorDiagnosticSession _editorDiagnosticSession;
        internal Coroutine _editorDiagnosticBatchAdvanceCoroutine;
#endif
        #endregion

        public enum EditorDiagnosticSmokeSegment
        {
            Head,
            Middle,
            Tail
        }

#if UNITY_EDITOR
        public event Action<string, VmdSaveResult> EditorDiagnosticSmokeFinished
        {
            add => EditorDiagnosticSession.Finished += value;
            remove => EditorDiagnosticSession.Finished -= value;
        }

        internal FBXEditorDiagnosticSession EditorDiagnosticSession
        {
            get
            {
                if (_editorDiagnosticSession == null)
                {
                    _editorDiagnosticSession = new FBXEditorDiagnosticSession();
                }

                return _editorDiagnosticSession;
            }
        }
#endif

        public bool IsProcessing => _isProcessing;

        public float EffectiveThumbProximalMaxLocalAngle
        {
            get { return Mathf.Min(ThumbProximalMaxLocalAngle, THUMB_PROXIMAL_SAFE_MAX_LOCAL_ANGLE); }
        }

        public bool EffectiveThumbLocalRotationGuard
        {
            get
            {
                return enableThumbLocalRotationGuard &&
                    (!ShouldSuppressFinalThumbGuardsWithManualReference ||
                     !disableThumbLocalRotationGuardWithManualFingerReference);
            }
        }

        public bool PreserveManualThumbPoseWithReference
        {
            get
            {
                return _shouldUseManualAnimatorFingerPoseReference &&
                    useManualAnimatorThumbLocalRotationReference &&
                    preserveManualFingerReferenceThumbMuscles &&
                    ShouldSuppressFinalThumbGuardsWithManualReference;
            }
        }

        private bool ShouldSuppressFinalThumbGuardsWithManualReference
        {
            get
            {
                if (!_shouldUseManualAnimatorFingerPoseReference)
                {
                    return false;
                }

                bool helperSyncActive = syncDetachedThumbBaseHelpers &&
                                        detachedThumbBaseHelperSyncWeight > 0f;
                bool palmStabilizeActive = stabilizeDetachedThumbBasePalm &&
                                           detachedThumbBasePalmStabilizeWeight > 0f;
                bool webbingStabilizeActive = stabilizeThumbWebbingCrease &&
                                              thumbWebbingCreaseStabilizeWeight > 0f;
                bool visualLengthGuardActive = enableThumbVisualLengthGuard &&
                                               (ThumbProjectionGuardWeight > 0f ||
                                                ThumbIndexSpreadGuardWeight > 0f ||
                                                ThumbSegmentStraightenWeight > 0f);

                return !(helperSyncActive ||
                         palmStabilizeActive ||
                         webbingStabilizeActive ||
                         visualLengthGuardActive);
            }
        }

        public bool ShouldSaveToImportFolder => _shouldSaveToImportFolder;
        public bool ShouldRecordVmdAfterImport
        {
            get => _shouldRecordVmdAfterImport;
            set => _shouldRecordVmdAfterImport = value;
        }
        internal FBXImportController ImportController => _importController;
        internal FBXConversionCoordinator ConversionCoordinator => _conversionCoordinator;
        internal FBXSessionState SessionState => _sessionState;
        internal string LastSessionMessage { get; private set; } = string.Empty;
        // 설정창 상태 게시용: 마지막 세션 진행률(0~1)과 안내 문구.
        public float SessionProgress { get; private set; }
        public string SessionStatusMessage => LastSessionMessage;
        public float ImportedMotionRecordingProgress =>
            _humanoidMotionRecordingController?.NormalizedRecordingProgress ?? 0f;
        public string ImportedMotionRecordingOutputPath =>
            _humanoidMotionRecordingController?.OutputFilePath ?? string.Empty;
        public bool HasPreparedImportedMotion =>
            _humanoidMotionPlaybackController?.IsPrepared ?? false;
        public bool IsImportedMotionPlaying =>
            _humanoidMotionPlaybackController?.State ==
            HumanoidMotionPlaybackState.Playing;
        public bool IsImportedMotionRecording =>
            _humanoidMotionRecordingController?.IsRecording ?? false;
        // smoke/e2e 증거 수집이 해석된 구간과 캡처 수를 읽을 때 사용함.
        internal HumanoidMotionRecordingController MotionRecordingController =>
            _humanoidMotionRecordingController;

        public bool IsPreparingImportedMotionCorrection =>
            _nativeSkinningCorrectionPlaybackDriver?.IsPreparing ?? false;
        public float ImportedMotionCorrectionPreparationProgress =>
            _nativeSkinningCorrectionPlaybackDriver?.Progress ?? 1f;
        public int ImportedMotionCorrectionProcessedFrameCount =>
            _nativeSkinningCorrectionPlaybackDriver?.ProcessedFrameCount ?? 0;
        public int ImportedMotionCorrectionTotalFrameCount =>
            _nativeSkinningCorrectionPlaybackDriver?.TotalFrameCount ?? 0;
        public int ImportedMotionCurrentFrameIndex =>
            _humanoidMotionPlaybackController?.CurrentFrameIndex ?? 0;
        public int ImportedMotionLastFrameIndex =>
            _humanoidMotionPlaybackController?.LastFrameIndex ?? 0;
        public float ImportedMotionCurrentTimeSeconds =>
            _humanoidMotionPlaybackController?.CurrentTimeSeconds ?? 0f;
        public float ImportedMotionClipLengthSeconds =>
            _humanoidMotionPlaybackController?.ClipLengthSeconds ?? 0f;
        public float ImportedMotionFrameRate =>
            _humanoidMotionPlaybackController?.ClipFrameRate ?? 0f;
        public string ImportedMotionClipName =>
            _humanoidMotionPlaybackController?.ClipName ?? string.Empty;
        public int ImportedMotionPoseCorrectionFrameCount =>
            _poseCorrectionDocument?.FrameCount ?? 0;
        // evidence 캡처처럼 준비된 재생 세션에 goal IK 실험 경로를 켤 때 사용한다.
        internal void SetImportedMotionIkGoalFootDrive(bool isEnabled, float alpha) =>
            _humanoidMotionPlaybackController?.SetIkGoalFootDrive(isEnabled, alpha);
        // 준비된 재생 세션의 정지 발 순간이동 확산을 토글한다 — A/B 검증과 롤백 경로.
        internal void SetImportedMotionFootStepDiffusion(bool isEnabled) =>
            _humanoidMotionPlaybackController?.SetFootStepDiffusion(isEnabled);
#if UNITY_EDITOR
        internal bool ShouldUseEditorHumanoidPlaybackSession =>
            Application.isPlaying && !EditorDiagnosticSession.IsRecordingOverrideActive;
#else
        internal bool ShouldUseEditorHumanoidPlaybackSession => false;
        // 빌드에서는 자동 녹화가 꺼진 임포트를 대화형 재생 세션으로 연다.
        internal bool ShouldUseRuntimeHumanoidPlaybackSession =>
            !ShouldRecordVmdAfterImport;
#endif
        internal bool ShouldFaceTargetToCameraOnIdle =>
            _idlePoseGuard != null && _idlePoseGuard.ShouldFaceTargetToCameraOnIdle;
        public bool ShouldUseLegacyPoseSpaceFacingCorrection => _shouldUseLegacyPoseSpaceFacingCorrection;
        public bool ShouldPreserveFbxRootRotation => _shouldPreserveFbxRootRotation;
        public bool ShouldPreserveRetargetBodyPosition => _shouldPreserveRetargetBodyPosition;
        public bool ShouldUseRetargetBodyPositionXZRootMotion { get => _shouldUseRetargetBodyPositionXZRootMotion; set => _shouldUseRetargetBodyPositionXZRootMotion = value; }
        public bool ShouldStabilizeGroundedFootXZ => _shouldStabilizeGroundedFootXZ;
        public bool ShouldUseEditorHumanoidClipMuscleReference => _shouldUseEditorHumanoidClipMuscleReference;
        public bool ShouldUseEditorHumanoidRootTranslationReference => _shouldUseEditorHumanoidRootTranslationReference;
        public bool ShouldUseManualAnimatorFingerPoseReference { get => _shouldUseManualAnimatorFingerPoseReference; set => _shouldUseManualAnimatorFingerPoseReference = value; }
        public bool ShouldUseManualAnimatorFullBodyPoseReference { get => _shouldUseManualAnimatorFullBodyPoseReference; set => _shouldUseManualAnimatorFullBodyPoseReference = value; }
        public bool ShouldExcludeManualAnimatorFullBodyLowerMuscles { get => _shouldExcludeManualAnimatorFullBodyLowerMuscles; set => _shouldExcludeManualAnimatorFullBodyLowerMuscles = value; }
        public bool ShouldApplyManualAnimatorFullBodyLowerMusclesOnly { get => _shouldApplyManualAnimatorFullBodyLowerMusclesOnly; set => _shouldApplyManualAnimatorFullBodyLowerMusclesOnly = value; }
        public bool ShouldApplyManualAnimatorFullBodyLegTwistMusclesOnly { get => _shouldApplyManualAnimatorFullBodyLegTwistMusclesOnly; set => _shouldApplyManualAnimatorFullBodyLegTwistMusclesOnly = value; }
        public bool ShouldUseSetHumanPoseRightLegTwistOutputReference { get => _shouldUseSetHumanPoseRightLegTwistOutputReference; set => _shouldUseSetHumanPoseRightLegTwistOutputReference = value; }
        public bool ShouldUseManualAnimatorBodyRotationReference { get => _shouldUseManualAnimatorBodyRotationReference; set => _shouldUseManualAnimatorBodyRotationReference = value; }
        public bool ShouldUseManualAnimatorBodyPositionYReference => _shouldUseManualAnimatorBodyPositionYReference;
        public bool ShouldUseManualAnimatorBodyPositionXzReference { get => _shouldUseManualAnimatorBodyPositionXzReference; set => _shouldUseManualAnimatorBodyPositionXzReference = value; }
        public bool ShouldUseManualAnimatorHipsLocalPositionReference { get => _shouldUseManualAnimatorHipsLocalPositionReference; set => _shouldUseManualAnimatorHipsLocalPositionReference = value; }
        public bool ShouldUseManualAnimatorFootHeightGroundingReference { get => _shouldUseManualAnimatorFootHeightGroundingReference; set => _shouldUseManualAnimatorFootHeightGroundingReference = value; }
        public bool ShouldUseManualAnimatorFootLocalRotationReference { get => _shouldUseManualAnimatorFootLocalRotationReference; set => _shouldUseManualAnimatorFootLocalRotationReference = value; }
        public bool ShouldUseManualAnimatorLowerBodySegmentDirectionReference { get => _shouldUseManualAnimatorLowerBodySegmentDirectionReference; set => _shouldUseManualAnimatorLowerBodySegmentDirectionReference = value; }
        public bool ShouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference { get => _shouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference; set => _shouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference = value; }
        public bool ShouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference { get => _shouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference; set => _shouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference = value; }
        public bool ShouldDisableManualAnimatorFootToToesSegmentDirectionReference { get => _shouldDisableManualAnimatorFootToToesSegmentDirectionReference; set => _shouldDisableManualAnimatorFootToToesSegmentDirectionReference = value; }
        public bool ShouldUseManualAnimatorFootHipsAlignedResidualYawReference { get => _shouldUseManualAnimatorFootHipsAlignedResidualYawReference; set => _shouldUseManualAnimatorFootHipsAlignedResidualYawReference = value; }
        public bool ShouldUseLeftSideForPostSetHumanPoseEndpointPosition { get => _shouldUseLeftSideForPostSetHumanPoseEndpointPosition; set => _shouldUseLeftSideForPostSetHumanPoseEndpointPosition = value; }
        public bool ShouldUseLeftSideForPreSetHumanPoseEndpointPosition { get => _shouldUseLeftSideForPreSetHumanPoseEndpointPosition; set => _shouldUseLeftSideForPreSetHumanPoseEndpointPosition = value; }
        public bool ShouldInvertPreSetHumanPoseEndpointPositionBodyX { get => _shouldInvertPreSetHumanPoseEndpointPositionBodyX; set => _shouldInvertPreSetHumanPoseEndpointPositionBodyX = value; }
        public bool ShouldInvertPreSetHumanPoseEndpointPositionBodyZ { get => _shouldInvertPreSetHumanPoseEndpointPositionBodyZ; set => _shouldInvertPreSetHumanPoseEndpointPositionBodyZ = value; }
        public bool ShouldLockTargetHumanoidBonePositions { get => _shouldLockTargetHumanoidBonePositions; set => _shouldLockTargetHumanoidBonePositions = value; }
        public bool ShouldClampRetargetMusclesToHumanRange => clampRetargetMusclesToHumanRange;
        public bool ShouldEnableAnatomicalArmGuard => enableAnatomicalArmGuard;
        public bool ShouldEnableThumbAnatomicalGuard => enableThumbAnatomicalGuard;
        public bool ShouldClampRetargetRootDeltaSpikes => clampRetargetRootDeltaSpikes;
        public bool ShouldFreezeRootYAfterInitialGrounding => FreezeRootYAfterInitialGrounding;
        public bool ShouldEnableThumbLocalRotationGuard => enableThumbLocalRotationGuard;
        public bool ShouldEnableThumbVisualLengthGuard => enableThumbVisualLengthGuard;

                public float EffectiveThumbStretchOffset
        {
            get
            {
                if (useDefaultThumbStretchOffsetWhenUnset && Mathf.Approximately(ThumbStretchOffset, 0f))
                {
                    return DEFAULT_THUMB_STRETCH_OFFSET;
                }

                return ThumbStretchOffset;
            }
        }

        public float EffectiveThumbProjectionMinPalmNormal
        {
            get
            {
                if (Mathf.Approximately(ThumbProjectionMinPalmNormal, LEGACY_THUMB_PROJECTION_MIN_PALM_NORMAL))
                {
                    return DEFAULT_THUMB_PROJECTION_MIN_PALM_NORMAL;
                }

                return Mathf.Clamp01(ThumbProjectionMinPalmNormal);
            }
        }

    }
}
