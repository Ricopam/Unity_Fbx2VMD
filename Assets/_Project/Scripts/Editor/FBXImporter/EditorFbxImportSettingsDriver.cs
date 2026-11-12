using System;
using System.Collections.Generic;
using System.IO;
using Fbx2Vmd.Profiling;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 제어된 Import_FBX 경로의 에디터 전용 ModelImporter 설정과 본 매핑 적용을 담당하는 드라이버.
    /// 런타임 컨트롤러는 FbxImportEditorHooks 슬롯을 통해 여기로 위임한다.
    /// </summary>
    internal static class EditorFbxImportSettingsDriver
    {
        internal static void ConfigureEditorImportSettingsIfNeeded(string sourcePath, string targetPath)
        {
            using var perfScope = PerfScope.Measure("FBXImportController.ConfigureEditorImportSettingsIfNeeded");
            if (ShouldConfigureEditorImportSettings(sourcePath, targetPath, Application.dataPath))
            {
                ConfigureImportSettings(targetPath);
                return;
            }

            if (TryEnsureHumanoidClipRootPoseContract(targetPath))
            {
                Debug.Log(
                    $"[FBXImport] 기존 Humanoid clip의 루트 회전과 Y 이동을 본 자세에 반영함. 경로={targetPath}");
                return;
            }

            Debug.Log($"[FBXImport] 제어된 Import_FBX 가져오기 설정 유지됨. 경로={targetPath}");
        }

        internal static bool ShouldConfigureEditorImportSettings(string sourcePath, string targetPath, string dataPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return true;
            }

            if (!PathsEqual(sourcePath, targetPath))
            {
                return true;
            }

            string targetRelativePath = ToAssetRelativePath(targetPath, dataPath);
            return !IsControlledImportAssetPath(targetRelativePath);
        }

        internal static bool IsControlledImportAssetPath(string relativePath)
        {
            return !string.IsNullOrEmpty(relativePath)
                && relativePath.Replace("\\", "/").StartsWith($"Assets/Resources/{FBXVmdPipeline.IMPORT_FBX_FOLDER}/", StringComparison.OrdinalIgnoreCase);
        }

        internal static string ToAssetRelativePath(string filePath, string dataPath)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(dataPath))
            {
                return "";
            }

            string standardizedFilePath = filePath.Replace("\\", "/");
            string standardizedDataPath = dataPath.Replace("\\", "/");

            if (!standardizedFilePath.StartsWith(standardizedDataPath, StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            return "Assets" + standardizedFilePath[standardizedDataPath.Length..];
        }

        private static bool PathsEqual(string left, string right)
        {
            try
            {
                return string.Equals(
                    Path.GetFullPath(left),
                    Path.GetFullPath(right),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static bool TryEnsureHumanoidClipRootPoseContract(
            string filePath)
        {
            string relativePath = ToAssetRelativePath(filePath, Application.dataPath);
            UnityEditor.ModelImporter importer =
                UnityEditor.AssetImporter.GetAtPath(relativePath) as UnityEditor.ModelImporter;
            if (importer == null ||
                importer.animationType != UnityEditor.ModelImporterAnimationType.Human)
            {
                return false;
            }

            UnityEditor.ModelImporterClipAnimation[] clipAnimations =
                importer.clipAnimations;
            if (clipAnimations == null || clipAnimations.Length == 0)
            {
                clipAnimations = importer.defaultClipAnimations;
            }

            if (clipAnimations == null ||
                clipAnimations.Length == 0 ||
                HumanoidClipImportPolicy.HasRootPoseContract(clipAnimations))
            {
                return false;
            }

            HumanoidClipImportPolicy.ApplyRootPoseContract(clipAnimations);
            importer.clipAnimations = clipAnimations;
            importer.SaveAndReimport();
            return true;
        }

        private static void ConfigureImportSettings(string filePath)
        {
            string standardizedFilePath = filePath.Replace("\\", "/");
            string standardizedDataPath = Application.dataPath.Replace("\\", "/");

            if (!standardizedFilePath.StartsWith(standardizedDataPath))
            {
                Debug.LogError($"파일 경로가 Assets 폴더 내에 있지 않습니다: {filePath}");
                return;
            }

            string relativePath = "Assets" + standardizedFilePath[standardizedDataPath.Length..];

            Debug.Log($"[1단계] FBX Import 시작: {relativePath}");
            UnityEditor.AssetDatabase.ImportAsset(relativePath, UnityEditor.ImportAssetOptions.ForceUpdate);

            Debug.Log("[2단계] FBX 정보 가져오기");
            UnityEditor.ModelImporter importer = UnityEditor.AssetImporter.GetAtPath(relativePath) as UnityEditor.ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[2단계 실패] ModelImporter를 가져올 수 없습니다: {relativePath}");
                return;
            }

            Debug.Log("[2단계 완료] ModelImporter 정보:");
            Debug.Log($"  - 현재 Animation Type: {importer.animationType}");
            Debug.Log($"  - 현재 Import Animation: {importer.importAnimation}");
            Debug.Log($"  - 현재 Optimize Bones: {importer.optimizeBones}");

            importer = ResetExistingHumanoidDescription(importer, relativePath);
            if (importer == null)
            {
                return;
            }

            Debug.Log("[3단계] Rig 설정 적용 중...");
            importer.importAnimation = true;
            importer.animationCompression = UnityEditor.ModelImporterAnimationCompression.Off;
            importer.animationType = UnityEditor.ModelImporterAnimationType.Human;
            importer.avatarSetup = UnityEditor.ModelImporterAvatarSetup.CreateFromThisModel;
            importer.optimizeBones = false;

            try
            {
                UnityEditor.SerializedObject serializedImporter = new UnityEditor.SerializedObject(importer);
                serializedImporter.Update();

                string[] propertyNames = { "m_OptimizeBones", "optimizeBones" };
                bool found = false;
                foreach (string propertyName in propertyNames)
                {
                    UnityEditor.SerializedProperty property = serializedImporter.FindProperty(propertyName);
                    if (property != null && property.propertyType == UnityEditor.SerializedPropertyType.Boolean)
                    {
                        property.boolValue = false;
                        found = true;
                        Debug.Log($"[Strip Bones Fix] SerializedObject를 통해 '{propertyName}' 비활성화 성공");
                    }
                }

                if (found)
                {
                    serializedImporter.ApplyModifiedProperties();
                }
                else
                {
                    Debug.LogWarning("[Strip Bones Fix] 'optimizeBones' 관련 속성을 찾지 못했습니다.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Strip Bones Fix] 오류: {e.Message}");
            }

            importer.animationWrapMode = WrapMode.ClampForever;
            importer.importBlendShapes = true;
            importer.importVisibility = true;
            importer.importCameras = false;
            importer.importLights = false;

            Debug.Log("[3단계] Bone Mapping 적용 시작");
            string mappingFilePath = Path.Combine(Application.dataPath, "Resources", FBXImportController.BONE_MAPPING_FILE);
            if (File.Exists(mappingFilePath))
            {
                Dictionary<string, string> mapping = ParseBoneMappingFile(mappingFilePath);
                if (mapping.Count > 0)
                {
                    Debug.Log($"[3단계] Bone Mapping 파일 파싱 완료: {mapping.Count}개 매핑");

                    HumanDescription description = importer.humanDescription;
                    List<HumanBone> humanBones = new List<HumanBone>();
                    foreach (KeyValuePair<string, string> pair in mapping)
                    {
                        HumanBone bone = new HumanBone
                        {
                            humanName = HumanoidAvatarBuilder.NormalizeHumanBoneName(pair.Key),
                            boneName = pair.Value
                        };
                        bone.limit.useDefaultValues = true;
                        humanBones.Add(bone);
                    }

                    description.human = humanBones.ToArray();

                    importer.humanDescription = description;
                    Debug.Log($"[3단계] Bone Mapping 적용: {humanBones.Count}개 본");
                }
                else
                {
                    Debug.LogWarning("[3단계] Bone Mapping 데이터가 비어있습니다.");
                }
            }
            else
            {
                Debug.LogWarning($"[3단계] Bone Mapping 파일을 찾을 수 없습니다: {mappingFilePath}");
            }

            Debug.Log("[3단계] Animation Clip 추출 시작");
            if (importer.defaultClipAnimations != null && importer.defaultClipAnimations.Length > 0)
            {
                UnityEditor.ModelImporterClipAnimation[] clipAnimations =
                    importer.defaultClipAnimations;
                HumanoidClipImportPolicy.ApplyRootPoseContract(clipAnimations);
                importer.clipAnimations = clipAnimations;
                Debug.Log($"[3단계] Animation Clip 추출: {clipAnimations.Length}개");

                foreach (UnityEditor.ModelImporterClipAnimation clip in clipAnimations)
                {
                    Debug.Log($"  - Clip: {clip.name} (Start: {clip.firstFrame}, End: {clip.lastFrame})");
                }
            }
            else
            {
                Debug.LogWarning("[3단계] defaultClipAnimations가 비어있습니다. 자동 경로에서는 임의 Take 001을 만들지 않습니다.");
                importer.clipAnimations = Array.Empty<UnityEditor.ModelImporterClipAnimation>();
            }

            Debug.Log("[3단계 완료] 최종 설정:");
            Debug.Log("  - Animation Type: Humanoid");
            Debug.Log($"  - Import Animation: {importer.importAnimation}");
            Debug.Log($"  - Optimize Game Objects (Strip Bones): {importer.optimizeGameObjects}");
            Debug.Log("  - Bone Mapping: 적용 완료");
            Debug.Log($"  - Animation Clips: {(importer.clipAnimations != null ? importer.clipAnimations.Length : 0)}개");

            UnityEditor.AssetDatabase.WriteImportSettingsIfDirty(relativePath);
            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.AssetDatabase.ImportAsset(relativePath, UnityEditor.ImportAssetOptions.ForceUpdate);

            Debug.Log("[3단계] 최종 Reimport 완료");
            Debug.Log("===========================================");
        }

        private static UnityEditor.ModelImporter ResetExistingHumanoidDescription(
            UnityEditor.ModelImporter importer,
            string relativePath)
        {
            HumanDescription description = importer.humanDescription;
            bool hasHumanMapping = description.human != null && description.human.Length > 0;
            bool hasSkeleton = description.skeleton != null && description.skeleton.Length > 0;
            if (!hasHumanMapping && !hasSkeleton)
            {
                return importer;
            }

            // 이전 자동 임포트가 부모 없는 skeleton을 저장했을 수 있으므로
            // FBX 원본 계층에서 description을 다시 생성한 뒤 Human mapping을 적용함.
            Debug.Log("[2단계] 기존 Humanoid description 초기화 중...");
            importer.animationType = UnityEditor.ModelImporterAnimationType.Generic;
            importer.humanDescription = new HumanDescription();
            importer.SaveAndReimport();

            UnityEditor.ModelImporter resetImporter =
                UnityEditor.AssetImporter.GetAtPath(relativePath) as UnityEditor.ModelImporter;
            if (resetImporter == null)
            {
                Debug.LogError($"[2단계 실패] Humanoid description 초기화 후 ModelImporter를 찾을 수 없습니다: {relativePath}");
            }

            return resetImporter;
        }

        private static Dictionary<string, string> ParseBoneMappingFile(string path)
        {
            Dictionary<string, string> mapping = new Dictionary<string, string>();
            string[] lines = File.ReadAllLines(path);
            bool insideBoneTemplate = false;

            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("m_BoneTemplate:"))
                {
                    insideBoneTemplate = true;
                    continue;
                }

                if (insideBoneTemplate)
                {
                    if (trimmedLine.StartsWith("m_"))
                    {
                        break;
                    }

                    int colonIndex = trimmedLine.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        string key = trimmedLine[..colonIndex].Trim();
                        string value = trimmedLine[(colonIndex + 1)..].Trim();
                        if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                        {
                            mapping[key] = value;
                        }
                    }
                }
            }

            return mapping;
        }
    }
}
