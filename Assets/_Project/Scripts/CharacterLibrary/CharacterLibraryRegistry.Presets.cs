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
        public void Save()
        {
            if (_store.LastReadFailed)
            {
                throw new InvalidOperationException(
                    "라이브러리 파일을 읽지 못한 상태입니다. " +
                    "기존 데이터 보호를 위해 저장을 차단했습니다.");
            }
            _store.Save(Document);
            _knownWriteTimeUtc = _store.ResolveLastWriteTimeUtc();
        }

        /// <summary>
        /// 엔트리의 프리셋 JSON 절대 경로. 없으면 빈 문자열.
        /// </summary>
        public string ResolvePresetFilePath(string entryId)
        {
            return CharacterLibraryPathResolver.ResolvePresetFilePath(LibraryFilePath, entryId);
        }

        /// <summary>
        /// 엔트리의 프리셋 스토어를 돌려준다. 경로가 비어 있으면 null.
        /// 같은 엔트리에는 같은 인스턴스를 재사용해 읽기 실패 시 저장 차단이 유지된다.
        /// </summary>
        public CharacterPresetStore OpenPresetStore(string entryId)
        {
            string path = ResolvePresetFilePath(entryId);
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            if (!_presetStores.TryGetValue(entryId, out CharacterPresetStore store))
            {
                store = new CharacterPresetStore(path);
                _presetStores[entryId] = store;
            }
            return store;
        }

        /// <summary>
        /// 엔트리의 프리셋 문서를 읽는다. 파일이 없거나 읽기 실패면 빈 문서를 돌려준다.
        /// </summary>
        public CharacterPresetDocument LoadPresets(string entryId)
        {
            CharacterPresetStore store = OpenPresetStore(entryId);
            CharacterPresetDocument document = store != null
                ? store.LoadOrCreateDefault()
                : new CharacterPresetDocument();
            document.characterId = entryId ?? string.Empty;
            return document;
        }

        /// <summary>
        /// id로 프리셋 하나를 찾는다. 없으면 null.
        /// </summary>
        public CharacterPreset FindPreset(string entryId, string presetId)
        {
            if (string.IsNullOrEmpty(entryId) || string.IsNullOrEmpty(presetId))
            {
                return null;
            }

            CharacterPresetDocument document = LoadPresets(entryId);
            for (int i = 0; i < document.presets.Count; i++)
            {
                if (string.Equals(document.presets[i].id, presetId, StringComparison.Ordinal))
                {
                    return document.presets[i];
                }
            }
            return null;
        }

        /// <summary>
        /// FBX 예외 키로 정규화한다. 파일명만 쓴다(Import_FBX 임포트 슬롯 기준).
        /// 경로가 들어와도 파일명으로 환원하므로 같은 파일명은 같은 예외를 공유한다.
        /// </summary>
        public static string NormalizeFbxKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }
            string fileName = Path.GetFileName(path.Trim());
            return fileName ?? string.Empty;
        }

        /// <summary>
        /// fbxKey로 예외 하나를 찾는다. 없으면 null.
        /// </summary>
        public CharacterFbxOverride FindFbxOverride(string entryId, string fbxKey)
        {
            string key = NormalizeFbxKey(fbxKey);
            if (string.IsNullOrEmpty(entryId) || key.Length == 0)
            {
                return null;
            }

            CharacterPresetDocument document = LoadPresets(entryId);
            for (int i = 0; i < document.fbxOverrides.Count; i++)
            {
                if (string.Equals(document.fbxOverrides[i].fbxKey, key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return document.fbxOverrides[i];
                }
            }
            return null;
        }

        /// <summary>
        /// 예외를 fbxKey 기준으로 추가 또는 갱신하고 프리셋 파일에 저장한다.
        /// </summary>
        public void UpsertFbxOverride(string entryId, CharacterFbxOverride fbxOverride)
        {
            if (fbxOverride == null)
            {
                throw new ArgumentNullException(nameof(fbxOverride));
            }
            string key = NormalizeFbxKey(fbxOverride.fbxKey);
            if (string.IsNullOrEmpty(entryId) || key.Length == 0)
            {
                throw new ArgumentException("엔트리 id와 fbxKey가 필요합니다.", nameof(fbxOverride));
            }

            CharacterPresetDocument document = LoadPresets(entryId);
            fbxOverride.fbxKey = key;
            fbxOverride.updatedAtUtc = DateTime.UtcNow.ToString("O");
            if (!TryReplaceFbxOverride(document, key, fbxOverride))
            {
                document.fbxOverrides.Add(fbxOverride);
            }
            SavePresetDocument(entryId, document);
        }

        private void SavePresetDocument(
            string entryId, CharacterPresetDocument document)
        {
            CharacterPresetStore store = OpenPresetStore(entryId);
            if (store == null)
            {
                throw new InvalidOperationException("프리셋 저장 경로를 찾을 수 없습니다.");
            }
            store.Save(document);
        }

        private static bool TryReplaceFbxOverride(
            CharacterPresetDocument document,
            string key,
            CharacterFbxOverride fbxOverride)
        {
            for (int i = 0; i < document.fbxOverrides.Count; i++)
            {
                if (string.Equals(document.fbxOverrides[i].fbxKey, key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    document.fbxOverrides[i] = fbxOverride;
                    return true;
                }
            }
            return false;
        }


        /// <summary>
        /// 예외를 삭제한다. 대상이 없으면 false.
        /// </summary>
        public bool RemoveFbxOverride(string entryId, string fbxKey)
        {
            string key = NormalizeFbxKey(fbxKey);
            if (string.IsNullOrEmpty(entryId) || key.Length == 0)
            {
                return false;
            }

            CharacterPresetDocument document = LoadPresets(entryId);
            for (int i = 0; i < document.fbxOverrides.Count; i++)
            {
                if (string.Equals(document.fbxOverrides[i].fbxKey, key,
                        StringComparison.OrdinalIgnoreCase))
                {
                    document.fbxOverrides.RemoveAt(i);
                    CharacterPresetStore store = OpenPresetStore(entryId);
                    if (store == null)
                    {
                        throw new InvalidOperationException("프리셋 저장 경로를 찾을 수 없습니다.");
                    }
                    store.Save(document);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 엔트리의 기본 프리셋을 지정한다. 빈 presetId면 해제한다.
        /// </summary>
        public bool SetDefaultPreset(string entryId, string presetId)
        {
            ReloadIfExternallyChanged();
            CharacterLibraryEntry entry = FindById(entryId);
            if (entry == null)
            {
                return false;
            }

            entry.defaultPresetId = presetId ?? string.Empty;
            Save();
            return true;
        }

        /// <summary>
        /// 프리셋 파일과 엔트리의 presetIds를 동기화한다.
        /// 목록에 없는 defaultPresetId는 해제한다.
        /// </summary>
        public void SyncPresetIds(string entryId, IEnumerable<string> presetIds)
        {
            ReloadIfExternallyChanged();
            CharacterLibraryEntry entry = FindById(entryId);
            if (entry == null)
            {
                return;
            }

            entry.presetIds = presetIds != null
                ? new List<string>(presetIds)
                : new List<string>();
            if (!string.IsNullOrEmpty(entry.defaultPresetId) &&
                !entry.presetIds.Contains(entry.defaultPresetId))
            {
                entry.defaultPresetId = string.Empty;
            }
            Save();
        }

        /// <summary>
        /// 엔트리의 썸네일 PNG 절대 경로. 없으면 빈 문자열.
    }
}
