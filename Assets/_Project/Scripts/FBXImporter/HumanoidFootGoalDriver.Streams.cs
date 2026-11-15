using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class HumanoidFootGoalDriver
    {

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
            ClassifyIntentList(
                isLeft ? intents?.Left : intents?.Right, frame,
                ref confident, ref slide, ref uncertain);
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

        private static void ClassifyIntentList(
            IReadOnlyList<HumanoidFootContactIntent> sideIntents, int frame,
            ref bool confident, ref bool slide, ref bool uncertain)
        {
            if (sideIntents == null)
            {
                return;
            }
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
    }
}
