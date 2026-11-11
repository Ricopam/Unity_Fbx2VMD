using System;
using System.Globalization;
using System.IO;
using Unity.Collections;
using UnityEditor.Media;
using UnityEditor.Recorder.Encoder;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public sealed class MotionVideoVp9Encoder : IEncoder
    {
        private MotionVideoVp9EncoderSettings _settings;
        private MotionVideoEncoderProcess _video;
        private FileStream _audio;
        private string _executable;
        private string _directory;
        private string _output;
        private byte[] _frame;
        private int _frameCount;
        private int _channels;
        private int _sampleRate;
        private bool _closed;

        public void OpenStream(IEncoderSettings settings, RecordingContext context)
        {
            _settings = (MotionVideoVp9EncoderSettings)settings;
            _closed = false;
            _frameCount = 0;
            try
            {
                if (!MotionVideoVp9EncoderSettings.TryFindExecutable(out _executable))
                    throw new FileNotFoundException("VP9 녹화용 FFmpeg를 찾을 수 없습니다.");
                _output = Path.GetFullPath(context.path);
                if (File.Exists(_output))
                    throw new IOException("같은 이름의 녹화 파일이 이미 있습니다: " + _output);
                _directory = _output + "." + Guid.NewGuid().ToString("N") + ".partial";
                Directory.CreateDirectory(_directory);
                _frame = new byte[checked(context.width * context.height * 4)];
                _video = new MotionVideoEncoderProcess(_executable, BuildArguments(context,
                    Path.Combine(_directory, "video.webm")), true);
                if (context.doCaptureAudio)
                {
                    _channels = GetAudioChannels(AudioSettings.speakerMode);
                    _sampleRate = AudioSettings.outputSampleRate;
                    _audio = new FileStream(Path.Combine(_directory, "audio.f32"), FileMode.CreateNew, FileAccess.Write);
                }
            }
            catch
            {
                ReleaseStreams();
                throw;
            }
        }

        internal static string BuildArguments(RecordingContext context, string output)
        {
            string rate = context.fps.numerator.ToString(CultureInfo.InvariantCulture) + "/" +
                context.fps.denominator.ToString(CultureInfo.InvariantCulture);
            return "-f rawvideo -pixel_format rgba -video_size " + context.width + "x" + context.height +
                " -framerate " + rate + " -i pipe:0 -an -c:v libvpx-vp9 -crf 18 -b:v 0" +
                " -cpu-used 4 -row-mt 1 -tile-columns 2 -threads 8 -pix_fmt yuva420p" +
                " -vf scale=out_color_matrix=bt709:out_range=tv -colorspace bt709 -color_primaries bt709" +
                " -color_trc bt709 -color_range tv " + MotionVideoEncoderProcess.QuotePath(output);
        }

        public void AddVideoFrame(NativeArray<byte> bytes, MediaTime time)
        {
            if (_closed || !string.IsNullOrEmpty(_settings.FailureMessage))
                return;
            try
            {
                if (bytes.Length != _frame.Length)
                    throw new ArgumentException("VP9 입력 프레임 크기가 녹화 해상도와 다릅니다.");
                bytes.CopyTo(_frame);
                // 처리 속도만큼 대기해 4K 프레임이 무제한으로 쌓이지 않도록 함.
                _video.Write(_frame);
                _frameCount++;
            }
            catch (Exception exception)
            {
                _settings.ReportFailure(exception);
            }
        }

        public void AddAudioFrame(NativeArray<float> bytes)
        {
            if (_closed || _audio == null || !string.IsNullOrEmpty(_settings.FailureMessage))
                return;
            try
            {
                var samples = bytes.ToArray();
                var buffer = new byte[checked(samples.Length * sizeof(float))];
                Buffer.BlockCopy(samples, 0, buffer, 0, buffer.Length);
                _audio.Write(buffer, 0, buffer.Length);
            }
            catch (Exception exception)
            {
                _settings.ReportFailure(exception);
            }
        }

        public void CloseStream()
        {
            if (_closed)
                return;
            _closed = true;
            try
            {
                bool hasAudio = _audio != null && _audio.Length > 0;
                _audio?.Dispose();
                _audio = null;
                if (!string.IsNullOrEmpty(_settings.FailureMessage))
                    return;
                if (_frameCount == 0)
                    throw new InvalidOperationException("저장할 녹화 프레임이 없습니다.");
                _video.Finish();
                string videoPath = Path.Combine(_directory, "video.webm");
                if (hasAudio)
                {
                    string merged = Path.Combine(_directory, "complete.webm");
                    string arguments = "-i " + MotionVideoEncoderProcess.QuotePath(videoPath) +
                        " -f f32le -ar " + _sampleRate + " -ac " + _channels + " -i " +
                        MotionVideoEncoderProcess.QuotePath(Path.Combine(_directory, "audio.f32")) +
                        " -map 0:v:0 -map 1:a:0 -c:v copy -c:a libopus -b:a 192k " +
                        MotionVideoEncoderProcess.QuotePath(merged);
                    using (var mux = new MotionVideoEncoderProcess(_executable, arguments, false))
                        mux.Finish();
                    File.Move(merged, _output);
                }
                else
                {
                    File.Move(videoPath, _output);
                }
                CleanCompletedTemporaryFiles();
            }
            catch (Exception exception)
            {
                // Recorder 자체 종료 처리를 보장하고 실패 산출물은 진단 경로에 남김.
                _settings.ReportFailure(new IOException(exception.Message + " 임시 경로: " + _directory, exception));
            }
            finally
            {
                ReleaseStreams();
                _frame = null;
            }
        }

        private void ReleaseStreams()
        {
            try
            {
                _audio?.Dispose();
            }
            catch (Exception exception)
            {
                _settings?.ReportFailure(exception);
            }
            finally
            {
                _audio = null;
            }
            try
            {
                _video?.Dispose();
            }
            catch (Exception exception)
            {
                _settings?.ReportFailure(exception);
            }
            finally
            {
                _video = null;
            }
        }

        private void CleanCompletedTemporaryFiles()
        {
            try
            {
                foreach (string name in new[] { "video.webm", "audio.f32" })
                {
                    string path = Path.Combine(_directory, name);
                    if (File.Exists(path))
                        File.Delete(path);
                }
                Directory.Delete(_directory);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("VP9 영상 저장은 완료됐으나 임시파일 정리에 실패했습니다: " + _directory + " / " + exception.Message);
            }
        }

        private static int GetAudioChannels(AudioSpeakerMode mode)
        {
            // Recorder AudioInput이 생성하는 실제 버퍼 채널 수와 일치시킴.
            switch (mode)
            {
                case AudioSpeakerMode.Stereo:
                case AudioSpeakerMode.Prologic: return 2;
                case AudioSpeakerMode.Quad: return 4;
                case AudioSpeakerMode.Surround: return 5;
                case AudioSpeakerMode.Mode5point1: return 6;
                case AudioSpeakerMode.Mode7point1: return 7;
                default: return 1;
            }
        }
    }
}
