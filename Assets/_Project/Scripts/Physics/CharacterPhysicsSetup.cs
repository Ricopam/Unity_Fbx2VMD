using System;
using System.Collections.Generic;
using System.Linq;
using MagicaCloth2;
using UnityEngine;

namespace Fbx2Vmd.ClothPhysics
{
    /// <summary>
    /// 캐릭터에 MC2 물리를 자동 세팅하는 오케스트레이터.
    /// ContextMenu로 실행하며, 콜라이더 자동 생성 → 머리카락 체인 탐지 →
    /// 부위별 BoneCloth 생성 순으로 진행한다.
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

        [Tooltip("물리를 적용할 머리카락 부위. 끈 부위는 클로스를 만들지 않아 " +
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

        [Header("머리카락 백엔드")]
        [Tooltip("머리카락 다이내믹스 엔진. 본 스프링 에셋은 에셋 임포트 시에만 동작하고 미설치면 MC2로 폴백")]
        public HairDynamicsBackend hairBackend = HairDynamicsBackend.MagicaCloth2;

        [Tooltip("본 스프링 식 머리카락 파라미터. enabled면 프리셋+tuning 결과 위에 " +
                 "스프링 의미론으로 덮어쓰고, 본 스프링 에셋 백엔드에서는 그대로 주입된다")]
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
                 SpringHairBackend.FindProduct(transform) != null) &&
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
            if (!ResolveBoneContext(animator, report,
                    out System.Func<HumanBodyBones, Transform> resolver,
                    out HashSet<Transform> bodyBones, out float torso))
                return;

            // 1. 버텍스 수집 → 콜라이더 생성 (Humanoid만 시메트리 가능, 폴백은 미러 직접 생성)
            var smrs = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var boneVerts = BodyColliderBuilder.GatherBoneVertices(smrs);
            BodyColliderBuilder.BuildResult colResult = BuildBodyColliders(
                animator, resolver, torso, boneVerts, report);

            // 네이티브 스프링본(VRM 등) 소유 본 — 같은 본에 MC2를 얹으면 이중 물리가 된다.
            // ConvertToMc2일 때도 소유 스캔은 필요 — 변환 본이 자동 탐지에 잡히면
            // 변환 클로스와 자동 생성 클로스의 이중 시뮬레이션이 된다.
            HashSet<Transform> externalOwned =
                ApplyNativeGuardAndConversion(animator, torso, report);

            // 2. 머리카락 체인 탐지
            Transform head = resolver(HumanBodyBones.Head);
            HairChainDetector.Result detect = head == null
                ? null
                : DetectAndFilterChains(head, bodyBones, externalOwned, report);
            if (detect == null)
            {
                Finish(report);
                return;
            }

            // 스커트 본은 미리 수집 — 장식물 토폴로지 탐지의 소유 집합에도 포함한다
            var skirtBoneDepths = SkirtClothBuilder.CollectSkirtBoneDepths(animator.transform);

            // 머리 측 체인 수 — 신체측 장식물 체인이 뒤에 추가되므로 마스크 대상 구간을 구분한다
            int headHairChainCount = detect.chains.Count;

            CollectAccessoryChains(animator, resolver, bodyBones, boneVerts,
                externalOwned, skirtBoneDepths, head, detect,
                ref headHairChainCount, report);

            BuildSkirtCloth(animator, resolver, smrs, torso,
                skirtBoneDepths, colResult, report);

            bool springPath = BuildHairDynamics(detect, headHairChainCount,
                torso, head, colResult, report);

            // 카운트는 머리카락 클로스만 — 스커트/VRM 변환 클로스도 generatedCloths에 들어간다
            int hairClothCount = generatedCloths.Count(IsHairCloth);
            report.Add($"머리카락 다이내믹스 생성: {(springPath ? "본 스프링 에셋 1개" : $"BoneCloth {hairClothCount}개")}");
            graceUntilFrame = Time.frameCount + RebuildGraceFrames;
            Finish(report);
        }


        /// <summary>생성물 전체 제거 (재실행 안전). 추출된 스커트 메시도 복원한다.</summary>
        [ContextMenu("자동 생성 물리 제거")]
        public void ClearGenerated()
        {
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
                if (child.name.StartsWith(ClothNamePrefix))
                    doomed.Add(child.gameObject);
            }
            foreach (var go in doomed)
            {
                if (Application.isPlaying)
                    Destroy(go);
                else
                    DestroyImmediate(go);
            }
            generatedColliders.Clear();
            generatedCloths.Clear();
            generatedParts.Clear();
            generatedIsLong.Clear();
        }

        MagicaCloth CreateHairCloth(
            HairPart part, List<HairChain> chains,
            Dictionary<ColliderCategory, List<ColliderComponent>> collidersByCategory,
            float torso, Transform head, List<string> report)
        {
            if (chains.Count == 0)
                return null;

            var go = new GameObject(ClothNamePrefix + part);
            go.transform.SetParent(transform, false);
            var cloth = go.AddComponent<MagicaCloth>();
            var sdata = cloth.SerializeData;

            sdata.clothType = ClothProcess.ClothType.BoneCloth;
            sdata.rootBones.Clear();
            foreach (var chain in chains)
                sdata.rootBones.Add(chain.rootBone);

            float maxLen = chains.Max(c => c.worldLength);
            bool isLong = torso > 0f && maxLen >= torso * 0.8f;

            // 부위별 파라미터 템플릿 + 튜닝 배율 (+ Boing 프로파일 오버라이드)
            HairPhysicsParameters.Apply(part, sdata, torso, head, isLong, tuning,
                usePresetBaseline, springProfile);

            // 관련 콜라이더만 등록 (32개 제한 관리)
            // Pre-build 데이터는 SerializeData2에 위치 — 에디터에서 Pre-build 생성도 필요
            cloth.GetSerializeData2().preBuildData.enabled = usePreBuild;
            var categories = HairPartColliderMapper.Map(part, maxLen, torso);
            var selected = HairPartColliderMapper.Select(categories, collidersByCategory, 32);
            sdata.colliderCollisionConstraint.colliderList.Clear();
            sdata.colliderCollisionConstraint.colliderList.AddRange(selected);
            ApplyCulling(cloth);
            cloth.OnBuildComplete += HandleClothBuildComplete;

            report.Add($"{part}: 체인 {chains.Count}개, 최대길이 {maxLen:F2}m, " +
                $"콜라이더 {selected.Count}개{(isLong ? " [장발]" : "")}");
            generatedParts.Add(part);
            generatedIsLong.Add(isLong);
            return cloth;
        }

        /// <summary>
        /// 이미 생성된 클로스에 현재 튜닝값만 다시 적용한다 (콜라이더/체인 재생성 없음).
        /// 인스펙터 슬라이더 조정 후 실행.
        /// </summary>
        [ContextMenu("흔들림 파라미터 재적용")]
        public void ReapplyParameters()
        {
            // 본 스프링 에셋 백엔드는 클로스가 아니라 본 스프링 체인 컴포넌트에 파라미터가 산다.
            if (hairBackend == HairDynamicsBackend.SpringBones)
            {
                ReapplySpringBackend();
                return;
            }
            if (generatedCloths.Count == 0)
            {
                lastReport = "재적용할 클로스가 없습니다. 자동 물리 설정을 먼저 실행하세요.";
                Debug.LogWarning(lastReport, this);
                return;
            }
            Animator animator = ResolveAnimator();
            float torso = 0.6f;
            Transform head = null;
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

            int applied = 0;
            for (int i = 0; i < generatedCloths.Count; i++)
            {
                var cloth = generatedCloths[i];
                if (!IsHairCloth(cloth))
                    continue; // MeshCloth·BoneClothLoop 스커트는 헤어 튜닝 적용 대상이 아님
                var part = i < generatedParts.Count ? generatedParts[i] : HairPart.Unknown;
                bool isLong = i < generatedIsLong.Count && generatedIsLong[i];
                HairPhysicsParameters.Apply(part, cloth.SerializeData, torso, head, isLong, tuning,
                    usePresetBaseline, springProfile);
                ApplyCulling(cloth);
                NotifyParameterChange(cloth);
                applied++;
            }
            var wireReport = new List<string>();
            WireHairMutualCollision(wireReport);
            lastReport = $"튜닝 재적용 완료: {applied}개 클로스 " +
                $"(sway={tuning.sway:F2}, damp={tuning.dampingScale:F2}, grav={tuning.gravityScale:F2})" +
                (wireReport.Count > 0 ? "\n" + string.Join("\n", wireReport) : "");
            Debug.Log("[CharacterPhysicsSetup] " + lastReport, this);
        }

        /// <summary>
        /// 머리카락용 클로스인지 — 스커트(MC2Cloth_Skirt*)와 VRM 변환 클로스(MC2Cloth_Vrm*)
        /// 는 BoneClothLoop/BoneCloth여도 제외한다. 변환 클로스는 제작자 튜닝이 정본이라
        /// 헤어 템플릿 재적용·상호 충돌 배선 대상이 아니다.
        /// </summary>
        static bool IsHairCloth(MagicaCloth c)
        {
            return c != null &&
                c.name.StartsWith(ClothNamePrefix) &&
                !c.name.StartsWith(ClothNamePrefix + "Skirt") &&
                !c.name.StartsWith(ClothNamePrefix + "Vrm") &&
                c.SerializeData.clothType == ClothProcess.ClothType.BoneCloth;
        }

        /// <summary>
        /// 머리카락 클로스끼리 상호 충돌을 별형으로 연결한다.
        /// MC2는 클로스당 상대를 1개만 지정 가능(한쪽만 설정하면 됨) → 루트 본이
        /// 가장 많은 클로스(주모발)를 앵커로 삼고 나머지가 그쪽으로 충돌하게 한다.
        /// 스커트 클로스(MC2Cloth_Skirt*)는 대상에서 제외.
        /// </summary>
        void WireHairMutualCollision(List<string> report)
        {
            var hairs = generatedCloths.Where(IsHairCloth).ToList();

            // 재적용 대비 — 기존 상호 충돌 연결을 먼저 해제
            foreach (var c in hairs)
            {
                var sc = c.SerializeData.selfCollisionConstraint;
                sc.syncMode = SelfCollisionConstraint.SelfCollisionMode.None;
                sc.syncPartner = null;
            }
            bool mutualOn = springProfile.enabled
                ? springProfile.interChainCollision
                : tuning.useMutualCollision;
            if (!mutualOn || hairs.Count < 2)
                return;

            var anchor = hairs.OrderByDescending(c => c.SerializeData.rootBones.Count).First();
            int linked = 0;
            foreach (var c in hairs)
            {
                var sc = c.SerializeData.selfCollisionConstraint;
                sc.clothMass = 1.0f; // 낮은 값은 떨림 유발 — 공식 예제와 동일하게 최대 질량
                if (c == anchor)
                    continue;
                sc.syncMode = SelfCollisionConstraint.SelfCollisionMode.FullMesh;
                sc.syncPartner = anchor;
                NotifyParameterChange(c);
                linked++;
            }
            NotifyParameterChange(anchor);
            if (linked > 0)
                report.Add($"머리카락 상호 충돌: 앵커 {anchor.name} 기준 {linked}개 클로스 연결");
        }

        /// <summary>
        /// 리스폰·순간이동 후 시뮬레이션을 초기 상태로 리셋한다.
        /// keepPose=true면 현재 자세를 유지한 채 재개, false면 바인드 포즈로 리셋.
        /// </summary>
        /// <param name="keepPose">true=자세 유지 리셋(순간이동 폭주 방지)</param>
        public void ResetPhysics(bool keepPose = false)
        {
            foreach (var cloth in generatedCloths)
            {
                if (cloth != null && cloth.IsValid())
                    cloth.ResetCloth(keepPose);
            }
        }

        /// <summary>
        /// 비활성→활성 전환 시 생성된 클로스의 무결성을 확인한다.
        /// OnEnable 직후에는 MC2 비동기 빌드가 진행 중일 수 있으므로
        /// 짧은 유예 시간 뒤 Update에서 판정한다.
        /// </summary>
        int integrityCheckDelay;

        /// <summary>씬 직렬화 클로스에 현재 컴포넌트 파라미터 재적용 예약.</summary>
        bool pendingParamApply;

        void OnEnable()
        {
            if (Application.isPlaying && generatedCloths.Count > 0)
            {
                graceUntilFrame = Time.frameCount + RebuildGraceFrames;
                integrityCheckDelay = RebuildGraceFrames + 5; // 유예 종료 후 무결성 판정
                // 씬에 직렬화된 클로스는 생성 시점 파라미터를 갖는다 —
                // 튜닝/springProfile 변경이 반영되도록 빌드 완료 후 한 번 재적용한다
                pendingParamApply = true;
            }
        }

        void Update()
        {
            // 다른 에셋이 PlayerLoop를 덮어쓰면 MC2 시뮬레이션이 조용히 멈춘다.
            // InitCustomGameLoop는 이미 등록돼 있으면 즉시 반환하는 멱등 호출.
            if (playerLoopGuard && Application.isPlaying && Time.frameCount % 120 == 0)
                MagicaManager.InitCustomGameLoop();

            // 빌드 완료를 기다리던 지연 셋업 — 빌드 중 클로스가 없어지면 실행
            if (deferredSetup && !generatedCloths.Any(c =>
                    c != null && c.Process.IsState(ClothProcess.State_Build)))
            {
                deferredSetup = false;
                SetupInternal();
                return;
            }
            if (integrityCheckDelay <= 0)
                return;
            if (--integrityCheckDelay > 0)
                return;

            int invalid = 0;
            foreach (var cloth in generatedCloths)
                if (!IsClothAlive(cloth))
                    invalid++;
            if (invalid == 0)
            {
                // 무결성 확인 시점 — 씬 직렬화 클로스의 구 파라미터를 현재 설정으로 한 번 재적용
                if (pendingParamApply)
                {
                    pendingParamApply = false;
                    ReapplyParameters();
                }
                return;
            }
            pendingParamApply = false; // 재생성 경로는 생성 시점 파라미터를 직접 쓴다
            if (autoRebuildOnEnable)
            {
                Debug.Log($"[CharacterPhysicsSetup] 무효 클로스 {invalid}개 — 자동 재설정 실행", this);
                Rebuild();
            }
            else
            {
                Debug.LogWarning($"[CharacterPhysicsSetup] 무효 클로스 {invalid}개 감지. " +
                    "ContextMenu '자동 물리 설정 실행'으로 재설정하거나 autoRebuildOnEnable을 켜세요.", this);
            }
        }

        /// <summary>
        /// 장식물이 매달릴 수 있는 신체 본을 해석한다 — 허리/척추/가슴/윗가슴/목/어깨.
        /// 해석 실패 본은 건너뛰고 중복은 제거한다.
        /// </summary>
        static List<Transform> GarmentAttachmentBones(Func<HumanBodyBones, Transform> resolver)
        {
            var list = new List<Transform>();
            foreach (var b in new[]
            {
                HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest,
                HumanBodyBones.UpperChest, HumanBodyBones.Neck,
                HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
            })
            {
                var t = resolver(b);
                if (t != null && !list.Contains(t))
                    list.Add(t);
            }
            return list;
        }

        /// <summary>생성 클로스에 컬링 옵션을 적용한다 (프리셋 JSON은 컬링을 저장하지 않아 여기서 설정).</summary>
        void ApplyCulling(MagicaCloth cloth)
        {
            var culling = cloth.SerializeData.cullingSettings;
            culling.cameraCullingMode = cameraCullingMode;
            culling.distanceCullingLength.SetValue(distanceCullingMeters > 0f, distanceCullingMeters);
            culling.distanceCullingFadeRatio = Mathf.Clamp01(distanceCullingFadeRatio);
        }

        /// <summary>클로스 비동기 빌드 실패를 리포트에 남긴다.</summary>
        void HandleClothBuildComplete(MagicaCloth cloth, bool success)
        {
            if (success)
                return;
            string clothName = cloth != null ? cloth.name : "?";
            lastReport = $"클로스 비동기 빌드 실패 ({clothName}) — " +
                "SerializeData/메시 상태를 확인하세요";
            Debug.LogWarning("[CharacterPhysicsSetup] " + lastReport, this);
        }

        /// <summary>플레이 중 파라미터 변경을 MC2에 알린다 (공식 API).</summary>
        static void NotifyParameterChange(MagicaCloth cloth)
        {
            if (!Application.isPlaying)
                return;
            cloth.SetParameterChange();
        }

        Animator ResolveAnimator()
        {
            if (targetAnimator != null)
                return targetAnimator;
            targetAnimator = GetComponent<Animator>();
            if (targetAnimator == null)
                targetAnimator = GetComponentInChildren<Animator>(true);
            return targetAnimator;
        }

#if UNITY_EDITOR
        /// <summary>
        /// 현재 생성된 클로스 파라미터를 캐릭터 전용 프리셋 JSON으로 저장한다.
        /// 저장 위치: Assets/Resources/PhysicsPresets/Character/{키}_{클로스명}.json
        /// tuning/skirtTuning의 characterPresetKey에 같은 키를 입력하면 이 파일이
        /// 공식 프리셋보다 우선 로드된다 (튜닝 결과의 캐릭터별 라이브러리화).
        /// </summary>
        [ContextMenu("생성 클로스를 캐릭터 프리셋으로 저장")]
        void ExportCharacterPresets()
        {
            if (generatedCloths.Count == 0)
            {
                lastReport = "저장할 클로스가 없습니다. 자동 물리 설정을 먼저 실행하세요.";
                Debug.LogWarning(lastReport, this);
                return;
            }
            string key = tuning.characterPresetKey;
            if (string.IsNullOrEmpty(key))
                key = skirtTuning.characterPresetKey;
            if (string.IsNullOrEmpty(key))
                key = name;
            string dir = System.IO.Path.Combine(
                Application.dataPath, "Resources/PhysicsPresets/Character");
            System.IO.Directory.CreateDirectory(dir);
            int saved = 0;
            foreach (var cloth in generatedCloths)
            {
                if (cloth == null)
                    continue;
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(dir, $"{key}_{cloth.name}.json"),
                    cloth.SerializeData.ExportJson());
                saved++;
            }
            UnityEditor.AssetDatabase.Refresh();
            lastReport = $"캐릭터 프리셋 {saved}개 저장 (키 '{key}') — " +
                "Resources/PhysicsPresets/Character/. 적용하려면 " +
                "characterPresetKey에 같은 키를 입력하세요.";
            Debug.Log("[CharacterPhysicsSetup] " + lastReport, this);
        }

        /// <summary>
        /// Avatar가 비어있을 때 모델 원본에서 Humanoid Avatar를 찾아 할당한다.
        /// 1순위: SkinnedMeshRenderer.sharedMesh의 원본 에셋(FBX) 안의 Avatar
        /// 2순위: 프리팹 소스와 같은 폴더의 Avatar
        /// 할당 후 Head 본 해결로 호환성을 검증하고, 실패 시 원복한다.
        /// </summary>
        private void ReapplySpringBackend()
        {
            if (SpringHairBackend.ReapplyExisting(transform, springProfile))
            {
                lastReport = "본 스프링 에셋 파라미터 재적용 완료";
                Debug.Log("[CharacterPhysicsSetup] " + lastReport, this);
            }
            else
            {
                lastReport = SpringHairBackend.Available
                    ? "재적용할 본 스프링 에셋 생성물이 없습니다. 자동 물리 설정을 먼저 실행하세요."
                    : "본 스프링 에셋 미설치 — 현재 MC2 경로가 동작 중입니다.";
                Debug.LogWarning(lastReport, this);
            }
        }

        bool TryAssignAvatarFromPrefabSource(Animator animator)
        {
            if (animator.avatar != null)
                return animator.avatar.isHuman;

            var candidatePaths = CollectAvatarCandidatePaths(animator);

            var tried = new List<Avatar>();
            foreach (var path in candidatePaths)
            {
                foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is Avatar avatar && avatar.isHuman && !tried.Contains(avatar))
                        tried.Add(avatar);
                }
            }

            foreach (var avatar in tried)
            {
                animator.avatar = avatar;
                // 호환성 검증: Head 본이 해결되지 않으면 다른 스켈레톤의 Avatar
                if (animator.isHuman && animator.GetBoneTransform(HumanBodyBones.Head) != null)
                    return true;
            }
            animator.avatar = null;
            return false;
        }

        private List<string> CollectAvatarCandidatePaths(Animator animator)
        {
            var candidatePaths = new List<string>();

            // 1) SMR 메시의 원본 에셋 경로 — 프리팹 연결 없이도 정확
            foreach (var smr in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null)
                    continue;
                string p = UnityEditor.AssetDatabase.GetAssetPath(smr.sharedMesh);
                if (!string.IsNullOrEmpty(p))
                    candidatePaths.Add(p);
            }

            // 2) 프리팹 소스와 같은 폴더
            string prefabPath = UnityEditor.PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
            if (string.IsNullOrEmpty(prefabPath))
            {
                var src = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
                if (src != null)
                    prefabPath = UnityEditor.AssetDatabase.GetAssetPath(src);
            }
            if (!string.IsNullOrEmpty(prefabPath))
            {
                string dir = System.IO.Path.GetDirectoryName(prefabPath).Replace('\\', '/');
                foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Avatar", new[] { dir }))
                {
                    string p = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    if (!candidatePaths.Contains(p))
                        candidatePaths.Add(p);
                }
            }
            return candidatePaths;
        }
#endif

        void Finish(List<string> report)
        {
            lastReport = string.Join("\n", report);
            Debug.Log("[CharacterPhysicsSetup]\n" + lastReport, this);
        }
    }
}
