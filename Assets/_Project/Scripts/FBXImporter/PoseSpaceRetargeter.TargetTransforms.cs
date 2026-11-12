using System;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 타겟 아바타의 본/트랜스폼 조회·정규화를 담당하는 partial 분할.
    public partial class PoseSpaceRetargeter
    {
        internal void CaptureTargetFootPositions(out Vector3 leftFootPosition, out Vector3 rightFootPosition)
        {
            leftFootPosition = ReadTargetBoneWorldPosition(HumanBodyBones.LeftFoot);
            rightFootPosition = ReadTargetBoneWorldPosition(HumanBodyBones.RightFoot);
        }

        internal Vector3 ReadTargetBoneWorldPosition(HumanBodyBones bone)
        {
            if (targetAnimator == null)
            {
                return BuildNaNVector3();
            }

            Transform targetBone = targetAnimator.GetBoneTransform(bone);
            return targetBone != null ? targetBone.position : BuildNaNVector3();
        }

        internal float CalculateTargetFootXzDelta(Vector3 beforePosition, HumanBodyBones footBone)
        {
            Vector3 afterPosition = ReadTargetBoneWorldPosition(footBone);
            if (!IsFinite(beforePosition) || !IsFinite(afterPosition))
            {
                return float.NaN;
            }

            Vector2 beforeXz = new Vector2(beforePosition.x, beforePosition.z);
            Vector2 afterXz = new Vector2(afterPosition.x, afterPosition.z);
            return Vector2.Distance(beforeXz, afterXz);
        }

        internal Vector3 ReadTargetBoneWorldForward(HumanBodyBones bone)
        {
            Transform targetBone = targetAnimator != null ? targetAnimator.GetBoneTransform(bone) : null;
            return targetBone != null && IsFinite(targetBone.forward) ? targetBone.forward : BuildNaNVector3();
        }

        internal Vector3 ReadTargetBoneWorldUp(HumanBodyBones bone)
        {
            Transform targetBone = targetAnimator != null ? targetAnimator.GetBoneTransform(bone) : null;
            return targetBone != null && IsFinite(targetBone.up) ? targetBone.up : BuildNaNVector3();
        }

        internal Transform FindTargetTransformByNameSuffix(string nameSuffix)
        {
            if (targetAnimator == null || targetAnimator.gameObject == null || string.IsNullOrEmpty(nameSuffix))
            {
                return null;
            }

            foreach (Transform candidate in targetAnimator.gameObject.GetComponentsInChildren<Transform>(true))
            {
                if (candidate != null && candidate.name.EndsWith(nameSuffix, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        internal Transform FindTargetTransform(Func<Transform, bool> predicate)
        {
            if (targetAnimator == null || targetAnimator.gameObject == null || predicate == null)
            {
                return null;
            }

            foreach (Transform candidate in targetAnimator.gameObject.GetComponentsInChildren<Transform>(true))
            {
                if (candidate != null && predicate(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        internal Transform FindThumbBaseHelperByName(bool leftHand)
        {
            string sideToken = leftHand ? "left" : "right";
            return FindTargetTransform(candidate =>
            {
                string normalizedName = NormalizeTransformName(candidate.name);
                return normalizedName.Contains(sideToken) &&
                    ThumbTransformNamePolicy.IsDetachedBaseHelper(normalizedName);
            });
        }

        internal static bool IsAncestorWithinHand(Transform ancestor, Transform descendant, Transform hand)
        {
            if (ancestor == null || descendant == null || hand == null || ancestor == descendant)
            {
                return false;
            }

            Transform current = descendant.parent;
            while (current != null)
            {
                if (current == ancestor)
                {
                    return true;
                }

                if (current == hand)
                {
                    break;
                }

                current = current.parent;
            }

            return false;
        }

        internal static string NormalizeTransformName(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.ToLowerInvariant();
        }
    }
}
