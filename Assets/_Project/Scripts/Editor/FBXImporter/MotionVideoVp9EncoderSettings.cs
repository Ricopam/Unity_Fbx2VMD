#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    [Serializable]
    [DisplayName("WebM VP9 (투명 배경)")]
    [EncoderSettings(typeof(MotionVideoVp9Encoder))]
    public sealed class MotionVideoVp9EncoderSettings : IEncoderSettings
    {
        public string Extension => "webm";
        public bool CanCaptureAlpha => true;
        public bool CanCaptureAudio => true;
        public TextureFormat GetTextureFormat(bool inputContainsAlpha) => TextureFormat.RGBA32;
        public bool SupportsCurrentPlatform() => Application.platform == RuntimePlatform.WindowsEditor;
        internal string FailureMessage { get; private set; }
        internal Action FailureHandler { get; set; }

        internal void ReportFailure(Exception exception)
        {
            if (!string.IsNullOrEmpty(FailureMessage))
                return;
            FailureMessage = "VP9 녹화 실패: " + exception.Message;
            Debug.LogError(FailureMessage);
            FailureHandler?.Invoke();
        }

        public void ValidateRecording(RecordingContext context, List<string> errors, List<string> warnings)
        {
            if (!TryFindExecutable(out _))
                errors.Add("VP9 녹화에는 FFmpeg가 필요합니다. FFmpeg를 설치하고 PATH를 확인해 주세요.");
            if (context.frameRateMode == FrameRatePlayback.Variable)
                errors.Add("VP9 녹화는 고정 프레임률만 지원합니다.");
            if (context.width <= 0 || context.height <= 0 || context.width % 2 != 0 || context.height % 2 != 0)
                errors.Add("VP9 녹화 해상도는 양의 짝수여야 합니다.");
        }

        internal static bool TryFindExecutable(out string executable)
        {
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                    continue;
                string candidate = Path.Combine(directory.Trim().Trim('"'), "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    executable = Path.GetFullPath(candidate);
                    return true;
                }
            }

            // 실행 중인 에디터가 설치 이전 PATH를 보유한 경우 WinGet 진입점도 확인함.
            executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WinGet", "Links", "ffmpeg.exe");
            return File.Exists(executable);
        }
    }
}
#endif
