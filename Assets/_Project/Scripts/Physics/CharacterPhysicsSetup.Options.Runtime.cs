using System;
using System.Collections.Generic;
using MagicaCloth2;
using UnityEngine;

namespace Fbx2Vmd.ClothPhysics
{
    public partial class CharacterPhysicsSetup
    {

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

        // 클로스 라이브러리 타입을 모르는 어셈블리(설정창 상태 게시)도 수를 읽을 수 있게 노출함.
        public int GeneratedClothCount => generatedCloths.Count;

        /// <summary>빌드가 끝나 시뮬레이션 가능한 클로스 수 — 설정창·점검 도구의 'n/m 생성' 표시용.</summary>
        public int CountValidCloths()
        {
            int count = 0;
            foreach (MagicaCloth cloth in generatedCloths)
            {
                if (cloth != null && cloth.IsValid())
                    count++;
            }
            return count;
        }

    }
}