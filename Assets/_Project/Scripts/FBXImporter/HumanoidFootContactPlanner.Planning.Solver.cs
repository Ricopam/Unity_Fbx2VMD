using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class HumanoidFootContactPlanner
    {
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
                SolvePairStep(
                    solved, k, effectivePinned, sourcePoints, runStart,
                    sourceToTargetRotation);
            }
            // 런 진입 프레임을 고정해 보정 시작점의 연속성을 유지함.
            solved[0] = new Vector3(entryAnchor.x, solved[0].y, entryAnchor.z);
        }
    }

    private static void SolvePairStep(
        Vector3[] solved, int k, bool[] effectivePinned,
        IReadOnlyList<Vector3> sourcePoints, int runStart,
        Quaternion sourceToTargetRotation)
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
            return;
        }

        // 소프트 제약: 원본의 상대 이동을 유지하도록 쌍을 당김.
        Vector3 relative = sourceToTargetRotation *
            (sourcePoints[runStart + k] - sourcePoints[runStart + k - 1]);
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
        PopulateToeSegments(
            ref channels, arrays, normalizedSourceHumanScale,
            normalizedTargetHumanScale, normalizedRotation);
        PopulateSideCorrections(
            ref channels, arrays, frameRate, normalizedSourceHumanScale,
            normalizedTargetHumanScale, normalizedRotation,
            targetToSourceRotation, minimumContactFrames, releaseFrames,
            intents);
        return channels;
    }

    private static void PopulateToeSegments(
        ref ChannelCorrections channels, SampleArrays arrays,
        float normalizedSourceHumanScale, float normalizedTargetHumanScale,
        Quaternion normalizedRotation)
    {
        channels.LeftToeSegments = BuildDesiredToeSegments(
            arrays.SourceLeftFeet, arrays.SourceLeftToes,
            arrays.TargetLeftFeet, arrays.TargetLeftToes,
            normalizedSourceHumanScale, normalizedTargetHumanScale,
            normalizedRotation);
        channels.RightToeSegments = BuildDesiredToeSegments(
            arrays.SourceRightFeet, arrays.SourceRightToes,
            arrays.TargetRightFeet, arrays.TargetRightToes,
            normalizedSourceHumanScale, normalizedTargetHumanScale,
            normalizedRotation);
    }

    private static void PopulateSideCorrections(
        ref ChannelCorrections channels, SampleArrays arrays,
        float frameRate, float normalizedSourceHumanScale,
        float normalizedTargetHumanScale, Quaternion normalizedRotation,
        Quaternion targetToSourceRotation, int minimumContactFrames,
        int releaseFrames, HumanoidFootContactIntentEstimate intents)
    {
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
            ThrowIfNonFiniteSample(source, target);
            CopySample(arrays, index, source, target);
        }
        return arrays;
    }

    private static void ThrowIfNonFiniteSample(
        HumanoidFootContactSample source, HumanoidFootContactSample target)
    {
        if (!IsFinite(source.Left) || !IsFinite(source.Right) ||
            !IsFinite(target.Left) || !IsFinite(target.Right) ||
            !IsFinite(source.LeftFoot) || !IsFinite(source.LeftToes) ||
            !IsFinite(source.RightFoot) || !IsFinite(source.RightToes) ||
            !IsFinite(target.LeftFoot) || !IsFinite(target.LeftToes) ||
            !IsFinite(target.RightFoot) || !IsFinite(target.RightToes))
        {
            throw new ArgumentException("발 접촉 표본은 유한한 값이어야 합니다.");
        }
    }

    private static void CopySample(
        SampleArrays arrays, int index,
        HumanoidFootContactSample source, HumanoidFootContactSample target)
    {
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

    struct ChannelCorrections
    {
        internal Vector3[] LeftCorrections;
        internal Vector3[] RightCorrections;
        internal Vector3[] LeftToeSegments;
        internal Vector3[] RightToeSegments;
        internal int LeftRunCount;
        internal int RightRunCount;
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
