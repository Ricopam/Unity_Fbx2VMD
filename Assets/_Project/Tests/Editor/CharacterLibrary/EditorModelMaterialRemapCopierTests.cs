using System.IO;
using System.Linq;
using Fbx2Vmd.CharacterLibrary;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.Editor.CharacterLibrary
{
    /// <summary>
    /// 라이브러리 관리 폴더의 FBX 사본이 원본의 재질 연결을 이어받는지 검증.
    /// 연결이 없으면 라이브러리 캐릭터가 텍스처 없는 흰색으로 보이던 회귀 방지.
    /// </summary>
    public class EditorModelMaterialRemapCopierTests
    {
        const string SourceAssetPath = "Assets/_Project/Model/YYB Hatsune Miku_default/YYB Hatsune Miku_default_1.0ver.fbx";
        const string TestFolder = "Assets/ImportedCharacters/_remap_copier_test";

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TestFolder);
        }

        [Test]
        public void Given_ProjectFbxCopy_When_CopyingRemaps_Then_CopyUsesSourceTexturedMaterials()
        {
            var source = AssetImporter.GetAtPath(SourceAssetPath) as ModelImporter;
            if (source == null || source.GetExternalObjectMap().Count == 0)
                Assert.Ignore($"재질 연결이 있는 로컬 원본 FBX가 없습니다: {SourceAssetPath}");

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            Directory.CreateDirectory(Path.Combine(projectRoot, TestFolder));
            string copyAssetPath = TestFolder + "/model.fbx";
            File.Copy(Path.Combine(projectRoot, SourceAssetPath), Path.Combine(projectRoot, copyAssetPath), true);
            AssetDatabase.ImportAsset(copyAssetPath, ImportAssetOptions.ForceUpdate);

            EditorModelMaterialRemapCopier.CopyFromSource(Path.Combine(projectRoot, SourceAssetPath), copyAssetPath);

            var copy = (ModelImporter)AssetImporter.GetAtPath(copyAssetPath);
            var expected = source.GetExternalObjectMap();
            var actual = copy.GetExternalObjectMap();
            Assert.That(actual.Count, Is.EqualTo(expected.Count));
            Assert.That(expected.All(pair => actual.TryGetValue(pair.Key, out var value) && value == pair.Value), Is.True);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(copyAssetPath);
            Assert.That(model.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .Any(m => m != null && m.HasProperty("_MainTex") && m.mainTexture != null), Is.True,
                "사본 모델의 재질에 텍스처가 있어야 함");
        }

        [Test]
        public void Given_SourceOutsideProject_When_CopyingRemaps_Then_DoesNothing()
        {
            Assert.DoesNotThrow(() => EditorModelMaterialRemapCopier.CopyFromSource(
                Path.Combine(Path.GetTempPath(), "outside.fbx"), TestFolder + "/missing.fbx"));
        }
    }
}
