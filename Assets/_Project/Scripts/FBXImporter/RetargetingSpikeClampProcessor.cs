using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 루트 위치·엉덩이 로컬 위치의 프레임 스파이크 클램프 상태와 계산을 담당함. PoseSpaceRetargeter에서 분리됨.
    internal sealed class RetargetingSpikeClampProcessor
    {
        internal float _lastRootPositionPoseDeltaMagnitude = float.NaN;
        internal float _maxRootPositionPoseDeltaMagnitude;
        internal int _rootPositionSpikeClampedCount;
        private Vector3 _previousTargetHipsLocalPosition;
        private bool _hasPreviousTargetHipsLocalPosition;
        internal float _lastTargetHipsLocalPositionDelta = float.NaN;
        internal float _maxTargetHipsLocalPositionDelta;
        internal int _targetHipsLocalPositionSpikeClampedCount;

        internal void ResetRootPositionMetrics()
        {
            _lastRootPositionPoseDeltaMagnitude = float.NaN;
            _maxRootPositionPoseDeltaMagnitude = 0f;
            _rootPositionSpikeClampedCount = 0;
        }

        internal void ResetHipsSpikeState()
        {
            _previousTargetHipsLocalPosition = Vector3.zero;
            _hasPreviousTargetHipsLocalPosition = false;
            _lastTargetHipsLocalPositionDelta = float.NaN;
            _maxTargetHipsLocalPositionDelta = 0f;
            _targetHipsLocalPositionSpikeClampedCount = 0;
        }

        internal void ClampRootPositionSpike(PoseSpaceRetargeter retargeter, Vector3 positionBeforePose, string source)
        {
            if (!retargeter.clampRootDeltaSpikes || retargeter.targetAnimator == null)
            {
                return;
            }

            Vector3 currentPosition = retargeter.targetAnimator.transform.position;
            bool shouldClamp = RetargetingPoseSmoothing.TryCalculateRootPositionSpikeClamp(
                positionBeforePose,
                currentPosition,
                retargeter.maxRootDeltaPerFrame,
                out Vector3 clampedPosition,
                out float poseDeltaMagnitude);

            _lastRootPositionPoseDeltaMagnitude = poseDeltaMagnitude;
            if (!PoseSpaceRetargeter.IsFinite(poseDeltaMagnitude))
            {
                return;
            }

            _maxRootPositionPoseDeltaMagnitude = Mathf.Max(_maxRootPositionPoseDeltaMagnitude, _lastRootPositionPoseDeltaMagnitude);
            if (!shouldClamp)
            {
                return;
            }

            ApplyRootPositionClamp(retargeter, clampedPosition, source);
        }

        private void ApplyRootPositionClamp(PoseSpaceRetargeter retargeter, Vector3 clampedPosition, string source)
        {
            _rootPositionSpikeClampedCount++;
            if (retargeter.logRootDeltaSpikes && !retargeter._rootDeltaSpikeWarningLogged)
            {
                Debug.LogWarning($"[PoseSpaceRetargeter] {source} root position spike {_lastRootPositionPoseDeltaMagnitude:F3}m clamped. limit={retargeter.maxRootDeltaPerFrame:F3}m");
                retargeter._rootDeltaSpikeWarningLogged = true;
            }

            retargeter.targetAnimator.transform.position = clampedPosition;
        }

        internal void ClampHipsLocalPositionSpike(PoseSpaceRetargeter retargeter)
        {
            if (!retargeter.clampTargetHipsLocalPositionSpikes || retargeter.targetAnimator == null || !retargeter.targetAnimator.isHuman)
            {
                ResetHipsSpikeState();
                return;
            }

            Transform targetHips = retargeter.targetAnimator.GetBoneTransform(HumanBodyBones.Hips);
            if (targetHips == null)
            {
                ResetHipsSpikeState();
                return;
            }

            Vector3 currentLocalPosition = targetHips.localPosition;
            if (!_hasPreviousTargetHipsLocalPosition)
            {
                RememberTargetHipsLocalPosition(currentLocalPosition);
                return;
            }

            ApplyHipsSpikeClamp(retargeter, targetHips, currentLocalPosition);
        }

        private void ApplyHipsSpikeClamp(PoseSpaceRetargeter retargeter, Transform targetHips, Vector3 currentLocalPosition)
        {
            bool shouldClamp = RetargetingPoseSmoothing.TryCalculateHipsLocalPositionSpikeClamp(
                _previousTargetHipsLocalPosition,
                currentLocalPosition,
                retargeter.maxTargetHipsLocalPositionDeltaPerFrame,
                out Vector3 clampedLocalPosition,
                out float deltaMagnitude);

            _lastTargetHipsLocalPositionDelta = deltaMagnitude;
            if (!PoseSpaceRetargeter.IsFinite(deltaMagnitude))
            {
                return;
            }

            _maxTargetHipsLocalPositionDelta = Mathf.Max(_maxTargetHipsLocalPositionDelta, deltaMagnitude);
            if (shouldClamp)
            {
                targetHips.localPosition = clampedLocalPosition;
                _targetHipsLocalPositionSpikeClampedCount++;
            }

            RememberTargetHipsLocalPosition(targetHips.localPosition);
        }

        private void RememberTargetHipsLocalPosition(Vector3 localPosition)
        {
            if (!PoseSpaceRetargeter.IsFinite(localPosition))
            {
                return;
            }

            _previousTargetHipsLocalPosition = localPosition;
            _hasPreviousTargetHipsLocalPosition = true;
        }
    }
}
