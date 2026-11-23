using NUnit.Framework;
using System;
using System.Diagnostics;
using System.IO;
using Fbx2Vmd.CharacterLibrary;
using UnityEngine;

namespace Tests.Editor.CharacterLibrary
{
    /// <summary>
    /// PMX 변환기 대기 정책과 출력 이름을 검증함.
    /// YYB 변환이 120초 한도에 걸리던 문제, "1.0ver" 이름이 잘려 결과를 못 찾던 문제의 회귀 방지.
    /// </summary>
    public class CharacterLibraryPmxConverterWaitTests
    {
        private const double ObservedYybConversionSeconds = 236;

        private static ProcessStartInfo Command(string arguments) => new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        [Test]
        public void Given_ObservedYybDuration_When_ReadingTimeout_Then_LeavesHeadroom()
        {
            Assert.That(FbxCharacterLoader.PmxConversionTimeout.TotalSeconds,
                Is.GreaterThanOrEqualTo(ObservedYybConversionSeconds * 2));
        }

        [Test]
        public void Given_SlowProcess_When_TimeoutPasses_Then_ThrowsTimeout()
        {
            // ping 4회는 약 3초 걸림. cmd 없이 직접 실행해 종료 시 자식 프로세스가 남지 않게 함.
            var start = Command("");
            start.FileName = "ping.exe";
            start.Arguments = "-n 4 127.0.0.1";
            Assert.Throws<TimeoutException>(() => FbxCharacterLoader.RunPmxConverter(
                start, TimeSpan.FromMilliseconds(500)));
        }

        [Test]
        public void Given_InterruptedConversion_When_Cleaning_Then_RemovesPartialOutputs()
        {
            string folder = Path.Combine(Path.GetTempPath(), "UnityFbx2Vmd-PmxPartial", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string outputBase = Path.Combine(folder, "model_1.0ver");
                string[] partials = { ".fbx", ".xml", ".model.bytes", ".extra.bytes" };
                foreach (string suffix in partials) File.WriteAllText(outputBase + suffix, "partial");
                string unrelated = Path.Combine(folder, "keep.png");
                File.WriteAllText(unrelated, "texture");

                FbxCharacterLoader.DeletePartialPmxOutputs(outputBase);

                foreach (string suffix in partials) Assert.That(File.Exists(outputBase + suffix), Is.False, suffix);
                Assert.That(File.Exists(unrelated), Is.True, "변환 산출물 외 파일까지 지움");
            }
            finally
            {
                try { Directory.Delete(folder, true); }
                catch (IOException) { /* 임시 폴더 정리 실패는 판정과 무관함 */ }
            }
        }

        [Test]
        public void Given_FastProcess_When_FinishingInTime_Then_Returns()
        {
            Assert.DoesNotThrow(() => FbxCharacterLoader.RunPmxConverter(
                Command("/c exit 0"), TimeSpan.FromSeconds(30)));
        }

        [Test]
        public void Given_DottedBaseName_When_Converting_Then_KeepsFullFileName()
        {
            string pmx = Path.Combine(Application.dataPath, "Plugins", "VMDRecorderSample", "Models", "TestModel", "test.pmx");
            string converterDir = Path.Combine(Application.dataPath, "MMD4Mecanim", "Editor", "PMX2FBX");
            string exe = Path.Combine(converterDir, "pmx2fbx.exe");
            if (!File.Exists(pmx) || !File.Exists(exe))
            {
                Assert.Ignore("test.pmx 또는 pmx2fbx 변환기가 없습니다.");
            }

            string folder = Path.Combine(Path.GetTempPath(), "UnityFbx2Vmd-PmxName", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                // YYB 원본처럼 이름에 점이 든 경우를 재현함.
                string outputBase = Path.Combine(folder, "model_1.0ver");
                var start = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = FbxCharacterLoader.BuildPmxConverterArguments(pmx, outputBase),
                    WorkingDirectory = converterDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                FbxCharacterLoader.RunPmxConverter(start, TimeSpan.FromSeconds(120));
                Assert.That(File.Exists(outputBase + ".fbx"), Is.True, "점 앞에서 잘린 이름으로 출력됨");
            }
            finally
            {
                try { Directory.Delete(folder, true); }
                catch (IOException) { /* 임시 폴더 정리 실패는 판정과 무관함 */ }
            }
        }

        [Test]
        public void Given_FailingProcess_When_Exiting_Then_ThrowsWithExitCode()
        {
            var error = Assert.Throws<InvalidOperationException>(() => FbxCharacterLoader.RunPmxConverter(
                Command("/c exit 3"), TimeSpan.FromSeconds(30)));
            StringAssert.Contains("exit 3", error.Message);
        }
    }
}
