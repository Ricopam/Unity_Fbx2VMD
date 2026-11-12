using System;
using System.Collections.Generic;
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

        private void CaptureTargetInitialTransforms(GameObject targetRoot)
        {
            ResetTargetCaptureState();

            foreach (Transform targetBone in targetRoot.GetComponentsInChildren<Transform>(true))
            {
                _targetInitialScales[targetBone] = targetBone.localScale;
            }

            if (targetAnimator == null || !targetAnimator.isHuman)
            {
                return;
            }

            CaptureTargetHumanoidLocalPositions();
            CaptureTargetThumbLocalRotations();
            ThumbBaseHelperResolver.CaptureState(this, true);
            ThumbBaseHelperResolver.CaptureState(this, false);
        }

        private void ResetTargetCaptureState()
        {
            _targetInitialScales.Clear();
            _targetInitialHumanoidLocalPositions.Clear();
            _targetInitialThumbBaseHelperLocalPositions.Clear();
            _targetInitialThumbLocalRotations.Clear();
            _cachedThumbBaseHelpers.Clear();
            _cachedThumbBaseExplicitSources.Clear();
            _initialThumbBaseHelperSourceDistances.Clear();
            _initialThumbBaseHelperSourceRelativeRotations.Clear();
            _scaleWarningLogged = false;
            _positionWarningLogged = false;
            _muscleClampWarningLogged = false;
            _anatomyGuardWarningLogged = false;
            _thumbGuardWarningLogged = false;
            _thumbLocalRotationGuardWarningLogged = false;
            _rootDeltaSpikeWarningLogged = false;
            _hasPreviousBodyRootMotionPosition = false;
            _spikeClamp.ResetHipsSpikeState();
            _lastRootDeltaMagnitude = float.NaN;
            _maxRootDeltaMagnitude = 0f;
            _rootDeltaSpikeSkippedCount = 0;
            _spikeClamp.ResetRootPositionMetrics();
            _legacyAnimationDriver.ResetStabilityMetrics();
            _visualPoseSmoother.ResetSession();
            _grounding.ResetSession();
            _lastEditorFootHeightGroundingReferenceLift = float.NaN;
            _appliedPoseClampWarningLogged = false;
            _targetHipsRestLocalPosition = Vector3.zero;
            _hasTargetHipsRestLocalPosition = false;
        }

        private void CaptureTargetHumanoidLocalPositions()
        {
            for (int i = (int)HumanBodyBones.Hips; i < (int)HumanBodyBones.LastBone; i++)
            {
                HumanBodyBones bone = (HumanBodyBones)i;
                Transform targetBone = targetAnimator.GetBoneTransform(bone);
                if (bone == HumanBodyBones.Hips)
                {
                    if (targetBone != null && IsFinite(targetBone.localPosition))
                    {
                        _targetHipsRestLocalPosition = targetBone.localPosition;
                        _hasTargetHipsRestLocalPosition = true;
                    }

                    continue;
                }

                if (targetBone == null || _targetInitialHumanoidLocalPositions.ContainsKey(targetBone))
                {
                    continue;
                }

                _targetInitialHumanoidLocalPositions[targetBone] = targetBone.localPosition;
            }
        }

        private void CaptureTargetThumbLocalRotations()
        {
            foreach (HumanBodyBones thumbBone in ThumbRotationBones)
            {
                Transform thumbTransform = targetAnimator.GetBoneTransform(thumbBone);
                if (thumbTransform == null || _targetInitialThumbLocalRotations.ContainsKey(thumbTransform))
                {
                    continue;
                }

                _targetInitialThumbLocalRotations[thumbTransform] = thumbTransform.localRotation;
            }
        }

        internal static string GetHierarchyPath(Transform transform)
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

        private void RestoreTargetHumanoidLocalPositions()
        {
            if (!ShouldLockTargetHumanoidBonePositions)
            {
                return;
            }

            foreach (KeyValuePair<Transform, Vector3> positionSnapshot in _targetInitialHumanoidLocalPositions)
            {
                Transform targetBone = positionSnapshot.Key;
                if (targetBone == null)
                {
                    continue;
                }

                if ((targetBone.localPosition - positionSnapshot.Value).sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                if (!_positionWarningLogged)
                {
                    Debug.LogWarning($"[PoseSpaceRetargeter] Target humanoid bone localPosition changed during retargeting. Restoring original bone length. First bone: {targetBone.name}");
                    _positionWarningLogged = true;
                }

                targetBone.localPosition = positionSnapshot.Value;
            }
        }

        private void RestoreTargetLocalScales()
        {
            foreach (KeyValuePair<Transform, Vector3> scaleSnapshot in _targetInitialScales)
            {
                Transform targetBone = scaleSnapshot.Key;
                if (targetBone == null)
                {
                    continue;
                }

                if ((targetBone.localScale - scaleSnapshot.Value).sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                if (!_scaleWarningLogged)
                {
                    Debug.LogWarning($"[PoseSpaceRetargeter] Target bone scale changed during retargeting. Restoring original localScale. First bone: {targetBone.name}");
                    _scaleWarningLogged = true;
                }

                targetBone.localScale = scaleSnapshot.Value;
            }
        }
    }
}
