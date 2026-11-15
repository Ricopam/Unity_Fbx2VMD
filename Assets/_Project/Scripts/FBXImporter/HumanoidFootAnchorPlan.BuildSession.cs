using System;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class HumanoidFootAnchorPlan
    {
        // TryBuild의 프레임 루프가 공유하는 가변 상태와 단계를 묶은 작업 세션.
        sealed class BuildSession
        {
            readonly Transform _foot;
            readonly HumanoidFootSoleSampler _sampler;
            readonly Vector3[][] _source;
            readonly Vector2[] _weights;
            readonly Quaternion _sourceRotation;
            readonly float _scaleRatio;
            readonly float _frameRate;
            readonly float _clipLength;
            readonly Action<float> _evaluate;
            readonly Quaternion[] _sourceFrames;
            readonly Quaternion? _localFootFrame;
            readonly HumanoidFootAnchorPolicy[] _anchorPolicies;
            readonly float _pinReleaseSourceDistance;
            readonly float _anchorTrackStep;
            readonly HumanoidFootAnchorPlan _result;
            readonly Contact[] _states = new Contact[2];
            readonly Vector3[] _offsets = new Vector3[2];
            readonly Vector3[] _emitted = new Vector3[2];
            readonly bool[] _emittedValid = new bool[2];
            readonly Contact[] _emittedContact = new Contact[2];
            readonly int[] _emittedPoint = { -1, -1 };
            readonly int[] _emittedFrame = new int[2];
            readonly float[] _emittedWeight = new float[2];
            Pair _pair;
            bool _wasOverlapping;
            int _frame;
            bool _hasSample;
            Vector3 _previousPrimaryPoint;
            bool _hasPreviousPrimaryPoint;
            bool _canAlign;
            int _preservedPrimary;
            int _secondaryChannel;
            Vector3? _secondaryStart;

            internal BuildSession(
                Transform foot, HumanoidFootSoleSampler sampler,
                Vector3[][] source, Vector2[] weights, Quaternion sourceRotation,
                float scaleRatio, float frameRate, float clipLength, Action<float> evaluate,
                Quaternion[] sourceFrames, Quaternion? localFootFrame,
                HumanoidFootAnchorPolicy[] anchorPolicies,
                float pinReleaseSourceDistance, float anchorTrackStep)
            {
                _foot = foot;
                _sampler = sampler;
                _source = source;
                _weights = weights;
                _sourceRotation = sourceRotation;
                _scaleRatio = scaleRatio;
                _frameRate = frameRate;
                _clipLength = clipLength;
                _evaluate = evaluate;
                _sourceFrames = sourceFrames;
                _localFootFrame = localFootFrame;
                _anchorPolicies = anchorPolicies;
                _pinReleaseSourceDistance = pinReleaseSourceDistance;
                _anchorTrackStep = anchorTrackStep;
                _result = new HumanoidFootAnchorPlan(weights.Length);
            }

            internal HumanoidFootAnchorPlan Result => _result;

            bool TryUpdateOffset(int channel, int point)
            {
                if (_sourceFrames == null) return true;
                if (!_sampler.TryGetLocalPoint(point, out Vector3 localPoint)) return false;
                // 준비한 발 기준축의 원본 단위 offset을 고정하여 회전 기여와 배율을 한 번만 적용함.
                _offsets[channel] = Quaternion.Inverse(_localFootFrame.Value) * (localPoint * _foot.lossyScale.x / _scaleRatio);
                return true;
            }

            Vector3 SourcePoint(int channel) => _sourceFrames == null ? _source[channel][_frame] :
                _source[0][_frame] + _sourceFrames[_frame] * _offsets[channel];

            bool TrySample()
            {
                if (_hasSample) return true;
                _evaluate(Mathf.Min(_frame / _frameRate, _clipLength));
                _hasSample = _sampler.TrySample();
                return _hasSample;
            }

            internal bool Run()
            {
                for (_frame = 0; _frame < _weights.Length; _frame++)
                {
                    _hasSample = false;
                    if (!AcquireContacts()) return false;
                    ApplyAnchorPolicies();
                    bool overlapping = _states[0] != null && _states[1] != null;
                    _previousPrimaryPoint = Vector3.zero;
                    _hasPreviousPrimaryPoint = false;
                    if (overlapping && !_wasOverlapping && !HandleOverlapEntry()) return false;
                    if (!overlapping) _pair = null;
                    _wasOverlapping = overlapping;
                    _canAlign = false;
                    _preservedPrimary = -1;
                    if (_pair != null) AlignPair();
                    _secondaryChannel = _canAlign ? 1 - _pair.Primary : -1;
                    _secondaryStart = null;
                    EmitFrames();
                    if (_secondaryChannel >= 0 && _anchorTrackStep > 0f) ConstrainSecondary();
                }
                return true;
            }

            bool AcquireContacts()
            {
                for (int channel = 0; channel < 2; channel++)
                {
                    if (_weights[_frame][channel] <= 0f) _states[channel] = null;
                    else if (_states[channel] == null)
                    {
                        if (!TrySample() || !_sampler.TrySelectContact(channel == 1, Vector3.up,
                                out int point, out Vector3 anchor)) return false;
                        // 짧은 재획득이면 직전 방출 앵커에 정점 간 물리 변위를 더해 시드하여
                        // 새 정점이 재샘플 위치로 발을 끌어당기는 팝을 방지함.
                        if (_emittedValid[channel] &&
                            _emittedWeight[channel] >= MinReacquireSeedWeight &&
                            _frame - _emittedFrame[channel] <= MaxReacquireGapFrames &&
                            _emittedPoint[channel] >= 0 &&
                            _sampler.TryGetLocalPoint(_emittedPoint[channel], out Vector3 previousLocal) &&
                            _sampler.TryGetLocalPoint(point, out Vector3 currentLocal))
                        {
                            anchor = _emitted[channel] + Vector3.ProjectOnPlane(
                                _foot.rotation * ((currentLocal - previousLocal) * _foot.lossyScale.x),
                                Vector3.up);
                        }
                        if (!TryUpdateOffset(channel, point)) return false;
                        _states[channel] = new Contact(_frame, point, anchor, SourcePoint(channel));
                    }
                }
                return true;
            }

            void ApplyAnchorPolicies()
            {
                for (int channel = 0; channel < 2; channel++)
                {
                    _states[channel]?.ApplyAnchorPolicy(SourcePoint(channel), _sourceRotation,
                        _scaleRatio,
                        _anchorPolicies == null
                            ? HumanoidFootAnchorPolicy.Free
                            : _anchorPolicies[_frame],
                        _pinReleaseSourceDistance);
                }
            }

            bool HandleOverlapEntry()
            {
                if (!TrySample()) return false;
                int primary = _states[0].StartFrame <= _states[1].StartFrame ? 0 : 1;
                if (_frame > 0 && _emittedValid[primary] && _emittedContact[primary] == _states[primary] &&
                    !RecapturePreviousPrimaryPoint(primary))
                    return false;
                Vector3 previousRear = SourcePoint(0), previousFront = SourcePoint(1);
                if (!TryCreatePair(_foot, _sampler, _states, _source, _frame, _sourceRotation, out _pair))
                    _result.RecordIssue(_frame, "공동 접촉 자세 또는 지상 방향을 준비하지 못함");
                else
                {
                    for (int channel = 0; channel < 2; channel++)
                    {
                        if (!TryUpdateOffset(channel, _states[channel].Point)) return false;
                        // 핀 고정 접촉은 기준점을 현재 원본 위치로 재설정해 앵커가 움직이지 않게 함.
                        // 비핀 접촉은 점 교체에 따른 기존 앵커 이동을 유지하고 원본 기준점 변경만 상쇄함.
                        _states[channel].SourcePoint = _states[channel].Pinned
                            ? SourcePoint(channel)
                            : _states[channel].SourcePoint + SourcePoint(channel) -
                                (channel == 0 ? previousRear : previousFront);
                    }
                }
                return true;
            }

            // 이전 프레임의 주 접촉 로컬 포인트를 재평가해 월드 위치로 보관한다.
            bool RecapturePreviousPrimaryPoint(int primary)
            {
                _evaluate(Mathf.Min((_frame - 1) / _frameRate, _clipLength));
                if (_sampler.TrySample() && _sampler.TryGetLocalPoint(_states[primary].Point, out Vector3 previousLocalPoint))
                {
                    _previousPrimaryPoint = _foot.TransformPoint(previousLocalPoint);
                    _hasPreviousPrimaryPoint = true;
                }
                _evaluate(Mathf.Min(_frame / _frameRate, _clipLength));
                return _sampler.TrySample();
            }

            void AlignPair()
            {
                Vector3 direction = Vector3.ProjectOnPlane(_sourceRotation * (_source[1][_frame] - _source[0][_frame]), Vector3.up);
                if (direction.sqrMagnitude >= 0.00000001f)
                {
                    float yaw = Vector3.SignedAngle(_pair.SourceDirection, direction, Vector3.up);
                    Vector3 anchor = _states[_pair.Primary].GetAnchor(SourcePoint(_pair.Primary), _sourceRotation, _scaleRatio);
                    HumanoidFootAnchorPolicy primaryPolicy = _anchorPolicies == null
                        ? HumanoidFootAnchorPolicy.Free : _anchorPolicies[_frame];
                    TryPreservePrimaryOnPointSwap(anchor);
                    if (_anchorTrackStep > 0f && _emittedValid[_pair.Primary] && _emittedContact[_pair.Primary] == _states[_pair.Primary] && _preservedPrimary != _pair.Primary)
                        anchor = MoveEmittedAnchor(_emitted[_pair.Primary], anchor, _anchorTrackStep);
                    int secondary = 1 - _pair.Primary;
                    // 보조 접촉은 최종 주 앵커를 기준으로 정렬해 이동 상한에 따른 간격 불일치를 막음.
                    Vector3 secondaryAnchor = anchor + Quaternion.AngleAxis(yaw, Vector3.up) * _pair.Offset;
                    // 핀 고정은 원본 추종만 멈추며, 쌍 간격과 양립 불가한 앵커를 그대로 두지 않음.
                    // 정합 위치로 한 번 접은 뒤 기준점을 현재 원본으로 옮겨 고정 상태를 유지함.
                    bool secondaryMisaligned = _states[secondary].Pinned && Vector3.ProjectOnPlane(_states[secondary].Anchor - secondaryAnchor, Vector3.up).magnitude > _foot.lossyScale.x * 0.005f;
                    if (!_states[secondary].Pinned || secondaryMisaligned)
                    {
                        _states[secondary].Anchor = secondaryAnchor;
                        _states[secondary].SourcePoint = SourcePoint(secondary);
                    }
                    _canAlign = true;
                }
                else
                {
                    _result.RecordIssue(_frame, "원본 발 방향의 지상 투영이 소실됨");
                    _pair = null;
                }
            }

            // 정점 교체의 실제 이동과 맞는 앵커 이동만 발목 자세 보상으로 허용해 주 채널 보존을 표시한다.
            void TryPreservePrimaryOnPointSwap(Vector3 anchor)
            {
                if (!_hasPreviousPrimaryPoint || _result._frames[_pair.Primary][_frame - 1].Point == _states[_pair.Primary].Point ||
                    !_sampler.TryGetLocalPoint(_states[_pair.Primary].Point, out Vector3 currentLocalPoint))
                    return;
                Vector3 anchorShift = Vector3.ProjectOnPlane(anchor - _emitted[_pair.Primary], Vector3.up);
                Vector3 pointShift = Vector3.ProjectOnPlane( _foot.TransformPoint(currentLocalPoint) - _previousPrimaryPoint, Vector3.up);
                if (Vector3.Distance(anchorShift, pointShift) <= _foot.lossyScale.x * 0.005f)
                    _preservedPrimary = _pair.Primary;
            }

            void EmitFrames()
            {
                for (int channel = 0; channel < 2; channel++)
                {
                    if (_states[channel] == null)
                    {
                        _result._frames[channel][_frame] = new Frame(-1, Vector3.zero, false);
                        // 직전 방출 앵커·정점을 유지해 짧은 재획득 시드에 사용함.
                        continue; }
                    HumanoidFootAnchorPolicy policy = _anchorPolicies == null
                        ? HumanoidFootAnchorPolicy.Free : _anchorPolicies[_frame];
                    Vector3 anchor = _states[channel].GetAnchor( SourcePoint(channel), _sourceRotation, _scaleRatio);
                    // 정점이 바뀐 프레임은 방출 기준점을 정점 이동량만큼 먼저 옮겨
                    // 같은 물리 위치끼리 비교하도록 함.
                    if (_emittedValid[channel] && _emittedContact[channel] == _states[channel] && _frame > 0 && TrySample() && _result._frames[channel][_frame - 1].Point >= 0 && _result._frames[channel][_frame - 1].Point != _states[channel].Point && _sampler.TryGetLocalPoint(_result._frames[channel][_frame - 1].Point, out Vector3 replacedLocal) && _sampler.TryGetLocalPoint(_states[channel].Point, out Vector3 currentLocal))
                    {
                        _emitted[channel] += Vector3.ProjectOnPlane( _foot.rotation * ((currentLocal - replacedLocal) * _foot.lossyScale.x), Vector3.up); }
                    // 방출 앵커는 프레임당 상한+갭 비례 회수로만 움직여 방침 전환·핀 시작
                    // 경계에서 누적 지연이 한 번에 방출되는 팝을 막음.
                    // 정점 교체의 실제 이동과 어울리는 앵커 이동은 예외로 둠.
                    if (_anchorTrackStep > 0f && _emittedValid[channel] && _emittedContact[channel] == _states[channel] && _preservedPrimary != channel)
                    {
                        if (channel == _secondaryChannel) _secondaryStart = _emitted[channel];
                        anchor = MoveEmittedAnchor(_emitted[channel], anchor, _anchorTrackStep); }
                    _result._frames[channel][_frame] = new Frame(_states[channel].Point, anchor, _canAlign);
                    _emitted[channel] = anchor;
                    _emittedValid[channel] = true;
                    _emittedContact[channel] = _states[channel];
                    _emittedPoint[channel] = _states[channel].Point;
                    _emittedFrame[channel] = _frame;
                    _emittedWeight[channel] = _weights[_frame][channel]; } }

            void ConstrainSecondary()
            {
                Vector3 secondary = ConstrainPairAnchor(_emitted[_pair.Primary], _emitted[_secondaryChannel],
                    _pair.Offset.magnitude, _secondaryStart, _anchorTrackStep * AnchorCatchUpStepFactor);
                _emitted[_secondaryChannel] = secondary;
                _result._frames[_secondaryChannel][_frame] =
                    new Frame(_states[_secondaryChannel].Point, secondary, true);
            }
        }
    }
}
