using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(CorrectionPassOrder.ThumbDeformation)]
    public partial class HumanoidThumbDeformationGuard : MonoBehaviour
    {


        private void Awake()
        {
            InitializeIfNeeded();
        }

        private void OnEnable()
        {
            InitializeIfNeeded();
        }

        private void LateUpdate()
        {
            if (!InitializeIfNeeded())
            {
                return;
            }

            int changed = 0;
            if (clampHumanoidThumbRotations)
            {
                changed += ClampThumbBoneRotations();
            }

            if (enableThumbVisualLengthGuard)
            {
                changed += PreserveThumbVisualLength();
            }

            changed += SyncDetachedThumbBaseHelperTransforms();

            _lastFrameCorrectionCount = changed;
            _correctionsAppliedTotal += changed;
            if (changed > 0)
            {
                _framesWithCorrections++;
            }

            if (changed > 0 && logCorrections && !_warningLogged)
            {
                Debug.LogWarning($"[HumanoidThumbDeformationGuard] 엄지 본 localRotation {changed}개를 최종 렌더 포즈에서 제한했습니다.");
                _warningLogged = true;
            }
        }

        private int ClampThumbBoneRotations()
        {
            int changed = 0;
            foreach (HumanBodyBones thumbBone in ThumbBones)
            {
                Transform thumbTransform = targetAnimator.GetBoneTransform(thumbBone);
                if (thumbTransform == null ||
                    !_initialLocalRotations.TryGetValue(thumbTransform, out Quaternion initialRotation))
                {
                    continue;
                }

                float limit = GetLimit(thumbBone);
                Quaternion rawRotation = GetCurrentRawLocalRotation(thumbTransform);
                if (!IsFinite(rawRotation))
                {
                    SetCorrectedLocalRotation(thumbTransform, initialRotation, initialRotation);
                    changed++;
                    continue;
                }

                Quaternion offsetRotation = GetThumbRotationOffset(thumbBone);
                if (!ThumbLocalRotationCalculator.TryCalculateCorrection(
                        initialRotation,
                        rawRotation,
                        thumbTransform.localRotation,
                        offsetRotation,
                        limit,
                        out Quaternion correctedRotation))
                {
                    continue;
                }

                SetCorrectedLocalRotation(thumbTransform, rawRotation, correctedRotation);
                changed++;
            }

            changed += ClampThumbBaseHelperTransforms();
            return changed;
        }

        public void Configure(
            Animator animator,
            PoseSpaceRetargeter poseSpaceRetargeter,
            float proximalLimit,
            float intermediateLimit,
            float distalLimit,
            Vector3 proximalOffset,
            bool mirrorRightOffset,
            Vector3 leftProximalOffset,
            Vector3 rightProximalOffset,
            bool logCorrectionMessages,
            bool clampHumanoidRotations = true,
            bool syncDetachedBaseHelpers = true,
            bool syncDetachedBaseHelperPositions = true,
            float detachedBaseHelperSyncWeight = 1f,
            float detachedBaseHelperMaxLocalAngle = 45f,
            float detachedBaseHelperMaxPositionOffset = 0.008f,
            Vector3 leftDetachedBaseHelperDeltaAxisOffset = default,
            Vector3 rightDetachedBaseHelperDeltaAxisOffset = default,
            Vector3 leftDetachedBaseHelperTargetRotationOffset = default,
            Vector3 rightDetachedBaseHelperTargetRotationOffset = default,
            bool stabilizeDetachedBasePalm = false,
            float detachedBasePalmStabilizeWeight = 0f,
            float detachedBasePalmMaxLocalAngle = 45f,
            bool enableVisualLengthGuard = true,
            float projectionMinPalmNormal = 0.32f,
            float projectionMaxPalmNormal = 0.58f,
            float projectionGuardWeight = 0.9f,
            float indexMaxSpreadAngle = 70f,
            float indexSpreadGuardWeight = 0.9f,
            float maxSegmentBendAngle = 10f,
            float segmentStraightenWeight = 0.9f,
            bool suppressPoseShapingWithManualReference = false,
            bool stabilizeWebbingCrease = true,
            float webbingCreaseStabilizeWeight = 0.35f,
            float webbingCreaseMaxLocalAngle = 18f,
            float webbingCreaseMaxPositionOffset = 0.005f)
        {
            ConfigureBase(
                animator, poseSpaceRetargeter, proximalLimit, intermediateLimit, distalLimit,
                proximalOffset, mirrorRightOffset, leftProximalOffset, rightProximalOffset,
                logCorrectionMessages, clampHumanoidRotations, suppressPoseShapingWithManualReference);
            ConfigureDetachedHelpers(
                syncDetachedBaseHelpers, syncDetachedBaseHelperPositions, detachedBaseHelperSyncWeight,
                detachedBaseHelperMaxLocalAngle, detachedBaseHelperMaxPositionOffset,
                leftDetachedBaseHelperDeltaAxisOffset, rightDetachedBaseHelperDeltaAxisOffset,
                leftDetachedBaseHelperTargetRotationOffset, rightDetachedBaseHelperTargetRotationOffset,
                stabilizeDetachedBasePalm, detachedBasePalmStabilizeWeight, detachedBasePalmMaxLocalAngle,
                stabilizeWebbingCrease, webbingCreaseStabilizeWeight,
                webbingCreaseMaxLocalAngle, webbingCreaseMaxPositionOffset);
            ConfigureThumbGuards(
                enableVisualLengthGuard, projectionMinPalmNormal, projectionMaxPalmNormal,
                projectionGuardWeight, indexMaxSpreadAngle, indexSpreadGuardWeight,
                maxSegmentBendAngle, segmentStraightenWeight, suppressPoseShapingWithManualReference);
            RecaptureBaseline();
        }
        }

        private void ConfigureBase(
            Animator animator,
            PoseSpaceRetargeter poseSpaceRetargeter,
            float proximalLimit,
            float intermediateLimit,
            float distalLimit,
            Vector3 proximalOffset,
            bool mirrorRightOffset,
            Vector3 leftProximalOffset,
            Vector3 rightProximalOffset,
            bool logCorrectionMessages,
            bool clampHumanoidRotations,
            bool suppressPoseShapingWithManualReference)
        {
            targetAnimator = animator;
            linkedPoseSpaceRetargeter = poseSpaceRetargeter;
            proximalMaxLocalAngle = Mathf.Clamp(
                suppressPoseShapingWithManualReference
                    ? Mathf.Max(proximalLimit, ManualThumbReferenceMinProximalMaxLocalAngle)
                    : proximalLimit,
                0f,
                90f);
            intermediateMaxLocalAngle = Mathf.Clamp(intermediateLimit, 0f, 120f);
            distalMaxLocalAngle = Mathf.Clamp(distalLimit, 0f, 120f);
            proximalLocalRotationOffset = proximalOffset;
            mirrorRightProximalLocalRotationOffset = mirrorRightOffset;
            leftProximalLocalRotationOffset = leftProximalOffset;
            rightProximalLocalRotationOffset = rightProximalOffset;
            logCorrections = logCorrectionMessages;
            clampHumanoidThumbRotations = clampHumanoidRotations;
        }

        private void ConfigureDetachedHelpers(
            bool syncDetachedBaseHelpers,
            bool syncDetachedBaseHelperPositions,
            float detachedBaseHelperSyncWeight,
            float detachedBaseHelperMaxLocalAngle,
            float detachedBaseHelperMaxPositionOffset,
            Vector3 leftDetachedBaseHelperDeltaAxisOffset,
            Vector3 rightDetachedBaseHelperDeltaAxisOffset,
            Vector3 leftDetachedBaseHelperTargetRotationOffset,
            Vector3 rightDetachedBaseHelperTargetRotationOffset,
            bool stabilizeDetachedBasePalm,
            float detachedBasePalmStabilizeWeight,
            float detachedBasePalmMaxLocalAngle,
            bool stabilizeWebbingCrease,
            float webbingCreaseStabilizeWeight,
            float webbingCreaseMaxLocalAngle,
            float webbingCreaseMaxPositionOffset)
        {
            syncDetachedThumbBaseHelpers = syncDetachedBaseHelpers;
            syncDetachedThumbBaseHelperPositions = syncDetachedBaseHelperPositions;
            detachedThumbBaseHelperSyncWeight = Mathf.Clamp01(detachedBaseHelperSyncWeight);
            detachedThumbBaseHelperMaxLocalAngle = Mathf.Clamp(detachedBaseHelperMaxLocalAngle, 0f, 45f);
            detachedThumbBaseHelperMaxPositionOffset = Mathf.Clamp(detachedBaseHelperMaxPositionOffset, 0f, 0.02f);
            leftDetachedThumbBaseHelperDeltaAxisOffset = leftDetachedBaseHelperDeltaAxisOffset;
            rightDetachedThumbBaseHelperDeltaAxisOffset = rightDetachedBaseHelperDeltaAxisOffset;
            leftDetachedThumbBaseHelperTargetRotationOffset = leftDetachedBaseHelperTargetRotationOffset;
            rightDetachedThumbBaseHelperTargetRotationOffset = rightDetachedBaseHelperTargetRotationOffset;
            stabilizeDetachedThumbBasePalm = stabilizeDetachedBasePalm;
            detachedThumbBasePalmStabilizeWeight = Mathf.Clamp01(detachedBasePalmStabilizeWeight);
            detachedThumbBasePalmMaxLocalAngle = Mathf.Clamp(detachedBasePalmMaxLocalAngle, 0f, 45f);
            stabilizeThumbWebbingCrease = stabilizeWebbingCrease;
            thumbWebbingCreaseStabilizeWeight = Mathf.Clamp01(webbingCreaseStabilizeWeight);
            thumbWebbingCreaseMaxLocalAngle = Mathf.Clamp(webbingCreaseMaxLocalAngle, 0f, 45f);
            thumbWebbingCreaseMaxPositionOffset = Mathf.Clamp(webbingCreaseMaxPositionOffset, 0f, 0.02f);
        }

        private void ConfigureThumbGuards(
            bool enableVisualLengthGuard,
            float projectionMinPalmNormal,
            float projectionMaxPalmNormal,
            float projectionGuardWeight,
            float indexMaxSpreadAngle,
            float indexSpreadGuardWeight,
            float maxSegmentBendAngle,
            float segmentStraightenWeight,
            bool suppressPoseShapingWithManualReference)
        {
            enableThumbVisualLengthGuard = enableVisualLengthGuard;
            thumbProjectionMinPalmNormal = Mathf.Clamp01(projectionMinPalmNormal);
            thumbProjectionMaxPalmNormal = Mathf.Clamp01(projectionMaxPalmNormal);
            thumbProjectionGuardWeight = Mathf.Clamp01(projectionGuardWeight);
            thumbIndexMaxSpreadAngle = Mathf.Clamp(indexMaxSpreadAngle, 0f, 90f);
            thumbIndexSpreadGuardWeight = Mathf.Clamp01(indexSpreadGuardWeight);
            thumbMaxSegmentBendAngle = Mathf.Clamp(maxSegmentBendAngle, 0f, 60f);
            thumbSegmentStraightenWeight = Mathf.Clamp01(segmentStraightenWeight);
            suppressPoseShapingWithManualThumbReference = suppressPoseShapingWithManualReference;
        }

        public void RecaptureBaseline()
        {
            _initialLocalRotations.Clear();
            _initialLocalPositions.Clear();
            _thumbBaseHelperTransforms.Clear();
            _detachedThumbBaseHelperSources.Clear();
            _detachedThumbBaseSourceInitialLocalRotations.Clear();
            _detachedThumbBaseInitialRelativeRotations.Clear();
            _detachedThumbBaseInitialDistances.Clear();
            _lastRawLocalRotations.Clear();
            _lastCorrectedLocalRotations.Clear();
            _cachedThumbSides.Clear();
            _warningLogged = false;
            _lastFrameCorrectionCount = 0;
            _correctionsAppliedTotal = 0;
            _framesWithCorrections = 0;

            if (!InitializeIfNeeded())
            {
                return;
            }

            foreach (HumanBodyBones thumbBone in ThumbBones)
            {
                Transform thumbTransform = targetAnimator.GetBoneTransform(thumbBone);
                if (thumbTransform == null || _initialLocalRotations.ContainsKey(thumbTransform))
                {
                    continue;
                }

                _initialLocalRotations[thumbTransform] = thumbTransform.localRotation;
                _initialLocalPositions[thumbTransform] = thumbTransform.localPosition;
                CacheThumbSide(thumbTransform, IsRightHumanThumbBone(thumbBone));
            }

            CaptureThumbBaseHelperRotations();
            CaptureDetachedThumbBaseHelperSources();
        }

        private bool InitializeIfNeeded()
        {
            if (targetAnimator == null)
            {
                targetAnimator = GetComponent<Animator>();
            }

            ResolveLinkedPoseSpaceRetargeter();

            return targetAnimator != null &&
                targetAnimator.avatar != null &&
                targetAnimator.avatar.isValid &&
                targetAnimator.avatar.isHuman;
        }

        private bool ShouldSuppressPoseShapingWithManualThumbReference(bool isRightThumb)
        {
            ResolveLinkedPoseSpaceRetargeter();
            return suppressPoseShapingWithManualThumbReference &&
                linkedPoseSpaceRetargeter != null &&
                linkedPoseSpaceRetargeter.ShouldSuppressThumbPoseShapingGuardForHand(!isRightThumb);
        }

        private PoseSpaceRetargeter ResolveLinkedPoseSpaceRetargeter()
        {
            if (linkedPoseSpaceRetargeter != null &&
                (targetAnimator == null || linkedPoseSpaceRetargeter.targetAnimator == targetAnimator))
            {
                return linkedPoseSpaceRetargeter;
            }

            if (targetAnimator != null)
            {
                PoseSpaceRetargeter[] retargeters = FindObjectsByType<PoseSpaceRetargeter>(FindObjectsSortMode.InstanceID);
                foreach (PoseSpaceRetargeter candidate in retargeters)
                {
                    if (candidate != null && candidate.targetAnimator == targetAnimator)
                    {
                        linkedPoseSpaceRetargeter = candidate;
                        return linkedPoseSpaceRetargeter;
                    }
                }
            }

            if (linkedPoseSpaceRetargeter == null)
            {
                linkedPoseSpaceRetargeter = GetComponent<PoseSpaceRetargeter>();
            }

            return linkedPoseSpaceRetargeter;
        }

        private float GetLimit(HumanBodyBones thumbBone)
        {
            switch (thumbBone)
            {
                case HumanBodyBones.LeftThumbProximal:
                case HumanBodyBones.RightThumbProximal:
                    return proximalMaxLocalAngle;
                case HumanBodyBones.LeftThumbIntermediate:
                case HumanBodyBones.RightThumbIntermediate:
                    return intermediateMaxLocalAngle;
                case HumanBodyBones.LeftThumbDistal:
                case HumanBodyBones.RightThumbDistal:
                    return distalMaxLocalAngle;
                default:
                    return 0f;
            }
        }

        private int ClampThumbBaseHelperTransforms()
        {
            int changed = 0;
            foreach (Transform thumbTransform in _thumbBaseHelperTransforms)
            {
                if (thumbTransform == null ||
                    !_initialLocalRotations.TryGetValue(thumbTransform, out Quaternion initialRotation))
                {
                    continue;
                }

                Quaternion rawRotation = GetCurrentRawLocalRotation(thumbTransform);
                if (!IsFinite(rawRotation))
                {
                    SetCorrectedLocalRotation(thumbTransform, initialRotation, initialRotation);
                    changed++;
                    continue;
                }

                Quaternion offsetRotation = GetProximalRotationOffsetRotation(thumbTransform);
                float limit = proximalMaxLocalAngle;
                if (!ThumbLocalRotationCalculator.TryCalculateCorrection(
                        initialRotation,
                        rawRotation,
                        thumbTransform.localRotation,
                        offsetRotation,
                        limit,
                        out Quaternion correctedRotation))
                {
                    continue;
                }

                SetCorrectedLocalRotation(thumbTransform, rawRotation, correctedRotation);
                changed++;
            }

            return changed;
        }

        private int PreserveThumbVisualLength()
        {
            ResetThumbPoseShapingDiagnostics();

            bool leftInactive =
                (GetEffectiveThumbProjectionGuardWeight(false) <= 0f || (thumbProjectionMinPalmNormal <= 0.001f && thumbProjectionMaxPalmNormal >= 0.999f)) &&
                (GetEffectiveThumbIndexSpreadGuardWeight(false) <= 0f || thumbIndexMaxSpreadAngle >= 89.999f) &&
                (GetEffectiveThumbSegmentStraightenWeight(false) <= 0f || thumbMaxSegmentBendAngle >= 59.999f);
            bool rightInactive =
                (GetEffectiveThumbProjectionGuardWeight(true) <= 0f || (thumbProjectionMinPalmNormal <= 0.001f && thumbProjectionMaxPalmNormal >= 0.999f)) &&
                (GetEffectiveThumbIndexSpreadGuardWeight(true) <= 0f || thumbIndexMaxSpreadAngle >= 89.999f) &&
                (GetEffectiveThumbSegmentStraightenWeight(true) <= 0f || thumbMaxSegmentBendAngle >= 59.999f);

            if (leftInactive && rightInactive)
            {
                return 0;
            }

            return PreserveThumbVisualLength(false) + PreserveThumbVisualLength(true);
        }

        private int PreserveThumbVisualLength(bool isRightThumb)
        {
            Transform proximal = targetAnimator.GetBoneTransform(
                isRightThumb ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal);
            Transform intermediate = targetAnimator.GetBoneTransform(
                isRightThumb ? HumanBodyBones.RightThumbIntermediate : HumanBodyBones.LeftThumbIntermediate);
            Transform distal = targetAnimator.GetBoneTransform(
                isRightThumb ? HumanBodyBones.RightThumbDistal : HumanBodyBones.LeftThumbDistal);

            if (proximal == null || intermediate == null)
            {
                return 0;
            }

            int changed = ProjectThumbProximalIntoPalmFrame(proximal, intermediate, isRightThumb);
            if (distal != null)
            {
                changed += StraightenThumbSegmentBend(proximal, intermediate, distal, isRightThumb);
            }

            return changed;
        }

        private int ProjectThumbProximalIntoPalmFrame(Transform proximal, Transform intermediate, bool isRightThumb)
        {
            if (!TryBuildPalmFrame(isRightThumb, out Vector3 sideAxis, out Vector3 palmNormal, out Vector3 forwardAxis))
            {
                return 0;
            }

            ThumbProjectionLimits limits = ResolveProjectionLimits(isRightThumb);
            if (!TryResolveThumbDirections(
                    proximal, intermediate, isRightThumb,
                    out Vector3 direction, out Vector3 indexDirection, out bool hasIndexDirection))
            {
                return 0;
            }

            if (!ThumbPoseDirectionCalculator.TryCalculateCorrectedDirection(
                    direction,
                    indexDirection,
                    hasIndexDirection,
                    sideAxis,
                    palmNormal,
                    forwardAxis,
                    limits.MinNormal,
                    limits.MaxNormal,
                    limits.MaxSpreadAngle,
                    limits.IndexSpreadGuardWeight,
                    limits.ProjectionGuardWeight,
                    out Vector3 targetDirection))
            {
                return 0;
            }

            // 방향 차이만 proximal 월드 회전에 얹고, 본 길이나 위치는 직접 바꾸지 않습니다.
            bool bypassManualProjectionPreserve =
                limits.UseHighRiskManualOverride &&
                ShouldBypassManualThumbProjectionPreserveForSmokeRisk(
                    Vector3.Dot(direction, palmNormal),
                    limits.MinNormal,
                    limits.MaxNormal);

            Quaternion correctedWorldRotation = Quaternion.FromToRotation(direction, targetDirection) * proximal.rotation;
            HumanBodyBones proximalBone = isRightThumb ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal;
            if (!bypassManualProjectionPreserve &&
                ShouldPreserveManualThumbWorldRotationCorrection(proximalBone, proximal, correctedWorldRotation))
            {
                RecordThumbProjectionCorrection(isRightThumb, true);
                return 0;
            }

            ApplyWorldRotationCorrection(proximal, correctedWorldRotation);
            RecordThumbProjectionCorrection(isRightThumb, false);
            return 1;
        }

        private bool TryResolveThumbDirections(
            Transform proximal,
            Transform intermediate,
            bool isRightThumb,
            out Vector3 direction,
            out Vector3 indexDirection,
            out bool hasIndexDirection)
        {
            Transform hand = targetAnimator.GetBoneTransform(isRightThumb ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            Transform index = targetAnimator.GetBoneTransform(isRightThumb ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
            direction = intermediate.position - proximal.position;
            if (!TryNormalize(direction, out direction))
            {
                indexDirection = Vector3.zero;
                hasIndexDirection = false;
                return false;
            }

            indexDirection = Vector3.zero;
            hasIndexDirection =
                hand != null &&
                index != null &&
                TryNormalize(index.position - hand.position, out indexDirection);
            return true;
        }

        private struct ThumbProjectionLimits
        {
            internal float MinNormal;
            internal float MaxNormal;
            internal float MaxSpreadAngle;
            internal bool UseHighRiskManualOverride;
            internal float IndexSpreadGuardWeight;
            internal float ProjectionGuardWeight;
        }

        private ThumbProjectionLimits ResolveProjectionLimits(bool isRightThumb)
        {
            var limits = new ThumbProjectionLimits
            {
                MinNormal = Mathf.Clamp01(thumbProjectionMinPalmNormal),
                IndexSpreadGuardWeight = GetEffectiveThumbIndexSpreadGuardWeight(isRightThumb),
                ProjectionGuardWeight = GetEffectiveThumbProjectionGuardWeight(isRightThumb)
            };
            limits.MaxNormal = Mathf.Clamp(Mathf.Max(thumbProjectionMaxPalmNormal, limits.MinNormal), 0f, 1f);
            float configuredMaxSpreadAngle = Mathf.Clamp(thumbIndexMaxSpreadAngle, 0f, 90f);
            limits.MaxSpreadAngle = configuredMaxSpreadAngle;
            ResolveLinkedPoseSpaceRetargeter();
            // 수동 기준 포즈에서 위험한 엄지 자세로 판정되면 이 메서드의 제한값을 더 강하게 적용합니다.
            if (linkedPoseSpaceRetargeter != null &&
                linkedPoseSpaceRetargeter.TryGetHighRiskManualThumbPoseConstraintOverrides(
                    !isRightThumb,
                    out float overrideMinNormal,
                    out float overrideMaxNormal,
                    out float overrideMaxSpreadAngle))
            {
                limits.UseHighRiskManualOverride = true;
                limits.MinNormal = Mathf.Clamp01(overrideMinNormal);
                limits.MaxNormal = Mathf.Clamp(Mathf.Max(overrideMaxNormal, limits.MinNormal), 0f, 1f);
                limits.MaxSpreadAngle = ResolveManualOverrideMaxSpreadAngle(configuredMaxSpreadAngle, overrideMaxSpreadAngle);
                limits.IndexSpreadGuardWeight = Mathf.Max(limits.IndexSpreadGuardWeight, 1f);
                limits.ProjectionGuardWeight = Mathf.Max(limits.ProjectionGuardWeight, 1f);
            }
            return limits;
        }

        private static float ResolveManualOverrideMaxSpreadAngle(float configuredMaxSpreadAngle, float overrideMaxSpreadAngle)
        {
            float configured = Mathf.Clamp(configuredMaxSpreadAngle, 0f, 90f);
            float overrideValue = Mathf.Clamp(overrideMaxSpreadAngle, 0f, 90f);
            return Mathf.Min(configured, overrideValue);
        }

        private static bool ShouldBypassManualThumbProjectionPreserveForSmokeRisk(
            float currentProjection,
            float minNormal,
            float maxNormal)
        {
            float minValue = Mathf.Clamp01(minNormal);
            float maxValue = Mathf.Clamp(Mathf.Max(maxNormal, minValue), 0f, 1f);
            float risk = ThumbPoseRiskCalculator.CalculateOutsideRange(
                currentProjection,
                minValue,
                maxValue,
                1f);
            return IsFinite(risk) && risk > ManualThumbProjectionPreserveSmokeRiskLimit;
        }

        private int StraightenThumbSegmentBend(Transform proximal, Transform intermediate, Transform distal, bool isRightThumb)
        {
            float segmentStraightenWeight = GetEffectiveThumbSegmentStraightenWeight(isRightThumb);
            if (segmentStraightenWeight <= 0f || thumbMaxSegmentBendAngle >= 59.999f)
            {
                return 0;
            }

            Vector3 proximalDirection = intermediate.position - proximal.position;
            Vector3 intermediateDirection = distal.position - intermediate.position;
            if (!TryNormalize(proximalDirection, out proximalDirection) ||
                !TryNormalize(intermediateDirection, out intermediateDirection))
            {
                return 0;
            }

            if (!ThumbPoseDirectionCalculator.TryCalculateStraightenedDirection(
                    proximalDirection,
                    intermediateDirection,
                    thumbMaxSegmentBendAngle,
                    segmentStraightenWeight,
                    out Vector3 targetDirection))
            {
                return 0;
            }

            Quaternion correctedWorldRotation = Quaternion.FromToRotation(intermediateDirection, targetDirection) * intermediate.rotation;
            HumanBodyBones intermediateBone = isRightThumb ? HumanBodyBones.RightThumbIntermediate : HumanBodyBones.LeftThumbIntermediate;
            if (ShouldPreserveManualThumbWorldRotationCorrection(intermediateBone, intermediate, correctedWorldRotation))
            {
                RecordThumbSegmentStraightenCorrection(isRightThumb, true);
                return 0;
            }

            ApplyWorldRotationCorrection(intermediate, correctedWorldRotation);
            RecordThumbSegmentStraightenCorrection(isRightThumb, false);
            return 1;
        }

        private bool ShouldPreserveManualThumbWorldRotationCorrection(
            HumanBodyBones thumbBone,
            Transform targetTransform,
            Quaternion correctedWorldRotation)
        {
            ResolveLinkedPoseSpaceRetargeter();
            return linkedPoseSpaceRetargeter != null &&
                linkedPoseSpaceRetargeter.ShouldPreserveManualThumbWorldRotationCorrection(
                    thumbBone,
                    targetTransform,
                    correctedWorldRotation);
        }

        private bool TryCalculateThumbAndIndexDirections(
            bool isRightThumb,
            out Vector3 thumbDirection,
            out Vector3 indexDirection)
        {
            thumbDirection = Vector3.zero;
            indexDirection = Vector3.zero;

            Transform hand = targetAnimator.GetBoneTransform(isRightThumb ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            Transform thumbProximal = targetAnimator.GetBoneTransform(
                isRightThumb ? HumanBodyBones.RightThumbProximal : HumanBodyBones.LeftThumbProximal);
            Transform thumbIntermediate = targetAnimator.GetBoneTransform(
                isRightThumb ? HumanBodyBones.RightThumbIntermediate : HumanBodyBones.LeftThumbIntermediate);
            Transform indexProximal = targetAnimator.GetBoneTransform(
                isRightThumb ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
            Transform indexIntermediate = targetAnimator.GetBoneTransform(
                isRightThumb ? HumanBodyBones.RightIndexIntermediate : HumanBodyBones.LeftIndexIntermediate);

            if (thumbProximal != null && thumbIntermediate != null)
            {
                thumbDirection = thumbIntermediate.position - thumbProximal.position;
            }
            else if (hand != null && thumbProximal != null)
            {
                thumbDirection = thumbProximal.position - hand.position;
            }

            if (hand != null && indexProximal != null)
            {
                indexDirection = indexProximal.position - hand.position;
            }
            else if (indexProximal != null && indexIntermediate != null)
            {
                indexDirection = indexIntermediate.position - indexProximal.position;
            }

            return TryNormalize(thumbDirection, out thumbDirection) &&
                TryNormalize(indexDirection, out indexDirection);
        }

        private bool TryBuildPalmFrame(bool isRightThumb, out Vector3 sideAxis, out Vector3 palmNormal, out Vector3 forwardAxis)
        {
            sideAxis = Vector3.zero;
            palmNormal = Vector3.zero;
            forwardAxis = Vector3.zero;

            Transform hand = targetAnimator.GetBoneTransform(isRightThumb ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            Transform index = targetAnimator.GetBoneTransform(isRightThumb ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
            Transform middle = targetAnimator.GetBoneTransform(isRightThumb ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
            Transform little = targetAnimator.GetBoneTransform(isRightThumb ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
            if (hand == null || index == null || middle == null || little == null)
            {
                return false;
            }

            Vector3 rawSide = index.position - little.position;
            if (isRightThumb)
            {
                rawSide = -rawSide;
            }

            Vector3 rawForward = ((index.position + middle.position + little.position) / 3f) - hand.position;
            if (!TryNormalize(rawSide, out sideAxis) ||
                !TryNormalize(rawForward, out forwardAxis) ||
                !TryNormalize(Vector3.Cross(sideAxis, forwardAxis), out palmNormal) ||
                !TryNormalize(Vector3.Cross(palmNormal, sideAxis), out forwardAxis))
            {
                return false;
            }

            return true;
        }

        private void ApplyWorldRotationCorrection(Transform targetTransform, Quaternion correctedWorldRotation)
        {
            Quaternion rawLocalRotation = _lastRawLocalRotations.TryGetValue(targetTransform, out Quaternion lastRawRotation)
                ? lastRawRotation
                : targetTransform.localRotation;

            targetTransform.rotation = correctedWorldRotation;
            _lastRawLocalRotations[targetTransform] = rawLocalRotation;
            _lastCorrectedLocalRotations[targetTransform] = targetTransform.localRotation;
        }

    }
}