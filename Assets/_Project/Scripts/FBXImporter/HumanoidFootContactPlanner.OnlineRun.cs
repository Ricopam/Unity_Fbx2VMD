using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class HumanoidFootContactPlanner
    {
        // ApplyContactRun 온라인 경로의 런 상태와 프레임 처리를 묶은 세션.
        sealed class OnlineContactRun
        {
            readonly IReadOnlyList<Vector3> _sourcePoints;
            readonly IReadOnlyList<Vector3> _targetPoints;
            readonly IReadOnlyList<Vector3> _sourceToes;
            readonly IReadOnlyList<Vector3> _toeSegments;
            readonly IReadOnlyList<bool> _toePivot;
            readonly Vector3[] _corrections;
            readonly Quaternion _sourceToTargetRotation;
            readonly Quaternion _targetToSourceRotation;
            readonly int _releaseFrames;
            readonly HumanoidFootAnchorPolicy[] _policies;
            readonly float _pinReleaseDistance;

            Vector3 _desiredVertex;
            Vector3 _previousSourceVertex;
            Vector3 _followVertex;
            Vector3 _releaseResidual = Vector3.zero;
            int _framesSinceRelease = int.MaxValue;
            bool _pinned;
            bool _pinBlocked;
            bool _pinPivot;
            Vector3 _pinnedVertex = Vector3.zero;
            Vector3 _pinSourceVertex = Vector3.zero;

            internal OnlineContactRun(
                IReadOnlyList<Vector3> sourcePoints,
                IReadOnlyList<Vector3> targetPoints,
                IReadOnlyList<Vector3> sourceToes,
                IReadOnlyList<Vector3> toeSegments,
                IReadOnlyList<bool> toePivot,
                Vector3[] corrections,
                Quaternion sourceToTargetRotation,
                Quaternion targetToSourceRotation,
                int releaseFrames,
                HumanoidFootAnchorPolicy[] policies,
                float pinReleaseDistance)
            {
                _sourcePoints = sourcePoints;
                _targetPoints = targetPoints;
                _sourceToes = sourceToes;
                _toeSegments = toeSegments;
                _toePivot = toePivot;
                _corrections = corrections;
                _sourceToTargetRotation = sourceToTargetRotation;
                _targetToSourceRotation = targetToSourceRotation;
                _releaseFrames = releaseFrames;
                _policies = policies;
                _pinReleaseDistance = pinReleaseDistance;
            }

            internal void Run(int runStart, int runEnd)
            {
                // 추종 궤적은 항상 발목·발끝 중점 기준(기존 동작과 동일).
                // 핀은 롤 프레임에서 발끝을 고정점으로 잡되 중점 목표로 환산해
                // 발목이 심은 발끝 주위를 회전하도록 함. 기준 전환은 핀 경계에서만
                // 일어나고 모든 전이는 연속으로 유지됨.
                _desiredVertex = _targetPoints[runStart];
                _previousSourceVertex = _sourcePoints[runStart];
                // 핀과 무관하게 원본 스텝을 계속 누적하는 순수 추종 궤적.
                // 핀 해제 시 이 궤적으로 수렴해야 런 끝점이 원본 총이동과 일치함.
                _followVertex = _desiredVertex;
                for (int index = runStart; index <= runEnd; index++)
                {
                    bool pivot = _toePivot[index];
                    bool wantsPin = _policies[index] == HumanoidFootAnchorPolicy.Pinned && !_pinBlocked;
                    bool released = _pinned && !wantsPin;
                    // 방침이 Pinned가 아닌 프레임에서는 핀이 풀림.
                    if (released)
                    {
                        _pinned = false; }
                    // 순수 추종 궤적은 핀 여부와 무관하게 매 프레임 전진함.
                    // 핀 기간의 원본 이동까지 누적해야 해제 수렴 끝점이 맞음.
                    AdvanceFollowVertex(index);
                    if (_pinned)
                    {
                        ApplyPinnedFrame(index, pivot); } else
                    {
                        ApplyUnpinnedFrame(index, pivot, wantsPin, released); }
                    StoreCorrection(index); } }

            void AdvanceFollowVertex(int index)
            {
                Vector3 step = _sourcePoints[index] - _previousSourceVertex;
                step.y = 0f;
                _followVertex += _sourceToTargetRotation * step;
                _previousSourceVertex = _sourcePoints[index];
            }

            void StoreCorrection(int index)
            {
                Vector3 worldCorrection = _desiredVertex - _targetPoints[index];
                worldCorrection.y = 0f;
                Vector3 rootSpaceCorrection = _targetToSourceRotation * worldCorrection;
                rootSpaceCorrection.y = 0f;
                _corrections[index] = rootSpaceCorrection;
            }

            void ApplyPinnedFrame(int index, bool pivot)
            {
                if (pivot != _pinPivot)
                {
                    // 핀 기준 전환: 같은 물리 위치로 환산해 팝을 막음.
                    // 목표 자세에서 발끝은 중점보다 세그먼트 절반만큼 앞에 있음.
                    _pinnedVertex += _toeSegments[index] * 0.5f *
                        (pivot ? 1f : -1f);
                    _pinPivot = pivot;
                    _pinSourceVertex = pivot
                        ? _sourceToes[index]
                        : _sourcePoints[index];
                }
                // 발끝 핀은 중점 목표를 핀 발끝 - 세그먼트/2로 둬서
                // 롤 중 발목 회전을 허용하면서 발끝만 고정함.
                _desiredVertex = _pinPivot
                    ? _pinnedVertex - _toeSegments[index] * 0.5f : _pinnedVertex;
                Vector3 sourceVertex = _pinPivot
                    ? _sourceToes[index] : _sourcePoints[index];
                if (_pinReleaseDistance > 0f && HorizontalDistance(sourceVertex, _pinSourceVertex) >= _pinReleaseDistance)
                {
                    // 원본 지지점이 핀 구간에서 실제로 움직이면 의도 추정
                    // 오류로 보고 추종을 재개함.
                    _pinned = false;
                    _pinBlocked = true;
                    // 이 프레임은 핀 목표로 적용되므로 잔차 캡처를 여기서 함.
                    _releaseResidual = _desiredVertex - _followVertex;
                    _framesSinceRelease = 0; } }

            void ApplyUnpinnedFrame(int index, bool pivot, bool wantsPin, bool released)
            {
                if (released)
                {
                    // 핀이 풀린 프레임의 잔차를 잡아 추종 궤적까지
                    // 블렌드할 양으로 남김. 즉시 점프하면 팝이 됨.
                    _releaseResidual = _desiredVertex - _followVertex;
                    _framesSinceRelease = 0; } else if (_framesSinceRelease <= _releaseFrames)
                {
                    _framesSinceRelease++; }
                if (_framesSinceRelease > _releaseFrames)
                {
                    _desiredVertex = _followVertex; } else
                {
                    float blendT = _releaseFrames > 0
                        ? Mathf.Clamp01( _framesSinceRelease / (_releaseFrames + 1f)) : 1f;
                    float smooth = blendT * blendT * (3f - 2f * blendT);
                    _desiredVertex = _followVertex + _releaseResidual * (1f - smooth); }
                if (wantsPin)
                {
                    // 핀 진입 시 추종 위치 그대로를 앵커로 삼아 경계 연속 유지.
                    // 발끝이 심어진 프레임이면 발끝 기준으로 핀을 잡음.
                    _pinned = true;
                    _pinPivot = pivot;
                    _pinnedVertex = pivot
                        ? _desiredVertex + _toeSegments[index] * 0.5f : _desiredVertex;
                    _pinSourceVertex = pivot
                        ? _sourceToes[index] : _sourcePoints[index]; } }
        }
    }
}
