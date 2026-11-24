using System.IO;
using System.Text;
using Fbx2Vmd.FBXImporter;
using NUnit.Framework;

namespace Tests.Editor.FBXImporter
{
    /// <summary>
    /// Main 파이프라인 점검의 합격 판정과 영상 완성 판정 검증.
    /// 메모리 부족으로 moov 상자 없이 끊긴 녹화를 합격으로 보던 일을 막음.
    /// </summary>
    public class MainPipelineCheckReportTests
    {
        static MainPipelineCheckReport PassingReport() => new MainPipelineCheckReport
        {
            material_count = 52,
            textured_material_count = 51,
            cloth_count = 2,
            valid_cloth_count = 2,
            hair_spring_count = 2,
            thumb_guard_enabled = true,
            surface_correction_enabled = true,
            grounded_samples = 300,
            playback_samples = 300,
            is_video_finalized = true,
            expected_frames = 300,
            recorded_frames = 301,
        };

        [Test]
        public void Given_AllChecksMet_When_Evaluating_Then_Passes()
        {
            var report = PassingReport();

            report.Evaluate();

            Assert.That(report.failures, Is.Empty);
            Assert.That(report.passed, Is.True);
        }

        [Test]
        public void Given_UnfinishedVideoAndMissingThumbGuard_When_Evaluating_Then_ListsEachFailure()
        {
            var report = PassingReport();
            report.is_video_finalized = false;
            report.thumb_guard_enabled = false;
            report.grounded_samples = 100;

            report.Evaluate();

            Assert.That(report.passed, Is.False);
            Assert.That(report.failures, Has.Some.Contains("녹화 영상"));
            Assert.That(report.failures, Has.Some.Contains("엄지"));
            Assert.That(report.failures, Has.Some.Contains("하체 접지"));
        }

        static byte[] Box(string type, int payloadLength)
        {
            int size = 8 + payloadLength;
            var bytes = new byte[size];
            bytes[0] = (byte)(size >> 24); bytes[1] = (byte)(size >> 16); bytes[2] = (byte)(size >> 8); bytes[3] = (byte)size;
            Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
            return bytes;
        }

        static MemoryStream Concat(params byte[][] parts)
        {
            var stream = new MemoryStream();
            foreach (var part in parts)
                stream.Write(part, 0, part.Length);
            stream.Position = 0;
            return stream;
        }

        [Test]
        public void Given_FileWithMoov_When_Checking_Then_IsFinalized()
        {
            using var stream = Concat(Box("ftyp", 16), Box("mdat", 64), Box("moov", 32));

            Assert.That(MainPipelineCheckReport.IsFinalizedMp4(stream), Is.True);
        }

        [Test]
        public void Given_FileCutBeforeMoov_When_Checking_Then_IsNotFinalized()
        {
            // 저장 도중 끊겨 mdat만 있고 moov가 없는 파일
            using var stream = Concat(Box("ftyp", 16), Box("mdat", 64));

            Assert.That(MainPipelineCheckReport.IsFinalizedMp4(stream), Is.False);
        }

        [Test]
        public void Given_MoovTruncated_When_Checking_Then_IsNotFinalized()
        {
            // moov 머리는 쓰였지만 본문이 잘린 파일
            byte[] moov = Box("moov", 32);
            byte[] cut = new byte[16];
            System.Array.Copy(moov, cut, cut.Length);
            using var stream = Concat(Box("ftyp", 16), Box("mdat", 64), cut);

            Assert.That(MainPipelineCheckReport.IsFinalizedMp4(stream), Is.False);
        }
    }
}
