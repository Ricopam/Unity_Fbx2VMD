using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed class NativeSkinningRendererCorrection
    {
        internal NativeSkinningRendererCorrection(
            SkinnedMeshRenderer renderer,
            NativeSkinningCorrectionCache cache)
        {
            Renderer = renderer != null
                ? renderer
                : throw new ArgumentNullException(nameof(renderer));
            Cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        internal SkinnedMeshRenderer Renderer { get; }

        internal NativeSkinningCorrectionCache Cache { get; }
    }

    internal sealed class NativeSkinningCorrectionPreprocessResult
    {
        internal NativeSkinningCorrectionPreprocessResult(
            int frameCount,
            int correctedFrameCount,
            int fallbackFrameCount,
            int correctionEntryCount,
            NativeSkinningRendererCorrection[] rendererCorrections,
            int collectedFailureCount = 0)
        {
            FrameCount = frameCount;
            CorrectedFrameCount = correctedFrameCount;
            FallbackFrameCount = fallbackFrameCount;
            CorrectionEntryCount = correctionEntryCount;
            RendererCorrections = rendererCorrections;
            CollectedFailureCount = collectedFailureCount;
        }

        internal int FrameCount { get; }

        internal int CorrectedFrameCount { get; }

        internal int FallbackFrameCount { get; }

        internal int CorrectionEntryCount { get; }

        internal NativeSkinningRendererCorrection[] RendererCorrections { get; }

        /// <summary>
        /// 수집 모드에서 보정 없이 건너뛴 실패 수입니다. 0보다 크면 결과는 부분 보정입니다.
        /// </summary>
        internal int CollectedFailureCount { get; }
    }

    /// <summary>
    /// 병렬 사전 계산 입력. 메인스레드에서 미리 캡처한 정점 스냅샷과 스키닝 행렬을 담고,
    /// 솔버가 워커 스레드에서 Unity API 없이 처리할 수 있게 한다.
    /// 스키닝 행렬이 있으면 워커가 LBS로 베이스라인과 본 변환을 직접 계산하므로
    /// 베이스라인 맵은 검증된 렌더러에서 생략할 수 있다.
    /// </summary>
    internal sealed class NativeSkinningPreparedFrame
    {
        internal NativeSkinningPreparedFrame(
            int frameIndex,
            IReadOnlyDictionary<SkinnedMeshRenderer, Vector3[]> baselineVertices,
            IReadOnlyDictionary<
                SkinnedMeshRenderer,
                Matrix4x4[]> skinningMatrices)
        {
            FrameIndex = frameIndex;
            BaselineVertices = baselineVertices;
            SkinningMatrices = skinningMatrices;
        }

        internal int FrameIndex { get; }

        internal IReadOnlyDictionary<SkinnedMeshRenderer, Vector3[]>
            BaselineVertices { get; }

        internal IReadOnlyDictionary<
            SkinnedMeshRenderer,
            Matrix4x4[]> SkinningMatrices { get; }
    }

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
                return true;
            }
            if (IsFaulted || IsCanceled)
            {
                return false;
            }

            try
            {
                int frameIndex = ProcessedFrameCount;
                var pendingFrames = new List<PendingRendererFrame>(
                    _rendererWorks.Length);
                bool usedFallback = false;
                int frameCorrectionEntryCount = 0;
                if (!ProcessRendererFrames(frameIndex, pendingFrames,
                        ref usedFallback, ref frameCorrectionEntryCount))
                {
                    return false;
                }

                foreach (PendingRendererFrame pending in pendingFrames)
                {
                    pending.Work.Frames.Add(pending.Frame);
                }
                if (pendingFrames.Count > 0)
                {
                    _correctedFrameCount++;
                    _correctionEntryCount += frameCorrectionEntryCount;
                }
                if (usedFallback)
                {
                    _fallbackFrameCount++;
                }

                _processedFrameCount++;
                _reportProgress?.Invoke(_processedFrameCount, _frameCount);
                if (_processedFrameCount == _frameCount)
                {
                    Complete();
                }
                return true;
            }
            catch (Exception exception)
            {
                return Fail(
                    $"Native 보정 사전 계산 중 예외가 발생했습니다: {exception.Message}");
            }
        }

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
                        continue;
                    }
                    Fail(errorMessage);
                    return false;
                }
                usedFallback |= rendererUsedFallback;
                if (frame == null)
                {
                    continue;
                }
                pendingFrames.Add(new PendingRendererFrame(work, frame));
                frameCorrectionEntryCount += frame.CorrectionCount;
            }
            return true;
        }

        internal sealed class RendererWork
        {
            private RendererWork(
                SkinnedMeshRenderer renderer,
                NativeSkinningSurfaceContract[] contracts,
                NativeSkinningDualQuaternionBlendContract[] blendContracts,
                Vector3[] restVertices,
                BoneWeight[] boneWeights)
            {
                Renderer = renderer;
                // 워커 스레드의 오류 메시지용 — 생성 시점(메인스레드)에 이름을 캐시한다.
                RendererName = renderer.name;
                Contracts = contracts;
                BlendContracts = blendContracts;
                VertexCount = restVertices.Length;
                RestVertices = restVertices;
                BoneWeights = boneWeights;
                Frames = new List<NativeSkinningCorrectionFrame>();
            }

            internal static bool TryCreate(
                SkinnedMeshRenderer renderer,
                NativeSkinningSurfaceContract[] contracts,
                out RendererWork work)
            {
                work = null;
                if (renderer == null ||
                    renderer.sharedMesh == null ||
                    contracts == null ||
                    contracts.Length == 0)
                {
                    return false;
                }

                Mesh mesh = renderer.sharedMesh;
                Vector3[] restVertices = mesh.vertices;
                BoneWeight[] boneWeights = mesh.boneWeights;
                if (restVertices.Length != mesh.vertexCount ||
                    boneWeights.Length != mesh.vertexCount ||
                    !NativeSkinningSurfaceCorrectionCalculator.AreVerticesFinite(
                        restVertices))
                {
                    return false;
                }

                var blendContracts = new NativeSkinningDualQuaternionBlendContract[
                    contracts.Length];
                for (int contractIndex = 0;
                     contractIndex < contracts.Length;
                     contractIndex++)
                {
                    if (!NativeSkinningDualQuaternionBlendContractBuilder.TryBuild(
                            boneWeights,
                            contracts[contractIndex],
                            out blendContracts[contractIndex]))
                    {
                        return false;
                    }
                }

                work = new RendererWork(
                    renderer,
                    contracts,
                    blendContracts,
                    restVertices,
                    boneWeights);
                return true;
            }

            internal SkinnedMeshRenderer Renderer { get; }

            internal string RendererName { get; }

            internal NativeSkinningSurfaceContract[] Contracts { get; }

            internal NativeSkinningDualQuaternionBlendContract[] BlendContracts { get; }

            internal int VertexCount { get; }

            internal Vector3[] RestVertices { get; }

            internal BoneWeight[] BoneWeights { get; }

            internal List<NativeSkinningCorrectionFrame> Frames { get; }

            // LBS 베이스라인 자가검증 상태 — 워커 표결과 드라이버 조회가 겹치므로
            // verdict는 volatile 쓰기, 비교 횟수는 Interlocked로만 갱신한다.
            internal const int LbsUndecided = 0;
            internal const int LbsUsable = 1;
            internal const int LbsRejected = 2;
            internal volatile int LbsVerdict;
            internal int LbsComparedFrameCount;
        }

        private readonly struct PendingRendererFrame
        {
            internal PendingRendererFrame(
                RendererWork work,
                NativeSkinningCorrectionFrame frame)
            {
                Work = work;
                Frame = frame;
            }

            internal RendererWork Work { get; }

            internal NativeSkinningCorrectionFrame Frame { get; }
        }
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
            if (frameCount <= 0 ||
                contracts == null ||
                contracts.Length == 0 ||
                contracts.Any(contract => contract == null) ||
                readFrameVertices == null)
            {
                return false;
            }

            var works = new List<
                NativeSkinningCorrectionPreprocessSession.RendererWork>();
            foreach (IGrouping<SkinnedMeshRenderer, NativeSkinningSurfaceContract>
                     group in contracts.GroupBy(contract => contract.Renderer))
            {
                NativeSkinningSurfaceContract[] rendererContracts = group.ToArray();
                if (group.Key == null ||
                    group.Key.sharedMesh == null ||
                    rendererContracts.Any(contract =>
                        contract.VertexCount != group.Key.sharedMesh.vertexCount) ||
                    !NativeSkinningCorrectionPreprocessSession.RendererWork.TryCreate(
                        group.Key,
                        rendererContracts,
                        out NativeSkinningCorrectionPreprocessSession.RendererWork work))
                {
                    return false;
                }
                works.Add(work);
            }
            if (works.Count == 0 ||
                works.Sum(work => work.Contracts.Length) != contracts.Length)
            {
                return false;
            }

            session = new NativeSkinningCorrectionPreprocessSession(
                frameCount,
                works.ToArray(),
                readFrameVertices,
                reportProgress,
                collectFailures);
            return true;
        }

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
