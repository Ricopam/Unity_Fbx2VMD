using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Fbx2Vmd.Profiling;

namespace Fbx2Vmd.FBXImporter
{
    public static partial class HumanoidAvatarBuilder
    {
        private static Dictionary<string, Transform> SmartMapTransforms(Transform root, Dictionary<string, string> nameData)
        {
            var result = new Dictionary<string, Transform>();
            TransformNameLookup lookup = BuildTransformNameLookup(root);

            MapNamedBones(nameData, lookup, result);
            ValidateRequiredBones(result, root);

            Debug.Log($"[AvatarBuilder] 매핑 완료: {result.Count}/{nameData.Count} 본 발견");
            return result;
        }

        private static void MapNamedBones(
            Dictionary<string, string> nameData,
            TransformNameLookup lookup,
            Dictionary<string, Transform> result)
        {
            foreach (var kvp in nameData)
            {
                string humanBoneName = kvp.Key;
                string targetBoneName = kvp.Value;
                if (!TryFindMappedTransform(
                        lookup, targetBoneName,
                        out Transform foundBone, out string matchKind))
                {
                    // 매칭 실패 로그
                    Debug.LogWarning($"본 매핑 실패: {humanBoneName} (대상: {targetBoneName})");
                    continue;
                }
                result[humanBoneName] = foundBone;
                if (matchKind == "normalized")
                {
                    Debug.Log($"Fallback 매칭 성공: {humanBoneName} -> {foundBone.name} (원본: {targetBoneName})");
                }
                else if (matchKind == "alias")
                {
                    Debug.Log($"Rig 별칭 매칭 성공: {humanBoneName} -> {foundBone.name} (원본: {targetBoneName})");
                }
            }
        }

        private static void ValidateRequiredBones(
            Dictionary<string, Transform> result, Transform root)
        {
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
            if (TryFindBestRankedCandidate(
                    lookup, candidates, out foundBone, out matchKind))
            {
                return true;
            }
            if (TryFindContainsCandidate(
                    allTransforms, candidates, out foundBone, out matchKind))
            {
                return true;
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


        private static bool TryFindBestRankedCandidate(
            TransformNameLookup lookup,
            IEnumerable<string> candidates,
            out Transform foundBone,
            out string matchKind)
        {
            foundBone = null;
            matchKind = "missing";
            int bestRank = -1;
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }
                if (!TryFindMappedTransform(
                        lookup, candidate,
                        out Transform candidateBone,
                        out string candidateKind))
                {
                    continue;
                }
                int rank = RankMatchKind(candidateKind);
                if (TryUpdateBestCandidate(
                        rank, candidateBone, candidateKind,
                        ref bestRank, ref foundBone, ref matchKind))
                {
                    return true;
                }
            }
            return foundBone != null;
        }

        private static bool TryUpdateBestCandidate(
            int rank,
            Transform candidateBone,
            string candidateKind,
            ref int bestRank,
            ref Transform foundBone,
            ref string matchKind)
        {
            if (rank <= bestRank)
            {
                return false;
            }
            foundBone = candidateBone;
            matchKind = candidateKind;
            bestRank = rank;
            return bestRank >= 3;
        }

        private static bool TryFindContainsCandidate(
            Transform[] allTransforms,
            IEnumerable<string> candidates,
            out Transform foundBone,
            out string matchKind)
        {
            foreach (string candidate in candidates)
            {
                if (TryFindUniqueContainsMatch(
                        allTransforms, candidate, out Transform containsMatch))
                {
                    foundBone = containsMatch;
                    matchKind = "contains";
                    return true;
                }
            }
            foundBone = null;
            matchKind = null;
            return false;
        }

        private static bool TryFindUniqueContainsMatch(Transform[] allTransforms, string targetBoneName, out Transform foundBone)
        {
            foundBone = null;
            string needle = NormalizeBoneName(targetBoneName);
            if (string.IsNullOrEmpty(needle))
            {
                return false;
            }

            if (!TryFindBestNameMatch(allTransforms, needle, out Transform best))
            {
                return false;
            }

            foundBone = best;
            return true;
        }

        private static bool TryFindBestNameMatch(
            Transform[] allTransforms,
            string needle,
            out Transform best)
        {
            best = null;
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
            return best != null && !ambiguous;
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

    }
}
