using UnityEngine;
using Fbx2Vmd.Retargeting;

namespace Fbx2Vmd.FBXImporter
{
    // 레이캐스트 그라운딩 적용·발락 보정을 담당하는 partial 분할.
    internal sealed partial class RetargetingGroundingProcessor
    {
        internal void ApplyRaycastGrounding(PoseSpaceRetargeter retargeter)
        {
            if (!TryPrepareRaycastGrounding(
                retargeter,
                out Transform lFoot,
                out Transform rFoot,
                out float footRadius,
                out float targetHeight,
                out float adjustment))
            {
                return;
            }

            Vector3 currentPos = retargeter.targetAnimator.transform.position;
            if (!PoseSpaceRetargeter.IsFinite(currentPos))
            {
                retargeter.LogPoseWarning("Target position became non-finite before grounding. Resetting to origin.");
                currentPos = Vector3.zero;
            }

            if (TryApplyFrozenRaycastGrounding(retargeter, ref currentPos, lFoot, rFoot, targetHeight, footRadius))
            {
                return;
            }

            ApplyGroundingVerticalStep(retargeter, currentPos, adjustment, lFoot, rFoot, targetHeight, footRadius);
        }

        private bool TryApplyFrozenRaycastGrounding(
            PoseSpaceRetargeter retargeter,
            ref Vector3 currentPos,
            Transform lFoot,
            Transform rFoot,
            float targetHeight,
            float footRadius)
        {
            if (!retargeter.freezeRootYAfterInitialGrounding || !_groundingInitialized || !_hasFrozenGroundingRootY)
            {
                return false;
            }

            currentPos.y = _frozenGroundingRootY;
            if (PoseSpaceRetargeter.IsFinite(currentPos))
            {
                retargeter.targetAnimator.transform.position = currentPos;
                ApplyGroundedFootLockXZ(retargeter, lFoot, rFoot, targetHeight, footRadius);
            }

            _lastGroundingVerticalStep = 0f;
            return true;
        }

        private bool TryPrepareRaycastGrounding(
            PoseSpaceRetargeter retargeter,
            out Transform lFoot,
            out Transform rFoot,
            out float footRadius,
            out float targetHeight,
            out float adjustment)
        {
            targetHeight = 0f;
            adjustment = 0f;

            if (!TryResolveGroundingFeet(retargeter, out lFoot, out rFoot, out footRadius, out float contactBottomY))
            {
                return false;
            }

            return TryResolveGroundingTargetAndAdjustment(retargeter, contactBottomY, out targetHeight, out adjustment);
        }

        private bool TryResolveGroundingFeet(
            PoseSpaceRetargeter retargeter,
            out Transform lFoot,
            out Transform rFoot,
            out float footRadius,
            out float contactBottomY)
        {
            // 양발 위치 확보 (발목)
            lFoot = retargeter.targetAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            rFoot = retargeter.targetAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
            footRadius = 0f;
            contactBottomY = 0f;

            if (lFoot == null || rFoot == null) return false;

            // 발바닥 위치 (발목 - 반지름)
            if (!_hasEstimatedFootRadius)
            {
                CalibrateTargetFootRadius(retargeter);
            }

            footRadius = GetEstimatedFootRadius();
            if (!GroundingStabilizer.TryCalculateFootBottomY(lFoot.position.y, footRadius, out float lBottom) ||
                !GroundingStabilizer.TryCalculateFootBottomY(rFoot.position.y, footRadius, out float rBottom))
            {
                retargeter.LogPoseWarning("Foot position became non-finite. Skipping grounding for this frame.");
                return false;
            }

            // 현재 가장 낮은 발바닥 높이. Renderer bounds가 발에서 너무 멀면
            // 옷/머리카락/소매 outlier로 보고 발 기준을 유지한다.
            float lowestFootCurrentY = Mathf.Min(lBottom, rBottom);
            contactBottomY = ResolveGroundingContactBottomY(retargeter, lowestFootCurrentY);
            return true;
        }

        private bool TryResolveGroundingTargetAndAdjustment(
            PoseSpaceRetargeter retargeter,
            float contactBottomY,
            out float targetHeight,
            out float adjustment)
        {
            // 목표는 지면(0) + Offset
            // Raycast를 사용하여 실제 지면을 찾을 수도 있으나, 현재는 평면(Plane) 위라고 가정하고 0.0f 사용
            // 만약 계단이나 경사면이라면 Physics.Raycast로 hit.point.y를 구해야 함.
            float targetGroundY = 0.0f; // 평면 가정
            targetHeight = ResolveEditorFootHeightGroundingReferenceTarget(retargeter, targetGroundY + retargeter.groundOffset);
            _lastGroundingTargetY = targetGroundY;
            _lastGroundingLowestFootBottomY = contactBottomY;

            // 보정값 계산 (목표 - 현재)
            // 양수면 들어 올리고, 음수면 내림 (양방향)
            if (!GroundingStabilizer.TryCalculateAdjustment(targetHeight, contactBottomY, out adjustment))
            {
                retargeter.LogPoseWarning("Grounding adjustment became non-finite. Skipping grounding for this frame.");
                _lastGroundingAdjustment = float.NaN;
                return false;
            }

            _lastGroundingAdjustment = adjustment;
            _maxGroundingAdjustment = Mathf.Max(_maxGroundingAdjustment, Mathf.Abs(adjustment));
            return true;
        }

        private void ApplyGroundingVerticalStep(
            PoseSpaceRetargeter retargeter,
            Vector3 currentPos,
            float adjustment,
            Transform lFoot,
            Transform rFoot,
            float targetHeight,
            float footRadius)
        {
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

            RecordGroundingStepMetrics(appliedVerticalStep, wasGroundingInitialized, smoothedGrounding, clampedGroundingStep);
            float clampedNextY = currentPos.y + appliedVerticalStep;
            currentPos.y = clampedNextY;
            CommitGroundedPosition(retargeter, currentPos, lFoot, rFoot, targetHeight, footRadius);
        }

        private void RecordGroundingStepMetrics(float appliedVerticalStep, bool wasGroundingInitialized, bool smoothedGrounding, bool clampedGroundingStep)
        {
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
        }

        private void CommitGroundedPosition(
            PoseSpaceRetargeter retargeter,
            Vector3 currentPos,
            Transform lFoot,
            Transform rFoot,
            float targetHeight,
            float footRadius)
        {
            if (!PoseSpaceRetargeter.IsFinite(currentPos))
            {
                return;
            }

            retargeter.targetAnimator.transform.position = currentPos;
            if (retargeter.freezeRootYAfterInitialGrounding && !_hasFrozenGroundingRootY)
            {
                _frozenGroundingRootY = currentPos.y;
                _hasFrozenGroundingRootY = true;
            }

            ApplyGroundedFootLockXZ(retargeter, lFoot, rFoot, targetHeight, footRadius);
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
            if (foot == null ||
                !GroundingStabilizer.TryCalculateFootBottomY(foot.position.y, footRadius, out float bottomY))
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
    }
}
