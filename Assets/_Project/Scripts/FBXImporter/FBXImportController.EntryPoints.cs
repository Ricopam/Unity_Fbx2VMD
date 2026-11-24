using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using System.IO;
using Fbx2Vmd.FileSystem;
using Fbx2Vmd.Profiling;

namespace Fbx2Vmd.FBXImporter
{
    // 파일 다이얼로그·Import_FBX 폴더·SharedSettings 진입점 분할.
    public partial class FBXImportController
    {
        public void ImportFromDialog()
        {
            TryImportFromDialog();
        }

        internal bool TryImportFromDialog()
        {
            _pipeline.EnsureServicesInitialized();

            if (_pipeline._isProcessing)
            {
                _pipeline.SetSessionState(FBXVmdPipeline.FBXSessionState.LoadingFbx, "이미 FBX 처리 중입니다.", 0.1f);
                return true;
            }

            _pipeline.SetSessionState(FBXVmdPipeline.FBXSessionState.Idle, "FBX 파일 선택 대기", 0f);
            string[] paths = _fileBrowserService.OpenFilePanel(
                "Import FBX", "", FBXVmdPipeline.FBX_EXTENSION, false);
            if (paths.Length == 0 || string.IsNullOrEmpty(paths[0]))
            {
                _pipeline.SetSessionState(FBXVmdPipeline.FBXSessionState.Cancelled, "파일 선택이 취소됨.", 0f);
                return true;
            }

            string sourcePath = paths[0];
            Debug.Log($"[FBXImport] 파일 선택됨. 경로={sourcePath}");
            _pipeline.ProcessFBXAsync(sourcePath);
            return true;
        }

        public void LoadFromImportFolder()
        {
            TryLoadFromImportFolder();
        }

        internal bool TryLoadFromImportFolder()
        {
            if (_pipeline._isProcessing)
            {
                _pipeline.SetSessionState(FBXVmdPipeline.FBXSessionState.LoadingFbx, "이미 FBX 처리 중입니다.", 0.1f);
                return true;
            }

            string targetDir = GetControlledImportDirectory();
            if (!Directory.Exists(targetDir))
            {
                _pipeline.SetSessionState(FBXVmdPipeline.FBXSessionState.Failed, $"Import_FBX 폴더가 없습니다: {targetDir}", 0f);
                return true;
            }

            string[] fbxFiles = Directory.GetFiles(targetDir, "*.fbx", SearchOption.TopDirectoryOnly);
            if (fbxFiles.Length == 0)
            {
                _pipeline.SetSessionState(FBXVmdPipeline.FBXSessionState.Failed, "Import_FBX 폴더에 FBX 파일이 없습니다", 0f);
                return true;
            }

            string selectedFile = fbxFiles[0];
            _pipeline.ProcessFBXAsync(selectedFile);
            return true;
        }

        public bool TryImportFromSharedSettings(string sourcePath)
        {
            if (_pipeline._isProcessing)
            {
                _pipeline.SetSessionState(FBXVmdPipeline.FBXSessionState.LoadingFbx, "이미 FBX 처리 중입니다.", 0.1f);
                return false;
            }

            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return false;
            }

            // Main 씬은 캐릭터 없이 시작하므로, 처리 도중 실패시키지 않고 요청 단계에서 안내함.
            if (_pipeline.targetCharacter == null)
            {
                _pipeline.SetSessionState(FBXVmdPipeline.FBXSessionState.Failed, MissingCharacterMessage, 0f);
                return false;
            }

            _pipeline.ProcessFBXAsync(sourcePath.Trim());
            return true;
        }

        internal const string MissingCharacterMessage = "캐릭터를 먼저 불러와 주세요. 설정창 홈의 캐릭터 선택에서 불러올 수 있습니다.";
    }
}
