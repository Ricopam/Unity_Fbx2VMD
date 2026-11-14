using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidThumbDeformationGuard
    {
        // E2E가 Guard 바인딩과 실제 보정 실행 여부를 증명할 수 있게 읽기 전용 진단을 제공함.
        internal int CapturedThumbBaselineBoneCount => _initialLocalRotations.Count;
        internal int LastFrameCorrectionCount => _lastFrameCorrectionCount;
        internal int CorrectionsAppliedTotal => _correctionsAppliedTotal;
        internal int FramesWithCorrections => _framesWithCorrections;
        internal bool HasBoundAnimator => targetAnimator != null;
        internal bool IsRetargeterLinked => linkedPoseSpaceRetargeter != null;

        public string BuildThumbHelperDebugSummary(bool isRightThumb)
        {
            string sideLabel = isRightThumb ? "R" : "L";
            Transform helperTransform = FindThumbBaseHelperTransformForSide(isRightThumb);
            if (helperTransform == null)
            {
                return $"side={sideLabel}, helper=<none>, source=<none>, state=missing";
            }

            _detachedThumbBaseHelperSources.TryGetValue(helperTransform, out Transform sourceTransform);

            float initialDistance = float.NaN;
            float currentDistance = float.NaN;
            float distanceDelta = float.NaN;
            if (sourceTransform != null &&
                _detachedThumbBaseInitialDistances.TryGetValue(helperTransform, out float storedDistance))
            {
                initialDistance = storedDistance;
                currentDistance = Vector3.Distance(helperTransform.position, sourceTransform.position);
                distanceDelta = Mathf.Abs(currentDistance - initialDistance);
            }

            float relativeRotationDelta = float.NaN;
            if (sourceTransform != null &&
                _detachedThumbBaseInitialRelativeRotations.TryGetValue(helperTransform, out Quaternion initialRelativeRotation))
            {
                Quaternion currentRelativeRotation = Quaternion.Inverse(sourceTransform.rotation) * helperTransform.rotation;
                relativeRotationDelta = Quaternion.Angle(initialRelativeRotation, currentRelativeRotation);
            }

            float helperLocalPositionDelta = float.NaN;
            if (_initialLocalPositions.TryGetValue(helperTransform, out Vector3 initialLocalPosition))
            {
                helperLocalPositionDelta = Vector3.Distance(initialLocalPosition, helperTransform.localPosition);
            }

            float helperLocalRotationDelta = float.NaN;
            if (_initialLocalRotations.TryGetValue(helperTransform, out Quaternion initialLocalRotation))
            {
                helperLocalRotationDelta = Quaternion.Angle(initialLocalRotation, helperTransform.localRotation);
            }

            return
                $"side={sideLabel}, helper={GetTransformPath(helperTransform)}, source={GetTransformPath(sourceTransform)}, " +
                $"initDist={FormatDebugFloat(initialDistance)}, currDist={FormatDebugFloat(currentDistance)}, distDelta={FormatDebugFloat(distanceDelta)}, " +
                $"relRotDelta={FormatDebugFloat(relativeRotationDelta)}, localPosDelta={FormatDebugFloat(helperLocalPositionDelta)}, " +
                $"localRotDelta={FormatDebugFloat(helperLocalRotationDelta)}, linkedRetargeter={GetTransformPath(linkedPoseSpaceRetargeter != null ? linkedPoseSpaceRetargeter.transform : null)}";
        }

        private void ResetThumbPoseShapingDiagnostics()
        {
            LastLeftThumbProjectionCorrectionApplyCount = 0;
            LastRightThumbProjectionCorrectionApplyCount = 0;
            LastLeftThumbProjectionCorrectionPreserveCount = 0;
            LastRightThumbProjectionCorrectionPreserveCount = 0;
            LastLeftThumbSegmentStraightenApplyCount = 0;
            LastRightThumbSegmentStraightenApplyCount = 0;
            LastLeftThumbSegmentStraightenPreserveCount = 0;
            LastRightThumbSegmentStraightenPreserveCount = 0;
            ResolveLinkedPoseSpaceRetargeter();
            linkedPoseSpaceRetargeter?.ResetThumbWorldRotationPreserveDiagnostics();
        }

        private void RecordThumbProjectionCorrection(bool isRightThumb, bool preserved)
        {
            if (isRightThumb)
            {
                if (preserved)
                {
                    LastRightThumbProjectionCorrectionPreserveCount++;
                }
                else
                {
                    LastRightThumbProjectionCorrectionApplyCount++;
                }
            }
            else
            {
                if (preserved)
                {
                    LastLeftThumbProjectionCorrectionPreserveCount++;
                }
                else
                {
                    LastLeftThumbProjectionCorrectionApplyCount++;
                }
            }
        }

        private void RecordThumbSegmentStraightenCorrection(bool isRightThumb, bool preserved)
        {
            if (isRightThumb)
            {
                if (preserved)
                {
                    LastRightThumbSegmentStraightenPreserveCount++;
                }
                else
                {
                    LastRightThumbSegmentStraightenApplyCount++;
                }
            }
            else
            {
                if (preserved)
                {
                    LastLeftThumbSegmentStraightenPreserveCount++;
                }
                else
                {
                    LastLeftThumbSegmentStraightenApplyCount++;
                }
            }
        }
    }
}
