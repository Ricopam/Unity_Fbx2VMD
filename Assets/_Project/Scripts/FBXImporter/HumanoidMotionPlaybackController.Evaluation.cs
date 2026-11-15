using System;
using Fbx2Vmd.Profiling;
using UnityEngine;


namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class HumanoidMotionPlaybackController
    {
        private bool EvaluateCurrentPoseWithCorrection(bool applyFootStabilization = true)
        {
            // 원본 자세와 상체 보정 뒤에 발 접촉을 마지막으로 고정함.
            _supportCurvePose?.Restore();
            _footGrounding?.RestoreAppliedPose();
            RestoreFootRotationBindings();
            Vector3 leftBendNormal, rightBendNormal;
            using (PerfScope.Measure("PlaybackController.PoseSample"))
            {
                _player.EvaluateAt(CurrentTimeSeconds);
                if (!TryCaptureFootBendNormals(
                        out leftBendNormal,
                        out rightBendNormal))
                {
                    return false;
                }
            }

            if (!TryApplyArmDirectionCorrection())
            {
                return false;
            }

            int frameIndex = CurrentFrameIndex;
            ApplyFootRotationReference(CurrentTimeSeconds);
            bool isApplied = ApplyPoseFrameEdit(frameIndex);

            if (!isApplied || !applyFootStabilization)
            {
                return isApplied;
            }

            using (PerfScope.Measure("PlaybackController.FootStabilization"))
            {
                return TryApplyFootContactStabilization(
                    leftBendNormal,
                    rightBendNormal);
            }
        }

        private bool ApplyPoseFrameEdit(int frameIndex)
        {
            bool isApplied;
            using (PerfScope.Measure("PlaybackController.PoseFrameEdit"))
            {
                isApplied = _poseCorrectionDocument == null ||
                    !_poseCorrectionDocument.HasFrameCorrection(frameIndex) ||
                    _poseFrameEditor.TryApply(
                        _poseCorrectionDocument,
                        frameIndex);
                _armSupportPoseApplier.Apply();
            }
            return isApplied;
        }

        private bool TryApplyArmDirectionCorrection()
        {
            if (!_poseReferencePlayer.IsInitialized)
            {
                return true;
            }

            if (!_poseReferencePlayer.TryEvaluateArmDirectionsAt(
                    CurrentTimeSeconds,
                    out HumanoidArmDirectionReference reference))
            {
                return false;
            }

            return _poseFrameEditor.TryApplyArmDirectionReference(
                reference,
                out _);
        }

        private bool TryCaptureFootBendNormals(
            out Vector3 leftBendNormal,
            out Vector3 rightBendNormal)
        {
            return _footContactStabilizer.TryCaptureBendNormals(
                out leftBendNormal,
                out rightBendNormal);
        }

        private bool TryApplyFootContactStabilization(
            Vector3 leftBendNormal,
            Vector3 rightBendNormal)
        {
            bool isApplied = TryApplyFootContactStabilizationCore(
                leftBendNormal, rightBendNormal);
            // 발 계층 최종 출력의 단발 순간이동을 확산 — 모든 경로 반환부를 감쌈.
            if (_useFootStepDiffusion)
                _footStepDiffuser.TryApply(CurrentTimeSeconds, ClipFrameRate,
                    _isGroundResponseEnabled ? _groundResponse : null, _footGrounding);
            if (isApplied && CanUseSupportCurves)
                ApplyPreparedSupportCurve(CurrentTimeSeconds * ClipFrameRate);
            return isApplied;
        }

        private bool TryApplyFootContactStabilizationCore(
            Vector3 leftBendNormal,
            Vector3 rightBendNormal)
        {
            ResetGroundingGateStatus();
            bool goalDriverActive = IsIkGoalFootDriveActive;
            if (goalDriverActive) _footGoalDriver.CaptureValidatedSupport(false, false);
            // 주장 여부는 weight 갱신 전 상태로 판정해 gate가 stabilizer 이전 자세를
            // 보는 alpha=0 순서를 유지함.
            bool driverClaims = goalDriverActive &&
                _footGoalDriver.WouldClaim(CurrentTimeSeconds);
            if (goalDriverActive)
                LastGroundingGate.evaluation_stage = driverClaims
                    ? "ik_goal_drive" : "ik_goal_abstain";
            // gate는 준비된 매 프레임에서 실행 — weight 감쇠·기권 프레임의 접지 보정
            // 공백을 막고, 주장 프레임에는 드라이버가 gate 결과 위에 오버레이함.
            bool gateApplied = ApplyGroundingGate(
                goalDriverActive, driverClaims, leftBendNormal, rightBendNormal,
                out bool earlyResolved);
            if (earlyResolved) return true;
            // gate 적용 프레임은 alpha=0과 같이 stabilizer를 건너뜀.
            bool isApplied = !gateApplied && _footContactStabilizer.TryApply(
                CurrentTimeSeconds,
                leftBendNormal,
                rightBendNormal);
            ApplyGoalDriverOverlay(
                goalDriverActive, driverClaims, gateApplied, isApplied,
                leftBendNormal, rightBendNormal);
            return isApplied || gateApplied;
        }




        private void ResetGroundingGateStatus()
        {
            LastGroundingStatus = _isGroundResponseEnabled
                ? HumanoidFootGroundingStatus.Unavailable : HumanoidFootGroundingStatus.Disabled;
            LastGroundingGate.measured = false;
            LastGroundingGate.evaluation_stage = "unavailable";
            LastGroundingGate.raw_pass = LastGroundingGate.held_pass = false;
            LastGroundingGate.used_physical_reach = false;
            LastGroundingGate.pelvis_offset_m = 0f;
            LastGroundingGate.pelvis_maximum_offset_m = 0f;
            LastGroundingGate.left = LastGroundingGate.right = null;
        }

        private void InitializeFootRotationBindings(Animator animator)
        {
            if (animator == null || !animator.isHuman)
                return;
            _sourceToTargetRotation = animator.transform.rotation;
            HumanoidFootRotationBinding.TryCreate(animator.GetBoneTransform(HumanBodyBones.LeftFoot),
                animator.GetBoneTransform(HumanBodyBones.LeftToes), animator.transform.up, out _leftFootRotation);
            HumanoidFootRotationBinding.TryCreate(animator.GetBoneTransform(HumanBodyBones.RightFoot),
                animator.GetBoneTransform(HumanBodyBones.RightToes), animator.transform.up, out _rightFootRotation);
        }

        private void RestoreFootRotationBindings()
        {
            _leftFootRotation?.RestoreAppliedRotation();
            _rightFootRotation?.RestoreAppliedRotation();
        }

        private void ApplyFootRotationReference(float timeSeconds)
        {
            if (!HasFootRotationReference)
                return;
            if (_poseReferencePlayer.TryEvaluateFootFramesAt(timeSeconds, out var left, out var right) &&
                _leftFootRotation.TryApplyWorldFrame(_sourceToTargetRotation * left) &&
                _rightFootRotation.TryApplyWorldFrame(_sourceToTargetRotation * right))
                return;
            RestoreFootRotationBindings();
            throw new InvalidOperationException("준비된 발 회전 기준을 적용하지 못했습니다.");
        }

        private void InitializeEditorReferenceCorrections(
            Animator targetAnimator,
            AnimationClip clip,
            GameObject sourceModelAsset)
        {
            _poseReferencePlayer.InitializeFromSourceModel(sourceModelAsset, clip);
            _sourceToTargetRotation *= Quaternion.Inverse(_poseReferencePlayer.InitialRootRotation);
            _footContactStabilizer.Initialize(
                targetAnimator,
                clip,
                _player.EvaluateAt,
                _poseReferencePlayer,
                sourceModelAsset?.name);
            // 초기화는 항상 수행해 세터 시점과 무관하게 둠 — 적용만 플래그로 게이트함.
            _footGoalDriver.TryInitialize(
                targetAnimator, _footContactStabilizer, clip.frameRate);
            if (_footGrounding != null)
            {
                PrepareFootGrounding(clip);
            }
        }

        // 착지 후보 평가가 Goal에 복제한 기존 밑창 기준을 바꾸지 않도록 뒤에서 준비함.
        private void PrepareFootGrounding(AnimationClip clip)
        {
            bool groundingPrepared = _footGrounding.TryPrepare(_footContactStabilizer.SourceSamples,
                _footContactStabilizer.SourceHumanScale, clip, EvaluateEditorContactReference,
                _footContactStabilizer.IntentEstimate);
            if (groundingPrepared)
                _footGrounding.PrepareReleaseReferences(clip, EvaluateEditorContactReference, _groundResponse);
            // goal 드라이버에 밑창 정점을 공유 — 발끝 본 스냅만으로는 회전된 발의
            // 밑창 정점이 지면 아래로 파고드는 침투를 막지 못함. 프로브가 없으면
            // 기존 발끝 스냅 폴백이 그대로 적용됨.
            if (_footGoalDriver.IsInitialized)
            {
                if (_footGrounding.TryGetSoleProbeLocalOffsets(true, out Vector3[] leftSole))
                    _footGoalDriver.SetSoleProbes(true, leftSole);
                if (_footGrounding.TryGetSoleProbeLocalOffsets(false, out Vector3[] rightSole))
                    _footGoalDriver.SetSoleProbes(false, rightSole);
            }
            if (groundingPrepared)
            {
                _footGrounding.PrepareTouchdownReferences(clip, EvaluateEditorContactReference, _groundResponse);
                _footGrounding.PrepareReleaseSmoothing(clip, EvaluateEditorContactReference, _groundResponse);
            }
        }

        private void EvaluateEditorContactReference(float timeSeconds)
        {
            _footGrounding?.RestoreAppliedPose();
            RestoreFootRotationBindings();
            _player.EvaluateAt(timeSeconds);
            if (!_poseReferencePlayer.TryEvaluateArmDirectionsAt(timeSeconds, out var reference) ||
                !_poseFrameEditor.TryApplyArmDirectionReference(reference, out _))
                throw new InvalidOperationException("접지 시작 자세의 상체 기준을 평가하지 못했습니다.");
            ApplyFootRotationReference(timeSeconds);
            _armSupportPoseApplier.Apply();
        }

        private static void ValidateTime(float timeSeconds, string parameterName)
        {
            if (float.IsNaN(timeSeconds) ||
                float.IsInfinity(timeSeconds) ||
                timeSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }
}
