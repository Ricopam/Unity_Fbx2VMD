using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Fbx2Vmd.Profiling;

namespace Fbx2Vmd.FBXImporter
{
    public static class HumanoidAvatarBuilder
    {
        private static readonly string[] RequiredBones = { "Hips", "Spine", "Head", "LeftUpperArm", "RightUpperArm", "LeftUpperLeg", "RightUpperLeg" };

        private static readonly string[] FingerBones =
        {
            "LeftThumbProximal",
            "LeftThumbIntermediate",
            "LeftThumbDistal",
            "LeftIndexProximal",
            "LeftIndexIntermediate",
            "LeftIndexDistal",
            "LeftMiddleProximal",
            "LeftMiddleIntermediate",
            "LeftMiddleDistal",
            "LeftRingProximal",
            "LeftRingIntermediate",
            "LeftRingDistal",
            "LeftLittleProximal",
            "LeftLittleIntermediate",
            "LeftLittleDistal",
            "RightThumbProximal",
            "RightThumbIntermediate",
            "RightThumbDistal",
            "RightIndexProximal",
            "RightIndexIntermediate",
            "RightIndexDistal",
            "RightMiddleProximal",
            "RightMiddleIntermediate",
            "RightMiddleDistal",
            "RightRingProximal",
            "RightRingIntermediate",
            "RightRingDistal",
            "RightLittleProximal",
            "RightLittleIntermediate",
            "RightLittleDistal"
        };

        public sealed class BoneMappingDiagnostic
        {
            public int MappingCount;
            public int MatchedCount;
            public int ExactMatchCount;
            public int NormalizedMatchCount;
            public int AliasMatchCount;
            public int RequiredTotal;
            public int RequiredMatchedCount;
            public int FingerTotal;
            public int FingerMatchedCount;
            public List<string> MissingRequiredBones = new List<string>();
            public List<string> MissingFingerBones = new List<string>();
            public List<BoneMappingMatch> Matches = new List<BoneMappingMatch>();
        }

        public sealed class BoneMappingMatch
        {
            public string HumanBoneName;
            public string TargetBoneName;
            public string MatchedBoneName;
            public string MatchKind;
        }

        public static void SetupHumanoid(GameObject targetRoot, Dictionary<string, string> explicitMapping)
        {
            using var perfScope = PerfScope.Measure("HumanoidAvatarBuilder.SetupHumanoid");
            Animator animator = targetRoot.GetComponent<Animator>();
            if (animator == null) animator = targetRoot.AddComponent<Animator>();

            if (explicitMapping == null || explicitMapping.Count == 0)
            {
                Debug.LogWarning("[AvatarBuilder] 본 매핑이 비어 있습니다. 자동 매핑 폴백 시도.");
                explicitMapping = BuildAutoMapping(targetRoot);
            }

            // 수동 교정 없이 순수 데이터로 아바타 생성
            Avatar newAvatar = CreatePureAvatar(targetRoot, explicitMapping);
            
            animator.avatar = newAvatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        /// <summary>
        /// BoneMapping_Data.txt가 없거나 리그가 다른 FBX를 처리할 때를 대비해, Transform 이름 기반으로 Humanoid 필수 본들을 자동 매핑합니다.
        /// - HumanTrait.RequiredBone 기준(최소 필수셋)만 대상으로 시도합니다.
        /// - 실패해도 예외를 던지지 않으며, 찾은 항목만 반환합니다.
        /// </summary>
        public static Dictionary<string, string> BuildAutoMapping(GameObject targetRoot)
        {
            var mapping = new Dictionary<string, string>();
            if (targetRoot == null)
            {
                return mapping;
            }

            TransformNameLookup lookup = BuildTransformNameLookup(targetRoot.transform);
            Transform[] allTransforms = targetRoot.GetComponentsInChildren<Transform>(true);

            var requiredBoneNames = new List<string>();
            for (int i = 0; i < HumanTrait.BoneCount; i++)
            {
                if (HumanTrait.RequiredBone(i))
                {
                    requiredBoneNames.Add(HumanTrait.BoneName[i]);
                }
            }

            int mappedCount = 0;
            foreach (string humanBoneName in requiredBoneNames)
            {
                if (TryAutoMapRequiredBone(lookup, allTransforms, humanBoneName, out Transform foundBone, out _))
                {
                    mapping[humanBoneName] = foundBone.name;
                    mappedCount++;
                }
            }

            Debug.Log($"[AvatarBuilder] 자동 본 매핑: {mappedCount}/{requiredBoneNames.Count}개 필수 본 매핑됨.");
            return mapping;
        }

        public static BoneMappingDiagnostic AnalyzeMapping(GameObject targetRoot, Dictionary<string, string> explicitMapping)
        {
            var diagnostic = new BoneMappingDiagnostic();
            if (targetRoot == null || explicitMapping == null)
            {
                return diagnostic;
            }

            TransformNameLookup lookup = BuildTransformNameLookup(targetRoot.transform);
            diagnostic.MappingCount = explicitMapping.Count;
            diagnostic.RequiredTotal = RequiredBones.Length;
            diagnostic.FingerTotal = FingerBones.Length;

            foreach (var kvp in explicitMapping)
            {
                string humanBoneName = kvp.Key;
                string targetBoneName = kvp.Value;
                Transform foundBone;
                string matchKind;
                bool matched = TryFindMappedTransform(lookup, targetBoneName, out foundBone, out matchKind);
                RecordMappingMatch(diagnostic, humanBoneName, targetBoneName, foundBone, matched, matchKind);
            }

            diagnostic.RequiredMatchedCount = TallyBoneMatches(
                diagnostic, RequiredBones, diagnostic.MissingRequiredBones);
            diagnostic.FingerMatchedCount = TallyBoneMatches(
                diagnostic, FingerBones, diagnostic.MissingFingerBones);

            return diagnostic;
        }

        private static void RecordMappingMatch(
            BoneMappingDiagnostic diagnostic,
            string humanBoneName, string targetBoneName,
            Transform foundBone, bool matched, string matchKind)
        {
            diagnostic.Matches.Add(new BoneMappingMatch
            {
                HumanBoneName = humanBoneName,
                TargetBoneName = targetBoneName,
                MatchedBoneName = matched && foundBone != null ? foundBone.name : "",
                MatchKind = matchKind
            });

            if (!matched)
            {
                return;
            }

            diagnostic.MatchedCount++;
            if (matchKind == "exact")
            {
                diagnostic.ExactMatchCount++;
            }
            else if (matchKind == "normalized")
            {
                diagnostic.NormalizedMatchCount++;
            }
            else if (matchKind == "alias")
            {
                diagnostic.AliasMatchCount++;
            }
        }

        private static int TallyBoneMatches(
            BoneMappingDiagnostic diagnostic, string[] bones,
            List<string> missingList)
        {
            int matchedCount = 0;
            foreach (string bone in bones)
            {
                bool matched = diagnostic.Matches.Any(match => match.HumanBoneName == bone && match.MatchKind != "missing");
                if (matched)
                {
                    matchedCount++;
                }
                else
                {
                    missingList.Add(bone);
                }
            }
            return matchedCount;
        }

        private static Avatar CreatePureAvatar(GameObject root, Dictionary<string, string> mappingData)
        {
            // 스마트 매핑
            var boneMap = SmartMapTransforms(root.transform, mappingData);
            if (boneMap.Count == 0) Debug.LogError("매핑 실패");

            // HumanDescription 생성
            // Assimp의 MakeLeftHanded가 이미 좌표계를 맞췄으므로, 
            // 현재 상태(Bind Pose)를 그대로 신뢰합니다.

            // HumanDescription 생성
            HumanDescription description = new HumanDescription
            {
                // 현재 트랜스폼 상태 그대로 스켈레톤 생성
                skeleton = CreateSkeleton(root.transform), 
                
                human = boneMap.Select(kvp => new HumanBone { 
                    humanName = NormalizeHumanBoneName(kvp.Key),
                    boneName = kvp.Value.name,
                    limit = new HumanLimit { useDefaultValues = true } 
                }).ToArray(),
                
                // 근육 설정 (기본값)
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f,
                feetSpacing = 0f, hasTranslationDoF = false
            };

            return AvatarBuilder.BuildHumanAvatar(root, description);
        }

        public static string NormalizeHumanBoneName(string humanName)
        {
            if (string.IsNullOrWhiteSpace(humanName))
            {
                return humanName;
            }

            string normalizedInput = NormalizeBoneName(humanName);
            for (int i = 0; i < HumanTrait.BoneCount; i++)
            {
                string unityBoneName = HumanTrait.BoneName[i];
                if (unityBoneName == humanName || NormalizeBoneName(unityBoneName) == normalizedInput)
                {
                    return unityBoneName;
                }
            }

            return humanName;
        }

        private static string NormalizeBoneName(string value)
        {
            return Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]", "");
        }

        // 트랜스폼 계층구조를 그대로 가져오는 함수
        private static SkeletonBone[] CreateSkeleton(Transform root)
        {
            var bones = new List<SkeletonBone>();
            AddBonesRecursive(root, bones);
            return bones.ToArray();
        }

        private static void AddBonesRecursive(Transform t, List<SkeletonBone> bones)
        {
            bones.Add(new SkeletonBone {
                name = t.name,
                position = t.localPosition,
                rotation = t.localRotation, // 원본 데이터 그대로 사용
                scale = t.localScale
            });
            foreach (Transform child in t) AddBonesRecursive(child, bones);
        }

        // 스마트 매핑: 정확한 이름 매칭 우선, 실패 시 normalized 매칭 시도
        private static Dictionary<string, Transform> SmartMapTransforms(Transform root, Dictionary<string, string> nameData)
        {
            var result = new Dictionary<string, Transform>();
            TransformNameLookup lookup = BuildTransformNameLookup(root);

            foreach (var kvp in nameData)
            {
                string humanBoneName = kvp.Key;   // 예: "Hips"
                string targetBoneName = kvp.Value; // 예: "13.joint_HipMaster"
                Transform foundBone;
                string matchKind;

                if (TryFindMappedTransform(lookup, targetBoneName, out foundBone, out matchKind))
                {
                    result[humanBoneName] = foundBone;
                    if (matchKind == "normalized")
                    {
                        Debug.Log($"Fallback 매칭 성공: {humanBoneName} -> {foundBone.name} (원본: {targetBoneName})");
                    }
                    else if (matchKind == "alias")
                    {
                        Debug.Log($"Rig 별칭 매칭 성공: {humanBoneName} -> {foundBone.name} (원본: {targetBoneName})");
                    }
                    continue;
                }
                
                // 매칭 실패 로그
                Debug.LogWarning($"본 매핑 실패: {humanBoneName} (대상: {targetBoneName})");
            }
            
            // 필수 본 체크 및 로그
            bool hasCriticalMissing = false;
            foreach (var bone in RequiredBones)
            {
                if (!result.ContainsKey(bone))
                {
                    Debug.LogError($"필수 본 누락: {bone}");
                    hasCriticalMissing = true;
                }
            }
            
            // [진단 도구] 매핑 실패 시 실제 계층 구조 덤프
            if (hasCriticalMissing)
            {
                Debug.LogError("========== [진단] FBX 계층 구조 덤프 시작 ==========");
                PrintHierarchy(root, 0);
                Debug.LogError("========== [진단] FBX 계층 구조 덤프 종료 ==========");
            }
            
            Debug.Log($"[AvatarBuilder] 매핑 완료: {result.Count}/{nameData.Count} 본 발견");
            return result;
        }

        private static bool TryAutoMapRequiredBone(
            TransformNameLookup lookup,
            Transform[] allTransforms,
            string humanBoneName,
            out Transform foundBone,
            out string matchKind)
        {
            foundBone = null;
            matchKind = "missing";

            IEnumerable<string> candidates = GetAutoMapCandidatesForHumanBone(humanBoneName);
            int bestRank = -1;
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                if (!TryFindMappedTransform(lookup, candidate, out Transform candidateBone, out string candidateKind))
                {
                    continue;
                }

                int rank = RankMatchKind(candidateKind);
                if (rank > bestRank)
                {
                    foundBone = candidateBone;
                    matchKind = candidateKind;
                    bestRank = rank;
                    if (bestRank >= 3)
                    {
                        return true;
                    }
                }
            }

            if (foundBone != null)
            {
                return true;
            }

            foreach (string candidate in candidates)
            {
                if (TryFindUniqueContainsMatch(allTransforms, candidate, out Transform containsMatch))
                {
                    foundBone = containsMatch;
                    matchKind = "contains";
                    return true;
                }
            }

            return false;
        }

        private static int RankMatchKind(string matchKind)
        {
            if (matchKind == "exact")
            {
                return 3;
            }

            if (matchKind == "normalized")
            {
                return 2;
            }

            if (matchKind == "alias")
            {
                return 1;
            }

            return 0;
        }

        private static IEnumerable<string> GetAutoMapCandidatesForHumanBone(string humanBoneName)
        {
            yield return humanBoneName;
            foreach (string alternate in GetAutoMapAlternates(humanBoneName))
            {
                yield return alternate;
            }
        }

        // Common alternative naming found in FBX rigs (Mixamo/Bip/Blender 등)
        private static readonly Dictionary<string, string[]> AutoMapAlternateNames =
            new Dictionary<string, string[]>
        {
            { "Hips", new[] { "Pelvis", "Hip", "Bip001 Pelvis", "Bip01 Pelvis" } },
            { "Spine", new[] { "Spine1", "Spine01", "Bip001 Spine", "Bip001 Spine1" } },
            { "Chest", new[] { "Spine2", "Spine02", "UpperChest", "Bip001 Spine2" } },
            { "LeftShoulder", new[] { "LeftClavicle", "Clavicle_L", "Shoulder_L", "Bip001 L Clavicle" } },
            { "RightShoulder", new[] { "RightClavicle", "Clavicle_R", "Shoulder_R", "Bip001 R Clavicle" } },
            { "LeftUpperArm", new[] { "LeftArm", "UpperArm_L", "Arm_L", "Bip001 L UpperArm" } },
            { "RightUpperArm", new[] { "RightArm", "UpperArm_R", "Arm_R", "Bip001 R UpperArm" } },
            { "LeftLowerArm", new[] { "LeftForeArm", "Forearm_L", "LowerArm_L", "Bip001 L Forearm" } },
            { "RightLowerArm", new[] { "RightForeArm", "Forearm_R", "LowerArm_R", "Bip001 R Forearm" } },
            { "LeftHand", new[] { "LeftWrist", "Hand_L", "Wrist_L", "Bip001 L Hand" } },
            { "RightHand", new[] { "RightWrist", "Hand_R", "Wrist_R", "Bip001 R Hand" } },
            { "LeftUpperLeg", new[] { "LeftUpLeg", "LeftThigh", "UpperLeg_L", "Thigh_L", "Bip001 L Thigh" } },
            { "RightUpperLeg", new[] { "RightUpLeg", "RightThigh", "UpperLeg_R", "Thigh_R", "Bip001 R Thigh" } },
            { "LeftLowerLeg", new[] { "LeftLeg", "LeftCalf", "LowerLeg_L", "Calf_L", "Bip001 L Calf" } },
            { "RightLowerLeg", new[] { "RightLeg", "RightCalf", "LowerLeg_R", "Calf_R", "Bip001 R Calf" } },
            { "LeftFoot", new[] { "LeftAnkle", "Foot_L", "Ankle_L", "Bip001 L Foot" } },
            { "RightFoot", new[] { "RightAnkle", "Foot_R", "Ankle_R", "Bip001 R Foot" } },
            { "LeftToes", new[] { "LeftToe", "Toe_L", "Toes_L", "Bip001 L Toe0" } },
            { "RightToes", new[] { "RightToe", "Toe_R", "Toes_R", "Bip001 R Toe0" } },
        };

        private static IEnumerable<string> GetAutoMapAlternates(string humanBoneName)
        {
            string[] alternates;
            return AutoMapAlternateNames.TryGetValue(humanBoneName, out alternates)
                ? alternates
                : EmptyAlternateNames;
        }

        private static readonly string[] EmptyAlternateNames = new string[0];


        private static bool TryFindUniqueContainsMatch(Transform[] allTransforms, string targetBoneName, out Transform foundBone)
        {
            foundBone = null;
            string needle = NormalizeBoneName(targetBoneName);
            if (string.IsNullOrEmpty(needle))
            {
                return false;
            }

            Transform best = null;
            int bestScore = -1;
            bool ambiguous = false;

            foreach (Transform t in allTransforms)
            {
                string hay = NormalizeBoneName(t.name);
                if (string.IsNullOrEmpty(hay))
                {
                    continue;
                }

                int score = GetNameMatchScore(hay, needle);

                if (score <= 0)
                {
                    continue;
                }

                if (best == null || score > bestScore)
                {
                    best = t;
                    bestScore = score;
                    ambiguous = false;
                }
                else if (score == bestScore && best != t)
                {
                    ambiguous = true;
                }
            }

            if (best == null || ambiguous)
            {
                return false;
            }

            foundBone = best;
            return true;
        }

        private static int GetNameMatchScore(string hay, string needle)
        {
            if (hay == needle)
            {
                return 300;
            }
            if (hay.EndsWith(needle))
            {
                return 200;
            }
            if (hay.Contains(needle))
            {
                return 100;
            }
            return -1;
        }

        internal static bool TryFindUniqueMappedTransform(Transform[] bones, string targetName,
            out Transform foundBone)
        {
            foundBone = null;
            if (bones == null || string.IsNullOrWhiteSpace(targetName))
                return false;

            Transform[] matches = bones.Where(bone => bone.name == targetName).Take(2).ToArray();
            string normalized = NormalizeBoneName(targetName);
            // 비라틴 이름을 정규화한 빈 문자열끼리는 같은 본으로 취급하지 않음.
            if (matches.Length == 0 && !string.IsNullOrEmpty(normalized))
                matches = bones.Where(bone => NormalizeBoneName(bone.name) == normalized).Take(2).ToArray();
            if (matches.Length == 0)
            {
                var aliases = new HashSet<string>(BuildBoneNameAliases(targetName));
                matches = bones.Where(bone => aliases.Overlaps(BuildBoneNameAliases(bone.name)))
                    .Take(2).ToArray();
            }

            // 임포터에 저장할 이름은 순회 순서와 관계없이 하나로 결정될 때만 허용함.
            if (matches.Length != 1)
                return false;
            foundBone = matches[0];
            return true;
        }

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
