using System;
using System.Collections.Generic;
using System.Linq;
using MagicaCloth2;
using UnityEngine;

namespace Fbx2Vmd.ClothPhysics
{
    /// <summary>
    /// 캐릭터의 머리카락 스프링 본과 의상 물리를 자동 설정함.
    /// ContextMenu로 실행하며, 콜라이더 자동 생성 → 머리카락 체인 탐지 →
    /// 부위별 스프링 본과 의상 클로스 생성 순으로 진행함.
    /// </summary>
    public partial class CharacterPhysicsSetup : MonoBehaviour
    {
        public const string ClothNamePrefix = "MC2Cloth_";

        [Header("대상")]
        [Tooltip("비워두면 자식에서 Animator를 자동 탐색")]
        public Animator targetAnimator;

        [Tooltip("사용자 지정 머리카락 루트 본. 지정 시 자동 탐지 무시")]
        public List<Transform> explicitHairRoots = new List<Transform>();

        [Header("콜라이더 옵션")]
        public bool useSymmetry = true;
        public bool includeLegs = true;
        [Range(1.0f, 1.5f)]
        public float colliderRadiusScale = 1.15f;

        [Header("머리카락 옵션")]
        [Tooltip("이름 규칙에 맞지 않는 미매핑 본도 머리카락 후보로 포함")]
        public bool includeUnknownBones = false;

        [Tooltip("물리를 적용할 머리카락 부위. 끈 부위는 스프링 본을 만들지 않아 " +
                 "본이 애니메이션을 그대로 따라간다 (예: Tail만 켜면 트윈테일만 물리). " +
                 "신체측 장식물 체인은 이 마스크 대상이 아님")]
        public HairPartMask hairPhysicsParts = HairPartMask.All;

        [Header("장식물 옵션")]
        [Tooltip("이름에 안 잡히는 신체측 체인을 토폴로지+스키닝으로 장식물 후보 탐지 " +
                 "(일반 FBX/VRM용 폴백 — 스키닝 버텍스를 가진 체인만 채택)")]
        public bool detectGarmentChainsByTopology = true;

        [Header("네이티브 스프링본")]
        [Tooltip("네이티브 스프링본(VRM VRMSpringBone 등)이 시뮬레이션하는 본에는 " +
                 "MC2를 생성하지 않는다 — 제작자 튜닝이 정본, 이중 물리 방지")]
        public bool respectNativeSpringBones = true;

        /// <summary>VRM 내장 스프링본 처리 방식.</summary>
        public enum VrmConversionMode
        {
            /// <summary>C-1: 네이티브 소유 본에는 MC2를 만들지 않고 네이티브 시뮬을 그대로 둠</summary>
            GuardOnly = 0,
            /// <summary>C-2: VRMSpringBone 튜닝을 MC2 BoneCloth로 변환 후 원본 비활성</summary>
            ConvertToMc2 = 1,
            /// <summary>네이티브 스프링본을 무시 — 이중 물리 위험이 있어 디버그용</summary>
            Off = 2,
        }

        [Tooltip("VRM 내장 스프링본 처리. GuardOnly=네이티브 소유 본에 MC2 미생성(기본), " +
                 "ConvertToMc2=제작자 튜닝을 MC2 클로스로 변환 후 원본 비활성, " +
                 "Off=무시(이중 물리 주의)")]
        public VrmConversionMode vrmConversionMode = VrmConversionMode.GuardOnly;

        /// <summary>
        /// 변환으로 비활성화한 VRM 스프링 — ClearGenerated에서 복원한다.
        /// 직렬화되어 있어 씬 저장·모드 전환 후에도 롤백 경로가 유지된다.
        /// </summary>
        [SerializeField, HideInInspector]
        List<VRM.VRMSpringBone> convertedSprings = new List<VRM.VRMSpringBone>();

        [Header("흔들림 조정")]
        public HairTuning tuning = HairTuning.Default;

        [Tooltip("장식물 클로스 물리 조정에 사용함. 머리카락 스프링은 고정 기본값을 씀")]
        public HairSpringProfile springProfile = HairSpringProfile.Default;

        /// <summary>
        /// 자동 물리 설정을 실행한다. 기존 자동 생성물은 정리 후 재생성.
        /// 단, 플레이 중 기존 클로스가 전부 유효/빌드 중이면 재생성을 생략한다 —
        /// 빌드 진행 중인 클로스를 파괴하면 MC2 스타트업이 취소돼 무효 클로스가 남는다.
        /// 강제 재생성은 '자동 생성 물리 제거' 후 실행하거나 Rebuild()를 사용.
        /// </summary>
        [ContextMenu("자동 물리 설정 실행")]
        public void Setup()
        {
            // 유효 클로스가 있어도 스커트 등 기대 생성물이 없으면 완전하지 않다 —
            // 씬 직렬화 클로스만 유효한 플레이에서 런타임 전용 스커트가 영구 누락되는 것을 막는다.
            if (Application.isPlaying &&
                (generatedCloths.Count > 0 ||
                 SpringHairBackend.FindProducts(transform).Any()) &&
                AllClothsAlive() && IsSetupComplete())
            {
                lastReport = $"이미 설정 완료 상태 — 클로스 {generatedCloths.Count}개 유효/빌드 중 (재실행 생략). " +
                    "재생성하려면 '자동 생성 물리 제거' 후 실행하세요.";
                Debug.Log("[CharacterPhysicsSetup] " + lastReport, this);
                return;
            }
            // 빌드 중인 클로스를 파괴하면 MC2 스타트업이 취소되므로 완료까지 지연한다.
            if (Application.isPlaying && generatedCloths.Any(c =>
                    c != null && c.Process.IsState(ClothProcess.State_Build)))
            {
                deferredSetup = true;
                lastReport = "클로스 빌드 진행 중 — 완료 후 자동 재설정 예약";
                Debug.Log("[CharacterPhysicsSetup] " + lastReport, this);
                return;
            }
            SetupInternal();
        }

        /// <summary>기대 생성물이 전부 있는지 — 스커트 본이 있으면 MeshCloth 존재 여부까지 확인.</summary>
        bool IsSetupComplete()
        {
            if (!autoSkirtCloth)
                return true;
            var animator = ResolveAnimator();
            if (animator == null ||
                SkirtClothBuilder.CollectSkirtBoneDepths(animator.transform).Count == 0)
                return true; // 스커트 비대상 모델
            return generatedCloths.Any(c =>
                c != null && (c.SerializeData.clothType == ClothProcess.ClothType.MeshCloth ||
                    c.name == ClothNamePrefix + "SkirtLoop"));
        }

        /// <summary>빌드 중 클로스가 있어 지연된 셋업 요청.</summary>
        bool deferredSetup;

        /// <summary>기존 클로스가 있어도 강제로 재생성한다.</summary>
        public void Rebuild()
        {
            ClearGenerated();
            SetupInternal();
        }

        // 비동기 init+빌드 완료 유예 (프레임). 이 기간의 !IsValid는 '방금 생성/방금 활성'으로 간주.
        // 죽은 클로스(SetActive로 팀 해제)와 init 비동기 진행 중은 둘 다 !IsValid라
        // 유예 종료 후에도 무효인 클로스만 죽은 것으로 판정한다.
        const int RebuildGraceFrames = 60;
        // 씬 직렬화 클로스도 플레이 시작 직후엔 init 중이므로 초기값 자체가 유예 구간.
        int graceUntilFrame = RebuildGraceFrames;

        /// <summary>플레이 중 클로스가 살아있는지(실행·빌드 중·유예 구간) 판정한다.</summary>
        bool IsClothAlive(MagicaCloth cloth)
        {
            if (cloth == null || !cloth.isActiveAndEnabled)
                return false;
            if (cloth.Process.IsState(ClothProcess.State_Build) || cloth.Process.IsRunning())
                return true;
            // 유예 구간의 !Running은 'init/빌드 대기' — 유예 후에도 미실행이면 죽은 클로스
            return Time.frameCount <= graceUntilFrame;
        }

        bool AllClothsAlive()
        {
            foreach (var cloth in generatedCloths)
                if (!IsClothAlive(cloth))
                    return false;
            return true;
        }

        void SetupInternal()
        {
            var report = new List<string>();
            ClearGenerated();

            Animator animator = ResolveAnimator();
            if (!ResolveBoneContext(animator, report, out System.Func<HumanBodyBones, Transform> resolver, out HashSet<Transform> bodyBones, out float torso))
                return;

            HashSet<Transform> externalOwned = BuildCollidersAndChains( animator, resolver, bodyBones, torso, report, out SkinnedMeshRenderer[] smrs, out BodyColliderBuilder.BuildResult colResult,
                out Dictionary<Transform, List<Vector3>> boneVerts, out Transform head, out HairChainDetector.Result detect);
            if (detect == null)
            {
                Finish(report);
                return;
            }
            var context2 = CollectClothChains( animator, resolver, bodyBones, boneVerts, externalOwned, head, detect, report);
            int headHairChainCount = context2.headHairChainCount;
            var skirtBoneDepths = context2.skirtBoneDepths;

            BuildDynamicsAndFinish( animator, resolver, smrs, torso, head, detect, headHairChainCount, skirtBoneDepths, colResult, report);
        }

        private void BuildDynamicsAndFinish(
            Animator animator, System.Func<HumanBodyBones, Transform> resolver,
            SkinnedMeshRenderer[] smrs, float torso, Transform head,
            HairChainDetector.Result detect, int headHairChainCount,
            Dictionary<Transform, int> skirtBoneDepths,
            BodyColliderBuilder.BuildResult colResult, List<string> report)
        {
            BuildSkirtCloth(animator, resolver, smrs, torso,
                skirtBoneDepths, colResult, report);
            BuildHairAndAccessoryDynamics(detect, headHairChainCount,
                torso, head, colResult, report);
            graceUntilFrame = Time.frameCount + RebuildGraceFrames;
            Finish(report);
        }

        // 1. 버텍스 수집 → 콜라이더 생성 + 2. 머리카락 체인 탐지까지의 준비 단계.
        // 네이티브 스프링본(VRM 등) 소유 본 — 같은 본에 MC2를 얹으면 이중 물리가 된다.
        private HashSet<Transform> BuildCollidersAndChains(
            Animator animator, System.Func<HumanBodyBones, Transform> resolver,
            HashSet<Transform> bodyBones,
            float torso, List<string> report,
            out SkinnedMeshRenderer[] smrs,
            out BodyColliderBuilder.BuildResult colResult,
            out Dictionary<Transform, List<Vector3>> boneVerts,
            out Transform head,
            out HairChainDetector.Result detect)
        {
            smrs = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            boneVerts = BodyColliderBuilder.GatherBoneVertices(smrs);
            colResult = BuildBodyColliders(
                animator, resolver, torso, boneVerts, report);
            head = resolver(HumanBodyBones.Head);
            var nativeOwned = CollectNativeSprings(animator, head, torso, report);
            detect = head == null
                ? null
                : DetectAndFilterChains(head, bodyBones, nativeOwned, report);
            bool protectAccessories = vrmConversionMode == VrmConversionMode.ConvertToMc2 ||
                (respectNativeSpringBones && vrmConversionMode != VrmConversionMode.Off);
            return protectAccessories ? nativeOwned : new HashSet<Transform>();
        }

        // 스커트 본은 미리 수집 — 장식물 토폴로지 탐지의 소유 집합에도 포함한다.
        // 머리 측 체인 수 — 신체측 장식물 체인이 뒤에 추가되므로 마스크 대상 구간을 구분한다.
        private (int headHairChainCount, Dictionary<Transform, int> skirtBoneDepths) CollectClothChains(
            Animator animator, System.Func<HumanBodyBones, Transform> resolver,
            HashSet<Transform> bodyBones,
            Dictionary<Transform, List<Vector3>> boneVerts,
            HashSet<Transform> externalOwned, Transform head,
            HairChainDetector.Result detect, List<string> report)
        {
            var skirtBoneDepths = SkirtClothBuilder.CollectSkirtBoneDepths(animator.transform);
            int headHairChainCount = detect.chains.Count;
            CollectAccessoryChains(animator, resolver, bodyBones, boneVerts,
                externalOwned, skirtBoneDepths, head, detect,
                ref headHairChainCount, report);
            return (headHairChainCount, skirtBoneDepths);
        }

        /// <summary>생성물 전체 제거 (재실행 안전). 추출된 스커트 메시도 복원한다.</summary>
        [ContextMenu("자동 생성 물리 제거")]
        public void ClearGenerated()
        {
            foreach (var spring in SpringHairBackend.FindProducts(transform))
                spring.RestorePose();

            // 변환으로 끈 네이티브 스프링 복원 — 이 호출이 없으면 생성물만 지워져
            // 물리가 소실되고 GuardOnly 복귀도 원본을 잃는다.
            foreach (var s in convertedSprings)
                if (s != null)
                    s.enabled = true;
            convertedSprings.Clear();
            SkirtClothBuilder.CleanupExtractions(transform);
            BodyColliderBuilder.ClearGenerated(transform);
            var doomed = new List<GameObject>();
            foreach (Transform child in transform)
            {
                if (child.name.StartsWith(ClothNamePrefix) ||
                    SpringHairBackend.IsGeneratedObject(transform, child))
                    doomed.Add(child.gameObject);
            }
            foreach (var go in doomed)
            {
                if (Application.isPlaying)
                {
                    if (SpringHairBackend.IsGeneratedObject(transform, go.transform))
                        go.SetActive(false);
                    Destroy(go);
                }
                else
                    DestroyImmediate(go);
            }
            generatedColliders.Clear();
            generatedCloths.Clear();
            generatedParts.Clear();
            generatedIsLong.Clear();
        }

        MagicaCloth CreateAccessoryCloth(
            HairPart part, List<HairChain> chains, Dictionary<ColliderCategory, List<ColliderComponent>> collidersByCategory, float torso, Transform head, List<string> report)
        {
            if (chains.Count == 0) return null;
            var go = new GameObject(ClothNamePrefix + part);
            go.transform.SetParent(transform, false);
            var cloth = go.AddComponent<MagicaCloth>();
            var sdata = cloth.SerializeData;
            sdata.clothType = ClothProcess.ClothType.BoneCloth;
            sdata.rootBones.Clear();
            foreach (var chain in chains) sdata.rootBones.Add(chain.rootBone);
            float maxLen = chains.Max(c => c.worldLength);
            bool isLong = torso > 0f && maxLen >= torso * 0.8f;
            // 부위별 파라미터 템플릿 + 튜닝 배율 (+ Boing 프로파일 오버라이드)
            HairPhysicsParameters.Apply(part, sdata, torso, head, isLong, tuning, usePresetBaseline, springProfile);
            // 관련 콜라이더만 등록 (32개 제한 관리)
            // Pre-build 데이터는 SerializeData2에 위치 — 에디터에서 Pre-build 생성도 필요
            cloth.GetSerializeData2().preBuildData.enabled = usePreBuild;
            var categories = HairPartColliderMapper.Map(part, maxLen, torso);
            var selected = HairPartColliderMapper.Select(categories, collidersByCategory, 32);
            // 기본 자세에서 이미 체인을 덮는 콜라이더는 첫 프레임부터 장식물을 밀어내므로 반경을 맞춤.
            var fitted = new List<string>();
            selected = RestPoseColliderFit.FitAccessoryColliders(selected,
                RestPoseColliderFit.CollectRestSegments(chains.Select(c => c.rootBone)),
                sdata.radius.value, fitted);
            if (fitted.Count > 0)
                report.Add($"{part}: 기본 자세와 겹친 콜라이더 조정 — {string.Join(", ", fitted)}");
            sdata.colliderCollisionConstraint.colliderList.Clear();
            sdata.colliderCollisionConstraint.colliderList.AddRange(selected);
            ApplyCulling(cloth);
            cloth.OnBuildComplete += HandleClothBuildComplete;
            report.Add($"{part}: 체인 {chains.Count}개, 최대길이 {maxLen:F2}m, " + $"콜라이더 {selected.Count}개{(isLong ? " [장발]" : "")}");
            generatedParts.Add(part);
            generatedIsLong.Add(isLong);
            return cloth; }

        /// <summary>
        /// 생성된 스프링 본과 장식물 클로스에 조정값을 다시 적용함.
        /// </summary>
        [ContextMenu("흔들림 파라미터 재적용")]
        public void ReapplyParameters()
        {
            ResolveTorsoAndHead(out float torso, out Transform head);
            int springs = ReapplySpringParameters(torso);
            int accessories = ApplyTuningToAccessoryCloths(torso, head);
            lastReport = $"흔들림 재적용: 스프링 본 {springs}개, 장식물 클로스 {accessories}개";
            Debug.Log("[CharacterPhysicsSetup] " + lastReport, this);
        }

        private int ApplyTuningToAccessoryCloths(float torso, Transform head)
        {
            int applied = 0;
            for (int i = 0; i < generatedCloths.Count; i++)
            {
                var cloth = generatedCloths[i];
                if (!IsAccessoryCloth(cloth))
                    continue;
                var part = i < generatedParts.Count ? generatedParts[i] : HairPart.Unknown;
                bool isLong = i < generatedIsLong.Count && generatedIsLong[i];
                HairPhysicsParameters.Apply(part, cloth.SerializeData, torso, head, isLong, tuning,
                    usePresetBaseline, springProfile);
                ApplyCulling(cloth);
                NotifyParameterChange(cloth);
                applied++;
            }
            return applied;
        }

        private void ResolveTorsoAndHead(out float torso, out Transform head)
        {
            torso = 0.6f;
            head = null;
            Animator animator = ResolveAnimator();
            if (animator != null && animator.isHuman)
            {
                torso = BodyColliderBuilder.MeasureTorso(animator);
                head = animator.GetBoneTransform(HumanBodyBones.Head);
            }
            else if (animator != null)
            {
                var nameMap = NamePatternBoneResolver.Resolve(animator.transform);
                nameMap.TryGetValue(HumanBodyBones.Head, out head);
                if (head != null && nameMap.TryGetValue(HumanBodyBones.Hips, out var hips))
                    torso = BodyColliderBuilder.MeasureTorso(hips, head);
            }
        }

        static bool IsAccessoryCloth(MagicaCloth cloth)
        {
            return cloth != null && cloth.name == ClothNamePrefix + HairPart.Accessory &&
                cloth.SerializeData.clothType == ClothProcess.ClothType.BoneCloth;
        }

        /// <summary>리스폰·순간이동 후 생성된 물리의 상태를 초기화함.</summary>
        public void ResetPhysics(bool keepPose = false)
        {
            foreach (var spring in SpringHairBackend.FindProducts(transform))
                spring.ResetState();
            foreach (var cloth in generatedCloths)
            {
                if (cloth != null && cloth.IsValid())
                    cloth.ResetCloth(keepPose);
            }
        }

        /// <summary>
        /// 비활성→활성 전환 시 생성된 클로스의 무결성을 확인한다.
        /// <summary>
        /// 장식물이 매달릴 수 있는 신체 본을 해석한다 — 허리/척추/가슴/윗가슴/목/어깨.
        /// 해석 실패 본은 건너뛰고 중복은 제거한다.
        /// </summary>
    }
}
