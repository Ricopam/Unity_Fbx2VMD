using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Fbx2Vmd.Profiling;

namespace Fbx2Vmd.FBXImporter
{
    public static partial class HumanoidAvatarBuilder
    {
        private sealed class TransformNameLookup
        {
            public Dictionary<string, Transform> ExactNameMap = new Dictionary<string, Transform>();
            public Dictionary<string, Transform> NormalizedMap = new Dictionary<string, Transform>();
            public Dictionary<string, Transform> NormalizedAliasMap = new Dictionary<string, Transform>();
        }

        private static TransformNameLookup BuildTransformNameLookup(Transform root)
        {
            var lookup = new TransformNameLookup();
            var allTransforms = root.GetComponentsInChildren<Transform>(true);

            foreach (var t in allTransforms)
            {
                if (!lookup.ExactNameMap.ContainsKey(t.name))
                {
                    lookup.ExactNameMap[t.name] = t;
                }

                string cleanName = NormalizeBoneName(t.name);
                if (!lookup.NormalizedMap.ContainsKey(cleanName))
                {
                    lookup.NormalizedMap[cleanName] = t;
                }

                foreach (string alias in BuildBoneNameAliases(t.name))
                {
                    if (!lookup.NormalizedAliasMap.ContainsKey(alias))
                    {
                        lookup.NormalizedAliasMap[alias] = t;
                    }
                }
            }

            return lookup;
        }

        private static bool TryFindMappedTransform(
            TransformNameLookup lookup,
            string targetBoneName,
            out Transform foundBone,
            out string matchKind)
        {
            if (lookup.ExactNameMap.TryGetValue(targetBoneName, out foundBone))
            {
                matchKind = "exact";
                return true;
            }

            string cleanTarget = NormalizeBoneName(targetBoneName);
            if (lookup.NormalizedMap.TryGetValue(cleanTarget, out foundBone))
            {
                matchKind = "normalized";
                return true;
            }

            if (TryFindAliasMatch(lookup.NormalizedAliasMap, targetBoneName, out foundBone))
            {
                matchKind = "alias";
                return true;
            }

            foundBone = null;
            matchKind = "missing";
            return false;
        }

        private static bool TryFindAliasMatch(
            Dictionary<string, Transform> normalizedAliasMap,
            string targetBoneName,
            out Transform foundBone)
        {
            foreach (string alias in BuildBoneNameAliases(targetBoneName))
            {
                if (normalizedAliasMap.TryGetValue(alias, out foundBone))
                {
                    return true;
                }
            }

            foundBone = null;
            return false;
        }

        private static IEnumerable<string> BuildBoneNameAliases(string boneName)
        {
            if (string.IsNullOrWhiteSpace(boneName))
            {
                yield break;
            }

            var aliases = new HashSet<string>();
            AddAlias(aliases, boneName);

            string withoutNamespace = StripNamespace(boneName);
            AddAlias(aliases, withoutNamespace);

            string withoutRigPrefix = StripKnownRigPrefix(withoutNamespace);
            AddAlias(aliases, withoutRigPrefix);

            foreach (string alias in aliases)
            {
                yield return alias;
            }
        }

        private static void AddAlias(HashSet<string> aliases, string value)
        {
            string normalized = NormalizeBoneName(value);
            if (!string.IsNullOrEmpty(normalized))
            {
                aliases.Add(normalized);
            }
        }

        private static string StripNamespace(string value)
        {
            int namespaceIndex = value.LastIndexOf(':');
            return namespaceIndex >= 0 && namespaceIndex < value.Length - 1
                ? value[(namespaceIndex + 1)..]
                : value;
        }

        private static string StripKnownRigPrefix(string value)
        {
            string[] prefixes =
            {
                "Skeleton_",
                "Skeleton",
                "mixamorig:",
                "mixamorig_",
                "mixamorig"
            };

            foreach (string prefix in prefixes)
            {
                if (value.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                {
                    return value[prefix.Length..].TrimStart('_', ':', ' ');
                }
            }

            return value;
        }
        
        /// <summary>
        /// 진단용: Transform 계층 구조를 콘솔에 출력
        /// </summary>
        private static void PrintHierarchy(Transform t, int depth)
        {
            string indent = new string(' ', depth * 2);
            Debug.Log($"{indent}└─ {t.name}");
            foreach (Transform child in t)
            {
                PrintHierarchy(child, depth + 1);
            }
        }
    }
}
