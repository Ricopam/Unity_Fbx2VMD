using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Fbx2Vmd.FBXImporter;
using UnityEngine;
using VRM;

namespace Fbx2Vmd.CharacterLibrary
{
    /// <summary>
    /// 프리셋의 캡처/적용 코덱. [SerializeField] 원시 필드(bool/int/float/string/
    /// Vector3/enum)를 리플렉션으로 읽고 쓴다. 참조형 필드는 자동으로 제외돼
    /// 프리셋이 씬 객체를 잘못 연결하지 않는다.
    /// 섹션별 키 규칙:
    ///   retarget   = FBXVmdPipeline 필드명
    ///   placement  = "position"|"rotation"|"scale" (캐릭터 루트), "guard.&lt;필드&gt;" (유휴 가드)
    ///   physics    = "spring[&lt;i&gt;].&lt;필드&gt;" (VRMSpringBone 컴포넌트 순서)
    ///   appearance = 예약
    /// </summary>
    public static partial class CharacterPresetSnapshot
    {
        private const string GuardKeyPrefix = "guard.";
        private const string SpringKeyPrefix = "spring[";

        // retarget 섹션의 대상은 리타깃/배치 튜닝 필드다. 출력 경로·녹화·진단·
        // 로그 같은 파이프라인 전역 설정은 캐릭터 프리셋에 포함하지 않는다.
        private static readonly string[] RetargetExcludedSubstrings =
        {
            "Folder", "Output", "Path", "File",
            "Video", "Recording", "Diagnostic",
            "Smoke", "_log", "_show", "_debug",
            "StartDelay", "PlaybackSpeed",
        };

        // VRMSpringBone에서 프리셋 대상으로 삼는 필드(파일 포맷에 고정).
        private static readonly string[] SpringBoneFields =
        {
            "m_stiffnessForce",
            "m_gravityPower",
            "m_gravityDir",
            "m_dragForce",
            "m_hitRadius",
            "m_updateType",
        };

        public static CharacterPreset CreateEmpty(string name)
        {
            string now = DateTime.UtcNow.ToString("O");
            return new CharacterPreset
            {
                id = Guid.NewGuid().ToString("N"),
                name = string.IsNullOrWhiteSpace(name) ? "프리셋" : name.Trim(),
                createdAtUtc = now,
                updatedAtUtc = now,
            };
        }

        /// <summary>
        /// 파이프라인의 직렬화 원시 필드를 retarget 섹션에 기록한다.
        /// </summary>
        public static void CaptureRetarget(CharacterPreset preset, FBXVmdPipeline pipeline)
        {
            preset.sections.retarget.Clear();
            if (pipeline == null)
            {
                return;
            }
            CaptureFields(pipeline, string.Empty, preset.sections.retarget,
                IsRetargetFieldExcluded);
        }

        /// <summary>
        /// 캐릭터 루트의 배치와 유휴 가드 플래그를 placement 섹션에 기록한다.
        /// </summary>
        public static void CapturePlacement(
            CharacterPreset preset, GameObject characterRoot, TargetIdlePoseGuard guard)
        {
            preset.sections.placement.Clear();
            if (characterRoot != null)
            {
                Transform t = characterRoot.transform;
                preset.sections.placement.Add(Encode("position", t.position));
                preset.sections.placement.Add(Encode("rotation", t.rotation));
                preset.sections.placement.Add(Encode("scale", t.localScale));
            }
            if (guard != null)
            {
                CaptureFields(guard, GuardKeyPrefix, preset.sections.placement);
            }
        }

        /// <summary>
        /// VRMSpringBone 컴포넌트들의 스프링 파라미터를 physics 섹션에 기록한다.
        /// 인덱스 키라 같은 모델 파일에서만 의미가 있다(캐릭터 귀속 프리셋이므로 성립).
        /// </summary>
        public static void CapturePhysics(CharacterPreset preset, GameObject characterRoot)
        {
            preset.sections.physics.Clear();
            if (characterRoot == null)
            {
                return;
            }

            VRMSpringBone[] springs = characterRoot.GetComponentsInChildren<VRMSpringBone>(true);
            for (int i = 0; i < springs.Length; i++)
            {
                for (int f = 0; f < SpringBoneFields.Length; f++)
                {
                    FieldInfo field = typeof(VRMSpringBone).GetField( SpringBoneFields[f], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field == null)
                    {
                        continue;
                    }
                    if (TryEncode(field.GetValue(springs[i]), field.FieldType, out string type, out string value))
                    {
                        preset.sections.physics.Add(new PresetFieldValue
                        {
                            key = $"spring[{i}].{SpringBoneFields[f]}", type = type, value = value, });
                    }
                }
            }
        }

        /// <summary>
        /// 프리셋을 파이프라인/캐릭터 루트/가드에 적용한다.
        /// 없는 필드·해석 실패 값은 건너뛰고 경고 목록에 모은다.
        /// </summary>
        public static List<string> Apply(
            CharacterPreset preset,
            FBXVmdPipeline pipeline,
            GameObject characterRoot,
            TargetIdlePoseGuard guard)
        {
            var warnings = new List<string>();
            if (preset == null || preset.sections == null)
            {
                return warnings;
            }
            return ApplySections(preset.sections, pipeline, characterRoot, guard, warnings);
        }

        /// <summary>
        /// 기본 섹션 위에 예외 섹션을 키 단위로 덮어쓴 병합본을 돌려준다.
        /// 예외에 없는 키는 기본값이 유지된다(캐릭터×FBX 예외의 상속 규칙).
        /// 입력 목록은 변경하지 않는다.
        /// </summary>
        public static CharacterPresetSections MergeSections(
            CharacterPresetSections baseSections, CharacterPresetSections overlaySections)
        {
            var merged = new CharacterPresetSections();
            MergeFieldList(merged.retarget,
                baseSections?.retarget, overlaySections?.retarget);
            MergeFieldList(merged.physics,
                baseSections?.physics, overlaySections?.physics);
            MergeFieldList(merged.appearance,
                baseSections?.appearance, overlaySections?.appearance);
            MergeFieldList(merged.placement,
                baseSections?.placement, overlaySections?.placement);
            return merged;
        }

        private static void MergeFieldList(
            List<PresetFieldValue> target,
            List<PresetFieldValue> baseList,
            List<PresetFieldValue> overlayList)
        {
            var indexByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            void AddOrReplace(List<PresetFieldValue> source)
            {
                if (source == null)
                {
                    return;
                }
                foreach (PresetFieldValue field in source)
                {
                    if (field == null || string.IsNullOrEmpty(field.key))
                    {
                        continue;
                    }
                    if (indexByKey.TryGetValue(field.key, out int existing))
                    {
                        target[existing] = field;
                    }
                    else
                    {
                        indexByKey[field.key] = target.Count;
                        target.Add(field);
                    }
                }
            }
            AddOrReplace(baseList);
            AddOrReplace(overlayList);
        }

        /// <summary>
        /// 섹션 값만 직접 적용한다. 캐릭터×FBX 예외처럼 프리셋 id 없이
        /// 값 묶음만 덮어쓸 때 사용한다. 기본 프리셋 뒤에 호출하면
        /// 예외에 있는 값만 덮어쓰는 상속 효과가 난다.
        /// </summary>
        public static List<string> ApplySections(
            CharacterPresetSections sections,
            FBXVmdPipeline pipeline,
            GameObject characterRoot,
            TargetIdlePoseGuard guard,
            List<string> warnings = null)
        {
            warnings = warnings ?? new List<string>();
            if (sections == null)
            {
                return warnings;
            }

#if UNITY_EDITOR
            RecordApplyUndo(pipeline, characterRoot, guard);
#endif

            if (pipeline != null)
            {
                ApplyFields(pipeline, string.Empty, sections.retarget, warnings);
            }

            ApplyPlacement(sections.placement, characterRoot, guard, warnings);
            ApplyPhysics(sections.physics, characterRoot, warnings);

            // placement가 루트 변환을 바꿨으면 유휴 가드의 베이스라인을 재캡처한다.
            // 그렇지 않으면 Play 중 다음 프레임에 가드가 캐릭터를 이전 자세로 되돌린다.
            if (pipeline != null && sections.placement != null &&
                sections.placement.Count > 0)
            {
                pipeline.RebindIdlePoseGuard();
            }
            // appearance 섹션은 예약 — 읽기만 하고 적용하지 않는다.
            return warnings;
        }

#if UNITY_EDITOR
        // 에디트 모드 적용은 되돌리기(Ctrl+Z)가 가능하게 대상을 Undo에 기록한다.
        private static void RecordApplyUndo(
            FBXVmdPipeline pipeline, GameObject characterRoot,
            TargetIdlePoseGuard guard)
        {
            if (Application.isPlaying)
            {
                return;
            }
            var undoTargets = new List<UnityEngine.Object>();
            if (pipeline != null)
            {
                undoTargets.Add(pipeline);
            }
            if (characterRoot != null)
            {
                undoTargets.Add(characterRoot.transform);
                undoTargets.AddRange(
                    characterRoot.GetComponentsInChildren<VRMSpringBone>(true));
            }
            if (guard != null)
            {
                undoTargets.Add(guard);
            }
            if (undoTargets.Count > 0)
            {
                UnityEditor.Undo.RecordObjects(
                    undoTargets.ToArray(), "캐릭터 프리셋 적용");
            }
        }
#endif

        private static void ApplyPlacement(
            List<PresetFieldValue> values, GameObject root,
            TargetIdlePoseGuard guard, List<string> warnings)
        {
            if (values == null)
            {
                return;
            }

            for (int i = 0; i < values.Count; i++)
            {
                PresetFieldValue field = values[i];
                if (TryApplyGuardKey(field, guard, warnings))
                {
                    continue;
                }

                if (root == null)
                {
                    continue;
                }

                ApplyRootField(root, field);
            }
        }

        private static void ApplyRootField(GameObject root, PresetFieldValue field)
        {
            if (field.key == "position" && TryDecode(field, out Vector3 position))
            {
                root.transform.position = position;
            }
            else if (field.key == "rotation" && TryDecode(field, out Quaternion rotation))
            {
                root.transform.rotation = rotation;
            }
            else if (field.key == "scale" && TryDecode(field, out Vector3 scale))
            {
                root.transform.localScale = scale;
            }
        }

        private static bool TryApplyGuardKey(
            PresetFieldValue field, TargetIdlePoseGuard guard, List<string> warnings)
        {
            if (!field.key.StartsWith(GuardKeyPrefix, StringComparison.Ordinal))
            {
                return false;
            }
            if (guard != null)
            {
                ApplyFieldValue(
                    guard, field.key.Substring(GuardKeyPrefix.Length), field, warnings);
            }
            else
            {
                warnings.Add($"유휴 가드가 없어 건너뜀: {field.key}");
            }
            return true;
        }

        private static void ApplyPhysics(
            List<PresetFieldValue> values, GameObject root, List<string> warnings)
        {
            if (values == null || values.Count == 0 || root == null)
            {
                return;
            }

            VRMSpringBone[] springs = root.GetComponentsInChildren<VRMSpringBone>(true);
            for (int i = 0; i < values.Count; i++)
            {
                PresetFieldValue field = values[i];
                int open = field.key.IndexOf('[');
                int close = field.key.IndexOf(']');
                int dot = field.key.IndexOf('.', close < 0 ? 0 : close);
                if (open < 0 || close <= open || dot <= close || !field.key.StartsWith(SpringKeyPrefix, StringComparison.Ordinal) || !int.TryParse(field.key.Substring(open + 1, close - open - 1),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                {
                    warnings.Add($"알 수 없는 물리 키: {field.key}");
                    continue;
                }

                if (index < 0 || index >= springs.Length)
                {
                    warnings.Add($"스프링 인덱스 범위 밖: {field.key}");
                    continue;
                }

                string fieldName = field.key.Substring(dot + 1);
                ApplyFieldValue(springs[index], fieldName, field, warnings);
            }
        }
    }
}

