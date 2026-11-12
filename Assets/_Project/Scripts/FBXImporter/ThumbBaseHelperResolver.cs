using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 썸 베이스 헬퍼·명시 소스 transform 탐색과 초기 관계 기준선 기록을 담당하는 런타임 해석기.
    /// 탐색 결과 캐시와 초기 거리·회전 사전은 PSR 소유이므로 owner 경유로 기록한다.
    /// </summary>
    internal static class ThumbBaseHelperResolver
    {
        internal static void CaptureState(PoseSpaceRetargeter owner, bool leftHand)
        {
            if (!TryFindHelperCandidate(owner, leftHand, out Transform helperTransform))
            {
                return;
            }

            owner._cachedThumbBaseHelpers[leftHand] = helperTransform;
            if (!owner._targetInitialThumbBaseHelperLocalPositions.ContainsKey(helperTransform))
            {
                owner._targetInitialThumbBaseHelperLocalPositions[helperTransform] = helperTransform.localPosition;
            }

            if (!TryFindExplicitSource(owner, leftHand, out Transform sourceTransform))
            {
                return;
            }

            owner._cachedThumbBaseExplicitSources[leftHand] = sourceTransform;
            CaptureRelationshipBaseline(owner, leftHand, helperTransform, sourceTransform);
        }

        internal static void CaptureRelationshipBaseline(PoseSpaceRetargeter owner, bool leftHand, Transform helperTransform, Transform sourceTransform)
        {
            if (helperTransform == null || sourceTransform == null)
            {
                return;
            }

            float initialDistance = Vector3.Distance(helperTransform.position, sourceTransform.position);
            if (PoseSpaceRetargeter.IsFinite(initialDistance))
            {
                owner._initialThumbBaseHelperSourceDistances[leftHand] = initialDistance;
            }

            Quaternion initialRelativeRotation = Quaternion.Inverse(sourceTransform.rotation) * helperTransform.rotation;
            if (PoseSpaceRetargeter.IsFinite(initialRelativeRotation))
            {
                owner._initialThumbBaseHelperSourceRelativeRotations[leftHand] = initialRelativeRotation;
            }
        }

        internal static bool TryFindHelperCandidate(PoseSpaceRetargeter owner, bool leftHand, out Transform helper)
        {
            helper = owner.FindThumbBaseHelperByName(leftHand);
            if (helper != null)
            {
                return true;
            }

            Animator targetAnimator = owner.targetAnimator;
            Transform hand = targetAnimator != null
                ? targetAnimator.GetBoneTransform(leftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand)
                : null;
            if (hand == null)
            {
                return false;
            }

            Transform thumbProximal = targetAnimator.GetBoneTransform(leftHand ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
            Transform thumbIntermediate = targetAnimator.GetBoneTransform(leftHand ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate);
            Transform thumbDistal = targetAnimator.GetBoneTransform(leftHand ? HumanBodyBones.LeftThumbDistal : HumanBodyBones.RightThumbDistal);
            Transform explicitSource = null;
            TryFindExplicitSource(owner, leftHand, out explicitSource);

            helper = SelectNearestThumbCandidate(hand, thumbProximal, thumbIntermediate, thumbDistal, explicitSource);
            return helper != null;
        }

        private static Transform SelectNearestThumbCandidate(
            Transform hand,
            Transform thumbProximal,
            Transform thumbIntermediate,
            Transform thumbDistal,
            Transform explicitSource)
        {
            Transform best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (Transform candidate in hand.GetComponentsInChildren<Transform>(true))
            {
                if (!IsAmbiguousExtraTransformCandidate(candidate, hand, thumbProximal, thumbIntermediate, thumbDistal))
                {
                    continue;
                }

                float distance = HelperCandidateDistance(candidate, hand, thumbProximal, explicitSource);
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                best = candidate;
            }

            return best;
        }

        private static float HelperCandidateDistance(Transform candidate, Transform hand, Transform thumbProximal, Transform explicitSource)
        {
            return explicitSource != null
                ? (candidate.position - explicitSource.position).sqrMagnitude
                : thumbProximal != null
                    ? (candidate.position - thumbProximal.position).sqrMagnitude
                    : (candidate.position - hand.position).sqrMagnitude;
        }

        internal static bool TryFindExplicitSource(PoseSpaceRetargeter owner, bool leftHand, out Transform source)
        {
            string sideToken = leftHand ? "left" : "right";
            source = owner.FindTargetTransform(candidate =>
            {
                string normalizedName = PoseSpaceRetargeter.NormalizeTransformName(candidate.name);
                return normalizedName.Contains(sideToken) &&
                    ThumbTransformNamePolicy.IsActiveBaseSource(normalizedName);
            });
            if (source != null)
            {
                return true;
            }

            Animator targetAnimator = owner.targetAnimator;
            Transform hand = targetAnimator != null
                ? targetAnimator.GetBoneTransform(leftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand)
                : null;
            Transform thumbProximal = targetAnimator != null
                ? targetAnimator.GetBoneTransform(leftHand ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal)
                : null;
            if (hand == null)
            {
                return false;
            }

            source = FindNearestActiveBaseSourceByName(targetAnimator, hand, thumbProximal);
            return source != null;
        }

        private static Transform FindNearestActiveBaseSourceByName(Animator targetAnimator, Transform hand, Transform thumbProximal)
        {
            Transform best = null;
            float bestDistance = float.PositiveInfinity;
            foreach (Transform candidate in targetAnimator.gameObject.GetComponentsInChildren<Transform>(true))
            {
                if (candidate == null)
                {
                    continue;
                }

                string normalizedName = PoseSpaceRetargeter.NormalizeTransformName(candidate.name);
                if (!ThumbTransformNamePolicy.IsActiveBaseSource(normalizedName))
                {
                    continue;
                }

                float distance = SourceCandidateDistance(candidate, hand, thumbProximal);
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                best = candidate;
            }

            return best;
        }

        private static float SourceCandidateDistance(Transform candidate, Transform hand, Transform thumbProximal)
        {
            return thumbProximal != null
                ? (candidate.position - thumbProximal.position).sqrMagnitude
                : (candidate.position - hand.position).sqrMagnitude;
        }

        private static bool IsAmbiguousExtraTransformCandidate(
            Transform candidate,
            Transform hand,
            Transform thumbProximal,
            Transform thumbIntermediate,
            Transform thumbDistal)
        {
            if (candidate == null || candidate == hand || candidate == thumbProximal || candidate == thumbIntermediate || candidate == thumbDistal)
            {
                return false;
            }

            string normalizedName = PoseSpaceRetargeter.NormalizeTransformName(candidate.name);
            if (HasDisqualifyingThumbName(normalizedName))
            {
                return false;
            }

            if (IsRelatedToThumbChain(candidate, hand, thumbProximal, thumbIntermediate, thumbDistal))
            {
                return false;
            }

            return true;
        }

        private static bool HasDisqualifyingThumbName(string normalizedName)
        {
            if (string.IsNullOrEmpty(normalizedName) ||
                !normalizedName.Contains("thumb") ||
                normalizedName.Contains("ghost") ||
                ThumbTransformNamePolicy.IsActiveBaseSource(normalizedName))
            {
                return true;
            }

            return normalizedName.Contains("thumb1") ||
                normalizedName.Contains("thumb2") ||
                normalizedName.Contains("thumb3") ||
                normalizedName.Contains("proximal") ||
                normalizedName.Contains("intermediate") ||
                normalizedName.Contains("distal") ||
                normalizedName.Contains("thumbtip");
        }

        internal static bool HasDetachedRelationship(PoseSpaceRetargeter owner, HumanBodyBones thumbBone, bool leftHand)
        {
            if (thumbBone != HumanBodyBones.LeftThumbProximal &&
                thumbBone != HumanBodyBones.RightThumbProximal)
            {
                return false;
            }

            return owner.GetCachedThumbBaseHelper(leftHand) != null &&
                owner.GetCachedExplicitThumbBaseSource(leftHand) != null;
        }

        private static bool IsRelatedToThumbChain(
            Transform candidate,
            Transform hand,
            Transform thumbProximal,
            Transform thumbIntermediate,
            Transform thumbDistal)
        {
            return PoseSpaceRetargeter.IsAncestorWithinHand(candidate, thumbProximal, hand) ||
                PoseSpaceRetargeter.IsAncestorWithinHand(candidate, thumbIntermediate, hand) ||
                PoseSpaceRetargeter.IsAncestorWithinHand(candidate, thumbDistal, hand) ||
                PoseSpaceRetargeter.IsAncestorWithinHand(thumbProximal, candidate, hand) ||
                PoseSpaceRetargeter.IsAncestorWithinHand(thumbIntermediate, candidate, hand) ||
                PoseSpaceRetargeter.IsAncestorWithinHand(thumbDistal, candidate, hand);
        }
    }
}
