using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 끝점 보정 후보와 중간 진단값을 입력 값만으로 계산함.
    /// </summary>
    internal static partial class RetargetingEndpointDiagnostics
    {
        private const float FootHipsAlignedResidualYawGateMeters = 0.12f;
        private const float FootHipsAlignedResidualYawSideGapMeters = 0.005f;
        private const float FootHipsAlignedResidualYawProtectedMaxAngle = 20f;

        internal static bool TryCalculateEditorFootHipsAlignedResidualYawReference(
            Vector3 desiredFootPosition,
            Vector3 currentFootPosition,
            Vector3 pivotPosition,
            Quaternion currentParentWorldRotation,
            float weight,
            float maxAngleDegrees,
            out Quaternion nextParentWorldRotation)
        {
            return TryCalculateEditorFootHipsAlignedResidualYawReference(
                desiredFootPosition,
                currentFootPosition,
                pivotPosition,
                currentParentWorldRotation,
                weight,
                maxAngleDegrees,
                out nextParentWorldRotation,
                out _);
        }

        internal static bool TryCalculateEditorFootHipsAlignedResidualYawReference(
            Vector3 desiredFootPosition, Vector3 currentFootPosition, Vector3 pivotPosition, Quaternion currentParentWorldRotation,
            float weight, float maxAngleDegrees, out Quaternion nextParentWorldRotation, out float yawCorrectionAngle)
        {
            nextParentWorldRotation = currentParentWorldRotation;
            yawCorrectionAngle = float.NaN;
            if (!IsFinite(desiredFootPosition) || !IsFinite(currentFootPosition) || !IsFinite(pivotPosition) || !IsFinite(currentParentWorldRotation))
            {
                return false;
            }

            if (!TryResolveYawDirections(
                    desiredFootPosition, currentFootPosition, pivotPosition,
                    out Vector3 currentDirection, out Vector3 desiredDirection))
            {
                return false;
            }

            Quaternion correction = Quaternion.FromToRotation(currentDirection, desiredDirection);
            if (!IsFinite(correction)) { return false; }

            correction = ApplyYawCorrectionLimits(correction, weight, maxAngleDegrees);
            return TryComposeCorrectedRotation(
                correction, currentParentWorldRotation,
                out nextParentWorldRotation, out yawCorrectionAngle);
        }

        // 보정 회전을 부모 회전에 합성하고 유의미 여부를 검증함.
        private static bool TryComposeCorrectedRotation(
            Quaternion correction,
            Quaternion currentParentWorldRotation,
            out Quaternion nextParentWorldRotation,
            out float yawCorrectionAngle)
        {
            yawCorrectionAngle = Quaternion.Angle(Quaternion.identity, correction);
            nextParentWorldRotation = currentParentWorldRotation;
            if (yawCorrectionAngle <= 0.001f)
            {
                return false;
            }

            Quaternion composed = correction * currentParentWorldRotation;
            if (!IsFinite(composed) || Quaternion.Angle(currentParentWorldRotation, composed) <= 0.001f)
            {
                return false;
            }

            nextParentWorldRotation = composed;
            return true;
        }

        // 피벗 기준 수평 오프셋을 단위 방향으로 해석함.
        private static bool TryResolveYawDirections(
            Vector3 desiredFootPosition,
            Vector3 currentFootPosition,
            Vector3 pivotPosition,
            out Vector3 currentDirection,
            out Vector3 desiredDirection)
        {
            Vector3 currentOffset = currentFootPosition - pivotPosition;
            Vector3 desiredOffset = desiredFootPosition - pivotPosition;
            currentOffset.y = 0f;
            desiredOffset.y = 0f;
            desiredDirection = default;
            return TryNormalize(currentOffset, out currentDirection) &&
                   TryNormalize(desiredOffset, out desiredDirection);
        }

        // 최대 각도와 가중치를 순서대로 회전 보정에 반영함.
        private static Quaternion ApplyYawCorrectionLimits(
            Quaternion correction,
            float weight,
            float maxAngleDegrees)
        {
            float maxAngle = Mathf.Max(0f, maxAngleDegrees);
            if (maxAngle > 0f)
            {
                float angle = Quaternion.Angle(Quaternion.identity, correction);
                if (angle > maxAngle)
                {
                    correction = Quaternion.Slerp(Quaternion.identity, correction, maxAngle / angle);
                }
            }

            float clampedWeight = Mathf.Clamp01(weight);
            if (clampedWeight < 0.999f)
            {
                correction = Quaternion.Slerp(Quaternion.identity, correction, clampedWeight);
            }
            return correction;
        }

        internal static float ResolveEditorFootHipsAlignedResidualYawSideAwareMaxAngle(
            float thisFootResidual,
            float otherFootResidual,
            float requestedMaxAngle,
            bool isThisFootDominantResidual)
        {
            float maxAngle = Mathf.Max(0f, requestedMaxAngle);
            if (maxAngle <= FootHipsAlignedResidualYawProtectedMaxAngle ||
                !IsFinite(thisFootResidual) ||
                !IsFinite(otherFootResidual) ||
                isThisFootDominantResidual)
            {
                return maxAngle;
            }

            bool thisFootAlreadyPassing = thisFootResidual <= FootHipsAlignedResidualYawGateMeters;
            bool otherFootStillFailing = otherFootResidual > FootHipsAlignedResidualYawGateMeters;
            bool meaningfulSideGap = otherFootResidual - thisFootResidual >= FootHipsAlignedResidualYawSideGapMeters;
            if (thisFootAlreadyPassing && otherFootStillFailing && meaningfulSideGap)
            {
                return Mathf.Min(maxAngle, FootHipsAlignedResidualYawProtectedMaxAngle);
            }

            return maxAngle;
        }

        internal static float CalculateEndpointPositionMaxYawAngle(
            Vector3 currentFootPosition,
            Vector3 pivotPosition,
            float maxOffset)
        {
            Vector3 currentOffset = currentFootPosition - pivotPosition;
            currentOffset.y = 0f;
            float radius = currentOffset.magnitude;
            if (!IsFinite(currentOffset) || radius <= 0.0001f)
            {
                return 0f;
            }

            float normalizedOffset = Mathf.Clamp01(Mathf.Max(0f, maxOffset) / radius);
            if (normalizedOffset <= 0f)
            {
                return 0f;
            }

            return Mathf.Asin(normalizedOffset) * Mathf.Rad2Deg;
        }

        internal static bool TryCalculateReferencePosition(
            Vector3 desiredFootPosition,
            Vector3 desiredToesPosition,
            Vector3 currentFootPosition,
            Vector3 currentToesPosition,
            float weight,
            float maxOffset,
            float positiveZScale,
            out Vector3 nextFootPosition)
        {
            return TryCalculateReferencePosition(
                desiredFootPosition,
                desiredToesPosition,
                currentFootPosition,
                currentToesPosition,
                weight,
                maxOffset,
                positiveZScale,
                toesBlendWeight: 1f,
                out nextFootPosition);
        }

        internal static bool TryCalculateReferencePosition(
            Vector3 desiredFootPosition,
            Vector3 desiredToesPosition,
            Vector3 currentFootPosition,
            Vector3 currentToesPosition,
            float weight,
            float maxOffset,
            float positiveZScale,
            float toesBlendWeight,
            out Vector3 nextFootPosition)
        {
            return TryCalculateReferencePosition(
                desiredFootPosition,
                desiredToesPosition,
                currentFootPosition,
                currentToesPosition,
                weight,
                maxOffset,
                positiveZScale,
                toesBlendWeight,
                out nextFootPosition,
                out _);
        }

        internal static bool TryCalculateReferencePosition( Vector3 desiredFootPosition, Vector3 desiredToesPosition, Vector3 currentFootPosition, Vector3 currentToesPosition, float weight, float maxOffset, float positiveZScale, float toesBlendWeight, out Vector3 nextFootPosition, out RetargetingEndpointDiagnosticSnapshot diagnostics)
        {
            nextFootPosition = currentFootPosition;
            diagnostics = RetargetingEndpointDiagnosticSnapshot.Empty;
            diagnostics.DesiredFootPosition = desiredFootPosition;
            diagnostics.DesiredToesPosition = desiredToesPosition;
            diagnostics.CurrentFootPosition = currentFootPosition;
            diagnostics.CurrentToesPosition = currentToesPosition;
            if (!IsFinite(desiredFootPosition) || !IsFinite(currentFootPosition))
            {
                return false; }
            Vector3 footDelta = desiredFootPosition - currentFootPosition;
            footDelta.y = 0f;
            Vector3 endpointDelta = ResolveEndpointDelta(
                footDelta, desiredToesPosition, currentToesPosition, toesBlendWeight);
            diagnostics.EndpointDeltaBeforeClamp = endpointDelta;
            if (!IsFinite(endpointDelta) || endpointDelta.sqrMagnitude <= 0.00000001f)
            {
                return false; }
            Vector3 correction = ApplyEndpointShaping(
                endpointDelta, maxOffset, positiveZScale, weight, ref diagnostics);
            if (!IsFinite(correction) || correction.sqrMagnitude <= 0.00000001f)
            {
                return false; }
            nextFootPosition = currentFootPosition + correction;
            nextFootPosition.y = currentFootPosition.y;
            diagnostics.NextFootPosition = nextFootPosition;
            return IsFinite(nextFootPosition); }

        // 발끝 델타가 유효하면 발 델타와 혼합한 평균을 반환함.
        private static Vector3 ResolveEndpointDelta(
            Vector3 footDelta,
            Vector3 desiredToesPosition,
            Vector3 currentToesPosition,
            float toesBlendWeight)
        {
            if (!IsFinite(desiredToesPosition) || !IsFinite(currentToesPosition))
            {
                return footDelta;
            }
            Vector3 toesDelta = desiredToesPosition - currentToesPosition;
            toesDelta.y = 0f;
            Vector3 averagedEndpointDelta = (footDelta + toesDelta) * 0.5f;
            return Vector3.Lerp(footDelta, averagedEndpointDelta, Mathf.Clamp01(toesBlendWeight));
        }

        // 오프셋 클램프와 전진 스케일을 적용한 뒤 가중 보정량과 진단을 기록함.
        private static Vector3 ApplyEndpointShaping(
            Vector3 endpointDelta,
            float maxOffset,
            float positiveZScale,
            float weight,
            ref RetargetingEndpointDiagnosticSnapshot diagnostics)
        {
            float clampedMaxOffset = Mathf.Max(0f, maxOffset);
            if (clampedMaxOffset > 0f)
            {
                endpointDelta = Vector3.ClampMagnitude(endpointDelta, clampedMaxOffset); }
            diagnostics.EndpointDeltaAfterClamp = endpointDelta;
            if (endpointDelta.z > 0f)
            {
                endpointDelta.z *= Mathf.Clamp01(positiveZScale); }
            diagnostics.EndpointDeltaAfterPositiveZScale = endpointDelta;
            Vector3 correction = endpointDelta * Mathf.Clamp01(weight);
            correction.y = 0f;
            diagnostics.Correction = correction;
            return correction;
        }

        internal static bool TryCalculateEvaluatorXzReferencePosition(
            Vector3 referenceFootPosition,
            Vector3 currentFootPosition,
            Vector3 firstMatchedFootOffset,
            float targetMagnitude,
            float weight,
            float maxOffset,
            out Vector3 nextFootPosition)
        {
            return TryCalculateEvaluatorXzReferencePosition(
                referenceFootPosition,
                currentFootPosition,
                firstMatchedFootOffset,
                targetMagnitude,
                weight,
                maxOffset,
                out nextFootPosition,
                out _);
        }

        internal static bool TryCalculateEvaluatorXzReferencePosition(
            Vector3 referenceFootPosition, Vector3 currentFootPosition, Vector3 firstMatchedFootOffset, float targetMagnitude, float weight, float maxOffset, out Vector3 nextFootPosition, out RetargetingEndpointDiagnosticSnapshot diagnostics)
        {
            nextFootPosition = currentFootPosition;
            diagnostics = RetargetingEndpointDiagnosticSnapshot.Empty;
            diagnostics.CurrentFootPosition = currentFootPosition;
            diagnostics.CurrentToesPosition = NaNVector3;
            diagnostics.EvaluatorXzReferenceEnabled = 1f;
            diagnostics.EvaluatorXzFirstOffset = firstMatchedFootOffset;
            diagnostics.EvaluatorXzTargetMagnitude = Mathf.Max(0f, targetMagnitude);
            if (!IsFinite(referenceFootPosition) || !IsFinite(currentFootPosition) || !IsFinite(firstMatchedFootOffset))
            {
                return false; }
            Vector3 normalizedDelta = currentFootPosition - referenceFootPosition - firstMatchedFootOffset;
            normalizedDelta.y = 0f;
            diagnostics.EvaluatorXzNormalizedDelta = normalizedDelta;
            diagnostics.DesiredToesPosition = NaNVector3;
            if (!IsFinite(normalizedDelta) || normalizedDelta.sqrMagnitude <= 0.00000001f)
            {
                diagnostics.DesiredFootPosition = currentFootPosition;
                return false; }
            float magnitude = normalizedDelta.magnitude;
            float clampedTargetMagnitude = Mathf.Max(0f, targetMagnitude);
            if (!IsFinite(magnitude) || magnitude <= clampedTargetMagnitude || magnitude <= 0f)
            {
                diagnostics.DesiredFootPosition = currentFootPosition;
                return false; }
            return ApplyXzCorrection( normalizedDelta, magnitude, clampedTargetMagnitude, weight, maxOffset, currentFootPosition, ref diagnostics, out nextFootPosition); }

        static bool ApplyXzCorrection(
            Vector3 normalizedDelta,
            float magnitude, float clampedTargetMagnitude, float weight, float maxOffset, Vector3 currentFootPosition, ref RetargetingEndpointDiagnosticSnapshot diagnostics, out Vector3 nextFootPosition)
        {
            nextFootPosition = currentFootPosition;
            Vector3 desiredNormalizedDelta = normalizedDelta * (clampedTargetMagnitude / magnitude);
            diagnostics.EvaluatorXzDesiredNormalizedDelta = desiredNormalizedDelta;
            Vector3 correction = desiredNormalizedDelta - normalizedDelta;
            correction.y = 0f;
            diagnostics.EndpointDeltaBeforeClamp = correction;
            float clampedMaxOffset = Mathf.Max(0f, maxOffset);
            if (clampedMaxOffset > 0f)
            {
                correction = Vector3.ClampMagnitude(correction, clampedMaxOffset); }
            diagnostics.EndpointDeltaAfterClamp = correction;
            diagnostics.EndpointDeltaAfterPositiveZScale = correction;
            correction *= Mathf.Clamp01(weight);
            correction.y = 0f;
            diagnostics.Correction = correction;
            if (!IsFinite(correction) || correction.sqrMagnitude <= 0.00000001f)
            {
                diagnostics.DesiredFootPosition = currentFootPosition;
                return false; }
            nextFootPosition = currentFootPosition + correction;
            nextFootPosition.y = currentFootPosition.y;
            diagnostics.DesiredFootPosition = nextFootPosition;
            diagnostics.NextFootPosition = nextFootPosition;
            return IsFinite(nextFootPosition); }

        internal static bool ShouldKeepHipsLocalPositionReferenceByTargetGap(
            Vector3 referenceFootPosition,
            Vector3 referenceToesPosition,
            Vector3 beforeFootPosition,
            Vector3 beforeToesPosition,
            Vector3 afterFootPosition,
            Vector3 afterToesPosition,
            float maxAllowedIncrease)
        {
            if (!TryCalculateEndpointTargetGap(
                    referenceFootPosition,
                    referenceToesPosition,
                    beforeFootPosition,
                    beforeToesPosition,
                    out float beforeGap) ||
                !TryCalculateEndpointTargetGap(
                    referenceFootPosition,
                    referenceToesPosition,
                    afterFootPosition,
                    afterToesPosition,
                    out float afterGap))
            {
                return true;
            }

            return afterGap <= beforeGap + Mathf.Max(0f, maxAllowedIncrease);
        }

        internal static bool TryFindFirstStagePositionJump(
            string[] stageNames,
            Vector3[] positions, float threshold, out string stage, out Vector3 delta, out float magnitude)
        {
            stage = "";
            delta = NaNVector3;
            magnitude = float.NaN;
            if (stageNames == null || positions == null || stageNames.Length != positions.Length || positions.Length < 2)
            {
                return false; }
            float safeThreshold = Mathf.Max(0f, threshold);
            for (int i = 1; i < positions.Length; i++)
            {
                Vector3 previous = positions[i - 1];
                Vector3 current = positions[i];
                if (!IsFinite(previous) || !IsFinite(current))
                {
                    continue; }
                Vector3 stageDelta = current - previous;
                float stageMagnitude = stageDelta.magnitude;
                if (!IsFinite(stageDelta) || !IsFinite(stageMagnitude) || stageMagnitude <= safeThreshold)
                {
                    continue; }
                stage = stageNames[i] ?? "";
                delta = stageDelta;
                magnitude = stageMagnitude;
                return true; }
            return false; }

        private static bool TryCalculateEndpointTargetGap(
            Vector3 referenceFootPosition,
            Vector3 referenceToesPosition,
            Vector3 targetFootPosition,
            Vector3 targetToesPosition,
            out float gap)
        {
            gap = float.NaN;
            if (!TryCalculateXzDistance(referenceFootPosition, targetFootPosition, out float footGap) ||
                !TryCalculateXzDistance(referenceToesPosition, targetToesPosition, out float toesGap))
            {
                return false;
            }

            gap = Mathf.Max(footGap, toesGap);
            return IsFinite(gap);
        }

    }

    /// <summary>
    /// 끝점 보정 단계별 계산값을 호출자에게 전달함.
    /// </summary>
}
