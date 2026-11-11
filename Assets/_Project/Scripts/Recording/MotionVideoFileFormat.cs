using UnityEngine;

namespace Fbx2Vmd.Recording
{
    /// <summary>
    /// 영상 녹화 출력 컨테이너/코덱 선택지.
    /// </summary>
    public enum MotionVideoFileFormat
    {
        [InspectorName("MP4 (H.264, 배경 불투명)")] Mp4 = 0,
        [InspectorName("WebM (VP9, 배경 투명·작은 용량)")] WebM = 1,
        [InspectorName("MOV (ProRes 4444, 편집용·배경 투명)")] MovProRes = 2,
    }

    public static class MotionVideoFileFormatExtensions
    {
        /// <summary>알파(투명 배경) 채널을 담을 수 있는 포맷인지 반환함.</summary>
        public static bool SupportsAlpha(this MotionVideoFileFormat format) =>
            format == MotionVideoFileFormat.MovProRes ||
            format == MotionVideoFileFormat.WebM;
    }
}
