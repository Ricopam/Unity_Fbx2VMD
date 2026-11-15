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
            if (!sourceReference.TryEvaluateFootSupportPointsAt( timeSeconds, out Vector3 sourceLeftFoot, out Vector3 sourceLeftToes, out Vector3 sourceRightFoot, out Vector3 sourceRightToes))
            {
                throw new InvalidOperationException( "원본 Humanoid 발 접촉점을 샘플링하지 못했습니다.");
            }

            Vector2? relativeFootHeights = null;
            Quaternion? leftFootFrame = null, rightFootFrame = null;
            if (sourceReference.HasFootRotationReference)
            {
                if (!sourceReference.TryCaptureFootSupportReference(out Vector2 heights, out Quaternion leftFrame, out Quaternion rightFrame))
                    throw new InvalidOperationException("원본 Humanoid 발 지지 방향을 샘플링하지 못했습니다.");
                relativeFootHeights = heights;
                leftFootFrame = leftFrame;
                rightFootFrame = rightFrame;
            }

            if (!sourceReference.TryCaptureHipsPose( out Vector3 sourceHipsPosition, out Quaternion sourceHipsRotation))
            {
                throw new InvalidOperationException( "원본 Humanoid 골반 기준을 샘플링하지 못했습니다.");
            }
            _sourceHipsPositions[frameIndex] = sourceHipsPosition;
            _sourceHipsRotations[frameIndex] = sourceHipsRotation;

            return new HumanoidFootContactSample( sourceLeftFoot, sourceLeftToes, sourceRightFoot, sourceRightToes, relativeFootHeights, leftFootFrame, rightFootFrame);
        }

        static Vector3 ResolveBendNormal(Vector3 correctionNormal, Vector3 fallbackNormal)
        {
            return correctionNormal.sqrMagnitude > MinimumBendNormalSquaredMagnitude
                ? correctionNormal
                : fallbackNormal;
        }


        bool TryResolveWorldCorrections(
            Vector3 leftRootSpaceCorrection,
            Vector3 rightRootSpaceCorrection,
            Vector3 leftRootSpaceToeDirection,
            Vector3 rightRootSpaceToeDirection,
            Vector3 leftBaseRootSpacePosition,
            Vector3 rightBaseRootSpacePosition,
            Vector3 leftBaseRootSpaceBendNormal,
            Vector3 rightBaseRootSpaceBendNormal,
            out WorldCorrections corrections)
        {
            Vector3 leftCurrentRootSpacePosition =
                CaptureRootSpacePosition(_leftLeg.CaptureFootPosition());
            Vector3 rightCurrentRootSpacePosition =
                CaptureRootSpacePosition(_rightLeg.CaptureFootPosition());
            corrections = new WorldCorrections
            {
                LeftCorrection = _targetRoot.TransformDirection(
                    leftBaseRootSpacePosition + leftRootSpaceCorrection -
                    leftCurrentRootSpacePosition),
                RightCorrection = _targetRoot.TransformDirection(
                    rightBaseRootSpacePosition + rightRootSpaceCorrection -
                    rightCurrentRootSpacePosition),
                LeftToeDirection =
                    _targetRoot.TransformDirection(leftRootSpaceToeDirection),
                RightToeDirection =
                    _targetRoot.TransformDirection(rightRootSpaceToeDirection),
                LeftBendNormal = _targetRoot.TransformDirection(
                    leftBaseRootSpaceBendNormal),
                RightBendNormal = _targetRoot.TransformDirection(
                    rightBaseRootSpaceBendNormal)
            };
            return IsFinite(corrections.LeftCorrection) &&
                IsFinite(corrections.RightCorrection) &&
                IsFinite(corrections.LeftToeDirection) &&
                IsFinite(corrections.RightToeDirection);
        }

        private Vector3 CaptureRootSpacePosition(Vector3 worldPosition)
        {
            return Quaternion.Inverse(_targetRoot.rotation) *
                (worldPosition - _targetRoot.position);
        }

        private Vector3 CaptureRootSpaceDirection(Vector3 worldDirection)
        {
            return Quaternion.Inverse(_targetRoot.rotation) * worldDirection;
        }

        private bool TryEvaluateBaseFootPositions(
            float timeSeconds,
            out Vector3 leftRootSpacePosition,
            out Vector3 rightRootSpacePosition,
            out Vector3 leftRootSpaceBendNormal,
            out Vector3 rightRootSpaceBendNormal)
        {
            leftRootSpacePosition = Vector3.zero;
            rightRootSpacePosition = Vector3.zero;
            leftRootSpaceBendNormal = Vector3.zero;
            rightRootSpaceBendNormal = Vector3.zero;
            if (!IsFinite(timeSeconds) || timeSeconds < 0f || _frameRate <= 0f || _leftBaseRootSpaceFootPositions.Length == 0 || _leftBaseRootSpaceFootPositions.Length !=
                _rightBaseRootSpaceFootPositions.Length || _leftBaseRootSpaceBendNormals.Length != _leftBaseRootSpaceFootPositions.Length || _rightBaseRootSpaceBendNormals.Length !=
                _rightBaseRootSpaceFootPositions.Length)
            {
                return false;
            }

            float frame = Mathf.Clamp( timeSeconds * _frameRate, 0f, _leftBaseRootSpaceFootPositions.Length - 1);
            int firstFrame = Mathf.FloorToInt(frame);
            int secondFrame = Mathf.Min( firstFrame + 1, _leftBaseRootSpaceFootPositions.Length - 1);
            float interpolation = frame - firstFrame;
            leftRootSpacePosition = Vector3.LerpUnclamped( _leftBaseRootSpaceFootPositions[firstFrame], _leftBaseRootSpaceFootPositions[secondFrame], interpolation);
            rightRootSpacePosition = Vector3.LerpUnclamped( _rightBaseRootSpaceFootPositions[firstFrame], _rightBaseRootSpaceFootPositions[secondFrame], interpolation);
            leftRootSpaceBendNormal = Vector3.LerpUnclamped( _leftBaseRootSpaceBendNormals[firstFrame], _leftBaseRootSpaceBendNormals[secondFrame], interpolation).normalized;
            rightRootSpaceBendNormal = Vector3.LerpUnclamped( _rightBaseRootSpaceBendNormals[firstFrame], _rightBaseRootSpaceBendNormals[secondFrame], interpolation).normalized;
            return IsFinite(leftRootSpacePosition) && IsFinite(rightRootSpacePosition) && IsFinite(leftRootSpaceBendNormal) && IsFinite(rightRootSpaceBendNormal);
        }


        private Transform _targetRoot;
        private HumanoidFootContactPlan _plan;
        private Vector3[] _leftBaseRootSpaceFootPositions = Array.Empty<Vector3>();
        private Vector3[] _rightBaseRootSpaceFootPositions = Array.Empty<Vector3>();
        private Vector3[] _leftBaseRootSpaceBendNormals = Array.Empty<Vector3>();
        private Vector3[] _rightBaseRootSpaceBendNormals = Array.Empty<Vector3>();
        private Vector3[] _sourceHipsPositions = Array.Empty<Vector3>();
        private Quaternion[] _sourceHipsRotations = Array.Empty<Quaternion>();
        private float _frameRate;

    }
}
