namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 썸 세계회전 보존 결정의 근거를 공개 진단 프로퍼티에 남기기 위한 분류 값.
    /// </summary>
    internal enum ThumbWorldRotationPreserveReason
    {
        None = 0,
        DetachedHelperReferenceAngle = 1,
        DetachedHelperReferenceFrameDeviation = 2,
        LocalRotationReference = 3,
        SuppressedManualReferenceAngle = 4,
        LocalRotationReferenceFallbackNoManualReference = 5,
        LocalRotationReferenceAfterSuppressedReference = 6
    }

    /// <summary>
    /// 썸 로컬회전 가드와 세계회전 보존 경로가 기록하는 최근 진단 값의 런타임 소유 홀더.
    /// 공개 프로퍼티와 HumanoidThumbDeformationGuard의 리셋 경로가 읽으므로 PSR에 상주한다.
    /// </summary>
    internal sealed class RetargetingThumbGuardDiagnostics
    {
        internal int LastLeftThumbLocalRotationGuardClampCount;
        internal int LastRightThumbLocalRotationGuardClampCount;
        internal int LastLeftThumbLocalRotationGuardPreserveCount;
        internal int LastRightThumbLocalRotationGuardPreserveCount;
        internal float LastLeftThumbLocalRotationGuardCurrentRisk = float.NaN;
        internal float LastRightThumbLocalRotationGuardCurrentRisk = float.NaN;
        internal float LastLeftThumbLocalRotationGuardLimitedRisk = float.NaN;
        internal float LastRightThumbLocalRotationGuardLimitedRisk = float.NaN;
        internal bool LastLeftThumbWorldRotationSuppressCompetingOverride;
        internal bool LastRightThumbWorldRotationSuppressCompetingOverride;
        internal bool LastLeftThumbWorldRotationKeepDetachedHelperOverride;
        internal bool LastRightThumbWorldRotationKeepDetachedHelperOverride;
        internal float LastLeftThumbWorldRotationCurrentReferenceFrameDeviation = float.NaN;
        internal float LastRightThumbWorldRotationCurrentReferenceFrameDeviation = float.NaN;
        internal float LastLeftThumbWorldRotationCandidateReferenceFrameDeviation = float.NaN;
        internal float LastRightThumbWorldRotationCandidateReferenceFrameDeviation = float.NaN;
        internal ThumbWorldRotationPreserveReason LastLeftThumbProximalWorldRotationPreserveReason;
        internal ThumbWorldRotationPreserveReason LastRightThumbProximalWorldRotationPreserveReason;
        internal ThumbWorldRotationPreserveReason LastLeftThumbIntermediateWorldRotationPreserveReason;
        internal ThumbWorldRotationPreserveReason LastRightThumbIntermediateWorldRotationPreserveReason;
        internal float LastLeftThumbProximalWorldRotationCurrentReferenceAngle = float.NaN;
        internal float LastRightThumbProximalWorldRotationCurrentReferenceAngle = float.NaN;
        internal float LastLeftThumbIntermediateWorldRotationCurrentReferenceAngle = float.NaN;
        internal float LastRightThumbIntermediateWorldRotationCurrentReferenceAngle = float.NaN;
        internal float LastLeftThumbProximalWorldRotationCandidateReferenceAngle = float.NaN;
        internal float LastRightThumbProximalWorldRotationCandidateReferenceAngle = float.NaN;
        internal float LastLeftThumbIntermediateWorldRotationCandidateReferenceAngle = float.NaN;
        internal float LastRightThumbIntermediateWorldRotationCandidateReferenceAngle = float.NaN;
        internal float LastLeftThumbProximalWorldRotationPreserveCurrentRisk = float.NaN;
        internal float LastRightThumbProximalWorldRotationPreserveCurrentRisk = float.NaN;
        internal float LastLeftThumbIntermediateWorldRotationPreserveCurrentRisk = float.NaN;
        internal float LastRightThumbIntermediateWorldRotationPreserveCurrentRisk = float.NaN;
        internal float LastLeftThumbProximalWorldRotationPreserveLimitedRisk = float.NaN;
        internal float LastRightThumbProximalWorldRotationPreserveLimitedRisk = float.NaN;
        internal float LastLeftThumbIntermediateWorldRotationPreserveLimitedRisk = float.NaN;
        internal float LastRightThumbIntermediateWorldRotationPreserveLimitedRisk = float.NaN;

        internal void ResetLocalRotationGuard()
        {
            (LastLeftThumbLocalRotationGuardClampCount, LastRightThumbLocalRotationGuardClampCount,
                LastLeftThumbLocalRotationGuardPreserveCount, LastRightThumbLocalRotationGuardPreserveCount) =
                (0, 0, 0, 0);
            (LastLeftThumbLocalRotationGuardCurrentRisk, LastRightThumbLocalRotationGuardCurrentRisk,
                LastLeftThumbLocalRotationGuardLimitedRisk, LastRightThumbLocalRotationGuardLimitedRisk) =
                (float.NaN, float.NaN, float.NaN, float.NaN);
        }

        internal void ResetWorldRotationPreserve()
        {
            (LastLeftThumbWorldRotationSuppressCompetingOverride, LastRightThumbWorldRotationSuppressCompetingOverride,
                LastLeftThumbWorldRotationKeepDetachedHelperOverride, LastRightThumbWorldRotationKeepDetachedHelperOverride) =
                (false, false, false, false);
            (LastLeftThumbWorldRotationCurrentReferenceFrameDeviation, LastRightThumbWorldRotationCurrentReferenceFrameDeviation,
                LastLeftThumbWorldRotationCandidateReferenceFrameDeviation, LastRightThumbWorldRotationCandidateReferenceFrameDeviation) =
                (float.NaN, float.NaN, float.NaN, float.NaN);

            ResetWorldRotationPreserveBone(true, true);
            ResetWorldRotationPreserveBone(true, false);
            ResetWorldRotationPreserveBone(false, true);
            ResetWorldRotationPreserveBone(false, false);
        }

        internal void ResetWorldRotationPreserveBone(bool leftHand, bool proximalBone)
        {
            RetargetingThumbGuardDiagnosticsRecorder.RecordWorldRotationReferenceAngles(
                this,
                leftHand,
                proximalBone,
                float.NaN,
                float.NaN);
            RetargetingThumbGuardDiagnosticsRecorder.RecordWorldRotationPreserveLocalRisk(
                this,
                leftHand,
                proximalBone,
                float.NaN,
                float.NaN);
            RetargetingThumbGuardDiagnosticsRecorder.RecordWorldRotationPreserveReason(
                this,
                leftHand,
                proximalBone,
                ThumbWorldRotationPreserveReason.None);
        }
    }
}
