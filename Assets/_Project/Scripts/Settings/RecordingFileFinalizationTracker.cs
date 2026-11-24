using System;

namespace Fbx2Vmd.Settings
{
    /// <summary>
    /// 녹화가 끝난 영상 파일이 끝까지 저장됐는지 판정함.
    /// 레코더 정리는 비동기라 정지 직후 성공으로 표시하지 않고 파일 크기가 안정될 때까지 기다림.
    /// </summary>
    public sealed class RecordingFileFinalizationTracker
    {
        // 녹화 증거 수집(FbxMotionRecordingEvidenceCapture)과 같은 기준: 크기가 0보다 크고 연속 두 번 같음.
        private const int RequiredStablePolls = 2;

        private long lastObservedSize = -1;
        private int stablePolls;

        public string OutputPath { get; private set; } = string.Empty;
        public bool IsFinalized { get; private set; }
        // 정지 후 레코더가 출력 경로를 비우면(인코딩 실패) 남은 일부 파일을 결과로 쓰지 않음.
        public bool HasFailed { get; private set; }

        /// <summary>녹화 시작을 요청할 때 호출함. 시작이 미뤄지는 동안 이전 결과를 숨김.</summary>
        public void BeginNewRecording()
        {
            OutputPath = string.Empty;
            HasFailed = false;
            ResetFinalization();
        }

        /// <summary>상태 게시 주기마다 호출함. readSize는 파일이 없으면 음수를 돌려줌.</summary>
        public void Observe(bool isRecording, string currentOutputPath, Func<string, long> readSize)
        {
            if (isRecording)
            {
                RememberRecording(currentOutputPath);
                return;
            }

            bool hasPendingFile = !string.IsNullOrEmpty(OutputPath) && !IsFinalized && readSize != null;
            if (!hasPendingFile)
            {
                return;
            }

            bool recorderDroppedOutput = string.IsNullOrEmpty(currentOutputPath);
            if (recorderDroppedOutput)
            {
                MarkFailed();
                return;
            }

            ObserveFileSize(readSize(OutputPath));
        }

        // 정지 뒤 실패 판정에 쓰도록 녹화 중에 경로를 확보함.
        private void RememberRecording(string currentOutputPath)
        {
            if (!string.IsNullOrEmpty(currentOutputPath))
            {
                OutputPath = currentOutputPath;
            }

            HasFailed = false;
            ResetFinalization();
        }

        private void MarkFailed()
        {
            OutputPath = string.Empty;
            HasFailed = true;
            ResetFinalization();
        }

        private void ObserveFileSize(long size)
        {
            if (size > 0 && size == lastObservedSize)
            {
                stablePolls++;
            }
            else
            {
                stablePolls = 0;
                lastObservedSize = size;
            }

            IsFinalized = stablePolls >= RequiredStablePolls;
        }

        private void ResetFinalization()
        {
            IsFinalized = false;
            stablePolls = 0;
            lastObservedSize = -1;
        }
    }
}
