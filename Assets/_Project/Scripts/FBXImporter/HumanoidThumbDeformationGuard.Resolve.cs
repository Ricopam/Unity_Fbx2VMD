using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidThumbDeformationGuard
    {
        private void CacheThumbSide(Transform thumbTransform, bool? knownIsRightThumb = null)
        {
            if (thumbTransform == null || _cachedThumbSides.ContainsKey(thumbTransform))
            {
                return;
            }

            if (knownIsRightThumb.HasValue)
            {
                _cachedThumbSides[thumbTransform] = knownIsRightThumb.Value;
                return;
            }

            if (TryResolveThumbSideFromHumanMapping(thumbTransform, out bool isRightThumb) ||
                (thumbTransform != null &&
                    ThumbTransformNamePolicy.TryResolveSide(thumbTransform.name, out isRightThumb)) ||
                TryResolveThumbSideByReferenceDistance(thumbTransform, out isRightThumb))
            {
                _cachedThumbSides[thumbTransform] = isRightThumb;
            }
        }

        private bool IsThumbBaseHelperTransform(Transform candidate)
        {
            return candidate != null &&
                !IsMappedHumanThumbBone(candidate) &&
                ThumbTransformNamePolicy.IsBaseHelper(candidate.name);
        }

        private bool IsDetachedThumbBaseHelperTransform(Transform candidate)
        {
            if (!IsThumbBaseHelperTransform(candidate))
            {
                return false;
            }

            string normalizedName = candidate.name.ToLowerInvariant();
            return !normalizedName.Contains("!") &&
                !normalizedName.Contains("ghost");
        }

        private bool IsMappedHumanThumbBone(Transform candidate)
        {
            if (candidate == null || targetAnimator == null)
            {
                return false;
            }

            foreach (HumanBodyBones thumbBone in ThumbBones)
            {
                if (targetAnimator.GetBoneTransform(thumbBone) == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        private Transform GetMappedThumbProximal(bool isRightThumb)
        {
            if (targetAnimator == null)
            {
                return null;
            }

            return targetAnimator.GetBoneTransform(
                isRightThumb ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal);
        }

        private Transform GetClosestMappedThumbProximal(Transform referenceTransform)
        {
            if (referenceTransform == null)
            {
                return null;
            }

            Transform leftThumbProximal = GetMappedThumbProximal(false);
            Transform rightThumbProximal = GetMappedThumbProximal(true);
            if (leftThumbProximal == null)
            {
                return rightThumbProximal != referenceTransform ? rightThumbProximal : null;
            }

            if (rightThumbProximal == null)
            {
                return leftThumbProximal != referenceTransform ? leftThumbProximal : null;
            }

            float leftDistance = (referenceTransform.position - leftThumbProximal.position).sqrMagnitude;
            float rightDistance = (referenceTransform.position - rightThumbProximal.position).sqrMagnitude;
            Transform closest = rightDistance < leftDistance ? rightThumbProximal : leftThumbProximal;
            return closest != referenceTransform ? closest : null;
        }

        private bool TryResolveThumbSide(Transform thumbTransform, out bool isRightThumb)
        {
            if (thumbTransform != null && _cachedThumbSides.TryGetValue(thumbTransform, out isRightThumb))
            {
                return true;
            }

            if (TryResolveThumbSideFromHumanMapping(thumbTransform, out isRightThumb) ||
                (thumbTransform != null &&
                    ThumbTransformNamePolicy.TryResolveSide(thumbTransform.name, out isRightThumb)) ||
                TryResolveThumbSideByReferenceDistance(thumbTransform, out isRightThumb))
            {
                if (thumbTransform != null)
                {
                    _cachedThumbSides[thumbTransform] = isRightThumb;
                }

                return true;
            }

            isRightThumb = false;
            return false;
        }

        private bool TryResolveThumbSideFromHumanMapping(Transform thumbTransform, out bool isRightThumb)
        {
            if (thumbTransform != null && targetAnimator != null)
            {
                foreach (HumanBodyBones thumbBone in ThumbBones)
                {
                    if (targetAnimator.GetBoneTransform(thumbBone) == thumbTransform)
                    {
                        isRightThumb = IsRightHumanThumbBone(thumbBone);
                        return true;
                    }
                }

                if (targetAnimator.GetBoneTransform(HumanBodyBones.LeftHand) == thumbTransform)
                {
                    isRightThumb = false;
                    return true;
                }

                if (targetAnimator.GetBoneTransform(HumanBodyBones.RightHand) == thumbTransform)
                {
                    isRightThumb = true;
                    return true;
                }
            }

            isRightThumb = false;
            return false;
        }

        private bool TryResolveThumbSideByReferenceDistance(Transform thumbTransform, out bool isRightThumb)
        {
            if (thumbTransform != null && targetAnimator != null)
            {
                float leftDistance = GetThumbSideReferenceDistance( thumbTransform, targetAnimator.GetBoneTransform(HumanBodyBones.LeftHand), GetMappedThumbProximal(false));
                float rightDistance = GetThumbSideReferenceDistance( thumbTransform, targetAnimator.GetBoneTransform(HumanBodyBones.RightHand), GetMappedThumbProximal(true));
                if (IsFinite(leftDistance) || IsFinite(rightDistance))
                {
                    if (!IsFinite(leftDistance))
                    {
                        isRightThumb = true;
                        return true;
                    }

                    if (!IsFinite(rightDistance))
                    {
                        isRightThumb = false;
                        return true;
                    }

                    isRightThumb = rightDistance < leftDistance;
                    return true;
                }
            }

            isRightThumb = false;
            return false;
        }

        private static float GetThumbSideReferenceDistance(
            Transform thumbTransform,
            Transform handTransform,
            Transform thumbProximalTransform)
        {
            float handDistance = handTransform != null
                ? (thumbTransform.position - handTransform.position).sqrMagnitude
                : float.NaN;
            float thumbDistance = thumbProximalTransform != null
                ? (thumbTransform.position - thumbProximalTransform.position).sqrMagnitude
                : float.NaN;

            if (!IsFinite(handDistance))
            {
                return thumbDistance;
            }

            if (!IsFinite(thumbDistance))
            {
                return handDistance;
            }

            return Mathf.Min(handDistance, thumbDistance);
        }

        private static bool IsExplicitDetachedThumbBaseSource(Transform sourceTransform)
        {
            return sourceTransform != null &&
                ThumbTransformNamePolicy.IsActiveBaseSource(sourceTransform.name);
        }
    }
}
