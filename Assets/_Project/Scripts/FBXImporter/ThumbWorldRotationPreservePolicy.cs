using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 수동참조 엄지 세계회전 보정의 보존 여부를 판정하는 런타임 정책.
    /// 로컬회전 가드 공유 로직은 ThumbGuardPolicy에, 진단 기록은 owner의 홀더에 위임한다.
    /// </summary>
    internal static class ThumbWorldRotationPreservePolicy
    {
        internal static bool ShouldPreserveManualWorldRotationCorrection(
            PoseSpaceRetargeter owner,
            HumanBodyBones thumbBone,
            Transform targetThumb,
            Quaternion candidateWorldRotation)
        {
            if (!TryComputeCandidateLocalRotation(targetThumb, candidateWorldRotation, out Quaternion candidateLocalRotation))
            {
                return false;
            }

            if (!ThumbGuardPolicy.TryGetBoneSide(thumbBone, out bool leftHand) ||
                !ThumbGuardPolicy.TryGetWorldRotationDiagnosticBone(thumbBone, out bool proximalBone))
            {
                return ThumbGuardPolicy.ShouldPreserveManualLocalRotationReference(
                    owner, thumbBone, targetThumb, targetThumb.localRotation, candidateLocalRotation);
            }

            ThumbWorldPreserveContext context = CollectWorldPreserveContext(
                owner,
                thumbBone,
                targetThumb,
                candidateWorldRotation,
                candidateLocalRotation,
                leftHand,
                proximalBone);
            return EvaluateWorldPreserveDecision(owner, context);
        }

        private static bool TryComputeCandidateLocalRotation(Transform targetThumb, Quaternion candidateWorldRotation, out Quaternion candidateLocalRotation)
        {
            candidateLocalRotation = Quaternion.identity;
            if (targetThumb == null || !PoseSpaceRetargeter.IsFinite(candidateWorldRotation))
            {
                return false;
            }

            candidateLocalRotation = targetThumb.parent != null
                ? Quaternion.Inverse(targetThumb.parent.rotation) * candidateWorldRotation
                : candidateWorldRotation;

            return PoseSpaceRetargeter.IsFinite(candidateLocalRotation);
        }

        private static ThumbWorldPreserveContext CollectWorldPreserveContext(
            PoseSpaceRetargeter owner,
            HumanBodyBones thumbBone,
            Transform targetThumb,
            Quaternion candidateWorldRotation,
            Quaternion candidateLocalRotation,
            bool leftHand,
            bool proximalBone)
        {
            owner._thumbGuardDiagnostics.ResetWorldRotationPreserveBone(leftHand, proximalBone);

            var context = new ThumbWorldPreserveContext
            {
                thumbBone = thumbBone,
                targetThumb = targetThumb,
                candidateWorldRotation = candidateWorldRotation,
                candidateLocalRotation = candidateLocalRotation,
                leftHand = leftHand,
                proximalBone = proximalBone
            };
            ComputeWorldPreserveFlags(owner, thumbBone, ref context);
            context.hasReferenceLocalRotation = ThumbGuardPolicy.TryGetManualReferenceLocalRotation(owner, thumbBone, out context.referenceLocalRotation);

            RecordWorldPreserveInputs(owner, ref context);
            return context;
        }

        private static void ComputeWorldPreserveFlags(PoseSpaceRetargeter owner, HumanBodyBones thumbBone, ref ThumbWorldPreserveContext context)
        {
            context.suppressCompeting = owner.ShouldSuppressCompetingManualThumbOverride(context.leftHand);
            context.keepDetached = owner.ShouldKeepDetachedHelperManualThumbOverrides(context.leftHand);
            context.allowManualReferenceWorldPreserve = !context.suppressCompeting || context.keepDetached;
            context.allowDetachedHelperShortcutPreserve = context.allowManualReferenceWorldPreserve && !context.keepDetached;
            context.hasDetachedHelperRelationship = owner.HasDetachedThumbBaseHelperRelationship(thumbBone, context.leftHand);
        }

        private static void RecordWorldPreserveInputs(PoseSpaceRetargeter owner, ref ThumbWorldPreserveContext context)
        {
            RecordWorldPreserveAngles(owner, ref context);

            context.currentReferenceFrameDeviation = float.NaN;
            context.candidateReferenceFrameDeviation = float.NaN;
            context.hasReferenceFrameDeviation =
                context.hasDetachedHelperRelationship &&
                owner.TryEvaluateThumbManualReferenceFrameDeviation(
                    context.leftHand,
                    context.targetThumb,
                    context.candidateWorldRotation,
                    out context.currentReferenceFrameDeviation,
                    out context.candidateReferenceFrameDeviation);
            RetargetingThumbGuardDiagnosticsRecorder.RecordWorldRotationOverrideContext(
                owner._thumbGuardDiagnostics,
                context.leftHand,
                context.suppressCompeting,
                context.keepDetached,
                context.currentReferenceFrameDeviation,
                context.candidateReferenceFrameDeviation);
        }

        private static void RecordWorldPreserveAngles(PoseSpaceRetargeter owner, ref ThumbWorldPreserveContext context)
        {
            if (context.hasReferenceLocalRotation)
            {
                context.currentReferenceAngle = Quaternion.Angle(context.referenceLocalRotation, context.targetThumb.localRotation);
                context.candidateReferenceAngle = Quaternion.Angle(context.referenceLocalRotation, context.candidateLocalRotation);
            }
            else
            {
                context.currentReferenceAngle = float.NaN;
                context.candidateReferenceAngle = float.NaN;
            }

            RetargetingThumbGuardDiagnosticsRecorder.RecordWorldRotationReferenceAngles(
                owner._thumbGuardDiagnostics,
                context.leftHand,
                context.proximalBone,
                context.currentReferenceAngle,
                context.candidateReferenceAngle);
        }

        private static bool EvaluateWorldPreserveDecision(PoseSpaceRetargeter owner, ThumbWorldPreserveContext context)
        {
            if (TryPreserveDetachedHelperShortcut(owner, context))
            {
                return true;
            }

            if (TryPreserveByFrameDeviationShortcut(owner, context))
            {
                return true;
            }

            if (!context.suppressCompeting)
            {
                return PreserveLocalRotationAndRecord(owner, context, ThumbWorldRotationPreserveReason.LocalRotationReference);
            }

            if (!context.hasReferenceLocalRotation)
            {
                return PreserveLocalRotationAndRecord(owner, context, ThumbWorldRotationPreserveReason.LocalRotationReferenceFallbackNoManualReference);
            }

            if (TryPreserveSuppressedManualAngle(owner, context))
            {
                return true;
            }

            return PreserveLocalRotationAndRecord(owner, context, ThumbWorldRotationPreserveReason.LocalRotationReferenceAfterSuppressedReference);
        }

        private static bool TryPreserveSuppressedManualAngle(PoseSpaceRetargeter owner, ThumbWorldPreserveContext context)
        {
            bool preserved = context.allowManualReferenceWorldPreserve &&
                context.candidateReferenceAngle > context.currentReferenceAngle + PoseSpaceRetargeter.ManualThumbWorldRotationReferenceToleranceDegrees;
            if (preserved)
            {
                RecordWorldPreserveReason(owner, context, ThumbWorldRotationPreserveReason.SuppressedManualReferenceAngle);
            }

            return preserved;
        }

        private static bool TryPreserveByFrameDeviationShortcut(PoseSpaceRetargeter owner, ThumbWorldPreserveContext context)
        {
            bool preserved = context.allowDetachedHelperShortcutPreserve &&
                context.hasReferenceFrameDeviation &&
                context.candidateReferenceFrameDeviation > context.currentReferenceFrameDeviation + 0.001f;
            if (preserved)
            {
                RecordWorldPreserveReason(owner, context, ThumbWorldRotationPreserveReason.DetachedHelperReferenceFrameDeviation);
            }

            return preserved;
        }

        private static bool TryPreserveDetachedHelperShortcut(PoseSpaceRetargeter owner, ThumbWorldPreserveContext context)
        {
            if (!context.allowDetachedHelperShortcutPreserve ||
                !context.hasDetachedHelperRelationship ||
                !context.hasReferenceLocalRotation)
            {
                return false;
            }

            bool preserved = context.currentReferenceAngle <= PoseSpaceRetargeter.ManualThumbDetachedHelperPreserveCurrentReferenceAngleMax &&
                context.candidateReferenceAngle > context.currentReferenceAngle + PoseSpaceRetargeter.ManualThumbWorldRotationReferenceToleranceDegrees;
            if (preserved)
            {
                RecordWorldPreserveReason(owner, context, ThumbWorldRotationPreserveReason.DetachedHelperReferenceAngle);
            }

            return preserved;
        }

        private static bool PreserveLocalRotationAndRecord(PoseSpaceRetargeter owner, ThumbWorldPreserveContext context, ThumbWorldRotationPreserveReason reason)
        {
            bool preserved = ThumbGuardPolicy.ShouldPreserveManualLocalRotationReference(
                owner,
                context.thumbBone,
                context.targetThumb,
                context.targetThumb.localRotation,
                context.candidateLocalRotation);
            if (preserved)
            {
                RetargetingThumbGuardDiagnosticsRecorder.RecordWorldRotationPreserveLocalRisk(
                    owner._thumbGuardDiagnostics,
                    context.leftHand,
                    context.proximalBone,
                    context.leftHand ? owner._thumbGuardDiagnostics.LastLeftThumbLocalRotationGuardCurrentRisk : owner._thumbGuardDiagnostics.LastRightThumbLocalRotationGuardCurrentRisk,
                    context.leftHand ? owner._thumbGuardDiagnostics.LastLeftThumbLocalRotationGuardLimitedRisk : owner._thumbGuardDiagnostics.LastRightThumbLocalRotationGuardLimitedRisk);
                RecordWorldPreserveReason(owner, context, reason);
            }

            return preserved;
        }

        private static void RecordWorldPreserveReason(PoseSpaceRetargeter owner, ThumbWorldPreserveContext context, ThumbWorldRotationPreserveReason reason)
        {
            RetargetingThumbGuardDiagnosticsRecorder.RecordWorldRotationPreserveReason(
                owner._thumbGuardDiagnostics,
                context.leftHand,
                context.proximalBone,
                reason);
        }
    }
}
