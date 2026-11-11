using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // SetHumanPose 근육 슬롯 — 소스 정의 순서 유지
    internal enum HumanMuscleSlot
    {
        LeftShoulderFrontBack,
        LeftArmTwist,
        RightForearmStretch,
        LeftForearmStretch,
        RightArmTwist,
        LeftUpperLegFrontBack,
        RightUpperLegFrontBack,
        LeftLowerLegStretch,
        RightLowerLegStretch,
        LeftFootUpDown,
        RightFootUpDown,
        SpineFrontBack,
        SpineLeftRight,
        SpineTwistLeftRight,
        ChestFrontBack,
        ChestLeftRight,
        ChestTwistLeftRight,
        UpperChestFrontBack,
        UpperChestLeftRight,
        UpperChestTwistLeftRight,
        LeftUpperLegInOut,
        RightUpperLegInOut,
        LeftUpperLegTwistInOut,
        RightUpperLegTwistInOut,
        LeftLowerLegTwistInOut,
        RightLowerLegTwistInOut,
        LeftFootTwistInOut,
        RightFootTwistInOut,
        LeftToesUpDown,
        RightToesUpDown,
    }

    // HumanTrait 근육 이름 토큰 기반 인덱스 해석 캐시
    internal sealed class RetargetingMuscleIndexMap
    {
        private const int UnresolvedHumanMuscleIndex = -2;

        // HumanMuscleSlot 순서와 1:1 대응하는 탐색 토큰표
        private static readonly string[][] SlotTokens =
        {
            new[] { "left", "shoulder", "frontback" },
            new[] { "left", "arm", "twist" },
            new[] { "right", "forearm", "stretch" },
            new[] { "left", "forearm", "stretch" },
            new[] { "right", "arm", "twist" },
            new[] { "left", "upperleg", "frontback" },
            new[] { "right", "upperleg", "frontback" },
            new[] { "left", "lowerleg", "stretch" },
            new[] { "right", "lowerleg", "stretch" },
            new[] { "left", "foot", "updown" },
            new[] { "right", "foot", "updown" },
            new[] { "spine", "frontback" },
            new[] { "spine", "leftright" },
            new[] { "spine", "twist" },
            new[] { "chest", "frontback" },
            new[] { "chest", "leftright" },
            new[] { "chest", "twist" },
            new[] { "upperchest", "frontback" },
            new[] { "upperchest", "leftright" },
            new[] { "upperchest", "twist" },
            new[] { "left", "upperleg", "inout" },
            new[] { "right", "upperleg", "inout" },
            new[] { "left", "upperleg", "twist" },
            new[] { "right", "upperleg", "twist" },
            new[] { "left", "lowerleg", "twist" },
            new[] { "right", "lowerleg", "twist" },
            new[] { "left", "foot", "twist" },
            new[] { "right", "foot", "twist" },
            new[] { "left", "toes", "updown" },
            new[] { "right", "toes", "updown" },
        };

        private readonly int[] _resolvedIndices = CreateUnresolvedIndexArray();

        internal int Get(HumanMuscleSlot slot)
        {
            int index = (int)slot;
            if (_resolvedIndices[index] == UnresolvedHumanMuscleIndex)
            {
                _resolvedIndices[index] = FindHumanMuscleIndexByTokens(SlotTokens[index]);
            }

            return _resolvedIndices[index];
        }

        internal float Read(HumanPose pose, HumanMuscleSlot slot)
        {
            return ReadHumanMuscleValue(pose, Get(slot));
        }

        internal RetargetingStageMuscleSnapshot ReadStageSnapshot(HumanPose pose)
        {
            return new RetargetingStageMuscleSnapshot
            {
                LeftShoulderFrontBack = Read(pose, HumanMuscleSlot.LeftShoulderFrontBack),
                LeftArmTwist = Read(pose, HumanMuscleSlot.LeftArmTwist),
                LeftForearmStretch = Read(pose, HumanMuscleSlot.LeftForearmStretch),
                RightForearmStretch = Read(pose, HumanMuscleSlot.RightForearmStretch),
                RightArmTwist = Read(pose, HumanMuscleSlot.RightArmTwist),
            };
        }

        private static int[] CreateUnresolvedIndexArray()
        {
            int[] indices = new int[SlotTokens.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                indices[i] = UnresolvedHumanMuscleIndex;
            }

            return indices;
        }

        private static float ReadHumanMuscleValue(HumanPose pose, int muscleIndex)
        {
            if (pose.muscles == null || muscleIndex < 0 || muscleIndex >= pose.muscles.Length)
            {
                return float.NaN;
            }

            float value = pose.muscles[muscleIndex];
            return IsFinite(value) ? value : float.NaN;
        }

        private static int FindHumanMuscleIndexByTokens(params string[] tokens)
        {
            if (tokens == null || tokens.Length == 0)
            {
                return -1;
            }

            for (int i = 0; i < HumanTrait.MuscleCount; i++)
            {
                string normalized = NormalizeHumanMuscleName(HumanTrait.MuscleName[i]);
                bool matched = true;
                foreach (string token in tokens)
                {
                    if (!normalized.Contains(NormalizeHumanMuscleName(token)))
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return i;
                }
            }

            return -1;
        }

        private static string NormalizeHumanMuscleName(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace(" ", string.Empty)
                    .Replace(".", string.Empty)
                    .Replace("-", string.Empty)
                    .Replace("_", string.Empty)
                    .ToLowerInvariant();
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
