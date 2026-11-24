using UnityEngine;

namespace Fbx2Vmd.Recording
{
    /// <summary>
    /// 영상 녹화 해상도 선택지. Game 창 크기와 무관하게 카메라를 지정 해상도로 렌더링해 저장함.
    /// </summary>
    public enum MotionVideoResolution
    {
        [InspectorName("1920 x 1080 (FHD)")] FullHd,
        [InspectorName("2560 x 1440 (QHD)")] Qhd,
        [InspectorName("3840 x 2160 (4K, 용량 큼)")] Uhd4K,
        [InspectorName("Game 창 크기 그대로")] GameView,
    }

    public static class MotionVideoResolutionExtensions
    {
        // Windows 영상 인코더가 받는 최대 크기
        private const int MaxEncoderWidth = 4096;
        private const int MaxEncoderHeight = 2304;

        /// <summary>
        /// 녹화할 가로·세로 픽셀을 구함. Game 창 크기 선택 시에만 화면 크기를 쓰며,
        /// H.264 인코더가 홀수 크기와 상한 초과를 받지 않으므로 상한 안의 짝수로 맞춤.
        /// </summary>
        public static Vector2Int ToPixelSize(this MotionVideoResolution resolution, int screenWidth, int screenHeight)
        {
            switch (resolution)
            {
                case MotionVideoResolution.Qhd: return new Vector2Int(2560, 1440);
                case MotionVideoResolution.Uhd4K: return new Vector2Int(3840, 2160);
                case MotionVideoResolution.GameView:
                    return new Vector2Int(
                        Mathf.Clamp(screenWidth & ~1, 2, MaxEncoderWidth),
                        Mathf.Clamp(screenHeight & ~1, 2, MaxEncoderHeight));
                default: return new Vector2Int(1920, 1080);
            }
        }
    }
}
