using System.IO;
using NUnit.Framework;

namespace Tests.Editor.CharacterLibrary
{
    /// <summary>
    /// 최종 기준 Main 씬은 빈 무대에서 캐릭터 불러오기부터 시작해야 함.
    /// 씬에 캐릭터를 미리 두거나 Play 시 마지막 캐릭터를 자동으로 불러오면 실제 사용 흐름 검증이 흐려짐.
    /// </summary>
    public class MainSceneStartStateTests
    {
        const string MainScenePath = "Assets/_Project/Scene/Main.unity";

        [Test]
        public void Given_MainScene_When_Inspecting_Then_HasNoPlacedCharacterAndNoAutoLoad()
        {
            Assert.That(File.Exists(MainScenePath), Is.True);
            string yaml = File.ReadAllText(MainScenePath);

            // Animator(클래스 ID 95)가 있으면 씬에 캐릭터가 미리 배치된 것임.
            Assert.That(yaml, Does.Not.Contain("--- !u!95 &"), "Main 씬에 캐릭터(Animator)가 배치되면 안 됨");
            // 프리팹으로 배치한 캐릭터는 Animator가 프리팹 안에 있으므로 프리팹 인스턴스(클래스 ID 1001)도 막음.
            Assert.That(yaml, Does.Not.Contain("--- !u!1001 &"), "Main 씬에 캐릭터 프리팹이 배치되면 안 됨");
            Assert.That(yaml, Does.Contain("_autoLoadLastCharacter: 0"), "Main 씬은 Play 시 마지막 캐릭터를 자동으로 불러오지 않아야 함");
        }
    }
}
