using System.Linq;
using MagicaCloth2;
using UnityEngine;

namespace Fbx2Vmd.ClothPhysics
{
    /// <summary>
    /// 머리카락 스프링 본과 의상 클로스의 초기화·재설정을 관리함.
    /// </summary>
    public partial class CharacterPhysicsSetup
    {
        // MC2의 비동기 빌드에 유예 시간을 주고 Update에서 판정함.
        int integrityCheckDelay;

        /// <summary>씬 직렬화 클로스에 현재 컴포넌트 파라미터 재적용 예약.</summary>
        bool pendingParamApply;

        void OnEnable()
        {
            if (Application.isPlaying && SpringHairBackend.FindProducts(transform).Any())
            {
                ResolveTorsoAndHead(out float torso, out _);
                ReapplySpringParameters(torso);
            }
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

            CheckIntegrityAndRebuild();
        }

        // 무결성 확인 시점 — 씬 직렬화 클로스의 구 파라미터를 현재 설정으로 한 번 재적용한다.
        private void CheckIntegrityAndRebuild()
        {
            int invalid = 0;
            foreach (var cloth in generatedCloths)
                if (!IsClothAlive(cloth))
                    invalid++;
            if (invalid == 0)
            {
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


        private int ReapplySpringParameters(float torso)
        {
            var springs = SpringHairBackend.FindProducts(transform).ToList();
            foreach (var spring in springs)
                SpringHairBackend.ApplyBodyUnit(spring, torso);
            return springs.Count;
        }

    }
}
