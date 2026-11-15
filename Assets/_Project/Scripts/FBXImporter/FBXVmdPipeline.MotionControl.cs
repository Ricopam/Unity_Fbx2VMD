using UnityEngine;
using UnityEngine.Serialization;
using System;
using System.IO;
using System.Linq;

namespace Fbx2Vmd.FBXImporter
{
    public partial class FBXVmdPipeline
    {
        public bool TryPlayImportedMotion()
        {
            if (IsImportedMotionRecording ||
                _humanoidMotionPlaybackController == null ||
                !TryPrepareNativeSkinningCorrection(
                    NativeSkinningPendingPlaybackRequest.Play,
                    out bool shouldStartImmediately))
            {
                return false;
            }

            return !shouldStartImmediately ||
                TryStartImportedMotionPlaybackNow();
        }

        private bool TryStartImportedMotionPlaybackNow()
        {
            if (!_humanoidMotionPlaybackController.Play())
            {
                return false;
            }

            SetSessionState(
                FBXSessionState.PreviewPlaying,
                "FBX 모션 재생 중",
                ResolveHumanoidPlaybackProgress());
            return true;
        }

        public bool TryPauseImportedMotion()
        {
            if (IsImportedMotionRecording ||
                IsPreparingImportedMotionCorrection ||
                _humanoidMotionPlaybackController == null ||
                !_humanoidMotionPlaybackController.Pause())
            {
                return false;
            }

            SetSessionState(
                FBXSessionState.PreviewPaused,
                "FBX 모션 일시정지",
                ResolveHumanoidPlaybackProgress());
            return true;
        }

        public bool TryStopImportedMotion()
        {
            if (TryCancelPendingCorrectionPreparation())
            {
                return true;
            }

            if (IsImportedMotionRecording)
            {
                return TryStopImportedMotionRecording();
            }

            if (_humanoidMotionPlaybackController == null ||
                !_humanoidMotionPlaybackController.Stop())
            {
                return false;
            }

            SetSessionState(FBXSessionState.Ready, "FBX 모션 재생 준비 완료", 1f);
            return true;
        }

        private void PausePlaybackIfRunning()
        {
            if (_humanoidMotionPlaybackController.State ==
                HumanoidMotionPlaybackState.Playing)
            {
                _humanoidMotionPlaybackController.Pause();
            }
        }

        private bool TryCancelPendingCorrectionPreparation()
        {
            if (_pendingNativeSkinningPlaybackRequest ==
                    NativeSkinningPendingPlaybackRequest.None)
            {
                return false;
            }

            _pendingNativeSkinningPlaybackRequest =
                NativeSkinningPendingPlaybackRequest.None;
            if (IsPreparingImportedMotionCorrection)
            {
                _nativeSkinningCorrectionPlaybackDriver.CancelPreparation();
            }
            else if (_nativeSkinningCorrectionPlaybackDriver?.IsFaulted ?? false)
            {
                _nativeSkinningCorrectionPlaybackDriver.Invalidate();
            }

            _humanoidMotionPlaybackController?.Stop();
            SetSessionState(
                FBXSessionState.Ready,
                "FBX 표면 보정 준비 취소 · 재생 대기",
                1f);
            return true;
        }

        public bool TrySeekImportedMotion(float timeSeconds)
        {
            if (IsImportedMotionRecording ||
                IsPreparingImportedMotionCorrection ||
                _humanoidMotionPlaybackController == null ||
                !_humanoidMotionPlaybackController.Seek(timeSeconds))
            {
                return false;
            }

            SetSessionState(
                _humanoidMotionPlaybackController.State == HumanoidMotionPlaybackState.Playing
                    ? FBXSessionState.PreviewPlaying
                    : FBXSessionState.PreviewPaused,
                $"FBX 모션 위치: {_humanoidMotionPlaybackController.CurrentTimeSeconds:F2}초",
                ResolveHumanoidPlaybackProgress());
            return true;
        }

        public bool TrySeekImportedMotionFrame(int frameIndex)
        {
            if (IsImportedMotionRecording ||
                IsPreparingImportedMotionCorrection ||
                _humanoidMotionPlaybackController == null)
            {
                return false;
            }

            if (!SeekFrameAndRestorePreview(frameIndex))
            {
                return false;
            }

            SetSessionState(
                FBXSessionState.PreviewPaused,
                $"FBX 모션 프레임: {_humanoidMotionPlaybackController.CurrentFrameIndex}",
                ResolveHumanoidPlaybackProgress());
            return true;
        }

        private bool SeekFrameAndRestorePreview(int frameIndex)
        {
            if (_humanoidMotionPlaybackController.State ==
                HumanoidMotionPlaybackState.Playing)
            {
                _humanoidMotionPlaybackController.Pause();
            }

            if (!_humanoidMotionPlaybackController.SeekFrame(frameIndex))
            {
                return false;
            }

            if (_poseCorrectionDocument != null)
            {
                _humanoidMotionPlaybackController.TryPreviewPoseCorrection(
                    _poseCorrectionDocument);
            }

            return true;
        }

        public bool TryCaptureImportedMotionPose(out HumanPose pose)
        {
            pose = default;
            return !IsImportedMotionRecording &&
                !IsPreparingImportedMotionCorrection &&
                _humanoidMotionPlaybackController != null &&
                _humanoidMotionPlaybackController.TryCaptureCurrentPose(out pose);
        }

        internal bool TryCaptureImportedMotionFootSurface(out HumanoidFootGroundingSnapshot left,
            out HumanoidFootGroundingSnapshot right, out HumanoidFootGroundingStatus status,
            out HumanoidFootGroundingGate gate)
        {
            left = null;
            right = null;
            status = _humanoidMotionPlaybackController?.LastGroundingStatus ??
                HumanoidFootGroundingStatus.Unavailable;
            gate = _humanoidMotionPlaybackController?.LastGroundingGate;
            return !IsImportedMotionRecording && !IsPreparingImportedMotionCorrection &&
                _humanoidMotionPlaybackController != null &&
                _humanoidMotionPlaybackController.TryCaptureCurrentFootSurface(out left, out right);
        }

        public bool TryApplyImportedMotionMuscleDelta(
            string muscleName,
            float delta)
        {
            if (IsImportedMotionRecording ||
                IsPreparingImportedMotionCorrection ||
                _humanoidMotionPlaybackController == null ||
                !_humanoidMotionPlaybackController.TryCaptureCurrentPose(out _))
            {
                return false;
            }

            PausePlaybackIfRunning();

            if (!TryApplyMuscleDeltaToDocument(muscleName, delta))
            {
                return false;
            }

            int frameIndex = _humanoidMotionPlaybackController.CurrentFrameIndex;

            _nativeSkinningCorrectionPlaybackDriver?.Invalidate();

            SetSessionState(
                FBXSessionState.PreviewPaused,
                $"FBX 자세 수정 미리보기: {frameIndex} 프레임",
                ResolveHumanoidPlaybackProgress());
            return true;
        }

        private bool TryApplyMuscleDeltaToDocument(
            string muscleName,
            float delta)
        {
            _poseCorrectionDocument ??= new HumanoidPoseCorrectionDocument(
                _preparedMotionName,
                _humanoidMotionPlaybackController.ClipFrameRate);
            int frameIndex = _humanoidMotionPlaybackController.CurrentFrameIndex;
            return _poseCorrectionDocument.TrySetMuscleDelta(
                    frameIndex,
                    muscleName,
                    delta) &&
                _humanoidMotionPlaybackController.TryPreviewPoseCorrection(
                    _poseCorrectionDocument);
        }

        public bool TryRestoreImportedMotionPoseFrame()
        {
            if (IsImportedMotionRecording ||
                IsPreparingImportedMotionCorrection ||
                _humanoidMotionPlaybackController == null)
            {
                return false;
            }

            if (_humanoidMotionPlaybackController.State ==
                HumanoidMotionPlaybackState.Playing)
            {
                _humanoidMotionPlaybackController.Pause();
            }

            int frameIndex = _humanoidMotionPlaybackController.CurrentFrameIndex;
            if (!_humanoidMotionPlaybackController.RestoreCurrentPose())
            {
                return false;
            }

            _poseCorrectionDocument?.TryRemoveFrame(frameIndex);
            _nativeSkinningCorrectionPlaybackDriver?.Invalidate();
            SetSessionState(
                FBXSessionState.PreviewPaused,
                $"FBX 원본 자세 복원: {frameIndex} 프레임",
                ResolveHumanoidPlaybackProgress());
            return true;
        }

        public bool TrySaveImportedMotionPoseCorrections(
            string filePath,
            out string errorMessage)
        {
            if (IsImportedMotionRecording)
            {
                errorMessage = "녹화 중에는 Humanoid 보정 문서를 저장할 수 없습니다.";
                return false;
            }

            if (_humanoidMotionPlaybackController == null ||
                !_humanoidMotionPlaybackController.IsPrepared ||
                _poseCorrectionDocument == null)
            {
                errorMessage = "저장할 Humanoid 보정 문서가 없습니다.";
                return false;
            }

            return HumanoidPoseCorrectionFileStore.TrySave(
                filePath,
                _poseCorrectionDocument,
                out errorMessage);
        }

    }
}
