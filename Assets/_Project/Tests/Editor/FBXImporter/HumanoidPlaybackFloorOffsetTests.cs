using System.Collections.Generic;
using System.Linq;
using Fbx2Vmd.FBXImporter;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.Editor.FBXImporter
{
    public class HumanoidPlaybackFloorOffsetTests
    {
        private const float HumanScale = 1f;

        [Test]
        public void Given_ConsistentlySunkSoles_When_Resolving_Then_RaisesByMedianSink()
        {
            var heights = new List<float> { -0.030f, -0.028f, -0.026f, -0.035f, -0.021f };
            Assert.That(HumanoidPlaybackFloorOffset.ResolveOffset(heights, HumanScale), Is.EqualTo(0.028f).Within(0.00001f));
        }

        [Test]
        public void Given_AirborneSamples_When_Resolving_Then_IgnoresThemOutsideSupportBand()
        {
            // 점프처럼 바닥에서 멀리 뜬 표본은 지지 대역 밖이라 중앙값에서 빠져야 함.
            var heights = new List<float> { -0.03f, -0.03f, -0.03f, 0.40f, 0.35f };
            Assert.That(HumanoidPlaybackFloorOffset.ResolveOffset(heights, HumanScale), Is.EqualTo(0.03f).Within(0.00001f));
        }

        [Test]
        public void Given_TooFewSupportedSamples_When_Resolving_Then_DoesNotCorrect()
        {
            var heights = new List<float> { -0.03f, 0.40f, 0.35f, 0.50f, 0.45f };
            Assert.That(HumanoidPlaybackFloorOffset.ResolveOffset(heights, HumanScale), Is.Zero);
        }

        [Test]
        public void Given_LargeSink_When_Resolving_Then_ClampsToMaximum()
        {
            var heights = Enumerable.Repeat(-0.09f, 10).ToList();
            Assert.That(HumanoidPlaybackFloorOffset.ResolveOffset(heights, HumanScale),
                Is.EqualTo(HumanScale * HumanoidPlaybackFloorOffset.MaximumOffsetPerHumanScale).Within(0.00001f));
        }

        [Test]
        public void Given_FloatingSoles_When_Resolving_Then_LowersBody()
        {
            var heights = Enumerable.Repeat(0.02f, 10).ToList();
            Assert.That(HumanoidPlaybackFloorOffset.ResolveOffset(heights, HumanScale), Is.EqualTo(-0.02f).Within(0.00001f));
        }

        [TestCase(0.001f)]
        [TestCase(-0.0015f)]
        public void Given_NegligibleDifference_When_Resolving_Then_KeepsExistingHeight(float height)
        {
            // 이미 바닥에 맞는 클립·모델은 보정 없이 기존 동작을 유지해야 함.
            var heights = Enumerable.Repeat(height, 10).ToList();
            Assert.That(HumanoidPlaybackFloorOffset.ResolveOffset(heights, HumanScale), Is.Zero);
        }

        [Test]
        public void Given_MostSamplesWithoutGround_When_Resolving_Then_DoesNotCorrect()
        {
            // 지면을 못 찾은 표본(NaN)도 분모에 들어가 소수 표본만으로 보정이 확정되지 않아야 함.
            var heights = new List<float> { -0.03f, float.NaN, float.NaN, float.NaN, float.NaN };
            Assert.That(HumanoidPlaybackFloorOffset.ResolveOffset(heights, HumanScale), Is.Zero);
        }

        [Test]
        public void Given_NoSamples_When_Resolving_Then_DoesNotCorrect()
        {
            Assert.That(HumanoidPlaybackFloorOffset.ResolveOffset(new List<float>(), HumanScale), Is.Zero);
            Assert.That(HumanoidPlaybackFloorOffset.ResolveOffset(new List<float> { -0.03f, float.NaN }, 0f), Is.Zero);
        }

        [Test]
        public void Given_YybSatisfactionPlayback_When_Preparing_Then_RaisesRootAndRestoresOnDispose()
        {
            const string clipPath = "Assets/Resources/Import_FBX/satisfaction_2.fbx";
            const string modelPath = "Assets/_Project/Model/YYB Hatsune Miku_default/YYB Hatsune Miku_default_1.0ver.fbx";
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(clipPath).OfType<AnimationClip>()
                .FirstOrDefault(c => c.humanMotion && !c.name.StartsWith("__", System.StringComparison.Ordinal));
            if (asset == null || clip == null) Assert.Ignore("로컬 YYB·satisfaction_2 입력이 없는 환경에서는 생략함");

            var floor = new GameObject("몸 높이 보정 바닥") { hideFlags = HideFlags.HideAndDontSave };
            GameObject model = null;
            var player = new NativeHumanoidAnimationPlayer();
            try
            {
                floor.AddComponent<BoxCollider>().size = new Vector3(40f, 0.1f, 40f);
                floor.transform.position = new Vector3(0f, -0.05f, 0f);
                model = Object.Instantiate(asset);
                model.hideFlags = HideFlags.HideAndDontSave;
                foreach (MonoBehaviour script in model.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
                Animator animator = model.GetComponentInChildren<Animator>(true);
                Assert.That(HumanoidFloorPlacement.TryPlace(animator), Is.True);
                float rootBefore = animator.transform.position.y;
                var ground = new HumanoidGroundResponse();
                ground.Initialize(animator);
                player.Initialize(animator, clip);

                float frameRate = HumanoidMotionFrameCalculator.NormalizeFrameRate(clip.frameRate);
                float offset = HumanoidPlaybackFloorOffset.Resolve(animator, player.EvaluateAt,
                    clip.length, frameRate, ground);
                player.ApplyVerticalOffset(offset);
                // 다시 적용해도 이전 값을 대체할 뿐 누적되지 않아야 함.
                player.ApplyVerticalOffset(offset);

                // 측정 근거: 접지 전 자세의 아래쪽 발 밑창 중앙값 약 -28mm(2026-11-25 Main 측정).
                Assert.That(offset, Is.InRange(0.015f, 0.045f));
                Assert.That(animator.transform.position.y, Is.EqualTo(rootBefore + offset).Within(0.00001f));
                float residual = HumanoidPlaybackFloorOffset.ResolveOffset(
                    MeasureLowest(animator, player, clip, ground), animator.humanScale);
                Assert.That(Mathf.Abs(residual), Is.LessThan(0.005f), "보정 뒤 지지 밑창 중앙값이 바닥 근처여야 함");

                player.Dispose();
                Assert.That(animator.transform.position.y, Is.EqualTo(rootBefore).Within(0.00001f),
                    "재생 정리 뒤에는 대기 자세 높이로 되돌아가야 함");
            }
            finally
            {
                player.Dispose();
                if (model != null) Object.DestroyImmediate(model);
                Object.DestroyImmediate(floor);
            }
        }

        private static List<float> MeasureLowest(Animator animator, NativeHumanoidAnimationPlayer player,
            AnimationClip clip, HumanoidGroundResponse ground)
        {
            Assert.That(HumanoidPlaybackFloorOffset.TryMeasureLowestSoleHeights(animator, player.EvaluateAt,
                clip.length, HumanoidMotionFrameCalculator.NormalizeFrameRate(clip.frameRate), ground,
                out List<float> heights), Is.True);
            return heights;
        }
    }
}
