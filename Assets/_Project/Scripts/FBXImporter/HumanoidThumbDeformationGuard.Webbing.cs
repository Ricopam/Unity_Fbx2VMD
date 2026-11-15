using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidThumbDeformationGuard
    {
        private const float ThumbWebbingSpreadFullRiskAngle = 72f;
        private const float ThumbWebbingProjectionFullRiskDistance = 1f;
        private const float ThumbWebbingHelperDistanceWarning = 0.003f;
        private const float ThumbWebbingHelperDistanceFullRisk = 0.008f;
        private const float ThumbWebbingHelperRotationWarning = 18f;
        private const float ThumbWebbingHelperRotationFullRisk = 45f;
        private const float ThumbWebbingDynamicMinLocalAngle = 4f;
        private const float ThumbWebbingDynamicMinPositionOffset = 0.0015f;

        private void GetEffectiveThumbWebbingCorrectiveSettings(
            Transform helperTransform,
            Transform sourceTransform,
            Quaternion targetLocalRotation,
            Vector3 targetLocalPosition,
            out float weight,
            out float maxLocalAngle,
            out float maxPositionOffset)
        {
            float poseRisk = CalculateThumbWebbingPoseRisk(
                helperTransform,
                sourceTransform,
                targetLocalRotation,
                targetLocalPosition);
            ThumbWebbingCorrectionCalculator.CalculateEffectiveSettings(
                thumbWebbingCreaseStabilizeWeight,
                thumbWebbingCreaseMaxLocalAngle,
                thumbWebbingCreaseMaxPositionOffset,
                detachedThumbBaseHelperMaxLocalAngle,
                detachedThumbBaseHelperMaxPositionOffset,
                poseRisk,
                ThumbWebbingDynamicMinLocalAngle,
                ThumbWebbingDynamicMinPositionOffset,
                out weight,
                out maxLocalAngle,
                out maxPositionOffset);
        }

        private float CalculateThumbWebbingPoseRisk(
            Transform helperTransform,
            Transform sourceTransform,
            Quaternion targetLocalRotation,
            Vector3 targetLocalPosition)
        {
            float spreadRisk = float.NaN;
            float projectionRisk = float.NaN;
            float helperDistanceRisk = CalculateThumbWebbingHelperDistanceRisk(helperTransform, sourceTransform, targetLocalPosition);
            float helperRotationRisk = CalculateThumbWebbingHelperRotationRisk(helperTransform, sourceTransform, targetLocalRotation);
            Transform thumbSideReference = helperTransform != null ? helperTransform : sourceTransform;
            if (TryResolveThumbSide(thumbSideReference, out bool isRightThumb) &&
                TryCalculateThumbAndIndexDirections(isRightThumb, out Vector3 thumbDirection, out Vector3 indexDirection))
            {
                spreadRisk = ThumbPoseRiskCalculator.CalculateAboveThreshold(
                    Vector3.Angle(thumbDirection, indexDirection),
                    Mathf.Clamp(thumbIndexMaxSpreadAngle, 0f, 90f),
                    ThumbWebbingSpreadFullRiskAngle);
                if (TryBuildPalmFrame(isRightThumb, out _, out Vector3 palmNormal, out _))
                {
                    projectionRisk = ThumbPoseRiskCalculator.CalculateOutsideRange(
                        Vector3.Dot(thumbDirection, palmNormal),
                        Mathf.Clamp01(thumbProjectionMinPalmNormal),
                        Mathf.Clamp(Mathf.Max(thumbProjectionMaxPalmNormal, thumbProjectionMinPalmNormal), 0f, 1f),
                        ThumbWebbingProjectionFullRiskDistance);
                }
            }
            return ThumbPoseRiskCalculator.FindMaximumFinite( spreadRisk, projectionRisk, helperDistanceRisk, helperRotationRisk); }

        private float CalculateThumbWebbingHelperDistanceRisk(
            Transform helperTransform,
            Transform sourceTransform,
            Vector3 targetLocalPosition)
        {
            if (helperTransform == null ||
                sourceTransform == null ||
                !_detachedThumbBaseInitialDistances.TryGetValue(helperTransform, out float initialDistance))
            {
                return float.NaN;
            }

            Vector3 helperWorldPosition = helperTransform.parent != null
                ? helperTransform.parent.TransformPoint(targetLocalPosition)
                : targetLocalPosition;
            float distanceDelta = Mathf.Abs(Vector3.Distance(helperWorldPosition, sourceTransform.position) - initialDistance);
            return ThumbPoseRiskCalculator.CalculateAboveThreshold(
                distanceDelta,
                ThumbWebbingHelperDistanceWarning,
                ThumbWebbingHelperDistanceFullRisk);
        }

        private float CalculateThumbWebbingHelperRotationRisk(
            Transform helperTransform,
            Transform sourceTransform,
            Quaternion targetLocalRotation)
        {
            if (helperTransform == null ||
                sourceTransform == null ||
                !_detachedThumbBaseInitialRelativeRotations.TryGetValue(helperTransform, out Quaternion initialRelativeRotation))
            {
                return float.NaN;
            }

            Quaternion helperWorldRotation = helperTransform.parent != null
                ? helperTransform.parent.rotation * targetLocalRotation
                : targetLocalRotation;
            Quaternion currentRelativeRotation = Quaternion.Inverse(sourceTransform.rotation) * helperWorldRotation;
            float rotationDelta = Quaternion.Angle(initialRelativeRotation, currentRelativeRotation);
            return ThumbPoseRiskCalculator.CalculateAboveThreshold(
                rotationDelta,
                ThumbWebbingHelperRotationWarning,
                ThumbWebbingHelperRotationFullRisk);
        }
    }
}
