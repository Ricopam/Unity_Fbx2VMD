using System;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class HumanoidFootContactStabilizer
    {
        static void ValidateInputs(
            Animator targetAnimator,
            AnimationClip clip,
            Action<float> evaluateTarget,
            HumanoidPoseReferencePlayer sourceReference)
        {
            if (targetAnimator == null)
            {
                throw new ArgumentNullException(nameof(targetAnimator));
            }

            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }

            if (evaluateTarget == null)
            {
                throw new ArgumentNullException(nameof(evaluateTarget));
            }

            if (sourceReference == null || !sourceReference.IsInitialized)
            {
                throw new InvalidOperationException(
                    "발 접촉 계획에는 초기화된 원본 Humanoid 기준이 필요합니다.");
            }
        }

        void SampleFrames(
            int frameCount,
            float frameRate,
            float clipLength,
            Action<float> evaluateTarget,
            HumanoidPoseReferencePlayer sourceReference,
            HumanoidFootContactSample[] sourceSamples,
            HumanoidFootContactSample[] targetSamples)
        {
            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                float timeSeconds = Mathf.Min(frameIndex / frameRate, clipLength);
                SampleFrame(
                    frameIndex, timeSeconds, sourceReference, evaluateTarget,
                    sourceSamples, targetSamples);
            }
        }

        void SampleFrame(
            int frameIndex,
            float timeSeconds,
            HumanoidPoseReferencePlayer sourceReference,
            Action<float> evaluateTarget,
            HumanoidFootContactSample[] sourceSamples,
            HumanoidFootContactSample[] targetSamples)
        {
            sourceSamples[frameIndex] = CaptureSourceSample(
                frameIndex, timeSeconds, sourceReference);
            evaluateTarget(timeSeconds);
            _leftBaseRootSpaceFootPositions[frameIndex] =
                CaptureRootSpacePosition(_leftLeg.CaptureFootPosition());
            _rightBaseRootSpaceFootPositions[frameIndex] =
                CaptureRootSpacePosition(_rightLeg.CaptureFootPosition());
            _leftBaseRootSpaceBendNormals[frameIndex] =
                CaptureRootSpaceDirection(_leftLeg.CaptureBendNormal());
            _rightBaseRootSpaceBendNormals[frameIndex] =
                CaptureRootSpaceDirection(_rightLeg.CaptureBendNormal());
            targetSamples[frameIndex] = new HumanoidFootContactSample(
                _leftLeg.CaptureFootPosition(),
                _leftLeg.CaptureToesPosition(),
                _rightLeg.CaptureFootPosition(),
                _rightLeg.CaptureToesPosition());
        }

        HumanoidFootContactSample CaptureSourceSample(
            int frameIndex,
            float timeSeconds,
            HumanoidPoseReferencePlayer sourceReference)
        {
            if (!sourceReference.TryEvaluateFootSupportPointsAt(
                    timeSeconds,
                    out Vector3 sourceLeftFoot,
                    out Vector3 sourceLeftToes,
                    out Vector3 sourceRightFoot,
                    out Vector3 sourceRightToes))
            {
                throw new InvalidOperationException(
                    "원본 Humanoid 발 접촉점을 샘플링하지 못했습니다.");
            }

            Vector2? relativeFootHeights = null;
            Quaternion? leftFootFrame = null, rightFootFrame = null;
            if (sourceReference.HasFootRotationReference)
            {
                if (!sourceReference.TryCaptureFootSupportReference(out Vector2 heights,
                        out Quaternion leftFrame, out Quaternion rightFrame))
                    throw new InvalidOperationException("원본 Humanoid 발 지지 방향을 샘플링하지 못했습니다.");
                relativeFootHeights = heights;
                leftFootFrame = leftFrame;
                rightFootFrame = rightFrame;
            }

            if (!sourceReference.TryCaptureHipsPose(
                    out Vector3 sourceHipsPosition,
                    out Quaternion sourceHipsRotation))
            {
                throw new InvalidOperationException(
                    "원본 Humanoid 골반 기준을 샘플링하지 못했습니다.");
            }
            _sourceHipsPositions[frameIndex] = sourceHipsPosition;
            _sourceHipsRotations[frameIndex] = sourceHipsRotation;

            return new HumanoidFootContactSample(
                sourceLeftFoot,
                sourceLeftToes,
                sourceRightFoot,
                sourceRightToes,
                relativeFootHeights,
                leftFootFrame,
                rightFootFrame);
        }

        static Vector3 ResolveBendNormal(Vector3 correctionNormal, Vector3 fallbackNormal)
        {
            return correctionNormal.sqrMagnitude > MinimumBendNormalSquaredMagnitude
                ? correctionNormal
                : fallbackNormal;
        }

    }
}
