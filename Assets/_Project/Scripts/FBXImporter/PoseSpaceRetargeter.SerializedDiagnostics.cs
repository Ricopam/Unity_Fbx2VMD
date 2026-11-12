using UnityEngine;
using UnityEngine.Serialization;

namespace Fbx2Vmd.FBXImporter
{
    // 스테이지·엔드포인트 진단 게터 표면을 담당하는 partial 분할.
    public partial class PoseSpaceRetargeter
    {
        public float LastRootDeltaMagnitude => _lastRootDeltaMagnitude;
        public float MaxRootDeltaMagnitude => _maxRootDeltaMagnitude;
        public int RootDeltaSpikeSkippedCount => _rootDeltaSpikeSkippedCount;
        public float LastRootPositionPoseDeltaMagnitude => _spikeClamp._lastRootPositionPoseDeltaMagnitude;
        public float MaxRootPositionPoseDeltaMagnitude => _spikeClamp._maxRootPositionPoseDeltaMagnitude;
        public int RootPositionSpikeClampedCount => _spikeClamp._rootPositionSpikeClampedCount;
        public float LastTargetHipsLocalPositionDelta => _spikeClamp._lastTargetHipsLocalPositionDelta;
        public float MaxTargetHipsLocalPositionDelta => _spikeClamp._maxTargetHipsLocalPositionDelta;
        public int TargetHipsLocalPositionSpikeClampedCount => _spikeClamp._targetHipsLocalPositionSpikeClampedCount;
        public float LastGroundingAdjustment => _grounding._lastGroundingAdjustment;
        public float MaxGroundingAdjustment => _grounding._maxGroundingAdjustment;
        public int GroundingStepClampedCount => _grounding._groundingStepClampedCount;
        public int GroundingSmoothedCount => _grounding._groundingSmoothedCount;
        public float LastGroundingVerticalStep => _grounding._lastGroundingVerticalStep;
        public float MaxGroundingVerticalStep => _grounding._maxGroundingVerticalStep;
        public float InitialGroundingVerticalStep => _grounding._initialGroundingVerticalStep;
        public float MaxGroundingVerticalStepAfterInitial => _grounding._maxGroundingVerticalStepAfterInitial;
        public float LastGroundingTargetY => _grounding._lastGroundingTargetY;
        public float LastGroundingLowestFootBottomY => _grounding._lastGroundingLowestFootBottomY;
        public float LastEditorFootHeightGroundingReferenceLift => _lastEditorFootHeightGroundingReferenceLift;
        public float RecordingStartRootY => _recordingStartRootY;
        public float RecordingStartBodyPositionY => _recordingStartBodyPositionY;
        public float RecordingStartHipsLocalY => _recordingStartHipsLocalY;
        public float RecordingStartHipsY => _recordingStartHipsY;
        public float RecordingStartHipsReferenceBeforeLocalY => _recordingStartHipsReferenceBeforeLocalY;
        public float RecordingStartHipsReferenceAfterLocalY => _recordingStartHipsReferenceAfterLocalY;
        public float RecordingStartHipsReferenceDeltaY => _recordingStartHipsReferenceDeltaY;
        public int RecordingStartHipsReferenceFlipDetected => _recordingStartHipsReferenceFlipDetected ? 1 : 0;
        public string RecordingStartHipsReferenceStage => _recordingStartHipsReferenceStage;
        public float LastLegacyAnimationStep => _legacyAnimationDriver.LastStep;
        public float MaxLegacyAnimationStep => _legacyAnimationDriver.MaxStep;
        public int LegacyAnimationStepSpikeCount => _legacyAnimationDriver.StepSpikeCount;
        public float CurrentLegacyAnimationTime => _legacyAnimationDriver.CurrentTime;
        public float CurrentLegacyAnimationClipLength => _legacyAnimationDriver.CurrentClipLength;
        public string CurrentLegacyAnimationClipName => _legacyAnimationDriver.CurrentClipName;
        public bool IsCompleteEditorHumanoidPoseReferenceActive
        {
            get
            {
                return _useCompleteEditorHumanoidMuscleReference &&
                    _hasEditorHumanoidPoseReferenceForFrame &&
                    _editorHumanoidPoseReferencePlayer != null &&
                    _editorHumanoidPoseReferencePlayer.IsInitialized;
            }
        }
        public int PoseVisualSmoothingCount => _visualPoseSmoother._poseVisualSmoothingCount;
        public int PoseVisualMuscleDeltaOnlySkippedCount => _visualPoseSmoother._poseVisualMuscleDeltaOnlySkippedCount;
        public float LastPoseVisualMaxMuscleDelta => _visualPoseSmoother._lastPoseVisualMaxMuscleDelta;
        public float MaxPoseVisualMaxMuscleDelta => _visualPoseSmoother._maxPoseVisualMaxMuscleDelta;
        public int LastLeftThumbLocalRotationGuardClampCount => _thumbGuardDiagnostics.LastLeftThumbLocalRotationGuardClampCount;
        public int LastRightThumbLocalRotationGuardClampCount => _thumbGuardDiagnostics.LastRightThumbLocalRotationGuardClampCount;
        public int LastLeftThumbLocalRotationGuardPreserveCount => _thumbGuardDiagnostics.LastLeftThumbLocalRotationGuardPreserveCount;
        public int LastRightThumbLocalRotationGuardPreserveCount => _thumbGuardDiagnostics.LastRightThumbLocalRotationGuardPreserveCount;
        public float LastLeftThumbLocalRotationGuardCurrentRisk => _thumbGuardDiagnostics.LastLeftThumbLocalRotationGuardCurrentRisk;
        public float LastRightThumbLocalRotationGuardCurrentRisk => _thumbGuardDiagnostics.LastRightThumbLocalRotationGuardCurrentRisk;
        public float LastLeftThumbLocalRotationGuardLimitedRisk => _thumbGuardDiagnostics.LastLeftThumbLocalRotationGuardLimitedRisk;
        public float LastRightThumbLocalRotationGuardLimitedRisk => _thumbGuardDiagnostics.LastRightThumbLocalRotationGuardLimitedRisk;
        public bool LastLeftThumbWorldRotationSuppressCompetingOverride => _thumbGuardDiagnostics.LastLeftThumbWorldRotationSuppressCompetingOverride;
        public bool LastRightThumbWorldRotationSuppressCompetingOverride => _thumbGuardDiagnostics.LastRightThumbWorldRotationSuppressCompetingOverride;
        public bool LastLeftThumbWorldRotationKeepDetachedHelperOverride => _thumbGuardDiagnostics.LastLeftThumbWorldRotationKeepDetachedHelperOverride;
        public bool LastRightThumbWorldRotationKeepDetachedHelperOverride => _thumbGuardDiagnostics.LastRightThumbWorldRotationKeepDetachedHelperOverride;
        public float LastLeftThumbWorldRotationCurrentReferenceFrameDeviation => _thumbGuardDiagnostics.LastLeftThumbWorldRotationCurrentReferenceFrameDeviation;
        public float LastRightThumbWorldRotationCurrentReferenceFrameDeviation => _thumbGuardDiagnostics.LastRightThumbWorldRotationCurrentReferenceFrameDeviation;
        public float LastLeftThumbWorldRotationCandidateReferenceFrameDeviation => _thumbGuardDiagnostics.LastLeftThumbWorldRotationCandidateReferenceFrameDeviation;
        public float LastRightThumbWorldRotationCandidateReferenceFrameDeviation => _thumbGuardDiagnostics.LastRightThumbWorldRotationCandidateReferenceFrameDeviation;
        public int LastLeftThumbProximalWorldRotationPreserveReason => (int)_thumbGuardDiagnostics.LastLeftThumbProximalWorldRotationPreserveReason;
        public int LastRightThumbProximalWorldRotationPreserveReason => (int)_thumbGuardDiagnostics.LastRightThumbProximalWorldRotationPreserveReason;
        public int LastLeftThumbIntermediateWorldRotationPreserveReason => (int)_thumbGuardDiagnostics.LastLeftThumbIntermediateWorldRotationPreserveReason;
        public int LastRightThumbIntermediateWorldRotationPreserveReason => (int)_thumbGuardDiagnostics.LastRightThumbIntermediateWorldRotationPreserveReason;
        public float LastLeftThumbProximalWorldRotationCurrentReferenceAngle => _thumbGuardDiagnostics.LastLeftThumbProximalWorldRotationCurrentReferenceAngle;
        public float LastRightThumbProximalWorldRotationCurrentReferenceAngle => _thumbGuardDiagnostics.LastRightThumbProximalWorldRotationCurrentReferenceAngle;
        public float LastLeftThumbIntermediateWorldRotationCurrentReferenceAngle => _thumbGuardDiagnostics.LastLeftThumbIntermediateWorldRotationCurrentReferenceAngle;
        public float LastRightThumbIntermediateWorldRotationCurrentReferenceAngle => _thumbGuardDiagnostics.LastRightThumbIntermediateWorldRotationCurrentReferenceAngle;
        public float LastLeftThumbProximalWorldRotationCandidateReferenceAngle => _thumbGuardDiagnostics.LastLeftThumbProximalWorldRotationCandidateReferenceAngle;
        public float LastRightThumbProximalWorldRotationCandidateReferenceAngle => _thumbGuardDiagnostics.LastRightThumbProximalWorldRotationCandidateReferenceAngle;
        public float LastLeftThumbIntermediateWorldRotationCandidateReferenceAngle => _thumbGuardDiagnostics.LastLeftThumbIntermediateWorldRotationCandidateReferenceAngle;
        public float LastRightThumbIntermediateWorldRotationCandidateReferenceAngle => _thumbGuardDiagnostics.LastRightThumbIntermediateWorldRotationCandidateReferenceAngle;
        public float LastLeftThumbProximalWorldRotationPreserveCurrentRisk => _thumbGuardDiagnostics.LastLeftThumbProximalWorldRotationPreserveCurrentRisk;
        public float LastRightThumbProximalWorldRotationPreserveCurrentRisk => _thumbGuardDiagnostics.LastRightThumbProximalWorldRotationPreserveCurrentRisk;
        public float LastLeftThumbIntermediateWorldRotationPreserveCurrentRisk => _thumbGuardDiagnostics.LastLeftThumbIntermediateWorldRotationPreserveCurrentRisk;
        public float LastRightThumbIntermediateWorldRotationPreserveCurrentRisk => _thumbGuardDiagnostics.LastRightThumbIntermediateWorldRotationPreserveCurrentRisk;
        public float LastLeftThumbProximalWorldRotationPreserveLimitedRisk => _thumbGuardDiagnostics.LastLeftThumbProximalWorldRotationPreserveLimitedRisk;
        public float LastRightThumbProximalWorldRotationPreserveLimitedRisk => _thumbGuardDiagnostics.LastRightThumbProximalWorldRotationPreserveLimitedRisk;
        public float LastLeftThumbIntermediateWorldRotationPreserveLimitedRisk => _thumbGuardDiagnostics.LastLeftThumbIntermediateWorldRotationPreserveLimitedRisk;
        public float LastRightThumbIntermediateWorldRotationPreserveLimitedRisk => _thumbGuardDiagnostics.LastRightThumbIntermediateWorldRotationPreserveLimitedRisk;
        public float LastPoseInputLeftShoulderFrontBackMuscle => _stageMuscleDiagnostics.PoseInput.LeftShoulderFrontBack;
        public float LastAfterEditorMuscleReferenceLeftShoulderFrontBackMuscle => _stageMuscleDiagnostics.AfterEditorMuscleReference.LeftShoulderFrontBack;
        public float LastAfterClampPoseMusclesLeftShoulderFrontBackMuscle => _stageMuscleDiagnostics.AfterClampPoseMuscles.LeftShoulderFrontBack;
        public float LastAfterAnatomicalArmGuardLeftShoulderFrontBackMuscle => _stageMuscleDiagnostics.AfterAnatomicalArmGuard.LeftShoulderFrontBack;
        public float LastAfterVisualSpikeSmoothingLeftShoulderFrontBackMuscle => _stageMuscleDiagnostics.AfterVisualSpikeSmoothing.LeftShoulderFrontBack;
        public float LastSetHumanPoseInputLeftShoulderFrontBackMuscle => _setHumanPoseDiagnostics.Input.LeftShoulderFrontBack;
        public float LastSetHumanPoseOutputLeftShoulderFrontBackMuscle => _setHumanPoseDiagnostics.Output.LeftShoulderFrontBack;
        public float LastSetHumanPoseLeftShoulderFrontBackDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.LeftShoulderFrontBack,
            _setHumanPoseDiagnostics.Output.LeftShoulderFrontBack);
        public float LastPoseInputLeftArmTwistMuscle => _stageMuscleDiagnostics.PoseInput.LeftArmTwist;
        public float LastAfterEditorMuscleReferenceLeftArmTwistMuscle => _stageMuscleDiagnostics.AfterEditorMuscleReference.LeftArmTwist;
        public float LastAfterClampPoseMusclesLeftArmTwistMuscle => _stageMuscleDiagnostics.AfterClampPoseMuscles.LeftArmTwist;
        public float LastAfterAnatomicalArmGuardLeftArmTwistMuscle => _stageMuscleDiagnostics.AfterAnatomicalArmGuard.LeftArmTwist;
        public float LastAfterVisualSpikeSmoothingLeftArmTwistMuscle => _stageMuscleDiagnostics.AfterVisualSpikeSmoothing.LeftArmTwist;
        public float LastSetHumanPoseInputLeftArmTwistMuscle => _setHumanPoseDiagnostics.Input.LeftArmTwist;
        public float LastSetHumanPoseOutputLeftArmTwistMuscle => _setHumanPoseDiagnostics.Output.LeftArmTwist;
        public float LastSetHumanPoseLeftArmTwistDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.LeftArmTwist,
            _setHumanPoseDiagnostics.Output.LeftArmTwist);
        public float LastPoseInputLeftForearmStretchMuscle => _stageMuscleDiagnostics.PoseInput.LeftForearmStretch;
        public float LastAfterEditorMuscleReferenceLeftForearmStretchMuscle => _stageMuscleDiagnostics.AfterEditorMuscleReference.LeftForearmStretch;
        public float LastAfterClampPoseMusclesLeftForearmStretchMuscle => _stageMuscleDiagnostics.AfterClampPoseMuscles.LeftForearmStretch;
        public float LastAfterAnatomicalArmGuardLeftForearmStretchMuscle => _stageMuscleDiagnostics.AfterAnatomicalArmGuard.LeftForearmStretch;
        public float LastAfterVisualSpikeSmoothingLeftForearmStretchMuscle => _stageMuscleDiagnostics.AfterVisualSpikeSmoothing.LeftForearmStretch;
        public float LastSetHumanPoseInputLeftForearmStretchMuscle => _setHumanPoseDiagnostics.Input.LeftForearmStretch;
        public float LastSetHumanPoseOutputLeftForearmStretchMuscle => _setHumanPoseDiagnostics.Output.LeftForearmStretch;
        public float LastSetHumanPoseLeftForearmStretchDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.LeftForearmStretch,
            _setHumanPoseDiagnostics.Output.LeftForearmStretch);
        public float LastPoseInputRightForearmStretchMuscle => _stageMuscleDiagnostics.PoseInput.RightForearmStretch;
        public float LastAfterEditorMuscleReferenceRightForearmStretchMuscle => _stageMuscleDiagnostics.AfterEditorMuscleReference.RightForearmStretch;
        public float LastAfterClampPoseMusclesRightForearmStretchMuscle => _stageMuscleDiagnostics.AfterClampPoseMuscles.RightForearmStretch;
        public float LastAfterAnatomicalArmGuardRightForearmStretchMuscle => _stageMuscleDiagnostics.AfterAnatomicalArmGuard.RightForearmStretch;
        public float LastAfterVisualSpikeSmoothingRightForearmStretchMuscle => _stageMuscleDiagnostics.AfterVisualSpikeSmoothing.RightForearmStretch;
        public float LastSetHumanPoseInputRightForearmStretchMuscle => _setHumanPoseDiagnostics.Input.RightForearmStretch;
        public float LastSetHumanPoseOutputRightForearmStretchMuscle => _setHumanPoseDiagnostics.Output.RightForearmStretch;
        public float LastSetHumanPoseRightForearmStretchDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.RightForearmStretch,
            _setHumanPoseDiagnostics.Output.RightForearmStretch);
        public float LastPoseInputRightArmTwistMuscle => _stageMuscleDiagnostics.PoseInput.RightArmTwist;
        public float LastAfterEditorMuscleReferenceRightArmTwistMuscle => _stageMuscleDiagnostics.AfterEditorMuscleReference.RightArmTwist;
        public float LastAfterClampPoseMusclesRightArmTwistMuscle => _stageMuscleDiagnostics.AfterClampPoseMuscles.RightArmTwist;
        public float LastAfterAnatomicalArmGuardRightArmTwistMuscle => _stageMuscleDiagnostics.AfterAnatomicalArmGuard.RightArmTwist;
        public float LastAfterVisualSpikeSmoothingRightArmTwistMuscle => _stageMuscleDiagnostics.AfterVisualSpikeSmoothing.RightArmTwist;
        public float LastSetHumanPoseInputRightArmTwistMuscle => _setHumanPoseDiagnostics.Input.RightArmTwist;
        public float LastSetHumanPoseOutputRightArmTwistMuscle => _setHumanPoseDiagnostics.Output.RightArmTwist;
        public float LastSetHumanPoseRightArmTwistDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.RightArmTwist,
            _setHumanPoseDiagnostics.Output.RightArmTwist);
        public float LastSetHumanPoseInputLeftUpperLegFrontBackMuscle => _setHumanPoseDiagnostics.Input.LeftUpperLegFrontBack;
        public float LastSetHumanPoseOutputLeftUpperLegFrontBackMuscle => _setHumanPoseDiagnostics.Output.LeftUpperLegFrontBack;
        public float LastSetHumanPoseLeftUpperLegFrontBackDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.LeftUpperLegFrontBack,
            _setHumanPoseDiagnostics.Output.LeftUpperLegFrontBack);
        public float LastSetHumanPoseInputRightUpperLegFrontBackMuscle => _setHumanPoseDiagnostics.Input.RightUpperLegFrontBack;
        public float LastSetHumanPoseOutputRightUpperLegFrontBackMuscle => _setHumanPoseDiagnostics.Output.RightUpperLegFrontBack;
        public float LastSetHumanPoseRightUpperLegFrontBackDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.RightUpperLegFrontBack,
            _setHumanPoseDiagnostics.Output.RightUpperLegFrontBack);
        public float LastSetHumanPoseInputLeftLowerLegStretchMuscle => _setHumanPoseDiagnostics.Input.LeftLowerLegStretch;
        public float LastSetHumanPoseOutputLeftLowerLegStretchMuscle => _setHumanPoseDiagnostics.Output.LeftLowerLegStretch;
        public float LastSetHumanPoseLeftLowerLegStretchDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.LeftLowerLegStretch,
            _setHumanPoseDiagnostics.Output.LeftLowerLegStretch);
        public float LastSetHumanPoseInputRightLowerLegStretchMuscle => _setHumanPoseDiagnostics.Input.RightLowerLegStretch;
        public float LastSetHumanPoseOutputRightLowerLegStretchMuscle => _setHumanPoseDiagnostics.Output.RightLowerLegStretch;
        public float LastSetHumanPoseRightLowerLegStretchDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.RightLowerLegStretch,
            _setHumanPoseDiagnostics.Output.RightLowerLegStretch);
        public float LastSetHumanPoseInputLeftFootUpDownMuscle => _setHumanPoseDiagnostics.Input.LeftFootUpDown;
        public float LastSetHumanPoseOutputLeftFootUpDownMuscle => _setHumanPoseDiagnostics.Output.LeftFootUpDown;
        public float LastSetHumanPoseLeftFootUpDownDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.LeftFootUpDown,
            _setHumanPoseDiagnostics.Output.LeftFootUpDown);
        public float LastSetHumanPoseInputRightFootUpDownMuscle => _setHumanPoseDiagnostics.Input.RightFootUpDown;
        public float LastSetHumanPoseOutputRightFootUpDownMuscle => _setHumanPoseDiagnostics.Output.RightFootUpDown;
        public float LastSetHumanPoseRightFootUpDownDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.RightFootUpDown,
            _setHumanPoseDiagnostics.Output.RightFootUpDown);
        public float LastSetHumanPoseInputBodyPositionX => _setHumanPoseDiagnostics.Input.BodyPosition.x;
        public float LastSetHumanPoseInputBodyPositionY => _setHumanPoseDiagnostics.Input.BodyPosition.y;
        public float LastSetHumanPoseInputBodyPositionZ => _setHumanPoseDiagnostics.Input.BodyPosition.z;
        public float LastSetHumanPoseOutputBodyPositionX => _setHumanPoseDiagnostics.Output.BodyPosition.x;
        public float LastSetHumanPoseOutputBodyPositionY => _setHumanPoseDiagnostics.Output.BodyPosition.y;
        public float LastSetHumanPoseOutputBodyPositionZ => _setHumanPoseDiagnostics.Output.BodyPosition.z;
        public float LastSetHumanPoseBodyPositionDeltaXZ => CalculateFiniteXzDelta(
            _setHumanPoseDiagnostics.Input.BodyPosition,
            _setHumanPoseDiagnostics.Output.BodyPosition);
        public float LastSetHumanPoseInputBodyRotationYaw => ReadBodyRotationYaw(_setHumanPoseDiagnostics.Input.BodyRotation);
        public float LastSetHumanPoseOutputBodyRotationYaw => ReadBodyRotationYaw(_setHumanPoseDiagnostics.Output.BodyRotation);
        public float LastSetHumanPoseBodyRotationDeltaAngle => CalculateFiniteAngleDelta(
            _setHumanPoseDiagnostics.Input.BodyRotation,
            _setHumanPoseDiagnostics.Output.BodyRotation);
        public float LastSetHumanPosePreSolveGhostRootWorldX => _setHumanPoseDiagnostics.PreSolveBasis.GhostRootWorldPosition.x;
        public float LastSetHumanPosePreSolveGhostRootWorldY => _setHumanPoseDiagnostics.PreSolveBasis.GhostRootWorldPosition.y;
        public float LastSetHumanPosePreSolveGhostRootWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.GhostRootWorldPosition.z;
        public float LastSetHumanPosePreSolveGhostRootYaw => ReadBodyRotationYaw(_setHumanPoseDiagnostics.PreSolveBasis.GhostRootWorldRotation);
        public float LastSetHumanPosePreSolveTargetRootWorldX => _setHumanPoseDiagnostics.PreSolveBasis.TargetRootWorldPosition.x;
        public float LastSetHumanPosePreSolveTargetRootWorldY => _setHumanPoseDiagnostics.PreSolveBasis.TargetRootWorldPosition.y;
        public float LastSetHumanPosePreSolveTargetRootWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.TargetRootWorldPosition.z;
        public float LastSetHumanPosePreSolveTargetRootYaw => ReadBodyRotationYaw(_setHumanPoseDiagnostics.PreSolveBasis.TargetRootWorldRotation);
        public float LastSetHumanPosePreSolveTargetHipsWorldX => _setHumanPoseDiagnostics.PreSolveBasis.TargetHipsWorldPosition.x;
        public float LastSetHumanPosePreSolveTargetHipsWorldY => _setHumanPoseDiagnostics.PreSolveBasis.TargetHipsWorldPosition.y;
        public float LastSetHumanPosePreSolveTargetHipsWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.TargetHipsWorldPosition.z;
        public float LastSetHumanPosePreSolveTargetHipsLocalX => _setHumanPoseDiagnostics.PreSolveBasis.TargetHipsLocalPosition.x;
        public float LastSetHumanPosePreSolveTargetHipsLocalY => _setHumanPoseDiagnostics.PreSolveBasis.TargetHipsLocalPosition.y;
        public float LastSetHumanPosePreSolveTargetHipsLocalZ => _setHumanPoseDiagnostics.PreSolveBasis.TargetHipsLocalPosition.z;
        public float LastSetHumanPosePreSolveBodyPositionX => _setHumanPoseDiagnostics.PreSolveBasis.BodyPosition.x;
        public float LastSetHumanPosePreSolveBodyPositionY => _setHumanPoseDiagnostics.PreSolveBasis.BodyPosition.y;
        public float LastSetHumanPosePreSolveBodyPositionZ => _setHumanPoseDiagnostics.PreSolveBasis.BodyPosition.z;
        public float LastSetHumanPosePreSolveBodyRotationYaw => ReadBodyRotationYaw(_setHumanPoseDiagnostics.PreSolveBasis.BodyRotation);
        public float LastPreSetHumanPoseEndpointBodyPositionBeforeX => _footReferenceDiagnostics.PreSetEndpointBodyPositionBefore.x;
        public float LastPreSetHumanPoseEndpointBodyPositionBeforeZ => _footReferenceDiagnostics.PreSetEndpointBodyPositionBefore.z;
        public float LastPreSetHumanPoseEndpointBodyPositionAfterX => _footReferenceDiagnostics.PreSetEndpointBodyPositionAfter.x;
        public float LastPreSetHumanPoseEndpointBodyPositionAfterZ => _footReferenceDiagnostics.PreSetEndpointBodyPositionAfter.z;
        public float LastPreSetHumanPoseEndpointBodyPositionDeltaX => _footReferenceDiagnostics.PreSetEndpointBodyPositionDelta.x;
        public float LastPreSetHumanPoseEndpointBodyPositionDeltaZ => _footReferenceDiagnostics.PreSetEndpointBodyPositionDelta.z;
        public float LastPreSetHumanPoseEndpointBodyPositionDeltaMagnitudeXZ => CalculateFiniteXzDelta(
            Vector3.zero,
            _footReferenceDiagnostics.PreSetEndpointBodyPositionDelta);
        public float LastSetHumanPosePreSolveGhostLeftFootWorldX => _setHumanPoseDiagnostics.PreSolveBasis.GhostEndpointPositions.LeftFoot.x;
        public float LastSetHumanPosePreSolveGhostLeftFootWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.GhostEndpointPositions.LeftFoot.z;
        public float LastSetHumanPosePreSolveGhostLeftToesWorldX => _setHumanPoseDiagnostics.PreSolveBasis.GhostEndpointPositions.LeftToes.x;
        public float LastSetHumanPosePreSolveGhostLeftToesWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.GhostEndpointPositions.LeftToes.z;
        public float LastSetHumanPosePreSolveCurrentLeftFootWorldX => _setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions.LeftFoot.x;
        public float LastSetHumanPosePreSolveCurrentLeftFootWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions.LeftFoot.z;
        public float LastSetHumanPosePreSolveCurrentLeftToesWorldX => _setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions.LeftToes.x;
        public float LastSetHumanPosePreSolveCurrentLeftToesWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions.LeftToes.z;
        public float LastSetHumanPosePreSolveTargetLeftFootWorldX => _setHumanPoseDiagnostics.PreSolveBasis.TargetEndpointPositions.LeftFoot.x;
        public float LastSetHumanPosePreSolveTargetLeftFootWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.TargetEndpointPositions.LeftFoot.z;
        public float LastSetHumanPosePreSolveTargetLeftToesWorldX => _setHumanPoseDiagnostics.PreSolveBasis.TargetEndpointPositions.LeftToes.x;
        public float LastSetHumanPosePreSolveTargetLeftToesWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.TargetEndpointPositions.LeftToes.z;
        public float LastSetHumanPosePreSolveGhostRightFootWorldX => _setHumanPoseDiagnostics.PreSolveBasis.GhostEndpointPositions.RightFoot.x;
        public float LastSetHumanPosePreSolveGhostRightFootWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.GhostEndpointPositions.RightFoot.z;
        public float LastSetHumanPosePreSolveGhostRightToesWorldX => _setHumanPoseDiagnostics.PreSolveBasis.GhostEndpointPositions.RightToes.x;
        public float LastSetHumanPosePreSolveGhostRightToesWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.GhostEndpointPositions.RightToes.z;
        public float LastSetHumanPosePreSolveCurrentRightFootWorldX => _setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions.RightFoot.x;
        public float LastSetHumanPosePreSolveCurrentRightFootWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions.RightFoot.z;
        public float LastSetHumanPosePreSolveCurrentRightToesWorldX => _setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions.RightToes.x;
        public float LastSetHumanPosePreSolveCurrentRightToesWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions.RightToes.z;
        public float LastSetHumanPosePreSolveTargetRightFootWorldX => _setHumanPoseDiagnostics.PreSolveBasis.TargetEndpointPositions.RightFoot.x;
        public float LastSetHumanPosePreSolveTargetRightFootWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.TargetEndpointPositions.RightFoot.z;
        public float LastSetHumanPosePreSolveTargetRightToesWorldX => _setHumanPoseDiagnostics.PreSolveBasis.TargetEndpointPositions.RightToes.x;
        public float LastSetHumanPosePreSolveTargetRightToesWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.TargetEndpointPositions.RightToes.z;
        public float LastSetHumanPoseInputSpineFrontBackMuscle => _setHumanPoseDiagnostics.Input.SpineFrontBack;
        public float LastSetHumanPoseInputSpineLeftRightMuscle => _setHumanPoseDiagnostics.Input.SpineLeftRight;
        public float LastSetHumanPoseInputSpineTwistLeftRightMuscle => _setHumanPoseDiagnostics.Input.SpineTwistLeftRight;
        public float LastSetHumanPoseInputChestFrontBackMuscle => _setHumanPoseDiagnostics.Input.ChestFrontBack;
        public float LastSetHumanPoseInputChestLeftRightMuscle => _setHumanPoseDiagnostics.Input.ChestLeftRight;
        public float LastSetHumanPoseInputChestTwistLeftRightMuscle => _setHumanPoseDiagnostics.Input.ChestTwistLeftRight;
        public float LastSetHumanPoseInputUpperChestFrontBackMuscle => _setHumanPoseDiagnostics.Input.UpperChestFrontBack;
        public float LastSetHumanPoseInputUpperChestLeftRightMuscle => _setHumanPoseDiagnostics.Input.UpperChestLeftRight;
        public float LastSetHumanPoseInputUpperChestTwistLeftRightMuscle => _setHumanPoseDiagnostics.Input.UpperChestTwistLeftRight;
        public float LastSetHumanPoseInputLeftUpperLegInOutMuscle => _setHumanPoseDiagnostics.Input.LeftUpperLegInOut;
        public float LastSetHumanPoseInputRightUpperLegInOutMuscle => _setHumanPoseDiagnostics.Input.RightUpperLegInOut;
        public float LastSetHumanPoseInputLeftUpperLegTwistInOutMuscle => _setHumanPoseDiagnostics.Input.LeftUpperLegTwistInOut;
        public float LastSetHumanPoseInputRightUpperLegTwistInOutMuscle => _setHumanPoseDiagnostics.Input.RightUpperLegTwistInOut;
        public float LastSetHumanPoseInputLeftLowerLegTwistInOutMuscle => _setHumanPoseDiagnostics.Input.LeftLowerLegTwistInOut;
        public float LastSetHumanPoseInputRightLowerLegTwistInOutMuscle => _setHumanPoseDiagnostics.Input.RightLowerLegTwistInOut;
        public float LastSetHumanPoseInputLeftFootTwistInOutMuscle => _setHumanPoseDiagnostics.Input.LeftFootTwistInOut;
        public float LastSetHumanPoseInputRightFootTwistInOutMuscle => _setHumanPoseDiagnostics.Input.RightFootTwistInOut;
        public float LastSetHumanPoseInputLeftToesUpDownMuscle => _setHumanPoseDiagnostics.Input.LeftToesUpDown;
        public float LastSetHumanPoseInputRightToesUpDownMuscle => _setHumanPoseDiagnostics.Input.RightToesUpDown;
        public float LastSetHumanPoseOutputRightUpperLegInOutMuscle => _setHumanPoseDiagnostics.Output.RightUpperLegInOut;
        public float LastSetHumanPoseRightUpperLegInOutDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.RightUpperLegInOut,
            _setHumanPoseDiagnostics.Output.RightUpperLegInOut);
        public float LastSetHumanPoseOutputRightUpperLegTwistInOutMuscle => _setHumanPoseDiagnostics.Output.RightUpperLegTwistInOut;
        public float LastSetHumanPoseRightUpperLegTwistInOutDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.RightUpperLegTwistInOut,
            _setHumanPoseDiagnostics.Output.RightUpperLegTwistInOut);
        public float LastSetHumanPoseOutputRightLowerLegTwistInOutMuscle => _setHumanPoseDiagnostics.Output.RightLowerLegTwistInOut;
        public float LastSetHumanPoseRightLowerLegTwistInOutDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.RightLowerLegTwistInOut,
            _setHumanPoseDiagnostics.Output.RightLowerLegTwistInOut);
        public float LastSetHumanPoseOutputRightFootTwistInOutMuscle => _setHumanPoseDiagnostics.Output.RightFootTwistInOut;
        public float LastSetHumanPoseRightFootTwistInOutDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.RightFootTwistInOut,
            _setHumanPoseDiagnostics.Output.RightFootTwistInOut);
        public float LastSetHumanPoseOutputRightToesUpDownMuscle => _setHumanPoseDiagnostics.Output.RightToesUpDown;
        public float LastSetHumanPoseRightToesUpDownDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.RightToesUpDown,
            _setHumanPoseDiagnostics.Output.RightToesUpDown);
        public float LastRetargetStageGhostLeftFootWorldX => _endpointStageDiagnostics.GhostPositions.LeftFoot.x;
        public float LastRetargetStageGhostLeftFootWorldZ => _endpointStageDiagnostics.GhostPositions.LeftFoot.z;
        public float LastRetargetStageGhostLeftToesWorldX => _endpointStageDiagnostics.GhostPositions.LeftToes.x;
        public float LastRetargetStageGhostLeftToesWorldZ => _endpointStageDiagnostics.GhostPositions.LeftToes.z;
        public float LastRetargetStageGhostRightFootWorldX => _endpointStageDiagnostics.GhostPositions.RightFoot.x;
        public float LastRetargetStageGhostRightFootWorldZ => _endpointStageDiagnostics.GhostPositions.RightFoot.z;
        public float LastRetargetStageGhostRightToesWorldX => _endpointStageDiagnostics.GhostPositions.RightToes.x;
        public float LastRetargetStageGhostRightToesWorldZ => _endpointStageDiagnostics.GhostPositions.RightToes.z;
        public float LastRetargetStageAfterSetHumanPoseLeftFootWorldX => _endpointStageDiagnostics.AfterSetHumanPosePositions.LeftFoot.x;
        public float LastRetargetStageAfterSetHumanPoseLeftFootWorldZ => _endpointStageDiagnostics.AfterSetHumanPosePositions.LeftFoot.z;
        public float LastRetargetStageAfterSetHumanPoseLeftToesWorldX => _endpointStageDiagnostics.AfterSetHumanPosePositions.LeftToes.x;
        public float LastRetargetStageAfterSetHumanPoseLeftToesWorldZ => _endpointStageDiagnostics.AfterSetHumanPosePositions.LeftToes.z;
        public float LastRetargetStageAfterSetHumanPoseRightFootWorldX => _endpointStageDiagnostics.AfterSetHumanPosePositions.RightFoot.x;
        public float LastRetargetStageAfterSetHumanPoseRightFootWorldZ => _endpointStageDiagnostics.AfterSetHumanPosePositions.RightFoot.z;
        public float LastRetargetStageAfterSetHumanPoseRightToesWorldX => _endpointStageDiagnostics.AfterSetHumanPosePositions.RightToes.x;
        public float LastRetargetStageAfterSetHumanPoseRightToesWorldZ => _endpointStageDiagnostics.AfterSetHumanPosePositions.RightToes.z;
        public float LastRetargetStageAfterManualReferencesLeftFootWorldX => _endpointStageDiagnostics.AfterManualReferencesPositions.LeftFoot.x;
        public float LastRetargetStageAfterManualReferencesLeftFootWorldZ => _endpointStageDiagnostics.AfterManualReferencesPositions.LeftFoot.z;
        public float LastRetargetStageAfterManualReferencesLeftToesWorldX => _endpointStageDiagnostics.AfterManualReferencesPositions.LeftToes.x;
        public float LastRetargetStageAfterManualReferencesLeftToesWorldZ => _endpointStageDiagnostics.AfterManualReferencesPositions.LeftToes.z;
        public float LastRetargetStageAfterManualReferencesRightFootWorldX => _endpointStageDiagnostics.AfterManualReferencesPositions.RightFoot.x;
        public float LastRetargetStageAfterManualReferencesRightFootWorldZ => _endpointStageDiagnostics.AfterManualReferencesPositions.RightFoot.z;
        public float LastRetargetStageAfterManualReferencesRightToesWorldX => _endpointStageDiagnostics.AfterManualReferencesPositions.RightToes.x;
        public float LastRetargetStageAfterManualReferencesRightToesWorldZ => _endpointStageDiagnostics.AfterManualReferencesPositions.RightToes.z;
        public float LastRetargetStageAfterRootRestoreLeftFootWorldX => _endpointStageDiagnostics.AfterRootRestorePositions.LeftFoot.x;
        public float LastRetargetStageAfterRootRestoreLeftFootWorldZ => _endpointStageDiagnostics.AfterRootRestorePositions.LeftFoot.z;
        public float LastRetargetStageAfterRootRestoreLeftToesWorldX => _endpointStageDiagnostics.AfterRootRestorePositions.LeftToes.x;
        public float LastRetargetStageAfterRootRestoreLeftToesWorldZ => _endpointStageDiagnostics.AfterRootRestorePositions.LeftToes.z;
        public float LastRetargetStageAfterRootRestoreRightFootWorldX => _endpointStageDiagnostics.AfterRootRestorePositions.RightFoot.x;
        public float LastRetargetStageAfterRootRestoreRightFootWorldZ => _endpointStageDiagnostics.AfterRootRestorePositions.RightFoot.z;
        public float LastRetargetStageAfterRootRestoreRightToesWorldX => _endpointStageDiagnostics.AfterRootRestorePositions.RightToes.x;
        public float LastRetargetStageAfterRootRestoreRightToesWorldZ => _endpointStageDiagnostics.AfterRootRestorePositions.RightToes.z;
        public float LastRetargetStageAfterRootDeltaLeftFootWorldX => _endpointStageDiagnostics.AfterRootDeltaPositions.LeftFoot.x;
        public float LastRetargetStageAfterRootDeltaLeftFootWorldZ => _endpointStageDiagnostics.AfterRootDeltaPositions.LeftFoot.z;
        public float LastRetargetStageAfterRootDeltaLeftToesWorldX => _endpointStageDiagnostics.AfterRootDeltaPositions.LeftToes.x;
        public float LastRetargetStageAfterRootDeltaLeftToesWorldZ => _endpointStageDiagnostics.AfterRootDeltaPositions.LeftToes.z;
        public float LastRetargetStageAfterRootDeltaRightFootWorldX => _endpointStageDiagnostics.AfterRootDeltaPositions.RightFoot.x;
        public float LastRetargetStageAfterRootDeltaRightFootWorldZ => _endpointStageDiagnostics.AfterRootDeltaPositions.RightFoot.z;
        public float LastRetargetStageAfterRootDeltaRightToesWorldX => _endpointStageDiagnostics.AfterRootDeltaPositions.RightToes.x;
        public float LastRetargetStageAfterRootDeltaRightToesWorldZ => _endpointStageDiagnostics.AfterRootDeltaPositions.RightToes.z;
        public float LastRetargetStageAfterGroundingLeftFootWorldX => _endpointStageDiagnostics.AfterGroundingPositions.LeftFoot.x;
        public float LastRetargetStageAfterGroundingLeftFootWorldZ => _endpointStageDiagnostics.AfterGroundingPositions.LeftFoot.z;
        public float LastRetargetStageAfterGroundingLeftToesWorldX => _endpointStageDiagnostics.AfterGroundingPositions.LeftToes.x;
        public float LastRetargetStageAfterGroundingLeftToesWorldZ => _endpointStageDiagnostics.AfterGroundingPositions.LeftToes.z;
        public float LastRetargetStageAfterGroundingRightFootWorldX => _endpointStageDiagnostics.AfterGroundingPositions.RightFoot.x;
        public float LastRetargetStageAfterGroundingRightFootWorldZ => _endpointStageDiagnostics.AfterGroundingPositions.RightFoot.z;
        public float LastRetargetStageAfterGroundingRightToesWorldX => _endpointStageDiagnostics.AfterGroundingPositions.RightToes.x;
        public float LastRetargetStageAfterGroundingRightToesWorldZ => _endpointStageDiagnostics.AfterGroundingPositions.RightToes.z;
        public float LastRetargetStageAfterBipedIKLeftFootWorldX => _endpointStageDiagnostics.AfterBipedIKPositions.LeftFoot.x;
        public float LastRetargetStageAfterBipedIKLeftFootWorldZ => _endpointStageDiagnostics.AfterBipedIKPositions.LeftFoot.z;
        public float LastRetargetStageAfterBipedIKLeftToesWorldX => _endpointStageDiagnostics.AfterBipedIKPositions.LeftToes.x;
        public float LastRetargetStageAfterBipedIKLeftToesWorldZ => _endpointStageDiagnostics.AfterBipedIKPositions.LeftToes.z;
        public float LastRetargetStageAfterBipedIKRightFootWorldX => _endpointStageDiagnostics.AfterBipedIKPositions.RightFoot.x;
        public float LastRetargetStageAfterBipedIKRightFootWorldZ => _endpointStageDiagnostics.AfterBipedIKPositions.RightFoot.z;
        public float LastRetargetStageAfterBipedIKRightToesWorldX => _endpointStageDiagnostics.AfterBipedIKPositions.RightToes.x;
        public float LastRetargetStageAfterBipedIKRightToesWorldZ => _endpointStageDiagnostics.AfterBipedIKPositions.RightToes.z;
        public float LastRetargetStageAfterLateVisualGroundingLeftFootWorldX => _endpointStageDiagnostics.AfterLateVisualGroundingPositions.LeftFoot.x;
        public float LastRetargetStageAfterLateVisualGroundingLeftFootWorldZ => _endpointStageDiagnostics.AfterLateVisualGroundingPositions.LeftFoot.z;
        public float LastRetargetStageAfterLateVisualGroundingLeftToesWorldX => _endpointStageDiagnostics.AfterLateVisualGroundingPositions.LeftToes.x;
        public float LastRetargetStageAfterLateVisualGroundingLeftToesWorldZ => _endpointStageDiagnostics.AfterLateVisualGroundingPositions.LeftToes.z;
        public float LastRetargetStageAfterLateVisualGroundingRightFootWorldX => _endpointStageDiagnostics.AfterLateVisualGroundingPositions.RightFoot.x;
        public float LastRetargetStageAfterLateVisualGroundingRightFootWorldZ => _endpointStageDiagnostics.AfterLateVisualGroundingPositions.RightFoot.z;
        public float LastRetargetStageAfterLateVisualGroundingRightToesWorldX => _endpointStageDiagnostics.AfterLateVisualGroundingPositions.RightToes.x;
        public float LastRetargetStageAfterLateVisualGroundingRightToesWorldZ => _endpointStageDiagnostics.AfterLateVisualGroundingPositions.RightToes.z;
        public string LastRetargetEndpointFirstJumpStage => _endpointStageDiagnostics.FirstJumpStage;
        public string LastRetargetEndpointFirstJumpEndpoint => _endpointStageDiagnostics.FirstJumpEndpoint;
        public float LastRetargetEndpointFirstJumpMagnitude => _endpointStageDiagnostics.FirstJumpMagnitude;
        public float LastRetargetEndpointFirstJumpDeltaX => _endpointStageDiagnostics.FirstJumpDelta.x;
        public float LastRetargetEndpointFirstJumpDeltaY => _endpointStageDiagnostics.FirstJumpDelta.y;
        public float LastRetargetEndpointFirstJumpDeltaZ => _endpointStageDiagnostics.FirstJumpDelta.z;
        public float LastEditorFootLocalRotationLeftFootXzDelta => _footReferenceDiagnostics.FootLocalRotationLeftFootXzDelta;
        public float LastEditorFootLocalRotationRightFootXzDelta => _footReferenceDiagnostics.FootLocalRotationRightFootXzDelta;
        public float LastEditorLowerBodySegmentDirectionLeftFootXzDelta => _lowerBodySegmentDiagnostics.LeftFootXzDelta;
        public float LastEditorLowerBodySegmentDirectionRightFootXzDelta => _lowerBodySegmentDiagnostics.RightFootXzDelta;
        public string LastEditorLowerBodySegmentDirectionMaxCorrectionSegment => _lowerBodySegmentDiagnostics.MaxCorrectionSegment;
        public float LastEditorLowerBodySegmentDirectionMaxCorrectionAngle => _lowerBodySegmentDiagnostics.MaxCorrectionAngle;
        public float LastEditorLowerBodySegmentDirectionMaxPreAngle => _lowerBodySegmentDiagnostics.MaxPreAngle;
        public float LastEditorLowerBodySegmentDirectionMaxPostAngle => _lowerBodySegmentDiagnostics.MaxPostAngle;
        public float LastEditorLowerBodySegmentDirectionMaxCorrectionAxisX => _lowerBodySegmentDiagnostics.MaxCorrectionAxis.x;
        public float LastEditorLowerBodySegmentDirectionMaxCorrectionAxisY => _lowerBodySegmentDiagnostics.MaxCorrectionAxis.y;
        public float LastEditorLowerBodySegmentDirectionMaxCorrectionAxisZ => _lowerBodySegmentDiagnostics.MaxCorrectionAxis.z;
        public float LastEditorLowerBodySegmentDirectionMaxReferenceDirectionX => _lowerBodySegmentDiagnostics.MaxReferenceDirection.x;
        public float LastEditorLowerBodySegmentDirectionMaxReferenceDirectionY => _lowerBodySegmentDiagnostics.MaxReferenceDirection.y;
        public float LastEditorLowerBodySegmentDirectionMaxReferenceDirectionZ => _lowerBodySegmentDiagnostics.MaxReferenceDirection.z;
        public float LastEditorLowerBodySegmentDirectionMaxPreDirectionX => _lowerBodySegmentDiagnostics.MaxPreDirection.x;
        public float LastEditorLowerBodySegmentDirectionMaxPreDirectionY => _lowerBodySegmentDiagnostics.MaxPreDirection.y;
        public float LastEditorLowerBodySegmentDirectionMaxPreDirectionZ => _lowerBodySegmentDiagnostics.MaxPreDirection.z;
        public float LastEditorLowerBodySegmentDirectionMaxPostDirectionX => _lowerBodySegmentDiagnostics.MaxPostDirection.x;
        public float LastEditorLowerBodySegmentDirectionMaxPostDirectionY => _lowerBodySegmentDiagnostics.MaxPostDirection.y;
        public float LastEditorLowerBodySegmentDirectionMaxPostDirectionZ => _lowerBodySegmentDiagnostics.MaxPostDirection.z;
        public float LastEditorLowerBodySegmentDirectionLeftUpperLegLowerLegCorrectionAngle => _lowerBodySegmentDiagnostics.LeftUpperLegLowerLegCorrectionAngle;
        public float LastEditorLowerBodySegmentDirectionRightUpperLegLowerLegCorrectionAngle => _lowerBodySegmentDiagnostics.RightUpperLegLowerLegCorrectionAngle;
        public float LastEditorLowerBodySegmentDirectionLeftLowerLegFootCorrectionAngle => _lowerBodySegmentDiagnostics.LeftLowerLegFootCorrectionAngle;
        public float LastEditorLowerBodySegmentDirectionRightLowerLegFootCorrectionAngle => _lowerBodySegmentDiagnostics.RightLowerLegFootCorrectionAngle;
        public float LastEditorLowerBodySegmentDirectionLeftFootToesCorrectionAngle => _lowerBodySegmentDiagnostics.LeftFootToesCorrectionAngle;
        public float LastEditorLowerBodySegmentDirectionRightFootToesCorrectionAngle => _lowerBodySegmentDiagnostics.RightFootToesCorrectionAngle;
        public float LastEditorLowerBodySegmentDirectionLeftLowerLegToFootParentWorldRotationDeltaAngle => _lowerBodySegmentDiagnostics.LeftLowerLegToFootParentWorldRotationDeltaAngle;
        public float LastEditorLowerBodySegmentDirectionRightLowerLegToFootParentWorldRotationDeltaAngle => _lowerBodySegmentDiagnostics.RightLowerLegToFootParentWorldRotationDeltaAngle;
        public float LastEditorLowerBodySegmentDirectionLeftLowerLegToFootChildFootLocalRotationDeltaAngle => _lowerBodySegmentDiagnostics.LeftLowerLegToFootChildFootLocalRotationDeltaAngle;
        public float LastEditorLowerBodySegmentDirectionRightLowerLegToFootChildFootLocalRotationDeltaAngle => _lowerBodySegmentDiagnostics.RightLowerLegToFootChildFootLocalRotationDeltaAngle;
        public float LastEditorLowerBodySegmentDirectionLeftFootToToesReferenceDirectionX => _lowerBodySegmentDiagnostics.LeftFootToToesReferenceDirection.x;
        public float LastEditorLowerBodySegmentDirectionLeftFootToToesReferenceDirectionY => _lowerBodySegmentDiagnostics.LeftFootToToesReferenceDirection.y;
        public float LastEditorLowerBodySegmentDirectionLeftFootToToesReferenceDirectionZ => _lowerBodySegmentDiagnostics.LeftFootToToesReferenceDirection.z;
        public float LastEditorLowerBodySegmentDirectionLeftFootToToesPreDirectionX => _lowerBodySegmentDiagnostics.LeftFootToToesPreDirection.x;
        public float LastEditorLowerBodySegmentDirectionLeftFootToToesPreDirectionY => _lowerBodySegmentDiagnostics.LeftFootToToesPreDirection.y;
        public float LastEditorLowerBodySegmentDirectionLeftFootToToesPreDirectionZ => _lowerBodySegmentDiagnostics.LeftFootToToesPreDirection.z;
        public float LastEditorLowerBodySegmentDirectionLeftFootToToesPostDirectionX => _lowerBodySegmentDiagnostics.LeftFootToToesPostDirection.x;
        public float LastEditorLowerBodySegmentDirectionLeftFootToToesPostDirectionY => _lowerBodySegmentDiagnostics.LeftFootToToesPostDirection.y;
        public float LastEditorLowerBodySegmentDirectionLeftFootToToesPostDirectionZ => _lowerBodySegmentDiagnostics.LeftFootToToesPostDirection.z;
        public float LastEditorLowerBodySegmentDirectionRightFootToToesReferenceDirectionX => _lowerBodySegmentDiagnostics.RightFootToToesReferenceDirection.x;
        public float LastEditorLowerBodySegmentDirectionRightFootToToesReferenceDirectionY => _lowerBodySegmentDiagnostics.RightFootToToesReferenceDirection.y;
        public float LastEditorLowerBodySegmentDirectionRightFootToToesReferenceDirectionZ => _lowerBodySegmentDiagnostics.RightFootToToesReferenceDirection.z;
        public float LastEditorLowerBodySegmentDirectionRightFootToToesPreDirectionX => _lowerBodySegmentDiagnostics.RightFootToToesPreDirection.x;
        public float LastEditorLowerBodySegmentDirectionRightFootToToesPreDirectionY => _lowerBodySegmentDiagnostics.RightFootToToesPreDirection.y;
        public float LastEditorLowerBodySegmentDirectionRightFootToToesPreDirectionZ => _lowerBodySegmentDiagnostics.RightFootToToesPreDirection.z;
        public float LastEditorLowerBodySegmentDirectionRightFootToToesPostDirectionX => _lowerBodySegmentDiagnostics.RightFootToToesPostDirection.x;
        public float LastEditorLowerBodySegmentDirectionRightFootToToesPostDirectionY => _lowerBodySegmentDiagnostics.RightFootToToesPostDirection.y;
        public float LastEditorLowerBodySegmentDirectionRightFootToToesPostDirectionZ => _lowerBodySegmentDiagnostics.RightFootToToesPostDirection.z;
        public float LastEditorLowerBodySegmentDirectionLeftLowerLegWorldX => _lowerBodySegmentDiagnostics.LeftLowerLegWorldPosition.x;
        public float LastEditorLowerBodySegmentDirectionLeftLowerLegWorldY => _lowerBodySegmentDiagnostics.LeftLowerLegWorldPosition.y;
        public float LastEditorLowerBodySegmentDirectionLeftLowerLegWorldZ => _lowerBodySegmentDiagnostics.LeftLowerLegWorldPosition.z;
        public float LastEditorLowerBodySegmentDirectionLeftFootWorldX => _lowerBodySegmentDiagnostics.LeftFootWorldPosition.x;
        public float LastEditorLowerBodySegmentDirectionLeftFootWorldY => _lowerBodySegmentDiagnostics.LeftFootWorldPosition.y;
        public float LastEditorLowerBodySegmentDirectionLeftFootWorldZ => _lowerBodySegmentDiagnostics.LeftFootWorldPosition.z;
        public float LastEditorLowerBodySegmentDirectionLeftToesWorldX => _lowerBodySegmentDiagnostics.LeftToesWorldPosition.x;
        public float LastEditorLowerBodySegmentDirectionLeftToesWorldY => _lowerBodySegmentDiagnostics.LeftToesWorldPosition.y;
        public float LastEditorLowerBodySegmentDirectionLeftToesWorldZ => _lowerBodySegmentDiagnostics.LeftToesWorldPosition.z;
        public float LastEditorLowerBodySegmentDirectionRightLowerLegWorldX => _lowerBodySegmentDiagnostics.RightLowerLegWorldPosition.x;
        public float LastEditorLowerBodySegmentDirectionRightLowerLegWorldY => _lowerBodySegmentDiagnostics.RightLowerLegWorldPosition.y;
        public float LastEditorLowerBodySegmentDirectionRightLowerLegWorldZ => _lowerBodySegmentDiagnostics.RightLowerLegWorldPosition.z;
        public float LastEditorLowerBodySegmentDirectionRightFootWorldX => _lowerBodySegmentDiagnostics.RightFootWorldPosition.x;
        public float LastEditorLowerBodySegmentDirectionRightFootWorldY => _lowerBodySegmentDiagnostics.RightFootWorldPosition.y;
        public float LastEditorLowerBodySegmentDirectionRightFootWorldZ => _lowerBodySegmentDiagnostics.RightFootWorldPosition.z;
        public float LastEditorLowerBodySegmentDirectionRightToesWorldX => _lowerBodySegmentDiagnostics.RightToesWorldPosition.x;
        public float LastEditorLowerBodySegmentDirectionRightToesWorldY => _lowerBodySegmentDiagnostics.RightToesWorldPosition.y;
        public float LastEditorLowerBodySegmentDirectionRightToesWorldZ => _lowerBodySegmentDiagnostics.RightToesWorldPosition.z;
        public float LastEditorLowerBodySegmentDirectionLeftLowerLegToFootCorrectionAxisX => _lowerBodySegmentDiagnostics.LeftLowerLegToFootCorrectionAxis.x;
        public float LastEditorLowerBodySegmentDirectionLeftLowerLegToFootCorrectionAxisY => _lowerBodySegmentDiagnostics.LeftLowerLegToFootCorrectionAxis.y;
        public float LastEditorLowerBodySegmentDirectionLeftLowerLegToFootCorrectionAxisZ => _lowerBodySegmentDiagnostics.LeftLowerLegToFootCorrectionAxis.z;
        public float LastEditorLowerBodySegmentDirectionRightLowerLegToFootCorrectionAxisX => _lowerBodySegmentDiagnostics.RightLowerLegToFootCorrectionAxis.x;
        public float LastEditorLowerBodySegmentDirectionRightLowerLegToFootCorrectionAxisY => _lowerBodySegmentDiagnostics.RightLowerLegToFootCorrectionAxis.y;
        public float LastEditorLowerBodySegmentDirectionRightLowerLegToFootCorrectionAxisZ => _lowerBodySegmentDiagnostics.RightLowerLegToFootCorrectionAxis.z;
        public float LastEditorLowerBodySegmentDirectionLeftFootForwardX => _lowerBodySegmentDiagnostics.LeftFootForward.x;
        public float LastEditorLowerBodySegmentDirectionLeftFootForwardY => _lowerBodySegmentDiagnostics.LeftFootForward.y;
        public float LastEditorLowerBodySegmentDirectionLeftFootForwardZ => _lowerBodySegmentDiagnostics.LeftFootForward.z;
        public float LastEditorLowerBodySegmentDirectionLeftFootUpX => _lowerBodySegmentDiagnostics.LeftFootUp.x;
        public float LastEditorLowerBodySegmentDirectionLeftFootUpY => _lowerBodySegmentDiagnostics.LeftFootUp.y;
        public float LastEditorLowerBodySegmentDirectionLeftFootUpZ => _lowerBodySegmentDiagnostics.LeftFootUp.z;
        public float LastEditorLowerBodySegmentDirectionRightFootForwardX => _lowerBodySegmentDiagnostics.RightFootForward.x;
        public float LastEditorLowerBodySegmentDirectionRightFootForwardY => _lowerBodySegmentDiagnostics.RightFootForward.y;
        public float LastEditorLowerBodySegmentDirectionRightFootForwardZ => _lowerBodySegmentDiagnostics.RightFootForward.z;
        public float LastEditorLowerBodySegmentDirectionRightFootUpX => _lowerBodySegmentDiagnostics.RightFootUp.x;
        public float LastEditorLowerBodySegmentDirectionRightFootUpY => _lowerBodySegmentDiagnostics.RightFootUp.y;
        public float LastEditorLowerBodySegmentDirectionRightFootUpZ => _lowerBodySegmentDiagnostics.RightFootUp.z;
        public float LastEditorFootHipsAlignedResidualYawLeftFootXzDelta => _footReferenceDiagnostics.FootHipsAlignedResidualYawLeftFootXzDelta;
        public float LastEditorFootHipsAlignedResidualYawRightFootXzDelta => _footReferenceDiagnostics.FootHipsAlignedResidualYawRightFootXzDelta;
        public float LastPostSetHumanPoseRightEndpointDesiredFootWorldX => _postSetEndpointDiagnostics.DesiredFootWorldPosition.x;
        public float LastPostSetHumanPoseRightEndpointDesiredFootWorldZ => _postSetEndpointDiagnostics.DesiredFootWorldPosition.z;
        public float LastPostSetHumanPoseRightEndpointDesiredToesWorldX => _postSetEndpointDiagnostics.DesiredToesWorldPosition.x;
        public float LastPostSetHumanPoseRightEndpointDesiredToesWorldZ => _postSetEndpointDiagnostics.DesiredToesWorldPosition.z;
        public float LastPostSetHumanPoseRightEndpointCurrentFootWorldX => _postSetEndpointDiagnostics.CurrentFootWorldPosition.x;
        public float LastPostSetHumanPoseRightEndpointCurrentFootWorldZ => _postSetEndpointDiagnostics.CurrentFootWorldPosition.z;
        public float LastPostSetHumanPoseRightEndpointCurrentToesWorldX => _postSetEndpointDiagnostics.CurrentToesWorldPosition.x;
        public float LastPostSetHumanPoseRightEndpointCurrentToesWorldZ => _postSetEndpointDiagnostics.CurrentToesWorldPosition.z;
        public float LastPostSetHumanPoseRightEndpointDeltaBeforeClampX => _postSetEndpointDiagnostics.DeltaBeforeClamp.x;
        public float LastPostSetHumanPoseRightEndpointDeltaBeforeClampZ => _postSetEndpointDiagnostics.DeltaBeforeClamp.z;
        public float LastPostSetHumanPoseRightEndpointDeltaAfterClampX => _postSetEndpointDiagnostics.DeltaAfterClamp.x;
        public float LastPostSetHumanPoseRightEndpointDeltaAfterClampZ => _postSetEndpointDiagnostics.DeltaAfterClamp.z;
        public float LastPostSetHumanPoseRightEndpointDeltaAfterPositiveZScaleX => _postSetEndpointDiagnostics.DeltaAfterPositiveZScale.x;
        public float LastPostSetHumanPoseRightEndpointDeltaAfterPositiveZScaleZ => _postSetEndpointDiagnostics.DeltaAfterPositiveZScale.z;
        public float LastPostSetHumanPoseRightEndpointCorrectionX => _postSetEndpointDiagnostics.Correction.x;
        public float LastPostSetHumanPoseRightEndpointCorrectionZ => _postSetEndpointDiagnostics.Correction.z;
        public float LastPostSetHumanPoseRightEndpointNextFootWorldX => _postSetEndpointDiagnostics.NextFootWorldPosition.x;
        public float LastPostSetHumanPoseRightEndpointNextFootWorldZ => _postSetEndpointDiagnostics.NextFootWorldPosition.z;
        public float LastPostSetHumanPoseRightEndpointMaxYawAngle => _postSetEndpointDiagnostics.MaxYawAngle;
        public float LastPostSetHumanPoseRightEndpointYawCorrectionAngle => _postSetEndpointDiagnostics.YawCorrectionAngle;
        public float LastPostSetHumanPoseRightEndpointUpperLegRotationDeltaAngle => _postSetEndpointDiagnostics.UpperLegRotationDeltaAngle;
        public float LastPostSetHumanPoseRightEndpointApplied => _postSetEndpointDiagnostics.Applied;
        public float LastPostSetHumanPoseRightEndpointEvaluatorXzReferenceEnabled => _postSetEndpointDiagnostics.EvaluatorXzReferenceEnabled;
        public float LastPostSetHumanPoseRightEndpointEvaluatorXzFirstOffsetX => _postSetEndpointDiagnostics.EvaluatorXzFirstOffset.x;
        public float LastPostSetHumanPoseRightEndpointEvaluatorXzFirstOffsetZ => _postSetEndpointDiagnostics.EvaluatorXzFirstOffset.z;
        public float LastPostSetHumanPoseRightEndpointEvaluatorXzNormalizedDeltaX => _postSetEndpointDiagnostics.EvaluatorXzNormalizedDelta.x;
        public float LastPostSetHumanPoseRightEndpointEvaluatorXzNormalizedDeltaZ => _postSetEndpointDiagnostics.EvaluatorXzNormalizedDelta.z;
        public float LastPostSetHumanPoseRightEndpointEvaluatorXzNormalizedMagnitude => _postSetEndpointDiagnostics.EvaluatorXzNormalizedDelta.magnitude;
        public float LastPostSetHumanPoseRightEndpointEvaluatorXzDesiredNormalizedDeltaX => _postSetEndpointDiagnostics.EvaluatorXzDesiredNormalizedDelta.x;
        public float LastPostSetHumanPoseRightEndpointEvaluatorXzDesiredNormalizedDeltaZ => _postSetEndpointDiagnostics.EvaluatorXzDesiredNormalizedDelta.z;
        public float LastPostSetHumanPoseRightEndpointEvaluatorXzTargetMagnitude => _postSetEndpointDiagnostics.EvaluatorXzTargetMagnitude;
    }
}
