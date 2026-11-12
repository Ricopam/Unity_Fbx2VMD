using System;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 비주얼 포즈 스파이크 스무딩 상태와 계산을 담당함. PoseSpaceRetargeter에서 분리됨.
    internal sealed class RetargetingVisualPoseSmoother
    {
        private bool _hasPreviousVisualPose;
        private float[] _previousVisualPoseMuscles;
        private Vector3 _previousVisualPoseBodyPosition;
        private Quaternion _previousVisualPoseBodyRotation = Quaternion.identity;
        internal int _poseVisualSmoothingCount;
        internal int _poseVisualMuscleDeltaOnlySkippedCount;
        internal float _lastPoseVisualMaxMuscleDelta = float.NaN;
        internal float _maxPoseVisualMaxMuscleDelta;

        internal void ResetMetrics()
        {
            _poseVisualSmoothingCount = 0;
            _poseVisualMuscleDeltaOnlySkippedCount = 0;
            _lastPoseVisualMaxMuscleDelta = float.NaN;
            _maxPoseVisualMaxMuscleDelta = 0f;
        }

        internal void ResetSession()
        {
            _hasPreviousVisualPose = false;
            ResetMetrics();
        }

        internal void ResetVisualPoseHistory()
        {
            _hasPreviousVisualPose = false;
            _previousVisualPoseBodyPosition = Vector3.zero;
            _previousVisualPoseBodyRotation = Quaternion.identity;
        }

        internal void SmoothPoseOnVisualSpike(PoseSpaceRetargeter retargeter, ref HumanPose pose)
        {
            if (!retargeter.smoothPoseOnLegacyAnimationStepSpike || pose.muscles == null || pose.muscles.Length == 0 ||
                !_hasPreviousVisualPose || _previousVisualPoseMuscles == null ||
                _previousVisualPoseMuscles.Length != pose.muscles.Length)
            {
                RememberVisualPose(pose);
                return;
            }

            float maxMuscleDelta = RecordMaxMuscleDelta(pose.muscles);
            float bodyPositionDelta = Vector3.Distance(_previousVisualPoseBodyPosition, pose.bodyPosition);
            float bodyRotationDelta = Quaternion.Angle(_previousVisualPoseBodyRotation, pose.bodyRotation);
            bool shouldSmooth = RetargetingPoseSmoothing.ShouldSmoothVisualPoseSpike(maxMuscleDelta,
                bodyPositionDelta, bodyRotationDelta, retargeter.poseVisualMuscleDeltaThreshold,
                retargeter._legacyAnimationDriver.StepSpikeThisFrame, out bool muscleDeltaOnlySpike);

            if (shouldSmooth)
            {
                BlendSpikedVisualPose(retargeter, ref pose, bodyPositionDelta, bodyRotationDelta);
                _poseVisualSmoothingCount++;
            }
            else if (muscleDeltaOnlySpike)
            {
                // 빠른 손/팔 동작 자체를 smoothing하면 의도한 동작이 멈칫하고 몸통이 늦게 따라오는 것처럼 보인다.
                _poseVisualMuscleDeltaOnlySkippedCount++;
            }

            RememberVisualPose(pose);
        }

        private float RecordMaxMuscleDelta(float[] muscles)
        {
            float maxMuscleDelta = 0f;
            for (int i = 0; i < muscles.Length; i++)
            {
                float delta = Mathf.Abs(muscles[i] - _previousVisualPoseMuscles[i]);
                if (delta > maxMuscleDelta)
                {
                    maxMuscleDelta = delta;
                }
            }

            _lastPoseVisualMaxMuscleDelta = maxMuscleDelta;
            _maxPoseVisualMaxMuscleDelta = Mathf.Max(_maxPoseVisualMaxMuscleDelta, maxMuscleDelta);
            return maxMuscleDelta;
        }

        private void BlendSpikedVisualPose(PoseSpaceRetargeter retargeter, ref HumanPose pose,
            float bodyPositionDelta, float bodyRotationDelta)
        {
            float currentWeight = RetargetingPoseSmoothing.CalculateVisualPoseSpikeCurrentWeight(
                retargeter.poseVisualSpikeCurrentWeight, bodyPositionDelta, bodyRotationDelta,
                retargeter._legacyAnimationDriver.StepSpikeThisFrame);
            ResolveEditorReferenceFlags(retargeter,
                out bool useEditorMuscleReference,
                out bool useCompleteEditorMuscleReference,
                out bool hasEditorBodyRotationReference);
            for (int i = 0; i < pose.muscles.Length; i++)
            {
                pose.muscles[i] = BlendSpikedMuscle(retargeter, i, pose.muscles[i], currentWeight,
                    useEditorMuscleReference, useCompleteEditorMuscleReference);
            }

            pose.bodyPosition = Vector3.Lerp(_previousVisualPoseBodyPosition, pose.bodyPosition, currentWeight);
            if (!RetargetingMuscleReferencePolicy.ShouldPreserveBodyRotationDuringVisualSmoothing(
                hasEditorBodyRotationReference))
            {
                pose.bodyRotation = Quaternion.Slerp(_previousVisualPoseBodyRotation, pose.bodyRotation, currentWeight);
            }
        }

        private static void ResolveEditorReferenceFlags(PoseSpaceRetargeter retargeter,
            out bool useEditorMuscleReference,
            out bool useCompleteEditorMuscleReference,
            out bool hasEditorBodyRotationReference)
        {
            useEditorMuscleReference = false;
#if UNITY_EDITOR
            useEditorMuscleReference = retargeter._useEditorHumanoidMuscleReference;
#endif
            useCompleteEditorMuscleReference = retargeter._useCompleteEditorHumanoidMuscleReference;
            hasEditorBodyRotationReference = retargeter._hasEditorHumanoidPoseReferenceForFrame;
        }

        private float BlendSpikedMuscle(PoseSpaceRetargeter retargeter, int muscleIndex, float currentValue,
            float currentWeight, bool useEditorMuscleReference, bool useCompleteEditorMuscleReference)
        {
            bool hasEditorMuscleReferenceCurve = false;
#if UNITY_EDITOR
            hasEditorMuscleReferenceCurve = retargeter._editorHumanoidMuscleCurves.ContainsKey(muscleIndex);
#endif
            bool shouldPreserveCurrentValue = RetargetingMuscleReferencePolicy.ShouldPreserveHumanoidMuscleDuringVisualSmoothing(
                muscleIndex, useEditorMuscleReference, hasEditorMuscleReferenceCurve, useCompleteEditorMuscleReference);
            bool isForearmStretchMuscle = !shouldPreserveCurrentValue &&
                retargeter.poseVisualSpikeForearmStretchClampMaxOffset > 0f &&
                RetargetingMuscleReferencePolicy.IsForearmStretchMuscle(muscleIndex);
            return RetargetingPoseSmoothing.BlendVisualPoseSpikeMuscle(
                _previousVisualPoseMuscles[muscleIndex], currentValue, currentWeight,
                shouldPreserveCurrentValue, isForearmStretchMuscle,
                retargeter.poseVisualSpikeForearmStretchClampMaxOffset);
        }

        private void RememberVisualPose(HumanPose pose)
        {
            if (pose.muscles == null || pose.muscles.Length == 0 || !PoseSpaceRetargeter.IsFinite(pose))
            {
                return;
            }

            if (_previousVisualPoseMuscles == null || _previousVisualPoseMuscles.Length != pose.muscles.Length)
            {
                _previousVisualPoseMuscles = new float[pose.muscles.Length];
            }

            Array.Copy(pose.muscles, _previousVisualPoseMuscles, pose.muscles.Length);
            _previousVisualPoseBodyPosition = pose.bodyPosition;
            _previousVisualPoseBodyRotation = pose.bodyRotation;
            _hasPreviousVisualPose = true;
        }
    }
}
