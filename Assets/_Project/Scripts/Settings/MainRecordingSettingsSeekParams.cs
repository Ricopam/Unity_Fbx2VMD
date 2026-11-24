using System;
using UnityEngine;

namespace Fbx2Vmd.Settings
{
    /// <summary>설정창 프레임 이동 명령(SeekMotionFrame)의 paramsJson을 검증해 읽음.</summary>
    public static class MainRecordingSettingsSeekParams
    {
        [Serializable]
        private sealed class Payload
        {
            // 필드가 빠진 문서를 0프레임 이동으로 오인하지 않도록 기본값을 음수로 둠.
            public int frameIndex = -1;
        }

        public static bool TryReadFrameIndex(string paramsJson, out int frameIndex)
        {
            frameIndex = 0;
            if (string.IsNullOrWhiteSpace(paramsJson))
            {
                return false;
            }

            Payload payload;
            try
            {
                payload = JsonUtility.FromJson<Payload>(paramsJson);
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (payload == null || payload.frameIndex < 0)
            {
                return false;
            }

            frameIndex = payload.frameIndex;
            return true;
        }
    }
}
