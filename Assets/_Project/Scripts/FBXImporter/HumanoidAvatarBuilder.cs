using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Fbx2Vmd.Profiling;

namespace Fbx2Vmd.FBXImporter
{
    public static partial class HumanoidAvatarBuilder
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

    }
}
