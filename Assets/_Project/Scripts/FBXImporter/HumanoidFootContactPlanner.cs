using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 한 프레임에서 샘플링한 양발 접촉점임.
    /// </summary>
    internal readonly struct HumanoidFootContactSample
    {
        internal HumanoidFootContactSample(Vector3 left, Vector3 right)
            : this(left, left, right, right)
        {
        }

        internal HumanoidFootContactSample(
            Vector3 leftFoot,
            Vector3 leftToes,
            Vector3 rightFoot,
            Vector3 rightToes)
            : this(leftFoot, leftToes, rightFoot, rightToes, null)
        {
        }

        internal HumanoidFootContactSample(
            Vector3 leftFoot, Vector3 leftToes, Vector3 rightFoot, Vector3 rightToes,
            Vector2? relativeFootHeights)
            : this(leftFoot, leftToes, rightFoot, rightToes, relativeFootHeights, null, null)
        {
        }

        internal HumanoidFootContactSample(
            Vector3 leftFoot, Vector3 leftToes, Vector3 rightFoot, Vector3 rightToes,
            Vector2? relativeFootHeights, Quaternion? leftFootFrame, Quaternion? rightFootFrame)
        {
            LeftFoot = leftFoot;
            LeftToes = leftToes;
            RightFoot = rightFoot;
            RightToes = rightToes;
            RelativeFootHeights = relativeFootHeights;
            LeftFootFrame = leftFootFrame;
            RightFootFrame = rightFootFrame;
        }

        // 기준축이 없는 표본과 평평한 발(0)을 구분함.
        internal Vector2? RelativeFootHeights { get; }

        internal Quaternion? LeftFootFrame { get; }

        internal Quaternion? RightFootFrame { get; }

        internal Vector3 LeftFoot { get; }

        internal Vector3 LeftToes { get; }

        internal Vector3 RightFoot { get; }

        internal Vector3 RightToes { get; }

        internal Vector3 Left => (LeftFoot + LeftToes) * 0.5f;

        internal Vector3 Right => (RightFoot + RightToes) * 0.5f;
    }

    /// <summary>
    /// 원본 접촉 구간과 대상 궤적에서 고정 길이 IK용 보정 계획을 계산함.
    /// </summary>
    internal static partial class HumanoidFootContactPlanner
    {
        private const float ContactHeightMarginPerHumanScale = 1f / 30f;
        private const float ContactSpeedLimitPerHumanScale = 1f / 6f;
        private const float MinimumContactDurationSeconds = 0.1f;
        private const float ReleaseDurationSeconds = 0.1f;
        private const float MinimumQuaternionMagnitude = 0.000001f;
        private const float MinimumSegmentLength = 0.000001f;
        private const float MinimumHeightCorrectionPerHumanScale = 1f / 1000f;
        private const float ToeHeightWeight = 2f;

        internal static HumanoidFootContactPlan Build(
            IReadOnlyList<HumanoidFootContactSample> sourceSamples,
            IReadOnlyList<HumanoidFootContactSample> targetSamples,
            float frameRate,
            float sourceHumanScale,
            Quaternion sourceToTargetRotation,
            float targetHumanScale = float.NaN,
            HumanoidFootContactIntentEstimate intents = null)
        {
            ValidateSamples(sourceSamples, targetSamples, frameRate);

            float normalizedSourceHumanScale = IsFinite(sourceHumanScale) &&
                sourceHumanScale > 0f
                    ? sourceHumanScale
                    : 1f;
            float normalizedTargetHumanScale = IsFinite(targetHumanScale) &&
                targetHumanScale > 0f
                    ? targetHumanScale
                    : normalizedSourceHumanScale;
            Quaternion normalizedRotation = NormalizeRotation(sourceToTargetRotation);
            int minimumContactFrames = Mathf.Max(
                1,
                Mathf.CeilToInt(frameRate * MinimumContactDurationSeconds));
            int releaseFrames = Mathf.Max(
                1,
                Mathf.CeilToInt(frameRate * ReleaseDurationSeconds));

            SampleArrays arrays = CollectSampleArrays(sourceSamples, targetSamples);
            Vector3[] targetLeftFeet = arrays.TargetLeftFeet;
            Vector3[] targetLeftToes = arrays.TargetLeftToes;
            Vector3[] targetRightFeet = arrays.TargetRightFeet;
            Vector3[] targetRightToes = arrays.TargetRightToes;

            Quaternion targetToSourceRotation =
                Quaternion.Inverse(normalizedRotation);
            ChannelCorrections channels = BuildChannelCorrections(
                arrays, normalizedSourceHumanScale, normalizedTargetHumanScale,
                normalizedRotation, targetToSourceRotation, frameRate,
                minimumContactFrames, releaseFrames, intents);
            Vector3[] leftToeDirections = ApplyToeDirections(
                channels.LeftToeSegments, targetLeftFeet, targetLeftToes,
                channels.LeftCorrections, targetToSourceRotation);
            Vector3[] rightToeDirections = ApplyToeDirections(
                channels.RightToeSegments, targetRightFeet, targetRightToes,
                channels.RightCorrections, targetToSourceRotation);
            return new HumanoidFootContactPlan(
                channels.LeftCorrections,
                channels.RightCorrections,
                leftToeDirections,
                rightToeDirections,
                frameRate,
                channels.LeftRunCount,
                channels.RightRunCount);
        }

        struct ChannelCorrections
        {
            internal Vector3[] LeftCorrections;
            internal Vector3[] RightCorrections;
            internal Vector3[] LeftToeSegments;
            internal Vector3[] RightToeSegments;
            internal int LeftRunCount;
            internal int RightRunCount;
        }

        static ChannelCorrections BuildChannelCorrections(
            SampleArrays arrays,
            float normalizedSourceHumanScale,
            float normalizedTargetHumanScale,
            Quaternion normalizedRotation,
            Quaternion targetToSourceRotation,
            float frameRate,
            int minimumContactFrames,
            int releaseFrames,
            HumanoidFootContactIntentEstimate intents)
        {
            var channels = new ChannelCorrections();
            // 발끝 방향 세그먼트를 먼저 계산해야 핀 보정이 발끝 기준점을 쓸 수 있음.
            channels.LeftToeSegments = BuildDesiredToeSegments(
                arrays.SourceLeftFeet,
                arrays.SourceLeftToes,
                arrays.TargetLeftFeet,
                arrays.TargetLeftToes,
                normalizedSourceHumanScale,
                normalizedTargetHumanScale,
                normalizedRotation);
            channels.RightToeSegments = BuildDesiredToeSegments(
                arrays.SourceRightFeet,
                arrays.SourceRightToes,
                arrays.TargetRightFeet,
                arrays.TargetRightToes,
                normalizedSourceHumanScale,
                normalizedTargetHumanScale,
                normalizedRotation);
            channels.LeftCorrections = BuildSideCorrections(
                arrays.SourceLeft, arrays.TargetLeft,
                arrays.SourceLeftFeet, arrays.SourceLeftToes,
                arrays.TargetLeftFeet, arrays.TargetLeftToes,
                channels.LeftToeSegments, frameRate,
                normalizedSourceHumanScale, normalizedTargetHumanScale,
                normalizedRotation, targetToSourceRotation,
                minimumContactFrames, releaseFrames, intents?.Left,
                out channels.LeftRunCount);
            channels.RightCorrections = BuildSideCorrections(
                arrays.SourceRight, arrays.TargetRight,
                arrays.SourceRightFeet, arrays.SourceRightToes,
                arrays.TargetRightFeet, arrays.TargetRightToes,
                channels.RightToeSegments, frameRate,
                normalizedSourceHumanScale, normalizedTargetHumanScale,
                normalizedRotation, targetToSourceRotation,
                minimumContactFrames, releaseFrames, intents?.Right,
                out channels.RightRunCount);
            return channels;
        }

        static Vector3[] BuildSideCorrections(
            IReadOnlyList<Vector3> sourcePoints,
            IReadOnlyList<Vector3> targetPoints,
            IReadOnlyList<Vector3> sourceFeet,
            IReadOnlyList<Vector3> sourceToes,
            IReadOnlyList<Vector3> targetFeet,
            IReadOnlyList<Vector3> targetToes,
            Vector3[] toeSegments,
            float frameRate,
            float normalizedSourceHumanScale,
            float normalizedTargetHumanScale,
            Quaternion normalizedRotation,
            Quaternion targetToSourceRotation,
            int minimumContactFrames,
            int releaseFrames,
            IReadOnlyList<HumanoidFootContactIntent> intents,
            out int runCount)
        {
            Vector3[] corrections = BuildFootCorrections(
                sourcePoints,
                targetPoints,
                sourceToes,
                targetFeet,
                toeSegments,
                frameRate,
                normalizedSourceHumanScale,
                normalizedRotation,
                minimumContactFrames,
                releaseFrames,
                intents,
                out runCount);
            ApplySupportHeightCorrections(
                sourceFeet,
                sourceToes,
                targetFeet,
                targetToes,
                corrections,
                normalizedSourceHumanScale,
                normalizedTargetHumanScale,
                targetToSourceRotation);
            return corrections;
        }

        struct SampleArrays
        {
            internal Vector3[] SourceLeft;
            internal Vector3[] SourceRight;
            internal Vector3[] TargetLeft;
            internal Vector3[] TargetRight;
            internal Vector3[] SourceLeftFeet;
            internal Vector3[] SourceLeftToes;
            internal Vector3[] SourceRightFeet;
            internal Vector3[] SourceRightToes;
            internal Vector3[] TargetLeftFeet;
            internal Vector3[] TargetLeftToes;
            internal Vector3[] TargetRightFeet;
            internal Vector3[] TargetRightToes;
        }

        static SampleArrays CollectSampleArrays(
            IReadOnlyList<HumanoidFootContactSample> sourceSamples,
            IReadOnlyList<HumanoidFootContactSample> targetSamples)
        {
            var arrays = new SampleArrays
            {
                SourceLeft = new Vector3[sourceSamples.Count],
                SourceRight = new Vector3[sourceSamples.Count],
                TargetLeft = new Vector3[targetSamples.Count],
                TargetRight = new Vector3[targetSamples.Count],
                SourceLeftFeet = new Vector3[sourceSamples.Count],
                SourceLeftToes = new Vector3[sourceSamples.Count],
                SourceRightFeet = new Vector3[sourceSamples.Count],
                SourceRightToes = new Vector3[sourceSamples.Count],
                TargetLeftFeet = new Vector3[targetSamples.Count],
                TargetLeftToes = new Vector3[targetSamples.Count],
                TargetRightFeet = new Vector3[targetSamples.Count],
                TargetRightToes = new Vector3[targetSamples.Count]
            };
            for (int index = 0; index < sourceSamples.Count; index++)
            {
                HumanoidFootContactSample source = sourceSamples[index];
                HumanoidFootContactSample target = targetSamples[index];
                if (!IsFinite(source.Left) ||
                    !IsFinite(source.Right) ||
                    !IsFinite(target.Left) ||
                    !IsFinite(target.Right) ||
                    !IsFinite(source.LeftFoot) ||
                    !IsFinite(source.LeftToes) ||
                    !IsFinite(source.RightFoot) ||
                    !IsFinite(source.RightToes) ||
                    !IsFinite(target.LeftFoot) ||
                    !IsFinite(target.LeftToes) ||
                    !IsFinite(target.RightFoot) ||
                    !IsFinite(target.RightToes))
                {
                    throw new ArgumentException("발 접촉 표본은 유한한 값이어야 합니다.");
                }

                arrays.SourceLeft[index] = source.Left;
                arrays.SourceRight[index] = source.Right;
                arrays.TargetLeft[index] = target.Left;
                arrays.TargetRight[index] = target.Right;
                arrays.SourceLeftFeet[index] = source.LeftFoot;
                arrays.SourceLeftToes[index] = source.LeftToes;
                arrays.SourceRightFeet[index] = source.RightFoot;
                arrays.SourceRightToes[index] = source.RightToes;
                arrays.TargetLeftFeet[index] = target.LeftFoot;
                arrays.TargetLeftToes[index] = target.LeftToes;
                arrays.TargetRightFeet[index] = target.RightFoot;
                arrays.TargetRightToes[index] = target.RightToes;
            }
            return arrays;
        }

        static Vector3[] ApplyToeDirections(
            Vector3[] toeSegments,
            Vector3[] targetFeet,
            Vector3[] targetToes,
            Vector3[] corrections,
            Quaternion targetToSourceRotation)
        {
            var toeDirections = new Vector3[toeSegments.Length];
            for (int index = 0; index < toeSegments.Length; index++)
            {
                toeDirections[index] =
                    targetToSourceRotation * toeSegments[index];
                // 발끝 방향 보정은 발목 축 회전으로 적용돼 발끝이 수평 이동하고
                // 그 절반만큼 접촉 중점이 밀림. 모든 프레임에서 발목 보정에
                // 회전 유발 수평 이동의 절반을 빼 "중점 = 기준 중점 + 보정"
                // 불변식을 지킴. 방향 델타가 0이면 보상도 0이라 게이트 불필요.
                corrections[index] += ToeRotationMidpointCompensation(
                    targetToes[index] - targetFeet[index],
                    toeSegments[index],
                    targetToSourceRotation);
            }
            return toeDirections;
        }

        // 발끝 세그먼트(발목→발끝 벡터)의 목표 형태를 대상 공간에 계산함.
        // 핀 보정이 발끝 기준점을 쓰려면 위치 보정보다 먼저 필요함.
        private static Vector3[] BuildDesiredToeSegments(
            IReadOnlyList<Vector3> sourceFeet,
            IReadOnlyList<Vector3> sourceToes,
            IReadOnlyList<Vector3> targetFeet,
            IReadOnlyList<Vector3> targetToes,
            float sourceHumanScale,
            float targetHumanScale,
            Quaternion sourceToTargetRotation)
        {
            float scaleRatio = targetHumanScale / sourceHumanScale;
            float sourceFootBaseline = CalculatePercentileHeight(sourceFeet, 0.02f);
            float sourceToesBaseline = CalculatePercentileHeight(sourceToes, 0.02f);
            float targetFootBaseline = CalculatePercentileHeight(targetFeet, 0.02f);
            float targetToesBaseline = CalculatePercentileHeight(targetToes, 0.02f);
            var segments = new Vector3[sourceFeet.Count];

            for (int index = 0; index < sourceFeet.Count; index++)
            {
                segments[index] = CalculateDesiredSegment(
                    sourceFeet[index], sourceToes[index],
                    targetFeet[index], targetToes[index],
                    sourceFootBaseline, sourceToesBaseline,
                    targetFootBaseline, targetToesBaseline,
                    scaleRatio, sourceToTargetRotation);
            }

            return segments;
        }


        private static void ApplySupportHeightCorrections(
            IReadOnlyList<Vector3> sourceFeet,
            IReadOnlyList<Vector3> sourceToes,
            IReadOnlyList<Vector3> targetFeet,
            IReadOnlyList<Vector3> targetToes,
            Vector3[] rootSpaceCorrections,
            float sourceHumanScale,
            float targetHumanScale,
            Quaternion targetToSourceRotation)
        {
            float scaleRatio = targetHumanScale / sourceHumanScale;
            float sourceFootBaseline = CalculatePercentileHeight(sourceFeet, 0.02f);
            float sourceToesBaseline = CalculatePercentileHeight(sourceToes, 0.02f);
            float targetFootBaseline = CalculatePercentileHeight(targetFeet, 0.02f);
            float targetToesBaseline = CalculatePercentileHeight(targetToes, 0.02f);

            for (int index = 0; index < sourceFeet.Count; index++)
            {
                CorrectFrameHeight(
                    index, sourceFeet, sourceToes, targetFeet, targetToes,
                    rootSpaceCorrections, sourceFootBaseline, sourceToesBaseline,
                    targetFootBaseline, targetToesBaseline, scaleRatio,
                    targetHumanScale, targetToSourceRotation);
            }
        }


        private static Vector3[] BuildFootCorrections(
            IReadOnlyList<Vector3> sourcePoints,
            IReadOnlyList<Vector3> targetPoints,
            IReadOnlyList<Vector3> sourceToes,
            IReadOnlyList<Vector3> targetFeet,
            IReadOnlyList<Vector3> toeSegments,
            float frameRate,
            float sourceHumanScale,
            Quaternion sourceToTargetRotation,
            int minimumContactFrames,
            int releaseFrames,
            IReadOnlyList<HumanoidFootContactIntent> intents,
            out int runCount)
        {
            // 확정된 의도 구간은 프레임별 앵커 방침으로 변환해 보정 계획에 반영함.
            HumanoidFootAnchorPolicy[] policies =
                HumanoidFootAnchorPolicyResolver.Rasterize(intents, sourcePoints.Count);
            float pinRelease =
                sourceHumanScale * HumanoidFootContactIntentEstimator.SlideDisplacementPerHumanScale;
            float contactHeight = CalculatePercentileHeight(sourcePoints, 0.02f) +
                sourceHumanScale * ContactHeightMarginPerHumanScale;
            float contactSpeedLimit =
                sourceHumanScale * ContactSpeedLimitPerHumanScale;
            bool[] contactFrames = DetectContactFrames(
                sourcePoints,
                frameRate,
                contactHeight,
                contactSpeedLimit);
            // 발끝이 낮고 정지한 프레임의 지지점은 발끝임. 이 프레임에서는
            // 발목이 움직여도(롤) 발끝을 고정해야 드리프트가 생기지 않음.
            float toeContactHeight =
                CalculatePercentileHeight(sourceToes, 0.02f) +
                sourceHumanScale * ContactHeightMarginPerHumanScale;
            bool[] toePivot = ComputeToePivot(
                sourceToes, sourcePoints.Count, toeContactHeight,
                contactSpeedLimit, frameRate);
            // 발끝이 바닥에 닿아있는 프레임은 의도·방침과 무관하게 접촉이다.
            // 롤·슬라이드·정착 구간은 중점이 들리거나 움직여 런 밖으로 빠지는데,
            // 그러면 확정 방침이 소비되지 않고 런 종료의 보정 해제가
            // 발끝 잔류 접촉 안에서 일어나 전이 팝이 된다.
            // 발끝 착지 프레임을 모두 런에 포함시켜 추종이 실제 리프트오프까지
            // 이어지게 함. 단, 빠르게 상승 중인 발끝은 이륙 진행 중이므로 제외함.
            IncludeToeLandings(
                contactFrames, sourceToes, toeContactHeight,
                contactSpeedLimit, frameRate);
            var corrections = new Vector3[sourcePoints.Count];
            runCount = ApplyContactRuns(
                sourcePoints, targetPoints, sourceToes, toeSegments, toePivot,
                contactFrames, corrections, sourceToTargetRotation,
                minimumContactFrames, releaseFrames, policies, pinRelease);

            return corrections;
        }

        // 기본값은 기존 핀/해제/해제블렌드 구현 — 병합된 의도와 기존 핀 경로의
        // 조합 효과를 분리 측정할 수 있게 실험 토글로만 열어둠.
        // 오프라인 솔버는 FbxDiagnostics.RunAll의 -offlineSolver 인자나 EditMode
        // 테스트에서만 켜고, 병합만으로 게이트 미달일 때 활성화를 검토함.
        internal static bool UseOfflineContactSolver = false;

        // 오프라인 제약 풀이의 반복 계수 — 접촉 프레임은 강하게 수렴하고
        // 비접촉 프레임은 원본 상대이동을 유지하는 쪽으로 약하게 수렴함.
        private const float ContactHardFactor = 0.9f;
        private const float FollowSoftFactor = 0.05f;
        private const int SolverIterationsPerFrame = 8;
        private const int SolverMinimumIterations = 60;

        static void IncludeToeLandings(
            bool[] contactFrames,
            IReadOnlyList<Vector3> sourceToes,
            float toeContactHeight,
            float contactSpeedLimit,
            float frameRate)
        {
            for (int index = 0; index < contactFrames.Length; index++)
            {
                if (contactFrames[index] ||
                    sourceToes[index].y > toeContactHeight)
                {
                    continue;
                }

                float toeRiseSpeed = index == 0
                    ? 0f
                    : (sourceToes[index].y - sourceToes[index - 1].y) * frameRate;
                if (toeRiseSpeed <= contactSpeedLimit)
                {
                    contactFrames[index] = true;
                }
            }
        }

        static int ApplyContactRuns(
            IReadOnlyList<Vector3> sourcePoints,
            IReadOnlyList<Vector3> targetPoints,
            IReadOnlyList<Vector3> sourceToes,
            IReadOnlyList<Vector3> toeSegments,
            bool[] toePivot,
            bool[] contactFrames,
            Vector3[] corrections,
            Quaternion sourceToTargetRotation,
            int minimumContactFrames,
            int releaseFrames,
            HumanoidFootAnchorPolicy[] policies,
            float pinRelease)
        {
            Quaternion targetToSourceRotation =
                Quaternion.Inverse(sourceToTargetRotation);
            int runCount = 0;
            int runStart = -1;

            for (int index = 0; index <= contactFrames.Length; index++)
            {
                bool isContact = index < contactFrames.Length && contactFrames[index];
                if (isContact && runStart < 0)
                {
                    runStart = index;
                    continue;
                }

                if (isContact || runStart < 0)
                {
                    continue;
                }

                runCount += TryApplyRun(
                    sourcePoints, targetPoints, sourceToes, toeSegments,
                    toePivot, contactFrames, corrections, sourceToTargetRotation,
                    runStart, index - 1, minimumContactFrames, releaseFrames,
                    policies, pinRelease);
                runStart = -1;
            }
            return runCount;
        }



        private static void ApplyContactRun(
            IReadOnlyList<Vector3> sourcePoints,
            IReadOnlyList<Vector3> targetPoints,
            IReadOnlyList<Vector3> sourceToes,
            IReadOnlyList<Vector3> toeSegments,
            IReadOnlyList<bool> toePivot,
            IReadOnlyList<bool> contactFrames,
            Vector3[] corrections,
            Quaternion sourceToTargetRotation,
            Quaternion targetToSourceRotation,
            int runStart,
            int runEnd,
            int releaseFrames,
            HumanoidFootAnchorPolicy[] policies,
            float pinReleaseDistance)
        {
            if (UseOfflineContactSolver)
            {
                ApplyContactRunOffline(
                    sourcePoints, targetPoints, contactFrames, corrections,
                    sourceToTargetRotation, targetToSourceRotation,
                    runStart, runEnd, releaseFrames, policies, pinReleaseDistance);
                return;
            }

            // 추종 궤적은 항상 발목·발끝 중점 기준(기존 동작과 동일).
            // 핀은 롤 프레임에서 발끝을 고정점으로 잡되 중점 목표로 환산해
            // 발목이 심은 발끝 주위를 회전하도록 함. 기준 전환은 핀 경계에서만
            // 일어나고 모든 전이는 연속으로 유지됨.
            var run = new OnlineContactRun(
                sourcePoints, targetPoints, sourceToes, toeSegments, toePivot,
                corrections, sourceToTargetRotation, targetToSourceRotation,
                releaseFrames, policies, pinReleaseDistance);
            run.Run(runStart, runEnd);
            ApplyReleaseBlend(corrections, contactFrames, runEnd, releaseFrames);
        }

        /// <summary>
        /// 클립 전체를 아는 오프라인 제약 풀이로 접촉 런을 보정함.
        /// 런 안 각 프레임의 대상 접촉점을 입자로 보고 반복 수렴함:
        ///   - 연속된 핀 프레임은 같은 수평 위치로 강하게 수렴(하드 제약)
        ///   - 나머지 프레임은 원본의 프레임간 상대 이동을 유지(소프트 제약)
        ///   - 런 첫 프레임은 진입 위치에 고정해 경계 연속성을 지킴
        /// 섬 단위 핀 해제가 없으므로 누적 잔차가 생기지 않음.
        /// </summary>
        private static void ApplyContactRunOffline(
            IReadOnlyList<Vector3> sourcePoints,
            IReadOnlyList<Vector3> targetPoints,
            IReadOnlyList<bool> contactFrames,
            Vector3[] corrections,
            Quaternion sourceToTargetRotation,
            Quaternion targetToSourceRotation,
            int runStart,
            int runEnd,
            int releaseFrames,
            HumanoidFootAnchorPolicy[] policies,
            float pinReleaseDistance)
        {
            int length = runEnd - runStart + 1;
            var solved = new Vector3[length];
            for (int k = 0; k < length; k++)
            {
                solved[k] = targetPoints[runStart + k];
            }

            // 핀 구간마다 원본 기준점을 잡아, 핀 안에서 원본이 실제로 크게 움직이면
            // 의도 추정 오류로 보고 그 프레임의 핀을 해제(소프트)함.
            bool[] effectivePinned = ComputeEffectivePinned(
                length, sourcePoints, runStart, policies, pinReleaseDistance);
            IterateSolver(
                solved, length, sourcePoints, runStart,
                effectivePinned, sourceToTargetRotation);

            for (int k = 0; k < length; k++)
            {
                int index = runStart + k;
                Vector3 rootSpaceCorrection = targetToSourceRotation *
                    (solved[k] - targetPoints[index]);
                rootSpaceCorrection.y = 0f;
                corrections[index] = rootSpaceCorrection;
            }

            ApplyReleaseBlend(corrections, contactFrames, runEnd, releaseFrames);
        }


        static void IterateSolver(
            Vector3[] solved,
            int length,
            IReadOnlyList<Vector3> sourcePoints,
            int runStart,
            bool[] effectivePinned,
            Quaternion sourceToTargetRotation)
        {
            Vector3 entryAnchor = solved[0];
            int iterations = Mathf.Max(SolverMinimumIterations,
                SolverIterationsPerFrame * length);
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                for (int k = 1; k < length; k++)
                {
                    if (effectivePinned[k - 1] && effectivePinned[k])
                    {
                        // 하드 제약: 연속 접촉 프레임을 수평 중점으로 수렴함.
                        float midX = (solved[k - 1].x + solved[k].x) * 0.5f;
                        float midZ = (solved[k - 1].z + solved[k].z) * 0.5f;
                        solved[k - 1] = HorizontalLerp(
                            solved[k - 1], midX, midZ, ContactHardFactor);
                        solved[k] = HorizontalLerp(
                            solved[k], midX, midZ, ContactHardFactor);
                    }
                    else
                    {
                        // 소프트 제약: 원본의 상대 이동을 유지하도록 쌍을 당김.
                        Vector3 relative = sourceToTargetRotation *
                            (sourcePoints[runStart + k] -
                             sourcePoints[runStart + k - 1]);
                        relative.y = 0f;
                        Vector3 error = new Vector3(
                            solved[k].x - solved[k - 1].x - relative.x,
                            0f,
                            solved[k].z - solved[k - 1].z - relative.z);
                        Vector3 half = error * (FollowSoftFactor * 0.5f);
                        solved[k - 1] = new Vector3(
                            solved[k - 1].x + half.x, solved[k - 1].y,
                            solved[k - 1].z + half.z);
                        solved[k] = new Vector3(
                            solved[k].x - half.x, solved[k].y,
                            solved[k].z - half.z);
                    }
                }
                // 런 진입 프레임을 고정해 보정 시작점의 연속성을 유지함.
                solved[0] = new Vector3(entryAnchor.x, solved[0].y, entryAnchor.z);
            }
        }

        private static Vector3 HorizontalLerp(Vector3 from, float x, float z,
            float t)
        {
            return new Vector3(
                Mathf.Lerp(from.x, x, t), from.y, Mathf.Lerp(from.z, z, t));
        }

        /// <summary>
        /// 발끝 방향 보정이 발목 축 회전으로 적용될 때 발끝이 수평 이동하고
        /// 접촉 중점(발목·발끝 평균)이 그 절반만큼 밀림. 접촉 중점을 계획 위치에
        /// 유지하려고 발목 보정에서 회전 유발 수평 이동의 절반을 뺌.
        /// 수직 성분은 착지 자체의 발끝 하강이므로 보상하지 않음.
        /// </summary>
        internal static Vector3 ToeRotationMidpointCompensation(
            Vector3 currentToeOffset,
            Vector3 desiredToeOffset,
            Quaternion targetToSourceRotation)
        {
            Vector3 horizontalDelta = desiredToeOffset - currentToeOffset;
            horizontalDelta.y = 0f;
            Vector3 rootSpace = targetToSourceRotation *
                (horizontalDelta * -0.5f);
            rootSpace.y = 0f;
            return rootSpace;
        }

        // 런 끝 잔여 보정을 비접촉 프레임에 걸쳐 부드럽게 감쇠시킴.
        private static void ApplyReleaseBlend(
            Vector3[] corrections,
            IReadOnlyList<bool> contactFrames,
            int runEnd,
            int releaseFrames)
        {
            Vector3 releaseCorrection = corrections[runEnd];
            for (int releaseIndex = 1; releaseIndex <= releaseFrames; releaseIndex++)
            {
                int frameIndex = runEnd + releaseIndex;
                if (frameIndex >= corrections.Length || contactFrames[frameIndex])
                {
                    break;
                }

                float progress = releaseIndex / (releaseFrames + 1f);
                float smoothProgress = progress * progress * (3f - 2f * progress);
                corrections[frameIndex] =
                    releaseCorrection * (1f - smoothProgress);
            }
        }


        private static void MarkStableContactSpan(
            IReadOnlyList<bool> stableFrames,
            bool[] contactFrames,
            int candidateStart,
            int candidateEnd)
        {
            int firstStable = -1;
            int lastStable = -1;
            for (int index = candidateStart; index <= candidateEnd; index++)
            {
                if (!stableFrames[index])
                {
                    continue;
                }

                if (firstStable < 0)
                {
                    firstStable = index;
                }

                lastStable = index;
            }

            if (firstStable < 0)
            {
                return;
            }

            int contactStart = Mathf.Max(candidateStart, firstStable - 1);
            int contactEnd = Mathf.Min(candidateEnd, lastStable + 1);
            for (int index = contactStart; index <= contactEnd; index++)
            {
                contactFrames[index] = true;
            }
        }

        private static float CalculateCenteredSpeed(
            IReadOnlyList<Vector3> points,
            int index,
            float frameRate)
        {
            if (points.Count <= 1)
            {
                return 0f;
            }

            if (index == 0)
            {
                return Vector3.Distance(points[0], points[1]) * frameRate;
            }

            if (index == points.Count - 1)
            {
                return Vector3.Distance(points[index - 1], points[index]) * frameRate;
            }

            return Vector3.Distance(points[index - 1], points[index + 1]) *
                frameRate * 0.5f;
        }

        private static float CalculatePercentileHeight(
            IReadOnlyList<Vector3> points,
            float percentile)
        {
            var heights = new float[points.Count];
            for (int index = 0; index < points.Count; index++)
            {
                heights[index] = points[index].y;
            }

            Array.Sort(heights);
            int percentileIndex = Mathf.Clamp(
                Mathf.CeilToInt(heights.Length * percentile) - 1,
                0,
                heights.Length - 1);
            return heights[percentileIndex];
        }

        private static void ValidateSamples(
            IReadOnlyList<HumanoidFootContactSample> sourceSamples,
            IReadOnlyList<HumanoidFootContactSample> targetSamples,
            float frameRate)
        {
            if (sourceSamples == null)
            {
                throw new ArgumentNullException(nameof(sourceSamples));
            }

            if (targetSamples == null)
            {
                throw new ArgumentNullException(nameof(targetSamples));
            }

            if (sourceSamples.Count == 0 || sourceSamples.Count != targetSamples.Count)
            {
                throw new ArgumentException("원본과 대상 발 접촉 표본 수가 일치해야 합니다.");
            }

            if (!IsFinite(frameRate) || frameRate <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(frameRate));
            }
        }

        private static Quaternion NormalizeRotation(Quaternion rotation)
        {
            if (!IsFinite(rotation) ||
                rotation.x * rotation.x +
                rotation.y * rotation.y +
                rotation.z * rotation.z +
                rotation.w * rotation.w <= MinimumQuaternionMagnitude)
            {
                throw new ArgumentException("원본과 대상 사이 회전은 유효해야 합니다.");
            }

            return rotation.normalized;
        }

        private static float HorizontalDistance(Vector3 first, Vector3 second)
        {
            return Vector2.Distance(
                new Vector2(first.x, first.z),
                new Vector2(second.x, second.z));
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) &&
                IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
