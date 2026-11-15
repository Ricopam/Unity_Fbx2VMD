using Fbx2Vmd.CharacterLibrary;
using Fbx2Vmd.FBXImporter;
using NUnit.Framework;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Tests.Editor.CharacterLibrary
{
    /// <summary>
    /// 캐릭터 라이브러리 바인딩의 실패 보존 계약을 검증한다.
    /// 준비 실패 시 기존 대상이 유지돼야 하고, 커밋 성공 시 복원 경로가 동작해야 한다.
    /// </summary>
    public class CharacterLibrarySceneBindingTests
    {
        private static string RealVrmPath => Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "Docs", "ref", "ExportedProject",
            "ExportedProject", "Assets", "StreamingAssets", "Characters",
            "HatsuneMikuNT.vrm"));

        [Test]
        public void Given_SceneCharacter_When_CommitAndRestore_Then_SwapsAndReturnsPrevious()
        {
            var pipelineHost = new GameObject("Pipeline");
            var sceneChar = new GameObject("Scene Character");
            var libRoot = new GameObject("Loaded Root");
            try
            {
                var pipeline = pipelineHost.AddComponent<FBXVmdPipeline>();
                pipeline.targetCharacter = sceneChar;

                // 커밋: 타겟 교체 + 이전 캐릭터 비활성 보존
                CharacterLibrarySceneBinding.Commit(pipeline, "entry-1", libRoot);
                Assert.That(pipeline.targetCharacter, Is.SameAs(libRoot));
                Assert.That(sceneChar.activeSelf, Is.False);
                Assert.That(libRoot.activeSelf, Is.True);
                var marker = libRoot.GetComponent<CharacterLibraryInstance>();
                Assert.That(marker.previousSceneTarget, Is.SameAs(sceneChar));

                // 복원: 이전 캐릭터 재활성 + 라이브러리 루트 제거
                bool restored = CharacterLibrarySceneBinding.Restore(
                    pipeline, out bool removed);
                Assert.That(restored, Is.True);
                Assert.That(removed, Is.True);
                Assert.That(pipeline.targetCharacter, Is.SameAs(sceneChar));
                Assert.That(sceneChar.activeSelf, Is.True);
                Assert.That(libRoot == null, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(pipelineHost);
                Object.DestroyImmediate(sceneChar);
                if (libRoot != null)
                {
                    Object.DestroyImmediate(libRoot);
                }
            }
        }

        [Test]
        public async Task Given_LoadFails_When_SelectingCharacter_Then_PreviousTargetStaysActive()
        {
            string vrmPath = RealVrmPath;
            if (!File.Exists(vrmPath))
            {
                Assert.Ignore($"E2E용 VRM이 없습니다: {vrmPath}");
            }

            string folder = Path.Combine(
                Path.GetTempPath(),
                "UnityFbx2Vmd-SceneBindingTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string libraryFile = Path.Combine(folder, "characters.json");
            string workingVrm = Path.Combine(folder, "working.vrm");
            File.Copy(vrmPath, workingVrm);

            var pipelineHost = new GameObject("Pipeline");
            var sceneChar = new GameObject("Scene Character");
            GameObject managerHost = null;
            try
            {
                var registry = new CharacterLibraryRegistry(libraryFile);
                CharacterLibraryRegistry.RegisterResult registered =
                    registry.Register(workingVrm);
                Assert.That(registered.success, Is.True, registered.error);

                var pipeline = pipelineHost.AddComponent<FBXVmdPipeline>();
                pipeline.targetCharacter = sceneChar;

                // EnsureExists는 열린 씬의 배치 매니저를 채택할 수 있으므로
                // 전용 인스턴스를 만들고 의존성은 모두 주입한다.
                // 편집 모드에선 Awake가 돌지 않아 정적 _current를 오염시키지 않는다.
                managerHost = new GameObject("TestManager");
                var manager = managerHost.AddComponent<CharacterLibraryCoordinator>();
                var managerType = typeof(CharacterLibraryCoordinator);
                managerType
                    .GetField("_registry", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(manager, registry);
                // _pipeline 미주입 시 EnsurePipeline이 씬의 실제 파이프라인을 찾아
                // 검증 대상이 아닌 오브젝트를 건드린다.
                managerType
                    .GetField("_pipeline", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(manager, pipeline);

                // 등록 후 소스를 손상시켜 실제 로더의 실패 경로를 지나게 한다.
                File.WriteAllText(workingVrm, "corrupt");

                bool ok = await manager.SelectCharacterAsync(registered.entry.id);
                Assert.That(ok, Is.False);
                Assert.That(manager.LastError, Is.Not.Empty);

                // 준비 실패 시 기존 대상 유지: 참조도 활성 상태도 그대로.
                Assert.That(pipeline.targetCharacter, Is.SameAs(sceneChar));
                Assert.That(sceneChar.activeSelf, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(pipelineHost);
                Object.DestroyImmediate(sceneChar);
                if (managerHost != null)
                {
                    Object.DestroyImmediate(managerHost);
                }
                try
                {
                    Directory.Delete(folder, true);
                }
                catch (Exception)
                {
                    // 임시 폴더 정리 실패는 테스트 판정에 영향을 주지 않는다.
                }
            }
        }
    }
}
