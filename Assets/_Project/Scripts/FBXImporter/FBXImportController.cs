using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using System.IO;
using Fbx2Vmd.FileSystem;
using Fbx2Vmd.Profiling;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// FBXVmdPipeline의 FBX 파일 선택 및 임포트 진입점을 담당하는 컴패니언 컨트롤러입니다.
    /// 파일 다이얼로그, Import_FBX 폴더 로드, SharedSettings 연동을 캡슐화합니다.
    /// </summary>
    public partial class FBXImportController
    {
        internal const string BONE_MAPPING_FILE = "BoneMapping_Data.txt";

        private readonly FBXVmdPipeline _pipeline;
        private readonly IFileBrowserService _fileBrowserService;
        private readonly Func<string, Task<GameObject>> _importModelAsync;

        public FBXImportController(
            FBXVmdPipeline pipeline,
            IFileBrowserService fileBrowserService,
            Func<string, Task<GameObject>> importModelAsync)
        {
            _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
            _fileBrowserService = fileBrowserService ?? throw new ArgumentNullException(nameof(fileBrowserService));
            _importModelAsync = importModelAsync ?? throw new ArgumentNullException(nameof(importModelAsync));
        }

        internal string CopyToControlledImportFolder(string sourcePath)
        {
            using var perfScope = PerfScope.Measure("FBXImportController.CopyToControlledImportFolder");
            string targetDir = GetControlledImportDirectory();
            Directory.CreateDirectory(targetDir);

            string safeFileName = SanitizeFileName(Path.GetFileName(sourcePath));
            string targetPath = Path.Combine(targetDir, safeFileName);

            if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, targetPath, true);
            }

            return targetPath;
        }

        internal static string GetControlledImportDirectory()
        {
#if UNITY_EDITOR
            return Path.Combine(Application.dataPath, "Resources", FBXVmdPipeline.IMPORT_FBX_FOLDER);
#else
            return Path.Combine(Application.persistentDataPath, FBXVmdPipeline.IMPORT_FBX_FOLDER);
#endif
        }

        internal static string SanitizeFileName(string fileName)
        {
            string safeName = string.IsNullOrWhiteSpace(fileName) ? "motion.fbx" : fileName.Trim();
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                safeName = safeName.Replace(invalidChar, '_');
            }

            return safeName;
        }

        internal static bool TryValidateSourcePath(string sourcePath, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                errorMessage = $"FBX 파일을 찾을 수 없습니다: {sourcePath}";
                return false;
            }

            if (!string.Equals(Path.GetExtension(sourcePath), ".fbx", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "FBX 파일만 선택할 수 있습니다.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        internal async Task<FBXModelImportResult> ImportRuntimeModelAsync(
            string sourcePath,
            bool shouldRecordVmdAfterImport)
        {
            if (!TryValidateSourcePath(sourcePath, out string sourceValidationError))
            {
                return FBXModelImportResult.Fail(sourceValidationError);
            }

            _pipeline.SetSessionState( FBXVmdPipeline.FBXSessionState.Selected, $"선택됨: {Path.GetFileName(sourcePath)}", 0.05f);
            string targetPath = CopyToControlledImportFolder(sourcePath);
            string outputBaseName = Path.GetFileNameWithoutExtension(targetPath);
            ReportImportModeAndCopy(sourcePath, targetPath, outputBaseName, shouldRecordVmdAfterImport);
            SetProfilerInputContext(targetPath);
            ConfigureEditorImportSettingsIfNeeded(sourcePath, targetPath);

            _pipeline.SetSessionState( FBXVmdPipeline.FBXSessionState.LoadingFbx, "FBX 로드 중", 0.25f);
            GameObject importedModel;
            // Assimp 네이티브 임포트는 워커 스레드에서 실행되므로 GC 지표는 호출 스레드 기준만 남는다(문서화된 한계).
            using (PerfScope.Measure("FBXImportController.ImportModelAsync"))
            {
                importedModel = await _importModelAsync(targetPath);
            }
            if (importedModel == null)
            {
                return FBXModelImportResult.Fail("FBX 로드에 실패했습니다.");
            }

            return FBXModelImportResult.Succeed(importedModel, targetPath, outputBaseName);
        }

        // VMD 자동 녹화 여부에 따라 출력명 로그와 복제 완료 상태를 보고함.
        private void ReportImportModeAndCopy(
            string sourcePath, string targetPath, string outputBaseName,
            bool shouldRecordVmdAfterImport)
        {
            if (shouldRecordVmdAfterImport)
            {
                Debug.Log($"[Recording] 자동 VMD 출력명 고정됨. VMD={outputBaseName}.vmd, 입력 FBX={Path.GetFileName(sourcePath)}");
            }
            else
            {
                Debug.Log($"[FBXImport] Unity 촬영 전용 모드 선택됨. 출력={outputBaseName}, VMD 자동 녹화=생략");
            }

            _pipeline.SetSessionState( FBXVmdPipeline.FBXSessionState.Copied, $"복제 완료: {Path.GetFileName(targetPath)}", 0.15f);
        }

        // 계측 컨텍스트에 입력 파일 크기를 기록함. 실패해도 변환을 막지 않음.
        private static void SetProfilerInputContext(string targetPath)
        {
            try
            {
                PipelineRunProfiler.SetContext(inputBytes: new FileInfo(targetPath).Length);
            }
            catch { /* 계측 컨텍스트 실패는 변환을 막지 않음 */ }
        }

        internal static Dictionary<string, string> LoadBoneMappingRuntime()
        {
            using var perfScope = PerfScope.Measure("FBXImportController.LoadBoneMappingRuntime");
            Dictionary<string, string> mapping = new Dictionary<string, string>();
            string loadName = Path.GetFileNameWithoutExtension(BONE_MAPPING_FILE);
            TextAsset mappingAsset = Resources.Load<TextAsset>(loadName);
            if (mappingAsset == null)
            {
                Debug.LogWarning($"[FBXImport] BoneMapping 로드 실패: Resources/{loadName}.txt (자동 본 매핑으로 폴백합니다.)");
                return mapping; }
            Debug.Log($"[FBXImport] BoneMapping 로드 성공 (Resources/{loadName})");
            string[] lines = mappingAsset.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            ParseBoneMappingLines(lines, mapping);
            return mapping; }

        // m_BoneTemplate 블록 안의 key:value 항목만 매핑에 수집함.
        private static void ParseBoneMappingLines(string[] lines, Dictionary<string, string> mapping)
        {
            bool insideBoneTemplate = false;
            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("m_BoneTemplate:"))
                {
                    insideBoneTemplate = true;
                    continue; }
                if (!insideBoneTemplate)
                {
                    continue; }
                if (trimmedLine.StartsWith("m_"))
                {
                    break; }
                int colonIndex = trimmedLine.IndexOf(':');
                if (colonIndex <= 0)
                {
                    continue; }
                string key = trimmedLine[..colonIndex].Trim();
                string value = trimmedLine[(colonIndex + 1)..].Trim();
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                {
                    mapping[key] = value; } } }

        internal static bool ValidateGhostAvatar(GameObject importedModel)
        {
            Animator ghostAnimator = importedModel.GetComponent<Animator>();
            if (ghostAnimator == null || ghostAnimator.avatar == null)
            {
                Debug.LogError("[FBXImport] Ghost Animator 또는 Avatar가 없습니다.");
                return false;
            }

            if (!ghostAnimator.avatar.isValid || !ghostAnimator.avatar.isHuman)
            {
                Debug.LogError($"[FBXImport] Ghost Avatar가 유효하지 않습니다. valid={ghostAnimator.avatar.isValid}, human={ghostAnimator.avatar.isHuman}");
                return false;
            }

            return true;
        }

        internal static bool TryPrepareRuntimeAvatar(
            GameObject importedModel,
            out Dictionary<string, string> boneMapping,
            out string errorMessage)
        {
            boneMapping = LoadBoneMappingRuntime();
            if (boneMapping == null)
            {
                boneMapping = new Dictionary<string, string>();
            }

            // BoneMapping_Data.txt는 특정 리그에 종속될 수 있으므로, 실패 시 자동 매핑으로 폴백함.
            HumanoidAvatarBuilder.SetupHumanoid(importedModel, boneMapping);
            if (!ValidateGhostAvatar(importedModel))
            {
                Debug.LogWarning("[FBXImport] Ghost Humanoid Avatar 생성 실패함. 자동 본 매핑으로 재시도함.");
                boneMapping = HumanoidAvatarBuilder.BuildAutoMapping(importedModel);
                HumanoidAvatarBuilder.SetupHumanoid(importedModel, boneMapping);

                if (!ValidateGhostAvatar(importedModel))
                {
                    errorMessage = "Ghost Humanoid Avatar 생성에 실패했습니다.";
                    return false;
                }
            }
            errorMessage = string.Empty;
            return true;
        }

        internal bool TryPrepareRuntimeAnimation(
            GameObject importedModel,
            bool shouldLogRuntimeAnimation,
            out Dictionary<string, string> boneMapping,
            out Animation ghostAnimation,
            out AnimationClip targetClip,
            out string errorMessage)
        {
            ghostAnimation = null;
            targetClip = null;
            boneMapping = null;
            ghostAnimation = importedModel.GetComponent<Animation>();
            targetClip = ExtractPrimaryClip(ghostAnimation, shouldLogRuntimeAnimation);
            if (targetClip == null)
            {
                errorMessage = "FBX에서 유효한 애니메이션 클립을 찾지 못했습니다.";
                return false; }
            if (!RuntimeHumanoidReferencePoseApplier.TryApply(importedModel, targetClip))
            {
                errorMessage = "FBX 기준 자세를 준비하지 못했습니다.";
                return false; }
            if (!TryPrepareRuntimeAvatar(importedModel, out boneMapping, out errorMessage))
            {
                return false; }
            PipelineRunProfiler.SetContext( clipLengthSec: targetClip.length, boneCount: importedModel.GetComponentsInChildren<Transform>(true).Length);
            _pipeline.SetSessionState( FBXVmdPipeline.FBXSessionState.AvatarReady, "Humanoid Avatar 준비 완료", 0.45f);
            errorMessage = string.Empty;
            return true; }

        internal static AnimationClip ExtractPrimaryClip(Animation ghostAnimation, bool shouldLogRuntimeAnimation)
        {
            using var perfScope = PerfScope.Measure("FBXImportController.ExtractPrimaryClip");
            if (ghostAnimation == null || ghostAnimation.clip == null)
            {
                return null;
            }

            AnimationClip targetClip = ghostAnimation.clip;
            if (targetClip.length <= 0f || float.IsNaN(targetClip.length) || float.IsInfinity(targetClip.length))
            {
                Debug.LogError($"[FBXImport] 애니메이션 길이가 올바르지 않습니다: {targetClip.length}");
                return null;
            }

            if (targetClip.length > 1000f)
            {
                Debug.LogWarning("[FBXImport] 애니메이션 길이가 비정상적으로 깁니다. Assimp timeScale을 확인하세요.");
            }

            if (shouldLogRuntimeAnimation)
            {
                Debug.Log($"[FBXImport] Clip: {targetClip.name}, Length: {targetClip.length:F3}s, FrameRate: {targetClip.frameRate}");
            }

            return targetClip;
        }

        // 에디터 임포트 설정은 에디터 드라이버로 위임함 — 슬롯이 비면 무동작
        internal void ConfigureEditorImportSettingsIfNeeded(string sourcePath, string targetPath)
        {
            FbxImportEditorHooks.ConfigureEditorImportSettings?.Invoke(sourcePath, targetPath);
        }
    }
}
