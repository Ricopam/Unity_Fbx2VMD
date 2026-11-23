using System.Collections.Generic;
using System.Linq;
using MagicaCloth2;
using UnityEngine;
using VRM;

namespace Fbx2Vmd.ClothPhysics
{
    /// <summary>
    /// 기본 자세의 흔들림 본을 이미 덮고 있는 신체 콜라이더를 정리함.
    /// 겹친 채로 시작하면 첫 프레임부터 충돌이 본을 밀어내 넥타이가 부풀고
    /// 트윈테일이 수평으로 뻗으므로, 장식물 클로스와 머리카락 스프링 본은
    /// 기본 자세에 닿지 않을 만큼 반경을 줄인 전용 콜라이더를 씀.
    /// 기본 자세는 Setup 시점의 현재 자세로 측정함.
    /// </summary>
    public static class RestPoseColliderFit
    {
        /// <summary>부위별 스프링 콜라이더 그룹 오브젝트 접두사. 원본 콜라이더 아래에 두어 신체 콜라이더 정리 때 함께 제거됨.</summary>
        public const string SpringGroupPrefix = BodyColliderBuilder.AutoNamePrefix + "SpringFit_";

        /// <summary>장식물 전용 축소 콜라이더 접두사. 원본 콜라이더 아래에 두어 함께 제거됨.</summary>
        public const string AccessoryColliderPrefix = BodyColliderBuilder.AutoNamePrefix + "AccFit_";

        // 기본 자세와 콜라이더 표면 사이에 남길 여유(m).
        const float ClearanceMargin = 0.002f;
        // 이 비율보다 작게 줄어야 하는 구는 충돌 효과가 없으므로 뺌.
        const float MinKeptRadiusRatio = 0.3f;
        // 본 선분과 캡슐 축의 최단 거리를 근사할 표본 수.
        const int SegmentSamples = 8;

        /// <summary>루트 아래 각 본과 부모를 잇는 기본 자세 선분(월드). 루트 자신의 부모 선분은 고정부라 제외함.</summary>
        public static List<(Vector3 a, Vector3 b)> CollectRestSegments(IEnumerable<Transform> roots)
        {
            var segments = new List<(Vector3, Vector3)>();
            foreach (var root in roots.Where(r => r != null).Distinct())
                foreach (var bone in root.GetComponentsInChildren<Transform>(true))
                    if (bone != root)
                        segments.Add((bone.parent.position, bone.position));
            return segments;
        }

        /// <summary>
        /// 장식물 클로스용 콜라이더 목록. 기본 자세 선분을 이미 덮는 콜라이더는
        /// 반경을 줄인 전용 사본으로 바꿔 몸통 관통 방지는 유지하고, 너무 작아지면 뺌.
        /// 원본은 다른 클로스(스커트 등)와 공유하므로 바꾸지 않음. 바꾼 내역은 changes에 기록함.
        /// </summary>
        public static List<ColliderComponent> FitAccessoryColliders(
            IEnumerable<ColliderComponent> colliders, IReadOnlyList<(Vector3 a, Vector3 b)> segments,
            float particleRadius, List<string> changes = null)
        {
            var result = new List<ColliderComponent>();
            foreach (var collider in colliders)
            {
                if (collider == null)
                    continue;
                var (start, end, radius) = WorldCapsule(collider);
                float clearance = MinDistanceToCapsule(segments, start, end) - particleRadius - ClearanceMargin;
                if (clearance >= radius)
                {
                    result.Add(collider);
                    continue;
                }
                if (clearance < radius * MinKeptRadiusRatio)
                {
                    changes?.Add($"{collider.name} 제외");
                    continue;
                }
                result.Add(CreateShrunkCopy(collider, clearance));
                changes?.Add($"{collider.name} 반경 {radius:F3}→{clearance:F3}m");
            }
            return result;
        }

        // 축 선분(양끝 구 중심)은 그대로 두고 반경만 줄인 사본을 원본 아래에 만듦.
        static ColliderComponent CreateShrunkCopy(ColliderComponent source, float worldRadius)
        {
            var go = new GameObject(AccessoryColliderPrefix + source.name);
            go.transform.SetParent(source.transform, false);
            float radius = worldRadius / Mathf.Max(0.0001f, MaxScale(source.transform));
            var size = source.GetSize();
            ColliderComponent copy;
            if (source is MagicaCapsuleCollider capsule)
            {
                var c = go.AddComponent<MagicaCapsuleCollider>();
                c.direction = capsule.direction;
                c.reverseDirection = capsule.reverseDirection;
                c.alignedOnCenter = capsule.alignedOnCenter;
                // 길이에서 양끝 반경을 뺀 축 길이를 보존함.
                c.SetSize(radius, radius, size.z - size.x - size.y + radius * 2f);
                copy = c;
            }
            else
            {
                var c = go.AddComponent<MagicaSphereCollider>();
                c.SetSize(radius);
                copy = c;
            }
            copy.center = source.center;
            copy.symmetryMode = source.symmetryMode;
            copy.UpdateParameters();
            return copy;
        }

        /// <summary>부위 전용 구 그룹을 만듦. 기본 자세 선분과 겹치는 구는 반경을 줄이거나 뺌.</summary>
        public static VRMSpringBoneColliderGroup[] BuildSpringGroups(
            IEnumerable<ColliderComponent> colliders, IReadOnlyList<(Vector3 a, Vector3 b)> segments,
            float hitRadius, string partName)
        {
            var groups = new List<VRMSpringBoneColliderGroup>();
            foreach (var collider in colliders.Where(c => c != null))
            {
                var spheres = LocalSpheres(collider)
                    .Select(s => (center: collider.transform.TransformPoint(s.Offset),
                        radius: s.Radius * MaxScale(collider.transform)))
                    .ToList();
                AddGroup(groups, collider.transform, partName, spheres, segments, hitRadius);
            }
            return groups.ToArray();
        }

        /// <summary>선분과 겹치지 않도록 줄인 구 반경. 너무 작아지면 0(제외)을 반환함.</summary>
        public static float FitRadius(Vector3 center, float radius,
            IReadOnlyList<(Vector3 a, Vector3 b)> segments, float hitRadius)
        {
            float clearance = float.MaxValue;
            foreach (var (a, b) in segments)
                clearance = Mathf.Min(clearance, ColliderMath.DistancePointToSegment(center, a, b));
            float fitted = Mathf.Min(radius, clearance - hitRadius - ClearanceMargin);
            return fitted >= radius * MinKeptRadiusRatio ? fitted : 0f;
        }

        /// <summary>MC2 콜라이더를 본 로컬 구로 근사함. 캡슐은 양끝과 중점의 구 3개.</summary>
        public static VRMSpringBoneColliderGroup.SphereCollider[] LocalSpheres(ColliderComponent source)
        {
            var size = source.GetSize();
            if (!(source is MagicaCapsuleCollider capsule))
                return new[] { Sphere(source.center, size.x) };

            var (start, end) = LocalAxis(capsule, size);
            if ((start - end).sqrMagnitude < 0.00000001f)
                return new[] { Sphere(start, Mathf.Max(size.x, size.y)) };
            return new[]
            {
                Sphere(start, size.x),
                Sphere((start + end) * 0.5f, (size.x + size.y) * 0.5f),
                Sphere(end, size.y),
            };
        }

        static void AddGroup(List<VRMSpringBoneColliderGroup> groups, Transform owner, string groupName,
            List<(Vector3 center, float radius)> worldSpheres,
            IReadOnlyList<(Vector3 a, Vector3 b)> segments, float hitRadius)
        {
            float scale = Mathf.Max(0.0001f, MaxScale(owner));
            var local = new List<VRMSpringBoneColliderGroup.SphereCollider>();
            foreach (var (center, radius) in worldSpheres)
            {
                float fitted = FitRadius(center, radius, segments, hitRadius);
                if (fitted > 0f)
                    local.Add(Sphere(owner.InverseTransformPoint(center), fitted / scale));
            }
            if (local.Count == 0)
                return;

            // 오브젝트 원점·회전을 소유 본과 같게 두어 오프셋 좌표계를 공유함.
            var go = new GameObject(SpringGroupPrefix + groupName);
            go.transform.SetParent(owner, false);
            var group = go.AddComponent<VRMSpringBoneColliderGroup>();
            group.Colliders = local.ToArray();
            groups.Add(group);
        }

        static (Vector3 start, Vector3 end, float radius) WorldCapsule(ColliderComponent collider)
        {
            var size = collider.GetSize();
            float scale = MaxScale(collider.transform);
            if (!(collider is MagicaCapsuleCollider capsule))
            {
                var c = collider.transform.TransformPoint(collider.center);
                return (c, c, size.x * scale);
            }
            var (start, end) = LocalAxis(capsule, size);
            return (collider.transform.TransformPoint(start), collider.transform.TransformPoint(end),
                Mathf.Max(size.x, size.y) * scale);
        }

        // 캡슐 양끝 구의 중심(로컬). 반경만큼 안쪽으로 들어간 축 선분.
        static (Vector3 start, Vector3 end) LocalAxis(MagicaCapsuleCollider capsule, Vector3 size)
        {
            var axis = capsule.GetLocalDir();
            float startLength = capsule.alignedOnCenter ? size.z * 0.5f : 0f;
            float endLength = capsule.alignedOnCenter ? startLength : size.z - size.x;
            return (capsule.center + axis * Mathf.Max(0f, startLength - size.x),
                capsule.center - axis * Mathf.Max(0f, endLength - size.y));
        }

        static float MinDistanceToCapsule(IReadOnlyList<(Vector3 a, Vector3 b)> segments, Vector3 start, Vector3 end)
        {
            float best = float.MaxValue;
            foreach (var (a, b) in segments)
                for (int i = 0; i <= SegmentSamples; i++)
                    best = Mathf.Min(best, ColliderMath.DistancePointToSegment(
                        Vector3.Lerp(a, b, i / (float)SegmentSamples), start, end));
            return best;
        }

        static float MaxScale(Transform target)
        {
            var s = target.lossyScale;
            return Mathf.Max(Mathf.Abs(s.x), Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z)));
        }

        static VRMSpringBoneColliderGroup.SphereCollider Sphere(Vector3 center, float radius)
        {
            return new VRMSpringBoneColliderGroup.SphereCollider { Offset = center, Radius = radius };
        }
    }
}
