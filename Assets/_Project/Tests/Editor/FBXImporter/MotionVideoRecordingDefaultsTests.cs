using Fbx2Vmd.FBXImporter;
using Fbx2Vmd.Recording;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor.FBXImporter
{
    /// <summary>
    /// 새로 붙인 파이프라인의 영상 녹화 기본값 검증. 투명 배경 MOV·60fps 기본값은
    /// 메모리 부족 시 녹화 파일 최종화 실패로 이어졌으므로 가벼운 MP4·30fps를 유지해야 함.
    /// </summary>
    public class MotionVideoRecordingDefaultsTests
    {
        [Test]
        public void Given_NewPipeline_When_ReadingVideoDefaults_Then_UsesMp4At30Fps()
        {
            var host = new GameObject("MotionVideoDefaults");
            try
            {
                var pipeline = host.AddComponent<FBXVmdPipeline>();

                Assert.That(pipeline.motionVideoFormat, Is.EqualTo(MotionVideoFileFormat.Mp4));
                Assert.That(pipeline.motionVideoFrameRate, Is.EqualTo(MotionVideoFrameRate.Fps30));
                Assert.That(pipeline.motionVideoResolution, Is.EqualTo(MotionVideoResolution.FullHd));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [TestCase(MotionVideoResolution.FullHd, 1920, 1080)]
        [TestCase(MotionVideoResolution.Qhd, 2560, 1440)]
        [TestCase(MotionVideoResolution.Uhd4K, 3840, 2160)]
        public void Given_FixedResolution_When_ResolvingSize_Then_IgnoresSmallGameView(
            MotionVideoResolution resolution, int width, int height)
        {
            // Game 창이 작아도 영상은 지정 크기로 저장되어야 함
            Assert.That(resolution.ToPixelSize(640, 361), Is.EqualTo(new Vector2Int(width, height)));
        }

        [Test]
        public void Given_GameViewResolutionWithOddSize_When_ResolvingSize_Then_RoundsDownToEven()
        {
            // H.264 인코더는 홀수 크기를 받지 않음
            Assert.That(MotionVideoResolution.GameView.ToPixelSize(1281, 721), Is.EqualTo(new Vector2Int(1280, 720)));
        }

        [TestCase(0, 0, 2, 2)]
        [TestCase(5120, 2880, 4096, 2304)]
        public void Given_GameViewOutsideEncoderRange_When_ResolvingSize_Then_ClampsToEncoderRange(
            int screenWidth, int screenHeight, int width, int height)
        {
            // 크기 0 또는 인코더 상한 초과 화면도 녹화 준비에서 실패하지 않아야 함
            Assert.That(MotionVideoResolution.GameView.ToPixelSize(screenWidth, screenHeight),
                Is.EqualTo(new Vector2Int(width, height)));
        }
    }
}
