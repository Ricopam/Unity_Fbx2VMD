using UnityEngine;
using Fbx2Vmd.Retargeting;

namespace Fbx2Vmd.FBXImporter
{
    // 프레임 리타겟 파이프라인 스테이지(LateUpdate 분해)를 담당하는 partial 분할.
    public partial class PoseSpaceRetargeter
    {
        void LateUpdate()
        {
            if (!_isInitialized || ghostAnimator == null || targetAnimator == null || _ghostHandler == null || _targetHandler == null) return;

            TickLegacyDriverAndUpdateScale();
            if (!ReadAndPrepareGhostPose())
            {
                RestoreTargetLocalScales();
                return;
            }

            Quaternion poseRootRotation = CapturePoseRootRotation();
            if (!TryResolveBodyPosition(out Vector3 bodyRootDelta))
            {
                RestoreTargetLocalScales();
                return;
            }

            SolveAndApplyTargetPose(bodyRootDelta);
            ApplyRootRotationSync(poseRootRotation);
            Vector3 targetDelta = CalculateRootDelta(bodyRootDelta);
            ApplyRootMotionAndGrounding(targetDelta);
        }

        private void TickLegacyDriverAndUpdateScale()
        {
            if (_legacyAnimationDriver.Tick(
                Time.deltaTime,
                Application.isPlaying,
                clampLegacyAnimationVisualStep,
                legacyAnimationVisualFrameRate))
            {
                _visualPoseSmoother.ResetVisualPoseHistory();
            }

            // 스케일 비율 계산 (매 프레임 체크하여 안정성 확보)
            // Container가 작동 중이라면 ghostHip.position.y는 ~0.8m 수준이어야 함.
            Transform ghostHip = ghostAnimator.GetBoneTransform(HumanBodyBones.Hips);
            Transform targetHip = targetAnimator.GetBoneTransform(HumanBodyBones.Hips);

            _scaleRatio = CalculateSafeScaleRatio(ghostHip, targetHip);
        }

        private bool ReadAndPrepareGhostPose()
        {
            // 포즈(근육) 동기화
            ResetRetargetPoseStageDiagnostics();
            _ghostHandler.GetHumanPose(ref _humanPose);
#if UNITY_EDITOR
            _endpointStageDiagnostics.GhostPositions = RetargetingEndpointStageDiagnostics.CapturePositions(ghostAnimator);
#endif
            if (!IsFinite(_humanPose))
            {
                LogPoseWarning("Ghost HumanPose contains non-finite values. Skipping this retarget frame.");
                return false;
            }

            RetargetingPoseInputTransformer.TransformInPlace(_humanPose.muscles);
#if UNITY_EDITOR
            if (_useEditorHumanoidMuscleReference &&
                _humanPose.muscles != null &&
                _editorHumanoidMuscleCurves.Count > 0)
            {
                RetargetingPoseInputTransformer.AlignWithReferenceCurvesInPlace(
                    _humanPose.muscles,
                    _editorHumanoidMuscleCurves,
                    _legacyAnimationDriver.CurrentTime);
            }
#endif
            ApplyMuscleReferenceAndGuards();
            return true;
        }

        private void ApplyMuscleReferenceAndGuards()
        {
            _stageMuscleDiagnostics.PoseInput = _muscleIndexMap.ReadStageSnapshot(_humanPose);
            ApplyEditorHumanoidMuscleReference(ref _humanPose);
            _stageMuscleDiagnostics.AfterEditorMuscleReference = _muscleIndexMap.ReadStageSnapshot(_humanPose);
            ApplyEditorHumanoidFingerPoseReference(ref _humanPose);
            ApplyEditorHumanoidBodyRotationReference(ref _humanPose);
            if (ShouldApplyRetargetSafetyGuards())
            {
                ThumbMuscleGuardPolicy.ApplyAnatomicalGuard(this, ref _humanPose, ThumbMuscleGuardPolicy.ShouldApplyStretchOffset(this));
            }
            ClampPoseMuscles(ref _humanPose);
            _stageMuscleDiagnostics.AfterClampPoseMuscles = _muscleIndexMap.ReadStageSnapshot(_humanPose);
            ApplyAnatomicalArmGuard(ref _humanPose);
            _stageMuscleDiagnostics.AfterAnatomicalArmGuard = _muscleIndexMap.ReadStageSnapshot(_humanPose);
            _visualPoseSmoother.SmoothPoseOnVisualSpike(this, ref _humanPose);
            _stageMuscleDiagnostics.AfterVisualSpikeSmoothing = _muscleIndexMap.ReadStageSnapshot(_humanPose);
        }

        private Quaternion CapturePoseRootRotation()
        {
            Quaternion poseRootRotation = _humanPose.bodyRotation;
            if (ShouldPreserveFbxRootRotation && !_hasPoseRootRotationCorrection && IsFinite(poseRootRotation) && _legacyAnimationDriver.IsPlaying)
            {
                _poseRootRotationCorrection = Quaternion.Inverse(poseRootRotation);
                _hasPoseRootRotationCorrection = true;
            }

            return poseRootRotation;
        }

        private bool TryResolveBodyPosition(out Vector3 bodyRootDelta)
        {
            // Y축은 target 기준으로 안정화하고, X/Z 체중 이동은 FBX 값을 유지한다.
            Vector3 bodyPos = _humanPose.bodyPosition;
            bodyPos.x *= _scaleRatio;
            bodyPos.z *= _scaleRatio;
            Vector3 bodyRootMotionSource = bodyPos;
#if UNITY_EDITOR
            bodyRootMotionSource = RootMotionGuard.SelectBodyPositionRootMotionSource(
                bodyPos,
                _editorReferenceBodyPosition,
                _hasEditorReferenceBodyPosition,
                ShouldUseManualAnimatorBodyRotationReference);
#endif
            bodyRootDelta = ExtractBodyPositionXZRootDelta(bodyRootMotionSource);
            ResolveBodyPositionHeight(ref bodyPos);
#if UNITY_EDITOR
            ApplyManualBodyPositionXzReference(ref bodyPos);
#endif
            if (!IsFinite(bodyPos))
            {
                LogPoseWarning("Retarget body position became non-finite. Skipping this retarget frame.");
                return false;
            }
            _humanPose.bodyPosition = bodyPos;
            return true;
        }

        private void ResolveBodyPositionHeight(ref Vector3 bodyPos)
        {
            if (preserveTargetBodyPosition && _hasTargetReferenceBodyPosition)
            {
                bodyPos = _targetReferenceBodyPosition;
                // 수동 기준 Animator의 bodyPos.y로 Y를 대체: ghost Legacy bodyPos 스파이크 없이 애니메이션 높이를 따른다.
                if (ShouldUseManualAnimatorBodyPositionYReference && _hasEditorReferenceBodyPosition)
                {
                    bodyPos.y = _editorReferenceBodyPosition.y;
                }
            }
            else
            {
                bodyPos.y *= _scaleRatio;
            }
        }

#if UNITY_EDITOR
        private void ApplyManualBodyPositionXzReference(ref Vector3 bodyPos)
        {
            float manualBodyPositionXzFrameGateWeight =
                ResolveManualAnimatorBodyPositionXzFrameGateWeight();
            if (ShouldUseManualAnimatorBodyPositionXzReference &&
                manualBodyPositionXzFrameGateWeight > 0f &&
                _hasEditorReferenceBodyPosition &&
                ManualPoseReferenceApplier.TryCalculateBodyPositionXzReference(
                    bodyPos,
                    _editorReferenceBodyPosition,
                    manualAnimatorBodyPositionXzReferenceWeight * manualBodyPositionXzFrameGateWeight,
                    manualAnimatorBodyPositionXzReferenceMaxOffset,
                    manualAnimatorBodyPositionXzReferenceAxisXScale,
                    manualAnimatorBodyPositionXzReferenceAxisZScale,
                    out Vector3 manualBodyPositionXz))
            {
                bodyPos = manualBodyPositionXz;
                if (!_editorBodyPositionXzReferenceLogged)
                {
                    Debug.Log(
                        $"[PoseSpaceRetargeter] Manual Animator bodyPosition X/Z reference applied. " +
                        $"weight={manualAnimatorBodyPositionXzReferenceWeight:F2}, " +
                        $"maxOffset={manualAnimatorBodyPositionXzReferenceMaxOffset:F3}m, " +
                        $"frameGate={manualAnimatorBodyPositionXzReferenceFrameGateStart:F0}-{manualAnimatorBodyPositionXzReferenceFrameGateEnd:F0}, " +
                        $"blendFrames={manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames:F0}, " +
                        $"axisScale={manualAnimatorBodyPositionXzReferenceAxisXScale:F2}/{manualAnimatorBodyPositionXzReferenceAxisZScale:F2}");
                    _editorBodyPositionXzReferenceLogged = true;
                }
            }
        }
#endif

        private void SolveAndApplyTargetPose(Vector3 bodyRootDelta)
        {
            Vector3 rootMotionCarrierPositionBeforePose = targetAnimator.transform.position;
            Vector3 poseSolveRootPosition = RootMotionGuard.SelectPoseSolveRootPosition(
                rootMotionCarrierPositionBeforePose,
                _hasTargetRootPoseGuardAnchorPosition ? _targetRootPoseGuardAnchorPosition : rootMotionCarrierPositionBeforePose,
                useBodyPositionXZRootMotion);
            if (IsFinite(poseSolveRootPosition))
            {
                targetAnimator.transform.position = poseSolveRootPosition;
            }

            Vector3 targetPositionBeforePose = targetAnimator.transform.position;
            ApplyPreSolveReferences();
            SolveTargetPoseAndCapture();
            ClampAppliedTargetPose();
#if UNITY_EDITOR
            ApplyEditorHumanoidBoneLocalRotationReference();
#endif
            RestoreTargetHumanoidLocalPositions();
            ApplyPostSolveReferencesAndGuards(targetPositionBeforePose, bodyRootDelta, rootMotionCarrierPositionBeforePose);
        }

        private void ApplyPreSolveReferences()
        {
#if UNITY_EDITOR
            _setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions = RetargetingEndpointStageDiagnostics.CapturePositions(targetAnimator);
            RetargetingEditorReferenceHooks.FootReference?.ApplyPreSetSignCorrectedRowLocalBodyPositionReference(this, ref _humanPose);
            if (!preSetHumanPoseEndpointPositionUseGhostCurrentBasis)
            {
                RetargetingEditorReferenceHooks.FootReference?.ApplyPreSetEndpointPositionReference(this);
            }
#endif
        }

        private void SolveTargetPoseAndCapture()
        {
            _setHumanPoseDiagnostics.CaptureInput(ReadSetHumanPoseInputSnapshot(_humanPose));
            CaptureSetHumanPosePreSolveBasisDiagnostics(_humanPose);
            _targetHandler.SetHumanPose(ref _humanPose);
            ApplySetHumanPoseRightLegTwistOutputReference(_humanPose);
            _setHumanPoseDiagnostics.CaptureOutput(ReadSetHumanPoseOutputSnapshot());
#if UNITY_EDITOR
            _endpointStageDiagnostics.AfterSetHumanPosePositions = RetargetingEndpointStageDiagnostics.CapturePositions(targetAnimator);
#endif
        }

        private void ApplyPostSolveReferencesAndGuards(
            Vector3 targetPositionBeforePose,
            Vector3 bodyRootDelta,
            Vector3 rootMotionCarrierPositionBeforePose)
        {
#if UNITY_EDITOR
            RetargetingEditorReferenceHooks.FootReference?.ApplyHipsLocalPositionReference(this);
            RetargetingEditorReferenceHooks.FootReference?.ApplyFootLocalRotationReference(this);
            RetargetingEditorReferenceHooks.LowerBodyDirection?.Apply(this);
            RetargetingEditorReferenceHooks.FootReference?.ApplyFootHipsAlignedResidualYawReference(this);
            RetargetingEditorReferenceHooks.FootReference?.ApplyPostSetEndpointPositionReference(this);
            RetargetingEditorReferenceHooks.ThumbReference?.ApplyThumbBasePositionReference(this);
#endif
#if UNITY_EDITOR
            _endpointStageDiagnostics.AfterManualReferencesPositions = RetargetingEndpointStageDiagnostics.CapturePositions(targetAnimator);
#endif
            _spikeClamp.ClampHipsLocalPositionSpike(this);
            ThumbGuardPolicy.ClampTargetLocalRotations(this);
            ApplyPostSolveThumbHandReferences();
            ApplyImplicitRootGuardAndRestore(targetPositionBeforePose, bodyRootDelta, rootMotionCarrierPositionBeforePose);
        }

        private void ApplyPostSolveThumbHandReferences()
        {
#if UNITY_EDITOR
            RetargetingEditorReferenceHooks.ThumbReference?.ApplyThumbLocalRotationReference(this);
            RetargetingEditorReferenceHooks.ThumbReference?.ApplyHandPalmFrameReference(this);
            RetargetingEditorReferenceHooks.FootReference?.ApplyHandLocalRotationReference(this);
            RetargetingEditorReferenceHooks.ThumbReference?.ApplyThumbSegmentDirectionReference(this);
            RetargetingEditorReferenceHooks.ThumbReference?.ApplyThumbHandDirectionReference(this);
            ApplyRightSleeveSilhouetteLocalOffsetReference();
#endif
        }

        private void ApplyImplicitRootGuardAndRestore(
            Vector3 targetPositionBeforePose,
            Vector3 bodyRootDelta,
            Vector3 rootMotionCarrierPositionBeforePose)
        {
            _spikeClamp.ClampRootPositionSpike(this, targetPositionBeforePose, "SetHumanPose");
            Vector3 implicitRootGuardReference = RootMotionGuard.SelectImplicitRootGuardReference(
                _hasTargetRootPoseGuardAnchorPosition ? _targetRootPoseGuardAnchorPosition : targetPositionBeforePose,
                targetPositionBeforePose,
                _movementScaleMultiplier);
            targetAnimator.transform.position = RootMotionGuard.ApplyImplicitBodyPositionRootGuard(
                implicitRootGuardReference,
                targetAnimator.transform.position,
                useBodyPositionXZRootMotion,
                bodyRootDelta);
            targetAnimator.transform.position = RootMotionGuard.RestoreRootMotionCarrierPositionAfterPose(
                rootMotionCarrierPositionBeforePose,
                targetAnimator.transform.position,
                useBodyPositionXZRootMotion);
#if UNITY_EDITOR
            _endpointStageDiagnostics.AfterRootRestorePositions = RetargetingEndpointStageDiagnostics.CapturePositions(targetAnimator);
#endif
        }

        private void ApplyRootRotationSync(Quaternion poseRootRotation)
        {
            // 월드 회전 동기화 (180도 문제 해결)
            if (ShouldPreserveFbxRootRotation && _hasPoseRootRotationCorrection && IsFinite(poseRootRotation))
            {
                Quaternion correctedRootRotation = _poseRootRotationCorrection * poseRootRotation;
                if (IsFinite(correctedRootRotation))
                {
                    targetAnimator.transform.rotation = correctedRootRotation;
                }
            }
            else if (!ShouldPreserveFbxRootRotation && fixReverseRotation)
            {
                // Ghost 회전 * 180도 보정
                targetAnimator.transform.rotation = ghostAnimator.transform.rotation * _facingCorrection;
            }
            else if (!ShouldPreserveFbxRootRotation)
            {
                targetAnimator.transform.rotation = ghostAnimator.transform.rotation;
            }
        }

        private Vector3 CalculateRootDelta(Vector3 bodyRootDelta)
        {
            // 루트 모션 동기화 (호 그리기 방지)
            // Ghost 이동량 계산
            Vector3 ghostDelta = ghostAnimator.transform.position - _prevGhostPos;
            Vector3 editorRootTranslationDelta = ExtractEditorRootTranslationDelta(ghostDelta);

            // 내 캐릭터 크기에 맞춰 이동량 스케일링
            Vector3 targetDelta = RootMotionGuard.CalculateRetargetRootDelta(
                ghostDelta,
                _scaleRatio,
                editorRootTranslationDelta,
                bodyRootDelta,
                _movementScaleMultiplier,
                useBodyPositionXZRootMotion,
                clampRootDeltaSpikes,
                maxRootDeltaPerFrame,
                out float targetDeltaMagnitude,
                out bool skippedByNonFinite,
                out bool limitedBySpike);
            RecordRootDeltaOutcome(
                targetDeltaMagnitude,
                skippedByNonFinite,
                limitedBySpike,
                ghostDelta,
                editorRootTranslationDelta);
            return targetDelta;
        }

        private void RecordRootDeltaOutcome(
            float targetDeltaMagnitude,
            bool skippedByNonFinite,
            bool limitedBySpike,
            Vector3 ghostDelta,
            Vector3 editorRootTranslationDelta)
        {
            if (skippedByNonFinite)
            {
                LogPoseWarning("Retarget root delta became non-finite. Skipping root motion for this frame.");
                _lastRootDeltaMagnitude = float.NaN;
                _rootDeltaSpikeSkippedCount++;
                return;
            }

            _lastRootDeltaMagnitude = targetDeltaMagnitude;
            _maxRootDeltaMagnitude = Mathf.Max(_maxRootDeltaMagnitude, _lastRootDeltaMagnitude);

            if (limitedBySpike)
            {
                _rootDeltaSpikeSkippedCount++;
                if (logRootDeltaSpikes && !_rootDeltaSpikeWarningLogged)
                {
                    Debug.LogWarning($"[PoseSpaceRetargeter] Root delta spike {_lastRootDeltaMagnitude:F3}m limited. ghostDelta={ghostDelta.magnitude:F3}m, editorRootDelta={editorRootTranslationDelta.magnitude:F3}m, limit={maxRootDeltaPerFrame:F3}m");
                    _rootDeltaSpikeWarningLogged = true;
                }
            }
        }

        private void ApplyRootMotionAndGrounding(Vector3 targetDelta)
        {
            // 이동 적용
            targetAnimator.transform.position += targetDelta;
#if UNITY_EDITOR
            _endpointStageDiagnostics.AfterRootDeltaPositions = RetargetingEndpointStageDiagnostics.CapturePositions(targetAnimator);
#endif

            // 위치 갱신
            _prevGhostPos = ghostAnimator.transform.position;

            // 스마트 접지 (Raycast Grounding) - 공중 부양 해결
            if (useSmartGrounding)
            {
                _grounding.ApplyRaycastGrounding(this);
            }
#if UNITY_EDITOR
            _endpointStageDiagnostics.AfterGroundingPositions = RetargetingEndpointStageDiagnostics.CapturePositions(targetAnimator);
#endif

            RestoreTargetLocalScales();
#if UNITY_EDITOR
            RetargetingEditorReferenceHooks.FootReference?.ApplyBipedIkFootPositionReference(this);
            _endpointStageDiagnostics.AfterBipedIKPositions = RetargetingEndpointStageDiagnostics.CapturePositions(targetAnimator);
            _endpointStageDiagnostics.CaptureAttribution();
#endif
        }
    }
}
