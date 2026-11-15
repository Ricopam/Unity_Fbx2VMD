using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidThumbDeformationGuard
    {
        private readonly HashSet<Transform> _thumbBaseHelperTransforms = new HashSet<Transform>();
        private readonly Dictionary<Transform, Transform> _detachedThumbBaseHelperSources = new Dictionary<Transform, Transform>();
        private readonly Dictionary<Transform, Quaternion> _detachedThumbBaseSourceInitialLocalRotations = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, Quaternion> _detachedThumbBaseInitialRelativeRotations = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, float> _detachedThumbBaseInitialDistances = new Dictionary<Transform, float>();

        private int SyncDetachedThumbBaseHelperTransforms()
        {
            bool useWebbingGuard = stabilizeThumbWebbingCrease && thumbWebbingCreaseStabilizeWeight > 0f;
            if ((!syncDetachedThumbBaseHelpers || detachedThumbBaseHelperSyncWeight <= 0f) && (!stabilizeDetachedThumbBasePalm || detachedThumbBasePalmStabilizeWeight <= 0f) && !useWebbingGuard)
            {
                return 0; }
            int changed = 0;
            foreach (KeyValuePair<Transform, Transform> pair in _detachedThumbBaseHelperSources)
            {
                Transform helperTransform = pair.Key;
                Transform sourceTransform = pair.Value;
                if (helperTransform == null || sourceTransform == null)
                {
                    continue; }
                Quaternion targetRotation = CalculateDetachedThumbBaseHelperTargetRotation(helperTransform, sourceTransform);
                if (Quaternion.Angle(helperTransform.localRotation, targetRotation) > 0.001f)
                {
                    helperTransform.localRotation = targetRotation;
                    changed++; }
                if (!syncDetachedThumbBaseHelperPositions)
                {
                    if ((stabilizeDetachedThumbBasePalm || useWebbingGuard) && _initialLocalPositions.TryGetValue(helperTransform, out Vector3 anchoredPosition) && (helperTransform.localPosition - anchoredPosition).sqrMagnitude > 0.00000001f)
                    {
                        // YYB 손꿈치 스킨용 Thumb0 보조본은 위치까지 움직이면 엄지 뿌리 메시가 손바닥 밖으로 끌려간다.
                        // 실제 엄지 구동본은 별도로 움직이고, 보조본 위치는 초기 손바닥 앵커에 고정한다.
                        helperTransform.localPosition = anchoredPosition;
                        changed++; }
                    continue; }
                Vector3 targetPosition = ResolveDetachedHelperTargetPosition( helperTransform, sourceTransform, useWebbingGuard);
                if ((helperTransform.localPosition - targetPosition).sqrMagnitude <= 0.00000001f)
                {
                    continue; }
                helperTransform.localPosition = targetPosition;
                changed++; }
            return changed; }

        private Vector3 ResolveDetachedHelperTargetPosition(
            Transform helperTransform, Transform sourceTransform, bool useWebbingGuard)
        {
            Vector3 targetPosition = GetSourcePositionInHelperParentSpace(helperTransform, sourceTransform);
            if (_initialLocalPositions.TryGetValue(helperTransform, out Vector3 initialPosition) &&
                detachedThumbBaseHelperSyncWeight < 0.999f)
            {
                targetPosition = Vector3.Lerp(initialPosition, targetPosition, detachedThumbBaseHelperSyncWeight);
            }

            if (_initialLocalPositions.TryGetValue(helperTransform, out initialPosition))
            {
                targetPosition = initialPosition + Vector3.ClampMagnitude(
                    targetPosition - initialPosition,
                    detachedThumbBaseHelperMaxPositionOffset);
            }

            if (useWebbingGuard)
            {
                targetPosition = ConstrainThumbWebbingHelperPosition(helperTransform, sourceTransform, targetPosition);
            }
            return targetPosition;
        }

        private Quaternion CalculateDetachedThumbBaseHelperTargetRotation(Transform helperTransform, Transform sourceTransform)
        {
            if (!_initialLocalRotations.TryGetValue(helperTransform, out Quaternion helperInitialRotation))
            {
                return sourceTransform.localRotation;
            }

            Quaternion sourceRotation = sourceTransform.localRotation;
            bool hasSourceInitialRotation = _detachedThumbBaseSourceInitialLocalRotations.TryGetValue( sourceTransform, out Quaternion sourceInitialRotation);
            Quaternion deltaAxisRemap = Quaternion.identity;
            if (syncDetachedThumbBaseHelpers && detachedThumbBaseHelperSyncWeight > 0f && hasSourceInitialRotation)
            {
                deltaAxisRemap = GetDetachedThumbBaseHelperDeltaAxisRemap(sourceTransform);
            }

            Quaternion targetRotationOffset = GetDetachedThumbBaseHelperTargetRotationOffset(sourceTransform);
            Quaternion targetRotation = ThumbBaseHelperRotationCalculator.CalculateBaseRotation( helperInitialRotation, sourceRotation, hasSourceInitialRotation, sourceInitialRotation,
                syncDetachedThumbBaseHelpers, detachedThumbBaseHelperSyncWeight, deltaAxisRemap, targetRotationOffset, stabilizeDetachedThumbBasePalm, detachedThumbBasePalmStabilizeWeight);

            float effectiveWebbingWeight = ResolveWebbingCorrectiveSettings( helperTransform, sourceTransform, targetRotation, out float effectiveWebbingMaxLocalAngle);

            return ThumbBaseHelperRotationCalculator.FinalizeRotation( helperInitialRotation, targetRotation, stabilizeThumbWebbingCrease, effectiveWebbingWeight, detachedThumbBaseHelperMaxLocalAngle,
                stabilizeDetachedThumbBasePalm, detachedThumbBasePalmStabilizeWeight, detachedThumbBasePalmMaxLocalAngle, effectiveWebbingMaxLocalAngle);
        }

        private float ResolveWebbingCorrectiveSettings(
            Transform helperTransform,
            Transform sourceTransform,
            Quaternion targetRotation,
            out float effectiveWebbingMaxLocalAngle)
        {
            float effectiveWebbingWeight = 0f;
            effectiveWebbingMaxLocalAngle = thumbWebbingCreaseMaxLocalAngle;
            if (stabilizeThumbWebbingCrease && thumbWebbingCreaseStabilizeWeight > 0f)
            {
                GetEffectiveThumbWebbingCorrectiveSettings(
                    helperTransform,
                    sourceTransform,
                    targetRotation,
                    helperTransform.localPosition,
                    out effectiveWebbingWeight,
                    out effectiveWebbingMaxLocalAngle,
                    out _);
            }
            return effectiveWebbingWeight;
        }

        private Quaternion GetDetachedThumbBaseHelperDeltaAxisRemap(Transform sourceTransform)
        {
            if (!IsExplicitDetachedThumbBaseSource(sourceTransform))
            {
                return Quaternion.identity;
            }

            Vector3 offset = Vector3.zero;
            if (TryResolveThumbSide(sourceTransform, out bool isRightThumb))
            {
                offset = isRightThumb
                    ? rightDetachedThumbBaseHelperDeltaAxisOffset
                    : leftDetachedThumbBaseHelperDeltaAxisOffset;
            }

            return offset.sqrMagnitude <= 0.000001f
                ? Quaternion.identity
                : Quaternion.Euler(offset);
        }

        private Quaternion GetDetachedThumbBaseHelperTargetRotationOffset(Transform sourceTransform)
        {
            if (!IsExplicitDetachedThumbBaseSource(sourceTransform))
            {
                return Quaternion.identity;
            }

            Vector3 offset = Vector3.zero;
            if (TryResolveThumbSide(sourceTransform, out bool isRightThumb))
            {
                offset = isRightThumb
                    ? rightDetachedThumbBaseHelperTargetRotationOffset
                    : leftDetachedThumbBaseHelperTargetRotationOffset;
            }

            return offset.sqrMagnitude <= 0.000001f
                ? Quaternion.identity
                : Quaternion.Euler(offset);
        }

        private Vector3 ConstrainThumbWebbingHelperPosition(
            Transform helperTransform,
            Transform sourceTransform,
            Vector3 targetPosition)
        {
            if (helperTransform == null ||
                !_initialLocalPositions.TryGetValue(helperTransform, out Vector3 initialPosition))
            {
                return targetPosition;
            }

            GetEffectiveThumbWebbingCorrectiveSettings(
                helperTransform,
                sourceTransform,
                helperTransform.localRotation,
                targetPosition,
                out float weight,
                out _,
                out float maxOffset);

            return ThumbWebbingCorrectionCalculator.ConstrainPosition(
                initialPosition,
                targetPosition,
                weight,
                maxOffset);
        }

        private static Vector3 GetSourcePositionInHelperParentSpace(Transform helperTransform, Transform sourceTransform)
        {
            if (helperTransform == null || sourceTransform == null)
            {
                return Vector3.zero;
            }

            if (helperTransform.parent == sourceTransform.parent)
            {
                return sourceTransform.localPosition;
            }

            return helperTransform.parent != null
                ? helperTransform.parent.InverseTransformPoint(sourceTransform.position)
                : sourceTransform.position;
        }

        private void CaptureThumbBaseHelperRotations()
        {
            if (targetAnimator == null)
            {
                return;
            }

            foreach (Transform candidate in targetAnimator.GetComponentsInChildren<Transform>(true))
            {
                if (!IsThumbBaseHelperTransform(candidate))
                {
                    continue;
                }

                if (_initialLocalRotations.ContainsKey(candidate))
                {
                    continue;
                }

                _initialLocalRotations[candidate] = candidate.localRotation;
                _initialLocalPositions[candidate] = candidate.localPosition;
                _thumbBaseHelperTransforms.Add(candidate);
                CacheThumbSide(candidate);
            }
        }

        private Transform FindThumbBaseHelperTransformForSide(bool isRightThumb)
        {
            foreach (Transform helperTransform in _thumbBaseHelperTransforms)
            {
                if (helperTransform == null)
                {
                    continue;
                }

                if (TryResolveThumbSide(helperTransform, out bool helperIsRightThumb) &&
                    helperIsRightThumb == isRightThumb)
                {
                    return helperTransform;
                }
            }

            return null;
        }

        private void CaptureDetachedThumbBaseHelperSources()
        {
            if (targetAnimator == null)
            {
                return;
            }
            foreach (Transform helperTransform in targetAnimator.GetComponentsInChildren<Transform>(true))
            {
                if (!IsDetachedThumbBaseHelperTransform(helperTransform))
                {
                    continue;
                }
                Transform sourceTransform = FindMatchingActiveThumbBaseSource(helperTransform);
                if (sourceTransform == null)
                {
                    continue;
                }
                CacheThumbSide(helperTransform);
                CacheThumbSide(sourceTransform);
                _detachedThumbBaseHelperSources[helperTransform] = sourceTransform;
                if (!_detachedThumbBaseSourceInitialLocalRotations.ContainsKey(sourceTransform))
                {
                    _detachedThumbBaseSourceInitialLocalRotations[sourceTransform] = sourceTransform.localRotation;
                }
                _detachedThumbBaseInitialRelativeRotations[helperTransform] = Quaternion.Inverse(sourceTransform.rotation) * helperTransform.rotation;
                _detachedThumbBaseInitialDistances[helperTransform] = Vector3.Distance(helperTransform.position, sourceTransform.position);
            }
        }

        private Transform FindMatchingActiveThumbBaseSource(Transform helperTransform)
        {
            if (helperTransform == null)
            {
                return null;
            }

            if (!TryResolveThumbSide(helperTransform, out bool isRightThumb))
            {
                return GetClosestMappedThumbProximal(helperTransform);
            }
            foreach (Transform candidate in targetAnimator.GetComponentsInChildren<Transform>(true))
            {
                if (candidate == null || candidate == helperTransform)
                {
                    continue;
                }
                if (!ThumbTransformNamePolicy.IsActiveBaseSource(candidate.name) ||
                    !TryResolveThumbSide(candidate, out bool candidateIsRightThumb) ||
                    candidateIsRightThumb != isRightThumb)
                {
                    continue;
                }
                return candidate;
            }
            Transform mappedThumbProximal = GetMappedThumbProximal(isRightThumb);
            return mappedThumbProximal != helperTransform ? mappedThumbProximal : null;
        }
    }
}
