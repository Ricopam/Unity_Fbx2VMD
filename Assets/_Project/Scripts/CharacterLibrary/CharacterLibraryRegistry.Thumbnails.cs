using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Fbx2Vmd.CharacterLibrary
{
    /// <summary>
    /// 캐릭터 라이브러리의 조회·등록·갱신을 담당하는 서비스.
    /// 스토어(영속화), 해시(식별), 메타 리더(표시 정보)를 묶는다.
    /// metaReader는 테스트에서 주입할 수 있는 IO 경계다.
    /// </summary>
    public sealed partial class CharacterLibraryRegistry
    {
        public string ResolveThumbnailPath(CharacterLibraryEntry entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }
            return CharacterLibraryPathResolver.ResolveThumbnailPath(
                LibraryFilePath, entry.thumbnailFileName);
        }

        private string TryWriteThumbnail(string entryId, byte[] png)
        {
            if (png == null || png.Length == 0)
            {
                return string.Empty;
            }

            try
            {
                string directory = CharacterLibraryPathResolver.ResolveThumbnailsDirectory(LibraryFilePath);
                Directory.CreateDirectory(directory);
                string fileName = entryId + ".png";
                File.WriteAllBytes(Path.Combine(directory, fileName), png);
                return fileName;
            }
            catch (Exception)
            {
                // 썸네일 저장 실패는 등록 자체를 막지 않는다.
                return string.Empty;
            }
        }

        private void DeleteThumbnailFile(string thumbnailFileName)
        {
            string path = CharacterLibraryPathResolver.ResolveThumbnailPath(
                LibraryFilePath, thumbnailFileName);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
                // 썸네일 삭제 실패는 레지스트리 동작을 막지 않는다.
            }
        }

        private static string ResolveDisplayName(CharacterLibraryEntryMeta meta, string path)
        {
            if (meta != null && !string.IsNullOrWhiteSpace(meta.title))
            {
                return meta.title.Trim();
            }
            return Path.GetFileNameWithoutExtension(path);
        }

    }
}
