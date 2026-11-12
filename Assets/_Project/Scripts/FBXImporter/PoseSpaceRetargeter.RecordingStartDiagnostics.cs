using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 녹화 시작 포즈·베이스라인 진단을 담당하는 partial 분할.
    public partial class PoseSpaceRetargeter
    {
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

            CaptureRecordingStartHipsReferenceBaseline();
        }

        private void CaptureRecordingStartHipsReferenceBaseline()
        {
            _recordingStartHipsReferenceBeforeLocalY = _footReferenceDiagnostics.HipsLocalReferenceBeforeLocalY;
            _recordingStartHipsReferenceAfterLocalY = _footReferenceDiagnostics.HipsLocalReferenceAfterLocalY;
            _recordingStartHipsReferenceDeltaY = _footReferenceDiagnostics.HipsLocalReferenceDeltaY;
            _recordingStartHipsReferenceFlipDetected = IsRecordingStartHipsBaselineFlip(
                _recordingStartHipsReferenceBeforeLocalY,
                _recordingStartHipsReferenceAfterLocalY,
                RecordingStartHipsBaselineFlipWarningThreshold);
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
    }
}
