using System;
using System.Collections.Generic;
using System.Linq;
using MagicaCloth2;
using UnityEngine;

namespace Fbx2Vmd.ClothPhysics
{
    /// <summary>
    /// 캐릭터에 MC2 물리를 자동 세팅하는 오케스트레이터.
    /// ContextMenu로 실행하며, 콜라이더 자동 생성 → 머리카락 체인 탐지 →
    /// 부위별 BoneCloth 생성 순으로 진행한다.
    /// </summary>
    public partial class CharacterPhysicsSetup
    {
#if UNITY_EDITOR
        /// <summary>
        /// 현재 생성된 클로스 파라미터를 캐릭터 전용 프리셋 JSON으로 저장한다.
        /// 저장 위치: Assets/Resources/PhysicsPresets/Character/{키}_{클로스명}.json
        /// tuning/skirtTuning의 characterPresetKey에 같은 키를 입력하면 이 파일이
        /// 공식 프리셋보다 우선 로드된다 (튜닝 결과의 캐릭터별 라이브러리화).
        /// </summary>
        [ContextMenu("생성 클로스를 캐릭터 프리셋으로 저장")]
        void ExportCharacterPresets()
        {
            if (generatedCloths.Count == 0)
            {
                lastReport = "저장할 클로스가 없습니다. 자동 물리 설정을 먼저 실행하세요.";
                Debug.LogWarning(lastReport, this);
                return;
            }
            string key = tuning.characterPresetKey;
            if (string.IsNullOrEmpty(key))
                key = skirtTuning.characterPresetKey;
            if (string.IsNullOrEmpty(key))
                key = name;
            string dir = System.IO.Path.Combine(
                Application.dataPath, "Resources/PhysicsPresets/Character");
            System.IO.Directory.CreateDirectory(dir);
            int saved = 0;
            foreach (var cloth in generatedCloths)
            {
                if (cloth == null)
                    continue;
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(dir, $"{key}_{cloth.name}.json"),
                    cloth.SerializeData.ExportJson());
                saved++;
            }
            UnityEditor.AssetDatabase.Refresh();
            lastReport = $"캐릭터 프리셋 {saved}개 저장 (키 '{key}') — " +
                "Resources/PhysicsPresets/Character/. 적용하려면 " +
                "characterPresetKey에 같은 키를 입력하세요.";
            Debug.Log("[CharacterPhysicsSetup] " + lastReport, this);
        }

        /// <summary>
        /// Avatar가 비어있을 때 모델 원본에서 Humanoid Avatar를 찾아 할당한다.
        /// 1순위: SkinnedMeshRenderer.sharedMesh의 원본 에셋(FBX) 안의 Avatar
        /// 2순위: 프리팹 소스와 같은 폴더의 Avatar
        /// 할당 후 Head 본 해결로 호환성을 검증하고, 실패 시 원복한다.
        /// </summary>
        bool TryAssignAvatarFromPrefabSource(Animator animator)
        {
            if (animator.avatar != null)
                return animator.avatar.isHuman;

            var candidatePaths = CollectAvatarCandidatePaths(animator);

            var tried = new List<Avatar>();
            foreach (var path in candidatePaths)
            {
                foreach (var asset in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is Avatar avatar && avatar.isHuman && !tried.Contains(avatar))
                        tried.Add(avatar);
                }
            }

            foreach (var avatar in tried)
            {
                animator.avatar = avatar;
                // 호환성 검증: Head 본이 해결되지 않으면 다른 스켈레톤의 Avatar
                if (animator.isHuman && animator.GetBoneTransform(HumanBodyBones.Head) != null)
                    return true;
            }
            animator.avatar = null;
            return false;
        }

        private List<string> CollectAvatarCandidatePaths(Animator animator)
        {
            var candidatePaths = new List<string>();

            // 1) SMR 메시의 원본 에셋 경로 — 프리팹 연결 없이도 정확
            foreach (var smr in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null)
                    continue;
                string p = UnityEditor.AssetDatabase.GetAssetPath(smr.sharedMesh);
                if (!string.IsNullOrEmpty(p))
                    candidatePaths.Add(p);
            }

            // 2) 프리팹 소스와 같은 폴더
            AppendPrefabDirAvatarPaths(candidatePaths);
            return candidatePaths;
        }

        private void AppendPrefabDirAvatarPaths(List<string> candidatePaths)
        {
            string prefabPath = UnityEditor.PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
            if (string.IsNullOrEmpty(prefabPath))
            {
                var src = UnityEditor.PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
                if (src != null)
                    prefabPath = UnityEditor.AssetDatabase.GetAssetPath(src);
            }
            if (string.IsNullOrEmpty(prefabPath))
                return;
            string dir = System.IO.Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Avatar", new[] { dir }))
            {
                string p = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (!candidatePaths.Contains(p))
                    candidatePaths.Add(p);
            }
        }
#endif
    }
}
