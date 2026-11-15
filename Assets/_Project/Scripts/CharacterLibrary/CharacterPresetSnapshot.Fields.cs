using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Fbx2Vmd.FBXImporter;
using UnityEngine;
using VRM;

namespace Fbx2Vmd.CharacterLibrary
{
    public static partial class CharacterPresetSnapshot
    {

        /// <summary>
        /// 대상 객체의 [SerializeField] 원시 필드를 값 목록에 기록한다.
        /// </summary>
        private static void CaptureFields(
            object target, string keyPrefix, List<PresetFieldValue> output,
            Func<string, bool> excludeField = null)
        {
            FieldInfo[] fields = target.GetType().GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (!field.IsDefined(typeof(SerializeField), inherit: true))
                {
                    continue;
                }
                if (excludeField != null && excludeField(field.Name))
                {
                    continue;
                }
                if (TryEncode(field.GetValue(target), field.FieldType,
                        out string type, out string value))
                {
                    output.Add(new PresetFieldValue
                    {
                        key = keyPrefix + field.Name,
                        type = type,
                        value = value,
                    });
                }
            }
        }

        private static void ApplyFields(
            object target, string keyPrefix, List<PresetFieldValue> values, List<string> warnings)
        {
            if (values == null || target == null)
            {
                return;
            }
            for (int i = 0; i < values.Count; i++)
            {
                PresetFieldValue field = values[i];
                string name = string.IsNullOrEmpty(keyPrefix)
                    ? field.key
                    : field.key.StartsWith(keyPrefix, StringComparison.Ordinal)
                        ? field.key.Substring(keyPrefix.Length)
                        : field.key;
                ApplyFieldValue(target, name, field, warnings);
            }
        }

        private static void ApplyFieldValue(
            object target, string fieldName, PresetFieldValue field, List<string> warnings)
        {
            FieldInfo info = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (info == null || !info.IsDefined(typeof(SerializeField), inherit: true))
            {
                warnings.Add($"필드 없음: {fieldName}");
                return;
            }
            // 저장된 enum 타입명과 현재 필드 타입이 다르면 정수 매핑이 어긋나므로 건너뛴다.
            if (IsEnumTypeMismatch(info, field, out string storedType))
            {
                warnings.Add(
                    $"enum 타입 불일치: {fieldName} ({storedType} != {info.FieldType.FullName})");
                return;
            }
            if (!TryDecodeFor(field, info.FieldType, out object value))
            {
                warnings.Add($"값 해석 실패: {fieldName}={field.value}");
                return;
            }
            try
            {
                info.SetValue(target, value);
            }
            catch (Exception)
            {
                warnings.Add($"타입 불일치: {fieldName}");
            }
        }

        private static bool IsEnumTypeMismatch(
            FieldInfo info, PresetFieldValue field, out string storedType)
        {
            storedType = null;
            if (!info.FieldType.IsEnum ||
                !field.type.StartsWith(PresetFieldType.EnumPrefix, StringComparison.Ordinal))
            {
                return false;
            }
            storedType = field.type.Substring(PresetFieldType.EnumPrefix.Length);
            return !string.Equals(storedType, info.FieldType.FullName, StringComparison.Ordinal);
        }

        private static bool TryEncode(object raw, Type fieldType, out string type, out string value)
        {
            if (TryEncodeScalar(raw, fieldType, out type, out value))
            {
                return true;
            }
            if (fieldType == typeof(Vector3))
            {
                type = PresetFieldType.Vector3;
                Vector3 v = (Vector3)raw;
                value = string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}", v.x, v.y, v.z);
            }
            else if (fieldType == typeof(Quaternion))
            {
                type = PresetFieldType.Quaternion;
                Quaternion q = (Quaternion)raw;
                value = string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R},{3:R}",
                    q.x, q.y, q.z, q.w);
            }
            else if (fieldType.IsEnum)
            {
                type = PresetFieldType.EnumPrefix + fieldType.FullName;
                value = Convert.ToInt32(raw, CultureInfo.InvariantCulture)
                    .ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                return false;
            }
            return true;
        }

        private static bool TryEncodeScalar(
            object raw, Type fieldType, out string type, out string value)
        {
            type = string.Empty;
            value = string.Empty;
            if (fieldType == typeof(bool))
            {
                type = PresetFieldType.Bool;
                value = (bool)raw ? "true" : "false";
            }
            else if (fieldType == typeof(int))
            {
                type = PresetFieldType.Int;
                value = ((int)raw).ToString(CultureInfo.InvariantCulture);
            }
            else if (fieldType == typeof(float))
            {
                type = PresetFieldType.Float;
                value = ((float)raw).ToString("R", CultureInfo.InvariantCulture);
            }
            else if (fieldType == typeof(string))
            {
                type = PresetFieldType.String;
                value = (string)raw ?? string.Empty;
            }
            else
            {
                return false;
            }
            return true;
        }

        private static bool TryDecodeFor(PresetFieldValue field, Type fieldType, out object value)
        {
            if (TryDecodeScalar(field, out value))
            {
                return true;
            }
            if (field.type == PresetFieldType.Vector3)
            {
                if (TryDecode(field, out Vector3 v)) { value = v; return true; }
            }
            else if (field.type == PresetFieldType.Quaternion)
            {
                if (TryDecode(field, out Quaternion q)) { value = q; return true; }
            }
            else if (field.type.StartsWith(PresetFieldType.EnumPrefix, StringComparison.Ordinal) &&
                     fieldType.IsEnum)
            {
                if (int.TryParse(field.value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int e))
                {
                    value = Enum.ToObject(fieldType, e);
                    return true;
                }
            }
            return false;
        }

        private static bool TryDecodeScalar(PresetFieldValue field, out object value)
        {
            value = null;
            if (field.type == PresetFieldType.Bool)
            {
                if (bool.TryParse(field.value, out bool b)) { value = b; return true; }
            }
            else if (field.type == PresetFieldType.Int)
            {
                if (int.TryParse(field.value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out int n)) { value = n; return true; }
            }
            else if (field.type == PresetFieldType.Float)
            {
                if (float.TryParse(field.value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float f)) { value = f; return true; }
            }
            else if (field.type == PresetFieldType.String)
            {
                value = field.value;
                return true;
            }
            return false;
        }

        private static bool IsRetargetFieldExcluded(string fieldName)
        {
            for (int i = 0; i < RetargetExcludedSubstrings.Length; i++)
            {
                if (fieldName.IndexOf(RetargetExcludedSubstrings[i],
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static PresetFieldValue Encode(string key, Vector3 v)
        {
            return new PresetFieldValue
            {
                key = key,
                type = PresetFieldType.Vector3,
                value = string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R}", v.x, v.y, v.z),
            };
        }

        private static PresetFieldValue Encode(string key, Quaternion q)
        {
            return new PresetFieldValue
            {
                key = key,
                type = PresetFieldType.Quaternion,
                value = string.Format(CultureInfo.InvariantCulture, "{0:R},{1:R},{2:R},{3:R}",
                    q.x, q.y, q.z, q.w),
            };
        }

        private static bool TryDecode(PresetFieldValue field, out Vector3 v)
        {
            v = default;
            if (field.type != PresetFieldType.Vector3)
            {
                return false;
            }
            string[] parts = field.value.Split(',');
            if (parts.Length != 3 ||
                !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                return false;
            }
            v = new Vector3(x, y, z);
            return true;
        }

        private static bool TryDecode(PresetFieldValue field, out Quaternion q)
        {
            q = default;
            if (field.type != PresetFieldType.Quaternion)
            {
                return false;
            }
            string[] parts = field.value.Split(',');
            if (parts.Length != 4 ||
                !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) ||
                !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float w))
            {
                return false;
            }
            q = new Quaternion(x, y, z, w);
            return true;
        }
    }
}
