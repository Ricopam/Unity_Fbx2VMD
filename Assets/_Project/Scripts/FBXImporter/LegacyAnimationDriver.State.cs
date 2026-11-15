namespace Fbx2Vmd.FBXImporter
{
    // 시간 추적·스텝 안정성 지표 상태 분할.
    internal sealed partial class LegacyAnimationDriver
    {
        private bool _hasPreviousTime;
        private float _previousTime;
        private float _lastStep = float.NaN;
        private float _maxStep;
        private int _stepSpikeCount;
        private bool _stepSpikeThisFrame;
    }
}
