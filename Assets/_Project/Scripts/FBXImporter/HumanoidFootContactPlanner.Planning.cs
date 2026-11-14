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

    }
}
