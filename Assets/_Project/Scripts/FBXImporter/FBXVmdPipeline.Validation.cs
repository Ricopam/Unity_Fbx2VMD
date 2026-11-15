using UnityEngine;
using UnityEngine.Serialization;
using System;
using System.IO;
using System.Linq;
using Fbx2Vmd.Profiling;


namespace Fbx2Vmd.FBXImporter
{
    public partial class FBXVmdPipeline
    {

        private float ResolveHumanoidPlaybackProgress()
        {
            if (_humanoidMotionPlaybackController == null ||
                _humanoidMotionPlaybackController.ClipLengthSeconds <= 0f)
            {
                return 0f;
            }

            return Mathf.Clamp01(
                _humanoidMotionPlaybackController.CurrentTimeSeconds /
                _humanoidMotionPlaybackController.ClipLengthSeconds);
        }

        private bool TryValidateImportedMotionPoseCorrectionCompatibility(
            HumanoidPoseCorrectionDocument document,
            out string errorMessage)
        {
            if (document == null)
            {
                errorMessage = "불러온 Humanoid 보정 문서가 없습니다.";
                return false;
            }

            if (!ValidateCorrectionMotionName(document, out errorMessage))
            {
                return false;
            }

            if (!ValidateCorrectionFrameRate(document, out errorMessage))
            {
                return false;
            }

            if (!ValidateCorrectionFrameRange(document, out errorMessage))
            {
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private bool ValidateCorrectionMotionName(
            HumanoidPoseCorrectionDocument document,
            out string errorMessage)
        {
            if (!string.Equals(
                    document.MotionName,
                    _preparedMotionName,
                    StringComparison.OrdinalIgnoreCase))
            {
                errorMessage =
                    $"현재 모션과 보정 문서의 모션 이름이 다릅니다: " +
                    $"{_preparedMotionName} / {document.MotionName}";
                return false;
            }

            errorMessage = null;
            return true;
        }

        private bool ValidateCorrectionFrameRate(
            HumanoidPoseCorrectionDocument document,
            out string errorMessage)
        {
            if (Mathf.Abs(
                    document.SourceFrameRate -
                    _humanoidMotionPlaybackController.ClipFrameRate) > 0.001f)
            {
                errorMessage =
                    $"현재 모션과 보정 문서의 프레임률이 다릅니다: " +
                    $"{_humanoidMotionPlaybackController.ClipFrameRate:F3} / " +
                    $"{document.SourceFrameRate:F3}";
                return false;
            }

            errorMessage = null;
            return true;
        }

        private bool ValidateCorrectionFrameRange(
            HumanoidPoseCorrectionDocument document,
            out string errorMessage)
        {
            if (document.LastCorrectionFrameIndex >
                _humanoidMotionPlaybackController.LastFrameIndex)
            {
                errorMessage =
                    $"보정 문서의 마지막 프레임이 현재 모션 범위를 벗어납니다: " +
                    $"{document.LastCorrectionFrameIndex} / " +
                    $"{_humanoidMotionPlaybackController.LastFrameIndex}";
                return false;
            }

            errorMessage = null;
            return true;
        }

        private void CleanupHumanoidMotionPlayback()
        {
            _nativeSkinningCorrectionPlaybackDriver?.Release();
            _nativeSkinningCorrectionPlaybackDriver = null;
            _pendingNativeSkinningPlaybackRequest =
                NativeSkinningPendingPlaybackRequest.None;
            _nativeSkinningCorrectionSetupError = string.Empty;
            _humanoidMotionRecordingController?.Dispose();
            _humanoidMotionRecordingController = null;
            _humanoidMotionPlaybackController?.Dispose();
            _poseCorrectionDocument = null;
            _preparedMotionName = string.Empty;
        }

    }
}
