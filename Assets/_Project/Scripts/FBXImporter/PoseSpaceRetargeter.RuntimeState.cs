using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 런타임 상태 필드를 담당하는 partial 분할.
    public partial class PoseSpaceRetargeter
    {
        // --- 내부 변수 ---
        private HumanPoseHandler _ghostHandler;
        private HumanPoseHandler _targetHandler;
        private HumanPose _humanPose;
        private HumanPose _appliedTargetPose;

        private Vector3 _prevGhostPos;
        private static readonly Quaternion LegacyFacingCorrection = Quaternion.Euler(0f, 180f, 0f);
        internal const string LeftThumbBaseHelperNameSuffix = "joint_LeftThumb0";
        internal const string RightThumbBaseHelperNameSuffix = "joint_RightThumb0";
        internal const float ManualThumbOverrideSpreadWarningAngle = 38f;
        internal const float ManualThumbOverrideSpreadFullRiskAngle = 52f;
        internal const float ManualThumbOverrideProjectionMin = 0.358f;
        internal const float ManualThumbOverrideProjectionMax = 0.5f;
        internal const float ManualThumbWorldRotationReferenceToleranceDegrees = 1.5f;
        internal const float ManualThumbDetachedHelperPreserveCurrentReferenceAngleMax = 12f;
        internal const float ManualThumbDetachedHelperOverrideKeepSpreadDeltaMax = 14f;
        internal const float ManualThumbDetachedHelperOverrideKeepProjectionDeltaMax = 0.1f;
        internal const float ManualThumbPoseShapingSuppressMaxRisk = 0.35f;
        internal const float ManualThumbOverrideSuppressRiskThreshold = 0.2f;
        internal const float ManualThumbOverrideRiskIncreaseTolerance = 0.05f;


        private Quaternion _facingCorrection = LegacyFacingCorrection;
        private Quaternion _poseRootRotationCorrection = Quaternion.identity;
        private Vector3 _targetReferenceBodyPosition;
        private bool _hasTargetReferenceBodyPosition;
        private Vector3 _targetRootPoseGuardAnchorPosition;
        private bool _hasTargetRootPoseGuardAnchorPosition;
        private Vector3 _previousBodyRootMotionPosition;
        private bool _hasPreviousBodyRootMotionPosition;
        private float _scaleRatio = 1.0f; // 체형 차이 비율
        private float _movementScaleMultiplier = 1.0f;
        private float _initialGhostHipHeight = 1.0f;
        private float _initialTargetHipHeight = 1.0f;
        private readonly Dictionary<Transform, Vector3> _targetInitialScales = new Dictionary<Transform, Vector3>();
        internal readonly Dictionary<Transform, Vector3> _targetInitialHumanoidLocalPositions = new Dictionary<Transform, Vector3>();
        internal readonly Dictionary<Transform, Vector3> _targetInitialThumbBaseHelperLocalPositions = new Dictionary<Transform, Vector3>();
        internal readonly Dictionary<Transform, Quaternion> _targetInitialThumbLocalRotations = new Dictionary<Transform, Quaternion>();
        internal Vector3 _targetHipsRestLocalPosition;
        internal bool _hasTargetHipsRestLocalPosition;
        internal readonly Dictionary<bool, Transform> _cachedThumbBaseHelpers = new Dictionary<bool, Transform>();
        internal readonly Dictionary<bool, Transform> _cachedThumbBaseExplicitSources = new Dictionary<bool, Transform>();
        internal readonly Dictionary<bool, float> _initialThumbBaseHelperSourceDistances = new Dictionary<bool, float>();
        internal readonly Dictionary<bool, Quaternion> _initialThumbBaseHelperSourceRelativeRotations = new Dictionary<bool, Quaternion>();
        private bool _scaleWarningLogged;
        private bool _positionWarningLogged;
        private bool _poseWarningLogged;
        private bool _muscleClampWarningLogged;
        private bool _anatomyGuardWarningLogged;
        internal bool _thumbGuardWarningLogged;
        internal bool _thumbLocalRotationGuardWarningLogged;
        internal bool _rootDeltaSpikeWarningLogged;
        private bool _appliedPoseClampWarningLogged;
        private bool _hasPoseRootRotationCorrection;
        private float _lastRootDeltaMagnitude = float.NaN;
        private float _maxRootDeltaMagnitude;
        private int _rootDeltaSpikeSkippedCount;
        private readonly RetargetingSpikeClampProcessor _spikeClamp = new RetargetingSpikeClampProcessor();
        private readonly RetargetingGroundingProcessor _grounding = new RetargetingGroundingProcessor();
        internal float _lastEditorFootHeightGroundingReferenceLift = float.NaN;
        private float _recordingStartRootY = float.NaN;
        private float _recordingStartBodyPositionY = float.NaN;
        private float _recordingStartHipsLocalY = float.NaN;
        private float _recordingStartHipsY = float.NaN;
        private float _recordingStartHipsReferenceBeforeLocalY = float.NaN;
        private float _recordingStartHipsReferenceAfterLocalY = float.NaN;
        private float _recordingStartHipsReferenceDeltaY = float.NaN;
        private bool _recordingStartHipsReferenceFlipDetected;
        private string _recordingStartHipsReferenceStage = string.Empty;
        private readonly RetargetingVisualPoseSmoother _visualPoseSmoother = new RetargetingVisualPoseSmoother();
        internal readonly RetargetingLowerBodySegmentDiagnostics _lowerBodySegmentDiagnostics = new RetargetingLowerBodySegmentDiagnostics();
        internal readonly RetargetingFootReferenceDiagnostics _footReferenceDiagnostics = new RetargetingFootReferenceDiagnostics();
        internal readonly RetargetingEndpointStageDiagnostics _endpointStageDiagnostics = new RetargetingEndpointStageDiagnostics();
        private readonly RetargetingStageMuscleDiagnostics _stageMuscleDiagnostics = new RetargetingStageMuscleDiagnostics();
        internal readonly RetargetingThumbGuardDiagnostics _thumbGuardDiagnostics = new RetargetingThumbGuardDiagnostics();
        internal readonly RetargetingSetHumanPoseDiagnostics _setHumanPoseDiagnostics = new RetargetingSetHumanPoseDiagnostics();
        internal readonly RetargetingPostSetEndpointDiagnostics _postSetEndpointDiagnostics = new RetargetingPostSetEndpointDiagnostics();
        private readonly RetargetingMuscleIndexMap _muscleIndexMap = new RetargetingMuscleIndexMap();

        internal static readonly HumanBodyBones[] ThumbRotationBones =
        {
            HumanBodyBones.LeftThumbProximal,
            HumanBodyBones.LeftThumbIntermediate,
            HumanBodyBones.LeftThumbDistal,
            HumanBodyBones.RightThumbProximal,
            HumanBodyBones.RightThumbIntermediate,
            HumanBodyBones.RightThumbDistal
        };
#if UNITY_EDITOR
        internal object _editorPoseReferenceSession;
        internal object _editorFootReferenceSession;
        internal object _editorSleeveReferenceSession;
        internal readonly Dictionary<int, AnimationCurve> _editorHumanoidMuscleCurves = new Dictionary<int, AnimationCurve>();
        internal bool _useEditorHumanoidMuscleReference;
        internal bool _useCompleteEditorHumanoidMuscleReference;
        internal bool _editorHumanoidMuscleReferenceLogged;
        internal HumanoidPoseReferencePlayer _editorHumanoidPoseReferencePlayer;
        internal HumanPose _editorHumanoidPoseReference;
        internal bool _hasEditorHumanoidPoseReferenceForFrame;
        internal bool _editorRootTranslationReferenceLogged;
        internal Animator _editorFingerReferenceAnimator;
        internal bool _useEditorFingerPoseReference;
        internal bool _editorFingerPoseReferenceLogged;
        internal bool _editorBodyRotationReferenceLogged;
        internal bool _hasEditorReferenceBodyPosition;
        internal Vector3 _editorReferenceBodyPosition;
        internal bool _hasEditorReferenceLowestFootRestY;
        internal float _editorReferenceLowestFootRestY;
        internal bool _allowEditorFootHeightGroundingReference;
        internal bool _hasEditorReferenceHipsRestLocalPosition;
        internal Vector3 _editorReferenceHipsRestLocalPosition;
        internal bool _editorHandLocalRotationReferenceLogged;
        internal bool _editorFootLocalRotationReferenceLogged;
        internal bool _editorFootHipsAlignedResidualYawReferenceLogged;
        internal bool _editorHipsLocalPositionReferenceLogged;
        internal bool _editorFootIkPositionReferenceLogged;
        internal bool _editorLowerBodySegmentDirectionReferenceLogged;
        internal bool _editorThumbLocalRotationReferenceLogged;
        internal bool _editorThumbSegmentDirectionReferenceLogged;
        internal bool _editorHandPalmFrameReferenceLogged;
        internal bool _editorThumbBasePositionReferenceLogged;
        internal bool _editorBodyPositionXzReferenceLogged;
#else
        internal Animator _editorFingerReferenceAnimator;
        internal bool _useEditorFingerPoseReference;
        private bool _hasEditorReferenceBodyPosition;
        private Vector3 _editorReferenceBodyPosition;
        private bool _hasEditorReferenceLowestFootRestY;
        private bool _allowEditorFootHeightGroundingReference;
        // 빌드에서는 muscle curve 추출이 불가해 HumanPoseHandler 샘플링 결과만 참조로 사용함.
        private HumanoidPoseReferencePlayer _editorHumanoidPoseReferencePlayer;
        private HumanPose _editorHumanoidPoseReference;
        internal bool _useCompleteEditorHumanoidMuscleReference;
        internal bool _hasEditorHumanoidPoseReferenceForFrame;
#endif

        // --- 초기화 ---
        internal bool _isInitialized = false;
        internal readonly LegacyAnimationDriver _legacyAnimationDriver = new LegacyAnimationDriver();
    }
}
