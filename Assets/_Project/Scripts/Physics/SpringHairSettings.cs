using UnityEngine;

namespace Fbx2Vmd.ClothPhysics
{
    /// <summary>
    /// 머리카락 체인 스프링 계산에 쓰는 고정 기본값. 프로파일의 체형 기준 값을 월드 값으로 바꿔 둠.
    /// 뿌리는 애니메이션 자세에 붙고 끝으로 갈수록 스프링 영향이 커짐.
    /// 얼굴을 덮는 짧은 머리는 단단하게, 트윈테일 같은 긴 머리는 관성으로 늦게 따라오며 출렁이게 함.
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

        public SpringHairSettings(float unit, HairPart part)
        {
            var profile = IsFirmPart(part) ? FirmHairProfile : SwingHairProfile;
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

        // 앞머리·바보털은 얼굴과 눈을 가리지 않게 원래 모양을 지킴.
        private static bool IsFirmPart(HairPart part) => part == HairPart.Front || part == HairPart.Ahoge;

        // 체인 전체 2.5Hz·감쇠비 0.7은 satisfaction_2 실측에서 작은 동작에도 거의 이탈하지 않는 값.
        private static HairSpringProfile FirmHairProfile => new HairSpringProfile
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

        // 긴 머리는 낮은 주파수·약한 감쇠로 몸보다 늦게 따라와 1~2번 출렁이고,
        // 끝으로 갈수록 원래 모양 유지를 줄여 관성으로 휘게 함.
        // 0.6Hz는 satisfaction_2 실측 트윈테일 큰 동작 이탈 p95 41°·최대 80°로 참고 공연 영상의 출렁임에 가장 가까운 값(사용자 선택).
        private static HairSpringProfile SwingHairProfile => new HairSpringProfile
        {
            enabled = true,
            frequency = 0.6f,
            dampingRatio = 0.25f,
            animationBlend = new DepthSpringCurve(SpringCurveType.RootOneTailZero),
            poseStiffness = new DepthSpringCurve(SpringCurveType.RootOneTailHalf),
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
