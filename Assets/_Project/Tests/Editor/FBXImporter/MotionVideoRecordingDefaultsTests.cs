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
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
