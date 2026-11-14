using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class NativeSkinningCorrectionPreprocessSession
    {
        /// <summary>
        /// 미리 캡처한 프레임 스냅샷 묶음을 병렬로 계산하고 결과를 프레임 순서대로 커밋함.
        /// 워커 스레드에서 호출되므로 Unity API에 접근하지 않는다.
        /// </summary>
        internal bool TryProcessPreparedFrames(
            IReadOnlyList<NativeSkinningPreparedFrame> preparedFrames)
        {
            if (preparedFrames == null)
            {
                return Fail("병렬 사전 계산 입력이 없습니다.");
            }

            int expectedFrameIndex = _processedFrameCount;
            var outcomes = new PreparedFrameOutcome[preparedFrames.Count];
            try
            {
                Parallel.For(0, preparedFrames.Count, index =>
                {
                    if (IsCanceled || IsFaulted)
                    {
                        return;
                    }

                    NativeSkinningPreparedFrame prepared = preparedFrames[index];
                    if (prepared == null ||
                        prepared.FrameIndex != expectedFrameIndex + index)
                    {
                        outcomes[index] = PreparedFrameOutcome.Fail(
                            "병렬 사전 계산 입력이 프레임 순서와 다릅니다.");
                        return;
                    }
                    outcomes[index] = SolvePreparedFrame(prepared);
                });

                if (!CommitPreparedOutcomes(outcomes))
                {
                    return false;
                }
            }
            catch (Exception exception)
            {
                return Fail(
                    $"Native 보정 사전 계산 중 예외가 발생했습니다: {exception.Message}");
            }
            return !IsFaulted;
        }

        private bool CommitPreparedOutcomes(PreparedFrameOutcome[] outcomes)
        {
            // 계산 결과는 프레임 순서대로 커밋해 순차 처리와 동일한 결과 순서를 보장한다.
            foreach (PreparedFrameOutcome outcome in outcomes)
            {
                if (outcome == null || IsCanceled)
                {
                    break;
                }
                if (outcome.FatalMessage != null)
                {
                    Fail(outcome.FatalMessage);
                    return false;
                }
                lock (_stateLock)
                {
                    if (IsCanceled)
                    {
                        break;
                    }
                    _collectedFailures.AddRange(outcome.WorkFailures);
                    foreach (PendingRendererFrame pending in outcome.Frames)
                    {
                        pending.Work.Frames.Add(pending.Frame);
                    }
                    if (outcome.Frames.Count > 0)
                    {
                        _correctedFrameCount++;
                        _correctionEntryCount += outcome.CorrectionEntryCount;
                    }
                    if (outcome.UsedFallback)
                    {
                        _fallbackFrameCount++;
                    }
                    _processedFrameCount++;
                    _reportProgress?.Invoke(
                        _processedFrameCount,
                        _frameCount);
                    if (_processedFrameCount == _frameCount)
                    {
                        CompleteLocked();
                        break;
                    }
                }
            }
            return true;
        }

        // 한 프레임의 모든 렌더러 계약을 스냅샷 입력으로 계산한다 (Unity API 미사용).
        private PreparedFrameOutcome SolvePreparedFrame(
            NativeSkinningPreparedFrame prepared)
        {
            int frameIndex = prepared.FrameIndex;
            var outcome = new PreparedFrameOutcome();
            foreach (RendererWork work in _rendererWorks)
            {
                Vector3[] baselineVertices = null;
                prepared.BaselineVertices?.TryGetValue(
                    work.Renderer,
                    out baselineVertices);
                Matrix4x4[] skinningMatrices = null;
                prepared.SkinningMatrices?.TryGetValue(
                    work.Renderer,
                    out skinningMatrices);
                if (!TryResolvePreparedBaseline(frameIndex, work,
                        skinningMatrices, ref baselineVertices,
                        out string fatalMessage))
                {
                    outcome.FatalMessage = fatalMessage;
                    return outcome;
                }
                if (!TryCalculateRendererFrameCore(
                        frameIndex,
                        baselineVertices,
                        CreateBoneTransformsProvider(skinningMatrices),
                        work,
                        out NativeSkinningCorrectionFrame frame,
                        out bool usedFallback,
                        out string errorMessage))
                {
                    if (_collectModeEnabled)
                    {
                        outcome.WorkFailures.Add(
                            $"renderer={work.RendererName} {errorMessage}");
                        continue;
                    }
                    outcome.FatalMessage = errorMessage;
                    return outcome;
                }
                outcome.UsedFallback |= usedFallback;
                if (frame != null)
                {
                    outcome.Frames.Add(new PendingRendererFrame(work, frame));
                    outcome.CorrectionEntryCount += frame.CorrectionCount;
                }
            }
            return outcome;
        }

        // 드라이버가 프레임 캡처 전에 묻는다 — true면 해당 렌더러를 베이크해야 함.
        internal bool RequiresBakedBaseline(SkinnedMeshRenderer renderer)
        {
            foreach (RendererWork work in _rendererWorks)
            {
                if (work.Renderer == renderer)
                {
                    return work.LbsVerdict != RendererWork.LbsUsable;
                }
            }
            return true;
        }

        // 병렬 프레임 하나의 계산 결과. 커밋은 호출 순서(프레임 순서)대로만 이뤄진다.
        private sealed class PreparedFrameOutcome
        {
            internal static PreparedFrameOutcome Fail(string message)
            {
                var outcome = new PreparedFrameOutcome();
                outcome.FatalMessage = message;
                return outcome;
            }

            internal List<PendingRendererFrame> Frames { get; } =
                new List<PendingRendererFrame>();

            internal List<string> WorkFailures { get; } =
                new List<string>();

            internal string FatalMessage { get; set; }

            internal bool UsedFallback { get; set; }

            internal int CorrectionEntryCount { get; set; }
        }
    }
}
