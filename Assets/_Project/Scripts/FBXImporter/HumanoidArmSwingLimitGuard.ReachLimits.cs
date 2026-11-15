using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 팔 스윙 제한 가드의 리치(손 도달 거리) 제한 책임 분리 partial.
    public partial class HumanoidArmSwingLimitGuard
    {
        void ApplyHorizontalReachLimits(
            float reachLimitWeight, float handHorizontalReachRatio,
            float reachMaxHandBelowShoulderRatio, float reachMinElbowAngleAfterApply)
        {
            horizontalReachLimitWeight = Mathf.Clamp01(reachLimitWeight);
            maxHandHorizontalReachRatio = Mathf.Clamp(handHorizontalReachRatio, 0f, 1.5f);
            horizontalReachMaxHandBelowShoulderRatio = Mathf.Clamp(reachMaxHandBelowShoulderRatio, 0f, 1.5f);
            horizontalReachMinElbowAngleAfterApply = Mathf.Clamp(reachMinElbowAngleAfterApply, 0f, 180f);
        }

        // 팔 들림 포즈 전용 리치 제한 계열 파라미터를 유효 범위로 고정해 필드에 반영한다.
        void ApplyRaisedPoseReachLimits(
            float raisedReachLimitWeight, float raisedMinUpperArmDownDot,
            float raisedMaxHandBelowShoulderRatio, float raisedMaxHandHorizontalReachRatio)
        {
            raisedPoseHorizontalReachLimitWeight = Mathf.Clamp01(raisedReachLimitWeight);
            raisedPoseMinUpperArmDownDot = Mathf.Clamp01(raisedMinUpperArmDownDot);
            raisedPoseMaxHandBelowShoulderRatio = Mathf.Clamp(raisedMaxHandBelowShoulderRatio, 0f, 1.5f);
            raisedPoseMaxHandHorizontalReachRatio = Mathf.Clamp(raisedMaxHandHorizontalReachRatio, 0f, 1.5f);
        }

        private bool TryApplyHorizontalReachLimit( Transform upper, Transform lower, Transform hand, Vector3 localHandOffset, float armLength, float belowShoulderRatio)
        {
            float reachWeight = Mathf.Clamp01(horizontalReachLimitWeight);
            float maxReachRatio = Mathf.Clamp(maxHandHorizontalReachRatio, 0f, 1.5f);
            float reachMaxBelowRatio = horizontalReachMaxHandBelowShoulderRatio > 0f
                ? Mathf.Clamp(horizontalReachMaxHandBelowShoulderRatio, 0f, 1.5f) : maxHandBelowShoulderRatio;
            if (reachWeight <= 0f || maxReachRatio <= 0f || armLength <= 0.0001f || belowShoulderRatio > reachMaxBelowRatio)
            {
                return false; }
            Vector2 horizontal = new Vector2(localHandOffset.x, localHandOffset.z);
            float horizontalDistance = horizontal.magnitude;
            float maxHorizontalDistance = maxReachRatio * armLength;
            if (horizontalDistance <= maxHorizontalDistance || horizontalDistance <= 0.0001f)
            {
                return false; }
            Vector2 targetHorizontal = horizontal.normalized * maxHorizontalDistance;
            Vector3 targetLocalOffset = new Vector3( targetHorizontal.x, localHandOffset.y, targetHorizontal.y);
            Vector3 upperLocalPosition = transform.InverseTransformPoint(upper.position);
            Vector3 targetWorldHandPosition = transform.TransformPoint(upperLocalPosition + targetLocalOffset);
            if (!IsFinite(targetWorldHandPosition))
            {
                return false; }
            return ApplyHorizontalReachRotation( upper, lower, hand, targetWorldHandPosition, reachWeight, horizontalDistance / armLength, maxReachRatio); }

        // 목표 손 위치로 상완을 회전시키고 전완을 재지향 — 후속 판정 실패 시 회전을 롤백한다.
        bool ApplyHorizontalReachRotation(
            Transform upper, Transform lower, Transform hand,
            Vector3 targetWorldHandPosition, float reachWeight,
            float horizontalRatio, float maxReachRatio)
        {
            Quaternion upperRotationBefore = upper.rotation;
            Quaternion lowerRotationBefore = lower.rotation;
            RotateBoneTowardDirection( upper, lower.position - upper.position, targetWorldHandPosition - upper.position, reachWeight);
            RedirectForearmToHandTarget(lower, hand, targetWorldHandPosition, reachWeight);
            if (ShouldRollbackHorizontalReachLimit(upper, lower, hand))
            {
                upper.rotation = upperRotationBefore;
                lower.rotation = lowerRotationBefore;
                return false; }
            LogOnce($"손 수평 reach를 제한했습니다. bone={upper.name}, horizontalRatio={horizontalRatio:F3}, maxRatio={maxReachRatio:F3}");
            return true; }

        private bool ShouldRollbackHorizontalReachLimit(Transform upper, Transform lower, Transform hand)
        {
            float minElbowAngle = Mathf.Clamp(horizontalReachMinElbowAngleAfterApply, 0f, 180f);
            if (minElbowAngle <= 0f)
            {
                return false;
            }

            float elbowAngle = CalculateElbowAngleDegrees(upper, lower, hand);
            return IsFinite(elbowAngle) && elbowAngle < minElbowAngle;
        }

        private bool TryApplyRaisedPoseHorizontalReachLimit(
            Transform upper,
            Transform lower, Transform hand, Vector3 localHandOffset, float armLength, float currentDownDot, float belowShoulderRatio)
        {
            float reachWeight = Mathf.Clamp01(raisedPoseHorizontalReachLimitWeight);
            float minDownDot = Mathf.Clamp01(raisedPoseMinUpperArmDownDot);
            float maxBelowRatio = Mathf.Clamp(raisedPoseMaxHandBelowShoulderRatio, 0f, 1.5f);
            float maxReachRatio = Mathf.Clamp(raisedPoseMaxHandHorizontalReachRatio, 0f, 1.5f);
            if (reachWeight <= 0f || maxReachRatio <= 0f || armLength <= 0.0001f || currentDownDot < minDownDot || belowShoulderRatio > maxBelowRatio)
            {
                return false; }
            Vector2 horizontal = new Vector2(localHandOffset.x, localHandOffset.z);
            float horizontalDistance = horizontal.magnitude;
            float maxHorizontalDistance = maxReachRatio * armLength;
            if (horizontalDistance <= maxHorizontalDistance || horizontalDistance <= 0.0001f)
            {
                return false; }
            Vector2 targetHorizontal = horizontal.normalized * maxHorizontalDistance;
            Vector3 targetLocalOffset = new Vector3( targetHorizontal.x, localHandOffset.y, targetHorizontal.y);
            Vector3 upperLocalPosition = transform.InverseTransformPoint(upper.position);
            Vector3 targetWorldHandPosition = transform.TransformPoint(upperLocalPosition + targetLocalOffset);
            if (!IsFinite(targetWorldHandPosition))
            {
                return false; }
            RotateBoneTowardDirection( upper, lower.position - upper.position, targetWorldHandPosition - upper.position, reachWeight);
            RedirectForearmToHandTarget(lower, hand, targetWorldHandPosition, reachWeight);
            LogOnce($"Raised-pose horizontal reach limited. bone={upper.name}, downDot={currentDownDot:F3}, belowRatio={belowShoulderRatio:F3}, horizontalRatio={horizontalDistance / armLength:F3}, maxRatio={maxReachRatio:F3}");
            return true; }
    }
}
