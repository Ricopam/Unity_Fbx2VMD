using System;
using System.Linq;
using Fbx2Vmd.FBXImporter;
using UnityEngine;

namespace Fbx2Vmd.CharacterLibrary
{
    /// <summary>
    /// 모델 FBX를 Humanoid(CreateFromThisModel)로 설정한다.
    /// 모션 클립 검증이 포함된 EnsureHumanoid와 달리 캐릭터는 클립이 없으므로
    /// 아바타 유효성만 확인하고, 알려진 본 명명 규칙 폴백 매핑은 재사용한다.
    /// </summary>
    internal static class EditorHumanoidAvatarConfigurator
    {
        internal static void EnsureHumanoidAvatar(string assetPath)
        {
            if (!(UnityEditor.AssetImporter.GetAtPath(assetPath) is UnityEditor.ModelImporter importer))
            {
                throw new InvalidOperationException(
                    $"ModelImporter를 찾을 수 없습니다: {assetPath}");
            }

            bool shouldReimport = false;
            if (importer.animationType != UnityEditor.ModelImporterAnimationType.Human)
            {
                importer.animationType = UnityEditor.ModelImporterAnimationType.Human;
                shouldReimport = true;
            }
            if (importer.avatarSetup != UnityEditor.ModelImporterAvatarSetup.CreateFromThisModel)
            {
                importer.avatarSetup = UnityEditor.ModelImporterAvatarSetup.CreateFromThisModel;
                shouldReimport = true;
            }

            HumanDescription description = importer.humanDescription;
            GameObject model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (EditorHumanoidClipImportConfigurator.TryResolveHumanBoneNames(
                    model, description.human, out HumanBone[] resolved) &&
                !ReferenceEquals(description.human, resolved))
            {
                description.human = resolved;
                importer.humanDescription = description;
                shouldReimport = true;
            }
            else if ((description.human == null || description.human.Length == 0) &&
                     EditorHumanoidClipImportConfigurator.TryBuildFallbackHumanDescription(
                         model, description, out HumanDescription fallback))
            {
                // MMD 계열 본 명명 등 자동 매핑이 비는 리그에 프로젝트 명명 규칙 매핑을 적용한다.
                importer.humanDescription = fallback;
                shouldReimport = true;
            }

            // 프로젝트 본 템플릿과 계층이 일치하는 리그에서 소실된 Chest 역할만 보완한다.
            description = importer.humanDescription;
            if (description.human != null && !description.human.Any(bone => bone.humanName == "Chest") &&
                EditorHumanoidClipImportConfigurator.TryCompleteTemplateChestMapping(
                    model, description.human,
                    FBXImportController.LoadBoneMappingRuntime(), out HumanBone[] completed))
            {
                description.human = completed;
                importer.humanDescription = description;
                shouldReimport = true;
            }

            if (shouldReimport)
            {
                importer.SaveAndReimport();
            }
        }
    }
}
