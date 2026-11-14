using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class HumanoidFootGrounding
    {
        internal bool IsPrepared { get; private set; }
        internal bool HasGround { get; private set; }
        internal bool UsedPhysicalReach { get; private set; }
        internal float MaximumTargetError { get; private set; }
        internal float MinimumSoleClearance { get; private set; }
        internal float MaximumSupportedContactError { get; private set; }
        internal int FullySupportedContactCount { get; private set; }
        internal string EvaluationStage { get; private set; } = "not_started";
        internal bool RawGatePass { get; private set; }
        internal float PelvisOffset { get; private set; }
        internal float PelvisMaximumOffset { get; private set; }
        internal HumanoidFootGroundingLegDiagnostic LeftDiagnostic => _left.Diagnostic;
        internal HumanoidFootGroundingLegDiagnostic RightDiagnostic => _right.Diagnostic;
        internal int UnresolvedSupportPairCount => _left.UnresolvedPairCount + _right.UnresolvedPairCount;
        internal int InterpolatedSupportContactCount => _left.InterpolatedContactCount + _right.InterpolatedContactCount;

        // 본을 움직이지 않고 현재 프레임의 지면·지지 가중치·밑창 측정값만 갱신함.
        // gate 보정을 적용하지 않는 경로(goal 드라이브)에서 표면 스냅샷과 진단이
        // 마지막 gate 평가나 초기값을 재사용하지 않게 하는 평가 전용 단계임.
        internal bool TryEvaluate(float timeSeconds, HumanoidGroundResponse ground)
        {
            ResetEvaluationState();
            if (!IsPrepared || _hips == null || ground == null ||
                float.IsNaN(timeSeconds) || float.IsInfinity(timeSeconds) || timeSeconds < 0f)
                return false;

            _left.CapturePose();
            _right.CapturePose();
            Physics.SyncTransforms();
            float frame = timeSeconds * _frameRate;
            EvaluationStage = "target";
            if (!_left.TryCalculateTarget(frame, ground, _humanScale) ||
                !_right.TryCalculateTarget(frame, ground, _humanScale))
                return false;

            HasGround = _left.HasGround || _right.HasGround;
            EvaluationStage = "measure";
            if (!_left.TryMeasure(out float leftClearance, out float leftContact, out int leftCount) ||
                !_right.TryMeasure(out float rightClearance, out float rightContact, out int rightCount))
                return false;

            MinimumSoleClearance = Mathf.Min(leftClearance, rightClearance);
            MaximumSupportedContactError = Mathf.Max(leftContact, rightContact);
            FullySupportedContactCount = leftCount + rightCount;
            return true;
        }

        // 발목 로컬 좌표의 밑창 후보 정점 — goal 드라이브의 밑창 인식 지면 스냅이 재사용함.
        // 샘플러의 대여 버퍼를 복제해 넘기므로 이후 갱신과 무관하게 안정적임.
        internal bool TryGetSoleProbeLocalOffsets(bool isLeft, out Vector3[] localPoints)
        {
            localPoints = null;
            if (!IsPrepared)
            {
                return false;
            }
            Vector3[] points = (isLeft ? _left : _right).Sampler.LocalSolePoints;
            if (points == null || points.Length == 0)
            {
                return false;
            }
            localPoints = (Vector3[])points.Clone();
            return true;
        }

        internal bool TryGetSoleLiftTarget(bool isLeft, Vector3 candidateWorld, Vector3 planePoint,
            Vector3 planeNormal, out Vector3 target)
        {
            target = default;
            if (!IsPrepared)
                return false;

            var leg = isLeft ? _left : _right;
            // gate 롤백이나 goal 적용 이후의 실제 밑창으로 관통을 판단함.
            if (!leg.Sampler.TrySample())
                return false;

            return HumanoidSoleGroundConstraint.TryLiftAbovePlane(
                candidateWorld, leg.Foot.rotation, leg.Sampler.LocalSolePoints,
                leg.Foot.lossyScale.x, planePoint, planeNormal, out target, out _);
        }

        private void ResetEvaluationState()
        {
            HasGround = false;
            UsedPhysicalReach = false;
            RawGatePass = false;
            PelvisOffset = 0f;
            PelvisMaximumOffset = 0f;
            EvaluationStage = "preconditions";
            MaximumTargetError = 0f;
            MinimumSoleClearance = 0f;
            MaximumSupportedContactError = 0f;
            FullySupportedContactCount = 0;
            _left.Diagnostic.Reset();
            _right.Diagnostic.Reset();
        }
    }
}
