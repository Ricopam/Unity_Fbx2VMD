using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Fbx2Vmd.Profiling;

namespace Fbx2Vmd.FBXImporter
{
    public static partial class HumanoidAvatarBuilder
    {
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

            var requiredBoneNames = CollectRequiredBoneNames();
            int mappedCount = MapRequiredBones(
                lookup, allTransforms, requiredBoneNames, mapping);

            Debug.Log($"[AvatarBuilder] 자동 본 매핑: {mappedCount}/{requiredBoneNames.Count}개 필수 본 매핑됨.");
            return mapping;
        }

        private static List<string> CollectRequiredBoneNames()
        {
            var requiredBoneNames = new List<string>();
            for (int i = 0; i < HumanTrait.BoneCount; i++)
            {
                if (HumanTrait.RequiredBone(i))
                {
                    requiredBoneNames.Add(HumanTrait.BoneName[i]);
                }
            }
            return requiredBoneNames;
        }

        private static int MapRequiredBones(
            TransformNameLookup lookup,
            Transform[] allTransforms,
            List<string> requiredBoneNames,
            Dictionary<string, string> mapping)
        {
            int mappedCount = 0;
            foreach (string humanBoneName in requiredBoneNames)
            {
                if (TryAutoMapRequiredBone(
                        lookup, allTransforms, humanBoneName,
                        out Transform foundBone, out _))
                {
                    mapping[humanBoneName] = foundBone.name;
                    mappedCount++;
                }
            }
            return mappedCount;
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

            RecordAllMatches(diagnostic, explicitMapping, lookup);

            diagnostic.RequiredMatchedCount = TallyBoneMatches(
                diagnostic, RequiredBones, diagnostic.MissingRequiredBones);
            diagnostic.FingerMatchedCount = TallyBoneMatches(
                diagnostic, FingerBones, diagnostic.MissingFingerBones);

            return diagnostic;
        }

        private static void RecordAllMatches(
            BoneMappingDiagnostic diagnostic,
            Dictionary<string, string> explicitMapping,
            TransformNameLookup lookup)
        {
            foreach (var kvp in explicitMapping)
            {
                string humanBoneName = kvp.Key;
                string targetBoneName = kvp.Value;
                bool matched = TryFindMappedTransform(
                    lookup, targetBoneName,
                    out Transform foundBone, out string matchKind);
                RecordMappingMatch(
                    diagnostic, humanBoneName, targetBoneName,
                    foundBone, matched, matchKind);
            }
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

    }
}
