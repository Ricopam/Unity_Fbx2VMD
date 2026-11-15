using System;
using System.Collections.Generic;
using System.IO;
using Fbx2Vmd.CharacterLibrary;
using Fbx2Vmd.FBXImporter;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fbx2Vmd.Settings.EditorTools
{
    /// <summary>
    /// pendingCommand의 캐릭터 라이브러리 명령을 실행한다.
    /// 씬 조작은 Unity 측이 단일 기록자이므로 여기서만 수행한다.
    /// </summary>
    internal static class MainRecordingSettingsCharacterCommandProcessor
    {
        internal static bool IsCharacterAction(string action)
        {
            switch (action)
            {
                case MainRecordingSettingsCommandEnvelope.LoadCharacterAction:
                case MainRecordingSettingsCommandEnvelope.ApplyPresetAction:
                case MainRecordingSettingsCommandEnvelope.SavePresetAction:
                case MainRecordingSettingsCommandEnvelope.RestoreSceneCharacterAction:
                case MainRecordingSettingsCommandEnvelope.RegisterVrmAction:
                case MainRecordingSettingsCommandEnvelope.RemoveCharacterAction:
                case MainRecordingSettingsCommandEnvelope.RefreshCharacterAction:
                case MainRecordingSettingsCommandEnvelope.ReconnectCharacterAction:
                case MainRecordingSettingsCommandEnvelope.SetDefaultPresetAction:
                case MainRecordingSettingsCommandEnvelope.DeletePresetAction:
                case MainRecordingSettingsCommandEnvelope.RenamePresetAction:
                case MainRecordingSettingsCommandEnvelope.CancelLoadAction:
                case MainRecordingSettingsCommandEnvelope.CopyPresetAction:
                case MainRecordingSettingsCommandEnvelope.UpdatePresetAction:
                case MainRecordingSettingsCommandEnvelope.SaveFbxOverrideAction:
                case MainRecordingSettingsCommandEnvelope.ApplyFbxOverrideAction:
                case MainRecordingSettingsCommandEnvelope.DeleteFbxOverrideAction:
                    return true;
                default:
                    return false;
            }
        }

        internal static async System.Threading.Tasks.Task<string> ExecuteAsync(
            MainRecordingSettingsCommandEnvelope command)
        {
            CharacterLibraryCoordinator manager = CharacterLibraryCoordinator.EnsureExists();
            CharacterLibraryRegistry registry = manager.Registry;

            switch (command.action)
            {
                case MainRecordingSettingsCommandEnvelope.LoadCharacterAction:
                    return await LoadCharacterAsync(manager, command).ConfigureAwait(true);
                case MainRecordingSettingsCommandEnvelope.ApplyPresetAction:
                    return ApplyPreset(manager, command);
                case MainRecordingSettingsCommandEnvelope.SavePresetAction:
                    return SavePreset(manager, command);
                case MainRecordingSettingsCommandEnvelope.RestoreSceneCharacterAction:
                    return RestoreSceneCharacter(manager);
                case MainRecordingSettingsCommandEnvelope.RegisterVrmAction:
                    return RegisterVrm(registry, command);
                case MainRecordingSettingsCommandEnvelope.RemoveCharacterAction:
                    return RemoveCharacter(registry, command);
                case MainRecordingSettingsCommandEnvelope.RefreshCharacterAction:
                    return RefreshCharacter(registry, command);
                case MainRecordingSettingsCommandEnvelope.ReconnectCharacterAction:
                    return ReconnectCharacter(registry, command);
                case MainRecordingSettingsCommandEnvelope.SetDefaultPresetAction:
                    return SetDefaultPreset(registry, command);
                case MainRecordingSettingsCommandEnvelope.DeletePresetAction:
                    return DeletePreset(registry, command);
                case MainRecordingSettingsCommandEnvelope.RenamePresetAction:
                    return RenamePreset(registry, command);
                case MainRecordingSettingsCommandEnvelope.CancelLoadAction:
                    manager.CancelLoading();
                    return "캐릭터 로드를 취소했습니다.";
                case MainRecordingSettingsCommandEnvelope.CopyPresetAction:
                    return CopyPreset(registry, command);
                case MainRecordingSettingsCommandEnvelope.UpdatePresetAction:
                    return UpdatePreset(registry, command);
                case MainRecordingSettingsCommandEnvelope.SaveFbxOverrideAction:
                    return SaveFbxOverride(manager, command);
                case MainRecordingSettingsCommandEnvelope.ApplyFbxOverrideAction:
                    return ApplyFbxOverride(manager, command);
                case MainRecordingSettingsCommandEnvelope.DeleteFbxOverrideAction:
                    return DeleteFbxOverride(manager, command);
                default:
                    throw new InvalidOperationException($"알 수 없는 캐릭터 명령: {command.action}");
            }
        }

        private static async System.Threading.Tasks.Task<string> LoadCharacterAsync(
            CharacterLibraryCoordinator manager,
            MainRecordingSettingsCommandEnvelope command)
        {
            string entryId = RequireText(command.characterId, "characterId가 필요합니다.");
            bool ok = await manager.SelectCharacterAsync(entryId).ConfigureAwait(true);
            if (!ok)
            {
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(manager.LastError) ? "캐릭터 로드에 실패했습니다." : manager.LastError);
            }

            return $"캐릭터 로드 완료: {entryId}";
        }

        private static string ApplyPreset(
            CharacterLibraryCoordinator manager,
            MainRecordingSettingsCommandEnvelope command)
        {
            string entryId = RequireText(command.characterId, "characterId가 필요합니다.");
            string presetId = RequireText(command.presetId, "presetId가 필요합니다.");
            CharacterLibraryEntry entry = RequireEntry(manager.Registry, entryId);
            CharacterPreset preset = manager.Registry.FindPreset(entryId, presetId);
            if (preset == null)
            {
                throw new InvalidOperationException($"프리셋을 찾을 수 없습니다: {presetId}");
            }

            // 프리셋은 캐릭터 귀속이다 — 다른 캐릭터에 인덱스 기반 값을 쓰면 안 된다.
            FBXVmdPipeline pipeline = RequireLoadedCharacter(manager, entry.id, out GameObject root);

            TargetIdlePoseGuard guard = pipeline.GetComponent<TargetIdlePoseGuard>();
            var warnings = CharacterPresetSnapshot.Apply(preset, pipeline, root, guard);
            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(pipeline.gameObject.scene);
            }

            return warnings.Count == 0
                ? $"프리셋 적용: {preset.name}"
                : $"프리셋 적용(일부 {warnings.Count}건 미적용): {preset.name}";
        }

        private static string SavePreset(
            CharacterLibraryCoordinator manager,
            MainRecordingSettingsCommandEnvelope command)
        {
            string entryId = RequireText(command.characterId, "characterId가 필요합니다.");
            CharacterLibraryEntry entry = RequireEntry(manager.Registry, entryId);
            FBXVmdPipeline pipeline = RequireLoadedCharacter(manager, entry.id, out GameObject root);

            string presetName = ReadParams(command)?["presetName"]?.ToString();
            CharacterPresetDocument doc = manager.Registry.LoadPresets(entry.id);
            CharacterPreset preset = CharacterPresetSnapshot.CreateEmpty(
                string.IsNullOrWhiteSpace(presetName)
                    ? $"프리셋 {doc.presets.Count + 1}"
                    : presetName.Trim());
            TargetIdlePoseGuard guard = pipeline.GetComponent<TargetIdlePoseGuard>();
            CharacterPresetSnapshot.CaptureRetarget(preset, pipeline);
            CharacterPresetSnapshot.CapturePlacement(preset, root, guard);
            CharacterPresetSnapshot.CapturePhysics(preset, root);

            doc.presets.Add(preset);
            manager.Registry.OpenPresetStore(entry.id).Save(doc);
            // 프리셋 파일은 저장됐지만 라이브러리 측 동기화가 실패할 수 있으므로 분리한다.
            manager.Registry.SyncPresetIds(entry.id, doc.presets.ConvertAll(p => p.id));
            if (string.IsNullOrEmpty(entry.defaultPresetId))
            {
                manager.Registry.SetDefaultPreset(entry.id, preset.id);
            }

            return $"프리셋 저장: {preset.name} ({entry.displayName})";
        }

        private static string RestoreSceneCharacter(CharacterLibraryCoordinator manager)
        {
            if (!manager.RestoreSceneCharacter())
            {
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(manager.LastError) ? "씬 캐릭터 복원에 실패했습니다." : manager.LastError);
            }

            return "씬 캐릭터를 복원했습니다.";
        }

        private static string RegisterVrm(
            CharacterLibraryRegistry registry,
            MainRecordingSettingsCommandEnvelope command)
        {
            string path = RequireText(command.path, "캐릭터 파일 경로가 필요합니다.");
            CharacterLibraryRegistry.RegisterResult result = registry.Register(path);
            if (!result.success)
            {
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(result.error) ? "캐릭터 등록에 실패했습니다." : result.error);
            }

            return $"캐릭터 등록 완료: {result.entry.displayName}";
        }

        private static string RemoveCharacter(
            CharacterLibraryRegistry registry,
            MainRecordingSettingsCommandEnvelope command)
        {
            string entryId = RequireText(command.characterId, "characterId가 필요합니다.");
            if (!registry.Remove(entryId))
            {
                throw new InvalidOperationException($"라이브러리에 없는 캐릭터입니다: {entryId}");
            }

            return $"라이브러리에서 제거했습니다: {entryId}";
        }

        private static string RefreshCharacter(
            CharacterLibraryRegistry registry,
            MainRecordingSettingsCommandEnvelope command)
        {
            CharacterLibraryEntry entry = RequireEntry(
                registry, RequireText(command.characterId, "characterId가 필요합니다."));
            registry.RefreshEntry(entry);
            registry.Save();
            return $"파일 상태를 갱신했습니다: {entry.displayName} ({entry.status})";
        }

        private static string ReconnectCharacter(
            CharacterLibraryRegistry registry,
            MainRecordingSettingsCommandEnvelope command)
        {
            CharacterLibraryEntry entry = RequireEntry(
                registry, RequireText(command.characterId, "characterId가 필요합니다."));
            string path = RequireText(command.path, "새 파일 경로가 필요합니다.");
            if (!registry.TryRelink(entry, path, out string error))
            {
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(error) ? "파일 재연결에 실패했습니다." : error);
            }

            return $"파일을 다시 연결했습니다: {entry.displayName}";
        }

        private static string SetDefaultPreset(
            CharacterLibraryRegistry registry,
            MainRecordingSettingsCommandEnvelope command)
        {
            string entryId = RequireText(command.characterId, "characterId가 필요합니다.");
            string presetId = RequireText(command.presetId, "presetId가 필요합니다.");
            if (!registry.SetDefaultPreset(entryId, presetId))
            {
                throw new InvalidOperationException("기본 프리셋 지정에 실패했습니다.");
            }

            return $"기본 프리셋을 지정했습니다: {presetId}";
        }

        private static string DeletePreset(
            CharacterLibraryRegistry registry,
            MainRecordingSettingsCommandEnvelope command)
        {
            CharacterLibraryEntry entry = RequireEntry(
                registry, RequireText(command.characterId, "characterId가 필요합니다."));
            string presetId = RequireText(command.presetId, "presetId가 필요합니다.");
            CharacterPresetDocument doc = registry.LoadPresets(entry.id);
            int index = doc.presets.FindIndex(p => p.id == presetId);
            if (index < 0)
            {
                throw new InvalidOperationException($"프리셋을 찾을 수 없습니다: {presetId}");
            }

            string name = doc.presets[index].name;
            // SyncPresetIds가 목록 밖 기본 프리셋을 먼저 해제하므로 삭제 대상 여부를 미리 잡아둔다.
            bool wasDefault = entry.defaultPresetId == presetId;
            doc.presets.RemoveAt(index);
            registry.OpenPresetStore(entry.id).Save(doc);
            registry.SyncPresetIds(entry.id, doc.presets.ConvertAll(p => p.id));
            if (wasDefault)
            {
                registry.SetDefaultPreset(
                    entry.id, doc.presets.Count > 0 ? doc.presets[0].id : string.Empty);
            }

            return $"프리셋 삭제: {name}";
        }

        private static string RenamePreset(
            CharacterLibraryRegistry registry,
            MainRecordingSettingsCommandEnvelope command)
        {
            CharacterLibraryEntry entry = RequireEntry(
                registry, RequireText(command.characterId, "characterId가 필요합니다."));
            string presetId = RequireText(command.presetId, "presetId가 필요합니다.");
            string newName = RequireText(
                ReadParams(command)?["presetName"]?.ToString(), "새 프리셋 이름이 필요합니다.");
            CharacterPresetDocument doc = registry.LoadPresets(entry.id);
            CharacterPreset preset = doc.presets.Find(p => p.id == presetId);
            if (preset == null)
            {
                throw new InvalidOperationException($"프리셋을 찾을 수 없습니다: {presetId}");
            }

            preset.name = newName.Trim();
            preset.updatedAtUtc = DateTime.UtcNow.ToString("O");
            registry.OpenPresetStore(entry.id).Save(doc);
            return $"프리셋 이름 변경: {preset.name}";
        }

        private static string CopyPreset(
            CharacterLibraryRegistry registry,
            MainRecordingSettingsCommandEnvelope command)
        {
            CharacterLibraryEntry entry = RequireEntry(
                registry, RequireText(command.characterId, "characterId가 필요합니다."));
            string presetId = RequireText(command.presetId, "presetId가 필요합니다.");
            CharacterPresetDocument doc = registry.LoadPresets(entry.id);
            CharacterPreset source = doc.presets.Find(p => p.id == presetId);
            if (source == null)
            {
                throw new InvalidOperationException($"프리셋을 찾을 수 없습니다: {presetId}");
            }

            string copyName = ReadParams(command)?["presetName"]?.ToString();
            CharacterPreset copy = CharacterPresetSnapshot.CreateEmpty(
                string.IsNullOrWhiteSpace(copyName) ? $"{source.name} 복사본" : copyName);
            // 섹션 값은 소유 캐릭터에 귀속되므로 값만 복사하고 id·이름·시각은 새로 부여한다.
            // 목록/필드 객체를 공유하면 이후 병합이 원본을 오염시키므로 깊은 복사한다.
            copy.sections = new CharacterPresetSections
            {
                retarget = CopyFields(source.sections?.retarget),
                physics = CopyFields(source.sections?.physics),
                appearance = CopyFields(source.sections?.appearance),
                placement = CopyFields(source.sections?.placement)
            };
            doc.presets.Add(copy);
            registry.OpenPresetStore(entry.id).Save(doc);
            registry.SyncPresetIds(entry.id, doc.presets.ConvertAll(p => p.id));
            return $"프리셋 복제: {source.name} → {copy.name}";
        }

        private static string UpdatePreset(
            CharacterLibraryRegistry registry,
            MainRecordingSettingsCommandEnvelope command)
        {
            CharacterLibraryEntry entry = RequireEntry(
                registry, RequireText(command.characterId, "characterId가 필요합니다."));
            string presetId = RequireText(command.presetId, "presetId가 필요합니다.");
            CharacterPresetDocument doc = registry.LoadPresets(entry.id);
            CharacterPreset preset = doc.presets.Find(p => p.id == presetId);
            if (preset == null)
            {
                throw new InvalidOperationException($"프리셋을 찾을 수 없습니다: {presetId}");
            }

            JObject parameters = ReadParams(command);
            int updated = 0;
            string presetName = parameters?["presetName"]?.ToString();
            if (!string.IsNullOrWhiteSpace(presetName))
            {
                preset.name = presetName.Trim();
            }

            // params.sections.<section> = [{key,type,value}] 형태로 넘어온 항목을
            // 같은 key의 기존 필드에 덮어쓰거나 새로 추가한다.
            if (parameters?["sections"] is JObject sections)
            {
                updated += MergeSection(preset.sections.retarget, sections["retarget"]);
                updated += MergeSection(preset.sections.placement, sections["placement"]);
                updated += MergeSection(preset.sections.physics, sections["physics"]);
                updated += MergeSection(preset.sections.appearance, sections["appearance"]);
            }

            if (updated == 0 && string.IsNullOrWhiteSpace(presetName))
            {
                throw new InvalidOperationException("갱신할 프리셋 값이 없습니다.");
            }

            preset.updatedAtUtc = DateTime.UtcNow.ToString("O");
            registry.OpenPresetStore(entry.id).Save(doc);
            return $"프리셋 저장: {preset.name} ({updated}개 값 갱신)";
        }

        private static string SaveFbxOverride(
            CharacterLibraryCoordinator manager,
            MainRecordingSettingsCommandEnvelope command)
        {
            CharacterLibraryEntry entry = RequireEntry(
                manager.Registry, RequireText(command.characterId, "characterId가 필요합니다."));
            FBXVmdPipeline pipeline = RequireLoadedCharacter(manager, entry.id, out GameObject root);
            string fbxKey = ResolveFbxKey(command);
            if (fbxKey.Length == 0)
            {
                throw new InvalidOperationException(
                    "예외를 저장할 FBX가 없습니다. FBX를 임포트하거나 path를 지정하세요.");
            }

            // 현재 라이브 상태를 통째로 캡처해 이 조합의 예외 스냅샷으로 저장한다.
            CharacterPreset capture = CharacterPresetSnapshot.CreateEmpty(fbxKey);
            TargetIdlePoseGuard guard = pipeline.GetComponent<TargetIdlePoseGuard>();
            CharacterPresetSnapshot.CaptureRetarget(capture, pipeline);
            CharacterPresetSnapshot.CapturePlacement(capture, root, guard);
            CharacterPresetSnapshot.CapturePhysics(capture, root);

            string overrideName = ReadParams(command)?["presetName"]?.ToString();
            var fbxOverride = new CharacterFbxOverride
            {
                fbxKey = fbxKey,
                name = string.IsNullOrWhiteSpace(overrideName) ? fbxKey : overrideName.Trim(),
                sections = capture.sections,
            };
            manager.Registry.UpsertFbxOverride(entry.id, fbxOverride);
            return $"FBX 예외 저장: {fbxOverride.name} ({entry.displayName})";
        }

        private static string ApplyFbxOverride(
            CharacterLibraryCoordinator manager,
            MainRecordingSettingsCommandEnvelope command)
        {
            CharacterLibraryEntry entry = RequireEntry(
                manager.Registry, RequireText(command.characterId, "characterId가 필요합니다."));
            FBXVmdPipeline pipeline = RequireLoadedCharacter(manager, entry.id, out GameObject root);
            string fbxKey = ResolveFbxKey(command);
            CharacterFbxOverride fbxOverride = manager.Registry.FindFbxOverride(entry.id, fbxKey);
            if (fbxOverride == null)
            {
                throw new InvalidOperationException(
                    $"이 FBX의 예외가 없습니다: {(fbxKey.Length > 0 ? fbxKey : "(현재 FBX 없음)")}");
            }

            // 기본 프리셋을 상속 기반으로 삼아 예외 값만 키 단위로 덮어쓴 병합본을 한 번에 적용한다.
            CharacterPreset basePreset = string.IsNullOrEmpty(entry.defaultPresetId)
                ? null
                : manager.Registry.FindPreset(entry.id, entry.defaultPresetId);
            CharacterPresetSections merged = CharacterPresetSnapshot.MergeSections(
                basePreset?.sections, fbxOverride.sections);
            TargetIdlePoseGuard guard = pipeline.GetComponent<TargetIdlePoseGuard>();
            var warnings = CharacterPresetSnapshot.ApplySections(
                merged, pipeline, root, guard);
            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(pipeline.gameObject.scene);
            }

            return warnings.Count == 0
                ? $"FBX 예외 적용: {fbxOverride.name}"
                : $"FBX 예외 적용(일부 {warnings.Count}건 미적용): {fbxOverride.name}";
        }

        private static string DeleteFbxOverride(
            CharacterLibraryCoordinator manager,
            MainRecordingSettingsCommandEnvelope command)
        {
            CharacterLibraryEntry entry = RequireEntry(
                manager.Registry, RequireText(command.characterId, "characterId가 필요합니다."));
            FBXVmdPipeline pipeline = RequireLoadedCharacter(manager, entry.id, out GameObject root);
            string fbxKey = ResolveFbxKey(command);
            if (!manager.Registry.RemoveFbxOverride(entry.id, fbxKey))
            {
                throw new InvalidOperationException(
                    $"이 FBX의 예외가 없습니다: {(fbxKey.Length > 0 ? fbxKey : "(현재 FBX 없음)")}");
            }

            // 해제 후 덮어쓴 값이 남지 않게 기본 프리셋으로 복귀한다.
            bool restored = false;
            if (!string.IsNullOrEmpty(entry.defaultPresetId))
            {
                CharacterPreset basePreset = manager.Registry.FindPreset(
                    entry.id, entry.defaultPresetId);
                if (basePreset != null)
                {
                    TargetIdlePoseGuard guard = pipeline.GetComponent<TargetIdlePoseGuard>();
                    CharacterPresetSnapshot.Apply(basePreset, pipeline, root, guard);
                    restored = true;
                }
            }
            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(pipeline.gameObject.scene);
            }
            return restored
                ? $"FBX 예외 해제(기본 프리셋 복귀): {fbxKey}"
                : $"FBX 예외 해제: {fbxKey}";
        }

        /// <summary>
        /// 활성 라이브러리 캐릭터가 entry와 일치하는지 확인하고 파이프라인을 돌려준다.
        /// </summary>
        private static FBXVmdPipeline RequireLoadedCharacter(
            CharacterLibraryCoordinator manager, string entryId, out GameObject root)
        {
            FBXVmdPipeline pipeline = manager.BoundPipeline;
            root = CharacterLibrarySceneBinding.FindActiveLibraryRoot(pipeline);
            string activeId = root != null
                ? root.GetComponent<CharacterLibraryInstance>()?.entryId
                : null;
            if (pipeline == null || root == null || activeId != entryId)
            {
                throw new InvalidOperationException("이 캐릭터를 먼저 불러온 뒤 사용하세요.");
            }
            return pipeline;
        }

        /// <summary>
        /// 예외 키를 결정한다. 우선순위: command.path 파일명 → 설정 문서의
        /// 마지막 임포트 fbxPath 파일명 → Import_FBX에서 생성시각이 가장
        /// 최근인 .fbx(임포트는 File.Copy라 mtime이 원본을 따르므로 생성시각 사용).
        /// </summary>
        private static string ResolveFbxKey(MainRecordingSettingsCommandEnvelope command)
        {
            string key = CharacterLibraryRegistry.NormalizeFbxKey(command.path);
            if (key.Length > 0)
            {
                return key;
            }

            try
            {
                MainRecordingSettingsDocument document =
                    new MainRecordingSettingsStore().LoadOrCreateDefault();
                key = CharacterLibraryRegistry.NormalizeFbxKey(document?.fbxPath);
                if (key.Length > 0 && string.Equals(
                        Path.GetExtension(key), ".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    return key;
                }
            }
            catch (Exception)
            {
                // 설정 문서를 읽지 못해도 폴더 스캔으로 계속 진행한다.
            }

            string dir = Path.Combine(Application.dataPath, "Resources",
                FBXVmdPipeline.IMPORT_FBX_FOLDER);
            if (!Directory.Exists(dir))
            {
                return string.Empty;
            }
            string newest = null;
            DateTime stamp = DateTime.MinValue;
            foreach (string file in Directory.GetFiles(dir))
            {
                if (!string.Equals(Path.GetExtension(file), ".fbx",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                DateTime created = File.GetCreationTimeUtc(file);
                if (created > stamp)
                {
                    stamp = created;
                    newest = file;
                }
            }
            return CharacterLibraryRegistry.NormalizeFbxKey(newest);
        }

        private static int MergeSection(List<PresetFieldValue> target, JToken fields)
        {
            if (target == null || fields is not JArray array)
            {
                return 0;
            }

            int updated = 0;
            foreach (JToken item in array)
            {
                string key = item?["key"]?.ToString();
                string value = item?["value"]?.ToString();
                string type = item?["type"]?.ToString();
                if (string.IsNullOrWhiteSpace(key) || value == null)
                {
                    continue;
                }

                PresetFieldValue existing = target.Find(f => f.key == key);
                if (existing == null)
                {
                    target.Add(new PresetFieldValue
                    {
                        key = key,
                        type = string.IsNullOrWhiteSpace(type) ? PresetFieldType.String : type,
                        value = value
                    });
                    updated++;
                }
                else if (existing.value != value ||
                         (!string.IsNullOrWhiteSpace(type) && existing.type != type))
                {
                    existing.value = value;
                    if (!string.IsNullOrWhiteSpace(type))
                    {
                        existing.type = type;
                    }
                    updated++;
                }
            }

            return updated;
        }

        private static List<PresetFieldValue> CopyFields(List<PresetFieldValue> source)
        {
            if (source == null)
            {
                return new List<PresetFieldValue>();
            }

            return source.ConvertAll(
                f => new PresetFieldValue { key = f.key, type = f.type, value = f.value });
        }

        private static CharacterLibraryEntry RequireEntry(
            CharacterLibraryRegistry registry, string entryId)
        {
            CharacterLibraryEntry entry = registry.FindById(entryId);
            if (entry == null)
            {
                throw new InvalidOperationException($"라이브러리에 없는 캐릭터입니다: {entryId}");
            }

            return entry;
        }

        private static string RequireText(string value, string message)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(message);
            }

            return value.Trim();
        }

        private static JObject ReadParams(MainRecordingSettingsCommandEnvelope command)
        {
            if (string.IsNullOrWhiteSpace(command.paramsJson))
            {
                return null;
            }

            try
            {
                return JObject.Parse(command.paramsJson);
            }
            catch
            {
                return null;
            }
        }
    }
}
