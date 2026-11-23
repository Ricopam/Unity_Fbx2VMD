using UnityEngine;

namespace Fbx2Vmd.ClothPhysics
{
    /// <summary>
    /// 머리카락 체인 스프링 계산에 쓰는 고정 기본값. 프로파일의 체형 기준 값을 월드 값으로 바꿔 둠.
    /// 뿌리는 애니메이션 자세에 붙고 끝으로 갈수록 스프링 영향이 커짐.
    /// 포즈 강성 1은 부모 기준 원래 모양을 유지해 작은 동작에서 머리카락이 함께 움직이게 함.
    /// </summary>
    internal readonly struct SpringHairSettings
    {
        // 굽힘 상한이 이 각도면 제한하지 않음.
        private const float NoBendLimitDegrees = 180f;
        private const float BendLimitTolerance = 1e-4f;

        public readonly float AngularFrequency;
        public readonly float DampingRatio;
        public readonly Vector3 Gravity;
        public readonly float MaxBendRadians;
        public readonly float MaxCollisionRadius;
        public readonly DepthSpringCurve AnimationBlend;
        public readonly DepthSpringCurve PoseStiffness;
        public readonly DepthSpringCurve BendAngleCap;
        public readonly DepthSpringCurve CollisionRadius;

        public SpringHairSettings(float unit)
        {
            var profile = DefaultHairProfile;
            AngularFrequency = profile.frequency * 2f * Mathf.PI;
            DampingRatio = profile.dampingRatio;
            Gravity = Vector3.down * profile.gravity;
            MaxBendRadians = profile.maxBendAngleDeg * Mathf.Deg2Rad;
            MaxCollisionRadius = profile.maxCollisionRadius * Mathf.Max(0f, unit);
            AnimationBlend = profile.animationBlend;
            PoseStiffness = profile.poseStiffness;
            BendAngleCap = profile.bendAngleCap;
            CollisionRadius = profile.collisionRadius;
        }

        // 체인 전체 2.5Hz·감쇠비 0.7은 satisfaction_2 실측에서 작은 동작 이탈과 큰 동작 흔들림의 균형점.
        private static HairSpringProfile DefaultHairProfile => new HairSpringProfile
        {
            enabled = true,
            frequency = 2.5f,
            dampingRatio = 0.7f,
            animationBlend = new DepthSpringCurve(SpringCurveType.RootOneTailZero),
            poseStiffness = new DepthSpringCurve(SpringCurveType.ConstantOne),
            bendAngleCap = new DepthSpringCurve(SpringCurveType.ConstantOne),
            maxBendAngleDeg = NoBendLimitDegrees,
            collisionRadius = new DepthSpringCurve(SpringCurveType.RootOneTailZero),
            maxCollisionRadius = 0.02f,
            gravity = 0f,
        };

        /// <summary>뿌리 기준 각도 상한을 쓰는지.</summary>
        public bool HasBendLimit => MaxBendRadians < NoBendLimitDegrees * Mathf.Deg2Rad - BendLimitTolerance;

        /// <summary>자세 맞춤용 평균 충돌 반경(월드).</summary>
        public float AverageCollisionRadius => MaxCollisionRadius * CollisionRadius.Average();
    }
}
