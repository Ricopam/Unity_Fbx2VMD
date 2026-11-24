using System.Collections;
using UnityEngine;

namespace Fbx2Vmd.CharacterLibrary
{
    /// <summary>썸네일 카메라 위치와 바라볼 지점.</summary>
    public readonly struct ThumbnailFraming
    {
        public readonly Vector3 position;
        public readonly Vector3 lookAt;

        public ThumbnailFraming(Vector3 position, Vector3 lookAt)
        {
            this.position = position;
            this.lookAt = lookAt;
        }
    }

    /// <summary>
    /// 파일에 썸네일이 없는 캐릭터(FBX·PMX)를 불러온 뒤 정면 상반신을 찍어 PNG로 만듦.
    /// 설정창 캐릭터 카드가 이름 첫 글자 대신 실제 모습을 보이게 하기 위함.
    /// </summary>
    public static class CharacterThumbnailCapture
    {
        public const int ImageSize = 256;
        private const float FieldOfView = 30f;
        // 머리~가슴 거리에 머리카락·어깨 여유를 더한 세로 절반 크기 배율.
        private const float FrameHalfHeightScale = 1.6f;
        // 머리 쪽으로 중심을 올려 정수리가 잘리지 않게 함.
        private const float CenterTowardHead = 0.75f;
        private const float MinimumDistance = 0.5f;
        private static readonly Color BackgroundColor = new Color(0.85f, 0.87f, 0.9f, 1f);

        /// <summary>머리·가슴 위치와 캐릭터 정면 방향으로 카메라 배치를 계산함.</summary>
        public static ThumbnailFraming ComputeFraming(Vector3 head, Vector3 chest, Vector3 forward, float fieldOfView)
        {
            Vector3 facing = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector3.forward;
            Vector3 lookAt = Vector3.Lerp(chest, head, CenterTowardHead);
            float halfHeight = Vector3.Distance(head, chest) * FrameHalfHeightScale;
            float distance = Mathf.Max(MinimumDistance, halfHeight / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad));
            // 수평으로 바라보도록 높이는 중심과 같게 둠.
            Vector3 horizontal = new Vector3(facing.x, 0f, facing.z);
            Vector3 direction = horizontal.sqrMagnitude > 1e-6f ? horizontal.normalized : Vector3.forward;
            return new ThumbnailFraming(lookAt + direction * distance, lookAt);
        }

        /// <summary>
        /// 휴머노이드 캐릭터의 정면 상반신을 PNG로 렌더링함. 머리·가슴 본을 찾지 못하면 null.
        /// </summary>
        public static byte[] TryRenderPng(GameObject root)
        {
            Animator animator = root != null ? root.GetComponentInChildren<Animator>() : null;
            bool isHumanoid = animator != null && animator.isHuman;
            Transform head = isHumanoid ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            Transform chest = isHumanoid ? animator.GetBoneTransform(HumanBodyBones.Chest) : null;
            if (chest == null && isHumanoid)
            {
                chest = animator.GetBoneTransform(HumanBodyBones.Spine);
            }

            if (head == null || chest == null)
            {
                return null;
            }

            ThumbnailFraming framing = ComputeFraming(head.position, chest.position, root.transform.forward, FieldOfView);
            return RenderPng(framing);
        }

        /// <summary>
        /// 항목에 썸네일이 없으면 캐릭터를 찍어 붙임. Play 중에는 유휴 자세가 적용된 뒤 찍도록 한 프레임 미룸.
        /// 실패해도 이미 끝난 캐릭터 불러오기는 되돌리지 않음.
        /// </summary>
        public static void CaptureMissing(
            MonoBehaviour host, CharacterLibraryRegistry registry, CharacterLibraryEntry entry, GameObject root)
        {
            bool needsThumbnail = entry != null && string.IsNullOrEmpty(entry.thumbnailFileName) && root != null;
            if (!needsThumbnail || registry == null)
            {
                return;
            }

            if (Application.isPlaying && host != null)
            {
                host.StartCoroutine(AttachAfterFrame(registry, entry.id, root));
                return;
            }

            Attach(registry, entry.id, root);
        }

        private static IEnumerator AttachAfterFrame(CharacterLibraryRegistry registry, string entryId, GameObject root)
        {
            yield return new WaitForEndOfFrame();
            Attach(registry, entryId, root);
        }

        private static void Attach(CharacterLibraryRegistry registry, string entryId, GameObject root)
        {
            if (root == null)
            {
                return;
            }

            try
            {
                registry.TryAttachGeneratedThumbnail(entryId, TryRenderPng(root));
            }
            catch (System.Exception error)
            {
                Debug.LogWarning($"[CharacterLibrary] 캐릭터 썸네일 생성 실패: {error.Message}");
            }
        }

        private static byte[] RenderPng(ThumbnailFraming framing)
        {
            GameObject cameraObject = CreateCamera(framing, out Camera camera);
            var target = new RenderTexture(ImageSize, ImageSize, 24, RenderTextureFormat.ARGB32);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                return ReadPng(target);
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                target.Release();
                Object.DestroyImmediate(target);
            }
        }

        private static GameObject CreateCamera(ThumbnailFraming framing, out Camera camera)
        {
            var cameraObject = new GameObject("CharacterThumbnailCamera") { hideFlags = HideFlags.HideAndDontSave };
            camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = FieldOfView;
            camera.nearClipPlane = 0.05f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = BackgroundColor;
            cameraObject.transform.position = framing.position;
            cameraObject.transform.LookAt(framing.lookAt);
            return cameraObject;
        }

        private static byte[] ReadPng(RenderTexture source)
        {
            RenderTexture previousActive = RenderTexture.active;
            var image = new Texture2D(ImageSize, ImageSize, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = source;
                image.ReadPixels(new Rect(0, 0, ImageSize, ImageSize), 0, 0);
                image.Apply();
                return image.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(image);
            }
        }
    }
}
