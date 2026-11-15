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
        public RegisterResult Register(string sourcePath)
        {
            var result = new RegisterResult();
            ReloadIfExternallyChanged();

            if (!TryNormalizeAndValidate(sourcePath, out string normalizedPath, result))
            {
                return result;
            }
            if (!TryReadUniqueContent(
                    normalizedPath, out long fileSize, out string lastWriteTimeUtc,
                    out string contentHash, result))
            {
                return result;
            }

            if (!TryReadCharacterMeta(
                    normalizedPath, out VrmCharacterMetaResult meta, result))
            {
                return result;
            }

            var entry = BuildRegisteredEntry(
                meta, normalizedPath, fileSize, lastWriteTimeUtc, contentHash);

            Document.entries.Add(entry);
            if (!TrySaveNewEntry(entry, result))
            {
                return result;
            }
            result.entry = entry;
            return result;
        }

        bool TryNormalizeAndValidate(string sourcePath, out string normalizedPath, RegisterResult result)
        {
            try
            {
                normalizedPath = Path.GetFullPath((sourcePath ?? string.Empty).Trim());
            }
            catch (Exception error)
            {
                normalizedPath = null;
                result.error = $"경로가 유효하지 않습니다: {error.Message}";
                return false;
            }

            if (!TryValidateCharacterFile(normalizedPath, result))
            {
                return false;
            }

            CharacterLibraryEntry existing = FindBySourcePath(normalizedPath);
            if (existing != null)
            {
                result.error = "이미 등록된 캐릭터입니다.";
                result.entry = existing;
                return false;
            }
            return true;
        }

        private bool TryReadCharacterMeta(
            string normalizedPath,
            out VrmCharacterMetaResult meta,
            RegisterResult result)
        {
            meta = _metaReader(normalizedPath);
            if (meta != null && meta.success)
            {
                return true;
            }
            result.error = meta != null && !string.IsNullOrEmpty(meta.error)
                ? meta.error
                : "캐릭터 메타데이터를 읽을 수 없습니다.";
            return false;
        }

        private bool TrySaveNewEntry(
            CharacterLibraryEntry entry, RegisterResult result)
        {
            try
            {
                Save();
                return true;
            }
            catch (Exception error)
            {
                // 저장 실패 시 남은 썸네일이 orphan이 되지 않게 정리하고,
                // 메모리 문서도 디스크 상태에 맞춰 롤백한다.
                DeleteThumbnailFile(entry.thumbnailFileName);
                Document.entries.Remove(entry);
                result.error = $"라이브러리를 저장할 수 없습니다: {error.Message}";
                return false;
            }
        }

        bool TryReadUniqueContent(
            string normalizedPath, out long fileSize, out string lastWriteTimeUtc,
            out string contentHash, RegisterResult result)
        {
            fileSize = 0;
            lastWriteTimeUtc = null;
            contentHash = null;
            if (!CharacterFileHasher.TryCaptureFileStat(
                    normalizedPath, out fileSize, out lastWriteTimeUtc, out contentHash))
            {
                result.error = "파일 정보를 읽을 수 없습니다.";
                return false;
            }

            // 다른 경로에 같은 내용의 파일이 이미 등록돼 있으면 중복으로 간주한다.
            for (int i = 0; i < Document.entries.Count; i++)
            {
                if (string.Equals(Document.entries[i].contentHash, contentHash, StringComparison.Ordinal))
                {
                    result.error = "동일한 내용의 파일이 이미 등록되어 있습니다.";
                    result.entry = Document.entries[i];
                    return false;
                }
            }
            return true;
        }

        private bool TryValidateCharacterFile(
            string normalizedPath, RegisterResult result)
        {
            if (!File.Exists(normalizedPath))
            {
                result.error = $"파일이 없습니다: {normalizedPath}";
                return false;
            }
            if (!IsSupportedCharacterFile(Path.GetExtension(normalizedPath)))
            {
                result.error = "캐릭터 파일(.vrm/.fbx/.pmx)만 등록할 수 있습니다.";
                return false;
            }
            return true;
        }

        private CharacterLibraryEntry BuildRegisteredEntry(
            VrmCharacterMetaResult meta,
            string normalizedPath,
            long fileSize,
            string lastWriteTimeUtc,
            string contentHash)
        {
            var entry = new CharacterLibraryEntry
            {
                id = Guid.NewGuid().ToString("N"),
                displayName = ResolveDisplayName(meta.meta, normalizedPath),
                sourcePath = normalizedPath,
                fileSize = fileSize,
                lastWriteTimeUtc = lastWriteTimeUtc,
                contentHash = contentHash,
                vrmSpecVersion = meta.specVersion ?? string.Empty,
                meta = meta.meta ?? new CharacterLibraryEntryMeta(),
                registeredAtUtc = DateTime.UtcNow.ToString("O"),
                status = CharacterLibraryEntryStatus.Ready,
            };
            entry.thumbnailFileName =
                TryWriteThumbnail(entry.id, meta.thumbnailPng);
            return entry;
        }

        private static bool IsSupportedCharacterFile(string extension)
        {
            return
                string.Equals(extension, ".vrm", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".fbx", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".pmx", StringComparison.OrdinalIgnoreCase);
        }

        private bool TryApplyRelink(
            CharacterLibraryEntry entry,
            string normalizedPath,
            out string error)
        {
            if (!TryValidateRelinkTarget(
                    entry, normalizedPath,
                    out long fileSize, out string lastWriteTimeUtc,
                    out string contentHash, out error))
            {
                return false;
            }
            ApplyRelink(entry, normalizedPath, fileSize, lastWriteTimeUtc);
            return true;
        }

        private CharacterLibraryEntry ResolveLatestEntry(
            CharacterLibraryEntry entry)
        {
            ReloadIfExternallyChanged();
            return FindById(entry.id) ?? entry;
        }

        /// <summary>
        /// 엔트리의 파일 존재·내용 변경 상태를 다시 확인해 status를 갱신한다.
        /// </summary>
        public void RefreshEntry(CharacterLibraryEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            entry = ResolveLatestEntry(entry);

            if (!File.Exists(entry.sourcePath))
            {
                entry.status = CharacterLibraryEntryStatus.Missing;
                return;
            }

            if (!CharacterFileHasher.TryCaptureFileStat(
                    entry.sourcePath, out long fileSize, out string lastWriteTimeUtc, out string contentHash))
            {
                entry.status = CharacterLibraryEntryStatus.Error;
                entry.lastError = "파일 정보를 읽을 수 없습니다.";
                return;
            }

            ApplyFileStatToEntry(
                entry, fileSize, lastWriteTimeUtc, contentHash);
        }

        private static void ApplyFileStatToEntry(
            CharacterLibraryEntry entry,
            long fileSize,
            string lastWriteTimeUtc,
            string contentHash)
        {
            // 내용 해시가 바뀐 경우만 Changed로 표시한다(mtime만 바뀐 터치는 무시).
            bool contentChanged = !string.Equals(
                entry.contentHash, contentHash, StringComparison.Ordinal);

            entry.fileSize = fileSize;
            entry.lastWriteTimeUtc = lastWriteTimeUtc;

            if (contentChanged)
            {
                entry.contentHash = contentHash;
                entry.status = CharacterLibraryEntryStatus.Changed;
            }
            else if (entry.status == CharacterLibraryEntryStatus.Missing ||
                     entry.status == CharacterLibraryEntryStatus.Error)
            {
                // 파일이 정상으로 읽히면 Missing/Error를 해소한다.
                entry.status = CharacterLibraryEntryStatus.Ready;
                entry.lastError = string.Empty;
            }
        }

        /// <summary>
        /// 파일이 이동/삭제된 엔트리를 새 경로에 연결한다. 내용 해시가 같을 때만 허용한다.
        /// </summary>
        public bool TryRelink(CharacterLibraryEntry entry, string newPath, out string error)
        {
            error = string.Empty;
            if (entry == null)
            {
                error = "엔트리가 없습니다.";
                return false;
            }

            entry = ResolveLatestEntry(entry);

            if (!TryNormalizeNewPath(
                    newPath, out string normalizedPath, out error))
            {
                return false;
            }

            if (!TryApplyRelink(entry, normalizedPath, out error))
            {
                return false;
            }

            Save();
            return true;
        }

        private static bool TryNormalizeNewPath(
            string newPath,
            out string normalizedPath,
            out string error)
        {
            try
            {
                normalizedPath =
                    Path.GetFullPath((newPath ?? string.Empty).Trim());
            }
            catch (Exception exception)
            {
                normalizedPath = null;
                error = $"경로가 유효하지 않습니다: {exception.Message}";
                return false;
            }

            if (!File.Exists(normalizedPath))
            {
                error = $"파일이 없습니다: {normalizedPath}";
                return false;
            }
            error = null;
            return true;
        }

        private static void ApplyRelink(
            CharacterLibraryEntry entry,
            string normalizedPath,
            long fileSize,
            string lastWriteTimeUtc)
        {
            entry.sourcePath = normalizedPath;
            entry.fileSize = fileSize;
            entry.lastWriteTimeUtc = lastWriteTimeUtc;
            entry.status = CharacterLibraryEntryStatus.Ready;
            entry.lastError = string.Empty;
        }

        private static bool HasSameExtension(
            CharacterLibraryEntry entry, string normalizedPath)
        {
            return string.Equals(
                Path.GetExtension(normalizedPath),
                Path.GetExtension(entry.sourcePath),
                StringComparison.OrdinalIgnoreCase);
        }


        // 재연결은 같은 파일의 위치만 바꾸므로 확장자가 달라지면
        // 로더 분기(spec)가 어긋난다. 형식 변경은 새 등록으로 처리한다.
        static bool TryValidateRelinkTarget(
            CharacterLibraryEntry entry, string normalizedPath,
            out long fileSize, out string lastWriteTimeUtc, out string contentHash,
            out string error)
        {
            fileSize = 0;
            lastWriteTimeUtc = null;
            contentHash = null;
            error = null;
            if (!HasSameExtension(entry, normalizedPath))
            {
                error = "파일 형식이 달라 재연결할 수 없습니다. 같은 형식의 파일을 선택하세요.";
                return false;
            }

            if (!CharacterFileHasher.TryCaptureFileStat(
                    normalizedPath, out fileSize, out lastWriteTimeUtc, out contentHash))
            {
                error = "파일 정보를 읽을 수 없습니다.";
                return false;
            }

            if (!string.Equals(entry.contentHash, contentHash, StringComparison.Ordinal))
            {
                error = "파일 내용이 등록된 캐릭터와 다릅니다.";
                return false;
            }
            return true;
        }

        public bool Remove(string id)
        {
            ReloadIfExternallyChanged();
            CharacterLibraryEntry entry = FindById(id);
            if (entry == null)
            {
                return false;
            }

            Document.entries.Remove(entry);
            if (string.Equals(Document.activeCharacterId, entry.id, StringComparison.Ordinal))
            {
                Document.activeCharacterId = string.Empty;
            }
            Save();
            DeleteThumbnailFile(entry.thumbnailFileName);
            // FBX 임포트 사본 폴더도 함께 정리한다(에디터 외 환경에서는 no-op).
            FbxCharacterLoader.TryDeleteImportedCopy(entry.id);
            // 캐릭터 귀속 프리셋 파일도 함께 정리한다(실패해도 제거는 유지).
            _presetStores.Remove(entry.id);
            DeletePresetFileIfExists(entry.id);
            return true;
        }

        private void DeletePresetFileIfExists(string entryId)
        {
            string presetFile = ResolvePresetFilePath(entryId);
            if (string.IsNullOrEmpty(presetFile) || !File.Exists(presetFile))
            {
                return;
            }
            try
            {
                File.Delete(presetFile);
            }
            catch (Exception error)
            {
                // 프리셋 파일 삭제 실패는 엔트리 제거를 막지 않지만 흔적은 남긴다.
                Debug.LogWarning(
                    $"[CharacterLibrary] 프리셋 파일을 삭제하지 못했습니다: " +
                    $"{presetFile} ({error.Message})");
            }
        }



        /// <summary>
        /// 마지막 사용 시각과 활성 캐릭터 표시를 갱신한다.
        /// </summary>
        public void MarkUsed(string id)
        {
            ReloadIfExternallyChanged();
            CharacterLibraryEntry entry = FindById(id);
            if (entry == null)
            {
                return;
            }

            entry.lastUsedAtUtc = DateTime.UtcNow.ToString("O");
            Document.activeCharacterId = entry.id;
            Save();
        }

        /// <summary>
        /// 활성 캐릭터 기록을 지운다. 씬 캐릭터로 복원할 때 호출해
        /// 다음 시작 시 자동 로드가 다시 실행되지 않게 한다.
        /// </summary>
        public void ClearActiveCharacter()
        {
            ReloadIfExternallyChanged();
            if (string.IsNullOrEmpty(Document.activeCharacterId))
            {
                return;
            }
            Document.activeCharacterId = string.Empty;
            Save();
        }

        /// <summary>
        /// 마지막 읽기가 IO 오류로 실패해 문서가 기본값인지.
        /// 이 상태에서 Save하면 정상 파일이 소실되므로 호출자가 먼저 확인할 수 있다.
        /// </summary>
    }
}
