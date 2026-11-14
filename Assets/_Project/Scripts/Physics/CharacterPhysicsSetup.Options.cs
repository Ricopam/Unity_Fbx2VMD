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

        [Tooltip("카메라 컬링 모드. AnimatorLinkage = 연동 Animator의 CullingMode를 따라감")]
        public CullingSettings.CameraCullingMode cameraCullingMode =
            CullingSettings.CameraCullingMode.AnimatorLinkage;

        [Tooltip("이 거리(m)를 넘는 클로스 시뮬레이션을 끈다. 0 = 거리 컬링 끔(녹화·소규모 씬 기본값)")]
        [Min(0f)]
        public float distanceCullingMeters = 0f;

        [Tooltip("거리 컬링 종료 직전 페이드 구간 비율(0~1)")]
        [Range(0f, 1f)]
        public float distanceCullingFadeRatio = 0.2f;

        [Header("런타임 옵션")]
        [Tooltip("MC2 Pre-build 사용. 활성화하려면 생성된 MagicaCloth 인스펙터에서 Pre-build 데이터도 함께 생성해야 함")]
        public bool usePreBuild = false;

        [Tooltip("비활성→활성 전환 시 클로스가 무효면 자동 재설정. MC2는 비활성 시 팀을 해제해 재활성만으로는 복구되지 않으므로 기본 켜기")]
        public bool autoRebuildOnEnable = true;

        [Tooltip("다른 에셋이 PlayerLoop를 덮어써 MC2 시뮬레이션이 멈추는 경우를 대비해 " +
                 "주기적으로 MagicaManager.InitCustomGameLoop()를 재호출한다 (등록 확인 후 누락 시에만 재등록)")]
        public bool playerLoopGuard = true;

        [Header("결과 (읽기 전용)")]
        public List<ColliderComponent> generatedColliders = new List<ColliderComponent>();
        public List<MagicaCloth> generatedCloths = new List<MagicaCloth>();
        public List<HairPart> generatedParts = new List<HairPart>();
        public List<bool> generatedIsLong = new List<bool>();
        [TextArea]
        [System.NonSerialized] public string lastReport = "";
    }
}
