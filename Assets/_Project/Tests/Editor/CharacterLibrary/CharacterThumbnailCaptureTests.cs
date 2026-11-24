using Fbx2Vmd.CharacterLibrary;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor.CharacterLibrary
{
    /// <summary>
    /// FBX·PMX 캐릭터처럼 파일에 썸네일이 없는 항목은 불러온 뒤 정면 상반신을 찍어 썸네일로 씀.
    /// 렌더링은 엔진 의존이라 카메라 배치 계산만 순수 함수로 검증함.
    /// </summary>
    public sealed class CharacterThumbnailCaptureTests
    {
        private const float FieldOfView = 30f;

        [Test]
        public void Framing_LooksAtUpperBodyFromFront()
        {
            var head = new Vector3(0f, 1.5f, 0f);
            var chest = new Vector3(0f, 1.2f, 0f);
            ThumbnailFraming framing = CharacterThumbnailCapture.ComputeFraming(head, chest, Vector3.forward, FieldOfView);

            // 화면 중심은 머리와 가슴 사이보다 약간 위(머리카락 여유)
            Assert.That(framing.lookAt.y, Is.InRange(1.35f, 1.5f));
            // 캐릭터 정면(+Z) 쪽에서 캐릭터를 바라봄
            Assert.That(framing.position.z, Is.GreaterThan(0.5f));
            Assert.That(framing.position.y, Is.EqualTo(framing.lookAt.y).Within(1e-4f));
            // 세로 시야 절반이 머리~가슴 거리의 1.6배를 담도록 거리 결정
            float halfHeight = Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad) * Vector3.Distance(framing.position, framing.lookAt);
            Assert.That(halfHeight, Is.EqualTo(0.3f * 1.6f).Within(1e-3f));
        }

        [Test]
        public void Framing_FollowsCharacterFacingDirection()
        {
            var head = new Vector3(2f, 1.5f, 0f);
            var chest = new Vector3(2f, 1.2f, 0f);
            ThumbnailFraming framing = CharacterThumbnailCapture.ComputeFraming(head, chest, Vector3.right, FieldOfView);

            Assert.That(framing.position.x, Is.GreaterThan(2.5f));
            Assert.That(framing.position.z, Is.EqualTo(0f).Within(1e-4f));
        }

        // 머리·가슴이 거의 겹친 이상 입력에서도 카메라가 캐릭터 안으로 들어가지 않게 최소 거리를 둠.
        [Test]
        public void Framing_DegenerateBones_KeepsMinimumDistance()
        {
            var point = new Vector3(0f, 1.4f, 0f);
            ThumbnailFraming framing = CharacterThumbnailCapture.ComputeFraming(point, point, Vector3.forward, FieldOfView);
            Assert.That(Vector3.Distance(framing.position, framing.lookAt), Is.GreaterThanOrEqualTo(0.5f));
        }

        [Test]
        public void Framing_ZeroForward_FallsBackToWorldForward()
        {
            ThumbnailFraming framing = CharacterThumbnailCapture.ComputeFraming(
                new Vector3(0f, 1.5f, 0f), new Vector3(0f, 1.2f, 0f), Vector3.zero, FieldOfView);
            Assert.That(framing.position.z, Is.GreaterThan(0.5f));
        }
    }
}
