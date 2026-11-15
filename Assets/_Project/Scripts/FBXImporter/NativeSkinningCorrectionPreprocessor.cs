using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{

    /// <summary>
    /// Native 보정 사전 계산을 프레임 단위로 진행하고 완료 결과만 공개함.
    /// </summary>
    internal sealed partial class NativeSkinningCorrectionPreprocessSession
    {
        private const float MinimumCorrectionSquaredMagnitude =
            0.0000000000000001f;
        // 이 횟수만큼 베이크-LBS 비교를 통과하면 이후 프레임은 베이크를 건너뛴다.
        private const int LbsCalibrationFrameCount = 8;
        private readonly int _frameCount;
        private readonly RendererWork[] _rendererWorks;
        private readonly Func<int, SkinnedMeshRenderer, Vector3[]>
            _readFrameVertices;
        private readonly Action<int, int> _reportProgress;
        private int _processedFrameCount;

        internal NativeSkinningCorrectionPreprocessSession(
            int frameCount,
            RendererWork[] rendererWorks,
            Func<int, SkinnedMeshRenderer, Vector3[]> readFrameVertices,
            Action<int, int> reportProgress,
            bool collectFailures)
        {
            _frameCount = frameCount;
            _rendererWorks = rendererWorks;
            _readFrameVertices = readFrameVertices;
            _reportProgress = reportProgress;
            _collectModeEnabled = collectFailures;
            _diagnosticsDirectory = GetDiagnosticsDirectory();
        }

        internal bool TryProcessNextFrame()
        {
            if (IsComplete)
            {
                return true; }
            if (IsFaulted || IsCanceled)
            {
                return false; }
            try
            {
                int frameIndex = ProcessedFrameCount;
                var pendingFrames = new List<PendingRendererFrame>( _rendererWorks.Length);
                bool usedFallback = false;
                int frameCorrectionEntryCount = 0;
                if (!ProcessRendererFrames(frameIndex, pendingFrames, ref usedFallback, ref frameCorrectionEntryCount))
                {
                    return false; }
                foreach (PendingRendererFrame pending in pendingFrames)
                {
                    pending.Work.Frames.Add(pending.Frame); }
                if (pendingFrames.Count > 0)
                {
                    _correctedFrameCount++;
                    _correctionEntryCount += frameCorrectionEntryCount; }
                if (usedFallback)
                {
                    _fallbackFrameCount++; }
                _processedFrameCount++;
                _reportProgress?.Invoke(_processedFrameCount, _frameCount);
                if (_processedFrameCount == _frameCount)
                {
                    Complete(); }
                return true; } catch (Exception exception)
            {
                return Fail( $"Native 보정 사전 계산 중 예외가 발생했습니다: {exception.Message}"); } }

        private bool ProcessRendererFrames(int frameIndex,
            List<PendingRendererFrame> pendingFrames,
            ref bool usedFallback, ref int frameCorrectionEntryCount)
        {
            foreach (RendererWork work in _rendererWorks)
            {
                if (!TryCalculateRendererFrame(
                        frameIndex,
                        work,
                        out NativeSkinningCorrectionFrame frame,
                        out bool rendererUsedFallback,
                        out string errorMessage))
                {
                    // 수집 모드에서는 실패 프레임을 기록하고 보정 없이 계속 진행한다.
                    if (_collectModeEnabled)
                    {
                        _collectedFailures.Add(
                            $"renderer={work.RendererName} {errorMessage}");
                        continue; }
                    Fail(errorMessage);
                    return false; }
                usedFallback |= rendererUsedFallback;
                if (frame == null)
                {
                    continue; }
                pendingFrames.Add(new PendingRendererFrame(work, frame));
                frameCorrectionEntryCount += frame.CorrectionCount; }
            return true; }
    }

    /// <summary>
    /// Native 보정 사전 계산 session을 검증하고 생성함.
    /// </summary>
    internal static class NativeSkinningCorrectionPreprocessor
    {
        // 진단용: 플래그 파일이 있으면 세션을 실패 수집 모드로 생성해야 함을 나타낸다.
        internal static bool IsFailureCollectionRequested()
        {
            return File.Exists(Path.Combine(
                NativeSkinningCorrectionPreprocessSession
                    .GetDiagnosticsDirectory(),
                "native-prep-collect.flag"));
        }

        // 진단용: 첫 실패에서 멈추지 않고 실패 프레임 전체를 수집하는 세션.
        internal static bool TryCreateFailureCollectingSession(
            int frameCount,
            NativeSkinningSurfaceContract[] contracts,
            Func<int, SkinnedMeshRenderer, Vector3[]> readFrameVertices,
            Action<int, int> reportProgress,
            out NativeSkinningCorrectionPreprocessSession session)
        {
            return TryCreateSessionCore(
                frameCount,
                contracts,
                readFrameVertices,
                reportProgress,
                out session,
                collectFailures: true);
        }

        internal static bool TryCreateSession(
            int frameCount,
            NativeSkinningSurfaceContract[] contracts,
            Func<int, SkinnedMeshRenderer, Vector3[]> readFrameVertices,
            Action<int, int> reportProgress,
            out NativeSkinningCorrectionPreprocessSession session)
        {
            return TryCreateSessionCore(
                frameCount,
                contracts,
                readFrameVertices,
                reportProgress,
                out session,
                collectFailures: false);
        }

        private static bool TryCreateSessionCore(
            int frameCount,
            NativeSkinningSurfaceContract[] contracts,
            Func<int, SkinnedMeshRenderer, Vector3[]> readFrameVertices,
            Action<int, int> reportProgress,
            out NativeSkinningCorrectionPreprocessSession session,
            bool collectFailures)
        {
            session = null;
            if (frameCount <= 0 || contracts == null || contracts.Length == 0 || contracts.Any(contract => contract == null) || readFrameVertices == null)
            {
                return false;
            }
            var works = new List<
                NativeSkinningCorrectionPreprocessSession.RendererWork>();
            foreach (IGrouping<SkinnedMeshRenderer, NativeSkinningSurfaceContract>
                     group in contracts.GroupBy(contract => contract.Renderer))
            {
                NativeSkinningSurfaceContract[] rendererContracts = group.ToArray();
                if (group.Key == null || group.Key.sharedMesh == null || rendererContracts.Any(contract => contract.VertexCount != group.Key.sharedMesh.vertexCount) || !NativeSkinningCorrectionPreprocessSession.RendererWork.TryCreate( group.Key, rendererContracts, out NativeSkinningCorrectionPreprocessSession.RendererWork work))
                {
                    return false; }
                works.Add(work); }
            if (works.Count == 0 || works.Sum(work => work.Contracts.Length) != contracts.Length)
            {
                return false; }
            session = new NativeSkinningCorrectionPreprocessSession( frameCount, works.ToArray(), readFrameVertices, reportProgress, collectFailures);
            return true; }

        internal static bool TryBuild(
            int frameCount,
            NativeSkinningSurfaceContract[] contracts,
            Func<int, SkinnedMeshRenderer, Vector3[]> readFrameVertices,
            Action<int, int> reportProgress,
            out NativeSkinningCorrectionPreprocessResult result)
        {
            result = null;
            if (!TryCreateSession(
                    frameCount,
                    contracts,
                    readFrameVertices,
                    reportProgress,
                    out NativeSkinningCorrectionPreprocessSession session))
            {
                return false;
            }
            while (!session.IsComplete)
            {
                if (!session.TryProcessNextFrame())
                {
                    return false;
                }
            }
            result = session.Result;
            return true;
        }
    }
}
