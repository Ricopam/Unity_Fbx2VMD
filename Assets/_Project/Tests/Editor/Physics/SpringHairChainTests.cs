using System.Collections.Generic;
using Fbx2Vmd.ClothPhysics;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor.ClothPhysics
{
    /// <summary>
    /// 머리카락 체인 스프링 동작 검증. 작은 동작에서 원래 모양을 유지하고,
    /// 큰 동작에서는 끝이 늦게 따라온 뒤 오래 출렁이지 않고 멈춰야 함.
    /// </summary>
    public class SpringHairChainTests
    {
        const float FrameTime = 1f / 60f;
        const int BoneCount = 5;
        const float BoneLength = 0.1f;
        static readonly IReadOnlyList<(Vector3, float)> NoSpheres = new List<(Vector3, float)>();

        GameObject head;
        Transform root;
        SpringHairChain chain;
        SpringHairSettings settings;

        [SetUp]
        public void SetUp()
        {
            // 머리 아래로 10cm 본 5개가 늘어진 체인
            head = new GameObject("Head");
            Transform parent = head.transform;
            for (int i = 0; i < BoneCount; i++)
            {
                var bone = new GameObject("hair_" + i).transform;
                bone.SetParent(parent, false);
                bone.localPosition = i == 0 ? Vector3.zero : Vector3.down * BoneLength;
                parent = bone;
            }
            root = head.transform.GetChild(0);
            chain = new SpringHairChain(root);
            settings = new SpringHairSettings(1f, HairPart.Front);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(head);
        }

        Transform Tip => chain.Bones[chain.Bones.Count - 1];

        // 애니메이션 자세(머리 기준 원래 모양)에서 끝점이 벗어난 각도.
        float TipDeviationDegrees()
        {
            Vector3 actual = Tip.position - root.position;
            Vector3 animated = head.transform.rotation * (Vector3.down * BoneLength * (BoneCount - 1));
            return Vector3.Angle(actual, animated);
        }

        void Step(int frames = 1, IReadOnlyList<(Vector3, float)> spheres = null)
        {
            for (int i = 0; i < frames; i++)
            {
                chain.RestoreAnimatedPose();
                chain.Step(FrameTime, settings, spheres ?? NoSpheres);
            }
        }

        [Test]
        public void Given_StillHead_When_Simulated_Then_HairKeepsOriginalShape()
        {
            Step(120);

            Assert.That(TipDeviationDegrees(), Is.LessThan(0.01f));
        }

        [Test]
        public void Given_HeadJumps_When_Simulated_Then_TipLagsThenSettlesWithoutLongWobble()
        {
            Step(10);
            head.transform.position += Vector3.right * 0.1f;

            Step();
            float lag = TipDeviationDegrees();
            Step(59);
            float afterOneSecond = TipDeviationDegrees();

            Assert.That(lag, Is.GreaterThan(5f), "큰 동작에서는 끝이 늦게 따라와야 함");
            Assert.That(afterOneSecond, Is.LessThan(1f), "1초 안에 원래 모양으로 멈춰야 함");
        }

        [Test]
        public void Given_SmallHeadMotion_When_Simulated_Then_DeviationIsMuchSmallerThanLargeMotion()
        {
            Step(10);
            float small = MaxDeviationWhileMoving(0.005f);
            head.transform.position = Vector3.zero;
            chain.Reset();
            Step(10);
            float large = MaxDeviationWhileMoving(0.05f);

            Assert.That(small, Is.LessThan(large * 0.2f));
        }

        [Test]
        public void Given_SameHeadJump_When_LongHairSimulated_Then_SwingsWiderAndLongerThanBangs()
        {
            // 같은 머리 이동에 긴 머리는 크게 벌어지고 늦게 가라앉아야 함(참고 영상의 트윈테일)
            float firmPeak = PeakAndLateDeviation(new SpringHairSettings(1f, HairPart.Front), out float firmLate);
            float swingPeak = PeakAndLateDeviation(new SpringHairSettings(1f, HairPart.Tail), out float swingLate);

            Assert.That(swingPeak, Is.GreaterThan(firmPeak * 1.5f), "긴 머리가 더 크게 흔들려야 함");
            Assert.That(swingLate, Is.GreaterThan(firmLate), "긴 머리가 더 오래 출렁여야 함");
            Assert.That(swingLate, Is.LessThan(swingPeak), "출렁임은 시간이 지나면 줄어야 함");
        }

        [Test]
        public void Given_LongHairSwing_When_ThreeSecondsPass_Then_SettlesBackToShape()
        {
            // 감쇠가 너무 약해 계속 출렁이는 값은 걸러냄
            PeakAndLateDeviation(new SpringHairSettings(1f, HairPart.Tail), out _);
            Step(180);

            Assert.That(TipDeviationDegrees(), Is.LessThan(2f));
        }

        [Test]
        public void Given_LongChain_When_SimulatedAt30And60Fps_Then_SwingsAlike()
        {
            // 녹화(30fps)와 미리보기(60fps 이상)의 흔들림이 같아야 함
            float at60 = PeakDeviationOfLongChain(1f / 60f);
            float at30 = PeakDeviationOfLongChain(1f / 30f);

            Assert.That(at30, Is.EqualTo(at60).Within(at60 * 0.15f));
        }

        // 본 20개 체인의 머리를 0.25초 동안 옮겼다 멈춘 뒤 1.5초 동안 끝점 최대 이탈각.
        static float PeakDeviationOfLongChain(float frameTime)
        {
            const int longBoneCount = 20;
            const float longBoneLength = 0.03f;
            var longHead = new GameObject("LongHead");
            try
            {
                Transform parent = longHead.transform;
                for (int i = 0; i < longBoneCount; i++)
                {
                    var bone = new GameObject("long_" + i).transform;
                    bone.SetParent(parent, false);
                    bone.localPosition = i == 0 ? Vector3.zero : Vector3.down * longBoneLength;
                    parent = bone;
                }
                Transform longRoot = longHead.transform.GetChild(0);
                var longChain = new SpringHairChain(longRoot);
                var longSettings = new SpringHairSettings(1f, HairPart.Tail);
                Transform tip = longChain.Bones[longChain.Bones.Count - 1];
                Vector3 restTip = Vector3.down * longBoneLength * (longBoneCount - 1);
                float moveSpeed = 0.4f;
                float peak = 0f;
                for (float time = 0f; time < 1.75f; time += frameTime)
                {
                    if (time < 0.25f)
                        longHead.transform.position += Vector3.right * moveSpeed * frameTime;
                    longChain.RestoreAnimatedPose();
                    longChain.Step(frameTime, longSettings, NoSpheres);
                    peak = Mathf.Max(peak, Vector3.Angle(tip.position - longRoot.position, restTip));
                }
                return peak;
            }
            finally
            {
                Object.DestroyImmediate(longHead);
            }
        }

        // 머리를 0.25초 동안 옆으로 움직였다 멈춘 뒤 1초 동안의 최대 이탈각과,
        // 멈춘 지 0.5~1초 구간의 최대 이탈각.
        float PeakAndLateDeviation(SpringHairSettings tested, out float late)
        {
            head.transform.position = Vector3.zero;
            chain.Reset();
            settings = tested;
            Step(10);
            float peak = 0f;
            late = 0f;
            for (int i = 0; i < 75; i++)
            {
                if (i < 15)
                    head.transform.position += Vector3.right * 0.01f;
                Step();
                float deviation = TipDeviationDegrees();
                peak = Mathf.Max(peak, deviation);
                if (i >= 45)
                    late = Mathf.Max(late, deviation);
            }
            return peak;
        }

        // 매 프레임 같은 거리만큼 옆으로 움직이는 동안 끝점 최대 이탈각.
        float MaxDeviationWhileMoving(float stepPerFrame)
        {
            float max = 0f;
            for (int i = 0; i < 20; i++)
            {
                head.transform.position += Vector3.right * stepPerFrame;
                Step();
                max = Mathf.Max(max, TipDeviationDegrees());
            }
            return max;
        }

        [Test]
        public void Given_Motion_When_Simulated_Then_BoneLengthsArePreserved()
        {
            Step(5);
            head.transform.position += new Vector3(0.2f, 0.1f, -0.1f);
            head.transform.rotation = Quaternion.Euler(20f, 40f, 0f);
            Step(7);

            var bones = chain.Bones;
            for (int i = 1; i < bones.Count; i++)
                Assert.That(Vector3.Distance(bones[i - 1].position, bones[i].position),
                    Is.EqualTo(BoneLength).Within(1e-4f));
        }

        [Test]
        public void Given_SphereOverlapsTip_When_Simulated_Then_TipLeavesWithoutSnapping()
        {
            // 끝점 옆 5mm에 중심을 둔 반경 6cm 구
            var spheres = new List<(Vector3, float)> { (Tip.position + Vector3.right * 0.005f, 0.06f) };
            // 밀어내기는 3m/s 상한이고, 구 밖으로 옮긴 목표를 따라가는 스프링 이동이 조금 더해짐.
            float maxPerFrame = 3f * FrameTime * 1.2f;
            Vector3 center = Tip.position + Vector3.right * 0.005f;

            for (int frame = 0; frame < 60; frame++)
            {
                Vector3 before = Tip.position;
                Step(1, spheres);
                Assert.That(Vector3.Distance(before, Tip.position), Is.LessThanOrEqualTo(maxPerFrame),
                    "한 프레임 이동이 최대 해소 속도를 넘지 않아야 함");
            }
            Assert.That(Vector3.Distance(Tip.position, center), Is.GreaterThanOrEqualTo(0.06f - 0.001f));
        }

        [Test]
        public void Given_ResetAfterJump_When_Simulated_Then_StartsFromAnimatedPose()
        {
            Step(10);
            head.transform.position += Vector3.right * 0.3f;

            chain.Reset();
            Step();

            Assert.That(TipDeviationDegrees(), Is.LessThan(0.01f));
        }

        [Test]
        public void Given_RootAndTipOnly_When_HeadJumps_Then_TipStillSwings()
        {
            // 뿌리+끝 본 2개짜리 체인도 끝이 늦게 따라와야 함(뿌리 회전을 막으면 전혀 안 움직임)
            var shortHead = new GameObject("ShortHead");
            try
            {
                var shortRoot = new GameObject("short_0").transform;
                shortRoot.SetParent(shortHead.transform, false);
                var shortTip = new GameObject("short_1").transform;
                shortTip.SetParent(shortRoot, false);
                shortTip.localPosition = Vector3.down * BoneLength;
                var shortChain = new SpringHairChain(shortRoot);
                for (int i = 0; i < 5; i++)
                {
                    shortChain.RestoreAnimatedPose();
                    shortChain.Step(FrameTime, settings, NoSpheres);
                }

                shortHead.transform.position += Vector3.right * 0.05f;
                shortChain.RestoreAnimatedPose();
                shortChain.Step(FrameTime, settings, NoSpheres);

                Vector3 animated = shortHead.transform.rotation * Vector3.down;
                Assert.That(Vector3.Angle(shortTip.position - shortRoot.position, animated), Is.GreaterThan(1f));
            }
            finally
            {
                Object.DestroyImmediate(shortHead);
            }
        }

        [Test]
        public void Given_ZeroDeltaTime_When_Stepped_Then_PoseStaysFinite()
        {
            chain.Step(0f, settings, NoSpheres);

            Assert.That(float.IsNaN(Tip.position.x), Is.False);
            Assert.That(TipDeviationDegrees(), Is.LessThan(0.01f));
        }

        [Test]
        public void Given_VectorBeyondCap_When_ClampingBend_Then_AngleIsCappedAndLengthKept()
        {
            Vector3 clamped = SpringHairChain.ClampBend(Vector3.right * 2f, Vector3.down, 30f * Mathf.Deg2Rad);

            Assert.That(Vector3.Angle(clamped, Vector3.down), Is.EqualTo(30f).Within(0.01f));
            Assert.That(clamped.magnitude, Is.EqualTo(2f).Within(1e-4f));
        }
    }
}
