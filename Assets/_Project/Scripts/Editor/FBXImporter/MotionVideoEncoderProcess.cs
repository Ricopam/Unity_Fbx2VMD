#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed class MotionVideoEncoderProcess : IDisposable
    {
        private readonly Process _process;
        private readonly StringBuilder _errors = new StringBuilder();
        private const int TimeoutMilliseconds = 60000;

        internal MotionVideoEncoderProcess(string executable, string arguments, bool input)
        {
            _process = new Process
            {
                StartInfo = new ProcessStartInfo(executable, "-hide_banner -loglevel error -nostdin -n " + arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = input,
                    RedirectStandardError = true
                }
            };
            _process.ErrorDataReceived += (_, args) =>
            {
                if (args.Data == null)
                    return;
                lock (_errors)
                {
                    if (_errors.Length < 8192)
                        _errors.AppendLine(args.Data);
                }
            };
            try
            {
                if (!_process.Start())
                    throw new IOException("FFmpeg를 시작하지 못했습니다.");
                _process.BeginErrorReadLine();
            }
            catch
            {
                _process.Dispose();
                throw;
            }
        }

        internal void Write(byte[] bytes)
        {
            if (_process.HasExited)
                throw new IOException("FFmpeg가 조기 종료되었습니다. " + ReadErrors());
            var writing = _process.StandardInput.BaseStream.WriteAsync(bytes, 0, bytes.Length);
            if (!writing.Wait(TimeoutMilliseconds))
            {
                _process.Kill();
                _process.WaitForExit(5000);
                // 종료로 발생한 파이프 오류를 관찰하고 미완료 쓰기가 버퍼를 참조하는 동안 재사용하지 않음.
                writing.ContinueWith(failed => { _ = failed.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                Task.WhenAny(writing, Task.Delay(5000)).GetAwaiter().GetResult();
                throw new TimeoutException("FFmpeg 프레임 전달 시간이 초과되었습니다.");
            }
            writing.GetAwaiter().GetResult();
        }

        internal void Finish()
        {
            if (_process.StartInfo.RedirectStandardInput)
                _process.StandardInput.Close();
            if (!_process.WaitForExit(TimeoutMilliseconds))
                throw new TimeoutException("FFmpeg 저장 시간이 초과되었습니다.");
            _process.WaitForExit();
            if (_process.ExitCode != 0)
                throw new IOException("FFmpeg 종료 코드 " + _process.ExitCode + ": " + ReadErrors());
        }

        private string ReadErrors()
        {
            lock (_errors)
                return _errors.ToString();
        }

        public void Dispose()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(5000);
                }
            }
            finally
            {
                _process.Dispose();
            }
        }

        internal static string QuotePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0)
                throw new ArgumentException("잘못된 녹화 파일 경로입니다.", nameof(path));
            return "\"" + path + "\"";
        }
    }
}
#endif
