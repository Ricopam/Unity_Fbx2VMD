using System.Collections.Generic;
using Fbx2Vmd.ClothPhysics;
using MagicaCloth2;
using NUnit.Framework;
using UnityEngine;
using VRM;

namespace Tests.Editor.ClothPhysics
{
    /// <summary>
    /// 기본 자세와 겹친 콜라이더 정리 검증.
    /// 겹친 채로 시작하면 넥타이가 부풀고 트윈테일이 수평으로 뻗던 회귀 방지.
    /// </summary>
    public class RestPoseColliderFitTests
    {
        readonly List<GameObject> created = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in created)
                if (go != null) Object.DestroyImmediate(go);
            created.Clear();
        }

        Transform Node(string name, Transform parent, Vector3 position)
        {
            var go = new GameObject(name);
            if (parent == null) created.Add(go);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            return go.transform;
        }

        // 원점에서 아래로 내려가는 3본 체인의 루트.
        Transform Chain(Vector3 top)
        {
            var root = Node("Chain", null, top);
            var mid = Node("Mid", root, top + Vector3.down * 0.1f);
            Node("Tip", mid, top + Vector3.down * 0.2f);
            return root;
        }

        MagicaSphereCollider SphereAt(string name, Vector3 center, float radius)
        {
            var bone = Node(name + "_Bone", null, center);
            var go = new GameObject(BodyColliderBuilder.AutoNamePrefix + name);
            go.transform.SetParent(bone, false);
            var col = go.AddComponent<MagicaSphereCollider>();
            col.SetSize(radius);
            return col;
        }

        [Test]
        public void Given_SegmentInsideSphere_When_FittingRadius_Then_ShrinksBelowClearance()
        {
            var segments = new List<(Vector3, Vector3)> { (new Vector3(0.1f, 0f, 0f), new Vector3(0.1f, -0.2f, 0f)) };

            float fitted = RestPoseColliderFit.FitRadius(Vector3.zero, 0.14f, segments, 0.005f);

            Assert.That(fitted, Is.GreaterThan(0f).And.LessThanOrEqualTo(0.1f - 0.005f));
        }

        [Test]
        public void Given_SegmentThroughCenter_When_FittingRadius_Then_DropsSphere()
        {
            var segments = new List<(Vector3, Vector3)> { (new Vector3(0f, 0.1f, 0f), new Vector3(0f, -0.1f, 0f)) };

            Assert.That(RestPoseColliderFit.FitRadius(Vector3.zero, 0.14f, segments, 0.005f), Is.EqualTo(0f));
        }

        [Test]
        public void Given_ClearSegment_When_FittingRadius_Then_KeepsRadius()
        {
            var segments = new List<(Vector3, Vector3)> { (new Vector3(1f, 0f, 0f), new Vector3(1f, -0.2f, 0f)) };

            Assert.That(RestPoseColliderFit.FitRadius(Vector3.zero, 0.14f, segments, 0.005f), Is.EqualTo(0.14f));
        }

        [Test]
        public void Given_ColliderCoveringRestChain_When_FittingAccessory_Then_UsesShrunkCopyAndKeepsOriginal()
        {
            var root = Chain(new Vector3(0f, 0f, 0.08f));
            var chest = SphereAt("Chest", new Vector3(0f, -0.1f, 0f), 0.14f);
            var far = SphereAt("Far", new Vector3(0f, -0.1f, 1f), 0.1f);
            var changes = new List<string>();

            var result = RestPoseColliderFit.FitAccessoryColliders(new ColliderComponent[] { chest, far },
                RestPoseColliderFit.CollectRestSegments(new[] { root }), 0.01f, changes);

            Assert.That(result.Count, Is.EqualTo(2));
            Assert.That(result[1], Is.EqualTo(far));
            var copy = (MagicaSphereCollider)result[0];
            Assert.That(copy.transform.parent, Is.EqualTo(chest.transform));
            Assert.That(copy.GetSize().x, Is.EqualTo(0.08f - 0.01f - 0.002f).Within(0.0005f));
            Assert.That(chest.GetSize().x, Is.EqualTo(0.14f), "다른 클로스와 공유하는 원본은 바꾸지 않아야 함");
            Assert.That(changes.Count, Is.EqualTo(1));
        }

        [Test]
        public void Given_ChainThroughCenter_When_FittingAccessory_Then_DropsCollider()
        {
            var root = Chain(new Vector3(0f, 0f, 0f));
            var chest = SphereAt("Chest", new Vector3(0f, -0.1f, 0f), 0.14f);

            var result = RestPoseColliderFit.FitAccessoryColliders(new ColliderComponent[] { chest },
                RestPoseColliderFit.CollectRestSegments(new[] { root }), 0.01f);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Given_StartAlignedCapsule_When_Shrunk_Then_AxisSegmentIsPreserved()
        {
            var bone = Node("Waist_Bone", null, Vector3.zero);
            var go = new GameObject(BodyColliderBuilder.AutoNamePrefix + "Waist");
            go.transform.SetParent(bone, false);
            var waist = go.AddComponent<MagicaCapsuleCollider>();
            waist.direction = MagicaCapsuleCollider.Direction.Y;
            waist.alignedOnCenter = false;
            waist.SetSize(0.1f, 0.1f, 0.4f);
            // 축(y 0~-0.2) 옆 6cm를 지나는 체인
            var root = Chain(new Vector3(0.06f, 0f, 0f));

            var result = RestPoseColliderFit.FitAccessoryColliders(new ColliderComponent[] { waist },
                RestPoseColliderFit.CollectRestSegments(new[] { root }), 0.005f);

            var copy = (MagicaCapsuleCollider)result[0];
            var size = copy.GetSize();
            Assert.That(size.x, Is.EqualTo(0.06f - 0.005f - 0.002f).Within(0.0005f));
            Assert.That(size.z - size.x - size.y, Is.EqualTo(0.4f - 0.2f).Within(0.0001f));
        }

        [Test]
        public void Given_HeadSphereOverlappingRestHair_When_BuildingSpringGroups_Then_ShrinksOwnGroupAndClearRemovesIt()
        {
            var body = Node("Body", null, Vector3.zero);
            var go = new GameObject(BodyColliderBuilder.AutoNamePrefix + "Head");
            go.transform.SetParent(body, false);
            var head = go.AddComponent<MagicaSphereCollider>();
            head.SetSize(0.1f);
            // 뿌리가 머리 구 표면 안쪽 6cm 지점에서 시작하는 트윈테일
            var hair = Chain(new Vector3(0.06f, 0f, 0f));

            var groups = RestPoseColliderFit.BuildSpringGroups(new ColliderComponent[] { head },
                RestPoseColliderFit.CollectRestSegments(new[] { hair }), 0.005f, "Tail");

            Assert.That(groups.Length, Is.EqualTo(1));
            Assert.That(groups[0].transform.parent, Is.EqualTo(head.transform));
            Assert.That(groups[0].Colliders[0].Radius, Is.LessThan(0.06f - 0.005f));
            Assert.That(head.GetComponent<VRMSpringBoneColliderGroup>(), Is.Null, "원본 콜라이더의 공유 그룹을 바꾸지 않아야 함");

            BodyColliderBuilder.ClearGenerated(body);
            Assert.That(body.GetComponentsInChildren<VRMSpringBoneColliderGroup>(true), Is.Empty);
        }

        [Test]
        public void Given_MirroredCollider_When_BuildingSpringGroups_Then_AddsRightGroupAndClearRemovesIt()
        {
            var body = Node("Body", null, Vector3.zero);
            var left = Node("LeftArm", body, new Vector3(-0.3f, 0.1f, 0f));
            var right = Node("RightArm", body, new Vector3(0.3f, 0.1f, 0f));
            var go = new GameObject(BodyColliderBuilder.AutoNamePrefix + "UpperArm_L");
            go.transform.SetParent(left, false);
            var arm = go.AddComponent<MagicaSphereCollider>();
            arm.SetSize(0.04f);
            // 축에서 벗어난 오프셋도 좌우만 뒤집혀야 함
            arm.center = new Vector3(-0.05f, 0.02f, 0.03f);
            var hair = Chain(new Vector3(0f, 0.5f, -0.2f));

            var groups = RestPoseColliderFit.BuildSpringGroups(new ColliderComponent[] { arm },
                RestPoseColliderFit.CollectRestSegments(new[] { hair }), 0.005f, "Tail",
                new Dictionary<ColliderComponent, Transform> { [arm] = right });

            Assert.That(groups.Length, Is.EqualTo(2));
            Assert.That(groups[1].transform.parent, Is.EqualTo(right));
            var mirrored = groups[1].transform.TransformPoint(groups[1].Colliders[0].Offset);
            Assert.That(Vector3.Distance(mirrored, new Vector3(0.35f, 0.12f, 0.03f)), Is.LessThan(0.0001f));

            BodyColliderBuilder.ClearGenerated(body);
            Assert.That(body.GetComponentsInChildren<VRMSpringBoneColliderGroup>(true), Is.Empty);
        }
    }
}
