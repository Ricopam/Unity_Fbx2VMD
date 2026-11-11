using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 썸/팜 참조군(로컬회전·베이스위치·세그먼트/핸드 방향·팜프레임)과 헬퍼 관계·위험 평가자의
    /// 에디터 구현 계약 — 적용 파이프라인 5진입점 + 평가/조회 10진입점.
    /// </summary>
    internal interface IEditorThumbReferenceDriver
    {
        void ApplyThumbLocalRotationReference(PoseSpaceRetargeter owner);

        void ApplyThumbBasePositionReference(PoseSpaceRetargeter owner);

        void ApplyThumbSegmentDirectionReference(PoseSpaceRetargeter owner);

        void ApplyThumbHandDirectionReference(PoseSpaceRetargeter owner);

        void ApplyHandPalmFrameReference(PoseSpaceRetargeter owner);

        bool ShouldSuppressCompetingManualThumbOverride(PoseSpaceRetargeter owner, bool leftHand);

        bool ShouldKeepDetachedHelperManualThumbOverrides(PoseSpaceRetargeter owner, bool leftHand);

        bool TryGetHighRiskManualThumbPoseConstraintOverrides(
            PoseSpaceRetargeter owner,
            bool leftHand,
            out float projectionMin,
            out float projectionMax,
            out float maxSpreadAngle);

        string BuildThumbHelperRelationshipDebugSummary(PoseSpaceRetargeter owner, bool leftHand);

        Transform GetCachedThumbBaseHelper(PoseSpaceRetargeter owner, bool leftHand);

        Transform GetCachedExplicitThumbBaseSource(PoseSpaceRetargeter owner, bool leftHand);

        bool TryEvaluateManualReferenceFrameDeviation(
            PoseSpaceRetargeter owner,
            bool leftHand,
            Transform targetThumb,
            Quaternion candidateWorldRotation,
            out float currentDeviation,
            out float candidateDeviation);

        bool TryEvaluateCurrentThumbReferenceFrameDelta(
            PoseSpaceRetargeter owner,
            bool leftHand,
            out float spreadDelta,
            out float projectionDelta);

        bool TryEvaluateThumbLocalRotationOverrideRisk(
            PoseSpaceRetargeter owner,
            bool leftHand,
            Transform targetThumb,
            Quaternion candidateRotation,
            out float risk);

        bool TryEvaluateManualOverrideRisk(PoseSpaceRetargeter owner, bool leftHand, out float risk);
    }
}
