using System;
using System.IO;
using UnityEditor;

namespace Fbx2Vmd.CharacterLibrary
{
    /// <summary>
    /// 프로젝트 안의 원본 FBX가 쓰는 재질 연결(임베디드 재질 → 텍스처가 든 재질 에셋)을
    /// 캐릭터 라이브러리 관리 폴더의 사본에도 똑같이 지정함.
    /// 사본에는 원본 .meta의 연결이 없어 텍스처 없는 임베디드 재질로 보이던 문제를 막음.
    /// </summary>
    internal static class EditorModelMaterialRemapCopier
    {
        internal static void CopyFromSource(string sourcePath, string targetAssetPath)
        {
            if (!TryGetProjectAssetPath(sourcePath, out string sourceAssetPath) ||
                string.Equals(sourceAssetPath, targetAssetPath, StringComparison.OrdinalIgnoreCase))
                return;
            if (!(AssetImporter.GetAtPath(sourceAssetPath) is ModelImporter source) ||
                !(AssetImporter.GetAtPath(targetAssetPath) is ModelImporter target))
                return;

            var existing = target.GetExternalObjectMap();
            bool isChanged = false;
            foreach (var remap in source.GetExternalObjectMap())
            {
                bool isSameRemap = existing.TryGetValue(remap.Key, out var current) && current == remap.Value;
                if (remap.Value == null || isSameRemap)
                    continue;
                target.AddRemap(remap.Key, remap.Value);
                isChanged = true;
            }
            if (isChanged)
                target.SaveAndReimport();
        }

        // 프로젝트 Assets 아래 파일이면 에셋 경로("Assets/...")를 돌려줌. 밖의 파일은 연결할 원본 설정이 없음.
        private static bool TryGetProjectAssetPath(string path, out string assetPath)
        {
            assetPath = null;
            if (string.IsNullOrWhiteSpace(path))
                return false;
            string fullPath = Path.GetFullPath(path).Replace('\\', '/');
            string dataPath = Path.GetFullPath(UnityEngine.Application.dataPath).Replace('\\', '/') + "/";
            if (!fullPath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                return false;
            assetPath = "Assets/" + fullPath.Substring(dataPath.Length);
            return true;
        }
    }
}
