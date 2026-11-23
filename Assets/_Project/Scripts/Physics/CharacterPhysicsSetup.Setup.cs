using System.Collections.Generic;
using System.Linq;
using MagicaCloth2;
using UnityEngine;

namespace Fbx2Vmd.ClothPhysics
{
    public partial class CharacterPhysicsSetup
    {
        private BodyColliderBuilder.BuildResult BuildBodyColliders(Animator animator,
            System.Func<HumanBodyBones, Transform> resolver, float torso,
            Dictionary<Transform, List<Vector3>> boneVerts, List<string> report)
        {
            var colResult = BodyColliderBuilder.Build(
                resolver, torso, boneVerts, animator.isHuman && useSymmetry,
                includeLegs, colliderRadiusScale);
            generatedColliders = colResult.colliders;
            report.Add($"콜라이더 {colResult.colliders.Count}개 생성");
            report.AddRange(colResult.warnings.Select(w => "경고: " + w));
            return colResult;
        }

        private void BuildHairAndAccessoryDynamics(HairChainDetector.Result detect,
            int headHairChainCount, float torso, Transform head,
            BodyColliderBuilder.BuildResult colResult, List<string> report)
        {
            var hair = FilterHairChains(detect.chains.Take(headHairChainCount), report);
            int springRoots = SpringHairBackend.Build(
                transform, hair, colResult, torso);
            report.Add($"머리카락 스프링 본 루트 {springRoots}개 생성");
            if (springRoots == 0 && hair.Count > 0)
                report.Add("주의: 스프링 본으로 구동할 수 있는 머리카락 체인이 없습니다.");

            var accessories = detect.chains.Skip(headHairChainCount).GroupBy(c => c.part);
            BuildAccessoryCloths(accessories, colResult, torso, head, report);
        }

        private List<HairChain> FilterHairChains(IEnumerable<HairChain> chains, List<string> report)
        {
            var hair = chains.ToList();
            if (hairPhysicsParts == HairPartMask.All)
                return hair;
            foreach (var part in hair.Select(c => c.part).Distinct()
                .Where(part => !hairPhysicsParts.HasFlag(part.ToMask())))
                report.Add($"{part}: 부위 마스크로 스킵 — 본이 애니메이션 포즈를 추종");
            return hair.Where(c => hairPhysicsParts.HasFlag(c.part.ToMask())).ToList();
        }

        private void BuildAccessoryCloths(
            IEnumerable<IGrouping<HairPart, HairChain>> groups,
            BodyColliderBuilder.BuildResult colResult, float torso, Transform head,
            List<string> report)
        {
            foreach (var group in groups)
            {
                var cloth = CreateAccessoryCloth(
                    group.Key, group.ToList(), colResult.byCategory, torso, head, report);
                if (cloth != null)
                    generatedCloths.Add(cloth);
            }
        }

        private bool ResolveBoneContext(Animator animator, List<string> report,
            out System.Func<HumanBodyBones, Transform> resolver,
            out HashSet<Transform> bodyBones, out float torso)
        {
            resolver = null;
            bodyBones = null;
            torso = 0f;
            if (animator == null)
            {
                lastReport = "실패: Animator를 찾을 수 없습니다.";
                Debug.LogError(lastReport, this);
                return false;
            }
            // 프리팹 Animator에 Avatar가 비어있으면 원본 모델에서 복구 시도 (에디터 전용)
            TryRecoverAvatarFromPrefab(animator, report);

            // Humanoid면 Avatar 매핑으로, 아니면 본 이름 패턴으로 해석한다.
            bool humanoid = animator.isHuman;
            if (humanoid)
            {
                resolver = animator.GetBoneTransform;
                bodyBones = BodyColliderBuilder.CollectBodyBoneSet(animator);
                torso = BodyColliderBuilder.MeasureTorso(animator);
            }
            else if (!TryResolveByNamePattern(
                    animator, report,
                    out resolver, out bodyBones, out torso))
            {
                return false;
            }
            return true;
        }

        private void TryRecoverAvatarFromPrefab(Animator animator, List<string> report)
        {
#if UNITY_EDITOR
            if (!animator.isHuman && TryAssignAvatarFromPrefabSource(animator))
                report.Add("Avatar를 원본 모델에서 복구해 할당했습니다.");
#endif
        }

        private HashSet<Transform> CollectNativeSprings(
            Animator animator, Transform head, float torso, List<string> report)
        {
            // 네이티브 머리카락과 프로젝트가 생성하는 스프링의 이중 구동을 막음.
            var externalOwned = NativeSpringBoneProbe.CollectOwnedBones(animator.transform, transform);
            if (vrmConversionMode == VrmConversionMode.ConvertToMc2)
                ApplyVrmConversion(animator, head, torso, report);
            return externalOwned;
        }

        private HairChainDetector.Result DetectAndFilterChains(Transform head,
            HashSet<Transform> bodyBones, HashSet<Transform> externalOwned,
            List<string> report)
        {
            if (head == null)
            {
                report.Add("경고: Head 본이 없어 머리카락 설정을 건너뜀");
                return null;
            }
            var detect = HairChainDetector.Detect(
                head, t => bodyBones.Contains(t), explicitHairRoots, includeUnknownBones);

            if (externalOwned.Count > 0)
            {
                // 루트뿐 아니라 체인 중간의 소유 본도 검사 — 일부만 네이티브 소유인
                // 체인은 통째로 네이티브에 맡긴다 (본 단위 이중 시뮬레이션 방지)
                int removed = detect.chains.RemoveAll(
                    c => c.bones.Any(b => b != null && externalOwned.Contains(b)));
                if (removed > 0)
                    report.Add($"네이티브 스프링본 소유 머리 체인 {removed}개 스킵 (이중 물리 방지)");
            }

            if (detect.skipped.Count > 0)
                report.Add($"미분류 본 {detect.skipped.Count}개 건너뜀: " +
                    string.Join(", ", detect.skipped.Select(t => t.name).Take(10)));
            return detect;
        }

        // C-2 변환 — 제작자 튜닝을 MC2 클로스로 이식하고 원본을 비활성.
        // 비활성 스프링은 ClearGenerated가 복원하므로 재실행·GuardOnly 복귀 모두 안전하다.
        private void ApplyVrmConversion(Animator animator, Transform head, float torso, List<string> report)
        {
            float unit = torso > 0f ? torso / 0.6f : 1f;
            var conv = VrmSpringBoneConverter.ConvertAll(
                transform, animator.transform, unit, report, spring =>
                    !SpringHairBackend.IsGeneratedObject(transform, spring.transform) &&
                    spring.RootBones != null &&
                    spring.RootBones.All(root => root == null || !IsHairRoot(root, head)));
            foreach (var cloth in conv.cloths)
            {
                cloth.GetSerializeData2().preBuildData.enabled = usePreBuild;
                ApplyCulling(cloth);
                cloth.OnBuildComplete += HandleClothBuildComplete;
                generatedCloths.Add(cloth);
                generatedParts.Add(HairPart.Unknown);
                generatedIsLong.Add(false);
            }
            generatedColliders.AddRange(conv.colliders);
            convertedSprings.AddRange(conv.disabledSprings);
            foreach (var u in conv.unsupported)
                report.Add($"VRM 미지원: {u}");
            if (conv.disabledSprings.Count == 0)
                report.Add("주의: ConvertToMc2인데 변환된 스프링이 없음 — " +
                    "네이티브 소유 본 제외는 유지되므로 해당 체인에 클로스가 생성되지 않을 수 있음");
        }

        private bool IsHairRoot(Transform root, Transform head)
        {
            return (head != null && (root.IsChildOf(head) || head.IsChildOf(root))) ||
                explicitHairRoots.Any(hair => hair != null &&
                    (root.IsChildOf(hair) || hair.IsChildOf(root)));
        }

        private void CollectAccessoryChains(Animator animator,
            System.Func<HumanBodyBones, Transform> resolver,
            HashSet<Transform> bodyBones,
            Dictionary<Transform, List<Vector3>> boneVerts,
            HashSet<Transform> externalOwned,
            Dictionary<Transform, int> skirtBoneDepths, Transform head,
            HairChainDetector.Result detect,
            ref int headHairChainCount, List<string> report)
        {
            // 2.5. 신체 측(허리/가슴 등)에 매달린 장식물 체인 탐지 — 머리 외 부착점
            var accRoots = new List<Transform>();
            var accClaimed = new HashSet<Transform>();
            CollectAccessoryRootsByName(
                animator, head, bodyBones, externalOwned, detect,
                accRoots, accClaimed);

            // 2.6. 이름에 안 잡힌 신체측 체인 — 토폴로지+스키닝 폴백 (일반 FBX/VRM용)
            if (detectGarmentChainsByTopology)
                AppendTopologyAccessoryRoots(resolver, bodyBones, accClaimed,
                    externalOwned, skirtBoneDepths, detect, boneVerts,
                    accRoots, report);

            // 중첩 루트 제거 — 상위 루트 서브트리에 속한 루트를 빼 같은 본의 이중 시뮬레이션을 막는다
            var accRootSet = new HashSet<Transform>(accRoots);
            accRoots.RemoveAll(r => accRootSet.Any(o => o != r && r.IsChildOf(o)));

            if (accRoots.Count > 0)
            {
                AdoptAccessoryChains(accRoots, head, bodyBones, externalOwned,
                    detect, ref headHairChainCount);
            }
        }

        // 헤어 체인 본 집합 — 명시 루트가 Head 밖을 가리키는 경우 acc 스캔과의
        // 중복 채택(같은 본이 두 클로스 루트)을 막는다
        private void CollectAccessoryRootsByName(
            Animator animator, Transform head, HashSet<Transform> bodyBones,
            HashSet<Transform> externalOwned, HairChainDetector.Result detect,
            List<Transform> accRoots, HashSet<Transform> accClaimed)
        {
            var hairBones = new HashSet<Transform>();
            foreach (var c in detect.chains)
                foreach (var b in c.bones)
                    hairBones.Add(b);
            foreach (var t in animator.GetComponentsInChildren<Transform>(true))
            {
                if (accClaimed.Contains(t) || hairBones.Contains(t) || t.IsChildOf(head)
                    || bodyBones.Contains(t)
                    || t.name.StartsWith("MC2") || externalOwned.Contains(t))
                    continue; // 상위 장식물 체인 내부·머리 쪽·신체 본·생성물·네이티브 소유 본은 제외
                if (t.childCount > 0 && HairBoneClassifier.Classify(t.name) == HairPart.Accessory)
                {
                    accRoots.Add(t);
                    foreach (var d in t.GetComponentsInChildren<Transform>(true))
                        accClaimed.Add(d); // 하위 체인 본은 중복 루트로 잡지 않음
                }
            }
        }

        // 네이티브 소유 본은 체인 수집에서도 절단 — 루트가 미소유여도 중간 본이
        // 섞여 들어가면 그 본만큼은 이중 물리가 된다
        private void AdoptAccessoryChains(
            List<Transform> accRoots, Transform head,
            HashSet<Transform> bodyBones, HashSet<Transform> externalOwned,
            HairChainDetector.Result detect, ref int headHairChainCount)
        {
            var acc = HairChainDetector.Detect(
                head, x => bodyBones.Contains(x) || externalOwned.Contains(x), accRoots);
            // 신체측 체인에는 헤어 부위 분류(위치 추정 포함)를 적용하지 않는다
            foreach (var ch in acc.chains)
                ch.part = HairPart.Accessory;
            headHairChainCount = detect.chains.Count;
            detect.chains.AddRange(acc.chains);
            detect.skipped.AddRange(acc.skipped);
        }

        private void AppendTopologyAccessoryRoots(
            System.Func<HumanBodyBones, Transform> resolver,
            HashSet<Transform> bodyBones, HashSet<Transform> accClaimed,
            HashSet<Transform> externalOwned,
            Dictionary<Transform, int> skirtBoneDepths,
            HairChainDetector.Result detect,
            Dictionary<Transform, List<Vector3>> boneVerts,
            List<Transform> accRoots, List<string> report)
        {
            var claimed = new HashSet<Transform>(bodyBones);
            claimed.UnionWith(accClaimed);
            claimed.UnionWith(externalOwned);
            foreach (var sb in skirtBoneDepths.Keys)
                claimed.Add(sb);
            foreach (var chain in detect.chains)
                foreach (var b in chain.bones)
                    claimed.Add(b);

            var garment = GarmentChainDetector.Detect(
                GarmentAttachmentBones(resolver), t => claimed.Contains(t),
                t => bodyBones.Contains(t), boneVerts);
            accRoots.AddRange(garment.roots);
            report.AddRange(garment.report); // 채택/스킵 사유를 모두 남긴다 (미탐지 진단용)
        }

        private void BuildSkirtCloth(Animator animator,
            System.Func<HumanBodyBones, Transform> resolver,
            SkinnedMeshRenderer[] smrs, float torso,
            Dictionary<Transform, int> skirtBoneDepths,
            BodyColliderBuilder.BuildResult colResult, List<string> report)
        {
            // 스커트 본 감지 → 클로스 자동 생성
            if (skirtBoneDepths.Count <= 0)
                return;
            if (!autoSkirtCloth)
            {
                report.Add($"스커트 본 {skirtBoneDepths.Count}개 감지 — autoSkirtCloth 꺼짐, 수동 설정 필요");
                return;
            }
            var skirtColliders = HairPartColliderMapper.Select(
                ColliderCategory.Waist | ColliderCategory.Hips | ColliderCategory.Legs
                    | ColliderCategory.Feet | ColliderCategory.Hands,
                colResult.byCategory, 32);

            // 스커트 본의 클립 구동 여부 판정 — 비구동이면 백스톱/포즈 제약의
            // 기준이 바인드 포즈에 고정되므로 커스텀 스키닝 폴백을 검토한다.
            var poseVerdict = SkirtAnimationProbe.EvaluateAnimator(
                animator, skirtBoneDepths.Keys.ToList(), out var poseDetail);
            bool tryCustomSkinning =
                poseVerdict == SkirtAnimationProbe.Verdict.Static && skirtSkinningFallback;
            var skinningBones = tryCustomSkinning
                ? SkirtAnimationProbe.ResolveSkinningBones(resolver)
                : null;
            if (TryBuildSkirtLoopCloth(skirtBoneDepths, skirtColliders,
                    poseVerdict, poseDetail, report))
                return;

            CreateSkirtMeshCloth(smrs, skirtBoneDepths, skirtColliders, torso,
                skinningBones, poseVerdict, poseDetail, report);
        }

        private bool TryBuildSkirtLoopCloth(
            Dictionary<Transform, int> skirtBoneDepths,
            List<ColliderComponent> skirtColliders,
            SkirtAnimationProbe.Verdict poseVerdict, string poseDetail,
            List<string> report)
        {
            // BoneCloth 루프 경로 — 정점=본이라 백스톱 전제(애니메이션 포즈)가 성립,
            // 프록시가 본 수십 개 수준이라 MeshCloth보다 부하가 낮다.
            if (skirtMode == SkirtMode.MeshCloth)
                return false;
            List<Transform> loopRoots = SkirtBoneClothBuilder.CollectChainRootsOrdered(
                transform, skirtBoneDepths);
            if (!SkirtBoneClothBuilder.CanBuildLoop(loopRoots))
                return false;
            MagicaCloth loopCloth = SkirtBoneClothBuilder.Create(
                transform, loopRoots, skirtColliders,
                useSkirtBackstop, skirtBackstopDistance,
                usePresetBaseline, skirtTuning);
            if (loopCloth == null)
                return false;
            loopCloth.GetSerializeData2().preBuildData.enabled = usePreBuild;
            ApplyCulling(loopCloth);
            loopCloth.OnBuildComplete += HandleClothBuildComplete;
            generatedCloths.Add(loopCloth);
            generatedParts.Add(HairPart.Unknown);
            generatedIsLong.Add(false);
            report.Add($"스커트 BoneCloth(SequentialLoopMesh) 자동 생성: " +
                $"체인 루트 {loopRoots.Count}개 (콜라이더 {skirtColliders.Count}개)");
            ReportSkirtPose(poseVerdict, poseDetail, true, report);
            return true;
        }

        private void CreateSkirtMeshCloth(SkinnedMeshRenderer[] smrs,
            Dictionary<Transform, int> skirtBoneDepths,
            List<ColliderComponent> skirtColliders, float torso,
            List<Transform> skinningBones,
            SkirtAnimationProbe.Verdict poseVerdict, string poseDetail,
            List<string> report)
        {
            if (skirtMode == SkirtMode.BoneClothLoop)
                report.Add("BoneClothLoop 지정이나 루프 불성립(루트 3개 미만) — MeshCloth로 폴백");
            // MeshCloth 경로 (vertexAttributeList로 고정/이동 자동 결정)
            var sr = SkirtClothBuilder.Create(
                transform, smrs, skirtBoneDepths, skirtColliders, torso,
                skirtWeightThreshold, skirtFixedChainDepth,
                skirtReductionScale, useSkirtBackstop, skirtBackstopDistance,
                usePresetBaseline, skirtTuning);
            if (sr.cloth != null)
            {
                RegisterSkirtCloth(sr, skinningBones, skirtColliders, report);
            }
            else
            {
                report.Add($"스커트 본 {skirtBoneDepths.Count}개 감지 — 스커트 버텍스를 찾지 못해 " +
                    "클로스 미생성 (메시가 스커트 본에 스키닝되지 않았거나 Read/Write 필요)");
            }
            ReportSkirtPose(poseVerdict, poseDetail, false, report);
        }

        private void RegisterSkirtCloth(
            SkirtClothBuilder.Result sr, List<Transform> skinningBones,
            List<ColliderComponent> skirtColliders, List<string> report)
        {
            sr.cloth.GetSerializeData2().preBuildData.enabled = usePreBuild;
            if (skinningBones != null &&
                SkirtAnimationProbe.ApplyCustomSkinning(
                    sr.cloth.SerializeData, skinningBones) == 0)
                report.Add("경고: 커스텀 스키닝 폴백 요청됐으나 신체 본 해석 실패 — 폴백 불가");
            ApplyCulling(sr.cloth);
            sr.cloth.OnBuildComplete += HandleClothBuildComplete;
            generatedCloths.Add(sr.cloth);
            generatedParts.Add(HairPart.Unknown);
            generatedIsLong.Add(false);
            report.Add($"스커트 MeshCloth 자동 생성: 고정 {sr.fixedVertexCount} + " +
                $"이동 {sr.moveVertexCount} 버텍스 ({sr.rendererCount}개 렌더러, " +
                $"콜라이더 {skirtColliders.Count}개)");
            if (sr.extractedMeshes.Count > 0)
                report.Add($"스커트 메시 추출 {sr.extractedMeshes.Count}개 " +
                    "(65535 버텍스 한도 대응 — 원본 메시에서 스커트 삼각형 분리)");
            foreach (var w in sr.warnings)
                report.Add("경고: " + w);
        }
    }
}
