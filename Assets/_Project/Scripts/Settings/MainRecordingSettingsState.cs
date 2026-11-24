using System;
using System.Globalization;

namespace Fbx2Vmd.Settings
{
    [Serializable]
    public sealed class MainRecordingSettingsState
    {
        public const string Playing = "playing";
        public const string Stopped = "stopped";

        public string playMode = Stopped;
        public string updatedAtUtc = string.Empty;
        public bool hasPreparedMotion;
        public bool isPlayingMotion;
        public bool isPreparingMotion;
        public bool isRecording;
        public float motionTimeSeconds;
        public string motionError = string.Empty;
        public bool isSurfaceCorrectionEnabled = true;
        public string surfaceCorrectionFailure = string.Empty;
        // 설정창이 캐릭터 선택 전 FBX 가져오기를 막고, 가져오기 진행·완료를 실제 상태로 표시하는 데 씀.
        public bool hasTargetCharacter;
        public string targetCharacterName = string.Empty;
        public bool isImportingFbx;
        // 재생 타임라인·프레임 이동 표시(프레임은 0 기반).
        public int currentFrameIndex;
        public int lastFrameIndex;
        public float motionFrameRate;
        public float motionClipLengthSeconds;
        public string motionClipName = string.Empty;
        // FBX 처리 진행률(0~1)과 안내 문구.
        public float sessionProgress;
        public string sessionMessage = string.Empty;
        // 녹화 진행률(0~1), 저장 파일, 파일 완성 확인 여부.
        public float recordingProgress;
        public string recordingOutputPath = string.Empty;
        public bool isRecordingFileFinalized;
        public bool isRecordingFileFailed;

        public static MainRecordingSettingsState Create(string playMode, DateTime utcNow)
        {
            return new MainRecordingSettingsState
            {
                playMode = NormalizePlayMode(playMode),
                updatedAtUtc = utcNow.ToString("O"),
            };
        }

        public void Normalize()
        {
            playMode = NormalizePlayMode(playMode);
            if (!HasValidTimestamp(updatedAtUtc))
            {
                updatedAtUtc = string.Empty;
            }
        }

        public static string NormalizePlayMode(string value)
        {
            return string.Equals(value, Playing, StringComparison.OrdinalIgnoreCase)
                ? Playing
                : Stopped;
        }

        private static bool HasValidTimestamp(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   DateTime.TryParse(
                       value,
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.RoundtripKind,
                       out _);
        }
    }
}
