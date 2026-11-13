using UnityEngine;
using Fbx2Vmd.Retargeting;

namespace Fbx2Vmd.FBXImporter
{
    // 리타게터의 접지 보정 상태와 계산을 담당함. PoseSpaceRetargeter에서 분리됨.
    internal sealed partial class RetargetingGroundingProcessor
    {
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

        private float ResolveEditorFootHeightGroundingReferenceTarget(
            PoseSpaceRetargeter retargeter, float baseTargetHeight)
        {
#if UNITY_EDITOR
            return RetargetingEditorReferenceHooks.FootHeightReference?.ResolveFootHeightGroundingReferenceTarget(retargeter, baseTargetHeight) ?? baseTargetHeight;
#else
            return baseTargetHeight;
#endif
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
