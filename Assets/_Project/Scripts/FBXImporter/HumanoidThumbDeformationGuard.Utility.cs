using System.Text;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidThumbDeformationGuard
    {
        private static string FormatDebugFloat(float value)
        {
            return IsFinite(value) ? value.ToString("F4") : "NaN";
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return "<none>";
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder(transform.name);
            Transform current = transform.parent;
            while (current != null)
            {
                builder.Insert(0, current.name + "/");
                current = current.parent;
            }

            return builder.ToString();
        }

        private Quaternion GetThumbRotationOffset(HumanBodyBones thumbBone)
        {
            switch (thumbBone)
            {
                case HumanBodyBones.LeftThumbProximal:
                    return GetProximalRotationOffsetRotation(false);
                case HumanBodyBones.RightThumbProximal:
                    return GetProximalRotationOffsetRotation(true);
                default:
                    return Quaternion.identity;
            }
        }

        private Quaternion GetProximalRotationOffsetRotation(Transform thumbTransform)
        {
            return GetProximalRotationOffsetRotation(TryResolveThumbSide(thumbTransform, out bool isRightThumb) && isRightThumb);
        }

        private Quaternion GetProximalRotationOffsetRotation(bool isRightThumb)
        {
            Vector3 offset = GetProximalRotationOffset(isRightThumb);
            if (offset.sqrMagnitude <= 0.000001f)
            {
                return Quaternion.identity;
            }

            return Quaternion.Euler(offset);
        }

        private Vector3 GetProximalRotationOffset(bool isRightThumb)
        {
            Vector3 offset = proximalLocalRotationOffset;
            if (isRightThumb && mirrorRightProximalLocalRotationOffset)
            {
                offset = new Vector3(offset.x, -offset.y, -offset.z);
            }

            return offset + (isRightThumb ? rightProximalLocalRotationOffset : leftProximalLocalRotationOffset);
        }

        private static bool IsRightHumanThumbBone(HumanBodyBones thumbBone)
        {
            switch (thumbBone)
            {
                case HumanBodyBones.RightThumbProximal:
                case HumanBodyBones.RightThumbIntermediate:
                case HumanBodyBones.RightThumbDistal:
                    return true;
                default:
                    return false;
            }
        }

        private Quaternion GetCurrentRawLocalRotation(Transform targetTransform)
        {
            Quaternion currentRotation = targetTransform.localRotation;
            if (_lastCorrectedLocalRotations.TryGetValue(targetTransform, out Quaternion lastCorrectedRotation) &&
                _lastRawLocalRotations.TryGetValue(targetTransform, out Quaternion lastRawRotation) &&
                Quaternion.Angle(currentRotation, lastCorrectedRotation) <= 0.001f)
            {
                return lastRawRotation;
            }

            return currentRotation;
        }

        private void SetCorrectedLocalRotation(Transform targetTransform, Quaternion rawRotation, Quaternion correctedRotation)
        {
            _lastRawLocalRotations[targetTransform] = rawRotation;
            _lastCorrectedLocalRotations[targetTransform] = correctedRotation;
            targetTransform.localRotation = correctedRotation;
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

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion rotation)
        {
            return IsFinite(rotation.x) &&
                IsFinite(rotation.y) &&
                IsFinite(rotation.z) &&
                IsFinite(rotation.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
