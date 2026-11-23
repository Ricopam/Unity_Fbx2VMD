using Fbx2Vmd.ClothPhysics;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Tests.Editor.ClothPhysics
{
    /// <summary>
    /// 라이브러리 캐릭터 교체 시 씬 물리 설정 복제 규칙을 검증함.
    /// 설정값은 복사하고, 원본 모델의 본·생성물 참조는 새 모델로 넘기지 않아야 함.
    /// </summary>
    public class LibraryCharacterPhysicsBinderTests
    {
        [Test]
        public void Given_TemplateSetup_When_Copying_Then_CopiesSettingsButNotModelReferences()
        {
            var sourceHost = new GameObject("Source");
            var targetHost = new GameObject("Target");
            var hairRoot = new GameObject("HairRoot");
            try
            {
                var source = sourceHost.AddComponent<CharacterPhysicsSetup>();
                source.colliderRadiusScale = 1.3f;
                source.includeLegs = false;
                source.vrmConversionMode = CharacterPhysicsSetup.VrmConversionMode.ConvertToMc2;
                source.targetAnimator = sourceHost.AddComponent<Animator>();
                source.explicitHairRoots.Add(hairRoot.transform);
                source.tuning.characterPresetKey = "source-key";
                var target = targetHost.AddComponent<CharacterPhysicsSetup>();

                LibraryCharacterPhysicsBinder.CopyConfiguration(source, target);

                Assert.That(target.colliderRadiusScale, Is.EqualTo(1.3f));
                Assert.That(target.includeLegs, Is.False);
                Assert.That(target.vrmConversionMode, Is.EqualTo(CharacterPhysicsSetup.VrmConversionMode.ConvertToMc2));
                Assert.That(target.tuning.characterPresetKey, Is.EqualTo("source-key"));
                Assert.That(target.targetAnimator, Is.Null, "원본 Animator가 새 모델로 넘어감");
                Assert.That(target.explicitHairRoots, Is.Empty, "원본 머리카락 본이 새 모델로 넘어감");
                Assert.That(target.generatedCloths, Is.Not.SameAs(source.generatedCloths));
            }
            finally
            {
                Object.DestroyImmediate(sourceHost);
                Object.DestroyImmediate(targetHost);
                Object.DestroyImmediate(hairRoot);
            }
        }

        [Test]
        public void Given_RootWithoutAnimator_When_Binding_Then_AddsNothing()
        {
            var root = new GameObject("No Animator");
            try
            {
                Assert.That(LibraryCharacterPhysicsBinder.Bind(null, root), Is.Null);
                Assert.That(root.GetComponent<CharacterPhysicsSetup>(), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Given_RootWithExistingSetup_When_Binding_Then_KeepsExisting()
        {
            var root = new GameObject("Has Setup");
            try
            {
                root.AddComponent<Animator>();
                var existing = root.AddComponent<CharacterPhysicsSetup>();
                Assert.That(LibraryCharacterPhysicsBinder.Bind(null, root), Is.Null);
                Assert.That(root.GetComponents<CharacterPhysicsSetup>(), Has.Length.EqualTo(1));
                Assert.That(root.GetComponent<CharacterPhysicsSetup>(), Is.SameAs(existing));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
