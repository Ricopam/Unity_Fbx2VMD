using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 원본 발 목표(goal)로 타겟 다리를 IK 구동하는 실험 경로임.
    /// useIkGoalFootDrive 플래그로만 진입하고 alpha=0이면 어떤 본도 건드리지 않음.
    /// pin이 확정된 프레임만 목표를 주장하고 나머지는 기존 경로 출력을 그대로 둠.
    /// </summary>
    internal sealed partial class HumanoidFootGoalDriver
    {
        // 앞꿈치 지지 발끝 하향 soft-cap — HumanoidFootGrounding.Leg과 동일값.
        internal const float ToePitchSoftKneeDeg = 25f;
        internal const float MaximumSupportedToePitch = 35f;
        // 무릎 과신전을 막는 신전 감쇠 경계 — 생산 경로의 감쇠 임계와 동일함.
        internal const float ExtensionDampingThresholdRadians = 2.8f;
        // goal 전이의 UE FInterpTo식 지수 평활 속도(1/s).
        internal const float GoalPositionSmoothingSpeed = 18f;
        // 핀 가중의 선형 전이 속도(1/s) — 지수 감쇠는 첫 프레임에 33%가 움직여
        // gate↔드라이버 자세 갭(수십mm)이 한계를 넘는 단발 스텝으로 노출됨.
        // 고정 속도 선형이면 프레임당 스텝이 갭/전이 프레임 수로 균일하게 상한 아래가 됨.
        internal const float PinWeightChangePerSecond = 3.5f;
        // 두 발 수평 간격 하한 — gate/기권 구간의 근육 리타겟 출력이 발을 한 점에
        // 모으는 결함(실측 21mm, 소스 목표는 130mm 분리)의 복구 발화 기준임.
        // 발 폭 이하인 ~21mm만 걸러내고 교차 스탠스(50mm+)는 건드리지 않게 여유를 둠.
        internal const float MinimumFootSeparationMeters = 0.05f;
        // 이 각도 이하의 무릎 굽힘은 bend normal이 수치적으로 불안정해 마지막 유효값을 유지함.
        private const float MinimumStableBendSine = 0.0175f;
        // 밑창 지면 정렬의 피치 가중 구간 — 이 각도 이상 기울면 롤 스탠스로 간주해 정렬하지 않음.
        private const float SoleAlignPitchSpanDeg = 45f;
        private const float MinimumAxisSquaredMagnitude = 0.000001f;

        internal bool IsInitialized { get; private set; }
        // 마지막 TryApply가 실제로 골반에 적용한 수직 오프셋(m). 진단 열 기록용임.
        internal float LastAppliedPelvisOffset { get; private set; }

        private Animator _animator;
        private Transform _hips;
        private float _frameRate;
        private float _clipLengthSeconds;
        private HumanoidFootGoalStream _goals;
        // goal 복원용 소스 골반 궤적 — rootSpace 베이크와 같은 프레임.
        private IReadOnlyList<Vector3> _sourceHipsPositions;
        private IReadOnlyList<Quaternion> _sourceHipsRotations;
        // 지면 높이 두 벌 — 소스 월드값은 게이트 증거용, 골반 상대값은 goal 스냅용.
        private float[] _leftFloorWorldY = Array.Empty<float>();
        private float[] _rightFloorWorldY = Array.Empty<float>();
        private float[] _leftFloorRootSpaceY = Array.Empty<float>();
        private float[] _rightFloorRootSpaceY = Array.Empty<float>();
        private readonly LegDrive _left = new LegDrive();
        private readonly LegDrive _right = new LegDrive();
        private float _lastTimeSeconds = float.NaN;
        // 발 모임 복구의 선형 weight — 핀 weight와 같은 속도로 오르내려
        // 복구 개입·해제가 단발 점프로 보이지 않게 함.
        private float _separationWeight;

        // stabilizer가 보존한 원본 궤적·의도·표식에서 goal 스트림과 핀 구간을 전부 선계산함.
        // 반환 false는 "데이터 부족으로 실험 경로 비활성"이며 예외가 아님.
        internal bool TryInitialize(
            Animator targetAnimator,
            HumanoidFootContactStabilizer stabilizer,
            float frameRate)
        {
            Clear();
            if (targetAnimator == null || stabilizer == null || !stabilizer.IsInitialized ||
                frameRate <= 0f)
            {
                return false;
            }

            IReadOnlyList<HumanoidFootContactSample> samples = stabilizer.SourceSamples;
            IReadOnlyList<Vector3> hipsPositions = stabilizer.SourceHipsPositions;
            IReadOnlyList<Quaternion> hipsRotations = stabilizer.SourceHipsRotations;
            if (samples == null || hipsPositions == null || hipsRotations == null ||
                samples.Count == 0 || hipsPositions.Count != samples.Count ||
                hipsRotations.Count != samples.Count)
            {
                return false;
            }

            return TryInitializeCore(targetAnimator, samples, hipsPositions, hipsRotations,
                stabilizer.IntentEstimate, stabilizer.IntentLabels,
                stabilizer.SourceHumanScale, frameRate);
        }

        // stabilizer에서 떼어낸 데이터 주입 경로 — EditMode 검증과 재사용을 분리함.
        internal bool TryInitializeCore(
            Animator targetAnimator,
            IReadOnlyList<HumanoidFootContactSample> samples,
            IReadOnlyList<Vector3> hipsPositions,
            IReadOnlyList<Quaternion> hipsRotations,
            HumanoidFootContactIntentEstimate intents,
            HumanoidFootContactIntentLabelSet labels,
            float sourceHumanScale,
            float frameRate)
        {
            Clear();
            if (targetAnimator == null || samples == null ||
                hipsPositions == null || hipsRotations == null ||
                samples.Count == 0 || hipsPositions.Count != samples.Count ||
                hipsRotations.Count != samples.Count || frameRate <= 0f)
            {
                return false;
            }

            _animator = targetAnimator;
            _hips = targetAnimator.GetBoneTransform(HumanBodyBones.Hips);
            if (_hips == null ||
                !_left.Initialize(targetAnimator, true) ||
                !_right.Initialize(targetAnimator, false))
            {
                Clear();
                return false;
            }
            // gate와 같은 해부학 기준 법선 — 무릎이 거의 펴진 구간의 자세 cross는
            // 잡음성이고 부호가 뒤집혀 무릎을 반대쪽으로 붕괴시키므로 기준 법선과의
            // 가중 정합으로 굽힘면을 고정함. 취득 실패 시 기존 캐시 경로로 폴백함.
            if (HumanoidLegBendCalibration.TryCapture(targetAnimator,
                    out Vector3 leftReference, out Vector3 rightReference))
            {
                _left.SetBendReference(leftReference);
                _right.SetBendReference(rightReference);
            }

            _frameRate = frameRate;
            _clipLengthSeconds = (samples.Count - 1) / frameRate;
            // goal 복원은 소스 골반 프레임을 씀 — rootSpace가 소스 골반 기준으로
            // 구워졌으므로 타겟 골반(실측 높이 1.03 vs 소스 0.77)을 쓰면 목표가
            // 그 차이만큼 어긋남. 소스 골반 복원은 베이크의 정확한 역변환이라
            // 목표가 원본 월드 발 위치와 일치하고 골반 보정과도 무관하게 고정됨.
            _sourceHipsPositions = hipsPositions;
            _sourceHipsRotations = hipsRotations;
            _goals = BuildGoalStream(samples, hipsPositions, hipsRotations);
            BuildPinArrays(samples, hipsPositions, hipsRotations, intents,
                labels, frameRate, sourceHumanScale);
            IsInitialized = true;
            return true;
        }

        // grounding이 측정한 발목 로컬 밑창 정점을 넘겨받음 — 발끝 본만으로는
        // 밑창 정점이 지면 아래로 파고드는 침투를 스냅할 수 없기 때문임.
        // 미설정 시 기존 발끝 스냅만 적용됨(폴백 동작 불변).
        internal void SetSoleProbes(bool isLeft, Vector3[] localPoints)
        {
            (isLeft ? _left : _right).SetSoleProbes(localPoints);
        }

        internal void Clear()
        {
            IsInitialized = false;
            LastAppliedPelvisOffset = 0f;
            _animator = null;
            _hips = null;
            _frameRate = 0f;
            _clipLengthSeconds = 0f;
            _goals = null;
            _sourceHipsPositions = null;
            _sourceHipsRotations = null;
            _leftFloorWorldY = _rightFloorWorldY = Array.Empty<float>();
            _leftFloorRootSpaceY = _rightFloorRootSpaceY = Array.Empty<float>();
            _left.Clear();
            _right.Clear();
            _lastTimeSeconds = float.NaN;
            _separationWeight = 0f;
        }

        // goal 스트림은 stabilizer의 원본 샘플과 같은 인덱스 — 관측 산출물과 동일 데이터임.
        private static HumanoidFootGoalStream BuildGoalStream(
            IReadOnlyList<HumanoidFootContactSample> samples,
            IReadOnlyList<Vector3> hipsPositions,
            IReadOnlyList<Quaternion> hipsRotations)
        {
            var stream = new HumanoidFootGoalStream();
            for (int index = 0; index < samples.Count; index++)
            {
                HumanoidFootContactSample sample = samples[index];
                stream.Record(index, true, hipsPositions[index], hipsRotations[index],
                    sample.LeftFoot, sample.LeftFootFrame ?? Quaternion.identity, sample.LeftToes);
                stream.Record(index, false, hipsPositions[index], hipsRotations[index],
                    sample.RightFoot, sample.RightFootFrame ?? Quaternion.identity, sample.RightToes);
            }
            return stream;
        }

        // pin·grounded 판정을 프레임 배열로 선계산 — 탐색(seek) 시에도 게이트 상태가
        // 시퀀스 오염 없이 재현됨. 증거는 전부 원본 공간 측정값임.
        private void BuildPinArrays(
            IReadOnlyList<HumanoidFootContactSample> samples,
            IReadOnlyList<Vector3> hipsPositions,
            IReadOnlyList<Quaternion> hipsRotations,
            HumanoidFootContactIntentEstimate intents,
            HumanoidFootContactIntentLabelSet labels,
            float frameRate, float sourceHumanScale)
        {
            _leftFloorWorldY = BuildFloorHeights(samples, intents?.Left, true);
            _rightFloorWorldY = BuildFloorHeights(samples, intents?.Right, false);
            _leftFloorRootSpaceY = ToRootSpaceFloorY(_leftFloorWorldY,
                hipsPositions, hipsRotations);
            _rightFloorRootSpaceY = ToRootSpaceFloorY(_rightFloorWorldY,
                hipsPositions, hipsRotations);
            _left.Pin = new bool[samples.Count];
            _left.Grounded = new bool[samples.Count];
            _right.Pin = new bool[samples.Count];
            _right.Grounded = new bool[samples.Count];
            float scale = sourceHumanScale > 0.0001f ? sourceHumanScale : 1f;
            var leftGate = new HumanoidFootGoalGate(frameRate, scale);
            var rightGate = new HumanoidFootGoalGate(frameRate, scale);
            for (int frame = 0; frame < samples.Count; frame++)
            {
                EvaluateSide(samples, frame, true, intents, labels, leftGate,
                    _leftFloorWorldY, frameRate, _left.Pin, _left.Grounded);
                EvaluateSide(samples, frame, false, intents, labels, rightGate,
                    _rightFloorWorldY, frameRate, _right.Pin, _right.Grounded);
            }
        }

        private static void EvaluateSide(
            IReadOnlyList<HumanoidFootContactSample> samples, int frame, bool isLeft,
            HumanoidFootContactIntentEstimate intents,
            HumanoidFootContactIntentLabelSet labels,
            HumanoidFootGoalGate gate, float[] floorWorldY, float frameRate,
            bool[] pin, bool[] grounded)
        {
            ClassifySideIntents(intents, frame, isLeft,
                out bool confident, out bool slide, out bool uncertain);
            bool? labelSupport = ResolveLabelSupport(labels, frame, isLeft);

            HumanoidFootContactSample sample = samples[frame];
            Vector3 foot = isLeft ? sample.LeftFoot : sample.RightFoot;
            Vector3 toes = isLeft ? sample.LeftToes : sample.RightToes;
            Vector3 prevFoot = frame > 0
                ? (isLeft ? samples[frame - 1].LeftFoot : samples[frame - 1].RightFoot)
                : foot;
            Vector3 planarDelta = foot - prevFoot;
            planarDelta.y = 0f;
            var evidence = new HumanoidFootGoalGate.FrameEvidence
            {
                HasConfidentPlantIntent = confident,
                HasSlideIntent = slide,
                InUncertainSpan = uncertain,
                LabelIsSupport = labelSupport,
                ToeHeightAboveFloor = toes.y - floorWorldY[frame],
                AnkleSpeed = planarDelta.magnitude * frameRate,
            };
            HumanoidFootGoalGate.GateOutput output = gate.Evaluate(evidence);
            pin[frame] = output.Pin > 0.5f;
            grounded[frame] = output.Grounded;
        }

        private static void ClassifySideIntents(
            HumanoidFootContactIntentEstimate intents, int frame, bool isLeft,
            out bool confident, out bool slide, out bool uncertain)
        {
            confident = false; slide = false; uncertain = false;
            IReadOnlyList<HumanoidFootContactIntent> sideIntents =
                isLeft ? intents?.Left : intents?.Right;
            if (sideIntents != null)
            {
                foreach (HumanoidFootContactIntent intent in sideIntents)
                {
                    if (frame < intent.StartFrame || frame >= intent.EndFrameExclusive) continue;
                    if (intent.Certainty == HumanoidFootContactIntentCertainty.Uncertain)
                    {
                        uncertain = true;
                    }
                    else if (intent.Mode == HumanoidFootContactIntentMode.Plant)
                    {
                        confident = true;
                    }
                    else
                    {
                        slide = true;
                    }
                }
            }
            IReadOnlyList<Vector2Int> uncertainSpans =
                isLeft ? intents?.LeftUncertainSpans : intents?.RightUncertainSpans;
            if (uncertainSpans != null)
            {
                foreach (Vector2Int span in uncertainSpans)
                {
                    if (frame >= span.x && frame < span.y) uncertain = true;
                }
            }
        }

        private static bool? ResolveLabelSupport(
            HumanoidFootContactIntentLabelSet labels, int frame, bool isLeft)
        {
            bool? labelSupport = null;
            IReadOnlyList<HumanoidFootContactIntentLabel> sideLabels =
                isLeft ? labels?.Left : labels?.Right;
            if (sideLabels != null)
            {
                foreach (HumanoidFootContactIntentLabel label in sideLabels)
                {
                    if (frame >= label.StartFrame && frame <= label.EndFrameInclusive)
                    {
                        labelSupport = label.IsSupport;
                    }
                }
            }
            return labelSupport;
        }

        // plant 구간은 그 구간의 최저 발끝 높이를, 나머지는 클립 최저 발끝 높이를 지면으로 씀.
        // 의도 Anchor는 발목·발끝 중점이라 지면으로 쓰면 스냅이 발을 반 발바닥 높이만큼 띄움.
        private static float[] BuildFloorHeights(
            IReadOnlyList<HumanoidFootContactSample> samples,
            IReadOnlyList<HumanoidFootContactIntent> intents, bool isLeft)
        {
            var floor = new float[samples.Count];
            float clipMinimum = MinimumToeHeight(samples, isLeft, 0, samples.Count);
            for (int frame = 0; frame < samples.Count; frame++)
            {
                floor[frame] = clipMinimum;
            }
            if (intents != null)
            {
                foreach (HumanoidFootContactIntent intent in intents)
                {
                    if (intent.Mode != HumanoidFootContactIntentMode.Plant) continue;
                    int start = Math.Max(0, intent.StartFrame);
                    int end = Math.Min(samples.Count, intent.EndFrameExclusive);
                    float contactHeight = MinimumToeHeight(samples, isLeft, start, end);
                    for (int frame = start; frame < end; frame++)
                    {
                        floor[frame] = contactHeight;
                    }
                }
            }
            return floor;
        }

        private static float MinimumToeHeight(IReadOnlyList<HumanoidFootContactSample> samples,
            bool isLeft, int start, int endExclusive)
        {
            float minimum = float.PositiveInfinity;
            for (int frame = start; frame < endExclusive; frame++)
            {
                minimum = Mathf.Min(minimum,
                    (isLeft ? samples[frame].LeftToes : samples[frame].RightToes).y);
            }
            return minimum;
        }

        // 월드 지면 높이를 프레임별 골반 상대 공간으로 변환 — goal 스냅 좌표계와 일치시킴.
        private static float[] ToRootSpaceFloorY(float[] floorWorldY,
            IReadOnlyList<Vector3> hipsPositions, IReadOnlyList<Quaternion> hipsRotations)
        {
            var rootSpace = new float[floorWorldY.Length];
            for (int frame = 0; frame < floorWorldY.Length; frame++)
            {
                Vector3 floorWorld = new Vector3(hipsPositions[frame].x,
                    floorWorldY[frame], hipsPositions[frame].z);
                rootSpace[frame] = (Quaternion.Inverse(hipsRotations[frame]) *
                    (floorWorld - hipsPositions[frame])).y;
            }
            return rootSpace;
        }

        internal void CaptureValidatedSupport(bool leftSupported, bool rightSupported)
        {
            _left.CaptureValidatedSupport(IsInitialized && leftSupported);
            _right.CaptureValidatedSupport(IsInitialized && rightSupported);
        }

        // alpha=0이면 아무것도 하지 않음 — 기존 경로와 비트 동일한 출력의 근거임.
        internal bool TryApply(float timeSeconds, float alpha,
            Vector3 leftBendNormal, Vector3 rightBendNormal,
            HumanoidGroundResponse ground = null)
        {
            LastAppliedPelvisOffset = 0f;
            if (!IsInitialized || alpha <= 0f)
            {
                return true;
            }

            int frame = HumanoidMotionFrameCalculator.CalculateFrameIndex(
                timeSeconds, _clipLengthSeconds, _frameRate);
            // 탐색 점프는 스무딩 상태를 리셋 — 이전 프레임 잔여가 새 위치로 번지지 않게 함.
            bool firstEval = float.IsNaN(_lastTimeSeconds);
            bool jumped = !firstEval &&
                Mathf.Abs(timeSeconds - _lastTimeSeconds) > 1.5f / _frameRate;
            float dt = firstEval || jumped ? 0f :
                Mathf.Max(0f, timeSeconds - _lastTimeSeconds);
            _lastTimeSeconds = timeSeconds;

            // 첫 평가는 전이 맥락이 없으므로 스냅으로 즉시 적용 — 같은 시각 재평가는
            // UpdateWeights의 dt<=0 조기 반환으로 상태를 유지해 멱등 재적용이 됨.
            UpdateSolveWeights(frame, alpha, jumped || firstEval, dt);
            if (_left.SolveWeight <= 0f && _right.SolveWeight <= 0f)
            {
                return true;
            }

            // 목표 골반은 소스 골반 궤적에서 가져옴 — rootSpace가 소스 골반 기준으로
            // 구워졌으므로 같은 프레임의 소스 골반으로 복원해야 원본 월드 발 위치가
            // 정확히 나옴. 타겟 골반은 골반 드롭의 영향을 받아 목표가 흔들림.
            Vector3 goalHipsPosition = _sourceHipsPositions[frame];
            Quaternion goalHipsRotation = _sourceHipsRotations[frame];
            // 골반 드롭은 양다리 공유 — 한쪽이라도 목표가 신장 한계를 넘으면 함께 계산함.
            _left.PrepareGoal(frame, _goals.Samples, goalHipsPosition, goalHipsRotation,
                _leftFloorRootSpaceY[frame], _leftFloorWorldY[frame], dt, _separationWeight, ground);
            _right.PrepareGoal(frame, _goals.Samples, goalHipsPosition, goalHipsRotation,
                _rightFloorRootSpaceY[frame], _rightFloorWorldY[frame], dt, _separationWeight, ground);
            float pelvisOffset = CalculatePelvisOffset(frame);
            if (Mathf.Abs(pelvisOffset) > 0f)
            {
                LastAppliedPelvisOffset =
                    pelvisOffset * Mathf.Max(_left.Weight, _right.Weight, _left.SupportWeight, _right.SupportWeight);
                _hips.position += Vector3.up * LastAppliedPelvisOffset;
            }

            // 원본 추정 지면은 접촉 스냅과 미검출 폴백에 사용함.
            // 대상 밑창 상승은 TrySolve에서 실제 지면을 우선 조회함.
            _left.SolvePreparedGoal(leftBendNormal);
            _right.SolvePreparedGoal(rightBendNormal);
            return true;
        }

        private void UpdateSolveWeights(int frame, float alpha, bool reset, float dt)
        {
            _left.UpdateWeights(alpha, _left.Pin[frame], reset, dt);
            _right.UpdateWeights(alpha, _right.Pin[frame], reset, dt);
            // gate/기권 구간의 발 모임 복구 — 근육 리타겟은 소스가 분리돼 있어도
            // 출력 발을 한 점에 모을 수 있음. 핀 weight가 0인 프레임에서도 현재
            // 자세의 수평 간격이 임계 이하면 소스 목표(원래 분리됨)쪽으로 solve를
            // 주장해 발이 겹치지 않게 함. 핀과 같은 선형 램프로 개입·해제함.
            float separationTarget =
                HorizontalFootSeparation() < MinimumFootSeparationMeters ? alpha : 0f;
            _separationWeight = reset
                ? separationTarget
                : Mathf.MoveTowards(_separationWeight, separationTarget,
                    PinWeightChangePerSecond * dt);
            _left.UpdateSupportWeight(_separationWeight, reset, dt);
            _right.UpdateSupportWeight(_separationWeight, reset, dt);
            _left.SourceSolveWeight = Mathf.Max(_left.Weight, _separationWeight);
            _right.SourceSolveWeight = Mathf.Max(_right.Weight, _separationWeight);
            _left.SolveWeight = Mathf.Max(_left.SourceSolveWeight, _left.SupportWeight);
            _right.SolveWeight = Mathf.Max(_right.SourceSolveWeight, _right.SupportWeight);
        }

        // 두 발의 수평 거리 — solve 전의 현재 출력 자세를 측정함. 수평만 보면
        // 한 발이 다른 발 위로 지나가는 정상 구간을 오탐하지 않음.
        private float HorizontalFootSeparation()
        {
            Vector3 left = _left.Foot.position;
            Vector3 right = _right.Foot.position;
            return Mathf.Sqrt((left.x - right.x) * (left.x - right.x) +
                (left.z - right.z) * (left.z - right.z));
        }

        // 이 프레임에 한 발이라도 IK가 목표를 주장하는지 — weight 갱신 전 상태로 판정해
        // 호출부가 stabilizer 이전에 gate 경로를 선택할 수 있게 함.
        // false(기권) 프레임은 alpha=0과 같은 gate 보정이 대신 적용됨.
        internal bool WouldClaim(float timeSeconds)
        {
            if (!IsInitialized)
            {
                return false;
            }
            int frame = HumanoidMotionFrameCalculator.CalculateFrameIndex(
                timeSeconds, _clipLengthSeconds, _frameRate);
            return _left.Pin[frame] || _right.Pin[frame] ||
                _left.Weight > 0f || _right.Weight > 0f;
        }

        private float CalculatePelvisOffset(int frame)
        {
            // 양발 원본 목표가 기권하면 지원 목표는 현재 발 위치이므로 새 골반 이동이 필요하지 않음.
            if (_left.SourceSolveWeight <= 0f && _right.SourceSolveWeight <= 0f) return 0f;
            bool needsLeft = TryBuildReachLeg(_left, frame,
                out HumanoidPelvisReachCalculator.Leg left);
            bool needsRight = TryBuildReachLeg(_right, frame,
                out HumanoidPelvisReachCalculator.Leg right);
            if (!needsLeft && !needsRight)
            {
                return 0f;
            }
            if (!needsLeft) left = PassiveLeg(_left);
            if (!needsRight) right = PassiveLeg(_right);
            // 골반이 목표를 쫓아 올라가지 않게 상방 이동은 0으로 묶음.
            return HumanoidPelvisReachCalculator.TryCalculateOffset(
                left, right, Vector3.up, _animator.humanScale, out float offset, out _)
                ? offset : 0f;
        }

        // weight가 0인 다리는 현재 발 위치를 목표로 둬 골반 계산이 그쪽을 억지로 내리지 않게 함.
        private static HumanoidPelvisReachCalculator.Leg PassiveLeg(LegDrive leg)
        {
            Vector3 toHip = leg.Upper.position - leg.Foot.position;
            return new HumanoidPelvisReachCalculator.Leg(toHip,
                leg.UpperLength, leg.LowerLength, 0f, 0f);
        }

        private bool TryBuildReachLeg(LegDrive leg, int frame,
            out HumanoidPelvisReachCalculator.Leg reachLeg)
        {
            reachLeg = default;
            if (leg.Weight <= 0f && leg.SupportWeight <= 0f) return false;
            Vector3 goalWorld = leg.EffectiveGoal;
            Vector3 toHip = leg.Upper.position - goalWorld;
            float distance = toHip.magnitude;
            // 감쇠 기준각은 현재 자세가 아니라 임계값으로 고정함 — 자세 이력이 섞이면
            // 신전 경계에서 같은 목표라도 프레임마다 도달 거리가 달라져 골반 드롭이
            // 진동하고 무릎이 흔들림. 임계값 기준은 min(현재각, 임계값)과 수학적으로
            // 동등하면서 감쇠 도달을 목표 거리의 순수 함수로 만듦.
            // 솔버는 본을 늘리지 않으므로 감쇠 도달 거리를 넘는 목표만 골반 드롭으로 흡수함.
            // 계산기는 본 길이 합을 넘는 MaximumReach를 거부하므로 신장 비율을 섞으면 드롭이 항상 실패함.
            if (!HumanoidPelvisReachCalculator.TryCalculateExtensionReach(
                    leg.UpperLength, leg.LowerLength, ExtensionDampingThresholdRadians,
                    distance, leg.Weight, ExtensionDampingThresholdRadians, out float dampedReach))
            {
                dampedReach = leg.Reach;
            }
            dampedReach = Mathf.Lerp(dampedReach, leg.Reach, leg.SupportWeight);
            if (distance <= dampedReach && leg.SupportWeight <= 0f)
            {
                return false;
            }
            reachLeg = new HumanoidPelvisReachCalculator.Leg(toHip,
                leg.UpperLength, leg.LowerLength, Mathf.Max(leg.Weight, leg.SupportWeight), 0f, dampedReach);
            return true;
        }

        private bool TryGetGoalSample(int frame, bool isLeft,
            out HumanoidFootGoalStream.GoalSample goal)
        {
            int index = frame * 2 + (isLeft ? 0 : 1);
            if (_goals == null || index < 0 || index >= _goals.Samples.Count)
            {
                goal = default;
                return false;
            }
            goal = _goals.Samples[index];
            return true;
        }

        // 골반 상대 goal 회전을 월드로 변환 — 골반 프레임을 매개로 루트 모션을 따라감.
        internal static Quaternion GoalFrameToWorld(HumanoidFootGoalStream.GoalSample goal,
            Quaternion hipsRotation)
        {
            return (hipsRotation * goal.RootSpaceFootRotation).normalized;
        }

        // FInterpTo식 지수 평활 — dt=0이면 현재값을 그대로 유지함.
        internal static float Smooth(float current, float target, float speed, float dt)
        {
            return target + (current - target) * Mathf.Exp(-speed * dt);
        }

        internal static Vector3 Smooth(Vector3 current, Vector3 target, float speed, float dt)
        {
            return target + (current - target) * Mathf.Exp(-speed * dt);
        }

        // 앞꿈치 지지 발끝 하향 soft-cap — 생산 경로의 tanh 압축과 동일 수학임.
        internal static Quaternion ApplyToePitchSoftCap(Quaternion footFrame,
            Vector3 groundNormal, float weight)
        {
            Vector3 forward = footFrame * Vector3.forward;
            // Asin은 ±90° 근처에서 float 오차가 0.02°까지 커져 atan2(수직, 수평)로 구함.
            float vertical = Vector3.Dot(forward, groundNormal);
            float horizontal = (forward - groundNormal * vertical).magnitude;
            float pitch = Mathf.Atan2(vertical, horizontal) * Mathf.Rad2Deg;
            if (pitch >= -ToePitchSoftKneeDeg)
            {
                return footFrame;
            }
            float span = MaximumSupportedToePitch - ToePitchSoftKneeDeg;
            float limited = -(ToePitchSoftKneeDeg +
                span * (float)Math.Tanh((-pitch - ToePitchSoftKneeDeg) / span));
            Vector3 axis = Vector3.Cross(groundNormal, forward);
            // 발끝이 지면 법선과 평행(수직 하향)하면 cross가 퇴화함 — 이때도 cap이 필요하므로
            // 같은 회전 부호를 주는 발 lateral 축으로 대체함.
            if (axis.sqrMagnitude < MinimumAxisSquaredMagnitude)
            {
                axis = footFrame * Vector3.right;
            }
            Quaternion corrected = Quaternion.AngleAxis(pitch - limited, axis.normalized) * footFrame;
            return Quaternion.Slerp(footFrame, corrected, weight);
        }

    }
}