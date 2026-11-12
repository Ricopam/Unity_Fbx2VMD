using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 썸 가드 경로가 계산한 값을 RetargetingThumbGuardDiagnostics에 좌우·본별로 분기해 기록하는 기록자.
    /// </summary>
    internal static class RetargetingThumbGuardDiagnosticsRecorder
    {
        internal static void RecordWorldRotationOverrideContext(
            RetargetingThumbGuardDiagnostics diagnostics,
            bool leftHand,
            bool suppressCompetingManualOverride,
            bool keepDetachedHelperOverride,
            float currentReferenceFrameDeviation,
            float candidateReferenceFrameDeviation)
        {
            if (leftHand)
            {
                diagnostics.LastLeftThumbWorldRotationSuppressCompetingOverride = suppressCompetingManualOverride;
                diagnostics.LastLeftThumbWorldRotationKeepDetachedHelperOverride = keepDetachedHelperOverride;
                diagnostics.LastLeftThumbWorldRotationCurrentReferenceFrameDeviation = currentReferenceFrameDeviation;
                diagnostics.LastLeftThumbWorldRotationCandidateReferenceFrameDeviation = candidateReferenceFrameDeviation;
            }
            else
            {
                diagnostics.LastRightThumbWorldRotationSuppressCompetingOverride = suppressCompetingManualOverride;
                diagnostics.LastRightThumbWorldRotationKeepDetachedHelperOverride = keepDetachedHelperOverride;
                diagnostics.LastRightThumbWorldRotationCurrentReferenceFrameDeviation = currentReferenceFrameDeviation;
                diagnostics.LastRightThumbWorldRotationCandidateReferenceFrameDeviation = candidateReferenceFrameDeviation;
            }
        }

        internal static void RecordWorldRotationReferenceAngles(
            RetargetingThumbGuardDiagnostics diagnostics,
            bool leftHand,
            bool proximalBone,
            float currentReferenceAngle,
            float candidateReferenceAngle)
        {
            if (leftHand)
            {
                if (proximalBone)
                {
                    diagnostics.LastLeftThumbProximalWorldRotationCurrentReferenceAngle = currentReferenceAngle;
                    diagnostics.LastLeftThumbProximalWorldRotationCandidateReferenceAngle = candidateReferenceAngle;
                }
                else
                {
                    diagnostics.LastLeftThumbIntermediateWorldRotationCurrentReferenceAngle = currentReferenceAngle;
                    diagnostics.LastLeftThumbIntermediateWorldRotationCandidateReferenceAngle = candidateReferenceAngle;
                }
            }
            else
            {
                if (proximalBone)
                {
                    diagnostics.LastRightThumbProximalWorldRotationCurrentReferenceAngle = currentReferenceAngle;
                    diagnostics.LastRightThumbProximalWorldRotationCandidateReferenceAngle = candidateReferenceAngle;
                }
                else
                {
                    diagnostics.LastRightThumbIntermediateWorldRotationCurrentReferenceAngle = currentReferenceAngle;
                    diagnostics.LastRightThumbIntermediateWorldRotationCandidateReferenceAngle = candidateReferenceAngle;
                }
            }
        }

        internal static void RecordWorldRotationPreserveLocalRisk(
            RetargetingThumbGuardDiagnostics diagnostics,
            bool leftHand,
            bool proximalBone,
            float currentRisk,
            float limitedRisk)
        {
            if (leftHand)
            {
                if (proximalBone)
                {
                    diagnostics.LastLeftThumbProximalWorldRotationPreserveCurrentRisk = currentRisk;
                    diagnostics.LastLeftThumbProximalWorldRotationPreserveLimitedRisk = limitedRisk;
                }
                else
                {
                    diagnostics.LastLeftThumbIntermediateWorldRotationPreserveCurrentRisk = currentRisk;
                    diagnostics.LastLeftThumbIntermediateWorldRotationPreserveLimitedRisk = limitedRisk;
                }
            }
            else
            {
                if (proximalBone)
                {
                    diagnostics.LastRightThumbProximalWorldRotationPreserveCurrentRisk = currentRisk;
                    diagnostics.LastRightThumbProximalWorldRotationPreserveLimitedRisk = limitedRisk;
                }
                else
                {
                    diagnostics.LastRightThumbIntermediateWorldRotationPreserveCurrentRisk = currentRisk;
                    diagnostics.LastRightThumbIntermediateWorldRotationPreserveLimitedRisk = limitedRisk;
                }
            }
        }

        internal static void RecordWorldRotationPreserveReason(
            RetargetingThumbGuardDiagnostics diagnostics,
            bool leftHand,
            bool proximalBone,
            ThumbWorldRotationPreserveReason reason)
        {
            if (leftHand)
            {
                if (proximalBone)
                {
                    diagnostics.LastLeftThumbProximalWorldRotationPreserveReason = reason;
                }
                else
                {
                    diagnostics.LastLeftThumbIntermediateWorldRotationPreserveReason = reason;
                }
            }
            else
            {
                if (proximalBone)
                {
                    diagnostics.LastRightThumbProximalWorldRotationPreserveReason = reason;
                }
                else
                {
                    diagnostics.LastRightThumbIntermediateWorldRotationPreserveReason = reason;
                }
            }
        }

        internal static void RecordLocalRotationGuardClamp(
            RetargetingThumbGuardDiagnostics diagnostics,
            HumanBodyBones thumbBone)
        {
            if (!PoseSpaceRetargeter.TryGetThumbBoneSide(thumbBone, out bool leftHand))
            {
                return;
            }

            if (leftHand)
            {
                diagnostics.LastLeftThumbLocalRotationGuardClampCount++;
            }
            else
            {
                diagnostics.LastRightThumbLocalRotationGuardClampCount++;
            }
        }

        internal static void RecordLocalRotationGuardRisk(
            RetargetingThumbGuardDiagnostics diagnostics,
            bool leftHand,
            float currentRisk,
            float limitedRisk,
            bool preserved)
        {
            if (leftHand)
            {
                diagnostics.LastLeftThumbLocalRotationGuardCurrentRisk = currentRisk;
                diagnostics.LastLeftThumbLocalRotationGuardLimitedRisk = limitedRisk;
                if (preserved)
                {
                    diagnostics.LastLeftThumbLocalRotationGuardPreserveCount++;
                }
            }
            else
            {
                diagnostics.LastRightThumbLocalRotationGuardCurrentRisk = currentRisk;
                diagnostics.LastRightThumbLocalRotationGuardLimitedRisk = limitedRisk;
                if (preserved)
                {
                    diagnostics.LastRightThumbLocalRotationGuardPreserveCount++;
                }
            }
        }
    }
}
