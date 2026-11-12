using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 썸 로컬회전 가드(초기자세 허용각 제한)와 수동참조 로컬회전 보존 판정을 담당하는 런타임 정책.
    /// 진단 기록은 owner의 ThumbGuardDiagnostics에, 참조 평가는 owner의 에디터 훅에 위임한다.
    /// </summary>
    internal static class ThumbGuardPolicy
    {
        internal static void ClampTargetLocalRotations(PoseSpaceRetargeter owner)
        {
            if (!owner.enableThumbLocalRotationGuard || owner.targetAnimator == null || owner._targetInitialThumbLocalRotations.Count == 0)
            {
                return;
            }

            if (HasFinalLocalRotationGuard(owner))
            {
                return;
            }

            owner._thumbGuardDiagnostics.ResetLocalRotationGuard();
            int changed = ClampAllThumbBones(owner);

            if (changed > 0 && owner.logThumbLocalRotationGuardCorrections && !owner._thumbLocalRotationGuardWarningLogged)
            {
                Debug.LogWarning($"[PoseSpaceRetargeter] 엄지 본 localRotation {changed}개를 기준 자세 허용각 안으로 제한했습니다.");
                owner._thumbLocalRotationGuardWarningLogged = true;
            }
        }

        private static int ClampAllThumbBones(PoseSpaceRetargeter owner)
        {
            int changed = 0;
            foreach (HumanBodyBones thumbBone in PoseSpaceRetargeter.ThumbRotationBones)
            {
                Transform thumbTransform = owner.targetAnimator.GetBoneTransform(thumbBone);
                if (thumbTransform == null || !owner._targetInitialThumbLocalRotations.TryGetValue(thumbTransform, out Quaternion initialRotation))
                {
                    continue;
                }

                if (TryClampThumbBone(owner, thumbBone, thumbTransform, initialRotation))
                {
                    changed++;
                }
            }

            return changed;
        }

        private static bool TryClampThumbBone(PoseSpaceRetargeter owner, HumanBodyBones thumbBone, Transform thumbTransform, Quaternion initialRotation)
        {
            float limit = GetLocalRotationLimit(owner, thumbBone);
            Quaternion currentRotation = thumbTransform.localRotation;
            if (TryRestoreInitialRotation(limit, currentRotation, thumbTransform, initialRotation))
            {
                return true;
            }

            float angle = Quaternion.Angle(initialRotation, currentRotation);
            if (angle <= limit)
            {
                return false;
            }

            Quaternion limitedRotation = ThumbLocalRotationCalculator.LimitLocalRotation(
                initialRotation,
                currentRotation,
                limit);
            if (ShouldPreserveManualLocalRotationReference(owner, thumbBone, thumbTransform, currentRotation, limitedRotation))
            {
                return false;
            }

            thumbTransform.localRotation = limitedRotation;
            RetargetingThumbGuardDiagnosticsRecorder.RecordLocalRotationGuardClamp(owner._thumbGuardDiagnostics, thumbBone);
            return true;
        }

        private static bool TryRestoreInitialRotation(float limit, Quaternion currentRotation, Transform thumbTransform, Quaternion initialRotation)
        {
            if (limit <= 0f || !PoseSpaceRetargeter.IsFinite(currentRotation))
            {
                thumbTransform.localRotation = initialRotation;
                return true;
            }

            return false;
        }

        private static bool HasFinalLocalRotationGuard(PoseSpaceRetargeter owner)
        {
            if (owner.targetAnimator == null)
            {
                return false;
            }

            HumanoidThumbDeformationGuard guard = owner.targetAnimator.GetComponent<HumanoidThumbDeformationGuard>();
            return guard != null && guard.enabled && guard.isActiveAndEnabled;
        }

        internal static bool ShouldPreserveManualLocalRotationReference(
            PoseSpaceRetargeter owner,
            HumanBodyBones thumbBone,
            Transform targetThumb,
            Quaternion currentRotation,
            Quaternion limitedRotation)
        {
            if (!HasManualLocalRotationReferenceContext(owner, thumbBone, targetThumb, out bool leftHand))
            {
                return false;
            }

            if (!TryEvaluateLocalRotationOverrideRisks(owner, leftHand, targetThumb, currentRotation, limitedRotation, out float currentRisk, out float limitedRisk))
            {
                return false;
            }

            if (Mathf.Max(currentRisk, limitedRisk) < PoseSpaceRetargeter.ManualThumbOverrideSuppressRiskThreshold)
            {
                return false;
            }

            bool preserved = limitedRisk > currentRisk + PoseSpaceRetargeter.ManualThumbOverrideRiskIncreaseTolerance;
            RecordLocalRotationGuardPreserve(owner, leftHand, currentRisk, limitedRisk, preserved);
            return preserved;
        }

        private static void RecordLocalRotationGuardPreserve(PoseSpaceRetargeter owner, bool leftHand, float currentRisk, float limitedRisk, bool preserved)
        {
            if (!preserved)
            {
                return;
            }

            RetargetingThumbGuardDiagnosticsRecorder.RecordLocalRotationGuardRisk(owner._thumbGuardDiagnostics, leftHand, currentRisk, limitedRisk, true);
        }

        private static bool HasManualLocalRotationReferenceContext(PoseSpaceRetargeter owner, HumanBodyBones thumbBone, Transform targetThumb, out bool leftHand)
        {
            leftHand = false;
            return owner._useEditorFingerPoseReference &&
                owner.useManualAnimatorThumbLocalRotationReference &&
                owner._editorFingerReferenceAnimator != null &&
                targetThumb != null &&
                PoseSpaceRetargeter.TryGetThumbBoneSide(thumbBone, out leftHand);
        }

        private static bool TryEvaluateLocalRotationOverrideRisks(
            PoseSpaceRetargeter owner,
            bool leftHand,
            Transform targetThumb,
            Quaternion currentRotation,
            Quaternion limitedRotation,
            out float currentRisk,
            out float limitedRisk)
        {
            if (!owner.TryEvaluateThumbLocalRotationOverrideRisk(leftHand, targetThumb, currentRotation, out currentRisk) ||
                !owner.TryEvaluateThumbLocalRotationOverrideRisk(leftHand, targetThumb, limitedRotation, out limitedRisk) ||
                !PoseSpaceRetargeter.IsFinite(currentRisk) ||
                !PoseSpaceRetargeter.IsFinite(limitedRisk))
            {
                currentRisk = float.NaN;
                limitedRisk = float.NaN;
                return false;
            }

            RetargetingThumbGuardDiagnosticsRecorder.RecordLocalRotationGuardRisk(
                owner._thumbGuardDiagnostics,
                leftHand,
                currentRisk,
                limitedRisk,
                false);
            return true;
        }

        internal static bool TryGetManualReferenceLocalRotation(PoseSpaceRetargeter owner, HumanBodyBones thumbBone, out Quaternion localRotation)
        {
            localRotation = Quaternion.identity;
            if (!owner._useEditorFingerPoseReference ||
                !owner.useManualAnimatorThumbLocalRotationReference ||
                owner._editorFingerReferenceAnimator == null)
            {
                return false;
            }

            Transform referenceThumb = owner._editorFingerReferenceAnimator.GetBoneTransform(thumbBone);
            if (referenceThumb == null || !PoseSpaceRetargeter.IsFinite(referenceThumb.localRotation))
            {
                return false;
            }

            localRotation = referenceThumb.localRotation;
            return true;
        }

        private static float GetLocalRotationLimit(PoseSpaceRetargeter owner, HumanBodyBones thumbBone)
        {
            switch (thumbBone)
            {
                case HumanBodyBones.LeftThumbProximal:
                case HumanBodyBones.RightThumbProximal:
                    return Mathf.Clamp(owner.thumbProximalMaxLocalAngle, 0f, 90f);
                case HumanBodyBones.LeftThumbIntermediate:
                case HumanBodyBones.RightThumbIntermediate:
                    return Mathf.Clamp(owner.thumbIntermediateMaxLocalAngle, 0f, 120f);
                case HumanBodyBones.LeftThumbDistal:
                case HumanBodyBones.RightThumbDistal:
                    return Mathf.Clamp(owner.thumbDistalMaxLocalAngle, 0f, 120f);
                default:
                    return 0f;
            }
        }

        internal static bool TryGetWorldRotationDiagnosticBone(HumanBodyBones thumbBone, out bool proximalBone)
        {
            if (thumbBone == HumanBodyBones.LeftThumbProximal ||
                thumbBone == HumanBodyBones.RightThumbProximal)
            {
                proximalBone = true;
                return true;
            }

            if (thumbBone == HumanBodyBones.LeftThumbIntermediate ||
                thumbBone == HumanBodyBones.RightThumbIntermediate)
            {
                proximalBone = false;
                return true;
            }

            proximalBone = false;
            return false;
        }
    }
}
