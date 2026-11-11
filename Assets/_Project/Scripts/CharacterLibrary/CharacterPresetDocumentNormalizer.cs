using System;
using System.Collections.Generic;

namespace Fbx2Vmd.CharacterLibrary
{
    /// <summary>
    /// 프리셋 문서의 null/손상 필드를 기본값으로 정규화한다.
    /// 규칙은 CharacterLibraryDocumentNormalizer와 같다:
    /// null은 빈 값, 빈 id는 새 GUID, 중복 id는 첫 항목만 유지한다.
    /// </summary>
    public static class CharacterPresetDocumentNormalizer
    {
        public static CharacterPresetDocument Normalize(CharacterPresetDocument document)
        {
            if (document == null)
            {
                return new CharacterPresetDocument();
            }

            if (document.schemaVersion <= 0)
            {
                document.schemaVersion = 1;
            }
            document.characterId = NormalizeString(document.characterId);
            document.updatedAtUtc = NormalizeString(document.updatedAtUtc);
            if (document.presets == null)
            {
                document.presets = new List<CharacterPreset>();
            }

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            // 앞에서부터 순회해 중복 id는 첫 항목만 유지한다(역순이면 마지막이 남는다).
            for (int i = document.presets.Count - 1; i >= 0; i--)
            {
                CharacterPreset preset = document.presets[i];
                if (preset == null)
                {
                    document.presets.RemoveAt(i);
                }
            }
            for (int i = 0; i < document.presets.Count; i++)
            {
                CharacterPreset preset = document.presets[i];
                preset.id = NormalizeString(preset.id);
                if (preset.id.Length == 0)
                {
                    preset.id = Guid.NewGuid().ToString("N");
                }
                if (!seenIds.Add(preset.id))
                {
                    document.presets.RemoveAt(i);
                    i--;
                    continue;
                }

                preset.name = NormalizeString(preset.name);
                preset.createdAtUtc = NormalizeString(preset.createdAtUtc);
                preset.updatedAtUtc = NormalizeString(preset.updatedAtUtc);
                NormalizeSections(preset);
            }

            if (document.fbxOverrides == null)
            {
                document.fbxOverrides = new List<CharacterFbxOverride>();
            }
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = document.fbxOverrides.Count - 1; i >= 0; i--)
            {
                CharacterFbxOverride fbxOverride = document.fbxOverrides[i];
                if (fbxOverride == null)
                {
                    document.fbxOverrides.RemoveAt(i);
                }
            }
            for (int i = 0; i < document.fbxOverrides.Count; i++)
            {
                CharacterFbxOverride fbxOverride = document.fbxOverrides[i];
                fbxOverride.fbxKey =
                    CharacterLibraryRegistry.NormalizeFbxKey(fbxOverride.fbxKey);
                if (fbxOverride.fbxKey.Length == 0 || !seenKeys.Add(fbxOverride.fbxKey))
                {
                    document.fbxOverrides.RemoveAt(i);
                    i--;
                    continue;
                }
                fbxOverride.name = NormalizeString(fbxOverride.name);
                fbxOverride.updatedAtUtc = NormalizeString(fbxOverride.updatedAtUtc);
                NormalizeSections(fbxOverride);
            }
            return document;
        }

        private static void NormalizeSections(CharacterPreset preset)
        {
            if (preset.sections == null)
            {
                preset.sections = new CharacterPresetSections();
            }
            NormalizeSections(preset.sections);
        }

        private static void NormalizeSections(CharacterFbxOverride fbxOverride)
        {
            if (fbxOverride.sections == null)
            {
                fbxOverride.sections = new CharacterPresetSections();
            }
            NormalizeSections(fbxOverride.sections);
        }

        private static void NormalizeSections(CharacterPresetSections sections)
        {
            NormalizeSection(sections.retarget);
            sections.retarget = sections.retarget ?? new List<PresetFieldValue>();
            NormalizeSection(sections.physics);
            sections.physics = sections.physics ?? new List<PresetFieldValue>();
            NormalizeSection(sections.appearance);
            sections.appearance = sections.appearance ?? new List<PresetFieldValue>();
            NormalizeSection(sections.placement);
            sections.placement = sections.placement ?? new List<PresetFieldValue>();
        }

        private static void NormalizeSection(List<PresetFieldValue> values)
        {
            if (values == null)
            {
                return;
            }
            for (int i = values.Count - 1; i >= 0; i--)
            {
                PresetFieldValue field = values[i];
                if (field == null || string.IsNullOrEmpty(field.key))
                {
                    values.RemoveAt(i);
                    continue;
                }
                field.type = NormalizeString(field.type);
                field.value = NormalizeString(field.value);
            }
        }

        private static string NormalizeString(string value)
        {
            return value ?? string.Empty;
        }
    }
}
