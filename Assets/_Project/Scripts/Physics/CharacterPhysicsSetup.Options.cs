using System;
using System.Collections.Generic;
using MagicaCloth2;
using UnityEngine;

namespace Fbx2Vmd.ClothPhysics
{
    public partial class CharacterPhysicsSetup
    {
        /// <summary>스커트 클로스 생성 방식</summary>
        public enum SkirtMode
        {
            /// <summary>렌더러 메시에서 버텍스 속성을 계산해 MeshCloth 생성 (기존 경로)</summary>
            MeshCloth = 0,
            /// <summary>스커트 체인 루트를 루프 면으로 연결하는 BoneCloth 생성 (SequentialLoopMesh)</summary>
            BoneClothLoop = 1,
            /// <summary>루프 성립 가능(루트 3+)이면 BoneClothLoop, 아니면 MeshCloth 폴백</summary>
            Auto = 2,
        }

        [Header("스커트 옵션")]
        [Tooltip("스커트 본 감지 시 클로스를 자동 생성")]
        public bool autoSkirtCloth = true;

        [Tooltip("스커트 생성 방식. BoneClothLoop는 본을 면으로 잇는 저부하 경로 " +
                 "(프록시=본 수십 개, MeshCloth는 버텍스 수천~만). Auto는 루프 불가 시 MeshCloth 폴백")]
        public SkirtMode skirtMode = SkirtMode.MeshCloth;

        [Tooltip("버텍스가 스커트로 분류되는 스커트 본 가중치 합 임계값")]
        [Range(0.1f, 1f)]
        public float skirtWeightThreshold = 0.5f;

        [Tooltip("체인 루트부터 이 깊이까지의 스커트 본에 붙은 버텍스를 고정(Fixed) — 허리 고정부")]
        [Range(0, 3)]
        public int skirtFixedChainDepth = 0;

        [Tooltip("스커트 프록시 메시 단순화 배율. 1.0 = 기본(체형 비례). 크면 입자 수가 줄어 성능↑ 디테일↓")]
        [Range(0.5f, 3.0f)]
        public float skirtReductionScale = 1.0f;

        [Tooltip("스커트에 백스톱 적용 — 스킨 포즈 기준 내측 진입을 제한해 다리 관통을 막는다. " +
                 "MC2 2.18.1에는 Penetration 제약이 없어 이것이 대체 수단. " +
                 "옷이 다리 사이로 들어가는 정상 동작까지 딱딱해지면 끈다")]
        public bool useSkirtBackstop = true;

        [Tooltip("스커트 백스톱 허용 거리(m). 작을수록 스킨 포즈에 강하게 붙는다")]
        [Min(0f)]
        public float skirtBackstopDistance = 0.005f;

        [Tooltip("스커트 깊이 커브·포즈 반영 튜닝. MeshCloth/BoneClothLoop 경로에 공통 적용")]
        public SkirtTuning skirtTuning = SkirtTuning.Default;

        [Tooltip("클립이 스커트 본을 구동하지 않을 때(휴머노이드 변환 등) 커스텀 스키닝으로 " +
                 "허리·다리 본에 자동 스키닝 — 백스톱/포즈 제약이 실제 자세를 따르게 함")]
        public bool skirtSkinningFallback = true;

        [Header("베이스라인 / 컬링")]
        [Tooltip("공식 MC2 프리셋(JSON)을 베이스라인으로 먼저 가져와 관리 필드 외도 공식값으로 채운다. " +
                 "끄면 기존 수작업 파라미터 경로만 사용")]
        public bool usePresetBaseline = true;


        private void ReportSkirtPose(SkirtAnimationProbe.Verdict poseVerdict,
            string poseDetail, bool isBonePath, List<string> report)
        {
            if (poseVerdict != SkirtAnimationProbe.Verdict.Static)
                report.Add($"스커트 포즈 판정: {poseDetail}");
            else if (!skirtSkinningFallback)
                report.Add($"스커트 포즈 판정: {poseDetail} — 커스텀 스키닝 폴백 꺼짐");
            else if (isBonePath)
                // BoneCloth는 정점=본이라 애니메이션 포즈가 본 자체 —
                // 커스텀 스키닝(메시 정점 재스키닝)은 의미 없음
                report.Add($"스커트 포즈 판정: {poseDetail} — BoneCloth 경로는 커스텀 스키닝 불필요");
            else
                report.Add($"스커트 포즈 판정: {poseDetail} → 커스텀 스키닝 폴백");
        }


        private bool TryResolveByNamePattern(
            Animator animator, List<string> report,
            out System.Func<HumanBodyBones, Transform> resolver,
            out HashSet<Transform> bodyBones, out float torso)
        {
            resolver = null;
            bodyBones = null;
            torso = 0f;
            var nameMap = NamePatternBoneResolver.Resolve(animator.transform);
            if (!nameMap.TryGetValue(HumanBodyBones.Head, out _) ||
                !nameMap.TryGetValue(HumanBodyBones.Hips, out _))
            {
                lastReport = "실패: Avatar가 Humanoid가 아니고 본 이름 패턴으로도 " +
                    "Head/Hips를 해석할 수 없습니다. (수동 본 지정 필요)\n" +
                    string.Join("\n", report);
                Debug.LogError(lastReport, this);
                return false;
            }
            resolver = b => nameMap.TryGetValue(b, out var t) ? t : null;
            bodyBones = new HashSet<Transform>(nameMap.Values);
            torso = BodyColliderBuilder.MeasureTorso(
                nameMap[HumanBodyBones.Hips], nameMap[HumanBodyBones.Head]);
            report.Add($"Humanoid 아님 — 이름 패턴 폴백으로 신체 본 {nameMap.Count}개 해석");
            return true;
        }


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


        void Finish(List<string> report)
        {
            lastReport = string.Join("\n", report);
            Debug.Log("[CharacterPhysicsSetup]\n" + lastReport, this);
        }

    }
}
