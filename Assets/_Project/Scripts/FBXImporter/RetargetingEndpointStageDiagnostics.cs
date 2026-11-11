using System;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 스테이지별 엔드포인트 월드 위치 스냅샷과 첫 점프 귀속 진단
    internal sealed class RetargetingEndpointStageDiagnostics
    {
        internal RetargetEndpointStageWorldPositions GhostPositions = RetargetEndpointStageWorldPositions.Empty;
        internal RetargetEndpointStageWorldPositions AfterSetHumanPosePositions = RetargetEndpointStageWorldPositions.Empty;
        internal RetargetEndpointStageWorldPositions AfterManualReferencesPositions = RetargetEndpointStageWorldPositions.Empty;
        internal RetargetEndpointStageWorldPositions AfterRootRestorePositions = RetargetEndpointStageWorldPositions.Empty;
        internal RetargetEndpointStageWorldPositions AfterRootDeltaPositions = RetargetEndpointStageWorldPositions.Empty;
        internal RetargetEndpointStageWorldPositions AfterGroundingPositions = RetargetEndpointStageWorldPositions.Empty;
        internal RetargetEndpointStageWorldPositions AfterBipedIKPositions = RetargetEndpointStageWorldPositions.Empty;
        internal RetargetEndpointStageWorldPositions AfterLateVisualGroundingPositions = RetargetEndpointStageWorldPositions.Empty;

        internal string FirstJumpStage { get; private set; } = "";
        internal string FirstJumpEndpoint { get; private set; } = "";
        internal Vector3 FirstJumpDelta { get; private set; } = BuildNaNVector3();
        internal float FirstJumpMagnitude { get; private set; } = float.NaN;

        private static readonly string[] RetargetEndpointStageNames =
        {
            "ghost",
            "after_set_human_pose",
            "after_manual_reference",
            "after_root_restore",
            "after_root_delta",
            "after_grounding",
            "after_biped_ik",
            "after_late_visual_grounding"
        };

        private const float RetargetEndpointStageJumpAttributionThreshold = 0.001f;

        internal static RetargetEndpointStageWorldPositions CapturePositions(Animator animator)
        {
            return new RetargetEndpointStageWorldPositions
            {
                LeftFoot = ReadAnimatorBoneWorldPosition(animator, HumanBodyBones.LeftFoot),
                LeftToes = ReadAnimatorBoneWorldPosition(animator, HumanBodyBones.LeftToes),
                RightFoot = ReadAnimatorBoneWorldPosition(animator, HumanBodyBones.RightFoot),
                RightToes = ReadAnimatorBoneWorldPosition(animator, HumanBodyBones.RightToes)
            };
        }

        internal void CaptureAttribution()
        {
            ResetAttribution();
            bool hasBest = false;
            int bestStageIndex = int.MaxValue;
            TryRecordStageJump(
                "left_foot",
                BuildStagePositions(endpoint => endpoint.LeftFoot),
                ref hasBest,
                ref bestStageIndex);
            TryRecordStageJump(
                "left_toes",
                BuildStagePositions(endpoint => endpoint.LeftToes),
                ref hasBest,
                ref bestStageIndex);
            TryRecordStageJump(
                "right_foot",
                BuildStagePositions(endpoint => endpoint.RightFoot),
                ref hasBest,
                ref bestStageIndex);
            TryRecordStageJump(
                "right_toes",
                BuildStagePositions(endpoint => endpoint.RightToes),
                ref hasBest,
                ref bestStageIndex);
        }

        internal void ResetAttribution()
        {
            FirstJumpStage = "";
            FirstJumpEndpoint = "";
            FirstJumpDelta = BuildNaNVector3();
            FirstJumpMagnitude = float.NaN;
        }

        private delegate Vector3 RetargetEndpointStageSelector(RetargetEndpointStageWorldPositions endpointPositions);

        private Vector3[] BuildStagePositions(RetargetEndpointStageSelector selector)
        {
            return new[]
            {
                selector(GhostPositions),
                selector(AfterSetHumanPosePositions),
                selector(AfterManualReferencesPositions),
                selector(AfterRootRestorePositions),
                selector(AfterRootDeltaPositions),
                selector(AfterGroundingPositions),
                selector(AfterBipedIKPositions),
                selector(AfterLateVisualGroundingPositions)
            };
        }

        private void TryRecordStageJump(
            string endpointName,
            Vector3[] positions,
            ref bool hasBest,
            ref int bestStageIndex)
        {
            if (!RetargetingEndpointDiagnostics.TryFindFirstStagePositionJump(
                    RetargetEndpointStageNames,
                    positions,
                    RetargetEndpointStageJumpAttributionThreshold,
                    out string stage,
                    out Vector3 delta,
                    out float magnitude))
            {
                return;
            }

            int stageIndex = Array.IndexOf(RetargetEndpointStageNames, stage);
            if (stageIndex < 0)
            {
                return;
            }

            if (hasBest && stageIndex >= bestStageIndex)
            {
                return;
            }

            hasBest = true;
            bestStageIndex = stageIndex;
            FirstJumpStage = stage;
            FirstJumpEndpoint = endpointName ?? "";
            FirstJumpDelta = delta;
            FirstJumpMagnitude = magnitude;
        }

        private static Vector3 ReadAnimatorBoneWorldPosition(Animator animator, HumanBodyBones bone)
        {
            if (animator == null)
            {
                return BuildNaNVector3();
            }

            Transform targetBone = animator.GetBoneTransform(bone);
            return targetBone != null ? targetBone.position : BuildNaNVector3();
        }

        private static Vector3 BuildNaNVector3()
        {
            return new Vector3(float.NaN, float.NaN, float.NaN);
        }
    }
}
