using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fbx2Vmd.ClothPhysics
{
    /// <summary>
    /// 머리카락 체인을 부위별 스프링(SpringHairDynamics) 생성물로 만들고 찾음.
    /// </summary>
    internal static class SpringHairBackend
    {
        public const string ProductPrefix = "SpringHair_";

        public static bool IsGeneratedObject(Transform host, Transform candidate)
        {
            return candidate.parent == host &&
                candidate.name.StartsWith(ProductPrefix, StringComparison.Ordinal);
        }

        public static IEnumerable<SpringHairDynamics> FindProducts(Transform host)
        {
            foreach (Transform child in host)
                if (IsGeneratedObject(host, child) && child.gameObject.activeSelf &&
                    child.TryGetComponent(out SpringHairDynamics spring))
                    yield return spring;
        }

        public static int Build(Transform host, IEnumerable<HairChain> chains,
            BodyColliderBuilder.BuildResult body, float torso)
        {
            int rootCount = 0;
            var selected = SelectChains(chains).ToList();
            float unit = BodyUnit(torso);
            // 기본 자세 머리카락과 겹치지 않게 맞출 때 본 반경은 깊이 평균을 씀.
            float hitRadius = new SpringHairSettings(unit).AverageCollisionRadius;
            foreach (var part in selected.GroupBy(chain => chain.part))
            {
                var product = new GameObject(ProductPrefix + part.Key);
                product.SetActive(false);
                product.transform.SetParent(host, false);
                var spring = product.AddComponent<SpringHairDynamics>();
                var roots = part.Select(chain => chain.rootBone).ToList();
                var categories = HairPartColliderMapper.Map(part.Key,
                    part.Max(chain => chain.worldLength), torso);
                var groups = RestPoseColliderFit.BuildSpringGroups(
                    HairPartColliderMapper.Select(categories, body.byCategory),
                    RestPoseColliderFit.CollectRestSegments(roots), hitRadius, part.Key.ToString(), body.mirrorBones);
                spring.Configure(roots, groups);
                spring.SetBodyUnit(unit);
                product.SetActive(true);
                rootCount += roots.Count;
            }
            return rootCount;
        }

        private static IEnumerable<HairChain> SelectChains(IEnumerable<HairChain> chains)
        {
            var eligible = new Dictionary<Transform, HairChain>();
            foreach (var chain in chains)
            {
                var root = chain.rootBone;
                if (root == null || root.parent == null || root.childCount == 0)
                    continue;

                // 스프링은 지정 루트 아래 모든 본을 구동하므로 탐지에서 제외된 본을 보호함.
                var allowed = new HashSet<Transform>(chain.bones);
                if (root.GetComponentsInChildren<Transform>(true).All(allowed.Contains))
                    eligible.TryAdd(root, chain);
            }
            return eligible.Values.Where(chain => !eligible.Keys.Any(other =>
                other != chain.rootBone && chain.rootBone.IsChildOf(other)));
        }

        public static void ApplyBodyUnit(SpringHairDynamics spring, float torso)
        {
            spring.SetBodyUnit(BodyUnit(torso));
        }

        // 충돌 반경 기본값의 기준 체형 몸통 길이(m).
        private const float ReferenceTorsoLength = 0.6f;

        // 충돌 반경 기본값은 기준 체형 값이라 실제 체형 비율로 늘이거나 줄임.
        private static float BodyUnit(float torso)
        {
            return torso > 0f ? torso / ReferenceTorsoLength : 1f;
        }
    }
}
