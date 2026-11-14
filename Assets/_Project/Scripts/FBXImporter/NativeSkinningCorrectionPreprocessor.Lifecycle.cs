using System.Collections.Generic;
using System.Linq;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class NativeSkinningCorrectionPreprocessSession
    {
        private int _correctedFrameCount;

        private int _fallbackFrameCount;

        private int _correctionEntryCount;
        // 진단용: 수집 모드에서 실패한 프레임의 오류 메시지 누적.
        private readonly List<string> _collectedFailures = new List<string>();

        private readonly bool _collectModeEnabled;
        // 병렬 솔버 커밋과 취소/실패가 겹칠 때 상태 쓰기를 직렬화한다.
        private readonly object _stateLock = new object();
        private volatile bool _isComplete;

        private volatile bool _isFaulted;

        private volatile bool _isCanceled;

        internal int ProcessedFrameCount => _processedFrameCount;

        internal float Progress => (float)_processedFrameCount / _frameCount;

        internal bool IsComplete
        {
            get => _isComplete;
            private set => _isComplete = value;
        }

        internal bool IsFaulted
        {
            get => _isFaulted;
            private set => _isFaulted = value;
        }

        internal bool IsCanceled
        {
            get => _isCanceled;
            private set => _isCanceled = value;
        }

        internal bool IsFinished => IsComplete || IsFaulted || IsCanceled;

        internal string FailureMessage { get; private set; } = string.Empty;

        internal NativeSkinningCorrectionPreprocessResult Result { get; private set; }

        internal void Cancel()
        {
            if (IsFinished)
            {
                return;
            }
            IsCanceled = true;
            lock (_stateLock)
            {
                ClearPartialFrames();
            }
        }

        private void Complete()
        {
            lock (_stateLock)
            {
                CompleteLocked();
            }
        }

        // 호출자가 _stateLock을 쥔 상태에서만 호출한다.
        private void CompleteLocked()
        {
            WriteCollectedFailures();
            NativeSkinningRendererCorrection[] rendererCorrections =
                _rendererWorks
                    .Where(work => work.Frames.Count > 0)
                    .Select(work => new NativeSkinningRendererCorrection(
                        work.Renderer,
                        new NativeSkinningCorrectionCache(
                            work.VertexCount,
                            work.Frames.ToArray())))
                    .ToArray();
            Result = new NativeSkinningCorrectionPreprocessResult(
                _frameCount,
                _correctedFrameCount,
                _fallbackFrameCount,
                _correctionEntryCount,
                rendererCorrections,
                _collectedFailures.Count);
            IsComplete = true;
        }

        private bool Fail(string message)
        {
            lock (_stateLock)
            {
                FailureMessage = string.IsNullOrWhiteSpace(message)
                    ? "Native 보정 사전 계산에 실패했습니다."
                    : message;
                IsFaulted = true;
                Result = null;
                ClearPartialFrames();
                return false;
            }
        }

        // 호출자가 _stateLock을 쥔 상태에서만 호출한다.
        private void ClearPartialFrames()
        {
            foreach (RendererWork work in _rendererWorks)
            {
                work.Frames.Clear();
            }
        }

    }
}
