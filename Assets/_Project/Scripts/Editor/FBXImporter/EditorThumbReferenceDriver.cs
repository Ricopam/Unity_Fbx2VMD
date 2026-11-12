using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter.Editor
{
    /// <summary>
    /// PoseSpaceRetargeter의 에디터 전용 썸/팜 참조 구현 — 썸 로컬회전·베이스위치·세그먼트/핸드 방향·
    /// 팜프레임 적용과 썸 헬퍼 관계·위험 평가자. [InitializeOnLoad] 시점에 FbxEditorFeatureWiring이
    /// RetargetingEditorReferenceHooks.ThumbReference에 할당한다.
    /// 배타 가변 상태가 없으므로 세션은 두지 않고, 공유 캐시·플래그는 owner 경유로 접근한다.
    /// </summary>
    internal sealed class EditorThumbReferenceDriver : IEditorThumbReferenceDriver
    {

        public void ApplyThumbLocalRotationReference(
            PoseSpaceRetargeter owner)
        {
            if (!owner.useManualAnimatorThumbLocalRotationReference ||
                !owner._useEditorFingerPoseReference ||
                owner._editorFingerReferenceAnimator == null ||
                owner.targetAnimator == null)
            {
                return;
            }

            bool suppressLeftThumbLocalRotation = ShouldSuppressCompetingManualThumbOverride(owner, true);
            bool suppressRightThumbLocalRotation = ShouldSuppressCompetingManualThumbOverride(owner, false);
            int changed = 0;
            foreach (HumanBodyBones thumbBone in PoseSpaceRetargeter.ThumbRotationBones)
            {
                Transform source = owner._editorFingerReferenceAnimator.GetBoneTransform(thumbBone);
                Transform target = owner.targetAnimator.GetBoneTransform(thumbBone);
                if (source == null || target == null)
                {
                    continue;
                }

                if ((thumbBone == HumanBodyBones.LeftThumbProximal ||
                        thumbBone == HumanBodyBones.LeftThumbIntermediate ||
                        thumbBone == HumanBodyBones.LeftThumbDistal) &&
                    suppressLeftThumbLocalRotation)
                {
                    continue;
                }

                if ((thumbBone == HumanBodyBones.RightThumbProximal ||
                        thumbBone == HumanBodyBones.RightThumbIntermediate ||
                        thumbBone == HumanBodyBones.RightThumbDistal) &&
                    suppressRightThumbLocalRotation)
                {
                    continue;
                }

                Quaternion sourceRotation = source.localRotation;
                if (!IsFinite(sourceRotation) || Quaternion.Angle(target.localRotation, sourceRotation) <= 0.001f)
                {
                    continue;
                }

                target.localRotation = sourceRotation;
                changed++;
            }

            if (changed > 0 && !owner._editorThumbLocalRotationReferenceLogged)
            {
                Debug.Log($"[PoseSpaceRetargeter] Manual Animator thumb localRotation reference applied. bones={changed}");
                owner._editorThumbLocalRotationReferenceLogged = true;
            }
        }

        public void ApplyThumbBasePositionReference(
            PoseSpaceRetargeter owner)
        {
            if (!owner.useManualAnimatorThumbBasePositionReference ||
                owner.manualAnimatorThumbBasePositionWeight <= 0f ||
                owner._editorFingerReferenceAnimator == null ||
                owner.targetAnimator == null)
            {
                return;
            }

            int changed = 0;
            changed += ApplyEditorHumanoidThumbBasePositionReferenceSide(owner,
                HumanBodyBones.LeftHand,
                HumanBodyBones.LeftThumbProximal,
                HumanBodyBones.LeftIndexProximal,
                PoseSpaceRetargeter.LeftThumbBaseHelperNameSuffix);
            changed += ApplyEditorHumanoidThumbBasePositionReferenceSide(owner,
                HumanBodyBones.RightHand,
                HumanBodyBones.RightThumbProximal,
                HumanBodyBones.RightIndexProximal,
                PoseSpaceRetargeter.RightThumbBaseHelperNameSuffix);

            if (changed > 0 && !owner._editorThumbBasePositionReferenceLogged)
            {
                Debug.Log($"[PoseSpaceRetargeter] Manual Animator thumb base position reference applied. targets={changed}, weight={owner.manualAnimatorThumbBasePositionWeight:F2}, maxOffset={owner.manualAnimatorThumbBasePositionMaxOffset:F4}");
                owner._editorThumbBasePositionReferenceLogged = true;
            }
        }

        private int ApplyEditorHumanoidThumbBasePositionReferenceSide(
            PoseSpaceRetargeter owner,
            HumanBodyBones handBone,
            HumanBodyBones thumbBone,
            HumanBodyBones indexBone,
            string helperNameSuffix)
        {
            Transform referenceThumb = owner._editorFingerReferenceAnimator.GetBoneTransform(thumbBone);
            Transform targetThumb = owner.targetAnimator.GetBoneTransform(thumbBone);

            if (referenceThumb == null || targetThumb == null)
            {
                return 0;
            }

            bool leftHand = handBone == HumanBodyBones.LeftHand;
            if (!TryBuildThumbPalmFrame(owner._editorFingerReferenceAnimator, leftHand, out ThumbPalmFrame referenceFrame) ||
                !TryBuildThumbPalmFrame(owner.targetAnimator, leftHand, out ThumbPalmFrame targetFrame))
            {
                return 0;
            }

            Vector3 referencePalmLocalThumb = referenceFrame.InverseTransformPoint(referenceThumb.position);
            float palmScale = Mathf.Clamp(targetFrame.Scale / referenceFrame.Scale, 0.25f, 4f);
            Vector3 desiredWorldPosition = targetFrame.TransformPoint(referencePalmLocalThumb * palmScale);
            if (ShouldRejectManualThumbBasePositionOverride(owner, leftHand, targetThumb, desiredWorldPosition))
            {
                return 0;
            }

            int changed = 0;
            changed += ApplyThumbBasePositionToTransform(owner,
                targetThumb,
                desiredWorldPosition,
                owner._targetInitialHumanoidLocalPositions);

            Transform helperTransform = GetCachedThumbBaseHelper(owner, leftHand) ?? owner.FindTargetTransformByNameSuffix(helperNameSuffix);
            if (helperTransform != null && helperTransform != targetThumb)
            {
                changed += ApplyThumbBasePositionToTransform(owner,
                    helperTransform,
                    desiredWorldPosition,
                    owner._targetInitialThumbBaseHelperLocalPositions);
            }

            return changed;
        }

        private int ApplyThumbBasePositionToTransform(
            PoseSpaceRetargeter owner,
            Transform targetTransform,
            Vector3 desiredWorldPosition,
            IDictionary<Transform, Vector3> initialLocalPositions)
        {
            if (targetTransform == null || targetTransform.parent == null)
            {
                return 0;
            }

            if (!initialLocalPositions.TryGetValue(targetTransform, out Vector3 initialLocalPosition))
            {
                initialLocalPosition = targetTransform.localPosition;
                initialLocalPositions[targetTransform] = initialLocalPosition;
            }

            Vector3 desiredLocalPosition = targetTransform.parent.InverseTransformPoint(desiredWorldPosition);
            float maxOffset = Mathf.Max(0f, owner.manualAnimatorThumbBasePositionMaxOffset);
            if (maxOffset > 0f)
            {
                desiredLocalPosition = initialLocalPosition + Vector3.ClampMagnitude(desiredLocalPosition - initialLocalPosition, maxOffset);
            }

            Vector3 targetLocalPosition = Vector3.Lerp(
                initialLocalPosition,
                desiredLocalPosition,
                Mathf.Clamp01(owner.manualAnimatorThumbBasePositionWeight));

            if ((targetTransform.localPosition - targetLocalPosition).sqrMagnitude <= 0.00000001f)
            {
                return 0;
            }

            targetTransform.localPosition = targetLocalPosition;
            return 1;
        }

        public void ApplyThumbSegmentDirectionReference(
            PoseSpaceRetargeter owner)
        {
            if (!owner.useManualAnimatorThumbSegmentDirectionReference ||
                !owner._useEditorFingerPoseReference ||
                owner._editorFingerReferenceAnimator == null ||
                owner.targetAnimator == null)
            {
                return;
            }

            float weight = Mathf.Clamp01(owner.manualAnimatorThumbSegmentDirectionWeight);
            if (weight <= 0.0001f)
            {
                return;
            }

            int changed = 0;
            if (!ShouldSuppressCompetingManualThumbOverride(owner, true))
            {
                changed += AlignEditorHumanoidThumbSegmentDirection(owner, true, HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, weight);
                changed += AlignEditorHumanoidThumbSegmentDirection(owner, true, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal, weight);
            }

            if (!ShouldSuppressCompetingManualThumbOverride(owner, false))
            {
                changed += AlignEditorHumanoidThumbSegmentDirection(owner, false, HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, weight);
                changed += AlignEditorHumanoidThumbSegmentDirection(owner, false, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal, weight);
            }

            if (changed > 0 && !owner._editorThumbSegmentDirectionReferenceLogged)
            {
                Debug.Log($"[PoseSpaceRetargeter] Manual Animator thumb segment direction reference applied. segments={changed}, weight={weight:F2}");
                owner._editorThumbSegmentDirectionReferenceLogged = true;
            }
        }

        public void ApplyThumbHandDirectionReference(
            PoseSpaceRetargeter owner)
        {
            if (!owner.useManualAnimatorThumbHandDirectionReference ||
                !owner._useEditorFingerPoseReference ||
                owner._editorFingerReferenceAnimator == null ||
                owner.targetAnimator == null)
            {
                return;
            }

            float weight = Mathf.Clamp01(owner.manualAnimatorThumbHandDirectionWeight);
            if (weight <= 0.0001f)
            {
                return;
            }

            int changed = 0;
            if (!ShouldSuppressCompetingManualThumbOverride(owner, true))
            {
                changed += AlignEditorHumanoidThumbHandDirection(owner, true, weight);
            }

            if (!ShouldSuppressCompetingManualThumbOverride(owner, false))
            {
                changed += AlignEditorHumanoidThumbHandDirection(owner, false, weight);
            }

            if (changed <= 0)
            {
                return;
            }
        }

        private int AlignEditorHumanoidThumbHandDirection(
            PoseSpaceRetargeter owner, bool leftHand, float weight)
        {
            Transform targetHand = owner.targetAnimator.GetBoneTransform(leftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Transform referenceHand = owner._editorFingerReferenceAnimator.GetBoneTransform(leftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Transform targetProximal = owner.targetAnimator.GetBoneTransform(leftHand ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
            Transform targetIntermediate = owner.targetAnimator.GetBoneTransform(leftHand ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate);
            Transform referenceIntermediate = owner._editorFingerReferenceAnimator.GetBoneTransform(leftHand ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate);

            if (targetHand == null || referenceHand == null ||
                targetProximal == null || targetIntermediate == null ||
                referenceIntermediate == null)
            {
                return 0;
            }

            Vector3 targetDirection = targetIntermediate.position - targetHand.position;
            Vector3 referenceDirection = referenceIntermediate.position - referenceHand.position;
            if (!IsFinite(targetDirection) || !IsFinite(referenceDirection) ||
                targetDirection.sqrMagnitude <= 0.00000001f ||
                referenceDirection.sqrMagnitude <= 0.00000001f)
            {
                return 0;
            }

            if (!TryBuildThumbPalmFrame(owner._editorFingerReferenceAnimator, leftHand, out ThumbPalmFrame referenceFrame) ||
                !TryBuildThumbPalmFrame(owner.targetAnimator, leftHand, out ThumbPalmFrame targetFrame))
            {
                return 0;
            }

            Vector3 referenceHandDirection = referenceFrame.InverseTransformDirection(referenceDirection.normalized).normalized;
            Vector3 desiredWorldDirection = targetFrame.TransformDirection(referenceHandDirection).normalized;
            Vector3 currentWorldDirection = targetDirection.normalized;
            if (!IsFinite(referenceHandDirection) || !IsFinite(desiredWorldDirection) || !IsFinite(currentWorldDirection))
            {
                return 0;
            }

            Quaternion correction = Quaternion.FromToRotation(currentWorldDirection, desiredWorldDirection);
            if (!IsFinite(correction))
            {
                return 0;
            }

            if (weight < 0.999f)
            {
                correction = Quaternion.Slerp(Quaternion.identity, correction, weight);
            }

            Quaternion nextWorldRotation = correction * targetProximal.rotation;
            if (!IsFinite(nextWorldRotation) || Quaternion.Angle(targetProximal.rotation, nextWorldRotation) <= 0.001f)
            {
                return 0;
            }

            targetProximal.rotation = nextWorldRotation;
            return 1;
        }

        public void ApplyHandPalmFrameReference(
            PoseSpaceRetargeter owner)
        {
            if (!owner.useManualAnimatorHandPalmFrameReference ||
                !owner._useEditorFingerPoseReference ||
                owner._editorFingerReferenceAnimator == null ||
                owner.targetAnimator == null)
            {
                return;
            }

            float weight = Mathf.Clamp01(owner.manualAnimatorHandPalmFrameWeight);
            if (weight <= 0.0001f)
            {
                return;
            }

            int changed = 0;
            if (!ShouldSuppressCompetingManualThumbOverride(owner, true))
            {
                changed += AlignEditorHumanoidHandPalmFrame(owner, true, weight);
            }

            if (!ShouldSuppressCompetingManualThumbOverride(owner, false))
            {
                changed += AlignEditorHumanoidHandPalmFrame(owner, false, weight);
            }

            if (changed > 0 && !owner._editorHandPalmFrameReferenceLogged)
            {
                Debug.Log($"[PoseSpaceRetargeter] Manual Animator hand palm-frame reference applied. hands={changed}, weight={weight:F2}");
                owner._editorHandPalmFrameReferenceLogged = true;
            }
        }

        public bool ShouldSuppressCompetingManualThumbOverride(
            PoseSpaceRetargeter owner, bool leftHand)
        {
            if (!TryEvaluateManualOverrideRisk(owner, leftHand, out float risk) ||
                risk < PoseSpaceRetargeter.ManualThumbOverrideSuppressRiskThreshold)
            {
                return false;
            }

            return !ShouldKeepDetachedHelperManualThumbOverrides(owner, leftHand);
        }

        public bool ShouldKeepDetachedHelperManualThumbOverrides(
            PoseSpaceRetargeter owner, bool leftHand)
        {
            HumanBodyBones proximalBone = leftHand ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal;
            if (!owner.HasDetachedThumbBaseHelperRelationship(proximalBone, leftHand) ||
                !TryEvaluateCurrentThumbReferenceFrameDelta(owner, leftHand, out float spreadDelta, out float projectionDelta))
            {
                return false;
            }

            return spreadDelta <= PoseSpaceRetargeter.ManualThumbDetachedHelperOverrideKeepSpreadDeltaMax &&
                projectionDelta <= PoseSpaceRetargeter.ManualThumbDetachedHelperOverrideKeepProjectionDeltaMax;
        }

        public bool TryGetHighRiskManualThumbPoseConstraintOverrides(
            PoseSpaceRetargeter owner,
            bool leftHand,
            out float projectionMin,
            out float projectionMax,
            out float maxSpreadAngle)
        {
            projectionMin = PoseSpaceRetargeter.ManualThumbOverrideProjectionMin;
            projectionMax = PoseSpaceRetargeter.ManualThumbOverrideProjectionMax;
            maxSpreadAngle = PoseSpaceRetargeter.ManualThumbOverrideSpreadFullRiskAngle;

            bool manualOverridePathActive =
                ShouldSuppressCompetingManualThumbOverride(owner, leftHand) ||
                ShouldKeepDetachedHelperManualThumbOverrides(owner, leftHand);

            return manualOverridePathActive &&
                TryEvaluateManualOverrideRisk(owner, leftHand, out float risk) &&
                IsFinite(risk) &&
                risk >= PoseSpaceRetargeter.ManualThumbPoseShapingSuppressMaxRisk;
        }

        public string BuildThumbHelperRelationshipDebugSummary(
            PoseSpaceRetargeter owner, bool leftHand)
        {
            Transform helperTransform = GetCachedThumbBaseHelper(owner, leftHand);
            Transform sourceTransform = GetCachedExplicitThumbBaseSource(owner, leftHand);
            string sideLabel = leftHand ? "L" : "R";

            if (helperTransform == null || sourceTransform == null)
            {
                return $"side={sideLabel}, helper={PoseSpaceRetargeter.GetHierarchyPath(helperTransform)}, source={PoseSpaceRetargeter.GetHierarchyPath(sourceTransform)}, state=missing";
            }

            float currentDistance = Vector3.Distance(helperTransform.position, sourceTransform.position);
            float initialDistance = owner._initialThumbBaseHelperSourceDistances.TryGetValue(leftHand, out float storedDistance)
                ? storedDistance
                : float.NaN;
            float distanceDelta = IsFinite(initialDistance) && IsFinite(currentDistance)
                ? Mathf.Abs(currentDistance - initialDistance)
                : float.NaN;

            float relativeRotationDelta = float.NaN;
            if (owner._initialThumbBaseHelperSourceRelativeRotations.TryGetValue(leftHand, out Quaternion initialRelativeRotation))
            {
                Quaternion currentRelativeRotation = Quaternion.Inverse(sourceTransform.rotation) * helperTransform.rotation;
                relativeRotationDelta = Quaternion.Angle(initialRelativeRotation, currentRelativeRotation);
            }

            float risk = float.NaN;
            TryEvaluateManualOverrideRisk(owner, leftHand, out risk);
            float spreadDelta = float.NaN;
            float projectionDelta = float.NaN;
            TryEvaluateCurrentThumbReferenceFrameDelta(owner, leftHand, out spreadDelta, out projectionDelta);

            return
                $"side={sideLabel}, helper={PoseSpaceRetargeter.GetHierarchyPath(helperTransform)}, source={PoseSpaceRetargeter.GetHierarchyPath(sourceTransform)}, " +
                $"initDist={PoseSpaceRetargeter.FormatDebugFloat(initialDistance)}, currDist={PoseSpaceRetargeter.FormatDebugFloat(currentDistance)}, distDelta={PoseSpaceRetargeter.FormatDebugFloat(distanceDelta)}, " +
                $"relRotDelta={PoseSpaceRetargeter.FormatDebugFloat(relativeRotationDelta)}, risk={PoseSpaceRetargeter.FormatDebugFloat(risk)}, " +
                $"suppress={ShouldSuppressCompetingManualThumbOverride(owner, leftHand)}, keepDetached={ShouldKeepDetachedHelperManualThumbOverrides(owner, leftHand)}, " +
                $"spreadDelta={PoseSpaceRetargeter.FormatDebugFloat(spreadDelta)}, projectionDelta={PoseSpaceRetargeter.FormatDebugFloat(projectionDelta)}";
        }

        private bool ShouldRejectManualThumbBasePositionOverride(
            PoseSpaceRetargeter owner, bool leftHand, Transform targetThumb, Vector3 desiredWorldPosition)
        {
            if (targetThumb == null)
            {
                return false;
            }

            if (!TryEvaluateManualOverrideRisk(owner, leftHand, targetThumb.position, false, Vector3.zero, out float currentRisk) ||
                !TryEvaluateManualOverrideRisk(owner, leftHand, desiredWorldPosition, true, desiredWorldPosition, out float desiredRisk))
            {
                return false;
            }

            return desiredRisk >= PoseSpaceRetargeter.ManualThumbOverrideSuppressRiskThreshold &&
                desiredRisk > currentRisk + PoseSpaceRetargeter.ManualThumbOverrideRiskIncreaseTolerance;
        }

        public bool TryEvaluateManualOverrideRisk(
            PoseSpaceRetargeter owner, bool leftHand, out float risk)
        {
            Transform thumbProximal = owner.targetAnimator != null
                ? owner.targetAnimator.GetBoneTransform(leftHand ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal)
                : null;
            return TryEvaluateManualOverrideRisk(owner,
                leftHand,
                thumbProximal != null ? thumbProximal.position : Vector3.zero,
                false,
                Vector3.zero,
                out risk);
        }

        public bool TryEvaluateManualOverrideRisk(
            PoseSpaceRetargeter owner,
            bool leftHand,
            Vector3 thumbProximalWorldPosition,
            bool overrideHelperWorldPosition,
            Vector3 helperWorldPosition,
            out float risk)
        {
            risk = float.NaN;
            if (owner.targetAnimator == null ||
                !TryBuildThumbPalmFrame(owner.targetAnimator, leftHand, out ThumbPalmFrame targetFrame))
            {
                return false;
            }

            Transform hand = owner.targetAnimator.GetBoneTransform(leftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Transform index = owner.targetAnimator.GetBoneTransform(leftHand ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            Transform thumbIntermediate = owner.targetAnimator.GetBoneTransform(leftHand ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate);
            if (hand == null || index == null || thumbIntermediate == null)
            {
                return false;
            }

            Vector3 thumbDirection = thumbIntermediate.position - thumbProximalWorldPosition;
            Vector3 indexDirection = index.position - hand.position;
            if (!PoseSpaceRetargeter.TryNormalize(thumbDirection, out thumbDirection) ||
                !PoseSpaceRetargeter.TryNormalize(indexDirection, out indexDirection))
            {
                return false;
            }

            float spreadAngle = Vector3.Angle(thumbDirection, indexDirection);
            float spreadRisk = ThumbPoseRiskCalculator.CalculateAboveThreshold(
                spreadAngle,
                PoseSpaceRetargeter.ManualThumbOverrideSpreadWarningAngle,
                PoseSpaceRetargeter.ManualThumbOverrideSpreadFullRiskAngle);
            float projection = Vector3.Dot(thumbDirection, targetFrame.Normal);
            float projectionRisk = ThumbPoseRiskCalculator.CalculateOutsideRange(
                projection,
                PoseSpaceRetargeter.ManualThumbOverrideProjectionMin,
                PoseSpaceRetargeter.ManualThumbOverrideProjectionMax,
                1f);
            float helperSeparationRisk = float.NaN;
            float webbingRisk = float.NaN;
            if (TryEvaluateThumbHelperRelationshipRisk(owner,
                leftHand,
                overrideHelperWorldPosition,
                helperWorldPosition,
                spreadRisk,
                projectionRisk,
                out float helperDistanceRisk,
                out float helperRotationRisk,
                out float helperWebbingRisk))
            {
                helperSeparationRisk = ThumbPoseRiskCalculator.FindMaximumFinite(
                    helperDistanceRisk,
                    helperRotationRisk);
                webbingRisk = helperWebbingRisk;
            }

            risk = ThumbPoseRiskCalculator.FindMaximumFinite(
                spreadRisk,
                projectionRisk,
                helperSeparationRisk,
                webbingRisk);
            return !float.IsNaN(risk) && !float.IsInfinity(risk);
        }

        private bool TryEvaluateThumbHelperRelationshipRisk(
            PoseSpaceRetargeter owner,
            bool leftHand,
            bool overrideHelperWorldPosition,
            Vector3 helperWorldPosition,
            float spreadRisk,
            float projectionRisk,
            out float helperDistanceRisk,
            out float helperRotationRisk,
            out float webbingRisk)
        {
            helperDistanceRisk = float.NaN;
            helperRotationRisk = float.NaN;
            webbingRisk = float.NaN;

            Transform helperTransform = GetCachedThumbBaseHelper(owner, leftHand);
            Transform sourceTransform = GetCachedExplicitThumbBaseSource(owner, leftHand);
            if (helperTransform == null || sourceTransform == null)
            {
                return false;
            }

            EnsureThumbBaseHelperRelationshipBaseline(owner, leftHand, helperTransform, sourceTransform);
            if (!owner._initialThumbBaseHelperSourceDistances.TryGetValue(leftHand, out float initialDistance) ||
                !owner._initialThumbBaseHelperSourceRelativeRotations.TryGetValue(leftHand, out Quaternion initialRelativeRotation))
            {
                return false;
            }

            Vector3 effectiveHelperWorldPosition = overrideHelperWorldPosition ? helperWorldPosition : helperTransform.position;
            float currentDistance = Vector3.Distance(effectiveHelperWorldPosition, sourceTransform.position);

            Quaternion relativeRotation = Quaternion.Inverse(sourceTransform.rotation) * helperTransform.rotation;
            float rotationDelta = float.NaN;
            if (IsFinite(relativeRotation))
            {
                rotationDelta = Quaternion.Angle(initialRelativeRotation, relativeRotation);
            }

            return ThumbPoseRiskCalculator.TryCalculateHelperRelationshipRisk(
                currentDistance,
                initialDistance,
                rotationDelta,
                spreadRisk,
                projectionRisk,
                out helperDistanceRisk,
                out helperRotationRisk,
                out webbingRisk);
        }

        private void EnsureThumbBaseHelperRelationshipBaseline(
            PoseSpaceRetargeter owner, bool leftHand, Transform helperTransform, Transform sourceTransform)
        {
            if (!owner._initialThumbBaseHelperSourceDistances.ContainsKey(leftHand) ||
                !owner._initialThumbBaseHelperSourceRelativeRotations.ContainsKey(leftHand))
            {
                ThumbBaseHelperResolver.CaptureRelationshipBaseline(owner, leftHand, helperTransform, sourceTransform);
            }
        }

        public Transform GetCachedThumbBaseHelper(
            PoseSpaceRetargeter owner, bool leftHand)
        {
            if (owner._cachedThumbBaseHelpers.TryGetValue(leftHand, out Transform helperTransform) && helperTransform != null)
            {
                return helperTransform;
            }

            if (ThumbBaseHelperResolver.TryFindHelperCandidate(owner, leftHand, out helperTransform))
            {
                owner._cachedThumbBaseHelpers[leftHand] = helperTransform;
                return helperTransform;
            }

            return null;
        }

        public Transform GetCachedExplicitThumbBaseSource(
            PoseSpaceRetargeter owner, bool leftHand)
        {
            if (owner._cachedThumbBaseExplicitSources.TryGetValue(leftHand, out Transform sourceTransform) && sourceTransform != null)
            {
                return sourceTransform;
            }

            if (ThumbBaseHelperResolver.TryFindExplicitSource(owner, leftHand, out sourceTransform))
            {
                owner._cachedThumbBaseExplicitSources[leftHand] = sourceTransform;
                return sourceTransform;
            }

            return null;
        }

        private int AlignEditorHumanoidHandPalmFrame(
            PoseSpaceRetargeter owner, bool leftHand, float weight)
        {
            Transform targetHand = owner.targetAnimator.GetBoneTransform(leftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (targetHand == null)
            {
                return 0;
            }

            if (!TryBuildThumbPalmFrame(owner._editorFingerReferenceAnimator, leftHand, out ThumbPalmFrame referenceFrame) ||
                !TryBuildThumbPalmFrame(owner.targetAnimator, leftHand, out ThumbPalmFrame targetFrame))
            {
                return 0;
            }

            Vector3 referenceForwardLocal = owner._editorFingerReferenceAnimator.transform.InverseTransformDirection(referenceFrame.Forward).normalized;
            Vector3 referenceNormalLocal = owner._editorFingerReferenceAnimator.transform.InverseTransformDirection(referenceFrame.Normal).normalized;
            Vector3 desiredForward = owner.targetAnimator.transform.TransformDirection(referenceForwardLocal).normalized;
            Vector3 desiredNormal = owner.targetAnimator.transform.TransformDirection(referenceNormalLocal).normalized;
            if (!IsFinite(referenceForwardLocal) || !IsFinite(referenceNormalLocal) ||
                !IsFinite(desiredForward) || !IsFinite(desiredNormal))
            {
                return 0;
            }

            Quaternion currentFrameRotation = Quaternion.LookRotation(targetFrame.Forward, targetFrame.Normal);
            Quaternion desiredFrameRotation = Quaternion.LookRotation(desiredForward, desiredNormal);
            Quaternion correction = desiredFrameRotation * Quaternion.Inverse(currentFrameRotation);
            if (!IsFinite(currentFrameRotation) || !IsFinite(desiredFrameRotation) || !IsFinite(correction))
            {
                return 0;
            }

            if (weight < 0.999f)
            {
                correction = Quaternion.Slerp(Quaternion.identity, correction, weight);
            }

            Quaternion nextWorldRotation = correction * targetHand.rotation;
            if (!IsFinite(nextWorldRotation) || Quaternion.Angle(targetHand.rotation, nextWorldRotation) <= 0.001f)
            {
                return 0;
            }

            targetHand.rotation = nextWorldRotation;
            return 1;
        }

        private int AlignEditorHumanoidThumbSegmentDirection(
            PoseSpaceRetargeter owner, bool leftHand, HumanBodyBones parentBone, HumanBodyBones childBone, float weight)
        {
            Transform targetHand = owner.targetAnimator.GetBoneTransform(leftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Transform referenceHand = owner._editorFingerReferenceAnimator.GetBoneTransform(leftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Transform targetParent = owner.targetAnimator.GetBoneTransform(parentBone);
            Transform targetChild = owner.targetAnimator.GetBoneTransform(childBone);
            Transform referenceParent = owner._editorFingerReferenceAnimator.GetBoneTransform(parentBone);
            Transform referenceChild = owner._editorFingerReferenceAnimator.GetBoneTransform(childBone);

            if (targetHand == null || referenceHand == null ||
                targetParent == null || targetChild == null ||
                referenceParent == null || referenceChild == null)
            {
                return 0;
            }

            Vector3 targetSegment = targetChild.position - targetParent.position;
            Vector3 referenceSegment = referenceChild.position - referenceParent.position;
            if (!IsFinite(targetSegment) || !IsFinite(referenceSegment) ||
                targetSegment.sqrMagnitude <= 0.00000001f ||
                referenceSegment.sqrMagnitude <= 0.00000001f)
            {
                return 0;
            }

            if (!TryBuildThumbPalmFrame(owner._editorFingerReferenceAnimator, leftHand, out ThumbPalmFrame referenceFrame) ||
                !TryBuildThumbPalmFrame(owner.targetAnimator, leftHand, out ThumbPalmFrame targetFrame))
            {
                return 0;
            }

            Vector3 referenceHandDirection = referenceFrame.InverseTransformDirection(referenceSegment.normalized).normalized;
            Vector3 desiredWorldDirection = targetFrame.TransformDirection(referenceHandDirection).normalized;
            Vector3 currentWorldDirection = targetSegment.normalized;
            if (!IsFinite(referenceHandDirection) || !IsFinite(desiredWorldDirection) || !IsFinite(currentWorldDirection))
            {
                return 0;
            }

            Quaternion correction = Quaternion.FromToRotation(currentWorldDirection, desiredWorldDirection);
            if (!IsFinite(correction))
            {
                return 0;
            }

            if (weight < 0.999f)
            {
                correction = Quaternion.Slerp(Quaternion.identity, correction, weight);
            }

            Quaternion nextWorldRotation = correction * targetParent.rotation;
            if (!IsFinite(nextWorldRotation) || Quaternion.Angle(targetParent.rotation, nextWorldRotation) <= 0.001f)
            {
                return 0;
            }

            targetParent.rotation = nextWorldRotation;
            return 1;
        }

        private static bool TryBuildThumbPalmFrame(Animator animator, bool leftHand, out ThumbPalmFrame frame)
        {
            frame = default;
            if (animator == null)
            {
                return false;
            }

            Transform hand = animator.GetBoneTransform(leftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Transform thumb = animator.GetBoneTransform(leftHand ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
            Transform index = animator.GetBoneTransform(leftHand ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            Transform middle = animator.GetBoneTransform(leftHand ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            Transform little = animator.GetBoneTransform(leftHand ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);

            if (hand == null || thumb == null || index == null)
            {
                return false;
            }

            Vector3 origin = hand.position;
            Vector3 fingerCenter = Vector3.zero;
            int fingerCount = 0;
            AddPalmPoint(index, origin, ref fingerCenter, ref fingerCount);
            AddPalmPoint(middle, origin, ref fingerCenter, ref fingerCount);
            AddPalmPoint(little, origin, ref fingerCenter, ref fingerCount);
            if (fingerCount <= 0)
            {
                return false;
            }

            Vector3 forward = fingerCenter / fingerCount - origin;
            if (!PoseSpaceRetargeter.TryNormalize(forward, out forward))
            {
                forward = index.position - origin;
                if (!PoseSpaceRetargeter.TryNormalize(forward, out forward))
                {
                    return false;
                }
            }

            Vector3 side = Vector3.zero;
            if (little != null)
            {
                side = index.position - little.position;
            }

            side = Vector3.ProjectOnPlane(side, forward);
            if (!PoseSpaceRetargeter.TryNormalize(side, out side))
            {
                side = Vector3.ProjectOnPlane(thumb.position - origin, forward);
                if (!PoseSpaceRetargeter.TryNormalize(side, out side))
                {
                    side = Vector3.ProjectOnPlane(hand.right, forward);
                    if (!PoseSpaceRetargeter.TryNormalize(side, out side))
                    {
                        return false;
                    }
                }
            }

            Vector3 thumbSide = Vector3.ProjectOnPlane(thumb.position - origin, forward);
            if (PoseSpaceRetargeter.TryNormalize(thumbSide, out thumbSide) && Vector3.Dot(side, thumbSide) < 0f)
            {
                side = -side;
            }

            Vector3 normal = Vector3.Cross(side, forward);
            if (!PoseSpaceRetargeter.TryNormalize(normal, out normal))
            {
                return false;
            }

            side = Vector3.Cross(forward, normal);
            if (!PoseSpaceRetargeter.TryNormalize(side, out side))
            {
                return false;
            }

            frame = new ThumbPalmFrame
            {
                Origin = origin,
                Side = side,
                Normal = normal,
                Forward = forward,
                Scale = CalculatePalmScale(origin, index, middle, little)
            };

            return frame.Scale > 0.0001f &&
                IsFinite(frame.Origin) &&
                IsFinite(frame.Side) &&
                IsFinite(frame.Normal) &&
                IsFinite(frame.Forward);
        }

        private static void AddPalmPoint(Transform point, Vector3 origin, ref Vector3 sum, ref int count)
        {
            if (point == null)
            {
                return;
            }

            Vector3 delta = point.position - origin;
            if (!IsFinite(delta) || delta.sqrMagnitude <= 0.00000001f)
            {
                return;
            }

            sum += point.position;
            count++;
        }

        private static float CalculatePalmScale(Vector3 origin, params Transform[] points)
        {
            float sum = 0f;
            int count = 0;
            foreach (Transform point in points)
            {
                if (point == null)
                {
                    continue;
                }

                float distance = Vector3.Distance(origin, point.position);
                if (!IsFinite(distance) || distance <= 0.0001f)
                {
                    continue;
                }

                sum += distance;
                count++;
            }

            return count > 0 ? sum / count : 0f;
        }

        public bool TryEvaluateManualReferenceFrameDeviation(
            PoseSpaceRetargeter owner,
            bool leftHand,
            Transform targetThumb,
            Quaternion candidateWorldRotation,
            out float currentDeviation,
            out float candidateDeviation)
        {
            currentDeviation = float.NaN;
            candidateDeviation = float.NaN;

            if (owner._editorFingerReferenceAnimator == null ||
                owner.targetAnimator == null ||
                targetThumb == null ||
                !IsFinite(candidateWorldRotation) ||
                !TryBuildThumbPalmFrame(owner._editorFingerReferenceAnimator, leftHand, out ThumbPalmFrame referenceFrame) ||
                !TryBuildThumbPalmFrame(owner.targetAnimator, leftHand, out ThumbPalmFrame targetFrame))
            {
                return false;
            }

            if (!TryEvaluateThumbReferenceFrameMetrics(owner, owner._editorFingerReferenceAnimator, leftHand, referenceFrame, out float referenceSpread, out float referenceProjection) ||
                !TryEvaluateThumbReferenceFrameMetrics(owner, owner.targetAnimator, leftHand, targetFrame, out float currentSpread, out float currentProjection))
            {
                return false;
            }

            currentDeviation = ThumbPoseRiskCalculator.CalculateReferenceFrameDeviation(
                currentSpread,
                currentProjection,
                referenceSpread,
                referenceProjection);

            Quaternion originalWorldRotation = targetThumb.rotation;
            targetThumb.rotation = candidateWorldRotation;
            try
            {
                if (!TryEvaluateThumbReferenceFrameMetrics(owner, owner.targetAnimator, leftHand, targetFrame, out float candidateSpread, out float candidateProjection))
                {
                    return false;
                }

                candidateDeviation = ThumbPoseRiskCalculator.CalculateReferenceFrameDeviation(
                    candidateSpread,
                    candidateProjection,
                    referenceSpread,
                    referenceProjection);
            }
            finally
            {
                targetThumb.rotation = originalWorldRotation;
            }

            return IsFinite(currentDeviation) && IsFinite(candidateDeviation);
        }

        private static bool TryEvaluateThumbReferenceFrameMetrics(
            PoseSpaceRetargeter owner,
            Animator animator,
            bool leftHand,
            ThumbPalmFrame frame,
            out float spreadAngle,
            out float projection)
        {
            spreadAngle = float.NaN;
            projection = float.NaN;

            if (animator == null)
            {
                return false;
            }

            Transform hand = animator.GetBoneTransform(leftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Transform index = animator.GetBoneTransform(leftHand ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            Transform proximal = animator.GetBoneTransform(leftHand ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
            Transform intermediate = animator.GetBoneTransform(leftHand ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate);
            if (hand == null || index == null || proximal == null || intermediate == null)
            {
                return false;
            }

            Vector3 thumbDirection = intermediate.position - proximal.position;
            Vector3 indexDirection = index.position - hand.position;
            if (!PoseSpaceRetargeter.TryNormalize(thumbDirection, out thumbDirection) ||
                !PoseSpaceRetargeter.TryNormalize(indexDirection, out indexDirection))
            {
                return false;
            }

            spreadAngle = Vector3.Angle(thumbDirection, indexDirection);
            projection = Vector3.Dot(thumbDirection, frame.Normal);
            return IsFinite(spreadAngle) && IsFinite(projection);
        }

        public bool TryEvaluateCurrentThumbReferenceFrameDelta(
            PoseSpaceRetargeter owner,
            bool leftHand,
            out float spreadDelta,
            out float projectionDelta)
        {
            spreadDelta = float.NaN;
            projectionDelta = float.NaN;

            if (owner._editorFingerReferenceAnimator == null ||
                owner.targetAnimator == null ||
                !TryBuildThumbPalmFrame(owner._editorFingerReferenceAnimator, leftHand, out ThumbPalmFrame referenceFrame) ||
                !TryBuildThumbPalmFrame(owner.targetAnimator, leftHand, out ThumbPalmFrame targetFrame) ||
                !TryEvaluateThumbReferenceFrameMetrics(owner, owner._editorFingerReferenceAnimator, leftHand, referenceFrame, out float referenceSpread, out float referenceProjection) ||
                !TryEvaluateThumbReferenceFrameMetrics(owner, owner.targetAnimator, leftHand, targetFrame, out float currentSpread, out float currentProjection))
            {
                return false;
            }

            spreadDelta = Mathf.Abs(currentSpread - referenceSpread);
            projectionDelta = Mathf.Abs(currentProjection - referenceProjection);
            return IsFinite(spreadDelta) && IsFinite(projectionDelta);
        }

        public bool TryEvaluateThumbLocalRotationOverrideRisk(
            PoseSpaceRetargeter owner,
            bool leftHand,
            Transform targetThumb,
            Quaternion candidateRotation,
            out float risk)
        {
            risk = float.NaN;
            if (targetThumb == null || !IsFinite(candidateRotation))
            {
                return false;
            }

            Quaternion originalRotation = targetThumb.localRotation;
            targetThumb.localRotation = candidateRotation;
            try
            {
                return TryEvaluateManualOverrideRisk(owner, leftHand, out risk);
            }
            finally
            {
                targetThumb.localRotation = originalRotation;
            }
        }

        private struct ThumbPalmFrame
        {
            public Vector3 Origin;
            public Vector3 Side;
            public Vector3 Normal;
            public Vector3 Forward;
            public float Scale;

            public Vector3 InverseTransformPoint(Vector3 worldPoint)
            {
                Vector3 delta = worldPoint - Origin;
                return new Vector3(
                    Vector3.Dot(delta, Side),
                    Vector3.Dot(delta, Normal),
                    Vector3.Dot(delta, Forward));
            }

            public Vector3 TransformPoint(Vector3 localPoint)
            {
                return Origin +
                    Side * localPoint.x +
                    Normal * localPoint.y +
                    Forward * localPoint.z;
            }

            public Vector3 InverseTransformDirection(Vector3 worldDirection)
            {
                return new Vector3(
                    Vector3.Dot(worldDirection, Side),
                    Vector3.Dot(worldDirection, Normal),
                    Vector3.Dot(worldDirection, Forward));
            }

            public Vector3 TransformDirection(Vector3 localDirection)
            {
                return Side * localDirection.x +
                    Normal * localDirection.y +
                    Forward * localDirection.z;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }
    }
}
