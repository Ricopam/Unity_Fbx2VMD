using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class RetargetingMuscleReferencePolicy
    {

        private static bool IsForearmStretchMuscle(string normalizedMuscleName)
        {
            return normalizedMuscleName.Contains("forearm") &&
                normalizedMuscleName.Contains("stretch");
        }

        private static bool IsLowerBodyMuscle(string muscleName)
        {
            string normalized = NormalizeMuscleName(muscleName);
            return normalized.Contains("upperleg") ||
                   normalized.Contains("lowerleg") ||
                   normalized.Contains("foot") ||
                   normalized.Contains("toes");
        }

        private static bool IsLegTwistOrInOutMuscle(string muscleName)
        {
            string normalized = NormalizeMuscleName(muscleName);
            bool isLeg = normalized.Contains("upperleg") ||
                         normalized.Contains("lowerleg") ||
                         normalized.Contains("foot");
            return isLeg &&
                (normalized.Contains("inout") || normalized.Contains("twist"));
        }

        private static bool IsRightArmMuscle(string muscleName)
        {
            return IsArmMuscle(muscleName, "right");
        }

        private static bool IsLeftArmMuscle(string muscleName)
        {
            return IsArmMuscle(muscleName, "left");
        }

        private static bool IsArmMuscle(string muscleName, string side)
        {
            string normalized = NormalizeMuscleName(muscleName);
            if (!normalized.Contains(side) || ContainsFingerName(normalized))
            {
                return false;
            }

            return normalized.Contains("shoulder") ||
                   normalized.Contains("arm") ||
                   normalized.Contains("forearm");
        }

        private static bool IsRightSleeveChainMuscle(string muscleName)
        {
            string normalized = NormalizeMuscleName(muscleName);
            if (normalized.Contains("spine") ||
                normalized.Contains("chest") ||
                normalized.Contains("upperchest"))
            {
                return true;
            }

            if (!normalized.Contains("right") ||
                ContainsFingerName(normalized) ||
                normalized.Contains("hand"))
            {
                return false;
            }

            return normalized.Contains("shoulder") ||
                   normalized.Contains("arm") ||
                   normalized.Contains("forearm");
        }

        private static bool ContainsFingerName(string normalizedMuscleName)
        {
            return normalizedMuscleName.Contains("thumb") ||
                   normalizedMuscleName.Contains("index") ||
                   normalizedMuscleName.Contains("middle") ||
                   normalizedMuscleName.Contains("ring") ||
                   normalizedMuscleName.Contains("little");
        }

        private static bool IsLeftUpperArmTwistMuscle(string normalizedMuscleName)
        {
            return !string.IsNullOrEmpty(normalizedMuscleName) &&
                normalizedMuscleName.Contains("left") &&
                normalizedMuscleName.Contains("arm") &&
                normalizedMuscleName.Contains("twist") &&
                !normalizedMuscleName.Contains("forearm");
        }

        private static bool IsRightUpperArmTwistMuscle(string normalizedMuscleName)
        {
            return !string.IsNullOrEmpty(normalizedMuscleName) &&
                normalizedMuscleName.Contains("right") &&
                normalizedMuscleName.Contains("arm") &&
                normalizedMuscleName.Contains("twist") &&
                !normalizedMuscleName.Contains("forearm");
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

    }
}
