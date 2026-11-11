using UnityEngine;

namespace Fbx2Vmd.FBXImporter.Editor
{
    /// <summary>
    /// 에디터 수동 참조 하체 세그먼트 방향 보정 구현.
    /// FbxEditorFeatureWiring이 RetargetingEditorReferenceHooks.LowerBodyDirection에 할당한다.
    /// 진단 기록은 owner._lowerBodySegmentDiagnostics(런타임 홀더)에 쓰고, 공유 상태는 owner 경유로만 접근한다.
    /// </summary>
    internal sealed class EditorLowerBodyDirectionDriver : IEditorLowerBodyDirectionDriver
    {
        public void Apply(PoseSpaceRetargeter owner)
        {
            if (!owner.ShouldUseManualAnimatorLowerBodySegmentDirectionReference ||
                owner.manualAnimatorLowerBodySegmentDirectionReferenceWeight <= 0f ||
                owner._editorFingerReferenceAnimator == null ||
                owner.targetAnimator == null)
            {
                return;
            }

            if (!(RetargetingEditorReferenceHooks.PoseReference?.UpdateManualReferenceAnimator(owner) ?? false))
            {
                return;
            }

            float weight = Mathf.Clamp01(owner.manualAnimatorLowerBodySegmentDirectionReferenceWeight);
            float maxAngle = Mathf.Max(0f, owner.manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle);
            float upperLegToLowerLegMaxAngle = ResolveManualAnimatorUpperLegToLowerLegSegmentDirectionMaxAngle(owner, maxAngle);
            float lowerLegToFootMaxAngle = ResolveManualAnimatorLowerLegToFootSegmentDirectionMaxAngle(owner, maxAngle);
            float footToToesMaxAngle = ResolveManualAnimatorFootToToesSegmentDirectionMaxAngle(owner, maxAngle);
            owner.CaptureTargetFootPositions(out Vector3 leftFootBefore, out Vector3 rightFootBefore);
            owner._lowerBodySegmentDiagnostics.ResetDetailed();
            int changed = 0;
            if (!owner.ShouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference)
            {
                changed += AlignEditorHumanoidLowerBodySegmentDirection(owner, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, weight, upperLegToLowerLegMaxAngle);
                changed += AlignEditorHumanoidLowerBodySegmentDirection(owner, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, weight, upperLegToLowerLegMaxAngle);
            }

            if (!owner.ShouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference)
            {
                changed += AlignEditorHumanoidLowerBodySegmentDirection(owner,
                    HumanBodyBones.LeftLowerLeg,
                    HumanBodyBones.LeftFoot,
                    weight,
                    ResolveManualAnimatorLowerLegToFootSegmentDirectionMaxAngle(owner, lowerLegToFootMaxAngle, rightSide: false));
                changed += AlignEditorHumanoidLowerBodySegmentDirection(owner,
                    HumanBodyBones.RightLowerLeg,
                    HumanBodyBones.RightFoot,
                    ResolveManualAnimatorRightLowerLegToFootSegmentDirectionBlendWeight(owner, weight),
                    ResolveManualAnimatorLowerLegToFootSegmentDirectionMaxAngle(owner, lowerLegToFootMaxAngle, rightSide: true),
                    owner.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale,
                    owner.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceEndpointBlendWeight);
            }

            if (!owner.ShouldDisableManualAnimatorFootToToesSegmentDirectionReference)
            {
                changed += AlignEditorHumanoidLowerBodySegmentDirection(owner, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, weight, footToToesMaxAngle);
                changed += AlignEditorHumanoidLowerBodySegmentDirection(owner, HumanBodyBones.RightFoot, HumanBodyBones.RightToes, weight, footToToesMaxAngle);
            }

            RecordEditorLowerBodySegmentDirectionReferenceDiagnostics(owner, leftFootBefore, rightFootBefore);

            if (changed > 0 && !owner._editorLowerBodySegmentDirectionReferenceLogged)
            {
                Debug.Log($"[PoseSpaceRetargeter] Manual Animator lower-body segment direction reference applied. segments={changed}, weight={weight:F2}, maxAngle={maxAngle:F1}deg");
                owner._editorLowerBodySegmentDirectionReferenceLogged = true;
            }
        }
        private int AlignEditorHumanoidLowerBodySegmentDirection(PoseSpaceRetargeter owner,
            HumanBodyBones parentBone,
            HumanBodyBones childBone,
            float weight,
            float maxAngle,
            float correctionAxisXzScale = 1f,
            float childWorldRotationBlendWeight = 1f)
        {
            Transform targetParent = owner.targetAnimator.GetBoneTransform(parentBone);
            Transform targetChild = owner.targetAnimator.GetBoneTransform(childBone);
            Transform referenceParent = owner._editorFingerReferenceAnimator.GetBoneTransform(parentBone);
            Transform referenceChild = owner._editorFingerReferenceAnimator.GetBoneTransform(childBone);
            if (targetParent == null || targetChild == null || referenceParent == null || referenceChild == null)
            {
                return 0;
            }

            Vector3 currentSegment = targetChild.position - targetParent.position;
            Vector3 referenceSegment = referenceChild.position - referenceParent.position;
            if (!PoseSpaceRetargeter.TryNormalize(currentSegment, out Vector3 currentDirection) ||
                !PoseSpaceRetargeter.TryNormalize(referenceSegment, out Vector3 referenceDirection))
            {
                return 0;
            }

            Vector3 referenceRootDirection = owner._editorFingerReferenceAnimator.transform.InverseTransformDirection(referenceDirection).normalized;
            Vector3 desiredWorldDirection = owner.targetAnimator.transform.TransformDirection(referenceRootDirection).normalized;
            if (!IsFinite(referenceRootDirection) || !IsFinite(desiredWorldDirection))
            {
                return 0;
            }

            Quaternion currentParentRotation = targetParent.rotation;
            Quaternion childWorldRotationBefore = targetChild.rotation;
            Quaternion childLocalRotationBefore = targetChild.localRotation;
            float preAngle = Vector3.Angle(currentDirection, desiredWorldDirection);
            if (!ManualPoseReferenceApplier.TryCalculateSegmentDirectionReference(
                    desiredWorldDirection,
                    currentDirection,
                    currentParentRotation,
                    weight,
                    maxAngle,
                    correctionAxisXzScale,
                    out Quaternion nextWorldRotation))
            {
                return 0;
            }

            targetParent.rotation = nextWorldRotation;
            float clampedChildWorldRotationBlend = Mathf.Clamp01(childWorldRotationBlendWeight);
            if (clampedChildWorldRotationBlend < 0.9999f)
            {
                targetChild.rotation = Quaternion.Slerp(
                    childWorldRotationBefore,
                    targetChild.rotation,
                    clampedChildWorldRotationBlend);
            }

            Vector3 postSegment = targetChild.position - targetParent.position;
            if (PoseSpaceRetargeter.TryNormalize(postSegment, out Vector3 postDirection))
            {
                Quaternion correction = nextWorldRotation * Quaternion.Inverse(currentParentRotation);
                float correctionAngle = Quaternion.Angle(Quaternion.identity, correction);
                float parentWorldRotationDeltaAngle = Quaternion.Angle(currentParentRotation, targetParent.rotation);
                float childLocalRotationDeltaAngle = Quaternion.Angle(childLocalRotationBefore, targetChild.localRotation);
                float postAngle = Vector3.Angle(postDirection, desiredWorldDirection);
                RecordEditorLowerBodySegmentDirectionSegmentDiagnostics(owner,
                    parentBone,
                    childBone,
                    correctionAngle,
                    parentWorldRotationDeltaAngle,
                    childLocalRotationDeltaAngle,
                    preAngle,
                    postAngle,
                    ReadFiniteCorrectionAxis(correction),
                    desiredWorldDirection,
                    currentDirection,
                    postDirection);
            }

            return 1;
        }
        private void RecordEditorLowerBodySegmentDirectionReferenceDiagnostics(PoseSpaceRetargeter owner, Vector3 leftFootBefore, Vector3 rightFootBefore)
        {
            owner._lowerBodySegmentDiagnostics.LeftFootXzDelta = owner.CalculateTargetFootXzDelta(leftFootBefore, HumanBodyBones.LeftFoot);
            owner._lowerBodySegmentDiagnostics.RightFootXzDelta = owner.CalculateTargetFootXzDelta(rightFootBefore, HumanBodyBones.RightFoot);
            RecordEditorLowerBodySegmentDirectionEndpointDiagnostics(owner);
        }
        private void RecordEditorLowerBodySegmentDirectionEndpointDiagnostics(PoseSpaceRetargeter owner)
        {
            owner._lowerBodySegmentDiagnostics.LeftLowerLegWorldPosition = owner.ReadTargetBoneWorldPosition(HumanBodyBones.LeftLowerLeg);
            owner._lowerBodySegmentDiagnostics.LeftFootWorldPosition = owner.ReadTargetBoneWorldPosition(HumanBodyBones.LeftFoot);
            owner._lowerBodySegmentDiagnostics.LeftToesWorldPosition = owner.ReadTargetBoneWorldPosition(HumanBodyBones.LeftToes);
            owner._lowerBodySegmentDiagnostics.RightLowerLegWorldPosition = owner.ReadTargetBoneWorldPosition(HumanBodyBones.RightLowerLeg);
            owner._lowerBodySegmentDiagnostics.RightFootWorldPosition = owner.ReadTargetBoneWorldPosition(HumanBodyBones.RightFoot);
            owner._lowerBodySegmentDiagnostics.RightToesWorldPosition = owner.ReadTargetBoneWorldPosition(HumanBodyBones.RightToes);
            owner._lowerBodySegmentDiagnostics.LeftFootForward = owner.ReadTargetBoneWorldForward(HumanBodyBones.LeftFoot);
            owner._lowerBodySegmentDiagnostics.LeftFootUp = owner.ReadTargetBoneWorldUp(HumanBodyBones.LeftFoot);
            owner._lowerBodySegmentDiagnostics.RightFootForward = owner.ReadTargetBoneWorldForward(HumanBodyBones.RightFoot);
            owner._lowerBodySegmentDiagnostics.RightFootUp = owner.ReadTargetBoneWorldUp(HumanBodyBones.RightFoot);
        }
        private void RecordEditorLowerBodySegmentDirectionSegmentDiagnostics(PoseSpaceRetargeter owner,
            HumanBodyBones parentBone,
            HumanBodyBones childBone,
            float correctionAngle,
            float parentWorldRotationDeltaAngle,
            float childLocalRotationDeltaAngle,
            float preAngle,
            float postAngle,
            Vector3 correctionAxis,
            Vector3 referenceDirection,
            Vector3 preDirection,
            Vector3 postDirection)
        {
            string segmentName = BuildLowerBodySegmentName(parentBone, childBone);
            SetEditorLowerBodySegmentDirectionCorrectionAngle(owner, segmentName, correctionAngle);
            SetEditorLowerBodySegmentDirectionCouplingDiagnostics(owner,
                segmentName,
                parentWorldRotationDeltaAngle,
                childLocalRotationDeltaAngle,
                correctionAxis,
                referenceDirection,
                preDirection,
                postDirection);
            if (!IsFinite(correctionAngle) ||
                (!float.IsNaN(owner._lowerBodySegmentDiagnostics.MaxCorrectionAngle) &&
                    correctionAngle <= owner._lowerBodySegmentDiagnostics.MaxCorrectionAngle))
            {
                return;
            }

            owner._lowerBodySegmentDiagnostics.MaxCorrectionSegment = segmentName;
            owner._lowerBodySegmentDiagnostics.MaxCorrectionAngle = correctionAngle;
            owner._lowerBodySegmentDiagnostics.MaxPreAngle = preAngle;
            owner._lowerBodySegmentDiagnostics.MaxPostAngle = postAngle;
            owner._lowerBodySegmentDiagnostics.MaxCorrectionAxis = correctionAxis;
            owner._lowerBodySegmentDiagnostics.MaxReferenceDirection = referenceDirection;
            owner._lowerBodySegmentDiagnostics.MaxPreDirection = preDirection;
            owner._lowerBodySegmentDiagnostics.MaxPostDirection = postDirection;
        }
        private void SetEditorLowerBodySegmentDirectionCouplingDiagnostics(PoseSpaceRetargeter owner,
            string segmentName,
            float parentWorldRotationDeltaAngle,
            float childLocalRotationDeltaAngle,
            Vector3 correctionAxis,
            Vector3 referenceDirection,
            Vector3 preDirection,
            Vector3 postDirection)
        {
            switch (segmentName)
            {
                case "LeftLowerLegToFoot":
                    owner._lowerBodySegmentDiagnostics.LeftLowerLegToFootParentWorldRotationDeltaAngle = parentWorldRotationDeltaAngle;
                    owner._lowerBodySegmentDiagnostics.LeftLowerLegToFootChildFootLocalRotationDeltaAngle = childLocalRotationDeltaAngle;
                    owner._lowerBodySegmentDiagnostics.LeftLowerLegToFootCorrectionAxis = correctionAxis;
                    break;
                case "RightLowerLegToFoot":
                    owner._lowerBodySegmentDiagnostics.RightLowerLegToFootParentWorldRotationDeltaAngle = parentWorldRotationDeltaAngle;
                    owner._lowerBodySegmentDiagnostics.RightLowerLegToFootChildFootLocalRotationDeltaAngle = childLocalRotationDeltaAngle;
                    owner._lowerBodySegmentDiagnostics.RightLowerLegToFootCorrectionAxis = correctionAxis;
                    break;
                case "LeftFootToToes":
                    owner._lowerBodySegmentDiagnostics.LeftFootToToesReferenceDirection = referenceDirection;
                    owner._lowerBodySegmentDiagnostics.LeftFootToToesPreDirection = preDirection;
                    owner._lowerBodySegmentDiagnostics.LeftFootToToesPostDirection = postDirection;
                    break;
                case "RightFootToToes":
                    owner._lowerBodySegmentDiagnostics.RightFootToToesReferenceDirection = referenceDirection;
                    owner._lowerBodySegmentDiagnostics.RightFootToToesPreDirection = preDirection;
                    owner._lowerBodySegmentDiagnostics.RightFootToToesPostDirection = postDirection;
                    break;
            }
        }
        private void SetEditorLowerBodySegmentDirectionCorrectionAngle(PoseSpaceRetargeter owner, string segmentName, float correctionAngle)
        {
            switch (segmentName)
            {
                case "LeftUpperLegToLowerLeg":
                    owner._lowerBodySegmentDiagnostics.LeftUpperLegLowerLegCorrectionAngle = correctionAngle;
                    break;
                case "RightUpperLegToLowerLeg":
                    owner._lowerBodySegmentDiagnostics.RightUpperLegLowerLegCorrectionAngle = correctionAngle;
                    break;
                case "LeftLowerLegToFoot":
                    owner._lowerBodySegmentDiagnostics.LeftLowerLegFootCorrectionAngle = correctionAngle;
                    break;
                case "RightLowerLegToFoot":
                    owner._lowerBodySegmentDiagnostics.RightLowerLegFootCorrectionAngle = correctionAngle;
                    break;
                case "LeftFootToToes":
                    owner._lowerBodySegmentDiagnostics.LeftFootToesCorrectionAngle = correctionAngle;
                    break;
                case "RightFootToToes":
                    owner._lowerBodySegmentDiagnostics.RightFootToesCorrectionAngle = correctionAngle;
                    break;
            }
        }
        private float ResolveManualAnimatorUpperLegToLowerLegSegmentDirectionMaxAngle(PoseSpaceRetargeter owner, float fallbackMaxAngle)
        {
            float segmentMaxAngle = Mathf.Max(0f, owner.manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle);
            return segmentMaxAngle > 0f ? segmentMaxAngle : fallbackMaxAngle;
        }
        private float ResolveManualAnimatorLowerLegToFootSegmentDirectionMaxAngle(PoseSpaceRetargeter owner, float fallbackMaxAngle)
        {
            float segmentMaxAngle = Mathf.Max(0f, owner.manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle);
            return segmentMaxAngle > 0f ? segmentMaxAngle : fallbackMaxAngle;
        }

        private float ResolveManualAnimatorLowerLegToFootSegmentDirectionMaxAngle(
            PoseSpaceRetargeter owner,
            float fallbackMaxAngle,
            bool rightSide)
        {
            float sideMaxAngle = Mathf.Max(
                0f,
                rightSide
                    ? owner.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle
                    : owner.manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle);
            if (rightSide && sideMaxAngle > 0f && !ShouldApplyManualAnimatorRightLowerLegToFootFrameGate(owner))
            {
                sideMaxAngle = 0f;
            }

            return sideMaxAngle > 0f
                ? sideMaxAngle
                : ResolveManualAnimatorLowerLegToFootSegmentDirectionMaxAngle(owner, fallbackMaxAngle);
        }
        private float ResolveManualAnimatorFootToToesSegmentDirectionMaxAngle(PoseSpaceRetargeter owner, float fallbackMaxAngle)
        {
            float footToToesMaxAngle = Mathf.Max(0f, owner.manualAnimatorFootToToesSegmentDirectionReferenceMaxAngle);
            return footToToesMaxAngle > 0f ? footToToesMaxAngle : fallbackMaxAngle;
        }
        private float ResolveManualAnimatorRightLowerLegToFootSegmentDirectionBlendWeight(PoseSpaceRetargeter owner, float fallbackWeight)
        {
            return Mathf.Clamp01(fallbackWeight) *
                Mathf.Clamp01(owner.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceBlendWeight);
        }
        private bool ShouldApplyManualAnimatorRightLowerLegToFootFrameGate(PoseSpaceRetargeter owner)
        {
            return owner.ShouldApplyManualReferenceFrameGate(
                owner.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateStart,
                owner.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateEnd);
        }
        private static Vector3 ReadFiniteCorrectionAxis(Quaternion correction)
        {
            if (!IsFinite(correction))
            {
                return BuildNaNVector3();
            }

            correction.ToAngleAxis(out float angle, out Vector3 axis);
            if (!IsFinite(angle) || angle <= 0.001f || !IsFinite(axis))
            {
                return BuildNaNVector3();
            }

            return axis.normalized;
        }
        private static string BuildLowerBodySegmentName(HumanBodyBones parentBone, HumanBodyBones childBone)
        {
            if (parentBone == HumanBodyBones.LeftUpperLeg && childBone == HumanBodyBones.LeftLowerLeg)
            {
                return "LeftUpperLegToLowerLeg";
            }

            if (parentBone == HumanBodyBones.RightUpperLeg && childBone == HumanBodyBones.RightLowerLeg)
            {
                return "RightUpperLegToLowerLeg";
            }

            if (parentBone == HumanBodyBones.LeftLowerLeg && childBone == HumanBodyBones.LeftFoot)
            {
                return "LeftLowerLegToFoot";
            }

            if (parentBone == HumanBodyBones.RightLowerLeg && childBone == HumanBodyBones.RightFoot)
            {
                return "RightLowerLegToFoot";
            }

            if (parentBone == HumanBodyBones.LeftFoot && childBone == HumanBodyBones.LeftToes)
            {
                return "LeftFootToToes";
            }

            if (parentBone == HumanBodyBones.RightFoot && childBone == HumanBodyBones.RightToes)
            {
                return "RightFootToToes";
            }

            return $"{parentBone}To{childBone}";
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static Vector3 BuildNaNVector3()
        {
            return new Vector3(float.NaN, float.NaN, float.NaN);
        }
    }
}
