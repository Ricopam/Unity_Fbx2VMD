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
                if (thumbTransform == null)
                {
                    continue;
                }
                if (TryClampTransformRotation(
                        thumbTransform,
                        GetThumbRotationOffset(thumbBone),
                        GetLimit(thumbBone)))
                {
                    changed++;
                }
            }
            changed += ClampThumbBaseHelperTransforms();
            return changed;
        }

        // 베이스라인 회전이 잡힌 한 개의 대상에 오프셋 적용 회전을 클램프한다.
        private bool TryClampTransformRotation(
            Transform thumbTransform, Quaternion offsetRotation, float limit)
        {
            if (thumbTransform == null ||
                !_initialLocalRotations.TryGetValue(thumbTransform, out Quaternion initialRotation))
            {
                return false;
            }
            Quaternion rawRotation = GetCurrentRawLocalRotation(thumbTransform);
            if (!IsFinite(rawRotation))
            {
                SetCorrectedLocalRotation(thumbTransform, initialRotation, initialRotation);
                return true;
            }
            if (!ThumbLocalRotationCalculator.TryCalculateCorrection(
                    initialRotation,
                    rawRotation,
                    thumbTransform.localRotation,
                    offsetRotation,
                    limit,
                    out Quaternion correctedRotation))
            {
                return false;
            }
            SetCorrectedLocalRotation(thumbTransform, rawRotation, correctedRotation);
            return true;
        }

        public void RecaptureBaseline()
        {
            ClearCapturedState();
            if (!InitializeIfNeeded())
            {
                return;
            }
            CaptureInitialThumbPoses();
            CaptureThumbBaseHelperRotations();
            CaptureDetachedThumbBaseHelperSources();
        }

        private void ClearCapturedState()
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
        }

        private void CaptureInitialThumbPoses()
        {
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

            // FBX 재생 경로에는 리타게터가 없어 매번 씬 전체를 찾게 되므로, 한 번 찾고 없으면 다음 설정까지 기억함.
            if (linkedPoseSpaceRetargeter == null && _hasSearchedMissingRetargeter)
            {
                return null;
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

            _hasSearchedMissingRetargeter = linkedPoseSpaceRetargeter == null;
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
                if (thumbTransform == null)
                {
                    continue;
                }
                if (TryClampTransformRotation(
                        thumbTransform,
                        GetProximalRotationOffsetRotation(thumbTransform),
                        proximalMaxLocalAngle))
                {
                    changed++;
                }
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


    }
}