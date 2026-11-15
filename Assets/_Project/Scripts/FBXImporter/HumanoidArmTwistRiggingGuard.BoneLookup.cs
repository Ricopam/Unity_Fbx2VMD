using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 계층 탐색·본 이름 매칭 등 트랜스폼 조회 전담 분할.
    public partial class HumanoidArmTwistRiggingGuard
    {
        private static Transform GetOrCreateChild(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            if (child != null)
            {
                return child;
            }

            GameObject childObject = new GameObject(childName);
            child = childObject.transform;
            child.SetParent(parent, false);
            return child;
        }

        private List<Transform> FindExactChildren(Transform root, params string[] names)
        {
            var results = new List<Transform>();
            if (root == null || names == null)
            {
                return results;
            }

            foreach (string exactName in names)
            {
                Transform found = FindDescendantByName(root, exactName);
                if (found != null && !results.Contains(found))
                {
                    results.Add(found);
                }
            }

            return results;
        }

        private static Transform FindDescendantByName(Transform root, string targetName)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child != root && IsMatchingBoneName(child.name, targetName))
                {
                    return child;
                }
            }

            return null;
        }

        private static bool IsMatchingBoneName(string boneName, string targetName)
        {
            if (string.IsNullOrEmpty(boneName) || string.IsNullOrEmpty(targetName))
            {
                return false;
            }

            return boneName == targetName ||
                   boneName.EndsWith("." + targetName, System.StringComparison.Ordinal) ||
                   boneName.EndsWith(targetName, System.StringComparison.Ordinal);
        }
    }
}
