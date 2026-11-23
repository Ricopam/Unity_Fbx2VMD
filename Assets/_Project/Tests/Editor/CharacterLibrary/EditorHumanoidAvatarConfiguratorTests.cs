using System;
using System.IO;
using Fbx2Vmd.CharacterLibrary;
using NUnit.Framework;
using UnityEditor;

namespace Tests.Editor.CharacterLibrary
{
    /// <summary>
    /// 라이브러리 캐릭터 임포트 설정을 검증함.
    /// Read/Write가 꺼지면 의상 클로스가 스커트 정점을 못 읽어 생성되지 않던 회귀 방지.
    /// </summary>
    public class EditorHumanoidAvatarConfiguratorTests
    {
        private const string SourceModel = "Assets/MagicaCloth2/Example (Can be deleted)/UnityChan/SD_Kohaku_chanz/Utc_sum_humanoid.fbx";

        [Test]
        public void Given_UnreadableModel_When_EnsuringAvatar_Then_EnablesReadWriteAndHumanoid()
        {
            if (!File.Exists(SourceModel)) Assert.Ignore("검증용 사람형 FBX가 없습니다.");
            string folderName = "AvatarReadable_" + Guid.NewGuid().ToString("N");
            string folder = "Assets/" + folderName;
            string path = folder + "/model.fbx";
            bool ownsFolder = false;
            try
            {
                Assert.That(AssetDatabase.CreateFolder("Assets", folderName), Is.Not.Empty);
                ownsFolder = true;
                File.Copy(SourceModel, path);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                importer.isReadable = false;
                importer.SaveAndReimport();

                EditorHumanoidAvatarConfigurator.EnsureHumanoidAvatar(path);

                importer = (ModelImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer.isReadable, Is.True);
                Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Human));
            }
            finally
            {
                if (ownsFolder) Assert.That(AssetDatabase.DeleteAsset(folder), Is.True, "검증용 에셋 정리 실패");
            }
        }
    }
}
