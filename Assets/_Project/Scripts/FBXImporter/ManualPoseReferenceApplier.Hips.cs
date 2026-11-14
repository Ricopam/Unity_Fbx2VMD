using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class ManualPoseReferenceApplier
    {
            internal static bool TryResolveHipsTransforms(
                Animator referenceAnimator,
                Animator targetAnimator,
                out Transform referenceHips,
                out Transform targetHips)
            {
                referenceHips = null;
                targetHips = null;
                if (referenceAnimator == null || targetAnimator == null)
                {
                    return false;
                }

                referenceHips = referenceAnimator.GetBoneTransform(HumanBodyBones.Hips);
                targetHips = targetAnimator.GetBoneTransform(HumanBodyBones.Hips);
                return referenceHips != null && targetHips != null;
            }

            internal static bool TryResolveHipsLocalPositionReference(
                Animator referenceAnimator,
                Animator targetAnimator,
                out Transform targetHips,
                out Vector3 referenceCurrentLocalPosition,
                out Vector3 currentLocalPosition)
            {
                targetHips = null;
                referenceCurrentLocalPosition = Vector3.zero;
                currentLocalPosition = Vector3.zero;
                if (!TryResolveHipsTransforms(
                    referenceAnimator,
                    targetAnimator,
                    out Transform referenceHips,
                    out targetHips))
                {
                    return false;
                }

                referenceCurrentLocalPosition = referenceHips.localPosition;
                currentLocalPosition = targetHips.localPosition;
                return true;
            }

            internal static void ApplyHipsLocalPosition(Transform targetHips, Vector3 localPosition)
            {
                targetHips.localPosition = localPosition;
            }

            internal static bool TryCalculateHipsLocalPositionReference(
                Vector3 referenceCurrentLocalPosition,
                Vector3 referenceRestLocalPosition,
                bool hasReferenceRestLocalPosition,
                Vector3 currentLocalPosition,
                float weight,
                float maxOffset,
                out Vector3 nextLocalPosition)
            {
                return TryCalculateHipsLocalPositionReference(
                    referenceCurrentLocalPosition,
                    referenceRestLocalPosition,
                    hasReferenceRestLocalPosition,
                    currentLocalPosition,
                    false,
                    currentLocalPosition,
                    weight,
                    maxOffset,
                    out nextLocalPosition);
            }

            internal static bool TryCalculateHipsLocalPositionReference(
                Vector3 referenceCurrentLocalPosition,
                Vector3 referenceRestLocalPosition,
                bool hasReferenceRestLocalPosition,
                Vector3 targetRestLocalPosition,
                bool hasTargetRestLocalPosition,
                Vector3 currentLocalPosition,
                float weight,
                float maxOffset,
                out Vector3 nextLocalPosition)
            {
                nextLocalPosition = currentLocalPosition;
                if (!IsFinite(referenceCurrentLocalPosition) || !IsFinite(currentLocalPosition))
                {
                    return false;
                }

                if (hasReferenceRestLocalPosition && !IsFinite(referenceRestLocalPosition))
                {
                    return false;
                }

                Vector3 desiredLocalPosition;
                if (hasReferenceRestLocalPosition)
                {
                    Vector3 referenceDelta = referenceCurrentLocalPosition - referenceRestLocalPosition;
                    Vector3 anchorLocalPosition = hasTargetRestLocalPosition && IsFinite(targetRestLocalPosition)
                        ? targetRestLocalPosition
                        : currentLocalPosition;
                    desiredLocalPosition = anchorLocalPosition + referenceDelta;
                }
                else
                {
                    desiredLocalPosition = referenceCurrentLocalPosition;
                }

                Vector3 delta = desiredLocalPosition - currentLocalPosition;
                if (!IsFinite(delta) || delta.sqrMagnitude <= 0.00000001f)
                {
                    return false;
                }

                float clampedMaxOffset = Mathf.Max(0f, maxOffset);
                if (clampedMaxOffset > 0f)
                {
                    delta = Vector3.ClampMagnitude(delta, clampedMaxOffset);
                }

                nextLocalPosition = currentLocalPosition + delta * Mathf.Clamp01(weight);
                if (!IsFinite(nextLocalPosition))
                {
                    nextLocalPosition = currentLocalPosition;
                    return false;
                }

                return true;
            }

            internal static bool TryResolveHipsAlignedEndpointPositionReference(
                Animator referenceAnimator,
                Animator targetAnimator,
                HumanBodyBones endpointBone,
                Transform referenceHips,
                Transform targetHips,
                Transform targetEndpoint,
                out Vector3 desiredEndpointPosition)
            {
                desiredEndpointPosition = targetEndpoint != null ? targetEndpoint.position : Vector3.zero;
                if (referenceAnimator == null || targetAnimator == null ||
                    referenceHips == null || targetHips == null || targetEndpoint == null)
                {
                    return false;
                }

                Transform referenceEndpoint = referenceAnimator.GetBoneTransform(endpointBone);
                if (referenceEndpoint == null)
                {
                    return false;
                }

                return TryCalculateHipsAlignedEndpointPositionReference(
                    referenceEndpoint.position,
                    referenceHips.position,
                    referenceAnimator.transform,
                    targetHips.position,
                    targetEndpoint.position,
                    targetAnimator.transform,
                    out desiredEndpointPosition);
            }

            internal static bool TryCalculateHipsAlignedEndpointPositionReference(
                Vector3 referenceEndpointPosition,
                Vector3 referenceHipsPosition,
                Transform referenceRoot,
                Vector3 targetHipsPosition,
                Vector3 currentTargetEndpointPosition,
                Transform targetRoot,
                out Vector3 desiredEndpointPosition)
            {
                desiredEndpointPosition = currentTargetEndpointPosition;
                if (referenceRoot == null || targetRoot == null)
                {
                    return false;
                }

                Vector3 referenceOffset = referenceEndpointPosition - referenceHipsPosition;
                if (!IsFinite(referenceOffset))
                {
                    return false;
                }

                Vector3 referenceRootOffset = referenceRoot.InverseTransformVector(referenceOffset);
                Vector3 desiredTargetOffset = targetRoot.TransformVector(referenceRootOffset);
                if (!IsFinite(desiredTargetOffset))
                {
                    return false;
                }

                desiredEndpointPosition = targetHipsPosition + desiredTargetOffset;
                desiredEndpointPosition.y = currentTargetEndpointPosition.y;
                return IsFinite(desiredEndpointPosition);
    }
}
