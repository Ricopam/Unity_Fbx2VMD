using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 수동 포즈 기준의 회전·본 구간 방향·Hips·Foot 위치를 계산하고 대상 Transform에 적용함.
    /// </summary>
    internal static partial class ManualPoseReferenceApplier
    {
        internal static float CalculateBodyPositionXzFrameGateWeight(
            float currentFrame,
            float startFrame,
            float endFrame,
            float blendFrames)
        {
            float start = Mathf.Max(0f, Mathf.Round(startFrame));
            float end = Mathf.Max(0f, Mathf.Round(endFrame));
            if (start <= 0f && end <= 0f)
            {
                return 1f;
            }

            if (end < start || end <= 0f)
            {
                return 1f;
            }

            float blend = Mathf.Max(0f, blendFrames);
            if (blend <= 0f)
            {
                return currentFrame >= start && currentFrame <= end ? 1f : 0f;
            }

            return CalculateGateFadeWeight(currentFrame, start, end, blend);
        }

        private static float CalculateGateFadeWeight(
            float currentFrame, float start, float end, float blend)
        {
            if (currentFrame >= start && currentFrame <= end)
            {
                return 1f;
            }
            if (currentFrame < start)
            {
                float fadeStart = start - blend;
                if (currentFrame <= fadeStart)
                {
                    return 0f;
                }
                return Mathf.Clamp01((currentFrame - fadeStart) / blend);
            }
            float fadeEnd = end + blend;
            if (currentFrame >= fadeEnd)
            {
                return 0f;
            }
            return Mathf.Clamp01((fadeEnd - currentFrame) / blend);
        }

        internal static bool TryCalculateBodyPositionXzReference(
            Vector3 currentBodyPosition,
            Vector3 referenceBodyPosition,
            float weight,
            float maxOffset,
            float axisXScale,
            float axisZScale,
            out Vector3 nextBodyPosition)
        {
            nextBodyPosition = currentBodyPosition;
            if (!IsFinite(currentBodyPosition) || !IsFinite(referenceBodyPosition))
            {
                return false;
            }

            float clampedWeight = Mathf.Clamp01(weight);
            if (clampedWeight <= 0f)
            {
                return false;
            }

            Vector3 delta = new Vector3(
                (referenceBodyPosition.x - currentBodyPosition.x) * Mathf.Clamp01(axisXScale),
                0f,
                (referenceBodyPosition.z - currentBodyPosition.z) * Mathf.Clamp01(axisZScale));
            return TryApplyBodyPositionXzDelta(
                currentBodyPosition,
                delta,
                clampedWeight,
                maxOffset,
                out nextBodyPosition);
        }

        internal static bool TryCalculateSignCorrectedBodyPositionXzReference(
            Vector3 currentBodyPosition,
            Vector3 referenceFootPosition,
            Vector3 currentFootPosition,
            float weight,
            float maxOffset,
            float axisXScale,
            float axisZScale,
            out Vector3 nextBodyPosition)
        {
            return TryCalculateSignCorrectedBodyPositionXzReference(
                currentBodyPosition,
                referenceFootPosition,
                currentFootPosition,
                weight,
                maxOffset,
                axisXScale,
                axisZScale,
                invertX: false,
                invertZ: false,
                out nextBodyPosition);
        }

        internal static bool TryCalculateSignCorrectedBodyPositionXzReference(
            Vector3 currentBodyPosition,
            Vector3 referenceFootPosition,
            Vector3 currentFootPosition,
            float weight,
            float maxOffset,
            float axisXScale,
            float axisZScale,
            bool invertX,
            bool invertZ,
            out Vector3 nextBodyPosition)
        {
            nextBodyPosition = currentBodyPosition;
            if (!IsFinite(currentBodyPosition) ||
                !IsFinite(referenceFootPosition) ||
                !IsFinite(currentFootPosition))
            {
                return false;
            }

            float clampedWeight = Mathf.Clamp01(weight);
            if (clampedWeight <= 0f)
            {
                return false;
            }

            Vector3 delta = BuildSignCorrectedDelta(
                referenceFootPosition, currentFootPosition,
                axisXScale, axisZScale, invertX, invertZ);
            return TryApplyBodyPositionXzDelta(
                currentBodyPosition,
                delta,
                clampedWeight,
                maxOffset,
                out nextBodyPosition);
        }

        private static Vector3 BuildSignCorrectedDelta(
            Vector3 referenceFootPosition, Vector3 currentFootPosition,
            float axisXScale, float axisZScale, bool invertX, bool invertZ)
        {
            Vector3 delta = referenceFootPosition - currentFootPosition;
            delta = new Vector3(
                delta.x * Mathf.Clamp01(axisXScale),
                0f,
                delta.z * Mathf.Clamp01(axisZScale));
            if (invertX)
            {
                delta.x = -delta.x;
            }
            if (invertZ)
            {
                delta.z = -delta.z;
            }
            return delta;
        }

        internal static float CalculateBoundedMuscleOutputReference(
            float inputValue,
            float outputValue,
            float weight,
            float maxDelta,
            float fallbackValue)
        {
            if (!IsFinite(outputValue))
            {
                return IsFinite(fallbackValue) ? fallbackValue : outputValue;
            }

            if (!IsFinite(inputValue))
            {
                return outputValue;
            }

            float clampedCorrection = Mathf.Clamp(
                inputValue - outputValue,
                -Mathf.Max(0f, maxDelta),
                Mathf.Max(0f, maxDelta));
            return outputValue + clampedCorrection * Mathf.Clamp01(weight);
        }

        internal static bool TryApplyBoundedMuscleOutputReference(
            ref HumanPose outputPose,
            HumanPose inputPose,
            int muscleIndex,
            float weight,
            float maxDelta)
        {
            if (muscleIndex < 0 ||
                inputPose.muscles == null ||
                outputPose.muscles == null ||
                muscleIndex >= inputPose.muscles.Length ||
                muscleIndex >= outputPose.muscles.Length)
            {
                return false;
            }

            float currentValue = outputPose.muscles[muscleIndex];
            float nextValue = CalculateBoundedMuscleOutputReference(
                inputPose.muscles[muscleIndex],
                currentValue,
                weight,
                maxDelta,
                currentValue);
            if (!IsFinite(nextValue) || Mathf.Approximately(nextValue, currentValue))
            {
                return false;
            }

            outputPose.muscles[muscleIndex] = nextValue;
            return true;
        }
    }
}

