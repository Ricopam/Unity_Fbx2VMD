using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(CorrectionPassOrder.ArmSwingLimit)]
    public partial class HumanoidArmSwingLimitGuard : MonoBehaviour
    {

        private Animator _animator;
        private PoseSpaceRetargeter _linkedRetargeter;

        private void Awake()
        {
            InitializeIfNeeded();
        }

        private void OnEnable()
        {
            InitializeIfNeeded();
        }

        private void LateUpdate()
        {
            ResetDiagnostics();
            if (!enableSwingLimit ||
                correctionWeight <= 0f ||
                !InitializeIfNeeded() ||
                ShouldPreserveCompleteEditorHumanoidPoseReference())
            {
                return;
            }

            ApplyArmLimit(
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm,
                HumanBodyBones.LeftHand,
                -1f);
            ApplyArmLimit(
                HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm,
                HumanBodyBones.RightHand,
                1f);
        }

        private void OnDisable()
        {
            ResetDiagnostics();
            DisposeDiagnosticPoseHandler();
        }

        private void OnDestroy()
        {
            DisposeDiagnosticPoseHandler();
        }

        public void Configure(
            Animator animator,
            bool enabled,
            float weight,
            float upperArmDownDot,
            float handHorizontalRatio,
            float handBelowShoulderRatio,
            float reachLimitWeight,
            float handHorizontalReachRatio,
            float reachMaxHandBelowShoulderRatio,
            float reachMinElbowAngleAfterApply,
            float raisedReachLimitWeight,
            float raisedMinUpperArmDownDot,
            float raisedMaxHandBelowShoulderRatio,
            float raisedMaxHandHorizontalReachRatio,
            bool logCorrectionMessages)
        {
            if (_animator != animator)
            {
                DisposeDiagnosticPoseHandler();
            }

            _animator = animator != null ? animator : GetComponent<Animator>();
            enableSwingLimit = enabled;
            correctionWeight = Mathf.Clamp01(weight);
            maxUpperArmDownDot = Mathf.Clamp01(upperArmDownDot);
            minHandHorizontalRatio = Mathf.Clamp(handHorizontalRatio, 0f, 1.5f);
            maxHandBelowShoulderRatio = Mathf.Clamp(handBelowShoulderRatio, 0f, 1.5f);
            horizontalReachLimitWeight = Mathf.Clamp01(reachLimitWeight);
            maxHandHorizontalReachRatio = Mathf.Clamp(handHorizontalReachRatio, 0f, 1.5f);
            horizontalReachMaxHandBelowShoulderRatio = Mathf.Clamp(reachMaxHandBelowShoulderRatio, 0f, 1.5f);
            horizontalReachMinElbowAngleAfterApply = Mathf.Clamp(reachMinElbowAngleAfterApply, 0f, 180f);
            raisedPoseHorizontalReachLimitWeight = Mathf.Clamp01(raisedReachLimitWeight);
            raisedPoseMinUpperArmDownDot = Mathf.Clamp01(raisedMinUpperArmDownDot);
            raisedPoseMaxHandBelowShoulderRatio = Mathf.Clamp(raisedMaxHandBelowShoulderRatio, 0f, 1.5f);
            raisedPoseMaxHandHorizontalReachRatio = Mathf.Clamp(raisedMaxHandHorizontalReachRatio, 0f, 1.5f);
            logCorrections = logCorrectionMessages;
            _warningLogged = false;
            this.enabled = enableSwingLimit;
        }

        internal void BindRetargeter(PoseSpaceRetargeter retargeter)
        {
            _linkedRetargeter = retargeter;
        }

        private bool InitializeIfNeeded()
        {
            if (_animator == null)
            {
                _animator = GetComponent<Animator>();
            }

            return _animator != null &&
                   _animator.avatar != null &&
                   _animator.avatar.isValid &&
                   _animator.avatar.isHuman;
        }

        private bool ShouldPreserveCompleteEditorHumanoidPoseReference()
        {
            return _linkedRetargeter != null &&
                _linkedRetargeter.targetAnimator == _animator &&
                _linkedRetargeter.IsCompleteEditorHumanoidPoseReferenceActive;
        }

        private void ApplyArmLimit(
            HumanBodyBones upperBone,
            HumanBodyBones lowerBone,
            HumanBodyBones handBone,
            float fallbackSideSign)
        {
            Transform upper = _animator.GetBoneTransform(upperBone);
            Transform lower = _animator.GetBoneTransform(lowerBone);
            Transform hand = _animator.GetBoneTransform(handBone);
            if (upper == null || lower == null || hand == null)
            {
                return;
            }

            if (!TryMeasureArmPose(upper, lower, hand, out ArmPoseMeasure pose))
            {
                return;
            }

            float forearmStretchBefore = CaptureForearmStretchMuscle(upperBone);
            if (pose.currentDownDot <= maxUpperArmDownDot)
            {
                ApplyRaisedPoseLimit(
                    upperBone, upper, lower, hand, pose, forearmStretchBefore);
                return;
            }

            bool horizontalReachLimited = TryApplyHorizontalReachLimit(
                upper,
                lower,
                hand,
                pose.localHandOffset,
                pose.armLength,
                pose.belowShoulderRatio);

            if (pose.horizontalRatio < minHandHorizontalRatio ||
                pose.belowShoulderRatio > maxHandBelowShoulderRatio)
            {
                RecordArmLimitDiagnostics(
                    upperBone,
                    standardLimitApplied: false,
                    horizontalReachLimited: horizontalReachLimited,
                    raisedReachLimited: false,
                    forearmStretchBefore: forearmStretchBefore);
                return;
            }

            ApplyStandardDownLimit(
                upperBone, upper, lower, hand, pose,
                fallbackSideSign, forearmStretchBefore, horizontalReachLimited);
        }

        void ApplyRaisedPoseLimit(
            HumanBodyBones upperBone, Transform upper, Transform lower,
            Transform hand, ArmPoseMeasure pose, float forearmStretchBefore)
        {
            bool raisedReachLimited = TryApplyRaisedPoseHorizontalReachLimit(
                upper,
                lower,
                hand,
                pose.localHandOffset,
                pose.armLength,
                pose.currentDownDot,
                pose.belowShoulderRatio);
            RecordArmLimitDiagnostics(
                upperBone,
                standardLimitApplied: false,
                horizontalReachLimited: false,
                raisedReachLimited: raisedReachLimited,
                forearmStretchBefore: forearmStretchBefore);
        }

        // 팔 포즈 측정값 묶음 — 제한 판정·적용의 입력.
        struct ArmPoseMeasure
        {
            public float armLength;
            public float currentDownDot;
            public float horizontalRatio;
            public float belowShoulderRatio;
            public Vector3 currentWorldDirection;
            public Vector3 originalHandPosition;
            public Vector3 currentLocalDirection;
            public Vector3 localHandOffset;
        }

        bool TryMeasureArmPose(Transform upper, Transform lower, Transform hand,
            out ArmPoseMeasure pose)
        {
            pose = default;
            Vector3 upperToLower = lower.position - upper.position;
            Vector3 lowerToHand = hand.position - lower.position;
            float upperLength = upperToLower.magnitude;
            float armLength = upperLength + lowerToHand.magnitude;
            if (upperLength <= 0.0001f || armLength <= 0.0001f)
            {
                return false;
            }

            pose.armLength = armLength;
            pose.currentWorldDirection = upperToLower / upperLength;
            pose.originalHandPosition = hand.position;
            pose.currentLocalDirection =
                transform.InverseTransformDirection(pose.currentWorldDirection).normalized;
            pose.currentDownDot = Mathf.Clamp01(-pose.currentLocalDirection.y);
            pose.localHandOffset = transform.InverseTransformPoint(hand.position) -
                                   transform.InverseTransformPoint(upper.position);
            pose.horizontalRatio =
                new Vector2(pose.localHandOffset.x, pose.localHandOffset.z).magnitude / armLength;
            pose.belowShoulderRatio = Mathf.Max(0f, -pose.localHandOffset.y) / armLength;
            return true;
        }

        void ApplyStandardDownLimit(
            HumanBodyBones upperBone, Transform upper, Transform lower,
            Transform hand, ArmPoseMeasure pose, float fallbackSideSign,
            float forearmStretchBefore, bool horizontalReachLimited)
        {
            Vector3 currentLocalDirection = pose.currentLocalDirection;
            Vector3 horizontal = new Vector3(currentLocalDirection.x, 0f, currentLocalDirection.z);
            if (horizontal.sqrMagnitude <= 0.000001f)
            {
                horizontal = new Vector3(fallbackSideSign, 0f, 0f);
            }

            horizontal.Normalize();
            float targetHorizontalMagnitude = Mathf.Sqrt(Mathf.Max(0f, 1f - maxUpperArmDownDot * maxUpperArmDownDot));
            Vector3 targetLocalDirection = horizontal * targetHorizontalMagnitude;
            targetLocalDirection.y = -maxUpperArmDownDot;
            targetLocalDirection.Normalize();

            Vector3 targetWorldDirection = transform.TransformDirection(targetLocalDirection).normalized;
            if (!IsFinite(targetWorldDirection))
            {
                RecordArmLimitDiagnostics(
                    upperBone,
                    standardLimitApplied: false,
                    horizontalReachLimited: horizontalReachLimited,
                    raisedReachLimited: false,
                    forearmStretchBefore: forearmStretchBefore);
                return;
            }

            Quaternion rotationDelta = Quaternion.FromToRotation(pose.currentWorldDirection, targetWorldDirection);
            Quaternion targetRotation = rotationDelta * upper.rotation;
            if (!IsFinite(targetRotation))
            {
                RecordArmLimitDiagnostics(
                    upperBone,
                    standardLimitApplied: false,
                    horizontalReachLimited: horizontalReachLimited,
                    raisedReachLimited: false,
                    forearmStretchBefore: forearmStretchBefore);
                return;
            }

            upper.rotation = Quaternion.Slerp(upper.rotation, targetRotation, correctionWeight);
            if (preserveHandTarget && !horizontalReachLimited)
            {
                RedirectForearmToHandTarget(lower, hand, pose.originalHandPosition, correctionWeight);
            }

            LogOnce($"상완 하강 각도를 제한했습니다. bone={upper.name}, downDot={pose.currentDownDot:F3}, horizontalRatio={pose.horizontalRatio:F3}, belowRatio={pose.belowShoulderRatio:F3}");
            RecordArmLimitDiagnostics(
                upperBone,
                standardLimitApplied: true,
                horizontalReachLimited: horizontalReachLimited,
                raisedReachLimited: false,
                forearmStretchBefore: forearmStretchBefore);
        }

        private bool TryApplyHorizontalReachLimit(
            Transform upper,
            Transform lower,
            Transform hand,
            Vector3 localHandOffset,
            float armLength,
            float belowShoulderRatio)
        {
            float reachWeight = Mathf.Clamp01(horizontalReachLimitWeight);
            float maxReachRatio = Mathf.Clamp(maxHandHorizontalReachRatio, 0f, 1.5f);
            float reachMaxBelowRatio = horizontalReachMaxHandBelowShoulderRatio > 0f
                ? Mathf.Clamp(horizontalReachMaxHandBelowShoulderRatio, 0f, 1.5f)
                : maxHandBelowShoulderRatio;
            if (reachWeight <= 0f ||
                maxReachRatio <= 0f ||
                armLength <= 0.0001f ||
                belowShoulderRatio > reachMaxBelowRatio)
            {
                return false;
            }

            Vector2 horizontal = new Vector2(localHandOffset.x, localHandOffset.z);
            float horizontalDistance = horizontal.magnitude;
            float maxHorizontalDistance = maxReachRatio * armLength;
            if (horizontalDistance <= maxHorizontalDistance ||
                horizontalDistance <= 0.0001f)
            {
                return false;
            }

            Vector2 targetHorizontal = horizontal.normalized * maxHorizontalDistance;
            Vector3 targetLocalOffset = new Vector3(
                targetHorizontal.x,
                localHandOffset.y,
                targetHorizontal.y);
            Vector3 upperLocalPosition = transform.InverseTransformPoint(upper.position);
            Vector3 targetWorldHandPosition = transform.TransformPoint(upperLocalPosition + targetLocalOffset);
            if (!IsFinite(targetWorldHandPosition))
            {
                return false;
            }

            Quaternion upperRotationBefore = upper.rotation;
            Quaternion lowerRotationBefore = lower.rotation;
            RotateBoneTowardDirection(
                upper,
                lower.position - upper.position,
                targetWorldHandPosition - upper.position,
                reachWeight);
            RedirectForearmToHandTarget(lower, hand, targetWorldHandPosition, reachWeight);
            if (ShouldRollbackHorizontalReachLimit(upper, lower, hand))
            {
                upper.rotation = upperRotationBefore;
                lower.rotation = lowerRotationBefore;
                return false;
            }

            LogOnce($"손 수평 reach를 제한했습니다. bone={upper.name}, horizontalRatio={horizontalDistance / armLength:F3}, maxRatio={maxReachRatio:F3}");
            return true;
        }

        private bool ShouldRollbackHorizontalReachLimit(Transform upper, Transform lower, Transform hand)
        {
            float minElbowAngle = Mathf.Clamp(horizontalReachMinElbowAngleAfterApply, 0f, 180f);
            if (minElbowAngle <= 0f)
            {
                return false;
            }

            float elbowAngle = CalculateElbowAngleDegrees(upper, lower, hand);
            return IsFinite(elbowAngle) && elbowAngle < minElbowAngle;
        }

        private bool TryApplyRaisedPoseHorizontalReachLimit(
            Transform upper,
            Transform lower,
            Transform hand,
            Vector3 localHandOffset,
            float armLength,
            float currentDownDot,
            float belowShoulderRatio)
        {
            float reachWeight = Mathf.Clamp01(raisedPoseHorizontalReachLimitWeight);
            float minDownDot = Mathf.Clamp01(raisedPoseMinUpperArmDownDot);
            float maxBelowRatio = Mathf.Clamp(raisedPoseMaxHandBelowShoulderRatio, 0f, 1.5f);
            float maxReachRatio = Mathf.Clamp(raisedPoseMaxHandHorizontalReachRatio, 0f, 1.5f);
            if (reachWeight <= 0f ||
                maxReachRatio <= 0f ||
                armLength <= 0.0001f ||
                currentDownDot < minDownDot ||
                belowShoulderRatio > maxBelowRatio)
            {
                return false;
            }

            Vector2 horizontal = new Vector2(localHandOffset.x, localHandOffset.z);
            float horizontalDistance = horizontal.magnitude;
            float maxHorizontalDistance = maxReachRatio * armLength;
            if (horizontalDistance <= maxHorizontalDistance ||
                horizontalDistance <= 0.0001f)
            {
                return false;
            }

            Vector2 targetHorizontal = horizontal.normalized * maxHorizontalDistance;
            Vector3 targetLocalOffset = new Vector3(
                targetHorizontal.x,
                localHandOffset.y,
                targetHorizontal.y);
            Vector3 upperLocalPosition = transform.InverseTransformPoint(upper.position);
            Vector3 targetWorldHandPosition = transform.TransformPoint(upperLocalPosition + targetLocalOffset);
            if (!IsFinite(targetWorldHandPosition))
            {
                return false;
            }

            RotateBoneTowardDirection(
                upper,
                lower.position - upper.position,
                targetWorldHandPosition - upper.position,
                reachWeight);
            RedirectForearmToHandTarget(lower, hand, targetWorldHandPosition, reachWeight);
            LogOnce($"Raised-pose horizontal reach limited. bone={upper.name}, downDot={currentDownDot:F3}, belowRatio={belowShoulderRatio:F3}, horizontalRatio={horizontalDistance / armLength:F3}, maxRatio={maxReachRatio:F3}");
            return true;
        }

        private static void RotateBoneTowardDirection(
            Transform bone,
            Vector3 currentDirection,
            Vector3 targetDirection,
            float weight)
        {
            if (bone == null ||
                currentDirection.sqrMagnitude <= 0.000001f ||
                targetDirection.sqrMagnitude <= 0.000001f ||
                weight <= 0f)
            {
                return;
            }

            currentDirection.Normalize();
            targetDirection.Normalize();
            if (!IsFinite(currentDirection) || !IsFinite(targetDirection))
            {
                return;
            }

            Quaternion correction = Quaternion.FromToRotation(currentDirection, targetDirection);
            Quaternion targetRotation = correction * bone.rotation;
            if (!IsFinite(targetRotation))
            {
                return;
            }

            bone.rotation = Quaternion.Slerp(bone.rotation, targetRotation, Mathf.Clamp01(weight));
        }

        private static void RedirectForearmToHandTarget(
            Transform lower,
            Transform hand,
            Vector3 originalHandPosition,
            float weight)
        {
            if (lower == null || hand == null || weight <= 0f)
            {
                return;
            }

            Vector3 currentDirection = hand.position - lower.position;
            Vector3 targetDirection = originalHandPosition - lower.position;
            if (currentDirection.sqrMagnitude <= 0.000001f ||
                targetDirection.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            currentDirection.Normalize();
            targetDirection.Normalize();
            if (!IsFinite(currentDirection) || !IsFinite(targetDirection))
            {
                return;
            }

            Quaternion correction = Quaternion.FromToRotation(currentDirection, targetDirection);
            Quaternion targetRotation = correction * lower.rotation;
            if (!IsFinite(targetRotation))
            {
                return;
            }

            lower.rotation = Quaternion.Slerp(lower.rotation, targetRotation, Mathf.Clamp01(weight));
        }

        private static float CalculateElbowAngleDegrees(Transform upper, Transform lower, Transform hand)
        {
            if (upper == null || lower == null || hand == null)
            {
                return float.NaN;
            }

            Vector3 toUpper = upper.position - lower.position;
            Vector3 toHand = hand.position - lower.position;
            if (toUpper.sqrMagnitude <= 0.000001f || toHand.sqrMagnitude <= 0.000001f)
            {
                return float.NaN;
            }

            return Vector3.Angle(toUpper, toHand);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
