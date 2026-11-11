using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UniGLTF;
using UnityEngine;
using VRM;
using Object = UnityEngine.Object;

namespace Fbx2Vmd.CharacterLibrary
{
    /// <summary>
    /// 캐릭터 런타임 로드 래퍼. VRM은 UniVRM, FBX·PMX는 에디터 임포트 경로로 비동기 로드 → 비활성 상태 반환 → Humanoid 검증까지 담당한다.
    /// 호출자는 결과의 커밋/파기를 결정한다(늦은 커밋으로 실패 시 기존 캐릭터를 보존).
    /// </summary>
    public sealed class CharacterLoader
    {
        public sealed class Result
        {
            public RuntimeGltfInstance instance;
            // 비VRM(FBX) 로드 경로의 루트. VRM은 instance.Root를 우선한다.
            public GameObject loadedRoot;
            public string error = string.Empty;
            public bool cancelled;
            public GameObject Root => instance != null ? instance.Root : loadedRoot;
            public bool success => Root != null && string.IsNullOrEmpty(error) && !cancelled;
        }

        /// <summary>
        /// 캐릭터 파일을 로드하고 Humanoid Avatar까지 검증한다.
        /// 성공 결과의 Root는 SetActive(false) 상태이며 메시는 아직 표시되지 않았다.
        /// 취소 토큰은 UniVRM 0.x에 미지원이므로 로드 완료 후 결과를 폐기하는 방식으로 처리한다.
        /// </summary>
        public async Task<Result> LoadAsync(
            string path, CancellationToken cancellationToken, string entryId = null)
        {
            string extension = Path.GetExtension(path ?? string.Empty);
            if (string.Equals(extension, ".fbx", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".pmx", StringComparison.OrdinalIgnoreCase))
            {
                // 동기 임포트는 완료 전 중단이 불가하므로 진입 시점에 취소를 검사한다.
                if (cancellationToken.IsCancellationRequested)
                {
                    return new Result { cancelled = true };
                }
                Result fbxResult = FbxCharacterLoader.Load(path, entryId);
                if (cancellationToken.IsCancellationRequested && fbxResult.Root != null)
                {
                    fbxResult.cancelled = true;
                    DestroyResult(fbxResult);
                }
                return fbxResult;
            }

            var result = new Result();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                result.error = $"캐릭터 파일이 없습니다: {path}";
                return result;
            }

            RuntimeGltfInstance loaded = null;
            try
            {
                IAwaitCaller awaitCaller = Application.isPlaying
                    ? (IAwaitCaller)new RuntimeOnlyAwaitCaller()
                    : new ImmediateCaller();

                loaded = await VrmUtility.LoadAsync(path, awaitCaller);

                if (cancellationToken.IsCancellationRequested)
                {
                    result.cancelled = true;
                    DestroyInstance(loaded);
                    return result;
                }

                GameObject root = loaded != null ? loaded.Root : null;
                if (root == null)
                {
                    result.error = "VRM 로드 결과가 비어 있습니다.";
                    DestroyInstance(loaded);
                    return result;
                }

                // 커밋 전까지 화면에 보이지 않게 한다.
                root.SetActive(false);

                Animator animator = root.GetComponent<Animator>();
                if (animator == null || animator.avatar == null ||
                    !animator.avatar.isValid || !animator.avatar.isHuman)
                {
                    result.error = "유효한 Humanoid Avatar가 없어 리타게팅 대상으로 사용할 수 없습니다.";
                    DestroyInstance(loaded);
                    return result;
                }

                result.instance = loaded;
                return result;
            }
            catch (Exception error)
            {
                result.error = $"VRM 로드 실패: {error.Message}";
                DestroyInstance(loaded);
                return result;
            }
        }

        /// <summary>
        /// 로드 결과의 루트를 파괴한다. VRM·FBX 경로를 모두 처리한다.
        /// </summary>
        public static void DestroyResult(Result result)
        {
            if (result == null)
            {
                return;
            }
            GameObject root = result.Root;
            if (root == null)
            {
                return;
            }
            if (Application.isPlaying)
            {
                Object.Destroy(root);
            }
            else
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 로드된 인스턴스를 파괴한다. RuntimeGltfInstance가 소유한 메시/텍스처/머티리얼도 함께 해제된다.
        /// 에디터 모드에서는 DestroyImmediate가 필요하다.
        /// </summary>
        public static void DestroyInstance(RuntimeGltfInstance instance)
        {
            if (instance == null)
            {
                return;
            }

            GameObject root = instance.Root;
            if (root == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(root);
            }
            else
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
