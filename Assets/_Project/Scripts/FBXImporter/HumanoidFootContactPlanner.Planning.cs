using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class HumanoidFootContactPlanner
    {
    static Vector3 CalculateDesiredSegment(
        Vector3 sourceFoot, Vector3 sourceToes,
        Vector3 targetFoot, Vector3 targetToes,
        float sourceFootBaseline, float sourceToesBaseline,
        float targetFootBaseline, float targetToesBaseline,
        float scaleRatio, Quaternion sourceToTargetRotation)
    {
        Vector3 targetSegment = targetToes - targetFoot;
        float targetSegmentLength = targetSegment.magnitude;
        if (targetSegmentLength <= MinimumSegmentLength)
        {
            return Vector3.zero;
        }

        float desiredFootHeight = targetFootBaseline +
            (sourceFoot.y - sourceFootBaseline) * scaleRatio;
        float desiredToesHeight = targetToesBaseline +
            (sourceToes.y - sourceToesBaseline) * scaleRatio;
        float desiredVerticalSeparation = Mathf.Clamp(
            desiredToesHeight - desiredFootHeight,
            -targetSegmentLength,
            targetSegmentLength);

        return BuildHorizontalMappedSegment(
            sourceToes, sourceFoot, sourceToTargetRotation, targetSegment,
            targetSegmentLength, desiredVerticalSeparation);
    }

    private static Vector3 BuildHorizontalMappedSegment(
        Vector3 sourceToes, Vector3 sourceFoot,
        Quaternion sourceToTargetRotation, Vector3 targetSegment,
        float targetSegmentLength, float desiredVerticalSeparation)
    {
        Vector3 sourceSegment = sourceToes - sourceFoot;
        Vector3 mappedSourceDirection =
            sourceToTargetRotation * sourceSegment.normalized;
        Vector2 horizontalDirection = new Vector2(
            mappedSourceDirection.x,
            mappedSourceDirection.z);
        if (horizontalDirection.sqrMagnitude <= MinimumSegmentLength)
        {
            horizontalDirection = new Vector2(
                targetSegment.x,
                targetSegment.z);
        }

        horizontalDirection.Normalize();
        float horizontalLength = Mathf.Sqrt(Mathf.Max(
            0f,
            targetSegmentLength * targetSegmentLength -
            desiredVerticalSeparation * desiredVerticalSeparation));
        return new Vector3(
            horizontalDirection.x * horizontalLength,
            desiredVerticalSeparation,
            horizontalDirection.y * horizontalLength);
    }

    static void CorrectFrameHeight(
        int index,
        IReadOnlyList<Vector3> sourceFeet,
        IReadOnlyList<Vector3> sourceToes,
        IReadOnlyList<Vector3> targetFeet,
        IReadOnlyList<Vector3> targetToes,
        Vector3[] rootSpaceCorrections,
        float sourceFootBaseline,
        float sourceToesBaseline,
        float targetFootBaseline,
        float targetToesBaseline,
        float scaleRatio,
        float targetHumanScale,
        Quaternion targetToSourceRotation)
    {
        Vector3 targetSegment = targetToes[index] - targetFeet[index];
        float targetSegmentLength = targetSegment.magnitude;
        float desiredFootHeight = targetFootBaseline +
            (sourceFeet[index].y - sourceFootBaseline) * scaleRatio;
        float desiredToesHeight = targetToesBaseline +
            (sourceToes[index].y - sourceToesBaseline) * scaleRatio;
        if (targetSegmentLength <= MinimumSegmentLength)
        {
            ApplyFootOnlyHeightCorrection(
                index, targetFeet, rootSpaceCorrections, desiredFootHeight,
                targetHumanScale, targetToSourceRotation);
            return;
        }

        float desiredVerticalSeparation = Mathf.Clamp(
            desiredToesHeight - desiredFootHeight,
            -targetSegmentLength,
            targetSegmentLength);

        ApplyToeWeightedHeightCorrection(
            index, targetFeet, rootSpaceCorrections, desiredFootHeight,
            desiredToesHeight, desiredVerticalSeparation, targetHumanScale,
            targetToSourceRotation);
    }

    // 발끝은 실제 지지점이고 Foot 본은 발목에 가까운 대리점이므로
    // 길이 제약이 충돌할 때 발끝 높이에 더 큰 가중치를 둠.
    private static void ApplyToeWeightedHeightCorrection(
        int index, IReadOnlyList<Vector3> targetFeet,
        Vector3[] rootSpaceCorrections, float desiredFootHeight,
        float desiredToesHeight, float desiredVerticalSeparation,
        float targetHumanScale, Quaternion targetToSourceRotation)
    {
        float correctedFootHeight =
            (desiredFootHeight +
             ToeHeightWeight *
             (desiredToesHeight - desiredVerticalSeparation)) /
            (1f + ToeHeightWeight);
        float heightCorrection =
            correctedFootHeight - targetFeet[index].y;
        if (Mathf.Abs(heightCorrection) >=
            targetHumanScale * MinimumHeightCorrectionPerHumanScale)
        {
            rootSpaceCorrections[index] += targetToSourceRotation *
                (Vector3.up * heightCorrection);
        }
    }

    private static void ApplyFootOnlyHeightCorrection(
        int index, IReadOnlyList<Vector3> targetFeet,
        Vector3[] rootSpaceCorrections, float desiredFootHeight,
        float targetHumanScale, Quaternion targetToSourceRotation)
    {
        float footOnlyHeightCorrection =
            desiredFootHeight - targetFeet[index].y;
        if (Mathf.Abs(footOnlyHeightCorrection) >=
            targetHumanScale * MinimumHeightCorrectionPerHumanScale)
        {
            rootSpaceCorrections[index] += targetToSourceRotation *
                (Vector3.up * footOnlyHeightCorrection);
        }
    }

    static int TryApplyRun(
        IReadOnlyList<Vector3> sourcePoints,
        IReadOnlyList<Vector3> targetPoints,
        IReadOnlyList<Vector3> sourceToes,
        IReadOnlyList<Vector3> toeSegments,
        bool[] toePivot,
        bool[] contactFrames,
        Vector3[] corrections,
        Quaternion sourceToTargetRotation,
        int runStart,
        int runEnd,
        int minimumContactFrames,
        int releaseFrames,
        HumanoidFootAnchorPolicy[] policies,
        float pinRelease)
    {
        if (runEnd - runStart + 1 < minimumContactFrames)
        {
            return 0;
        }

        ApplyContactRun(
            sourcePoints,
            targetPoints,
            sourceToes,
            toeSegments,
            toePivot,
            contactFrames,
            corrections,
            sourceToTargetRotation,
            Quaternion.Inverse(sourceToTargetRotation),
            runStart,
            runEnd,
            releaseFrames,
            policies,
            pinRelease);
        return 1;
    }

    static bool[] ComputeToePivot(
        IReadOnlyList<Vector3> sourceToes,
        int count,
        float toeContactHeight,
        float contactSpeedLimit,
        float frameRate)
    {
        var toePivot = new bool[count];
        for (int index = 0; index < count; index++)
        {
            toePivot[index] = sourceToes[index].y <= toeContactHeight &&
                CalculateCenteredSpeed(sourceToes, index, frameRate) <=
                contactSpeedLimit;
        }
        return toePivot;
    }

    static bool[] ComputeEffectivePinned(
        int length,
        IReadOnlyList<Vector3> sourcePoints,
        int runStart,
        HumanoidFootAnchorPolicy[] policies,
        float pinReleaseDistance)
    {
        var effectivePinned = new bool[length];
        int pinStart = -1;
        for (int k = 0; k <= length; k++)
        {
            bool isPin = k < length &&
                policies[runStart + k] == HumanoidFootAnchorPolicy.Pinned;
            if (isPin && pinStart < 0) { pinStart = k; continue; }
            if (isPin || pinStart < 0) { continue; }
            Vector3 pinSource = sourcePoints[runStart + pinStart];
            for (int j = pinStart; j < k; j++)
            {
                effectivePinned[j] = pinReleaseDistance <= 0f ||
                    HorizontalDistance(sourcePoints[runStart + j], pinSource) <
                    pinReleaseDistance;
            }
            pinStart = -1;
        }
        return effectivePinned;
    }

    private static bool[] DetectContactFrames(
        IReadOnlyList<Vector3> points,
        float frameRate,
        float contactHeight,
        float contactSpeedLimit)
    {
        var contactCandidates = new bool[points.Count];
        var stableFrames = new bool[points.Count];
        MarkContactCandidates(
            points, frameRate, contactHeight, contactSpeedLimit,
            contactCandidates, stableFrames);

        return CollectStableSpans(contactCandidates, stableFrames, points.Count);
    }

    // 낮은 발이 계속 상승·하강하는 통과 구간은 접촉으로 보지 않되,
    // 안정 프레임 사이의 발 롤링은 하나의 접촉 구간으로 보존함.
    private static bool[] CollectStableSpans(
        bool[] contactCandidates, bool[] stableFrames, int count)
    {
        var result = new bool[count];
        int candidateStart = -1;
        for (int index = 0; index <= contactCandidates.Length; index++)
        {
            bool isCandidate = index < contactCandidates.Length &&
                contactCandidates[index];
            if (isCandidate && candidateStart < 0)
            {
                candidateStart = index;
                continue;
            }

            if (isCandidate || candidateStart < 0)
            {
                continue;
            }

            MarkStableContactSpan(
                stableFrames,
                result,
                candidateStart,
                index - 1);
            candidateStart = -1;
        }
        return result;
    }

    private static void MarkContactCandidates(
        IReadOnlyList<Vector3> points, float frameRate,
        float contactHeight, float contactSpeedLimit,
        bool[] contactCandidates, bool[] stableFrames)
    {
        for (int index = 0; index < points.Count; index++)
        {
            float horizontalSpeed = index == 0
                ? 0f
                : HorizontalDistance(points[index - 1], points[index]) * frameRate;
            contactCandidates[index] = points[index].y <= contactHeight &&
                horizontalSpeed <= contactSpeedLimit;
            stableFrames[index] = contactCandidates[index] &&
                CalculateCenteredSpeed(points, index, frameRate) <=
                contactSpeedLimit;
        }
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
        bool[] contactFrames = ResolveContactFrames(
            sourcePoints, sourceToes, frameRate, sourceHumanScale,
            out bool[] toePivot);
        var corrections = new Vector3[sourcePoints.Count];
        runCount = ApplyContactRuns(
            sourcePoints, targetPoints, sourceToes, toeSegments, toePivot,
            contactFrames, corrections, sourceToTargetRotation,
            minimumContactFrames, releaseFrames, policies, pinRelease);

        return corrections;
    }

    // 발끝이 바닥에 닿아있는 프레임은 의도·방침과 무관하게 접촉이다.
    // 발끝 착지 프레임을 모두 런에 포함시켜 추종이 실제 리프트오프까지 이어지게 함.
    private static bool[] ResolveContactFrames(
        IReadOnlyList<Vector3> sourcePoints,
        IReadOnlyList<Vector3> sourceToes,
        float frameRate, float sourceHumanScale,
        out bool[] toePivot)
    {
        float contactHeight = CalculatePercentileHeight(sourcePoints, 0.02f) +
            sourceHumanScale * ContactHeightMarginPerHumanScale;
        float contactSpeedLimit =
            sourceHumanScale * ContactSpeedLimitPerHumanScale;
        bool[] contactFrames = DetectContactFrames(
            sourcePoints, frameRate, contactHeight, contactSpeedLimit);
        // 발끝이 낮고 정지한 프레임의 지지점은 발끝임. 이 프레임에서는
        // 발목이 움직여도(롤) 발끝을 고정해야 드리프트가 생기지 않음.
        float toeContactHeight =
            CalculatePercentileHeight(sourceToes, 0.02f) +
            sourceHumanScale * ContactHeightMarginPerHumanScale;
        toePivot = ComputeToePivot(
            sourceToes, sourcePoints.Count, toeContactHeight,
            contactSpeedLimit, frameRate);
        IncludeToeLandings(
            contactFrames, sourceToes, toeContactHeight,
            contactSpeedLimit, frameRate);
        return contactFrames;
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
    }
}
