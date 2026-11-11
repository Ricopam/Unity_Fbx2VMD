using UnityEngine;
using RootMotion;
using RootMotion.FinalIK;

namespace Fbx2Vmd.FBXImporter.Editor
{
    /// <summary>
    /// PoseSpaceRetargeter의 에디터 전용 발 참조 구현 — hips local·손/발 로컬회전·
    /// FootHips 정렬 잔여 yaw·Pre/PostSet 엔드포인트·BipedIK 목표 적용.
    /// [InitializeOnLoad] 시점에 FbxEditorFeatureWiring이 RetargetingEditorReferenceHooks.FootReference에 할당한다.
    /// 진단 값은 런타임 홀더(RetargetingFootReferenceDiagnostics)에 기록하고,
    /// 에디터 세션 상태(BipedIK 인스턴스·1회 로그 플래그·평가자 오프셋)는 owner 단위 Session으로 격리한다.
    /// </summary>
    internal sealed class EditorFootReferenceDriver : IEditorFootReferenceDriver
    {
        private sealed class Session
        {
            internal BipedIK BipedIk;
            internal bool BipedIkCreated;
            internal bool BipedIkInitiated;
            internal bool HasPostSetEvaluatorXzFirstOffset;
            internal Vector3 PostSetEvaluatorXzFirstOffset = new Vector3(float.NaN, float.NaN, float.NaN);
        }

        private static Session GetSession(PoseSpaceRetargeter owner)
        {
            Session session = owner._editorFootReferenceSession as Session;
            if (session == null)
            {
                session = new Session();
                owner._editorFootReferenceSession = session;
            }
            return session;
        }

        public void ApplyHipsLocalPositionReference(
            PoseSpaceRetargeter owner)
        {
            if (!owner.ShouldUseManualAnimatorHipsLocalPositionReference ||
                owner.manualAnimatorHipsLocalPositionWeight <= 0f ||
                owner._editorFingerReferenceAnimator == null ||
                owner.targetAnimator == null)
            {
                return;
            }

            if (!(RetargetingEditorReferenceHooks.PoseReference?.UpdateManualReferenceAnimator(owner) ?? false))
            {
                return;
            }

            if (!ManualPoseReferenceApplier.TryResolveHipsLocalPositionReference(
                owner._editorFingerReferenceAnimator,
                owner.targetAnimator,
                out Transform targetHips,
                out Vector3 refCurrentLocalPosition,
                out Vector3 currentLocalPosition))
            {
                return;
            }

            Vector3 ghostRightFootPosition = RetargetingAnimatorPoseReader.ReadBoneWorldPosition(owner.ghostAnimator, HumanBodyBones.RightFoot);
            Vector3 ghostRightToesPosition = RetargetingAnimatorPoseReader.ReadBoneWorldPosition(owner.ghostAnimator, HumanBodyBones.RightToes);
            Vector3 beforeRightFootPosition = RetargetingAnimatorPoseReader.ReadBoneWorldPosition(owner.targetAnimator, HumanBodyBones.RightFoot);
            Vector3 beforeRightToesPosition = RetargetingAnimatorPoseReader.ReadBoneWorldPosition(owner.targetAnimator, HumanBodyBones.RightToes);
            // Delta 방식: testprefab의 clip 시작 대비 현재 변위만 YYB 자연 위치에 더한다.
            // 절대 복사는 모델 비율 차이(YYB Hips Y≈1.024 vs testprefab≈1.056)로 인해 YYB Hips를 잘못된 높이로 강제한다.
            if (!ManualPoseReferenceApplier.TryCalculateHipsLocalPositionReference(
                refCurrentLocalPosition,
                owner._editorReferenceHipsRestLocalPosition,
                owner._hasEditorReferenceHipsRestLocalPosition,
                owner._targetHipsRestLocalPosition,
                owner._hasTargetHipsRestLocalPosition,
                currentLocalPosition,
                owner.manualAnimatorHipsLocalPositionWeight,
                owner.manualAnimatorHipsLocalPositionMaxOffset,
                out Vector3 nextLocalPosition))
            {
                return;
            }

            ManualPoseReferenceApplier.ApplyHipsLocalPosition(targetHips, nextLocalPosition);
            Vector3 afterRightFootPosition = RetargetingAnimatorPoseReader.ReadBoneWorldPosition(owner.targetAnimator, HumanBodyBones.RightFoot);
            Vector3 afterRightToesPosition = RetargetingAnimatorPoseReader.ReadBoneWorldPosition(owner.targetAnimator, HumanBodyBones.RightToes);
            if (!RetargetingEndpointDiagnostics.ShouldKeepHipsLocalPositionReferenceByTargetGap(
                ghostRightFootPosition,
                ghostRightToesPosition,
                beforeRightFootPosition,
                beforeRightToesPosition,
                afterRightFootPosition,
                afterRightToesPosition,
                PoseSpaceRetargeter.HipsLocalPositionTargetGapGuardMaxIncreaseMeters))
            {
                ManualPoseReferenceApplier.ApplyHipsLocalPosition(targetHips, currentLocalPosition);
                RecordEditorHipsLocalReferenceDiagnostics(owner, currentLocalPosition, currentLocalPosition);
                return;
            }

            RecordEditorHipsLocalReferenceDiagnostics(owner, currentLocalPosition, nextLocalPosition);
            if (!owner._editorHipsLocalPositionReferenceLogged)
            {
                Debug.Log($"[PoseSpaceRetargeter] Manual Animator Hips localPosition reference applied. weight={owner.manualAnimatorHipsLocalPositionWeight:F2}, maxOffset={owner.manualAnimatorHipsLocalPositionMaxOffset:F3}m");
                owner._editorHipsLocalPositionReferenceLogged = true;
            }
        }

        private void RecordEditorHipsLocalReferenceDiagnostics(
            PoseSpaceRetargeter owner, Vector3 beforeLocalPosition, Vector3 afterLocalPosition)
        {
            owner._footReferenceDiagnostics.HipsLocalReferenceBeforeLocalY = IsFinite(beforeLocalPosition) ? beforeLocalPosition.y : float.NaN;
            owner._footReferenceDiagnostics.HipsLocalReferenceAfterLocalY = IsFinite(afterLocalPosition) ? afterLocalPosition.y : float.NaN;
            owner._footReferenceDiagnostics.HipsLocalReferenceDeltaY =
                IsFinite(owner._footReferenceDiagnostics.HipsLocalReferenceBeforeLocalY) && IsFinite(owner._footReferenceDiagnostics.HipsLocalReferenceAfterLocalY)
                    ? owner._footReferenceDiagnostics.HipsLocalReferenceAfterLocalY - owner._footReferenceDiagnostics.HipsLocalReferenceBeforeLocalY
                    : float.NaN;
        }

        public void ApplyHandLocalRotationReference(
            PoseSpaceRetargeter owner)
        {
            if (!owner.useManualAnimatorHandLocalRotationReference ||
                owner._editorFingerReferenceAnimator == null ||
                owner.targetAnimator == null)
            {
                return;
            }

            if (!(RetargetingEditorReferenceHooks.PoseReference?.UpdateManualReferenceAnimator(owner) ?? false))
            {
                return;
            }

            int changed = 0;
            if (!owner.ShouldSuppressCompetingManualThumbOverride(true))
            {
                changed += ManualPoseReferenceApplier.ApplyExactLocalRotationReference(
                    owner._editorFingerReferenceAnimator,
                    owner.targetAnimator,
                    HumanBodyBones.LeftHand);
            }

            if (!owner.ShouldSuppressCompetingManualThumbOverride(false))
            {
                changed += ManualPoseReferenceApplier.ApplyExactLocalRotationReference(
                    owner._editorFingerReferenceAnimator,
                    owner.targetAnimator,
                    HumanBodyBones.RightHand);
            }

            if (changed > 0 && !owner._editorHandLocalRotationReferenceLogged)
            {
                Debug.Log($"[PoseSpaceRetargeter] Manual Animator hand localRotation reference applied. bones={changed}");
                owner._editorHandLocalRotationReferenceLogged = true;
            }
        }

        public void ApplyFootLocalRotationReference(
            PoseSpaceRetargeter owner)
        {
            if (!owner.ShouldUseManualAnimatorFootLocalRotationReference ||
                owner.manualAnimatorFootLocalRotationReferenceWeight <= 0f ||
                owner._editorFingerReferenceAnimator == null ||
                owner.targetAnimator == null)
            {
                return;
            }

            if (!(RetargetingEditorReferenceHooks.PoseReference?.UpdateManualReferenceAnimator(owner) ?? false))
            {
                return;
            }

            owner.CaptureTargetFootPositions(out Vector3 leftFootBefore, out Vector3 rightFootBefore);
            int changed = 0;
            changed += ManualPoseReferenceApplier.ApplyBlendedLocalRotationReference(
                owner._editorFingerReferenceAnimator,
                owner.targetAnimator,
                HumanBodyBones.LeftUpperLeg,
                owner.manualAnimatorFootLocalRotationReferenceWeight);
            changed += ManualPoseReferenceApplier.ApplyBlendedLocalRotationReference(
                owner._editorFingerReferenceAnimator,
                owner.targetAnimator,
                HumanBodyBones.RightUpperLeg,
                owner.manualAnimatorFootLocalRotationReferenceWeight);
            changed += ManualPoseReferenceApplier.ApplyBlendedLocalRotationReference(
                owner._editorFingerReferenceAnimator,
                owner.targetAnimator,
                HumanBodyBones.LeftLowerLeg,
                owner.manualAnimatorFootLocalRotationReferenceWeight);
            changed += ManualPoseReferenceApplier.ApplyBlendedLocalRotationReference(
                owner._editorFingerReferenceAnimator,
                owner.targetAnimator,
                HumanBodyBones.RightLowerLeg,
                owner.manualAnimatorFootLocalRotationReferenceWeight);
            changed += ManualPoseReferenceApplier.ApplyBlendedLocalRotationReference(
                owner._editorFingerReferenceAnimator,
                owner.targetAnimator,
                HumanBodyBones.LeftFoot,
                owner.manualAnimatorFootLocalRotationReferenceWeight);
            changed += ManualPoseReferenceApplier.ApplyBlendedLocalRotationReference(
                owner._editorFingerReferenceAnimator,
                owner.targetAnimator,
                HumanBodyBones.RightFoot,
                owner.manualAnimatorFootLocalRotationReferenceWeight);
            changed += ManualPoseReferenceApplier.ApplyBlendedLocalRotationReference(
                owner._editorFingerReferenceAnimator,
                owner.targetAnimator,
                HumanBodyBones.LeftToes,
                owner.manualAnimatorFootLocalRotationReferenceWeight);
            changed += ManualPoseReferenceApplier.ApplyBlendedLocalRotationReference(
                owner._editorFingerReferenceAnimator,
                owner.targetAnimator,
                HumanBodyBones.RightToes,
                owner.manualAnimatorFootLocalRotationReferenceWeight);
            RecordEditorFootLocalRotationReferenceDiagnostics(owner, leftFootBefore, rightFootBefore);

            if (changed > 0 && !owner._editorFootLocalRotationReferenceLogged)
            {
                Debug.Log($"[PoseSpaceRetargeter] Manual Animator lower-body localRotation reference applied. bones={changed}, weight={owner.manualAnimatorFootLocalRotationReferenceWeight:F2}");
                owner._editorFootLocalRotationReferenceLogged = true;
            }
        }

        private void RecordEditorFootLocalRotationReferenceDiagnostics(
            PoseSpaceRetargeter owner, Vector3 leftFootBefore, Vector3 rightFootBefore)
        {
            owner._footReferenceDiagnostics.FootLocalRotationLeftFootXzDelta = owner.CalculateTargetFootXzDelta(leftFootBefore, HumanBodyBones.LeftFoot);
            owner._footReferenceDiagnostics.FootLocalRotationRightFootXzDelta = owner.CalculateTargetFootXzDelta(rightFootBefore, HumanBodyBones.RightFoot);
        }

        private void RecordEditorFootHipsAlignedResidualYawReferenceDiagnostics(
            PoseSpaceRetargeter owner, Vector3 leftFootBefore, Vector3 rightFootBefore)
        {
            owner._footReferenceDiagnostics.FootHipsAlignedResidualYawLeftFootXzDelta = owner.CalculateTargetFootXzDelta(leftFootBefore, HumanBodyBones.LeftFoot);
            owner._footReferenceDiagnostics.FootHipsAlignedResidualYawRightFootXzDelta = owner.CalculateTargetFootXzDelta(rightFootBefore, HumanBodyBones.RightFoot);
        }

        public void ApplyFootHipsAlignedResidualYawReference(
            PoseSpaceRetargeter owner)
        {
            if (!owner.ShouldUseManualAnimatorFootHipsAlignedResidualYawReference ||
                owner.manualAnimatorFootHipsAlignedResidualYawReferenceWeight <= 0f ||
                owner._editorFingerReferenceAnimator == null ||
                owner.targetAnimator == null)
            {
                return;
            }

            if (!(RetargetingEditorReferenceHooks.PoseReference?.UpdateManualReferenceAnimator(owner) ?? false))
            {
                return;
            }

            Transform referenceHips = owner._editorFingerReferenceAnimator.GetBoneTransform(HumanBodyBones.Hips);
            Transform targetHips = owner.targetAnimator.GetBoneTransform(HumanBodyBones.Hips);
            if (referenceHips == null || targetHips == null)
            {
                return;
            }

            float weight = Mathf.Clamp01(owner.manualAnimatorFootHipsAlignedResidualYawReferenceWeight);
            float maxAngle = Mathf.Max(0f, owner.manualAnimatorFootHipsAlignedResidualYawReferenceMaxAngle);
            float leftResidual = float.NaN;
            float rightResidual = float.NaN;
            TryCalculateEditorFootHipsAlignedResidualForBone(owner,
                HumanBodyBones.LeftFoot,
                referenceHips,
                targetHips,
                out leftResidual);
            TryCalculateEditorFootHipsAlignedResidualForBone(owner,
                HumanBodyBones.RightFoot,
                referenceHips,
                targetHips,
                out rightResidual);

            bool leftDominantResidual = IsFinite(leftResidual) &&
                (!IsFinite(rightResidual) || leftResidual > rightResidual);
            bool rightDominantResidual = IsFinite(rightResidual) &&
                (!IsFinite(leftResidual) || rightResidual > leftResidual);
            float leftMaxAngle = RetargetingEndpointDiagnostics.ResolveEditorFootHipsAlignedResidualYawSideAwareMaxAngle(
                leftResidual,
                rightResidual,
                maxAngle,
                leftDominantResidual);
            float rightMaxAngle = RetargetingEndpointDiagnostics.ResolveEditorFootHipsAlignedResidualYawSideAwareMaxAngle(
                rightResidual,
                leftResidual,
                maxAngle,
                rightDominantResidual);
            owner.CaptureTargetFootPositions(out Vector3 leftFootBefore, out Vector3 rightFootBefore);
            int changed = 0;
            changed += ApplyEditorHumanoidFootHipsAlignedResidualYawReferenceBone(owner,
                HumanBodyBones.LeftUpperLeg,
                HumanBodyBones.LeftFoot,
                referenceHips,
                targetHips,
                weight,
                leftMaxAngle);
            changed += ApplyEditorHumanoidFootHipsAlignedResidualYawReferenceBone(owner,
                HumanBodyBones.RightUpperLeg,
                HumanBodyBones.RightFoot,
                referenceHips,
                targetHips,
                weight,
                rightMaxAngle);
            RecordEditorFootHipsAlignedResidualYawReferenceDiagnostics(owner, leftFootBefore, rightFootBefore);

            if (changed > 0 && !owner._editorFootHipsAlignedResidualYawReferenceLogged)
            {
                Debug.Log($"[PoseSpaceRetargeter] Manual Animator hips-aligned foot X/Z residual yaw reference applied. feet={changed}, weight={weight:F2}, maxAngle={maxAngle:F1}deg");
                owner._editorFootHipsAlignedResidualYawReferenceLogged = true;
            }
        }

        private bool TryCalculateEditorFootHipsAlignedResidualForBone(
            PoseSpaceRetargeter owner,
            HumanBodyBones footBone,
            Transform referenceHips,
            Transform targetHips,
            out float residual)
        {
            residual = float.NaN;
            Transform targetFoot = owner.targetAnimator.GetBoneTransform(footBone);
            if (targetFoot == null ||
                !ManualPoseReferenceApplier.TryResolveHipsAlignedEndpointPositionReference(
                    owner._editorFingerReferenceAnimator,
                    owner.targetAnimator,
                    footBone,
                    referenceHips,
                    targetHips,
                    targetFoot,
                    out Vector3 desiredFootPosition))
            {
                return false;
            }

            Vector3 residualVector = desiredFootPosition - targetFoot.position;
            residualVector.y = 0f;
            residual = residualVector.magnitude;
            return IsFinite(residual);
        }

        private int ApplyEditorHumanoidFootHipsAlignedResidualYawReferenceBone(
            PoseSpaceRetargeter owner,
            HumanBodyBones upperLegBone,
            HumanBodyBones footBone,
            Transform referenceHips,
            Transform targetHips,
            float weight,
            float maxAngle)
        {
            Transform targetUpperLeg = owner.targetAnimator.GetBoneTransform(upperLegBone);
            Transform targetFoot = owner.targetAnimator.GetBoneTransform(footBone);
            if (targetUpperLeg == null || targetFoot == null)
            {
                return 0;
            }

            if (!ManualPoseReferenceApplier.TryResolveHipsAlignedEndpointPositionReference(
                    owner._editorFingerReferenceAnimator,
                    owner.targetAnimator,
                    footBone,
                    referenceHips,
                    targetHips,
                    targetFoot,
                    out Vector3 desiredFootPosition))
            {
                return 0;
            }

            if (!RetargetingEndpointDiagnostics.TryCalculateEditorFootHipsAlignedResidualYawReference(
                    desiredFootPosition,
                    targetFoot.position,
                    targetUpperLeg.position,
                    targetUpperLeg.rotation,
                    weight,
                    maxAngle,
                    out Quaternion nextWorldRotation))
            {
                return 0;
            }

            targetUpperLeg.rotation = nextWorldRotation;
            return 1;
        }

        public void ApplyPreSetEndpointPositionReference(
            PoseSpaceRetargeter owner)
        {
            if (!owner.usePreSetHumanPoseRightEndpointPositionReference ||
                owner.preSetHumanPoseRightEndpointPositionReferenceWeight <= 0f ||
                owner.preSetHumanPoseRightEndpointPositionReferenceMaxOffset <= 0f ||
                owner._editorFingerReferenceAnimator == null ||
                owner.targetAnimator == null)
            {
                return;
            }

            if (!(RetargetingEditorReferenceHooks.PoseReference?.UpdateManualReferenceAnimator(owner) ?? false))
            {
                return;
            }

            Transform referenceHips = owner._editorFingerReferenceAnimator.GetBoneTransform(HumanBodyBones.Hips);
            Transform targetHips = owner.targetAnimator.GetBoneTransform(HumanBodyBones.Hips);
            bool useLeftSide = owner.ShouldUseLeftSideForPreSetHumanPoseEndpointPosition;
            HumanBodyBones footBone = useLeftSide ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;
            HumanBodyBones toesBone = useLeftSide ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes;
            HumanBodyBones upperLegBone = useLeftSide ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg;
            Transform targetUpperLeg = owner.targetAnimator.GetBoneTransform(upperLegBone);
            Transform targetFoot = owner.targetAnimator.GetBoneTransform(footBone);
            Transform targetToes = owner.targetAnimator.GetBoneTransform(toesBone);
            if (referenceHips == null ||
                targetHips == null ||
                targetUpperLeg == null ||
                targetFoot == null)
            {
                return;
            }

            if (!owner.ShouldApplyPreSetHumanPoseRightEndpointPositionFrameGate())
            {
                return;
            }

            if (!ManualPoseReferenceApplier.TryResolveHipsAlignedEndpointPositionReference(
                    owner._editorFingerReferenceAnimator,
                    owner.targetAnimator,
                    footBone,
                    referenceHips,
                    targetHips,
                    targetFoot,
                    out Vector3 desiredFootPosition))
            {
                return;
            }

            Vector3 desiredToesPosition = PoseSpaceRetargeter.BuildNaNVector3();
            if (targetToes != null)
            {
                ManualPoseReferenceApplier.TryResolveHipsAlignedEndpointPositionReference(
                    owner._editorFingerReferenceAnimator,
                    owner.targetAnimator,
                    toesBone,
                    referenceHips,
                    targetHips,
                    targetToes,
                    out desiredToesPosition);
            }

            if (!RetargetingEndpointDiagnostics.TryCalculateReferencePosition(
                    desiredFootPosition,
                    desiredToesPosition,
                    targetFoot.position,
                    targetToes != null ? targetToes.position : PoseSpaceRetargeter.BuildNaNVector3(),
                    owner.preSetHumanPoseRightEndpointPositionReferenceWeight,
                    owner.preSetHumanPoseRightEndpointPositionReferenceMaxOffset,
                    owner.preSetHumanPoseRightEndpointPositionReferencePositiveZScale,
                    owner.preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight,
                    out Vector3 nextFootPosition))
            {
                return;
            }

            float maxAngleDegrees = RetargetingEndpointDiagnostics.CalculateEndpointPositionMaxYawAngle(
                targetFoot.position,
                targetUpperLeg.position,
                owner.preSetHumanPoseRightEndpointPositionReferenceMaxOffset);
            if (!RetargetingEndpointDiagnostics.TryCalculateEditorFootHipsAlignedResidualYawReference(
                    nextFootPosition,
                    targetFoot.position,
                    targetUpperLeg.position,
                    targetUpperLeg.rotation,
                    1f,
                    maxAngleDegrees,
                    out Quaternion nextWorldRotation,
                    out _))
            {
                return;
            }

            targetUpperLeg.rotation = nextWorldRotation;
        }

        public void ApplyPreSetSignCorrectedRowLocalBodyPositionReference(
            PoseSpaceRetargeter owner, ref HumanPose pose)
        {
            owner._footReferenceDiagnostics.ResetPreSetEndpoint();
            if (!owner.usePreSetHumanPoseRightEndpointPositionReference ||
                !owner.preSetHumanPoseEndpointPositionUseGhostCurrentBasis ||
                owner.preSetHumanPoseRightEndpointPositionReferenceWeight <= 0f ||
                owner.preSetHumanPoseRightEndpointPositionReferenceMaxOffset <= 0f ||
                owner.ghostAnimator == null ||
                owner.targetAnimator == null ||
                !IsFinite(pose.bodyPosition))
            {
                return;
            }

            if (!owner.ShouldApplyPreSetHumanPoseRightEndpointPositionFrameGate())
            {
                return;
            }

            RetargetEndpointStageWorldPositions ghostPositions = RetargetingEndpointStageDiagnostics.CapturePositions(owner.ghostAnimator);
            RetargetEndpointStageWorldPositions currentPositions = owner._setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions;
            bool useLeftSide = owner.ShouldUseLeftSideForPreSetHumanPoseEndpointPosition;
            Vector3 ghostFootPosition = useLeftSide ? ghostPositions.LeftFoot : ghostPositions.RightFoot;
            Vector3 currentFootPosition = useLeftSide ? currentPositions.LeftFoot : currentPositions.RightFoot;
            Vector3 bodyPositionBefore = pose.bodyPosition;

            if (ManualPoseReferenceApplier.TryCalculateSignCorrectedBodyPositionXzReference(
                    bodyPositionBefore,
                    ghostFootPosition,
                    currentFootPosition,
                    owner.preSetHumanPoseRightEndpointPositionReferenceWeight,
                    owner.preSetHumanPoseRightEndpointPositionReferenceMaxOffset,
                    axisXScale: 1f,
                    axisZScale: 1f,
                    invertX: owner.ShouldInvertPreSetHumanPoseEndpointPositionBodyX,
                    invertZ: owner.ShouldInvertPreSetHumanPoseEndpointPositionBodyZ,
                    out Vector3 nextBodyPosition))
            {
                CapturePreSetHumanPoseEndpointBodyPositionDiagnostics(owner, bodyPositionBefore, nextBodyPosition);
                pose.bodyPosition = nextBodyPosition;
            }
        }

        private void CapturePreSetHumanPoseEndpointBodyPositionDiagnostics(
            PoseSpaceRetargeter owner, Vector3 before, Vector3 after)
        {
            if (!IsFinite(before) || !IsFinite(after))
            {
                owner._footReferenceDiagnostics.ResetPreSetEndpoint();
                return;
            }

            owner._footReferenceDiagnostics.PreSetEndpointBodyPositionBefore = before;
            owner._footReferenceDiagnostics.PreSetEndpointBodyPositionAfter = after;
            owner._footReferenceDiagnostics.PreSetEndpointBodyPositionDelta = after - before;
        }

        public void ApplyPostSetEndpointPositionReference(
            PoseSpaceRetargeter owner)
        {
            Session s = GetSession(owner);
            owner._postSetEndpointDiagnostics.Reset();
            if (!owner.usePostSetHumanPoseRightFootEvaluatorXzReference)
            {
                s.HasPostSetEvaluatorXzFirstOffset = false;
                s.PostSetEvaluatorXzFirstOffset = PoseSpaceRetargeter.BuildNaNVector3();
            }

            if (!owner.usePostSetHumanPoseRightEndpointPositionReference ||
                owner.postSetHumanPoseRightEndpointPositionReferenceWeight <= 0f ||
                owner.postSetHumanPoseRightEndpointPositionReferenceMaxOffset <= 0f ||
                owner._editorFingerReferenceAnimator == null ||
                owner.targetAnimator == null)
            {
                return;
            }

            if (!(RetargetingEditorReferenceHooks.PoseReference?.UpdateManualReferenceAnimator(owner) ?? false))
            {
                return;
            }

            Transform referenceHips = owner._editorFingerReferenceAnimator.GetBoneTransform(HumanBodyBones.Hips);
            Transform targetHips = owner.targetAnimator.GetBoneTransform(HumanBodyBones.Hips);
            bool useLeftSide = owner.ShouldUseLeftSideForPostSetHumanPoseEndpointPosition;
            HumanBodyBones footBone = useLeftSide ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot;
            HumanBodyBones toesBone = useLeftSide ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes;
            HumanBodyBones upperLegBone = useLeftSide ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg;
            Transform referenceFoot = owner._editorFingerReferenceAnimator.GetBoneTransform(footBone);
            Transform targetUpperLeg = owner.targetAnimator.GetBoneTransform(upperLegBone);
            Transform targetFoot = owner.targetAnimator.GetBoneTransform(footBone);
            Transform targetToes = owner.targetAnimator.GetBoneTransform(toesBone);
            if (referenceHips == null ||
                targetHips == null ||
                targetUpperLeg == null ||
                targetFoot == null)
            {
                return;
            }

            Vector3 evaluatorXzFirstOffset = PoseSpaceRetargeter.BuildNaNVector3();
            if (owner.usePostSetHumanPoseRightFootEvaluatorXzReference)
            {
                if (referenceFoot == null ||
                    !TryResolvePostSetHumanPoseRightFootEvaluatorXzFirstOffset(owner,
                        referenceFoot.position,
                        targetFoot.position,
                        out evaluatorXzFirstOffset))
                {
                    return;
                }
            }

            if (!owner.ShouldApplyPostSetHumanPoseRightEndpointPositionFrameGate())
            {
                return;
            }

            Vector3 nextFootPosition;
            RetargetingEndpointDiagnosticSnapshot endpointDiagnostics;
            bool calculated;
            if (owner.usePostSetHumanPoseRightFootEvaluatorXzReference)
            {
                calculated = RetargetingEndpointDiagnostics.TryCalculateEvaluatorXzReferencePosition(
                    referenceFoot.position,
                    targetFoot.position,
                    evaluatorXzFirstOffset,
                    owner.postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude,
                    owner.postSetHumanPoseRightEndpointPositionReferenceWeight,
                    owner.postSetHumanPoseRightEndpointPositionReferenceMaxOffset,
                    out nextFootPosition,
                    out endpointDiagnostics);
            }
            else
            {
                if (!ManualPoseReferenceApplier.TryResolveHipsAlignedEndpointPositionReference(
                        owner._editorFingerReferenceAnimator,
                        owner.targetAnimator,
                        footBone,
                        referenceHips,
                        targetHips,
                        targetFoot,
                        out Vector3 desiredFootPosition))
                {
                    return;
                }

                Vector3 desiredToesPosition = PoseSpaceRetargeter.BuildNaNVector3();
                if (targetToes != null)
                {
                    ManualPoseReferenceApplier.TryResolveHipsAlignedEndpointPositionReference(
                        owner._editorFingerReferenceAnimator,
                        owner.targetAnimator,
                        toesBone,
                        referenceHips,
                        targetHips,
                        targetToes,
                        out desiredToesPosition);
                }

                calculated = RetargetingEndpointDiagnostics.TryCalculateReferencePosition(
                    desiredFootPosition,
                    desiredToesPosition,
                    targetFoot.position,
                    targetToes != null ? targetToes.position : PoseSpaceRetargeter.BuildNaNVector3(),
                    owner.postSetHumanPoseRightEndpointPositionReferenceWeight,
                    owner.postSetHumanPoseRightEndpointPositionReferenceMaxOffset,
                    owner.postSetHumanPoseRightEndpointPositionReferencePositiveZScale,
                    owner.postSetHumanPoseRightEndpointPositionReferenceToesBlendWeight,
                    out nextFootPosition,
                    out endpointDiagnostics);
            }

            if (!calculated)
            {
                owner._postSetEndpointDiagnostics.Record(
                    endpointDiagnostics,
                    maxYawAngle: float.NaN,
                    yawCorrectionAngle: float.NaN,
                    upperLegRotationDeltaAngle: float.NaN,
                    applied: 0f);
                return;
            }

            float maxAngleDegrees = RetargetingEndpointDiagnostics.CalculateEndpointPositionMaxYawAngle(
                targetFoot.position,
                targetUpperLeg.position,
                owner.postSetHumanPoseRightEndpointPositionReferenceMaxOffset);
            if (!RetargetingEndpointDiagnostics.TryCalculateEditorFootHipsAlignedResidualYawReference(
                    nextFootPosition,
                    targetFoot.position,
                    targetUpperLeg.position,
                    targetUpperLeg.rotation,
                    1f,
                    maxAngleDegrees,
                    out Quaternion nextWorldRotation,
                    out float yawCorrectionAngle))
            {
                owner._postSetEndpointDiagnostics.Record(
                    endpointDiagnostics,
                    maxAngleDegrees,
                    yawCorrectionAngle,
                    upperLegRotationDeltaAngle: float.NaN,
                    applied: 0f);
                return;
            }

            float upperLegRotationDeltaAngle = Quaternion.Angle(targetUpperLeg.rotation, nextWorldRotation);
            owner._postSetEndpointDiagnostics.Record(
                endpointDiagnostics,
                maxAngleDegrees,
                yawCorrectionAngle,
                upperLegRotationDeltaAngle,
                applied: 1f);

            targetUpperLeg.rotation = nextWorldRotation;
        }

        private bool TryResolvePostSetHumanPoseRightFootEvaluatorXzFirstOffset(
            PoseSpaceRetargeter owner,
            Vector3 referenceFootPosition,
            Vector3 currentFootPosition,
            out Vector3 firstOffset)
        {
            Session s = GetSession(owner);
            firstOffset = s.PostSetEvaluatorXzFirstOffset;
            if (!IsFinite(referenceFootPosition) || !IsFinite(currentFootPosition))
            {
                return false;
            }

            if (!s.HasPostSetEvaluatorXzFirstOffset ||
                !IsFinite(s.PostSetEvaluatorXzFirstOffset))
            {
                s.PostSetEvaluatorXzFirstOffset = currentFootPosition - referenceFootPosition;
                s.PostSetEvaluatorXzFirstOffset.y = 0f;
                s.HasPostSetEvaluatorXzFirstOffset =
                    IsFinite(s.PostSetEvaluatorXzFirstOffset);
            }

            firstOffset = s.PostSetEvaluatorXzFirstOffset;
            return s.HasPostSetEvaluatorXzFirstOffset && IsFinite(firstOffset);
        }

        public void ApplyBipedIkFootPositionReference(
            PoseSpaceRetargeter owner)
        {
            if (!owner.useManualAnimatorBipedIkFootPositionReference ||
                owner.manualAnimatorBipedIkFootPositionReferenceWeight <= 0f ||
                owner._editorFingerReferenceAnimator == null ||
                owner.targetAnimator == null)
            {
                DisableOwnedEditorManualFootBipedIk(owner);
                return;
            }

            if (!(RetargetingEditorReferenceHooks.PoseReference?.UpdateManualReferenceAnimator(owner) ?? false))
            {
                return;
            }

            BipedIK bipedIk = EnsureEditorManualFootBipedIk(owner);
            if (bipedIk == null)
            {
                return;
            }

            if (!ManualPoseReferenceApplier.TryResolveHipsTransforms(
                owner._editorFingerReferenceAnimator,
                owner.targetAnimator,
                out Transform referenceHips,
                out Transform targetHips))
            {
                return;
            }

            int changed = 0;
            changed += ApplyEditorHumanoidBipedIkFootPositionReferenceGoal(owner,
                bipedIk,
                AvatarIKGoal.LeftFoot,
                HumanBodyBones.LeftFoot,
                referenceHips,
                targetHips);
            changed += ApplyEditorHumanoidBipedIkFootPositionReferenceGoal(owner,
                bipedIk,
                AvatarIKGoal.RightFoot,
                HumanBodyBones.RightFoot,
                referenceHips,
                targetHips);

            if (changed <= 0)
            {
                return;
            }

            bipedIk.UpdateSolverExternal();
            if (!owner._editorFootIkPositionReferenceLogged)
            {
                Debug.Log($"[PoseSpaceRetargeter] Manual Animator BipedIK foot position reference applied. feet={changed}, weight={owner.manualAnimatorBipedIkFootPositionReferenceWeight:F2}, maxOffset={owner.manualAnimatorBipedIkFootPositionReferenceMaxOffset:F3}m");
                owner._editorFootIkPositionReferenceLogged = true;
            }
        }

        private BipedIK EnsureEditorManualFootBipedIk(
            PoseSpaceRetargeter owner)
        {
            Session s = GetSession(owner);
            if (owner.targetAnimator == null)
            {
                return null;
            }

            if (s.BipedIk == null)
            {
                s.BipedIk = owner.targetAnimator.GetComponent<BipedIK>();
                if (s.BipedIk == null)
                {
                    s.BipedIk = owner.targetAnimator.gameObject.AddComponent<BipedIK>();
                    s.BipedIkCreated = true;
                }
                s.BipedIkInitiated = false;
            }

            if (s.BipedIk == null)
            {
                return null;
            }

            if (owner.targetAnimator.isHuman)
            {
                BipedReferences references = s.BipedIk.references;
                BipedReferences.AutoDetectReferences(
                    ref references,
                    owner.targetAnimator.transform,
                    BipedReferences.AutoDetectParams.Default);
                s.BipedIk.references = references;
            }

            s.BipedIk.enabled = true;
            s.BipedIk.fixTransforms = false;
            if (!s.BipedIkInitiated)
            {
                s.BipedIk.InitiateBipedIK();
                s.BipedIkInitiated = true;
            }
            s.BipedIk.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
            s.BipedIk.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
            s.BipedIk.solvers.leftFoot.maintainRotationWeight = 1f;
            s.BipedIk.solvers.rightFoot.maintainRotationWeight = 1f;
            return s.BipedIk;
        }

        private void DisableOwnedEditorManualFootBipedIk(
            PoseSpaceRetargeter owner)
        {
            Session s = GetSession(owner);
            if (s.BipedIk == null)
            {
                return;
            }

            s.BipedIk.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f);
            s.BipedIk.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
            s.BipedIk.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
            s.BipedIk.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
            if (s.BipedIkCreated)
            {
                s.BipedIk.fixTransforms = false;
                s.BipedIk.enabled = false;
            }
        }

        private int ApplyEditorHumanoidBipedIkFootPositionReferenceGoal(
            PoseSpaceRetargeter owner,
            BipedIK bipedIk,
            AvatarIKGoal goal,
            HumanBodyBones footBone,
            Transform referenceHips,
            Transform targetHips)
        {
            if (!ManualPoseReferenceApplier.TryResolveFootIkPositionReference(
                    owner._editorFingerReferenceAnimator,
                    owner.targetAnimator,
                    footBone,
                    referenceHips,
                    targetHips,
                    owner.manualAnimatorBipedIkFootPositionReferenceWeight,
                    owner.manualAnimatorBipedIkFootPositionReferenceMaxOffset,
                    out Vector3 nextPosition))
            {
                bipedIk.SetIKPositionWeight(goal, 0f);
                return 0;
            }

            bipedIk.SetIKPosition(goal, nextPosition);
            bipedIk.SetIKPositionWeight(goal, 1f);
            return 1;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }
}
