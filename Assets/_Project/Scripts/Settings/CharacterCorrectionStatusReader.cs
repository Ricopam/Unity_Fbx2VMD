using Fbx2Vmd.ClothPhysics;
using Fbx2Vmd.FBXImporter;
using UnityEngine;

namespace Fbx2Vmd.Settings
{
    /// <summary>
    /// 설정창 보정·물리 화면에 보일 적용 상태를 실제 컴포넌트에서 읽음.
    /// 설정창은 이 값을 읽기 전용으로 표시하므로 판정 기준은 Main 파이프라인 점검(MainPipelineCheck)과 같게 둠.
    /// </summary>
    public static class CharacterCorrectionStatusReader
    {
        public static void Read(MainRecordingSettingsState state, FBXVmdPipeline pipeline)
        {
            if (state == null || pipeline == null)
            {
                return;
            }

            state.footGroundingStatus = pipeline.ImportedMotionFootGroundingStatus;
            ReadCharacter(state, pipeline.targetCharacter);
        }

        public static void ReadCharacter(MainRecordingSettingsState state, GameObject character)
        {
            if (state == null || character == null)
            {
                return;
            }

            var thumbGuard = character.GetComponent<HumanoidThumbDeformationGuard>();
            // 에디터 정지 상태에서도 같은 판정이 되도록 isActiveAndEnabled 대신 직접 조합함.
            state.isThumbGuardEnabled = thumbGuard != null && thumbGuard.enabled && thumbGuard.gameObject.activeInHierarchy;

            var physics = character.GetComponent<CharacterPhysicsSetup>();
            state.clothCount = physics != null ? physics.GeneratedClothCount : 0;
            state.validClothCount = physics != null ? physics.CountValidCloths() : 0;
            state.hairSpringCount = character.GetComponentsInChildren<SpringHairDynamics>(true).Length;
        }
    }
}
