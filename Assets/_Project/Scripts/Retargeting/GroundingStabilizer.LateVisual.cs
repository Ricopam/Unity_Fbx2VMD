using UnityEngine;

namespace Fbx2Vmd.Retargeting
{
    // 최종 시각(late visual) 접지 보정 계산 전담 분할.
    public static partial class GroundingStabilizer
    {
        /// <summary>
        /// 최종 시각 접지 잔차가 적용 가능한 범위인지 판정함.
        /// </summary>
        public static bool TryCalculateLateVisualGroundingEffectiveResidual( float residual, bool smoothLateVisualGroundingCorrection, float groundingDeadZone, float maxLateVisualGroundingCorrection, out float effectiveResidual, out bool exceededMaxCorrection)
        {
            effectiveResidual = 0f;
            exceededMaxCorrection = false;
            bool isPenetrationResidual = residual > 0.0001f;
            bool isFloatingResidual = residual < -0.0001f;
            bool isVisualFloorResidual = isPenetrationResidual || isFloatingResidual;
            float deadZone = Mathf.Max(0.001f, groundingDeadZone);
            float skipDeadZone = isVisualFloorResidual ? 0.001f : deadZone;
            if (Mathf.Abs(residual) <= skipDeadZone)
            {
                return false; }
            float maxCorrection = Mathf.Max(0.001f, maxLateVisualGroundingCorrection);
            if (Mathf.Abs(residual) > maxCorrection)
            {
                exceededMaxCorrection = true;
                return false; }
            effectiveResidual = residual;
            if (smoothLateVisualGroundingCorrection && deadZone > 0f && !isVisualFloorResidual)
            {
                effectiveResidual = Mathf.Sign(residual) * Mathf.Max(0f, Mathf.Abs(residual) - deadZone);
                if (Mathf.Abs(effectiveResidual) <= 0.0001f)
                {
                    effectiveResidual = 0f;
                    return false; } }
            return true; }

        /// <summary>
        /// 현재 접지 이동과 반대 방향인 최종 시각 보정을 건너뛸지 판정함.
        /// </summary>
        public static bool ShouldSkipLateVisualGroundingForActiveVerticalStep(
            float residual,
            bool smoothLateVisualGroundingCorrection,
            float lastGroundingVerticalStep)
        {
            if (!smoothLateVisualGroundingCorrection ||
                !IsFinite(residual) ||
                !IsFinite(lastGroundingVerticalStep) ||
                Mathf.Abs(residual) <= 0.0005f ||
                Mathf.Abs(lastGroundingVerticalStep) <= 0.0005f)
            {
                return false;
            }

            return Mathf.Sign(residual) != Mathf.Sign(lastGroundingVerticalStep);
        }

        /// <summary>
        /// 최종 시각 접지 이동을 적용한 유한 위치를 계산함.
        /// </summary>
        public static bool TryCalculateLateVisualGroundingAppliedPosition(
            Vector3 currentPosition,
            float appliedResidual,
            out Vector3 appliedPosition)
        {
            appliedPosition = Vector3.zero;
            if (!IsFinite(currentPosition))
            {
                return false;
            }

            appliedPosition = currentPosition;
            appliedPosition.y += appliedResidual;
            if (!IsFinite(appliedPosition))
            {
                appliedPosition = Vector3.zero;
                return false;
            }

            return true;
        }
    }
}
