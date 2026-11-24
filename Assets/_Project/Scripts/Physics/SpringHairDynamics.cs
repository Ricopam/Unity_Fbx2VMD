using System.Collections.Generic;
using System.Linq;
using Fbx2Vmd.FBXImporter;
using UnityEngine;
using VRM;

namespace Fbx2Vmd.ClothPhysics
{
    /// <summary>
    /// 머리카락 부위 하나의 체인 스프링을 실행함. 몸 보정 패스가 최종 자세를 쓴 뒤에 계산해
    /// 머리카락이 한 프레임 늦게 따라가지 않게 함. 충돌은 부위 전용 구 그룹을 씀.
    /// </summary>
    [DefaultExecutionOrder(CorrectionPassOrder.HairSpringDynamics)]
    public sealed class SpringHairDynamics : MonoBehaviour
    {
        [SerializeField] private Transform[] roots = System.Array.Empty<Transform>();
        [SerializeField] private VRMSpringBoneColliderGroup[] colliderGroups = System.Array.Empty<VRMSpringBoneColliderGroup>();
        [SerializeField] private float unit = 1f;
        // 부위마다 흔들림 세기가 다름. 이 값이 생기기 전에 저장된 생성물은 Unknown(긴 머리 설정)으로 읽힘.
        [SerializeField] private HairPart part = HairPart.Unknown;

        private readonly List<SpringHairChain> chains = new List<SpringHairChain>();
        private readonly List<(Vector3 center, float radius)> spheres = new List<(Vector3, float)>();

        public IReadOnlyList<Transform> Roots => roots;

        public IReadOnlyList<VRMSpringBoneColliderGroup> ColliderGroups => colliderGroups;

        public HairPart Part => part;

        /// <summary>구동할 루트 본과 충돌 구 그룹, 부위를 지정함. 체인은 다음 계산 때 다시 만듦.</summary>
        public void Configure(IEnumerable<Transform> rootBones, IEnumerable<VRMSpringBoneColliderGroup> groups,
            HairPart hairPart)
        {
            RestorePose();
            part = hairPart;
            roots = rootBones.Where(root => root != null).Distinct().ToArray();
            colliderGroups = groups.Where(group => group != null).ToArray();
            chains.Clear();
        }

        /// <summary>체형 단위를 바꿈. 충돌 반경이 체형 비율로 늘거나 줄어듦.</summary>
        public void SetBodyUnit(float bodyUnit)
        {
            unit = bodyUnit;
        }

        /// <summary>리스폰·순간이동 뒤 이전 흔들림이 남지 않게 다음 프레임부터 새로 시작함.</summary>
        public void ResetState()
        {
            foreach (var chain in chains)
                chain.Reset();
        }

        /// <summary>물리 결과를 지우고 애니메이션 자세로 되돌림.</summary>
        public void RestorePose()
        {
            foreach (var chain in chains)
                chain.RestoreAnimatedPose();
        }

        /// <summary>현재 자세를 기준으로 deltaTime만큼 계산함. 실행 중에는 LateUpdate가 호출함.</summary>
        internal void Simulate(float deltaTime)
        {
            if (chains.Count == 0)
                BuildChains();
            ReadSpheres();
            // 머리카락은 고정 기본값을 씀. 캐릭터 설정의 프로파일은 장식물 클로스 조정용임.
            var settings = new SpringHairSettings(unit, part);
            foreach (var chain in chains)
                chain.Step(deltaTime, settings, spheres);
        }

        private void Update()
        {
            RestorePose();
        }

        private void LateUpdate()
        {
            Simulate(Time.deltaTime);
        }

        // 꺼진 동안 캐릭터가 움직였을 수 있으므로 다시 켜지면 현재 자세에서 새로 시작함.
        private void OnEnable()
        {
            ResetState();
        }

        private void OnDisable()
        {
            RestorePose();
        }

        private void BuildChains()
        {
            foreach (var root in roots)
                if (root != null)
                    chains.Add(new SpringHairChain(root));
        }

        private void ReadSpheres()
        {
            spheres.Clear();
            foreach (var group in colliderGroups)
            {
                if (group == null || group.Colliders == null)
                    continue;
                float scale = MaxScale(group.transform);
                foreach (var collider in group.Colliders)
                    if (collider != null && collider.Radius > 0f)
                        spheres.Add((group.transform.TransformPoint(collider.Offset), collider.Radius * scale));
            }
        }

        private static float MaxScale(Transform target)
        {
            var scale = target.lossyScale;
            return Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        }
    }
}
