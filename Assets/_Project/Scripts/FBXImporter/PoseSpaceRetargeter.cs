using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using System;
using System.Collections.Generic;
using Fbx2Vmd.Retargeting;
using RootMotion;
using RootMotion.FinalIK;

namespace Fbx2Vmd.FBXImporter
{
    [DefaultExecutionOrder(CorrectionPassOrder.PoseSpaceRetarget)]
    public partial class PoseSpaceRetargeter : MonoBehaviour
    {
        private const float RecordingStartHipsBaselineFlipWarningThreshold = 0.02f;
        internal const float HipsLocalPositionTargetGapGuardMaxIncreaseMeters = 0.0005f;
        private const string RecordingStartHipsReferenceStagePrewarmComplete = "prewarm-complete";

        public void ResetPlaybackStabilityMetrics()
        {
            _legacyAnimationDriver.ResetStabilityMetrics();
            ResetEditorHumanoidRootTranslationReferenceState();
            _spikeClamp.ResetHipsSpikeState();
            _visualPoseSmoother.ResetVisualPoseHistory();
            _visualPoseSmoother.ResetMetrics();
            _grounding.ResetMetrics();
            _lastEditorFootHeightGroundingReferenceLift = float.NaN;
            _hasEditorReferenceLowestFootRestY = false;
            _allowEditorFootHeightGroundingReference = true;
            ResetRetargetPoseStageDiagnostics();
        }

        public bool PrepareRecordingStartPose(float startTimeSeconds, float playbackSpeed, bool holdPose)
        {
            if (!_legacyAnimationDriver.TryPrepareRecordingStartPose(startTimeSeconds, playbackSpeed, holdPose))
            {
                return false;
            }

            _visualPoseSmoother.ResetVisualPoseHistory();
            return true;
        }

        public void CaptureRecordingStartBaselineSnapshot()
        {
            ResetRecordingStartHipsBaselineDiagnostics();
            _recordingStartHipsReferenceStage = RecordingStartHipsReferenceStagePrewarmComplete;
            if (targetAnimator == null)
            {
                return;
            }

            Vector3 rootPosition = targetAnimator.transform.position;
            _recordingStartRootY = IsFinite(rootPosition) ? rootPosition.y : float.NaN;
            _recordingStartBodyPositionY = TryGetTargetBodyPositionY(out float bodyPositionY)
                ? bodyPositionY
                : float.NaN;

            Transform targetHips = targetAnimator.GetBoneTransform(HumanBodyBones.Hips);
            if (targetHips != null)
            {
                Vector3 hipsLocalPosition = targetHips.localPosition;
                Vector3 hipsPosition = targetHips.position;
                _recordingStartHipsLocalY = IsFinite(hipsLocalPosition) ? hipsLocalPosition.y : float.NaN;
                _recordingStartHipsY = IsFinite(hipsPosition) ? hipsPosition.y : float.NaN;
            }

            _recordingStartHipsReferenceBeforeLocalY = _footReferenceDiagnostics.HipsLocalReferenceBeforeLocalY;
            _recordingStartHipsReferenceAfterLocalY = _footReferenceDiagnostics.HipsLocalReferenceAfterLocalY;
            _recordingStartHipsReferenceDeltaY = _footReferenceDiagnostics.HipsLocalReferenceDeltaY;
            _recordingStartHipsReferenceFlipDetected = IsRecordingStartHipsBaselineFlip(
                _recordingStartHipsReferenceBeforeLocalY,
                _recordingStartHipsReferenceAfterLocalY,
                RecordingStartHipsBaselineFlipWarningThreshold);
        }

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

        internal static Vector3 BuildNaNVector3()
        {
            return new Vector3(float.NaN, float.NaN, float.NaN);
        }

        private static Quaternion BuildNaNQuaternion()
        {
            return new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN);
        }

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

        public void Initialize(RetargetingContext context, RetargetingSettings settings)
        {
            GameObject ghostRoot = context.GhostRoot;
            GameObject targetRoot = context.TargetRoot;
            AnimationClip clip = context.Clip;

            _legacyAnimationDriver.Dispose();

            ghostAnimator = ghostRoot.GetComponent<Animator>();
            targetAnimator = targetRoot.GetComponent<Animator>();
            CaptureTargetInitialTransforms(targetRoot);
            _targetRootPoseGuardAnchorPosition = targetAnimator != null ? targetAnimator.transform.position : Vector3.zero;
            _hasTargetRootPoseGuardAnchorPosition = targetAnimator != null && IsFinite(_targetRootPoseGuardAnchorPosition);

            if (clip == null) return;

            _legacyAnimationDriver.Initialize(ghostRoot, ghostAnimator, clip);

            // 포즈 핸들러 초기화
            if (!ghostAnimator.avatar || !targetAnimator.avatar) return;
            _ghostHandler = new HumanPoseHandler(ghostAnimator.avatar, ghostAnimator.transform);
            _targetHandler = new HumanPoseHandler(targetAnimator.avatar, targetAnimator.transform);
            _humanPose = new HumanPose();
            _appliedTargetPose = new HumanPose();
            CacheTargetReferenceBodyPosition();
            _grounding.CalibrateTargetFootRadius(this);

            // 초기 위치 저장
            _prevGhostPos = ghostAnimator.transform.position;
            ResetEditorHumanoidRootTranslationReferenceState();
            ResetRecordingStartHipsBaselineDiagnostics();
            ResetLastEditorHipsLocalReferenceDiagnostics();
            CacheInitialHipHeights();
            _hasEditorReferenceLowestFootRestY = false;
            _allowEditorFootHeightGroundingReference = false;
            _facingCorrection = settings != null && settings.ShouldUseLegacyPoseSpaceFacingCorrection
                ? LegacyFacingCorrection
                : Quaternion.Inverse(ghostAnimator.transform.rotation) * targetAnimator.transform.rotation;
            _poseRootRotationCorrection = Quaternion.identity;
            _hasPoseRootRotationCorrection = false;
            ApplyRetargetingSettings(settings);

            _isInitialized = true;
            EnsureLateVisualGroundingCorrection();
            Debug.Log("[Master Stage] 시스템 초기화됨. 첫 Update 대기 중...");
        }

        private void OnDestroy()
        {
            _editorHumanoidPoseReferencePlayer?.Dispose();
#if UNITY_EDITOR
            DisposeEditorHumanoidFingerPoseReference();
#endif
            _legacyAnimationDriver.Dispose();
        }

#if UNITY_EDITOR
        public void ConfigureEditorHumanoidMuscleReference(
            AnimationClip referenceClip,
            bool shouldUseCompletePoseReference = false)
        {
            RetargetingEditorReferenceHooks.PoseReference?.ConfigureMuscleReference(this, referenceClip, shouldUseCompletePoseReference);
        }

        public void ConfigureEditorHumanoidRootTranslationReference(AnimationClip referenceClip)
        {
            RetargetingEditorReferenceHooks.PoseReference?.ConfigureRootTranslationReference(this, referenceClip);
        }

        public void ConfigureEditorHumanoidFingerPoseReference(
            GameObject referencePrefab,
            RuntimeAnimatorController referenceController,
            AnimationClip referenceClip,
            bool enableFingerPoseReference = true,
            bool enableFullBodyPoseReference = true,
            float fullBodyPoseReferenceWeight = 1f,
            bool fullBodyPoseExcludeLowerBodyMuscles = false,
            bool fullBodyPoseLowerBodyMusclesOnly = false,
            bool fullBodyPoseLegTwistMusclesOnly = false,
            bool fullBodyPoseRightArmMusclesOnly = false,
            bool fullBodyPoseLeftArmMusclesOnly = false,
            bool fullBodyPoseRightSleeveChainMusclesOnly = false,
            float fullBodyPoseFrameGateStart = 0f,
            float fullBodyPoseFrameGateEnd = 0f)
        {
            EditorFingerPoseReferenceOptions options = new EditorFingerPoseReferenceOptions
            {
                EnableFingerPoseReference = enableFingerPoseReference,
                EnableFullBodyPoseReference = enableFullBodyPoseReference,
                FullBodyPoseReferenceWeight = fullBodyPoseReferenceWeight,
                FullBodyPoseExcludeLowerBodyMuscles = fullBodyPoseExcludeLowerBodyMuscles,
                FullBodyPoseLowerBodyMusclesOnly = fullBodyPoseLowerBodyMusclesOnly,
                FullBodyPoseLegTwistMusclesOnly = fullBodyPoseLegTwistMusclesOnly,
                FullBodyPoseRightArmMusclesOnly = fullBodyPoseRightArmMusclesOnly,
                FullBodyPoseLeftArmMusclesOnly = fullBodyPoseLeftArmMusclesOnly,
                FullBodyPoseRightSleeveChainMusclesOnly = fullBodyPoseRightSleeveChainMusclesOnly,
                FullBodyPoseFrameGateStart = fullBodyPoseFrameGateStart,
                FullBodyPoseFrameGateEnd = fullBodyPoseFrameGateEnd,
            };
            RetargetingEditorReferenceHooks.PoseReference?.ConfigureFingerPoseReference(
                this,
                referencePrefab,
                referenceController,
                referenceClip,
                options);
        }

#endif

        // 빌드에서는 에디터 muscle 클립이 없어 임포트된 소스 모델을 HumanPoseHandler로 샘플링해 참조로 사용함.
        internal void ConfigureRuntimeHumanoidPoseReference(
            GameObject referenceSourceModel,
            AnimationClip referenceClip)
        {
            _editorHumanoidPoseReferencePlayer?.Dispose();
            _editorHumanoidPoseReferencePlayer = null;
            _useCompleteEditorHumanoidMuscleReference = false;
            _hasEditorHumanoidPoseReferenceForFrame = false;
            _hasEditorReferenceBodyPosition = false;

            if (referenceSourceModel == null || referenceClip == null)
            {
                return;
            }

            try
            {
                _editorHumanoidPoseReferencePlayer = new HumanoidPoseReferencePlayer();
                _editorHumanoidPoseReferencePlayer.InitializeFromSourceModel(
                    referenceSourceModel,
                    referenceClip);
                _useCompleteEditorHumanoidMuscleReference =
                    _editorHumanoidPoseReferencePlayer.IsInitialized;
            }
            catch (Exception exception)
            {
                _editorHumanoidPoseReferencePlayer?.Dispose();
                _editorHumanoidPoseReferencePlayer = null;
                Debug.LogWarning(
                    $"[PoseSpaceRetargeter] 런타임 Humanoid 포즈 참조를 준비하지 못했습니다: {exception.Message}");
            }
        }


        private void ApplyEditorHumanoidMuscleReference(ref HumanPose pose)
        {
#if UNITY_EDITOR
            ApplyEditorHumanoidMuscleReferenceEditor(ref pose);
#else
            ApplyRuntimeHumanoidPoseReference(ref pose);
#endif
        }

#if !UNITY_EDITOR
        private void ApplyRuntimeHumanoidPoseReference(ref HumanPose pose)
        {
            _hasEditorHumanoidPoseReferenceForFrame = false;
            if (pose.muscles == null ||
                !_useCompleteEditorHumanoidMuscleReference ||
                _editorHumanoidPoseReferencePlayer == null ||
                !_editorHumanoidPoseReferencePlayer.TryEvaluateAt(
                    _legacyAnimationDriver.CurrentTime,
                    ref _editorHumanoidPoseReference))
            {
                return;
            }

            int muscleCount = Mathf.Min(
                pose.muscles.Length,
                _editorHumanoidPoseReference.muscles.Length);
            Array.Copy(
                _editorHumanoidPoseReference.muscles,
                pose.muscles,
                muscleCount);
            pose.bodyRotation = _editorHumanoidPoseReference.bodyRotation;
            _editorReferenceBodyPosition = _editorHumanoidPoseReference.bodyPosition;
            _hasEditorReferenceBodyPosition = IsFinite(_editorReferenceBodyPosition);
            _hasEditorHumanoidPoseReferenceForFrame = true;
        }
#endif

        private void ApplyEditorHumanoidFingerPoseReference(ref HumanPose pose)
        {
#if UNITY_EDITOR
            ApplyEditorHumanoidFingerPoseReferenceEditor(ref pose);
#endif
        }

        private void ApplyEditorHumanoidBodyRotationReference(ref HumanPose pose)
        {
#if UNITY_EDITOR
            ApplyEditorHumanoidBodyRotationReferenceEditor(ref pose);
#endif
        }

        private void ResetLastEditorHipsLocalReferenceDiagnostics()
        {
            _footReferenceDiagnostics.ResetHipsLocal();
        }

        private void ResetRecordingStartHipsBaselineDiagnostics()
        {
            _recordingStartRootY = float.NaN;
            _recordingStartBodyPositionY = float.NaN;
            _recordingStartHipsLocalY = float.NaN;
            _recordingStartHipsY = float.NaN;
            _recordingStartHipsReferenceBeforeLocalY = float.NaN;
            _recordingStartHipsReferenceAfterLocalY = float.NaN;
            _recordingStartHipsReferenceDeltaY = float.NaN;
            _recordingStartHipsReferenceFlipDetected = false;
            _recordingStartHipsReferenceStage = string.Empty;
        }

        private bool TryGetTargetBodyPositionY(out float bodyPositionY)
        {
            bodyPositionY = float.NaN;
            if (_targetHandler == null)
            {
                return false;
            }

            HumanPose targetPose = new HumanPose();
            _targetHandler.GetHumanPose(ref targetPose);
            if (!IsFinite(targetPose.bodyPosition))
            {
                return false;
            }

            bodyPositionY = targetPose.bodyPosition.y;
            return true;
        }

        private static bool IsRecordingStartHipsBaselineFlip(float beforeLocalY, float afterLocalY, float warningThreshold)
        {
            if (!IsFinite(beforeLocalY) || !IsFinite(afterLocalY) || !IsFinite(warningThreshold))
            {
                return false;
            }

            return Mathf.Abs(afterLocalY - beforeLocalY) > Mathf.Max(0f, warningThreshold);
        }

        internal bool ShouldSuppressCompetingManualThumbOverride(bool leftHand)
        {
#if UNITY_EDITOR
            return RetargetingEditorReferenceHooks.ThumbReference?.ShouldSuppressCompetingManualThumbOverride(this, leftHand) ?? false;
#else
            return false;
#endif
        }

        internal bool ShouldKeepDetachedHelperManualThumbOverrides(bool leftHand)
        {
#if UNITY_EDITOR
            return RetargetingEditorReferenceHooks.ThumbReference?.ShouldKeepDetachedHelperManualThumbOverrides(this, leftHand) ?? false;
#else
            return false;
#endif
        }

        public bool TryGetHighRiskManualThumbPoseConstraintOverrides(
            bool leftHand,
            out float projectionMin,
            out float projectionMax,
            out float maxSpreadAngle)
        {
#if UNITY_EDITOR
            projectionMin = 0f;
            projectionMax = 0f;
            maxSpreadAngle = 0f;
            return RetargetingEditorReferenceHooks.ThumbReference?.TryGetHighRiskManualThumbPoseConstraintOverrides(
                this,
                leftHand,
                out projectionMin,
                out projectionMax,
                out maxSpreadAngle) ?? false;
#else
            projectionMin = 0f;
            projectionMax = 0f;
            maxSpreadAngle = 0f;
            return false;
#endif
        }

        public string BuildThumbHelperRelationshipDebugSummary(bool leftHand)
        {
#if UNITY_EDITOR
            return RetargetingEditorReferenceHooks.ThumbReference?.BuildThumbHelperRelationshipDebugSummary(this, leftHand);
#else
            return leftHand
                ? "side=L, state=editor-only"
                : "side=R, state=editor-only";
#endif
        }

        internal Transform GetCachedThumbBaseHelper(bool leftHand)
        {
#if UNITY_EDITOR
            return RetargetingEditorReferenceHooks.ThumbReference?.GetCachedThumbBaseHelper(this, leftHand);
#else
            return null;
#endif
        }

        internal Transform GetCachedExplicitThumbBaseSource(bool leftHand)
        {
#if UNITY_EDITOR
            return RetargetingEditorReferenceHooks.ThumbReference?.GetCachedExplicitThumbBaseSource(this, leftHand);
#else
            return null;
#endif
        }

        internal bool TryEvaluateThumbManualReferenceFrameDeviation(
            bool leftHand,
            Transform targetThumb,
            Quaternion candidateWorldRotation,
            out float currentDeviation,
            out float candidateDeviation)
        {
#if UNITY_EDITOR
            currentDeviation = float.NaN;
            candidateDeviation = float.NaN;
            return RetargetingEditorReferenceHooks.ThumbReference?.TryEvaluateManualReferenceFrameDeviation(
                this,
                leftHand,
                targetThumb,
                candidateWorldRotation,
                out currentDeviation,
                out candidateDeviation) ?? false;
#else
            currentDeviation = float.NaN;
            candidateDeviation = float.NaN;
            return false;
#endif
        }

        internal bool TryEvaluateThumbLocalRotationOverrideRisk(
            bool leftHand,
            Transform targetThumb,
            Quaternion candidateRotation,
            out float risk)
        {
#if UNITY_EDITOR
            risk = float.NaN;
            return RetargetingEditorReferenceHooks.ThumbReference?.TryEvaluateThumbLocalRotationOverrideRisk(this, leftHand, targetThumb, candidateRotation, out risk) ?? false;
#else
            risk = float.NaN;
            return false;
#endif
        }

#if UNITY_EDITOR
        private void ApplyEditorHumanoidMuscleReferenceEditor(ref HumanPose pose)
        {
            RetargetingEditorReferenceHooks.PoseReference?.ApplyMuscleReference(this, ref pose);
        }

        private void ApplyEditorHumanoidFingerPoseReferenceEditor(ref HumanPose pose)
        {
            RetargetingEditorReferenceHooks.PoseReference?.ApplyFingerPoseReference(this, ref pose);
        }

        private void ApplyEditorHumanoidBodyRotationReferenceEditor(ref HumanPose pose)
        {
            RetargetingEditorReferenceHooks.PoseReference?.ApplyBodyRotationReference(this, ref pose);
        }

        internal bool ShouldApplyPostSetHumanPoseRightEndpointPositionFrameGate()
        {
            return ShouldApplyManualReferenceFrameGate(
                postSetHumanPoseRightEndpointPositionReferenceFrameGateStart,
                postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd);
        }

        internal bool ShouldApplyPreSetHumanPoseRightEndpointPositionFrameGate()
        {
            return ShouldApplyManualReferenceFrameGate(
                preSetHumanPoseRightEndpointPositionReferenceFrameGateStart,
                preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd);
        }

        internal bool ShouldApplyManualReferenceFrameGate(float startFrame, float endFrame)
        {
            if (!ManualPoseReferenceApplier.HasActiveFrameGate(startFrame, endFrame))
            {
                return true;
            }

            float frameRate = Mathf.Clamp(legacyAnimationVisualFrameRate, 15f, 120f);
            int currentFrame = Mathf.RoundToInt(_legacyAnimationDriver.CurrentTime * frameRate);
            return ManualPoseReferenceApplier.IsFrameWithinGate(
                currentFrame,
                startFrame,
                endFrame);
        }

        internal float ResolveManualAnimatorBodyPositionXzFrameGateWeight()
        {
            float start = Mathf.Max(0f, manualAnimatorBodyPositionXzReferenceFrameGateStart);
            float end = Mathf.Max(0f, manualAnimatorBodyPositionXzReferenceFrameGateEnd);
            float frameRate = Mathf.Clamp(legacyAnimationVisualFrameRate, 15f, 120f);
            float currentFrame = Mathf.RoundToInt(_legacyAnimationDriver.CurrentTime * frameRate);
            return ManualPoseReferenceApplier.CalculateBodyPositionXzFrameGateWeight(
                currentFrame,
                start,
                end,
                Mathf.Max(0f, manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames));
        }

        internal static bool TryNormalize(Vector3 value, out Vector3 normalized)
        {
            normalized = Vector3.zero;
            if (!IsFinite(value) || value.sqrMagnitude <= 0.00000001f)
            {
                return false;
            }

            normalized = value.normalized;
            return IsFinite(normalized);
        }

        private void DisposeEditorHumanoidFingerPoseReference()
        {
            RetargetingEditorReferenceHooks.PoseReference?.DisposeFingerPoseReference(this);
        }

        private bool ShouldApplyRightSleeveSilhouetteLocalOffsetFrameGate()
        {
            return RetargetingEditorReferenceHooks.SleeveReference?.ShouldApplyRightSleeveSilhouetteLocalOffsetFrameGate(this) ?? false;
        }

        internal bool ShouldApplySingleFrameFallbackReferenceFrameGate(float startFrame, float endFrame)
        {
            if (!ManualPoseReferenceApplier.HasConfiguredFrameGate(startFrame, endFrame))
            {
                return true;
            }

            float frameRate = Mathf.Clamp(legacyAnimationVisualFrameRate, 1f, 240f);
            int currentFrame = Mathf.RoundToInt(_legacyAnimationDriver.CurrentTime * frameRate);
            return ManualPoseReferenceApplier.IsFrameWithinSingleFrameFallbackGate(
                currentFrame,
                startFrame,
                endFrame);
        }

        private void ApplyRightSleeveSilhouetteLocalOffsetReference()
        {
            RetargetingEditorReferenceHooks.SleeveReference?.ApplyRightSleeveSilhouetteLocalOffsetReference(this);
        }

        private void ApplyRightSleeveSilhouetteLocalOffsetToTransform(Transform target, Vector3 offset)
        {
            RetargetingEditorReferenceHooks.SleeveReference?.ApplyRightSleeveSilhouetteLocalOffsetToTransform(this, target, offset);
        }

        private void RestoreRightSleeveSilhouetteLocalOffsetReference()
        {
            RetargetingEditorReferenceHooks.SleeveReference?.RestoreRightSleeveSilhouetteLocalOffsetReference(this);
        }

#endif

        private float CalculateSafeScaleRatio(Transform ghostHip, Transform targetHip)
        {
            bool hasAnimatorScale = ghostAnimator != null && targetAnimator != null;
            float ghostHumanScale = hasAnimatorScale ? ghostAnimator.humanScale : 0f;
            float targetHumanScale = hasAnimatorScale ? targetAnimator.humanScale : 0f;
            bool hasHipPositions = ghostHip != null && targetHip != null;
            float ghostHipY = hasHipPositions ? ghostHip.position.y : 0f;
            float targetHipY = hasHipPositions ? targetHip.position.y : 0f;

            float ratio = RetargetingScaleRatioCalculator.CalculateSafeScaleRatio(
                _scaleRatio,
                hasAnimatorScale,
                ghostHumanScale,
                targetHumanScale,
                _initialGhostHipHeight,
                _initialTargetHipHeight,
                hasHipPositions,
                ghostHipY,
                targetHipY,
                out bool usedInvalidFallback);
            if (usedInvalidFallback)
            {
                LogPoseWarning("Invalid retarget scale ratio. Falling back to 1.0.");
            }

            return ratio;
        }

        private void CacheInitialHipHeights()
        {
            Transform ghostHip = ghostAnimator != null ? ghostAnimator.GetBoneTransform(HumanBodyBones.Hips) : null;
            Transform targetHip = targetAnimator != null ? targetAnimator.GetBoneTransform(HumanBodyBones.Hips) : null;

            if (ghostHip != null && IsFinite(ghostHip.position.y) && Mathf.Abs(ghostHip.position.y) > 0.01f)
            {
                _initialGhostHipHeight = Mathf.Abs(ghostHip.position.y);
            }

            if (targetHip != null && IsFinite(targetHip.position.y) && Mathf.Abs(targetHip.position.y) > 0.01f)
            {
                _initialTargetHipHeight = Mathf.Abs(targetHip.position.y);
            }
        }

        private void CacheTargetReferenceBodyPosition()
        {
            _hasTargetReferenceBodyPosition = false;
            _hasPreviousBodyRootMotionPosition = false;

            if (_targetHandler == null)
            {
                return;
            }

            var targetPose = new HumanPose();
            _targetHandler.GetHumanPose(ref targetPose);
            if (!IsFinite(targetPose.bodyPosition))
            {
                return;
            }

            _targetReferenceBodyPosition = targetPose.bodyPosition;
            _hasTargetReferenceBodyPosition = true;
        }

        private Vector3 ExtractBodyPositionXZRootDelta(Vector3 bodyPosition)
        {
            if (!useBodyPositionXZRootMotion || !_hasTargetReferenceBodyPosition)
            {
                _hasPreviousBodyRootMotionPosition = false;
                return Vector3.zero;
            }

            Vector3 current = new Vector3(bodyPosition.x, 0f, bodyPosition.z);
            if (!IsFinite(current))
            {
                _hasPreviousBodyRootMotionPosition = false;
                return Vector3.zero;
            }

            if (!_hasPreviousBodyRootMotionPosition)
            {
                _previousBodyRootMotionPosition = current;
                _hasPreviousBodyRootMotionPosition = true;
                return Vector3.zero;
            }

            Vector3 delta = current - _previousBodyRootMotionPosition;
            _previousBodyRootMotionPosition = current;
            if (!IsFinite(delta))
            {
                return Vector3.zero;
            }

            delta.y = 0f;
            return delta;
        }

        private Vector3 ExtractEditorRootTranslationDelta(Vector3 ghostDelta)
        {
#if UNITY_EDITOR
            return RetargetingEditorReferenceHooks.PoseReference?.ExtractRootTranslationDelta(this, ghostDelta) ?? Vector3.zero;
#else
            return Vector3.zero;
#endif
        }

        private void ResetEditorHumanoidRootTranslationReferenceState()
        {
#if UNITY_EDITOR
            RetargetingEditorReferenceHooks.PoseReference?.ResetRootTranslationState(this);
#endif
        }

#if UNITY_EDITOR
#endif

        private static Vector3 FlattenXZ(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        internal void LogPoseWarning(string message)
        {
            if (_poseWarningLogged)
            {
                return;
            }

            Debug.LogWarning($"[PoseSpaceRetargeter] {message}");
            _poseWarningLogged = true;
        }

        internal static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        internal static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        internal static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        internal static bool IsFinite(HumanPose pose)
        {
            if (!IsFinite(pose.bodyPosition) || !IsFinite(pose.bodyRotation))
            {
                return false;
            }

            if (pose.muscles == null)
            {
                return true;
            }

            foreach (float muscle in pose.muscles)
            {
                if (!IsFinite(muscle))
                {
                    return false;
                }
            }

            return true;
        }

        private void ClampPoseMuscles(ref HumanPose pose)
        {
            if (!ShouldApplyRetargetSafetyGuards() ||
                !clampMusclesToHumanRange ||
                pose.muscles == null)
            {
                return;
            }

            int clampedCount =
                HumanoidMuscleRangeLimiter.ClampNonFingerMusclesInPlace(pose.muscles);

            if (clampedCount > 0 && !_muscleClampWarningLogged)
            {
                Debug.LogWarning($"[PoseSpaceRetargeter] Humanoid muscle 값 {clampedCount}개가 안전 범위를 벗어나 [-1, 1]로 제한되었습니다.");
                _muscleClampWarningLogged = true;
            }
        }

        private void ApplySetHumanPoseRightLegTwistOutputReference(HumanPose inputPose)
        {
            if (!ShouldUseSetHumanPoseRightLegTwistOutputReference ||
                _targetHandler == null ||
                inputPose.muscles == null)
            {
                return;
            }

            _targetHandler.GetHumanPose(ref _appliedTargetPose);
            if (_appliedTargetPose.muscles == null)
            {
                return;
            }

            float weight = Mathf.Clamp01(setHumanPoseRightLegTwistOutputReferenceWeight);
            float maxDelta = Mathf.Max(0f, setHumanPoseRightLegTwistOutputReferenceMaxDelta);
            if (weight <= 0f || maxDelta <= 0f)
            {
                return;
            }

            bool changed = ManualPoseReferenceApplier.TryApplyBoundedMuscleOutputReference(
                ref _appliedTargetPose,
                inputPose,
                _muscleIndexMap.Get(HumanMuscleSlot.RightUpperLegTwistInOut),
                weight,
                maxDelta);
            changed |= ManualPoseReferenceApplier.TryApplyBoundedMuscleOutputReference(
                ref _appliedTargetPose,
                inputPose,
                _muscleIndexMap.Get(HumanMuscleSlot.RightLowerLegTwistInOut),
                weight,
                maxDelta);

            if (changed)
            {
                _targetHandler.SetHumanPose(ref _appliedTargetPose);
            }
        }

        private void ApplyEditorHumanoidBoneLocalRotationReference()
        {
#if UNITY_EDITOR
            if (!IsCompleteEditorHumanoidPoseReferenceActive)
            {
                return;
            }

            // 완전한 Native 기준 포즈는 HumanPose 재해석 과정에서 범위를 벗어난 muscle이 손실될 수 있음.
            // 동일 모델의 Humanoid 본 회전만 복구하고 root 이동, 본 위치와 scale은 기존 경로가 소유함.
            _editorHumanoidPoseReferencePlayer.TryApplyHumanoidBoneLocalRotationsTo(targetAnimator);
#endif
        }

        private void ApplyAnatomicalArmGuard(ref HumanPose pose)
        {
            if (!ShouldApplyRetargetSafetyGuards() ||
                !enableAnatomicalArmGuard ||
                pose.muscles == null)
            {
                return;
            }

            int changed = HumanoidArmDeformationGuard.ClampAnatomicalArmMuscles(
                ref pose,
                armStretchMuscleLimit,
                upperArmTwistMuscleLimit,
                lowerArmTwistMuscleLimit,
                clampArmStretchMuscles);

            if (changed > 0 && !_anatomyGuardWarningLogged)
            {
                Debug.LogWarning($"[PoseSpaceRetargeter] 팔 변형 방지를 위해 Humanoid arm muscle {changed}개를 제한했습니다.");
                _anatomyGuardWarningLogged = true;
            }
        }

        private void ClampAppliedTargetPose()
        {
            if (!ShouldApplyRetargetSafetyGuards() ||
                (!clampMusclesToHumanRange && !enableAnatomicalArmGuard && !enableThumbAnatomicalGuard) ||
                _targetHandler == null)
            {
                return;
            }

            _targetHandler.GetHumanPose(ref _appliedTargetPose);
            if (!IsFinite(_appliedTargetPose))
            {
                LogPoseWarning("Applied target HumanPose contains non-finite values. Skipping applied pose clamp.");
                return;
            }

            int changed = 0;
            if (clampMusclesToHumanRange)
            {
                changed += HumanoidMuscleRangeLimiter.ClampNonFingerMusclesInPlace(
                    _appliedTargetPose.muscles);
            }

            if (enableAnatomicalArmGuard)
            {
                changed += HumanoidArmDeformationGuard.ClampAnatomicalArmMuscles(
                    ref _appliedTargetPose,
                    armStretchMuscleLimit,
                    upperArmTwistMuscleLimit,
                    lowerArmTwistMuscleLimit,
                    clampArmStretchMuscles);
            }

            changed += ThumbMuscleGuardPolicy.ApplyAnatomicalGuard(this, ref _appliedTargetPose, false);

            if (changed <= 0)
            {
                return;
            }

            _targetHandler.SetHumanPose(ref _appliedTargetPose);
            if (!_appliedPoseClampWarningLogged)
            {
                Debug.LogWarning($"[PoseSpaceRetargeter] Target 적용 후 범위를 벗어난 Humanoid muscle {changed}개를 추가 보정했습니다.");
                _appliedPoseClampWarningLogged = true;
            }
        }

        private bool ShouldApplyRetargetSafetyGuards()
        {
#if UNITY_EDITOR
            return RetargetingMuscleReferencePolicy.ShouldApplyRetargetSafetyGuards(
                _useCompleteEditorHumanoidMuscleReference);
#else
            return true;
#endif
        }

        public bool IsManualThumbLocalRotationReferenceActive
        {
            get
            {
#if UNITY_EDITOR
                return useManualAnimatorThumbLocalRotationReference &&
                    _useEditorFingerPoseReference &&
                    _editorFingerReferenceAnimator != null;
#else
                return false;
#endif
            }
        }

        public bool ShouldSuppressThumbPoseShapingGuard
        {
            get
            {
#if UNITY_EDITOR
                return ShouldSuppressThumbPoseShapingGuardForHand(true) ||
                    ShouldSuppressThumbPoseShapingGuardForHand(false);
#else
                return false;
#endif
            }
        }

        public bool ShouldSuppressLeftThumbPoseShapingGuard
        {
            get
            {
#if UNITY_EDITOR
                return ShouldSuppressThumbPoseShapingGuardForHand(true);
#else
                return false;
#endif
            }
        }

        public bool ShouldSuppressRightThumbPoseShapingGuard
        {
            get
            {
#if UNITY_EDITOR
                return ShouldSuppressThumbPoseShapingGuardForHand(false);
#else
                return false;
#endif
            }
        }

        public bool ShouldSuppressThumbPoseShapingGuardForHand(bool leftHand)
        {
            return ThumbMuscleGuardPolicy.ShouldSuppressPoseShapingGuardForHand(this, leftHand);
        }


        public void ResetThumbWorldRotationPreserveDiagnostics()
        {
            _thumbGuardDiagnostics.ResetWorldRotationPreserve();
        }



        public bool ShouldPreserveManualThumbWorldRotationCorrection(
            HumanBodyBones thumbBone,
            Transform targetThumb,
            Quaternion candidateWorldRotation)
        {
            return ThumbWorldRotationPreservePolicy.ShouldPreserveManualWorldRotationCorrection(this, thumbBone, targetThumb, candidateWorldRotation);
        }


        internal bool HasDetachedThumbBaseHelperRelationship(HumanBodyBones thumbBone, bool leftHand)
        {
            return ThumbBaseHelperResolver.HasDetachedRelationship(this, thumbBone, leftHand);
        }

        internal static bool TryGetThumbBoneSide(HumanBodyBones thumbBone, out bool leftHand)
        {
            return ThumbGuardPolicy.TryGetBoneSide(thumbBone, out leftHand);
        }


        private void CaptureTargetInitialTransforms(GameObject targetRoot)
        {
            _targetInitialScales.Clear();
            _targetInitialHumanoidLocalPositions.Clear();
            _targetInitialThumbBaseHelperLocalPositions.Clear();
            _targetInitialThumbLocalRotations.Clear();
            _cachedThumbBaseHelpers.Clear();
            _cachedThumbBaseExplicitSources.Clear();
            _initialThumbBaseHelperSourceDistances.Clear();
            _initialThumbBaseHelperSourceRelativeRotations.Clear();
            _scaleWarningLogged = false;
            _positionWarningLogged = false;
            _muscleClampWarningLogged = false;
            _anatomyGuardWarningLogged = false;
            _thumbGuardWarningLogged = false;
            _thumbLocalRotationGuardWarningLogged = false;
            _rootDeltaSpikeWarningLogged = false;
            _hasPreviousBodyRootMotionPosition = false;
            _spikeClamp.ResetHipsSpikeState();
            _lastRootDeltaMagnitude = float.NaN;
            _maxRootDeltaMagnitude = 0f;
            _rootDeltaSpikeSkippedCount = 0;
            _spikeClamp.ResetRootPositionMetrics();
            _legacyAnimationDriver.ResetStabilityMetrics();
            _visualPoseSmoother.ResetSession();
            _grounding.ResetSession();
            _lastEditorFootHeightGroundingReferenceLift = float.NaN;
            _appliedPoseClampWarningLogged = false;
            _targetHipsRestLocalPosition = Vector3.zero;
            _hasTargetHipsRestLocalPosition = false;

            foreach (Transform targetBone in targetRoot.GetComponentsInChildren<Transform>(true))
            {
                _targetInitialScales[targetBone] = targetBone.localScale;
            }

            if (targetAnimator == null || !targetAnimator.isHuman)
            {
                return;
            }

            for (int i = (int)HumanBodyBones.Hips; i < (int)HumanBodyBones.LastBone; i++)
            {
                HumanBodyBones bone = (HumanBodyBones)i;
                Transform targetBone = targetAnimator.GetBoneTransform(bone);
                if (bone == HumanBodyBones.Hips)
                {
                    if (targetBone != null && IsFinite(targetBone.localPosition))
                    {
                        _targetHipsRestLocalPosition = targetBone.localPosition;
                        _hasTargetHipsRestLocalPosition = true;
                    }

                    continue;
                }

                if (targetBone == null || _targetInitialHumanoidLocalPositions.ContainsKey(targetBone))
                {
                    continue;
                }

                _targetInitialHumanoidLocalPositions[targetBone] = targetBone.localPosition;
            }

            foreach (HumanBodyBones thumbBone in ThumbRotationBones)
            {
                Transform thumbTransform = targetAnimator.GetBoneTransform(thumbBone);
                if (thumbTransform == null || _targetInitialThumbLocalRotations.ContainsKey(thumbTransform))
                {
                    continue;
                }

                _targetInitialThumbLocalRotations[thumbTransform] = thumbTransform.localRotation;
            }

            ThumbBaseHelperResolver.CaptureState(this, true);
            ThumbBaseHelperResolver.CaptureState(this, false);
        }

        internal static string FormatDebugFloat(float value)
        {
            return IsFinite(value) ? value.ToString("F4") : "NaN";
        }

        internal static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return "<none>";
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder(transform.name);
            Transform current = transform.parent;
            while (current != null)
            {
                builder.Insert(0, current.name + "/");
                current = current.parent;
            }

            return builder.ToString();
        }

        private void RestoreTargetHumanoidLocalPositions()
        {
            if (!ShouldLockTargetHumanoidBonePositions)
            {
                return;
            }

            foreach (KeyValuePair<Transform, Vector3> positionSnapshot in _targetInitialHumanoidLocalPositions)
            {
                Transform targetBone = positionSnapshot.Key;
                if (targetBone == null)
                {
                    continue;
                }

                if ((targetBone.localPosition - positionSnapshot.Value).sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                if (!_positionWarningLogged)
                {
                    Debug.LogWarning($"[PoseSpaceRetargeter] Target humanoid bone localPosition changed during retargeting. Restoring original bone length. First bone: {targetBone.name}");
                    _positionWarningLogged = true;
                }

                targetBone.localPosition = positionSnapshot.Value;
            }
        }

        private void RestoreTargetLocalScales()
        {
            foreach (KeyValuePair<Transform, Vector3> scaleSnapshot in _targetInitialScales)
            {
                Transform targetBone = scaleSnapshot.Key;
                if (targetBone == null)
                {
                    continue;
                }

                if ((targetBone.localScale - scaleSnapshot.Value).sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                if (!_scaleWarningLogged)
                {
                    Debug.LogWarning($"[PoseSpaceRetargeter] Target bone scale changed during retargeting. Restoring original localScale. First bone: {targetBone.name}");
                    _scaleWarningLogged = true;
                }

                targetBone.localScale = scaleSnapshot.Value;
            }
        }

        private void EnsureLateVisualGroundingCorrection()
        {
            if (!enableLateVisualGroundingCorrection)
            {
                return;
            }

            PoseSpaceLateVisualGroundingCorrection correction = GetComponent<PoseSpaceLateVisualGroundingCorrection>();
            if (correction == null)
            {
                correction = gameObject.AddComponent<PoseSpaceLateVisualGroundingCorrection>();
            }

            correction.Initialize(this);
        }

        public void ApplyLateVisualGroundingCorrection()
        {
            _grounding.ApplyLateVisualCorrection(this);
        }
    }

}
