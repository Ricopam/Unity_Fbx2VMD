using System;
using System.Collections.Generic;
using RootMotion.FinalIK;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 원본 접촉 궤적을 대상 양다리에 본 길이 변화 없이 적용함.
    /// </summary>
    internal sealed partial class HumanoidFootContactStabilizer
    {
        private const float MinimumBendNormalSquaredMagnitude = 0.00000001f;
        private const float MinimumCorrectionSquaredMagnitude = 0.0000000001f;

        private readonly LegChain _leftLeg = new LegChain();
        private readonly LegChain _rightLeg = new LegChain();

        internal bool IsInitialized =>
            _targetRoot != null &&
            _plan != null &&
            _leftLeg.IsValid &&
            _rightLeg.IsValid;

        internal int LeftContactRunCount => _plan?.LeftContactRunCount ?? 0;

        internal int RightContactRunCount => _plan?.RightContactRunCount ?? 0;

        internal IReadOnlyList<HumanoidFootContactSample> SourceSamples { get; private set; } =
            Array.Empty<HumanoidFootContactSample>();

        internal IReadOnlyList<HumanoidFootContactSample> TargetSamples { get; private set; } =
            Array.Empty<HumanoidFootContactSample>();

        // 원본 궤적과 같은 프레임 인덱스의 골반 포즈임. goal 스트림의 루트 상대 기준으로 씀.
        internal IReadOnlyList<Vector3> SourceHipsPositions { get; private set; } =
            Array.Empty<Vector3>();

        internal IReadOnlyList<Quaternion> SourceHipsRotations { get; private set; } =
            Array.Empty<Quaternion>();

        internal float SourceHumanScale { get; private set; }

        // 원본 궤적만으로 추정한 접지 의도 구간임. 런타임 앵커 방침과 증거 기록이 같은 값을 씀.
        internal HumanoidFootContactIntentEstimate IntentEstimate { get; private set; }

        internal HumanoidFootContactIntentLabelSet IntentLabels { get; private set; } =
            HumanoidFootContactIntentLabelSet.Empty;

        internal void Initialize(
            Animator targetAnimator,
            AnimationClip clip,
            Action<float> evaluateTarget, HumanoidPoseReferencePlayer sourceReference, string sourceAssetName = null)
        {
            ValidateInputs(targetAnimator, clip, evaluateTarget, sourceReference);
            Clear();
            _targetRoot = targetAnimator.transform;
            _leftLeg.Initialize( targetAnimator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes);
            _rightLeg.Initialize( targetAnimator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
            float frameRate = HumanoidMotionFrameCalculator.NormalizeFrameRate( clip.frameRate);
            int frameCount = Mathf.CeilToInt(clip.length * frameRate) + 1;
            var sourceSamples = new HumanoidFootContactSample[frameCount];
            var targetSamples = new HumanoidFootContactSample[frameCount];
            _leftBaseRootSpaceFootPositions = new Vector3[frameCount];
            _rightBaseRootSpaceFootPositions = new Vector3[frameCount];
            _leftBaseRootSpaceBendNormals = new Vector3[frameCount];
            _rightBaseRootSpaceBendNormals = new Vector3[frameCount];
            _sourceHipsPositions = new Vector3[frameCount];
            _sourceHipsRotations = new Quaternion[frameCount];
            _frameRate = frameRate;
            Quaternion sourceToTargetRotation = _targetRoot.rotation;
            try
            {
                SampleFrames( frameCount, frameRate, clip.length, evaluateTarget, sourceReference, sourceSamples, targetSamples);
                BuildPlanAndPublish( sourceSamples, targetSamples, frameRate, sourceAssetName, sourceReference, sourceToTargetRotation, targetAnimator.humanScale); } finally
            {
                evaluateTarget(0f); } }

        void BuildPlanAndPublish(
            HumanoidFootContactSample[] sourceSamples,
            HumanoidFootContactSample[] targetSamples,
            float frameRate,
            string sourceAssetName,
            HumanoidPoseReferencePlayer sourceReference,
            Quaternion sourceToTargetRotation,
            float targetHumanScale)
        {
            // 누적된 사람 표식을 같은 입력의 과거 증거에서 읽어 추정에 반영함.
            IntentLabels = HumanoidFootContactIntentLabelStore.Load(sourceAssetName);
            try
            {
                IntentEstimate = HumanoidFootContactIntentEstimator.Estimate( sourceSamples, frameRate, sourceReference.SourceHumanScale, IntentLabels);
            }
            catch (Exception error)
            {
                // 추정 실패가 접지 보정 자체를 막지 않게 구간 정책 없이 진행함.
                Debug.LogWarning( $"발 접지 의도 추정 실패로 의도 구간 없이 보정함: {error.Message}");
                IntentEstimate = null;
            }
            _plan = HumanoidFootContactPlanner.Build( sourceSamples, targetSamples, frameRate, sourceReference.SourceHumanScale, sourceToTargetRotation, targetHumanScale, IntentEstimate);
            // 이미 평가한 원본 궤적을 정밀 밑창 접지에서도 재사용함.
            SourceSamples = Array.AsReadOnly(sourceSamples);
            TargetSamples = Array.AsReadOnly(targetSamples);
            SourceHipsPositions = Array.AsReadOnly(_sourceHipsPositions);
            SourceHipsRotations = Array.AsReadOnly(_sourceHipsRotations);
            SourceHumanScale = sourceReference.SourceHumanScale; }

        internal bool TryCaptureBendNormals(
            out Vector3 leftBendNormal,
            out Vector3 rightBendNormal)
        {
            leftBendNormal = Vector3.zero;
            rightBendNormal = Vector3.zero;
            if (!IsInitialized)
            {
                return true;
            }

            leftBendNormal = _leftLeg.CaptureBendNormal();
            rightBendNormal = _rightLeg.CaptureBendNormal();
            return IsFinite(leftBendNormal) && IsFinite(rightBendNormal);
        }

        internal bool TryApply(
            float timeSeconds,
            Vector3 leftBendNormal,
            Vector3 rightBendNormal)
        {
            if (!IsInitialized)
            {
                return true;
            }
            if (!_plan.TryEvaluateSupportPose( timeSeconds, out Vector3 leftRootSpaceCorrection, out Vector3 rightRootSpaceCorrection, out Vector3 leftRootSpaceToeDirection,
                    out Vector3 rightRootSpaceToeDirection))
            {
                return false;
            }
            if (!TryEvaluateBaseFootPositions( timeSeconds, out Vector3 leftBaseRootSpacePosition, out Vector3 rightBaseRootSpacePosition, out Vector3 leftBaseRootSpaceBendNormal,
                    out Vector3 rightBaseRootSpaceBendNormal))
            {
                return false;
            }
            if (!TryResolveWorldCorrections( leftRootSpaceCorrection, rightRootSpaceCorrection, leftRootSpaceToeDirection, rightRootSpaceToeDirection, leftBaseRootSpacePosition,
                    rightBaseRootSpacePosition, leftBaseRootSpaceBendNormal, rightBaseRootSpaceBendNormal, out WorldCorrections corrections))
            {
                return false;
            }
            _leftLeg.Apply( corrections.LeftCorrection, corrections.LeftToeDirection, ResolveBendNormal(corrections.LeftBendNormal, leftBendNormal));
            _rightLeg.Apply( corrections.RightCorrection, corrections.RightToeDirection, ResolveBendNormal(corrections.RightBendNormal, rightBendNormal));
            return true;
        }

        internal void Clear()
        {
            _plan = null;
            IntentEstimate = null;
            IntentLabels = HumanoidFootContactIntentLabelSet.Empty;
            SourceSamples = Array.Empty<HumanoidFootContactSample>();
            TargetSamples = Array.Empty<HumanoidFootContactSample>();
            SourceHipsPositions = Array.Empty<Vector3>();
            SourceHipsRotations = Array.Empty<Quaternion>();
            SourceHumanScale = 0f;
            _targetRoot = null;
            _leftBaseRootSpaceFootPositions = Array.Empty<Vector3>();
            _rightBaseRootSpaceFootPositions = Array.Empty<Vector3>();
            _leftBaseRootSpaceBendNormals = Array.Empty<Vector3>();
            _rightBaseRootSpaceBendNormals = Array.Empty<Vector3>();
            _sourceHipsPositions = Array.Empty<Vector3>();
            _sourceHipsRotations = Array.Empty<Quaternion>();
            _frameRate = 0f;
            _leftLeg.Clear();
            _rightLeg.Clear();
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private sealed class LegChain
        {
            private Transform _upperLeg;
            private Transform _lowerLeg;
            private Transform _foot;
            private Transform _toes;
            private Vector3 _fallbackBendNormal;

            internal bool IsValid =>
                _upperLeg != null && _lowerLeg != null && _foot != null;

            internal void Initialize(
                Animator animator,
                HumanBodyBones upperLeg,
                HumanBodyBones lowerLeg,
                HumanBodyBones foot,
                HumanBodyBones toes)
            {
                _upperLeg = animator.GetBoneTransform(upperLeg);
                _lowerLeg = animator.GetBoneTransform(lowerLeg);
                _foot = animator.GetBoneTransform(foot);
                _toes = animator.GetBoneTransform(toes);
                if (!IsValid)
                {
                    throw new InvalidOperationException(
                        "고정 길이 발 접촉 보정에는 대퇴·하퇴·발 Humanoid 본이 필요합니다.");
                }

                _fallbackBendNormal = CalculateBendNormal();
                if (_fallbackBendNormal.sqrMagnitude <=
                    MinimumBendNormalSquaredMagnitude)
                {
                    _fallbackBendNormal = animator.transform.right;
                }
            }

            internal Vector3 CaptureFootPosition()
            {
                return _foot.position;
            }

            internal Vector3 CaptureToesPosition()
            {
                return _toes == null ? _foot.position : _toes.position;
            }

            internal Vector3 CaptureBendNormal()
            {
                return CalculateBendNormal();
            }

            internal void Apply(
                Vector3 worldCorrection,
                Vector3 worldToeDirection,
                Vector3 bendNormal)
            {
                if (!IsValid)
                {
                    return;
                }
                Quaternion footRotation = _foot.rotation;
                if (worldCorrection.sqrMagnitude > MinimumCorrectionSquaredMagnitude)
                {
                    if (bendNormal.sqrMagnitude <= MinimumBendNormalSquaredMagnitude)
                    {
                        bendNormal = _fallbackBendNormal;
                    }
                    IKSolverTrigonometric.Solve( _upperLeg, _lowerLeg, _foot, _foot.position + worldCorrection, bendNormal, 1f);
                    _foot.rotation = footRotation;
                }
                if (_toes == null || worldToeDirection.sqrMagnitude <= MinimumCorrectionSquaredMagnitude)
                {
                    return;
                }
                Vector3 currentToeDirection = _toes.position - _foot.position;
                if (currentToeDirection.sqrMagnitude <= MinimumCorrectionSquaredMagnitude)
                {
                    return; }
                _foot.rotation = Quaternion.FromToRotation( currentToeDirection, worldToeDirection) * _foot.rotation; }

            internal void Clear()
            {
                _upperLeg = null;
                _lowerLeg = null;
                _foot = null;
                _toes = null;
                _fallbackBendNormal = Vector3.zero;
            }

            private Vector3 CalculateBendNormal()
            {
                if (!IsValid)
                {
                    return Vector3.zero;
                }

                return Vector3.Cross(
                    _lowerLeg.position - _upperLeg.position,
                    _foot.position - _lowerLeg.position);
            }
        }
    }
}
