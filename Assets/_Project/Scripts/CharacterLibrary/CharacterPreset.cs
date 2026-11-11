using System;
using System.Collections.Generic;

namespace Fbx2Vmd.CharacterLibrary
{
    /// <summary>
    /// 캐릭터 1개의 프리셋 묶음 JSON. 파일은 presets/&lt;characterId&gt;.json에 저장된다.
    /// </summary>
    [Serializable]
    public sealed class CharacterPresetDocument
    {
        public int schemaVersion = 1;
        public string characterId = string.Empty;
        public string updatedAtUtc = string.Empty;
        public List<CharacterPreset> presets = new List<CharacterPreset>();
        public List<CharacterFbxOverride> fbxOverrides = new List<CharacterFbxOverride>();
    }

    /// <summary>
    /// 캐릭터×FBX 예외 1개. 특정 (캐릭터, FBX) 조합에만 적용되는 덮어쓰기 스냅샷이다.
    /// fbxKey는 Import_FBX에 임포트된 FBX 파일명(예: "motion.fbx")이다.
    /// 파일명 기준 키라 같은 이름으로 다시 임포트해도 예외가 유지된다.
    /// 적용 순서는 기본 프리셋 → 예외라서 예외에 없는 값은 기본값을 따른다.
    /// </summary>
    [Serializable]
    public sealed class CharacterFbxOverride
    {
        public string fbxKey = string.Empty;
        public string name = string.Empty;
        public string updatedAtUtc = string.Empty;
        public CharacterPresetSections sections = new CharacterPresetSections();
    }

    /// <summary>
    /// 캐릭터별로 이름 붙여 저장하는 설정 스냅샷 1개.
    /// 섹션별 값 목록은 키-값 스키마라 버전 차이에 강하다(없는 키는 건너뛴다).
    /// </summary>
    [Serializable]
    public sealed class CharacterPreset
    {
        public string id = string.Empty;
        public string name = string.Empty;
        public string createdAtUtc = string.Empty;
        public string updatedAtUtc = string.Empty;
        public CharacterPresetSections sections = new CharacterPresetSections();
    }

    /// <summary>
    /// 프리셋 섹션. retarget=FBXVmdPipeline 직렬화 필드 스냅샷,
    /// physics=VRMSpringBone 컴포넌트 값, appearance=예약(재질/표정),
    /// placement=캐릭터 루트 배치 + 유휴 가드 플래그.
    /// </summary>
    [Serializable]
    public sealed class CharacterPresetSections
    {
        public List<PresetFieldValue> retarget = new List<PresetFieldValue>();
        public List<PresetFieldValue> physics = new List<PresetFieldValue>();
        public List<PresetFieldValue> appearance = new List<PresetFieldValue>();
        public List<PresetFieldValue> placement = new List<PresetFieldValue>();
    }

    /// <summary>
    /// 프리셋 값 1개. key는 섹션별 해석 규칙(필드명, "spring[i].필드",
    /// "position"/"rotation"/"scale"/"guard.필드")을 따른다.
    /// value는 InvariantCulture 문자열이다.
    /// </summary>
    [Serializable]
    public sealed class PresetFieldValue
    {
        public string key = string.Empty;
        public string type = string.Empty;
        public string value = string.Empty;
    }

    /// <summary>
    /// PresetFieldValue.type 문자열. enum은 "enum:" + Type.FullName(중첩 타입은 '+').
    /// 적용 시 저장된 타입명과 현재 필드의 enum 타입이 다르면 건너뛰고 경고한다.
    /// </summary>
    public static class PresetFieldType
    {
        public const string Bool = "bool";
        public const string Int = "int";
        public const string Float = "float";
        public const string String = "string";
        public const string Vector3 = "vector3";
        public const string Quaternion = "quaternion";
        public const string EnumPrefix = "enum:";
    }
}
