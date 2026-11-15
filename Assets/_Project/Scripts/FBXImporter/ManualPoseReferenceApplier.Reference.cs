using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class ManualPoseReferenceApplier
    {
        internal static bool TryCalculateSegmentDirectionReference(
            Vector3 referenceSegmentDirection,
            Vector3 currentSegmentDirection,
            Quaternion currentParentWorldRotation,
            float weight,
            float maxAngleDegrees,
            float correctionAxisXzScale,
            out Quaternion nextParentWorldRotation)
        {
            nextParentWorldRotation = currentParentWorldRotation;
            if (!IsFinite(referenceSegmentDirection) ||
                !IsFinite(currentSegmentDirection) ||
                !IsFinite(currentParentWorldRotation) ||
                !TryNormalize(referenceSegmentDirection, out Vector3 referenceDirection) ||
                !TryNormalize(currentSegmentDirection, out Vector3 currentDirection))
            {
                return false;
            }

            if (!TryBuildCorrection(
                    referenceDirection,
                    currentDirection,
                    maxAngleDegrees,
                    correctionAxisXzScale,
                    weight,
                    out Quaternion correction))
            {
                return false;
            }

            nextParentWorldRotation = correction * currentParentWorldRotation;
            if (!IsFinite(nextParentWorldRotation) ||
                Quaternion.Angle(currentParentWorldRotation, nextParentWorldRotation) <= 0.001f)
            {
                nextParentWorldRotation = currentParentWorldRotation;
                return false;
            }

            return true;
        }

        static bool TryBuildCorrection(
            Vector3 referenceDirection, Vector3 currentDirection,
            float maxAngleDegrees, float correctionAxisXzScale, float weight,
            out Quaternion correction)
        {
            correction = Quaternion.FromToRotation(currentDirection, referenceDirection);
            if (!IsFinite(correction))
            {
                return false;
            }

            float maxAngle = Mathf.Max(0f, maxAngleDegrees);
            if (maxAngle > 0f)
            {
                float angle = Quaternion.Angle(Quaternion.identity, correction);
                if (angle > maxAngle)
                {
                    correction = Quaternion.Slerp(Quaternion.identity, correction, maxAngle / angle);
                }
            }

            correction = ScaleCorrectionAxisXz(correction, correctionAxisXzScale);
            if (!IsFinite(correction))
            {
                return false;
            }

            float clampedWeight = Mathf.Clamp01(weight);
            if (clampedWeight < 0.999f)
            {
                correction = Quaternion.Slerp(Quaternion.identity, correction, clampedWeight);
            }
            return true;
        }

        internal static bool HasActiveFrameGate(float startFrame, float endFrame)
        {
            float start = Mathf.Max(0f, startFrame);
            float end = Mathf.Max(0f, endFrame);
            return HasConfiguredFrameGate(start, end) && end >= start && end > 0f;
        }

        internal static bool HasConfiguredFrameGate(float startFrame, float endFrame)
        {
            float start = Mathf.Max(0f, startFrame);
            float end = Mathf.Max(0f, endFrame);
            return start > 0f || end > 0f;
        }

        internal static bool IsFrameWithinGate(int currentFrame, float startFrame, float endFrame)
        {
            if (!HasActiveFrameGate(startFrame, endFrame))
            {
                return true;
            }

            float start = Mathf.Max(0f, startFrame);
            float end = Mathf.Max(0f, endFrame);
            return currentFrame >= Mathf.RoundToInt(start) && currentFrame <= Mathf.RoundToInt(end);
        }

        internal static bool IsFrameWithinSingleFrameFallbackGate(
            int currentFrame,
            float startFrame,
            float endFrame)
        {
            float start = Mathf.Max(0f, startFrame);
            float end = Mathf.Max(0f, endFrame);
            if (!HasConfiguredFrameGate(start, end))
            {
                return true;
            }

            if (end <= 0f || end < start)
            {
                end = start;
            }

            return currentFrame >= Mathf.RoundToInt(start) && currentFrame <= Mathf.RoundToInt(end);
        }

        internal static int ApplyExactLocalRotationReference(
            Animator referenceAnimator,
            Animator targetAnimator,
            HumanBodyBones bone)
        {
            if (referenceAnimator == null || targetAnimator == null)
            {
                return 0;
            }

            Transform source = referenceAnimator.GetBoneTransform(bone);
            Transform target = targetAnimator.GetBoneTransform(bone);
            if (source == null || target == null)
            {
                return 0;
            }

            Quaternion sourceRotation = source.localRotation;
            if (!IsFinite(sourceRotation) || Quaternion.Angle(target.localRotation, sourceRotation) <= 0.001f)
            {
                return 0;
            }

            target.localRotation = sourceRotation;
            return 1;
        }

        internal static int ApplyBlendedLocalRotationReference(
            Animator referenceAnimator,
            Animator targetAnimator,
            HumanBodyBones bone,
            float weight)
        {
            if (referenceAnimator == null || targetAnimator == null)
            {
                return 0;
            }

            Transform source = referenceAnimator.GetBoneTransform(bone);
            Transform target = targetAnimator.GetBoneTransform(bone);
            if (source == null || target == null)
            {
                return 0;
            }

            if (!TryCalculateLocalRotationReference(
                    source.localRotation,
                    target.localRotation,
                    weight,
                    out Quaternion nextLocalRotation))
            {
                return 0;
            }

            target.localRotation = nextLocalRotation;
            return 1;
        }

        internal static bool TryCalculateLocalRotationReference(
            Quaternion referenceLocalRotation,
            Quaternion currentLocalRotation,
            float weight,
            out Quaternion nextLocalRotation)
        {
            nextLocalRotation = currentLocalRotation;
            if (!IsFinite(referenceLocalRotation) || !IsFinite(currentLocalRotation))
            {
                return false;
            }

            if (Quaternion.Angle(currentLocalRotation, referenceLocalRotation) <= 0.001f)
            {
                return false;
            }

            nextLocalRotation = Quaternion.Slerp(
                currentLocalRotation,
                referenceLocalRotation,
                Mathf.Clamp01(weight));
            if (!IsFinite(nextLocalRotation))
            {
                nextLocalRotation = currentLocalRotation;
                return false;
            }

            return true;
        }


        internal static bool TryResolveFootIkPositionReference(
            Animator referenceAnimator,
            Animator targetAnimator,
            HumanBodyBones footBone,
            Transform referenceHips,
            Transform targetHips,
            float weight,
            float maxOffset,
            out Vector3 nextPosition)
        {
            nextPosition = Vector3.zero;
            if (referenceAnimator == null || targetAnimator == null ||
                referenceHips == null || targetHips == null)
            {
                return false;
            }

            Transform referenceFoot = referenceAnimator.GetBoneTransform(footBone);
            Transform targetFoot = targetAnimator.GetBoneTransform(footBone);
            if (referenceFoot == null || targetFoot == null)
            {
                return false;
            }

            return TryCalculateFootIkPositionReference(
                referenceFoot.position,
                referenceHips.position,
                targetFoot.position,
                targetHips.position,
                weight,
                maxOffset,
                out nextPosition);
        }

        internal static bool TryCalculateFootIkPositionReference(
            Vector3 referenceFootPosition,
            Vector3 referenceHipsPosition,
            Vector3 currentFootPosition,
            Vector3 targetHipsPosition,
            float weight,
            float maxOffset,
            out Vector3 nextPosition)
        {
            nextPosition = currentFootPosition;
            if (!IsFinite(referenceFootPosition) ||
                !IsFinite(referenceHipsPosition) ||
                !IsFinite(currentFootPosition) ||
                !IsFinite(targetHipsPosition))
            {
                return false;
            }

            Vector3 desiredPosition = targetHipsPosition + (referenceFootPosition - referenceHipsPosition);
            Vector3 delta = desiredPosition - currentFootPosition;
            if (!IsFinite(delta) || delta.sqrMagnitude <= 0.00000001f)
            {
                return false;
            }

            float clampedMaxOffset = Mathf.Max(0f, maxOffset);
            if (clampedMaxOffset > 0f)
            {
                delta = Vector3.ClampMagnitude(delta, clampedMaxOffset);
            }

            nextPosition = currentFootPosition + delta * Mathf.Clamp01(weight);
            if (!IsFinite(nextPosition))
            {
                nextPosition = currentFootPosition;
                return false;
            }

            return true;
        }


        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Quaternion value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z) &&
                !float.IsNaN(value.w) && !float.IsInfinity(value.w);
        }

        private static Quaternion ScaleCorrectionAxisXz(Quaternion correction, float axisXzScale)
        {
            float scale = Mathf.Clamp01(axisXzScale);
            if (scale >= 0.999f)
            {
                return correction;
            }

            correction.ToAngleAxis(out float angle, out Vector3 axis);
            if (!IsFinite(angle) || angle <= 0.001f || !IsFinite(axis))
            {
                return correction;
            }

            Vector3 scaledAxis = new Vector3(axis.x * scale, axis.y, axis.z * scale);
            if (!TryNormalize(scaledAxis, out Vector3 normalizedAxis))
            {
                return Quaternion.identity;
            }

            return Quaternion.AngleAxis(angle, normalizedAxis);
        }

        private static bool TryApplyBodyPositionXzDelta(
            Vector3 currentBodyPosition,
            Vector3 delta,
            float clampedWeight,
            float maxOffset,
            out Vector3 nextBodyPosition)
        {
            nextBodyPosition = currentBodyPosition;
            if (!IsFinite(delta) || delta.sqrMagnitude <= 0.00000001f)
            {
                return false;
            }

            float clampedMaxOffset = Mathf.Max(0f, maxOffset);
            if (clampedMaxOffset > 0f)
            {
                float magnitude = delta.magnitude;
                if (magnitude > clampedMaxOffset)
                {
                    delta = delta / magnitude * clampedMaxOffset;
                }
            }

            nextBodyPosition = new Vector3(
                currentBodyPosition.x + delta.x * clampedWeight,
                currentBodyPosition.y,
                currentBodyPosition.z + delta.z * clampedWeight);
            return IsFinite(nextBodyPosition);
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool TryNormalize(Vector3 value, out Vector3 normalized)
        {
            normalized = Vector3.zero;
            if (!IsFinite(value) || value.sqrMagnitude <= 0.00000001f)
            {
                return false;
            }

            normalized = value.normalized;
            return IsFinite(normalized);
        }
    }
}
