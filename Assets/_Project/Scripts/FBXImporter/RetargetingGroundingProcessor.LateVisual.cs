using UnityEngine;
using Fbx2Vmd.Retargeting;

namespace Fbx2Vmd.FBXImporter
{
    // 레이트 비주얼 그라운딩 보정 체인을 담당하는 partial 분할.
    internal sealed partial class RetargetingGroundingProcessor
    {
        internal void ApplyLateVisualCorrection(PoseSpaceRetargeter retargeter)
        {
            try
            {
                ApplyLateVisualCorrectionCore(retargeter);
            }
            finally
            {
                if (retargeter.targetAnimator != null)
                {
#if UNITY_EDITOR
                    retargeter._endpointStageDiagnostics.AfterLateVisualGroundingPositions = RetargetingEndpointStageDiagnostics.CapturePositions(retargeter.targetAnimator);
                    retargeter._endpointStageDiagnostics.CaptureAttribution();
#endif
                }
            }
        }

        private void ApplyLateVisualCorrectionCore(PoseSpaceRetargeter retargeter)
        {
            if (!retargeter._isInitialized || !retargeter.useSmartGrounding || !retargeter.enableLateVisualGroundingCorrection || retargeter.targetAnimator == null)
            {
                return;
            }

            if (TryApplyFrozenGroundingRootY(retargeter))
            {
                return;
            }

            if (!TryResolveLateVisualResidual(retargeter, out float residual))
            {
                return;
            }

            if (!TryResolveLateVisualEffectiveResidual(retargeter, residual, out float effectiveResidual))
            {
                return;
            }

            ApplyLateVisualResidualStep(retargeter, effectiveResidual);
        }

        private bool TryApplyFrozenGroundingRootY(PoseSpaceRetargeter retargeter)
        {
            if (!retargeter.freezeRootYAfterInitialGrounding || !_groundingInitialized || !_hasFrozenGroundingRootY)
            {
                return false;
            }

            Vector3 frozenPos = retargeter.targetAnimator.transform.position;
            frozenPos.y = _frozenGroundingRootY;
            if (PoseSpaceRetargeter.IsFinite(frozenPos))
            {
                retargeter.targetAnimator.transform.position = frozenPos;
            }

            _lateVisualGroundingInitialized = true;
            _lastGroundingVerticalStep = 0f;
            return true;
        }

        private bool TryResolveLateVisualResidual(PoseSpaceRetargeter retargeter, out float residual)
        {
            residual = 0f;
            if (!TryGetLowestFootBottomY(retargeter, out float lowestFootBottomY))
            {
                return false;
            }

            float rendererMinY = ResolveGroundingContactBottomY(retargeter, lowestFootBottomY);

            float targetGroundY = 0.0f;
            float targetHeight = ResolveEditorFootHeightGroundingReferenceTarget(retargeter, targetGroundY + retargeter.groundOffset);
            _lastGroundingTargetY = targetGroundY;
            _lastGroundingLowestFootBottomY = rendererMinY;

            if (!GroundingStabilizer.TryCalculateAdjustment(targetHeight, rendererMinY, out residual))
            {
                retargeter.LogPoseWarning("Late visual grounding residual became non-finite. Skipping final grounding for this frame.");
                return false;
            }

            return true;
        }

        private bool TryResolveLateVisualEffectiveResidual(PoseSpaceRetargeter retargeter, float residual, out float effectiveResidual)
        {
            if (GroundingStabilizer.ShouldSkipLateVisualGroundingForActiveVerticalStep(
                residual,
                retargeter.smoothLateVisualGroundingCorrection,
                _lastGroundingVerticalStep))
            {
                effectiveResidual = 0f;
                _lateVisualGroundingInitialized = true;
                return false;
            }

            if (!GroundingStabilizer.TryCalculateLateVisualGroundingEffectiveResidual(
                residual,
                retargeter.smoothLateVisualGroundingCorrection,
                retargeter.groundingDeadZone,
                retargeter.maxLateVisualGroundingCorrection,
                out effectiveResidual,
                out bool exceededMaxCorrection))
            {
                WarnLateVisualMaxCorrectionExceeded(retargeter, residual, exceededMaxCorrection);
                _lateVisualGroundingInitialized = true;
                return false;
            }

            return true;
        }

        private void WarnLateVisualMaxCorrectionExceeded(PoseSpaceRetargeter retargeter, float residual, bool exceededMaxCorrection)
        {
            if (!exceededMaxCorrection || _lateVisualGroundingWarningLogged)
            {
                return;
            }

            float maxCorrection = Mathf.Max(0.001f, retargeter.maxLateVisualGroundingCorrection);
            Debug.LogWarning($"[PoseSpaceRetargeter] Late visual grounding residual {residual:F3}m exceeded max {maxCorrection:F3}m. Skipping this frame to avoid collapsing a real jump.");
            _lateVisualGroundingWarningLogged = true;
        }

        private void ApplyLateVisualResidualStep(PoseSpaceRetargeter retargeter, float effectiveResidual)
        {
            Vector3 currentPos = retargeter.targetAnimator.transform.position;
            if (!PoseSpaceRetargeter.IsFinite(currentPos))
            {
                retargeter.LogPoseWarning("Target position became non-finite before late visual grounding. Skipping final grounding for this frame.");
                return;
            }

            float appliedResidual = GroundingStabilizer.CalculateLateVisualGroundingStep(
                effectiveResidual,
                retargeter.smoothLateVisualGroundingCorrection,
                _lateVisualGroundingInitialized,
                retargeter.lateVisualGroundingSnapThreshold,
                retargeter.lateVisualGroundingSmoothing,
                retargeter.maxLateVisualGroundingStepPerFrame);
            if (Mathf.Abs(appliedResidual) <= 0.000001f)
            {
                return;
            }

            ApplyLateVisualPosition(retargeter, currentPos, appliedResidual);
        }

        private void ApplyLateVisualPosition(PoseSpaceRetargeter retargeter, Vector3 currentPos, float appliedResidual)
        {
            if (!GroundingStabilizer.TryCalculateLateVisualGroundingAppliedPosition(
                currentPos,
                appliedResidual,
                out Vector3 appliedPosition))
            {
                retargeter.LogPoseWarning("Target position became non-finite after late visual grounding. Skipping final grounding for this frame.");
                return;
            }

            retargeter.targetAnimator.transform.position = appliedPosition;
            _lateVisualGroundingInitialized = true;
            RecordLateVisualAppliedMetrics(appliedResidual);
        }

        private void RecordLateVisualAppliedMetrics(float appliedResidual)
        {
            _lastGroundingAdjustment = appliedResidual;
            _maxGroundingAdjustment = Mathf.Max(_maxGroundingAdjustment, Mathf.Abs(appliedResidual));
            _lastGroundingVerticalStep = appliedResidual;
            _maxGroundingVerticalStep = Mathf.Max(_maxGroundingVerticalStep, Mathf.Abs(appliedResidual));
            if (_groundingInitialized)
            {
                _maxGroundingVerticalStepAfterInitial = Mathf.Max(_maxGroundingVerticalStepAfterInitial, Mathf.Abs(appliedResidual));
            }
            else
            {
                _groundingInitialized = true;
                _initialGroundingVerticalStep = appliedResidual;
            }
        }
    }
}
