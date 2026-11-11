using NUnit.Framework;
using System;
using System.IO;
using UnityEngine;

namespace Tests.Editor.CharacterLibrary
{
    /// <summary>
    /// 벤더된 pmx2fbx 변환기가 실제 PMX를 FBX로 변환하는지 검증한다.
    /// 프로젝트 표준 test.pmx(모든 회귀의 기준 모델)를 사용한다.
    /// </summary>
    public class CharacterLibraryPmxConversionTests
    {
        private static string ProjectPmxPath => Path.GetFullPath(Path.Combine(
            Application.dataPath, "Plugins", "VMDRecorderSample",
            "Models", "TestModel", "test.pmx"));

        private static string ConverterExe => Path.GetFullPath(Path.Combine(
            Application.dataPath, "MMD4Mecanim", "Editor", "PMX2FBX",
            "pmx2fbx.exe"));

        [Test]
        public void Given_TestPmx_When_Converting_Then_ProducesValidFbx()
        {
            if (!File.Exists(ProjectPmxPath))
            {
                Assert.Ignore($"변환용 PMX가 없습니다: {ProjectPmxPath}");
            }
            if (!File.Exists(ConverterExe))
            {
                Assert.Ignore($"pmx2fbx 변환기가 없습니다: {ConverterExe}");
            }

            string folder = Path.Combine(
                Path.GetTempPath(),
                "UnityFbx2Vmd-PmxConversion",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string outputBase = Path.Combine(folder, "test");
                var start = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = ConverterExe,
                    Arguments = $"-i \"{ProjectPmxPath}\" -o \"{outputBase}\"",
                    WorkingDirectory = Path.GetDirectoryName(ConverterExe),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                int exitCode;
                using (var process = System.Diagnostics.Process.Start(start))
                {
                    // 출력 버퍼를 비동기로 비우지 않으면 변환기가 블록된다.
                    var stdoutReader = process.StandardOutput.ReadToEndAsync();
                    var stderrReader = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(120000))
                    {
                        try { process.Kill(); } catch { }
                        Assert.Fail("pmx2fbx 변환이 시간 안에 끝나지 않았습니다.");
                    }
                    exitCode = process.ExitCode;
                }

                Assert.That(exitCode, Is.EqualTo(0));
                string fbxPath = outputBase + ".fbx";
                Assert.That(File.Exists(fbxPath), Is.True,
                    $"FBX 출력이 없습니다: {fbxPath}");

                // pmx2fbx는 ASCII FBX를 출력한다. 헤더 주석으로 실제 포맷을 확인한다.
                byte[] magic = new byte[64];
                int read;
                using (var stream = File.OpenRead(fbxPath))
                {
                    read = stream.Read(magic, 0, magic.Length);
                }
                string header = System.Text.Encoding.ASCII.GetString(magic, 0, read);
                Assert.That(header, Does.StartWith("; FBX 7."),
                    "출력이 FBX 포맷이 아닙니다: " + header.Substring(0, read));
                Assert.That(new FileInfo(fbxPath).Length, Is.GreaterThan(1000));
            }
            finally
            {
                try
                {
                    Directory.Delete(folder, true);
                }
                catch (Exception)
                {
                    // 임시 폴더 정리 실패는 판정에 영향을 주지 않는다.
                }
            }
        }
    }
}
