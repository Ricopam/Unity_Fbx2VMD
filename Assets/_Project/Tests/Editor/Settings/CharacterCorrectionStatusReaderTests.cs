using Fbx2Vmd.ClothPhysics;
using Fbx2Vmd.FBXImporter;
using Fbx2Vmd.Settings;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor.Settings
{
    public sealed class CharacterCorrectionStatusReaderTests
    {
        private GameObject character;

        [TearDown]
        public void TearDown()
        {
            if (character != null)
            {
                Object.DestroyImmediate(character);
            }
        }

        [Test]
        public void NoCharacter_ReportsNothingApplied()
        {
            var state = new MainRecordingSettingsState();
            CharacterCorrectionStatusReader.ReadCharacter(state, null);
            Assert.That(state.isThumbGuardEnabled, Is.False);
            Assert.That(state.clothCount, Is.Zero);
            Assert.That(state.validClothCount, Is.Zero);
            Assert.That(state.hairSpringCount, Is.Zero);
        }

        [Test]
        public void Character_CountsHairSpringsAndThumbGuard()
        {
            character = new GameObject("character");
            new GameObject("front").AddComponent<SpringHairDynamics>().transform.SetParent(character.transform);
            new GameObject("twintail").AddComponent<SpringHairDynamics>().transform.SetParent(character.transform);
            character.AddComponent<HumanoidThumbDeformationGuard>();
            var state = new MainRecordingSettingsState();

            CharacterCorrectionStatusReader.ReadCharacter(state, character);

            Assert.That(state.hairSpringCount, Is.EqualTo(2));
            Assert.That(state.isThumbGuardEnabled, Is.True);
        }

        // 비활성 엄지 보정은 적용되지 않은 것으로 보여야 함.
        [Test]
        public void DisabledThumbGuard_IsNotReportedAsApplied()
        {
            character = new GameObject("character");
            character.AddComponent<HumanoidThumbDeformationGuard>().enabled = false;
            var state = new MainRecordingSettingsState();

            CharacterCorrectionStatusReader.ReadCharacter(state, character);

            Assert.That(state.isThumbGuardEnabled, Is.False);
        }

        // 생성 목록에 남은 빈 항목은 '생성 실패'로 세어 2/2처럼 보이지 않게 함.
        [Test]
        public void MissingCloth_CountsTowardTotalButNotValid()
        {
            character = new GameObject("character");
            var setup = character.AddComponent<CharacterPhysicsSetup>();
            setup.generatedCloths.Add(null);
            var state = new MainRecordingSettingsState();

            CharacterCorrectionStatusReader.ReadCharacter(state, character);

            Assert.That(state.clothCount, Is.EqualTo(1));
            Assert.That(state.validClothCount, Is.Zero);
        }
    }
}
