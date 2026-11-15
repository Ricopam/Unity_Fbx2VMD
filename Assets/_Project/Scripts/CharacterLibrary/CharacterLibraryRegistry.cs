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
        private readonly CharacterLibraryStore _store;
        private readonly Func<string, VrmCharacterMetaResult> _metaReader;
        // 엔트리별 프리셋 스토어를 재사용해 LastReadFailed 보호가 읽기-쓰기에 걸쳐 유지되게 한다.
        private readonly Dictionary<string, CharacterPresetStore> _presetStores =
            new Dictionary<string, CharacterPresetStore>(StringComparer.Ordinal);
        private CharacterLibraryDocument _document;
        private DateTime _knownWriteTimeUtc = DateTime.MinValue;

        public CharacterLibraryRegistry(
            string libraryFilePath = null,
            Func<string, VrmCharacterMetaResult> metaReader = null)
        {
            _store = new CharacterLibraryStore(libraryFilePath);
            _metaReader = metaReader ?? CharacterFileMetaReader.Read;
        }

        public string LibraryFilePath => _store.LibraryFilePath;

        public IReadOnlyList<CharacterLibraryEntry> Entries => Document.entries;

        private CharacterLibraryDocument Document
        {
            get
            {
                if (_document == null)
                {
                    _document = _store.LoadOrCreateDefault();
                    _knownWriteTimeUtc = _store.ResolveLastWriteTimeUtc();
                }
                return _document;
            }
        }

        /// <summary>
        /// 다른 인스턴스(창/매니저 등)가 같은 파일을 저장했을 때 캐시를 버리고 다시 읽는다.
        /// 쓰기 전에 호출해 read-modify-write 덮어쓰기를 막는다.
        /// </summary>
        private void ReloadIfExternallyChanged()
        {
            if (_document == null)
            {
                return;
            }

            DateTime current = _store.ResolveLastWriteTimeUtc();
            if (current != _knownWriteTimeUtc)
            {
                _document = _store.LoadOrCreateDefault();
                _knownWriteTimeUtc = current;
            }
        }

        public string ActiveCharacterId => Document.activeCharacterId;

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

        public CharacterLibraryEntry FindById(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            for (int i = 0; i < Document.entries.Count; i++)
            {
                CharacterLibraryEntry entry = Document.entries[i];
                if (string.Equals(entry.id, id, StringComparison.Ordinal))
                {
                    return entry;
                }
            }
            return null;
        }

        public CharacterLibraryEntry FindBySourcePath(string normalizedPath)
        {
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return null;
            }

            for (int i = 0; i < Document.entries.Count; i++)
            {
                CharacterLibraryEntry entry = Document.entries[i];
                if (string.Equals(entry.sourcePath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }
            return null;
        }

        /// <summary>
        /// 캐릭터 파일(VRM/FBX/PMX)을 라이브러리에 등록한다.
        /// 성공 시 엔트리, 실패 시 entry=null과 한국어 오류 메시지를 돌려준다.
        /// </summary>
        public sealed class RegisterResult
        {
            public CharacterLibraryEntry entry;
            public string error = string.Empty;
            public bool success => entry != null && string.IsNullOrEmpty(error);
        }
    }
}
