using UnityEngine;
using System;
using Fbx2Vmd.Profiling;

namespace Fbx2Vmd.FBXImporter
{
    public partial class FBXVmdPipeline
    {
        private bool TryPrepareNativeSkinningCorrection(
            NativeSkinningPendingPlaybackRequest request,
            out bool shouldStartImmediately)
        {
            shouldStartImmediately = false;
            if (!TryGateNativeSkinningCorrection(
                    request,
                    ref shouldStartImmediately,
                    out bool gateResult))
            {
                return gateResult;
            }
            if (!_nativeSkinningCorrectionPlaybackDriver.BeginPreparation())
            {
                SetSessionState(
                    FBXSessionState.Failed,
                    _nativeSkinningCorrectionPlaybackDriver.FailureMessage,
                    0f);
                return false;
            }

            _pendingNativeSkinningPlaybackRequest = request;
            SetSessionState(
                FBXSessionState.Retargeting,
                "FBX 팔 표면 보정 준비 중",
                0f);
            return true;
        }

        private bool TryGateNativeSkinningCorrection(
            NativeSkinningPendingPlaybackRequest request,
            ref bool shouldStartImmediately,
            out bool gateResult)
        {
            gateResult = false;
            if (!TryGateCorrectionDisabled(
                    request,
                    ref shouldStartImmediately,
                    ref gateResult))
            {
                return false;
            }
            return TryGateDriverState(
                request,
                ref shouldStartImmediately,
                ref gateResult);
        }

        private bool TryGateDriverState(
            NativeSkinningPendingPlaybackRequest request,
            ref bool shouldStartImmediately,
            ref bool gateResult)
        {
            if (_nativeSkinningCorrectionPlaybackDriver == null ||
                _nativeSkinningCorrectionPlaybackDriver.IsReady)
            {
                shouldStartImmediately = true;
                gateResult = true;
                return false;
            }
            if (_nativeSkinningCorrectionPlaybackDriver.IsFaulted)
            {
                SetSessionState(
                    FBXSessionState.Failed,
                    _nativeSkinningCorrectionPlaybackDriver.FailureMessage,
                    0f);
                return false;
            }
            if (_nativeSkinningCorrectionPlaybackDriver.IsPreparing)
            {
                gateResult = _pendingNativeSkinningPlaybackRequest == request;
                return false;
            }
            return true;
        }

        private bool TryGateCorrectionDisabled(
            NativeSkinningPendingPlaybackRequest request,
            ref bool shouldStartImmediately,
            ref bool gateResult)
        {
            if (!IsNativeSkinningCorrectionEnabled)
            {
                shouldStartImmediately = request == NativeSkinningPendingPlaybackRequest.Play;
                if (!shouldStartImmediately)
                {
                    SetSessionState(FBXSessionState.Ready, "비교 재생 상태입니다. 녹화하려면 표면 보정을 켜 주세요.", 0f);
                }
                gateResult = shouldStartImmediately;
                return false;
            }
            if (!string.IsNullOrWhiteSpace(_nativeSkinningCorrectionSetupError))
            {
                SetSessionState(
                    FBXSessionState.Failed,
                    _nativeSkinningCorrectionSetupError,
                    0f);
                return false;
            }
            return true;
        }

        private bool TickNativeSkinningCorrectionPreparation()
        {
            using var perfScope = PerfScope.Measure("FBXVmdPipeline.TickNativeSkinningCorrectionPreparation");
            if (_nativeSkinningCorrectionPlaybackDriver == null)
            {
                return false;
            }
            if (_nativeSkinningCorrectionPlaybackDriver.IsPreparing)
            {
                return HandlePreparationInProgress();
            }
            if (_pendingNativeSkinningPlaybackRequest ==
                NativeSkinningPendingPlaybackRequest.None)
            {
                return false;
            }
            return DispatchPendingPlaybackRequest();
        }

        private bool HandlePreparationInProgress()
        {
            // 방어적 불변식: 정상 호출 경로에선 녹화 중 재준비가 성립하지 않지만,
            // 성립하면 재생 틱만 스킵된 채 Recorder가 렌더러 숨김 상태를 계속
            // 캡처해 영상이 오염되므로 조용히 두지 않고 중단함.
            if (IsImportedMotionRecording)
            {
                Debug.LogWarning(
                    "[FBXImport] 영상 녹화 중 보정 재준비가 발생해 녹화를 중단합니다.");
                TryStopImportedMotionRecording();
            }
            SetSessionState(
                FBXSessionState.Retargeting,
                $"FBX 팔 표면 보정 준비 중: " +
                $"{ImportedMotionCorrectionProcessedFrameCount} / " +
                $"{ImportedMotionCorrectionTotalFrameCount}",
                ImportedMotionCorrectionPreparationProgress,
                shouldLog: false);
            return true;
        }

    }
}
