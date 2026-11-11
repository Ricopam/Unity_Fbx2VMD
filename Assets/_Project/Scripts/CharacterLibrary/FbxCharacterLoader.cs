using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Fbx2Vmd.FBXImporter;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Fbx2Vmd.CharacterLibrary
{
    /// <summary>
    /// 사람형 FBX 캐릭터 로더. VRM과 달리 Unity 런타임 임포트가 없으므로
    /// 소스를 프로젝트 내 관리 폴더로 복사해 ModelImporter(Humanoid)로 가져온다.
    /// 에디터 전용 경로다 — 플레이어 빌드에서는 즉시 실패를 돌려준다.
    /// </summary>
    internal static class FbxCharacterLoader
    {
        internal const string IMPORTED_CHARACTERS_FOLDER = "ImportedCharacters";

        /// <summary>
        /// FBX/PMX를 관리 폴더로 복사(PMX는 FBX 변환)하고 Humanoid 임포트한 뒤
        /// 비활성 인스턴스를 돌려준다. 임포트된 에셋 사본은 재로드 재사용과
        /// 프로젝트 내 보관을 위해 유지한다.
        /// </summary>
        public static CharacterLoader.Result Load(string sourcePath, string entryId)
        {
            var result = new CharacterLoader.Result();
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                result.error = $"캐릭터 파일이 없습니다: {sourcePath}";
                return result;
            }
#if UNITY_EDITOR
            GameObject root = null;
            try
            {
                string assetPath = ImportToControlledFolder(sourcePath, entryId);
                GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (prefab == null)
                {
                    result.error = $"FBX 프리팹을 읽을 수 없습니다: {assetPath}";
                    return result;
                }

                root = Object.Instantiate(prefab);
                // 커밋 전까지 화면에 보이지 않게 한다.
                root.SetActive(false);

                Animator animator = root.GetComponent<Animator>();
                if (animator == null || animator.avatar == null ||
                    !animator.avatar.isValid || !animator.avatar.isHuman)
                {
                    result.error = "유효한 Humanoid Avatar가 없어 리타게팅 대상으로 사용할 수 없습니다.";
                    DestroyRoot(root);
                    return result;
                }

                result.loadedRoot = root;
                return result;
            }
            catch (Exception error)
            {
                result.error = $"캐릭터 로드 실패: {error.Message}";
                try { DestroyRoot(root); }
                catch (Exception destroyError) { Debug.LogException(destroyError); }
                return result;
            }
#else
            result.error = "FBX 캐릭터 로드는 Unity 에디터에서만 지원됩니다.";
            return result;
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// 소스 FBX를 `Assets/ImportedCharacters/<entryId>/`에 복사하고
        /// Humanoid로 임포트한 뒤 asset 경로를 돌려준다.
        /// Resources 하위가 아니므로 플레이어 빌드 번들에 포함되지 않는다.
        /// </summary>
        private static string ImportToControlledFolder(string sourcePath, string entryId)
        {
            // entryId는 public API 입력이므로 경로 이탈 문자를 차단한다.
            string folderName = string.IsNullOrEmpty(entryId)
                ? "_unassigned"
                : Path.GetFileName(entryId);
            string targetDir = Path.Combine(
                Application.dataPath, IMPORTED_CHARACTERS_FOLDER, folderName);
            Directory.CreateDirectory(targetDir);

            string safeFileName = FBXImportController.SanitizeFileName(Path.GetFileName(sourcePath));
            string targetPath;
            if (string.Equals(
                    Path.GetExtension(sourcePath), ".pmx", StringComparison.OrdinalIgnoreCase))
            {
                // PMX는 Unity가 직접 임포트하지 못하므로 벤더된 pmx2fbx로
                // 관리 폴더에 FBX를 생성하고 그 결과물을 임포트한다.
                string baseName = Path.GetFileNameWithoutExtension(safeFileName);
                if (string.IsNullOrEmpty(baseName))
                {
                    throw new InvalidOperationException("PMX 파일 이름이 비어 있습니다.");
                }
                ConvertPmxToFbx(sourcePath, Path.Combine(targetDir, baseName));
                CopyPmxTextures(sourcePath, targetDir);
                targetPath = Path.Combine(targetDir, baseName + ".fbx");
            }
            else
            {
                targetPath = Path.Combine(targetDir, safeFileName);
                if (!string.Equals(
                        Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(sourcePath, targetPath, true);
                }
            }

            if (!File.Exists(targetPath))
            {
                throw new FileNotFoundException(
                    $"변환된 모델 파일을 찾을 수 없습니다: {targetPath}");
            }

            string relativePath = "Assets/" + IMPORTED_CHARACTERS_FOLDER + "/" +
                                  folderName + "/" + Path.GetFileName(targetPath);
            UnityEditor.AssetDatabase.ImportAsset(
                relativePath, UnityEditor.ImportAssetOptions.ForceUpdate);
            EnsureHumanoidAvatar(relativePath);
            return relativePath;
        }

        /// <summary>
        /// 벤더된 MMD4Mecanim PMX2FBX 외부 변환기로 PMX를 FBX로 변환한다.
        /// 출력 베이스명은 소스 파일명을 그대로 유지한다.
        /// </summary>
        private static void ConvertPmxToFbx(string pmxPath, string outputBase)
        {
            string converterDir = Path.Combine(
                Application.dataPath, "MMD4Mecanim", "Editor", "PMX2FBX");
            string converterExe = Path.Combine(converterDir, "pmx2fbx.exe");
            if (!File.Exists(converterExe))
            {
                throw new FileNotFoundException(
                    $"PMX 변환기가 없습니다: {converterExe}");
            }

            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = converterExe,
                Arguments = $"-i \"{pmxPath}\" -o \"{outputBase}\"",
                WorkingDirectory = converterDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using (var process = System.Diagnostics.Process.Start(start))
            {
                // 변환기는 대량의 진행 출력을 내니 비동기로 비워 버퍼 데드락을 막는다.
                _ = process.StandardOutput.ReadToEndAsync();
                var stderrReader = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(120000))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("PMX 변환이 시간 안에 끝나지 않았습니다.");
                }
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        $"PMX 변환 실패(exit {process.ExitCode}): {stderrReader.Result}");
                }
            }

            string generated = outputBase + ".fbx";
            if (!File.Exists(generated))
            {
                throw new FileNotFoundException(
                    $"PMX 변환 결과가 없습니다: {generated}");
            }
        }

        /// <summary>
        /// PMX 디렉터리의 텍스처 파일을 상대 경로 그대로 관리 폴더로 복사한다.
        /// FBX 재질이 텍스처를 찾을 수 있게 한다.
        /// 사용자가 넓은 디렉터리를 고를 수 있으므로 깊이·개수 상한과
        /// 정션/읽기 불가 디렉터리 건너뛰기로 복사 범위를 제한한다.
        /// </summary>
        private static void CopyPmxTextures(string pmxPath, string targetDir)
        {
            const int MaxDepth = 4;
            const int MaxFiles = 512;

            string sourceDir = Path.GetDirectoryName(Path.GetFullPath(pmxPath));
            if (string.IsNullOrEmpty(sourceDir) || !Directory.Exists(sourceDir))
            {
                return;
            }
            var textureExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".dds", ".gif",
                ".spa", ".sph", ".toon",
            };
            string targetFull = Path.GetFullPath(targetDir);
            var pending = new Stack<KeyValuePair<string, int>>();
            pending.Push(new KeyValuePair<string, int>(sourceDir, 0));
            int copied = 0;
            bool depthSkipped = false;
            while (pending.Count > 0)
            {
                KeyValuePair<string, int> item = pending.Pop();
                if (item.Value > MaxDepth)
                {
                    depthSkipped = true;
                    continue;
                }

                string[] files;
                string[] subdirs;
                try
                {
                    files = Directory.GetFiles(item.Key);
                    subdirs = Directory.GetDirectories(item.Key);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"PMX 텍스처 폴더를 읽을 수 없어 건너뜁니다: {item.Key} ({exception.Message})");
                    continue;
                }

                foreach (string file in files)
                {
                    if (!textureExts.Contains(Path.GetExtension(file)))
                    {
                        continue;
                    }
                    if (++copied > MaxFiles)
                    {
                        Debug.LogWarning($"PMX 텍스처 복사 상한({MaxFiles}개)에 도달했습니다: {sourceDir}");
                        return;
                    }
                    try
                    {
                        string relative = file.Substring(sourceDir.Length).TrimStart(
                            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        string dest = Path.Combine(targetFull, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        File.Copy(file, dest, true);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"PMX 텍스처 복사를 건너뜁니다: {file} ({exception.Message})");
                    }
                }

                foreach (string sub in subdirs)
                {
                    var info = new DirectoryInfo(sub);
                    // 정션·심볼릭 링크는 순환 복사 위험이 있으므로 따라가지 않는다.
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }
                    // 관리 폴더가 소스 트리 안에 있으면 자기 복사를 막는다.
                    if (info.FullName.StartsWith(targetFull, StringComparison.OrdinalIgnoreCase) ||
                        targetFull.StartsWith(info.FullName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    pending.Push(new KeyValuePair<string, int>(sub, item.Value + 1));
                }
            }
            if (depthSkipped)
            {
                Debug.LogWarning(
                    $"PMX 텍스처 탐색 깊이 상한({MaxDepth})을 넘는 폴더를 건너뛰었습니다: {sourceDir}");
            }
        }

        /// <summary>
        /// 모델 FBX를 Humanoid(CreateFromThisModel)로 설정한다.
        /// 모션 클립 검증이 포함된 EnsureHumanoid와 달리 캐릭터는 클립이 없으므로
        /// 아바타 유효성만 확인하고, 알려진 본 명명 규칙 폴백 매핑은 재사용한다.
        /// </summary>
        private static void EnsureHumanoidAvatar(string assetPath)
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

        /// <summary>
        /// 엔트리 삭제 시 임포트 사본 폴더를 정리한다. 실패해도 엔트리 삭제는 유지된다.
        /// </summary>
        public static void TryDeleteImportedCopy(string entryId)
        {
            if (string.IsNullOrEmpty(entryId))
            {
                return;
            }
            try
            {
                string assetPath = "Assets/" + IMPORTED_CHARACTERS_FOLDER + "/" +
                                   Path.GetFileName(entryId);
                if (UnityEditor.AssetDatabase.IsValidFolder(assetPath))
                {
                    UnityEditor.AssetDatabase.DeleteAsset(assetPath);
                }
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }
        }
#else
        public static void TryDeleteImportedCopy(string entryId) { }
#endif

        private static void DestroyRoot(GameObject root)
        {
            if (root == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Object.Destroy(root);
            }
            else
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
