using UnityEngine;
using Fbx2Vmd.Retargeting;

namespace Fbx2Vmd.FBXImporter
{
    // 리타게터의 접지 보정 상태와 계산을 담당함. PoseSpaceRetargeter에서 분리됨.
    internal sealed class RetargetingGroundingProcessor
    {
        private const float DefaultFootRadius = 0.04f;
        private const float GroundingDirectionReversalStepScale = 0.4f;

        internal bool _groundingInitialized;
        internal float _lastGroundingAdjustment = float.NaN;
        internal float _maxGroundingAdjustment;
        internal int _groundingStepClampedCount;
        internal int _groundingSmoothedCount;
        internal float _lastGroundingVerticalStep = float.NaN;
        internal float _maxGroundingVerticalStep;
        internal float _initialGroundingVerticalStep = float.NaN;
        internal float _maxGroundingVerticalStepAfterInitial;
        internal float _lastGroundingTargetY = float.NaN;
        internal float _lastGroundingLowestFootBottomY = float.NaN;
        private bool _lateVisualGroundingWarningLogged;
        private bool _lateVisualGroundingInitialized;
        private bool _hasFrozenGroundingRootY;
        private float _frozenGroundingRootY;
        private bool _rendererGroundingOutlierWarningLogged;
        private bool _hasEstimatedFootRadius;
        private float _estimatedFootRadius = DefaultFootRadius;
        private Vector3 _leftFootLockPosition;
        private Vector3 _rightFootLockPosition;
        private bool _leftFootLocked;
        private bool _rightFootLocked;

        internal void ResetMetrics()
        {
            _maxGroundingAdjustment = 0f;
            _groundingStepClampedCount = 0;
            _groundingSmoothedCount = 0;
            _maxGroundingVerticalStep = 0f;
            _maxGroundingVerticalStepAfterInitial = 0f;
        }

        internal void ResetSession()
        {
            _leftFootLocked = false;
            _rightFootLocked = false;
            _groundingInitialized = false;
            _hasFrozenGroundingRootY = false;
            _frozenGroundingRootY = 0f;
            _lastGroundingAdjustment = float.NaN;
            _maxGroundingAdjustment = 0f;
            _groundingStepClampedCount = 0;
            _groundingSmoothedCount = 0;
            _lastGroundingVerticalStep = float.NaN;
            _maxGroundingVerticalStep = 0f;
            _initialGroundingVerticalStep = float.NaN;
            _maxGroundingVerticalStepAfterInitial = 0f;
            _lastGroundingTargetY = float.NaN;
            _lastGroundingLowestFootBottomY = float.NaN;
            _lateVisualGroundingWarningLogged = false;
            _rendererGroundingOutlierWarningLogged = false;
            _lateVisualGroundingInitialized = false;
        }

        internal void ApplyRaycastGrounding(PoseSpaceRetargeter retargeter)
        {
            // 양발 위치 확보 (발목)
            Transform lFoot = retargeter.targetAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rFoot = retargeter.targetAnimator.GetBoneTransform(HumanBodyBones.RightFoot);

            if (lFoot == null || rFoot == null) return;

            // 발바닥 위치 (발목 - 반지름)
            if (!_hasEstimatedFootRadius)
            {
                CalibrateTargetFootRadius(retargeter);
            }

            float footRadius = GetEstimatedFootRadius();
            if (!GroundingStabilizer.TryCalculateFootBottomY(lFoot.position.y, footRadius, out float lBottom) ||
                !GroundingStabilizer.TryCalculateFootBottomY(rFoot.position.y, footRadius, out float rBottom))
            {
                retargeter.LogPoseWarning("Foot position became non-finite. Skipping grounding for this frame.");
                return;
            }

            // 현재 가장 낮은 발바닥 높이. Renderer bounds가 발에서 너무 멀면
            // 옷/머리카락/소매 outlier로 보고 발 기준을 유지한다.
            float lowestFootCurrentY = Mathf.Min(lBottom, rBottom);
            float contactBottomY = ResolveGroundingContactBottomY(retargeter, lowestFootCurrentY);

            // 목표는 지면(0) + Offset
            // Raycast를 사용하여 실제 지면을 찾을 수도 있으나, 현재는 평면(Plane) 위라고 가정하고 0.0f 사용
            // 만약 계단이나 경사면이라면 Physics.Raycast로 hit.point.y를 구해야 함.
            float targetGroundY = 0.0f; // 평면 가정
            float targetHeight = ResolveEditorFootHeightGroundingReferenceTarget(retargeter, targetGroundY + retargeter.groundOffset);
            _lastGroundingTargetY = targetGroundY;
            _lastGroundingLowestFootBottomY = contactBottomY;

            // 보정값 계산 (목표 - 현재)
            // 양수면 들어 올리고, 음수면 내림 (양방향)
            if (!GroundingStabilizer.TryCalculateAdjustment(targetHeight, contactBottomY, out float adjustment))
            {
                retargeter.LogPoseWarning("Grounding adjustment became non-finite. Skipping grounding for this frame.");
                _lastGroundingAdjustment = float.NaN;
                return;
            }

            _lastGroundingAdjustment = adjustment;
            _maxGroundingAdjustment = Mathf.Max(_maxGroundingAdjustment, Mathf.Abs(adjustment));

            Vector3 currentPos = retargeter.targetAnimator.transform.position;
            if (!PoseSpaceRetargeter.IsFinite(currentPos))
            {
                retargeter.LogPoseWarning("Target position became non-finite before grounding. Resetting to origin.");
                currentPos = Vector3.zero;
            }

            if (retargeter.freezeRootYAfterInitialGrounding && _groundingInitialized && _hasFrozenGroundingRootY)
            {
                currentPos.y = _frozenGroundingRootY;
                if (PoseSpaceRetargeter.IsFinite(currentPos))
                {
                    retargeter.targetAnimator.transform.position = currentPos;
                    ApplyGroundedFootLockXZ(retargeter, lFoot, rFoot, targetHeight, footRadius);
                }

                _lastGroundingVerticalStep = 0f;
                return;
            }

            bool wasGroundingInitialized = _groundingInitialized;
            float appliedVerticalStep = GroundingStabilizer.CalculateVerticalStep(
                currentPos.y,
                adjustment,
                wasGroundingInitialized,
                retargeter.smoothGrounding,
                retargeter.groundingSmoothing,
                retargeter.maxGroundingVerticalStepPerFrame,
                retargeter.groundingDeadZone,
                _lastGroundingVerticalStep,
                GroundingDirectionReversalStepScale,
                out bool skippedByDeadZone,
                out bool smoothedGrounding,
                out bool clampedGroundingStep);
            if (skippedByDeadZone)
            {
                _lastGroundingVerticalStep = 0f;
                return;
            }

            if (!wasGroundingInitialized)
            {
                _groundingInitialized = true;
            }

            if (smoothedGrounding)
            {
                _groundingSmoothedCount++;
            }

            if (clampedGroundingStep)
            {
                _groundingStepClampedCount++;
            }

            float clampedNextY = currentPos.y + appliedVerticalStep;
            _lastGroundingVerticalStep = appliedVerticalStep;
            _maxGroundingVerticalStep = Mathf.Max(_maxGroundingVerticalStep, Mathf.Abs(appliedVerticalStep));
            if (wasGroundingInitialized)
            {
                _maxGroundingVerticalStepAfterInitial = Mathf.Max(_maxGroundingVerticalStepAfterInitial, Mathf.Abs(appliedVerticalStep));
            }
            else
            {
                _initialGroundingVerticalStep = appliedVerticalStep;
            }

            currentPos.y = clampedNextY;

            if (PoseSpaceRetargeter.IsFinite(currentPos))
            {
                retargeter.targetAnimator.transform.position = currentPos;
                if (retargeter.freezeRootYAfterInitialGrounding && !_hasFrozenGroundingRootY)
                {
                    _frozenGroundingRootY = currentPos.y;
                    _hasFrozenGroundingRootY = true;
                }

                ApplyGroundedFootLockXZ(retargeter, lFoot, rFoot, targetHeight, footRadius);
            }
        }

        private float ResolveEditorFootHeightGroundingReferenceTarget(
            PoseSpaceRetargeter retargeter, float baseTargetHeight)
        {
#if UNITY_EDITOR
            return RetargetingEditorReferenceHooks.FootHeightReference?.ResolveFootHeightGroundingReferenceTarget(retargeter, baseTargetHeight) ?? baseTargetHeight;
#else
            return baseTargetHeight;
#endif
        }

        internal void ApplyLateVisualCorrection(PoseSpaceRetargeter retargeter)
        {
            try
            {
                if (!retargeter._isInitialized || !retargeter.useSmartGrounding || !retargeter.enableLateVisualGroundingCorrection || retargeter.targetAnimator == null)
                {
                    return;
                }

                if (retargeter.freezeRootYAfterInitialGrounding && _groundingInitialized && _hasFrozenGroundingRootY)
                {
                    Vector3 frozenPos = retargeter.targetAnimator.transform.position;
                    frozenPos.y = _frozenGroundingRootY;
                    if (PoseSpaceRetargeter.IsFinite(frozenPos))
                    {
                        retargeter.targetAnimator.transform.position = frozenPos;
                    }

                    _lateVisualGroundingInitialized = true;
                    _lastGroundingVerticalStep = 0f;
                    return;
                }

                if (!TryGetLowestFootBottomY(retargeter, out float lowestFootBottomY))
                {
                    return;
                }

                float rendererMinY = ResolveGroundingContactBottomY(retargeter, lowestFootBottomY);

                float targetGroundY = 0.0f;
                float targetHeight = ResolveEditorFootHeightGroundingReferenceTarget(retargeter, targetGroundY + retargeter.groundOffset);
                _lastGroundingTargetY = targetGroundY;
                _lastGroundingLowestFootBottomY = rendererMinY;

                if (!GroundingStabilizer.TryCalculateAdjustment(targetHeight, rendererMinY, out float residual))
                {
                    retargeter.LogPoseWarning("Late visual grounding residual became non-finite. Skipping final grounding for this frame.");
                    return;
                }

                if (GroundingStabilizer.ShouldSkipLateVisualGroundingForActiveVerticalStep(
                    residual,
                    retargeter.smoothLateVisualGroundingCorrection,
                    _lastGroundingVerticalStep))
                {
                    _lateVisualGroundingInitialized = true;
                    return;
                }

                if (!GroundingStabilizer.TryCalculateLateVisualGroundingEffectiveResidual(
                    residual,
                    retargeter.smoothLateVisualGroundingCorrection,
                    retargeter.groundingDeadZone,
                    retargeter.maxLateVisualGroundingCorrection,
                    out float effectiveResidual,
                    out bool exceededMaxCorrection))
                {
                    if (exceededMaxCorrection && !_lateVisualGroundingWarningLogged)
                    {
                        float maxCorrection = Mathf.Max(0.001f, retargeter.maxLateVisualGroundingCorrection);
                        Debug.LogWarning($"[PoseSpaceRetargeter] Late visual grounding residual {residual:F3}m exceeded max {maxCorrection:F3}m. Skipping this frame to avoid collapsing a real jump.");
                        _lateVisualGroundingWarningLogged = true;
                    }

                    _lateVisualGroundingInitialized = true;
                    return;
                }

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

        private bool TryGetLowestFootBottomY(PoseSpaceRetargeter retargeter, out float lowestFootBottomY)
        {
            lowestFootBottomY = 0f;
            if (retargeter.targetAnimator == null)
            {
                return false;
            }

            Transform leftFoot = retargeter.targetAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightFoot = retargeter.targetAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (leftFoot == null || rightFoot == null)
            {
                return false;
            }

            float footRadius = GetEstimatedFootRadius();
            return GroundingStabilizer.TryCalculateLowestFootBottomY(
                leftFoot.position.y,
                rightFoot.position.y,
                footRadius,
                out lowestFootBottomY);
        }

        private float GetEstimatedFootRadius()
        {
            return _hasEstimatedFootRadius ? _estimatedFootRadius : DefaultFootRadius;
        }

        internal void CalibrateTargetFootRadius(PoseSpaceRetargeter retargeter)
        {
            _hasEstimatedFootRadius = false;
            _estimatedFootRadius = DefaultFootRadius;
            if (retargeter.targetAnimator == null || !TryGetRendererBoundsMinY(retargeter, out float rendererMinY))
            {
                return;
            }

            Transform leftFoot = retargeter.targetAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            Transform rightFoot = retargeter.targetAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (leftFoot == null || rightFoot == null)
            {
                return;
            }

            if (!GroundingStabilizer.TryCalculateEstimatedFootRadius(
                leftFoot.position.y,
                rightFoot.position.y,
                rendererMinY,
                out float estimatedRadius))
            {
                return;
            }

            _estimatedFootRadius = estimatedRadius;
            _hasEstimatedFootRadius = true;
        }

        private float ResolveGroundingContactBottomY(PoseSpaceRetargeter retargeter, float lowestFootBottomY)
        {
            bool hasRendererBounds = TryGetRendererBoundsMinY(retargeter, out float rendererMinY);
            float contactBottomY = GroundingStabilizer.ResolveGroundingContactBottomY(
                lowestFootBottomY,
                hasRendererBounds,
                rendererMinY,
                retargeter.rejectRendererGroundingOutliers,
                retargeter.maxRendererFootGroundingSeparation,
                out bool rendererGroundingOutlier);

            if (rendererGroundingOutlier && !_rendererGroundingOutlierWarningLogged)
            {
                float separation = Mathf.Abs(rendererMinY - lowestFootBottomY);
                float maxSeparation = Mathf.Max(0.02f, retargeter.maxRendererFootGroundingSeparation);
                Debug.LogWarning($"[PoseSpaceRetargeter] Renderer bounds grounding outlier ignored. rendererMinY={rendererMinY:F3}, footBottomY={lowestFootBottomY:F3}, separation={separation:F3}, limit={maxSeparation:F3}");
                _rendererGroundingOutlierWarningLogged = true;
            }

            return contactBottomY;
        }

        private void ApplyGroundedFootLockXZ(
            PoseSpaceRetargeter retargeter, Transform leftFoot, Transform rightFoot, float targetHeight, float footRadius)
        {
            if (!retargeter.ShouldStabilizeGroundedFootXZ || retargeter.groundedFootLockWeight <= 0f || retargeter.targetAnimator == null)
            {
                _leftFootLocked = false;
                _rightFootLocked = false;
                return;
            }

            Vector3 correctionSum = Vector3.zero;
            int correctionCount = 0;
            AddFootLockCorrection(leftFoot, targetHeight, footRadius, ref _leftFootLocked, ref _leftFootLockPosition, ref correctionSum, ref correctionCount);
            AddFootLockCorrection(rightFoot, targetHeight, footRadius, ref _rightFootLocked, ref _rightFootLockPosition, ref correctionSum, ref correctionCount);
            if (!GroundingStabilizer.TryCalculateGroundedFootLockRootCorrection(
                correctionSum,
                correctionCount,
                retargeter.groundedFootLockWeight,
                retargeter.maxGroundedFootLockStep,
                out Vector3 correction))
            {
                return;
            }

            Vector3 rootPosition = retargeter.targetAnimator.transform.position + correction;
            if (PoseSpaceRetargeter.IsFinite(rootPosition))
            {
                retargeter.targetAnimator.transform.position = rootPosition;
            }
        }

        private void AddFootLockCorrection(
            Transform foot,
            float targetHeight,
            float footRadius,
            ref bool locked,
            ref Vector3 lockPosition,
            ref Vector3 correctionSum,
            ref int correctionCount)
        {
            if (foot == null)
            {
                locked = false;
                return;
            }

            if (!GroundingStabilizer.TryCalculateFootBottomY(foot.position.y, footRadius, out float bottomY))
            {
                locked = false;
                return;
            }

            bool shouldAccumulate = GroundingStabilizer.TryCalculateFootLockCorrection(
                bottomY,
                foot.position,
                targetHeight,
                locked,
                lockPosition,
                out bool nextLocked,
                out Vector3 nextLockPosition,
                out Vector3 correction);
            locked = nextLocked;
            lockPosition = nextLockPosition;
            if (!shouldAccumulate)
            {
                return;
            }

            correctionSum += correction;
            correctionCount++;
        }

        private bool TryGetRendererBoundsMinY(PoseSpaceRetargeter retargeter, out float minY)
        {
            minY = float.NaN;
            if (retargeter.targetAnimator == null)
            {
                return false;
            }

            Renderer[] renderers = retargeter.targetAnimator.GetComponentsInChildren<Renderer>(true);
            bool hasBounds = false;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || renderer.bounds.size.sqrMagnitude <= 0f)
                {
                    continue;
                }

                if (!PoseSpaceRetargeter.IsFinite(renderer.bounds.min.y))
                {
                    continue;
                }

                minY = hasBounds ? Mathf.Min(minY, renderer.bounds.min.y) : renderer.bounds.min.y;
                hasBounds = true;
            }

            return hasBounds;
        }
    }
}
