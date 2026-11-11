using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Fbx2Vmd.FBXImporter;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor.Media;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using UnityEngine.TestTools;

namespace Tests.Editor.FBXImporter
{
    public class MotionVideoVp9EncoderTests
    {
        private static string EvidenceRoot => Path.GetFullPath(
            "Docs/Workflow/Local/evidence/vp9-recording-tests");

        [Test]
        public void Given_Vp9Settings_When_InspectingInput_Then_PreservesAlphaAndAudio()
        {
            IEncoderSettings settings = new MotionVideoVp9EncoderSettings();
            Assert.That(settings.CanCaptureAlpha, Is.True);
            Assert.That(settings.CanCaptureAudio, Is.True);
            Assert.That(settings.GetTextureFormat(true), Is.EqualTo(TextureFormat.RGBA32));
            Assert.That(settings.Extension, Is.EqualTo("webm"));
        }

        [Test]
        public void Given_VariableRateOrOddDimensions_When_Validating_Then_RejectsRecording()
        {
            var settings = new MotionVideoVp9EncoderSettings();
            var errors = new List<string>();
            settings.ValidateRecording(new RecordingContext
            {
                width = 63, height = 48, frameRateMode = FrameRatePlayback.Variable
            }, errors, new List<string>());
            Assert.That(errors, Has.Some.Contains("고정 프레임률"));
            Assert.That(errors, Has.Some.Contains("짝수"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Given_TransparentFrames_When_EncodingAndClosingTwice_Then_WritesWebM(bool audio)
        {
            Directory.CreateDirectory(EvidenceRoot);
            string output = Path.Combine(EvidenceRoot, "unity-vp9-" + audio + "-" + Guid.NewGuid().ToString("N") + ".webm");
            var settings = new MotionVideoVp9EncoderSettings();
            var encoder = new MotionVideoVp9Encoder();
            var context = CreateContext(output, audio);
            int channels = (int)typeof(MotionVideoVp9Encoder).GetMethod("GetAudioChannels",
                BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { AudioSettings.speakerMode });
            var pixels = new NativeArray<byte>(64 * 48 * 4, Allocator.Temp);
            var samples = new NativeArray<float>(AudioSettings.outputSampleRate / 30 * channels, Allocator.Temp);
            try
            {
                for (int y = 0; y < 48; y++)
                for (int x = 0; x < 64; x++)
                {
                    int offset = (y * 64 + x) * 4;
                    pixels[offset] = y < 24 ? (byte)255 : (byte)0;
                    pixels[offset + 1] = y >= 24 ? (byte)255 : (byte)0;
                    pixels[offset + 3] = x < 16 ? (byte)0 : x < 32 ? (byte)128 : (byte)255;
                }
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = 0.1f;
                encoder.OpenStream(settings, context);
                try
                {
                    for (int frame = 0; frame < 30; frame++)
                    {
                        encoder.AddVideoFrame(pixels, default);
                        if (audio)
                            encoder.AddAudioFrame(samples);
                    }
                }
                finally
                {
                    encoder.CloseStream();
                }
            }
            finally
            {
                pixels.Dispose();
                samples.Dispose();
            }
            encoder.CloseStream();
            Assert.That(File.Exists(output), Is.True);
            Assert.That(new FileInfo(output).Length, Is.GreaterThan(100));
            Assert.That(Directory.GetDirectories(EvidenceRoot, Path.GetFileName(output) + ".*.partial"), Is.Empty);
            File.WriteAllText(Path.Combine(EvidenceRoot, "latest-" + audio + ".txt"), output);
        }

        [Test]
        public void Given_NoFrames_When_Closing_Then_DoesNotPublishCompletedFile()
        {
            Directory.CreateDirectory(EvidenceRoot);
            string output = Path.Combine(EvidenceRoot, "empty-" + Guid.NewGuid().ToString("N") + ".webm");
            var encoder = new MotionVideoVp9Encoder();
            encoder.OpenStream(new MotionVideoVp9EncoderSettings(), CreateContext(output, false));
            LogAssert.Expect(LogType.Error, new Regex("VP9 녹화 실패: 저장할 녹화 프레임"));
            encoder.CloseStream();
            encoder.CloseStream();
            Assert.That(File.Exists(output), Is.False);
        }

        [Test]
        public void Given_TemporaryCleanupFails_When_Closing_Then_PreservesCompletedFile()
        {
            Directory.CreateDirectory(EvidenceRoot);
            string output = Path.Combine(EvidenceRoot, "cleanup-" + Guid.NewGuid().ToString("N") + ".webm");
            var settings = new MotionVideoVp9EncoderSettings();
            var encoder = new MotionVideoVp9Encoder();
            encoder.OpenStream(settings, CreateContext(output, false));
            using (var pixels = new NativeArray<byte>(64 * 48 * 4, Allocator.Temp))
                encoder.AddVideoFrame(pixels, default);
            string temporary = Directory.GetDirectories(EvidenceRoot, Path.GetFileName(output) + ".*.partial")[0];
            File.WriteAllText(Path.Combine(temporary, "preserve.txt"), "진단용 추가 파일");
            LogAssert.Expect(LogType.Warning, new Regex("VP9 영상 저장은 완료됐으나 임시파일 정리"));
            Assert.DoesNotThrow(() => encoder.CloseStream());
            Assert.That(File.Exists(output), Is.True);
            Assert.That(typeof(MotionVideoVp9EncoderSettings).GetProperty("FailureMessage",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(settings), Is.Null);
        }

        [Test]
        public void Given_AudioDisposeFails_When_Closing_Then_ReleasesVideoWithoutThrowing()
        {
            Directory.CreateDirectory(EvidenceRoot);
            string output = Path.Combine(EvidenceRoot, "dispose-" + Guid.NewGuid().ToString("N") + ".webm");
            var encoder = new MotionVideoVp9Encoder();
            encoder.OpenStream(new MotionVideoVp9EncoderSettings(), CreateContext(output, false));
            typeof(MotionVideoVp9Encoder).GetField("_audio", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(encoder, new FailingDisposeStream(output + ".f32"));
            LogAssert.Expect(LogType.Error, new Regex("VP9 녹화 실패: 테스트용 오디오 종료 실패"));
            Assert.DoesNotThrow(() => encoder.CloseStream());
            Assert.That(typeof(MotionVideoVp9Encoder).GetField("_video", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(encoder), Is.Null);
            Assert.That(File.Exists(output), Is.False);
        }

        private sealed class FailingDisposeStream : FileStream
        {
            internal FailingDisposeStream(string path) : base(path, FileMode.CreateNew) { }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing)
                    throw new IOException("테스트용 오디오 종료 실패");
            }
        }

        [Test]
        public void Given_BadFrame_When_Closing_Then_DoesNotPublishCompletedFile()
        {
            Directory.CreateDirectory(EvidenceRoot);
            string output = Path.Combine(EvidenceRoot, "bad-frame-" + Guid.NewGuid().ToString("N") + ".webm");
            var encoder = new MotionVideoVp9Encoder();
            encoder.OpenStream(new MotionVideoVp9EncoderSettings(), CreateContext(output, false));
            using (var pixels = new NativeArray<byte>(4, Allocator.Temp))
            {
                LogAssert.Expect(LogType.Error, new Regex("VP9 녹화 실패: VP9 입력 프레임 크기"));
                encoder.AddVideoFrame(pixels, default);
            }
            encoder.CloseStream();
            Assert.That(File.Exists(output), Is.False);
        }

        [UnityTest]
        public IEnumerator Given_RenderTexture_When_RecordingWithMovieRecorder_Then_WritesVp9()
        {
            yield return new EnterPlayMode();
            Directory.CreateDirectory(EvidenceRoot);
            string output = Path.Combine(EvidenceRoot, "recorder-" + Guid.NewGuid().ToString("N"));
            var renderTexture = new RenderTexture(64, 48, 0, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(64, 48, TextureFormat.RGBA32, false);
            var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            RecorderController controller = null;
            try
            {
                for (int y = 0; y < 48; y++)
                for (int x = 0; x < 64; x++)
                    texture.SetPixel(x, y, new Color(y >= 24 ? 1 : 0, y < 24 ? 1 : 0, 0,
                        x < 16 ? 0 : x < 32 ? 0.5f : 1));
                texture.Apply();
                Graphics.Blit(texture, renderTexture);
                movie.EncoderSettings = new MotionVideoVp9EncoderSettings();
                movie.ImageInputSettings = new RenderTextureInputSettings { RenderTexture = renderTexture };
                movie.CaptureAlpha = true;
                movie.CaptureAudio = false;
                movie.OutputFile = output;
                settings.AddRecorderSettings(movie);
                settings.FrameRate = 30;
                settings.SetRecordModeToFrameInterval(0, 5);
                controller = new RecorderController(settings);
                controller.PrepareRecording();
                Assert.That(controller.StartRecording(), Is.True);
                for (int frame = 0; frame < 180 && controller.IsRecording(); frame++)
                    yield return null;
                Assert.That(controller.IsRecording(), Is.False, "Recorder 자동 종료가 필요합니다.");
                Assert.That(File.Exists(output + ".webm"), Is.True);
                File.WriteAllText(Path.Combine(EvidenceRoot, "latest-recorder.txt"), output + ".webm");
            }
            finally
            {
                controller?.StopRecording();
                UnityEngine.Object.DestroyImmediate(movie);
                UnityEngine.Object.DestroyImmediate(settings);
                UnityEngine.Object.DestroyImmediate(texture);
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
            yield return new ExitPlayMode();
        }

        [Test]
        public void Given_ExistingOutput_When_Opening_Then_PreservesOriginalFile()
        {
            Directory.CreateDirectory(EvidenceRoot);
            string output = Path.Combine(EvidenceRoot, "existing-" + Guid.NewGuid().ToString("N") + ".webm");
            File.WriteAllText(output, "preserve");
            var encoder = new MotionVideoVp9Encoder();
            Assert.Throws<IOException>(() => encoder.OpenStream(new MotionVideoVp9EncoderSettings(), CreateContext(output, false)));
            Assert.That(File.ReadAllText(output), Is.EqualTo("preserve"));
        }

        private static RecordingContext CreateContext(string output, bool audio) => new RecordingContext
        {
            width = 64, height = 48, fps = new MediaRational(30),
            path = output, doCaptureAlpha = true, doCaptureAudio = audio,
            frameRateMode = FrameRatePlayback.Constant
        };
    }
}
