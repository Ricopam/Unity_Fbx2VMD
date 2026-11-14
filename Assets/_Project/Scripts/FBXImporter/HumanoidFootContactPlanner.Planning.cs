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
            float footOnlyHeightCorrection =
                desiredFootHeight - targetFeet[index].y;
            if (Mathf.Abs(footOnlyHeightCorrection) >=
                targetHumanScale * MinimumHeightCorrectionPerHumanScale)
            {
                rootSpaceCorrections[index] += targetToSourceRotation *
                    (Vector3.up * footOnlyHeightCorrection);
            }
            return;
        }

        float desiredVerticalSeparation = Mathf.Clamp(
            desiredToesHeight - desiredFootHeight,
            -targetSegmentLength,
            targetSegmentLength);

        // 발끝은 실제 지지점이고 Foot 본은 발목에 가까운 대리점이므로
        // 길이 제약이 충돌할 때 발끝 높이에 더 큰 가중치를 둠.
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

        // 낮은 발이 계속 상승·하강하는 통과 구간은 접촉으로 보지 않되,
        // 안정 프레임 사이의 발 롤링은 하나의 접촉 구간으로 보존함.
        var result = new bool[points.Count];
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

    }
}
