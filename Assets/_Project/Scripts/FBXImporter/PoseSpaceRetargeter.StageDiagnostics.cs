using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 프레임 스테이지 진단 스냅샷/리셋을 담당하는 partial 분할.
    public partial class PoseSpaceRetargeter
    {
        private void ResetRetargetPoseStageDiagnostics()
        {
            _stageMuscleDiagnostics.Reset();
            _setHumanPoseDiagnostics.Input.ClearCore();
            _setHumanPoseDiagnostics.Output.ClearCore();
            _setHumanPoseDiagnostics.ResetPreSolveBasis();
#if UNITY_EDITOR
            _footReferenceDiagnostics.ResetPreSetEndpoint();
#endif
            _setHumanPoseDiagnostics.ResetExtendedMuscles();
            _footReferenceDiagnostics.ResetXzDeltas();
            _lowerBodySegmentDiagnostics.ResetFootXzDeltas();
#if UNITY_EDITOR
            _lowerBodySegmentDiagnostics.ResetDetailed();
#endif
#if UNITY_EDITOR
            _postSetEndpointDiagnostics.Reset();
#endif
            ResetEndpointStageDiagnostics();
        }

        private void ResetEndpointStageDiagnostics()
        {
            _endpointStageDiagnostics.GhostPositions = RetargetEndpointStageWorldPositions.Empty;
            _endpointStageDiagnostics.AfterSetHumanPosePositions = RetargetEndpointStageWorldPositions.Empty;
            _endpointStageDiagnostics.AfterManualReferencesPositions = RetargetEndpointStageWorldPositions.Empty;
            _endpointStageDiagnostics.AfterRootRestorePositions = RetargetEndpointStageWorldPositions.Empty;
            _endpointStageDiagnostics.AfterRootDeltaPositions = RetargetEndpointStageWorldPositions.Empty;
            _endpointStageDiagnostics.AfterGroundingPositions = RetargetEndpointStageWorldPositions.Empty;
            _endpointStageDiagnostics.AfterBipedIKPositions = RetargetEndpointStageWorldPositions.Empty;
            _endpointStageDiagnostics.AfterLateVisualGroundingPositions = RetargetEndpointStageWorldPositions.Empty;
#if UNITY_EDITOR
            _endpointStageDiagnostics.ResetAttribution();
#endif
        }

        private void CaptureSetHumanPosePreSolveBasisDiagnostics(HumanPose pose)
        {
#if UNITY_EDITOR
            _setHumanPoseDiagnostics.CapturePreSolveBasis(new RetargetingSetHumanPosePreSolveBasis
            {
                GhostRootWorldPosition = RetargetingAnimatorPoseReader.ReadRootWorldPosition(ghostAnimator),
                GhostRootWorldRotation = RetargetingAnimatorPoseReader.ReadRootWorldRotation(ghostAnimator),
                TargetRootWorldPosition = RetargetingAnimatorPoseReader.ReadRootWorldPosition(targetAnimator),
                TargetRootWorldRotation = RetargetingAnimatorPoseReader.ReadRootWorldRotation(targetAnimator),
                TargetHipsWorldPosition = RetargetingAnimatorPoseReader.ReadBoneWorldPosition(targetAnimator, HumanBodyBones.Hips),
                TargetHipsLocalPosition = RetargetingAnimatorPoseReader.ReadBoneLocalPosition(targetAnimator, HumanBodyBones.Hips),
                BodyPosition = IsFinite(pose.bodyPosition) ? pose.bodyPosition : BuildNaNVector3(),
                BodyRotation = IsFinite(pose.bodyRotation) ? pose.bodyRotation : BuildNaNQuaternion(),
                GhostEndpointPositions = RetargetingEndpointStageDiagnostics.CapturePositions(ghostAnimator),
                CurrentEndpointPositions = _setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions,
                TargetEndpointPositions = RetargetingEndpointStageDiagnostics.CapturePositions(targetAnimator)
            });
#endif
        }

        private RetargetingSetHumanPoseInputSnapshot ReadSetHumanPoseInputSnapshot(HumanPose pose)
        {
            RetargetingSetHumanPoseInputSnapshot snapshot = CreateCoreInputSnapshot(pose);
            PopulateLowerLimbInputSnapshot(ref snapshot, pose);
            return snapshot;
        }

        private RetargetingSetHumanPoseInputSnapshot CreateCoreInputSnapshot(HumanPose pose)
        {
            return new RetargetingSetHumanPoseInputSnapshot
            {
                LeftShoulderFrontBack = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftShoulderFrontBack),
                LeftArmTwist = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftArmTwist),
                LeftForearmStretch = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftForearmStretch),
                RightForearmStretch = _muscleIndexMap.Read(pose, HumanMuscleSlot.RightForearmStretch),
                RightArmTwist = _muscleIndexMap.Read(pose, HumanMuscleSlot.RightArmTwist),
                LeftUpperLegFrontBack = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftUpperLegFrontBack),
                RightUpperLegFrontBack = _muscleIndexMap.Read(pose, HumanMuscleSlot.RightUpperLegFrontBack),
                LeftLowerLegStretch = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftLowerLegStretch),
                RightLowerLegStretch = _muscleIndexMap.Read(pose, HumanMuscleSlot.RightLowerLegStretch),
                LeftFootUpDown = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftFootUpDown),
                RightFootUpDown = _muscleIndexMap.Read(pose, HumanMuscleSlot.RightFootUpDown),
                SpineFrontBack = _muscleIndexMap.Read(pose, HumanMuscleSlot.SpineFrontBack),
                SpineLeftRight = _muscleIndexMap.Read(pose, HumanMuscleSlot.SpineLeftRight),
                SpineTwistLeftRight = _muscleIndexMap.Read(pose, HumanMuscleSlot.SpineTwistLeftRight),
                ChestFrontBack = _muscleIndexMap.Read(pose, HumanMuscleSlot.ChestFrontBack),
                ChestLeftRight = _muscleIndexMap.Read(pose, HumanMuscleSlot.ChestLeftRight),
                ChestTwistLeftRight = _muscleIndexMap.Read(pose, HumanMuscleSlot.ChestTwistLeftRight),
                UpperChestFrontBack = _muscleIndexMap.Read(pose, HumanMuscleSlot.UpperChestFrontBack),
                UpperChestLeftRight = _muscleIndexMap.Read(pose, HumanMuscleSlot.UpperChestLeftRight),
                UpperChestTwistLeftRight = _muscleIndexMap.Read(pose, HumanMuscleSlot.UpperChestTwistLeftRight)
            };
        }

        private void PopulateLowerLimbInputSnapshot(ref RetargetingSetHumanPoseInputSnapshot snapshot, HumanPose pose)
        {
            snapshot.LeftUpperLegInOut = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftUpperLegInOut);
            snapshot.RightUpperLegInOut = _muscleIndexMap.Read(pose, HumanMuscleSlot.RightUpperLegInOut);
            snapshot.LeftUpperLegTwistInOut = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftUpperLegTwistInOut);
            snapshot.RightUpperLegTwistInOut = _muscleIndexMap.Read(pose, HumanMuscleSlot.RightUpperLegTwistInOut);
            snapshot.LeftLowerLegTwistInOut = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftLowerLegTwistInOut);
            snapshot.RightLowerLegTwistInOut = _muscleIndexMap.Read(pose, HumanMuscleSlot.RightLowerLegTwistInOut);
            snapshot.LeftFootTwistInOut = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftFootTwistInOut);
            snapshot.RightFootTwistInOut = _muscleIndexMap.Read(pose, HumanMuscleSlot.RightFootTwistInOut);
            snapshot.LeftToesUpDown = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftToesUpDown);
            snapshot.RightToesUpDown = _muscleIndexMap.Read(pose, HumanMuscleSlot.RightToesUpDown);
            snapshot.BodyPosition = IsFinite(pose.bodyPosition) ? pose.bodyPosition : BuildNaNVector3();
            snapshot.BodyRotation = IsFinite(pose.bodyRotation) ? pose.bodyRotation : BuildNaNQuaternion();
        }

        private RetargetingSetHumanPoseOutputSnapshot ReadSetHumanPoseOutputSnapshot()
        {
            RetargetingSetHumanPoseOutputSnapshot output = RetargetingSetHumanPoseOutputSnapshot.NaN;
            if (_targetHandler == null)
            {
                return output;
            }

            _targetHandler.GetHumanPose(ref _appliedTargetPose);
            PopulateOutputSnapshotFromAppliedPose(ref output);
            return output;
        }

        private void PopulateOutputSnapshotFromAppliedPose(ref RetargetingSetHumanPoseOutputSnapshot output)
        {
            output.LeftShoulderFrontBack = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.LeftShoulderFrontBack);
            output.LeftArmTwist = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.LeftArmTwist);
            output.LeftForearmStretch = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.LeftForearmStretch);
            output.RightForearmStretch = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.RightForearmStretch);
            output.RightArmTwist = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.RightArmTwist);
            output.LeftUpperLegFrontBack = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.LeftUpperLegFrontBack);
            output.RightUpperLegFrontBack = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.RightUpperLegFrontBack);
            output.LeftLowerLegStretch = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.LeftLowerLegStretch);
            output.RightLowerLegStretch = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.RightLowerLegStretch);
            output.LeftFootUpDown = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.LeftFootUpDown);
            output.RightFootUpDown = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.RightFootUpDown);
            output.RightUpperLegInOut = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.RightUpperLegInOut);
            output.RightUpperLegTwistInOut = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.RightUpperLegTwistInOut);
            output.RightLowerLegTwistInOut = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.RightLowerLegTwistInOut);
            output.RightFootTwistInOut = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.RightFootTwistInOut);
            output.RightToesUpDown = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.RightToesUpDown);
            output.BodyPosition = IsFinite(_appliedTargetPose.bodyPosition)
                ? _appliedTargetPose.bodyPosition
                : BuildNaNVector3();
            output.BodyRotation = IsFinite(_appliedTargetPose.bodyRotation)
                ? _appliedTargetPose.bodyRotation
                : BuildNaNQuaternion();
        }

        private static float CalculateFiniteAbsDelta(float a, float b)
        {
            return IsFinite(a) && IsFinite(b) ? Mathf.Abs(a - b) : float.NaN;
        }

        private static float CalculateFiniteXzDelta(Vector3 a, Vector3 b)
        {
            if (!IsFinite(a) || !IsFinite(b))
            {
                return float.NaN;
            }

            float deltaX = b.x - a.x;
            float deltaZ = b.z - a.z;
            return Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        }

        private static float CalculateFiniteAngleDelta(Quaternion a, Quaternion b)
        {
            return IsFinite(a) && IsFinite(b) ? Quaternion.Angle(a, b) : float.NaN;
        }

        private static float ReadBodyRotationYaw(Quaternion rotation)
        {
            return IsFinite(rotation) ? rotation.eulerAngles.y : float.NaN;
        }
    }
}
