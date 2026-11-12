using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 썸 수동 레퍼런스의 에디터 훅 래퍼를 담당하는 partial 분할.
    public partial class PoseSpaceRetargeter
    {
        internal bool ShouldSuppressCompetingManualThumbOverride(bool leftHand)
        {
#if UNITY_EDITOR
            return RetargetingEditorReferenceHooks.ThumbReference?.ShouldSuppressCompetingManualThumbOverride(this, leftHand) ?? false;
#else
            return false;
#endif
        }

        internal bool ShouldKeepDetachedHelperManualThumbOverrides(bool leftHand)
        {
#if UNITY_EDITOR
            return RetargetingEditorReferenceHooks.ThumbReference?.ShouldKeepDetachedHelperManualThumbOverrides(this, leftHand) ?? false;
#else
            return false;
#endif
        }

        public bool TryGetHighRiskManualThumbPoseConstraintOverrides(
            bool leftHand,
            out float projectionMin,
            out float projectionMax,
            out float maxSpreadAngle)
        {
#if UNITY_EDITOR
            projectionMin = 0f;
            projectionMax = 0f;
            maxSpreadAngle = 0f;
            return RetargetingEditorReferenceHooks.ThumbReference?.TryGetHighRiskManualThumbPoseConstraintOverrides(
                this,
                leftHand,
                out projectionMin,
                out projectionMax,
                out maxSpreadAngle) ?? false;
#else
            projectionMin = 0f;
            projectionMax = 0f;
            maxSpreadAngle = 0f;
            return false;
#endif
        }

        public string BuildThumbHelperRelationshipDebugSummary(bool leftHand)
        {
#if UNITY_EDITOR
            return RetargetingEditorReferenceHooks.ThumbReference?.BuildThumbHelperRelationshipDebugSummary(this, leftHand);
#else
            return leftHand
                ? "side=L, state=editor-only"
                : "side=R, state=editor-only";
#endif
        }

        internal Transform GetCachedThumbBaseHelper(bool leftHand)
        {
#if UNITY_EDITOR
            return RetargetingEditorReferenceHooks.ThumbReference?.GetCachedThumbBaseHelper(this, leftHand);
#else
            return null;
#endif
        }

        internal Transform GetCachedExplicitThumbBaseSource(bool leftHand)
        {
#if UNITY_EDITOR
            return RetargetingEditorReferenceHooks.ThumbReference?.GetCachedExplicitThumbBaseSource(this, leftHand);
#else
            return null;
#endif
        }

        internal bool TryEvaluateThumbManualReferenceFrameDeviation(
            bool leftHand,
            Transform targetThumb,
            Quaternion candidateWorldRotation,
            out float currentDeviation,
            out float candidateDeviation)
        {
#if UNITY_EDITOR
            currentDeviation = float.NaN;
            candidateDeviation = float.NaN;
            return RetargetingEditorReferenceHooks.ThumbReference?.TryEvaluateManualReferenceFrameDeviation(
                this,
                leftHand,
                targetThumb,
                candidateWorldRotation,
                out currentDeviation,
                out candidateDeviation) ?? false;
#else
            currentDeviation = float.NaN;
            candidateDeviation = float.NaN;
            return false;
#endif
        }

        internal bool TryEvaluateThumbLocalRotationOverrideRisk(
            bool leftHand,
            Transform targetThumb,
            Quaternion candidateRotation,
            out float risk)
        {
#if UNITY_EDITOR
            risk = float.NaN;
            return RetargetingEditorReferenceHooks.ThumbReference?.TryEvaluateThumbLocalRotationOverrideRisk(this, leftHand, targetThumb, candidateRotation, out risk) ?? false;
#else
            risk = float.NaN;
            return false;
#endif
        }
    }
}
