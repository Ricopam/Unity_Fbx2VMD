using Fbx2Vmd.FBXImporter;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor.FBXImporter
{
    public class HumanoidPelvisSupportFadeTests
    {
        private const float Upper = 0.45f;
        private const float Lower = 0.45f;
        // 완전 지지 목표까지 0.9m인데 최대 도달 0.85m라 완전 지지에서는 골반을 0.05m 내려야 함.
        private const double Reach = 0.85d;
        private const float MaximumOffset = 0.9f;

        private static HumanoidPelvisReachCalculator.Leg Leg(float targetHeight, float support) =>
            new HumanoidPelvisReachCalculator.Leg(new Vector3(0f, 0.9f - targetHeight, 0f), Upper, Lower, support, 0f, Reach);

        private static HumanoidPelvisReachCalculator.Leg AirLeg() =>
            new HumanoidPelvisReachCalculator.Leg(new Vector3(0.2f, 0.5f, 0f), Upper, Lower, 0f, 0.3f, Reach);

        [Test]
        public void Given_PartialSupportRelease_When_Fading_Then_LowersPelvisInProportionToSupport()
        {
            // 지지 0.5에서 보간 목표(높이 0.1m)는 낮춤 없이 닿지만, 완전 지지 낮춤(-0.05m)의 절반만큼 남겨 튐을 나눔.
            var left = Leg(0.1f, 0.5f);
            Assert.That(HumanoidPelvisReachCalculator.TryCalculateOffset(left, AirLeg(), Vector3.up, MaximumOffset,
                out float offset, out Vector2 footOffsets), Is.True);
            Assert.That(offset, Is.EqualTo(0f).Within(0.000001f));

            bool isFaded = HumanoidPelvisReachCalculator.TryFadeOffsetWithSupport(left, AirLeg(), Leg(0f, 1f), AirLeg(),
                Vector3.up, MaximumOffset, ref offset, ref footOffsets);

            Assert.That(isFaded, Is.True);
            Assert.That(offset, Is.EqualTo(-0.025f).Within(0.0001f));
            Assert.That(footOffsets.y, Is.EqualTo(offset).Within(0.0001f), "공중 발은 골반을 따라 함께 내려감");
        }

        [TestCase(1f)]
        [TestCase(0f)]
        public void Given_FullOrNoSupport_When_Fading_Then_KeepsExistingOffset(float support)
        {
            var left = Leg(support > 0f ? 0f : 0.3f, support);
            HumanoidPelvisReachCalculator.TryCalculateOffset(left, AirLeg(), Vector3.up, MaximumOffset,
                out float offset, out Vector2 footOffsets);
            float before = offset;
            Assert.That(HumanoidPelvisReachCalculator.TryFadeOffsetWithSupport(left, AirLeg(), Leg(0f, 1f), AirLeg(),
                Vector3.up, MaximumOffset, ref offset, ref footOffsets), Is.False);
            Assert.That(offset, Is.EqualTo(before));
        }

        [Test]
        public void Given_CurrentNeedIsLarger_When_Fading_Then_KeepsCurrentLowering()
        {
            // 지금 필요한 낮춤이 가중치 비례 값보다 크면 그대로 둠(더 내리기만 하고 덜 내리지 않음).
            var left = Leg(-0.04f, 0.5f);
            HumanoidPelvisReachCalculator.TryCalculateOffset(left, AirLeg(), Vector3.up, MaximumOffset,
                out float offset, out Vector2 footOffsets);
            float before = offset;
            Assert.That(before, Is.LessThan(-0.025f));
            Assert.That(HumanoidPelvisReachCalculator.TryFadeOffsetWithSupport(left, AirLeg(), Leg(0f, 1f), AirLeg(),
                Vector3.up, MaximumOffset, ref offset, ref footOffsets), Is.False);
            Assert.That(offset, Is.EqualTo(before));
        }

        [Test]
        public void Given_FullSupportUnreachable_When_Fading_Then_KeepsExistingOffset()
        {
            var left = Leg(0.1f, 0.5f);
            HumanoidPelvisReachCalculator.TryCalculateOffset(left, AirLeg(), Vector3.up, MaximumOffset,
                out float offset, out Vector2 footOffsets);
            // 완전 지지 목표가 최대 이동 한도로도 닿지 않으면 근거가 없으므로 바꾸지 않음.
            var unreachable = new HumanoidPelvisReachCalculator.Leg(new Vector3(0f, 3f, 0f), Upper, Lower, 1f, 0f, Reach);
            Assert.That(HumanoidPelvisReachCalculator.TryFadeOffsetWithSupport(left, AirLeg(), unreachable, AirLeg(),
                Vector3.up, 0.1f, ref offset, ref footOffsets), Is.False);
            Assert.That(offset, Is.EqualTo(0f).Within(0.000001f));
        }
    }
}
