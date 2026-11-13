using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 그라운딩 프로세서의 상태 필드를 담당하는 partial 분할.
    internal sealed partial class RetargetingGroundingProcessor
    {
        private const float DefaultFootRadius = 0.04f;
        private const float GroundingDirectionReversalStepScale = 0.4f;

        internal bool _groundingInitialized;
        internal float _lastGroundingAdjustment = float.NaN;
        internal float _maxGroundingAdjustment;
        internal int _groundingStepClampedCount;
        internal int _groundingSmoothedCount;
        internal float _lastGroundingVerticalStep = float.NaN;
        internal float _maxGroundingVerticalStep;
        internal float _initialGroundingVerticalStep = float.NaN;
        internal float _maxGroundingVerticalStepAfterInitial;
        internal float _lastGroundingTargetY = float.NaN;
        internal float _lastGroundingLowestFootBottomY = float.NaN;
        private bool _lateVisualGroundingWarningLogged;
        private bool _lateVisualGroundingInitialized;
        private bool _hasFrozenGroundingRootY;
        private float _frozenGroundingRootY;
        private bool _rendererGroundingOutlierWarningLogged;
        private bool _hasEstimatedFootRadius;
        private float _estimatedFootRadius = DefaultFootRadius;
        private Vector3 _leftFootLockPosition;
        private Vector3 _rightFootLockPosition;
        private bool _leftFootLocked;
        private bool _rightFootLocked;
    }
}
