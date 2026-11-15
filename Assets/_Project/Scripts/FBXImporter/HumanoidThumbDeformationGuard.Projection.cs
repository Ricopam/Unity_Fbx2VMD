using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidThumbDeformationGuard
    {
        private int ProjectThumbProximalIntoPalmFrame(Transform proximal, Transform intermediate, bool isRightThumb)
        {
            if (!TryBuildPalmFrame(isRightThumb, out Vector3 sideAxis, out Vector3 palmNormal, out Vector3 forwardAxis))
            {
                return 0;
            }

            if (!TryResolveProjectedDirection(
                    proximal, intermediate, isRightThumb,
                    sideAxis, palmNormal, forwardAxis,
                    out ThumbProjectionLimits limits,
                    out Vector3 direction,
                    out Vector3 targetDirection))
            {
                return 0;
            }

            // 방향 차이만 proximal 월드 회전에 얹고, 본 길이나 위치는 직접 바꾸지 않습니다.
            return ApplyProjectionCorrection(
                proximal, direction, targetDirection, limits, palmNormal, isRightThumb);
        }

        private int ApplyProjectionCorrection(
            Transform proximal, Vector3 direction, Vector3 targetDirection,
            ThumbProjectionLimits limits, Vector3 palmNormal, bool isRightThumb)
        {
            bool bypassManualProjectionPreserve =
                limits.UseHighRiskManualOverride &&
                ShouldBypassManualThumbProjectionPreserveForSmokeRisk(
                    Vector3.Dot(direction, palmNormal),
                    limits.MinNormal,
                    limits.MaxNormal);
            Quaternion correctedWorldRotation = Quaternion.FromToRotation(direction, targetDirection) * proximal.rotation;
            HumanBodyBones proximalBone = isRightThumb ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal;
            if (!bypassManualProjectionPreserve &&
                ShouldPreserveManualThumbWorldRotationCorrection(proximalBone, proximal, correctedWorldRotation))
            {
                RecordThumbProjectionCorrection(isRightThumb, true);
                return 0;
            }
            ApplyWorldRotationCorrection(proximal, correctedWorldRotation);
            RecordThumbProjectionCorrection(isRightThumb, false);
            return 1;
        }

        private bool TryResolveThumbDirections(
            Transform proximal,
            Transform intermediate,
            bool isRightThumb,
            out Vector3 direction,
            out Vector3 indexDirection,
            out bool hasIndexDirection)
        {
            Transform hand = targetAnimator.GetBoneTransform(isRightThumb ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            Transform index = targetAnimator.GetBoneTransform(isRightThumb ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
            direction = intermediate.position - proximal.position;
            if (!TryNormalize(direction, out direction))
            {
                indexDirection = Vector3.zero;
                hasIndexDirection = false;
                return false;
            }

            indexDirection = Vector3.zero;
            hasIndexDirection =
                hand != null &&
                index != null &&
                TryNormalize(index.position - hand.position, out indexDirection);
            return true;
        }

        private struct ThumbProjectionLimits
        {
            internal float MinNormal;
            internal float MaxNormal;
            internal float MaxSpreadAngle;
            internal bool UseHighRiskManualOverride;
            internal float IndexSpreadGuardWeight;
            internal float ProjectionGuardWeight;
        }

        private ThumbProjectionLimits ResolveProjectionLimits(bool isRightThumb)
        {
            var limits = new ThumbProjectionLimits
            {
                MinNormal = Mathf.Clamp01(thumbProjectionMinPalmNormal),
                IndexSpreadGuardWeight = GetEffectiveThumbIndexSpreadGuardWeight(isRightThumb),
                ProjectionGuardWeight = GetEffectiveThumbProjectionGuardWeight(isRightThumb)
            };
            limits.MaxNormal = Mathf.Clamp(Mathf.Max(thumbProjectionMaxPalmNormal, limits.MinNormal), 0f, 1f);
            float configuredMaxSpreadAngle = Mathf.Clamp(thumbIndexMaxSpreadAngle, 0f, 90f);
            limits.MaxSpreadAngle = configuredMaxSpreadAngle;
            ResolveLinkedPoseSpaceRetargeter();
            // 수동 기준 포즈에서 위험한 엄지 자세로 판정되면 이 메서드의 제한값을 더 강하게 적용합니다.
            if (linkedPoseSpaceRetargeter != null &&
                linkedPoseSpaceRetargeter.TryGetHighRiskManualThumbPoseConstraintOverrides(
                    !isRightThumb,
                    out float overrideMinNormal,
                    out float overrideMaxNormal,
                    out float overrideMaxSpreadAngle))
            {
                limits.UseHighRiskManualOverride = true;
                limits.MinNormal = Mathf.Clamp01(overrideMinNormal);
                limits.MaxNormal = Mathf.Clamp(Mathf.Max(overrideMaxNormal, limits.MinNormal), 0f, 1f);
                limits.MaxSpreadAngle = ResolveManualOverrideMaxSpreadAngle(configuredMaxSpreadAngle, overrideMaxSpreadAngle);
                limits.IndexSpreadGuardWeight = Mathf.Max(limits.IndexSpreadGuardWeight, 1f);
                limits.ProjectionGuardWeight = Mathf.Max(limits.ProjectionGuardWeight, 1f);
            }
            return limits;
        }

        private static float ResolveManualOverrideMaxSpreadAngle(float configuredMaxSpreadAngle, float overrideMaxSpreadAngle)
        {
            float configured = Mathf.Clamp(configuredMaxSpreadAngle, 0f, 90f);
            float overrideValue = Mathf.Clamp(overrideMaxSpreadAngle, 0f, 90f);
            return Mathf.Min(configured, overrideValue);
        }

        private static bool ShouldBypassManualThumbProjectionPreserveForSmokeRisk(
            float currentProjection,
            float minNormal,
            float maxNormal)
        {
            float minValue = Mathf.Clamp01(minNormal);
            float maxValue = Mathf.Clamp(Mathf.Max(maxNormal, minValue), 0f, 1f);
            float risk = ThumbPoseRiskCalculator.CalculateOutsideRange(
                currentProjection,
                minValue,
                maxValue,
                1f);
            return IsFinite(risk) && risk > ManualThumbProjectionPreserveSmokeRiskLimit;
        }

        private bool TryResolveProjectedDirection(
            Transform proximal, Transform intermediate, bool isRightThumb,
            Vector3 sideAxis, Vector3 palmNormal, Vector3 forwardAxis,
            out ThumbProjectionLimits limits,
            out Vector3 direction,
            out Vector3 targetDirection)
        {
            limits = ResolveProjectionLimits(isRightThumb);
            targetDirection = Vector3.zero;
            if (!TryResolveThumbDirections(
                    proximal, intermediate, isRightThumb,
                    out direction, out Vector3 indexDirection, out bool hasIndexDirection))
            {
                return false;
            }
            return ThumbPoseDirectionCalculator.TryCalculateCorrectedDirection(
                direction,
                indexDirection,
                hasIndexDirection,
                sideAxis,
                palmNormal,
                forwardAxis,
                limits.MinNormal,
                limits.MaxNormal,
                limits.MaxSpreadAngle,
                limits.IndexSpreadGuardWeight,
                limits.ProjectionGuardWeight,
                out targetDirection);
        }

        private int StraightenThumbSegmentBend(Transform proximal, Transform intermediate, Transform distal, bool isRightThumb)
        {
            if (!TryResolveStraightenedDirection(
                    proximal, intermediate, distal, isRightThumb,
                    out Vector3 intermediateDirection,
                    out Vector3 targetDirection))
            {
                return 0;
            }

            Quaternion correctedWorldRotation = Quaternion.FromToRotation(intermediateDirection, targetDirection) * intermediate.rotation;
            HumanBodyBones intermediateBone = isRightThumb ? HumanBodyBones.RightThumbIntermediate : HumanBodyBones.LeftThumbIntermediate;
            if (ShouldPreserveManualThumbWorldRotationCorrection(intermediateBone, intermediate, correctedWorldRotation))
            {
                RecordThumbSegmentStraightenCorrection(isRightThumb, true);
                return 0;
            }

            ApplyWorldRotationCorrection(intermediate, correctedWorldRotation);
            RecordThumbSegmentStraightenCorrection(isRightThumb, false);
            return 1;
        }

        private bool ShouldPreserveManualThumbWorldRotationCorrection(
            HumanBodyBones thumbBone,
            Transform targetTransform,
            Quaternion correctedWorldRotation)
        {
            ResolveLinkedPoseSpaceRetargeter();
            return linkedPoseSpaceRetargeter != null &&
                linkedPoseSpaceRetargeter.ShouldPreserveManualThumbWorldRotationCorrection(
                    thumbBone,
                    targetTransform,
                    correctedWorldRotation);
        }

        private bool TryResolveStraightenedDirection(
            Transform proximal, Transform intermediate, Transform distal,
            bool isRightThumb,
            out Vector3 intermediateDirection,
            out Vector3 targetDirection)
        {
            intermediateDirection = Vector3.zero;
            targetDirection = Vector3.zero;
            float segmentStraightenWeight = GetEffectiveThumbSegmentStraightenWeight(isRightThumb);
            if (segmentStraightenWeight <= 0f || thumbMaxSegmentBendAngle >= 59.999f)
            {
                return false;
            }
            Vector3 proximalDirection = intermediate.position - proximal.position;
            intermediateDirection = distal.position - intermediate.position;
            if (!TryNormalize(proximalDirection, out proximalDirection) ||
                !TryNormalize(intermediateDirection, out intermediateDirection))
            {
                return false;
            }
            return ThumbPoseDirectionCalculator.TryCalculateStraightenedDirection(
                proximalDirection,
                intermediateDirection,
                thumbMaxSegmentBendAngle,
                segmentStraightenWeight,
                out targetDirection);
        }

        private bool TryCalculateThumbAndIndexDirections(
            bool isRightThumb,
            out Vector3 thumbDirection,
            out Vector3 indexDirection)
        {
            thumbDirection = Vector3.zero;
            indexDirection = Vector3.zero;
            ResolveHandChainBones(
                isRightThumb,
                out Transform hand,
                out Transform thumbProximal,
                out Transform thumbIntermediate,
                out Transform indexProximal,
                out Transform indexIntermediate);

            if (thumbProximal != null && thumbIntermediate != null)
            {
                thumbDirection = thumbIntermediate.position - thumbProximal.position;
            }
            else if (hand != null && thumbProximal != null)
            {
                thumbDirection = thumbProximal.position - hand.position;
            }

            if (hand != null && indexProximal != null)
            {
                indexDirection = indexProximal.position - hand.position;
            }
            else if (indexProximal != null && indexIntermediate != null)
            {
                indexDirection = indexIntermediate.position - indexProximal.position;
            }

            return TryNormalize(thumbDirection, out thumbDirection) &&
                TryNormalize(indexDirection, out indexDirection);
        }

        private void ResolveHandChainBones(
            bool isRightThumb,
            out Transform hand,
            out Transform thumbProximal,
            out Transform thumbIntermediate,
            out Transform indexProximal,
            out Transform indexIntermediate)
        {
            hand = targetAnimator.GetBoneTransform(isRightThumb ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            thumbProximal = targetAnimator.GetBoneTransform(
                isRightThumb ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal);
            thumbIntermediate = targetAnimator.GetBoneTransform(
                isRightThumb ? HumanBodyBones.RightThumbIntermediate : HumanBodyBones.LeftThumbIntermediate);
            indexProximal = targetAnimator.GetBoneTransform(
                isRightThumb ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
            indexIntermediate = targetAnimator.GetBoneTransform(
                isRightThumb ? HumanBodyBones.RightIndexIntermediate : HumanBodyBones.LeftIndexIntermediate);
        }

        private bool TryBuildPalmFrame(bool isRightThumb, out Vector3 sideAxis, out Vector3 palmNormal, out Vector3 forwardAxis)
        {
            sideAxis = Vector3.zero;
            palmNormal = Vector3.zero;
            forwardAxis = Vector3.zero;

            Transform hand = targetAnimator.GetBoneTransform(isRightThumb ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            Transform index = targetAnimator.GetBoneTransform(isRightThumb ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
            Transform middle = targetAnimator.GetBoneTransform(isRightThumb ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
            Transform little = targetAnimator.GetBoneTransform(isRightThumb ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
            if (hand == null || index == null || middle == null || little == null)
            {
                return false;
            }

            Vector3 rawSide = index.position - little.position;
            if (isRightThumb)
            {
                rawSide = -rawSide;
            }

            Vector3 rawForward = ((index.position + middle.position + little.position) / 3f) - hand.position;
            if (!TryNormalize(rawSide, out sideAxis) ||
                !TryNormalize(rawForward, out forwardAxis) ||
                !TryNormalize(Vector3.Cross(sideAxis, forwardAxis), out palmNormal) ||
                !TryNormalize(Vector3.Cross(palmNormal, sideAxis), out forwardAxis))
            {
                return false;
            }

            return true;
        }

        private void ApplyWorldRotationCorrection(Transform targetTransform, Quaternion correctedWorldRotation)
        {
            Quaternion rawLocalRotation = _lastRawLocalRotations.TryGetValue(targetTransform, out Quaternion lastRawRotation)
                ? lastRawRotation
                : targetTransform.localRotation;

            targetTransform.rotation = correctedWorldRotation;
            _lastRawLocalRotations[targetTransform] = rawLocalRotation;
            _lastCorrectedLocalRotations[targetTransform] = targetTransform.localRotation;
        }
    }
}
