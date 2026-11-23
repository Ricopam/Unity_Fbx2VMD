using System.Collections;
using System.Reflection;
using Fbx2Vmd.CharacterLibrary;
using UnityEngine;

namespace Fbx2Vmd.ClothPhysics
{
    /// <summary>
    /// 캐릭터 라이브러리로 교체된 캐릭터에 씬 캐릭터와 같은 물리 설정을 붙임.
    /// 씬의 CharacterPhysicsSetup은 원래 캐릭터에 직렬화돼 있어 교체 시 함께 꺼지므로,
    /// 새 루트에 설정을 복제하고 자동 물리 설정(머리카락·의상 클로스·스프링 본)을 실행함.
    /// </summary>
    public static class LibraryCharacterPhysicsBinder
    {
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            // 도메인 리로드 비활성 설정에서도 중복 구독되지 않게 먼저 해제함.
            CharacterLibrarySceneBinding.CharacterCommitted -= OnCharacterCommitted;
            CharacterLibrarySceneBinding.CharacterCommitted += OnCharacterCommitted;
        }

        // 편집 모드 생성물은 씬 저장 시 라이브러리 루트와 함께 버려지고 시뮬레이션도 없으므로 Play에서만 붙임.
        static void OnCharacterCommitted(GameObject sceneSource, GameObject newRoot)
        {
            if (Application.isPlaying)
            {
                Bind(sceneSource, newRoot);
            }
        }

        /// <summary>
        /// 새 루트에 물리 설정을 붙이고 실행함. 이미 있거나 복제할 원본 설정이 없으면 아무것도 하지 않음.
        /// 반환값은 새로 붙인 설정(없으면 null).
        /// </summary>
        public static CharacterPhysicsSetup Bind(GameObject sceneSource, GameObject newRoot)
        {
            if (newRoot == null || newRoot.GetComponent<CharacterPhysicsSetup>() != null)
            {
                return null;
            }
            Animator animator = newRoot.GetComponent<Animator>();
            CharacterPhysicsSetup template = FindTemplate(sceneSource, newRoot);
            if (animator == null || template == null)
            {
                return null;
            }

            var setup = newRoot.AddComponent<CharacterPhysicsSetup>();
            CopyConfiguration(template, setup);
            // 캐릭터 전용 프리셋 키는 다른 모델에 맞지 않으므로 공용 기본값을 쓰게 비움.
            setup.tuning.characterPresetKey = null;
            setup.skirtTuning.characterPresetKey = null;
            setup.targetAnimator = animator;
            try
            {
                setup.Setup();
            }
            catch
            {
                // 반쯤 생성된 설정이 남으면 다음 Bind가 "이미 있음"으로 재시도를 막으므로 정리 후 전파함.
                setup.ClearGenerated();
                Object.DestroyImmediate(setup);
                throw;
            }
            Debug.Log($"[LibraryCharacterPhysicsBinder] {newRoot.name} 물리 설정: {setup.lastReport}", setup);
            return setup;
        }

        // 스왑 전 씬 캐릭터의 설정을 우선하고, 없으면 씬의 다른 설정을 사용함.
        static CharacterPhysicsSetup FindTemplate(GameObject sceneSource, GameObject newRoot)
        {
            if (sceneSource != null)
            {
                var own = sceneSource.GetComponentInChildren<CharacterPhysicsSetup>(true);
                if (own != null)
                {
                    return own;
                }
            }
            foreach (var candidate in Object.FindObjectsByType<CharacterPhysicsSetup>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
            {
                if (candidate.gameObject.scene == newRoot.scene && !candidate.transform.IsChildOf(newRoot.transform))
                {
                    return candidate;
                }
            }
            return null;
        }

        /// <summary>
        /// 공개 설정 필드(값·열거형·구조체·문자열)만 복사함.
        /// 원본 모델의 본·생성물을 가리키는 UnityEngine.Object 참조와 컬렉션
        /// (targetAnimator, explicitHairRoots, generatedCloths 등)은 새 모델에 맞지 않으므로 제외함.
        /// </summary>
        public static void CopyConfiguration(CharacterPhysicsSetup source, CharacterPhysicsSetup target)
        {
            foreach (FieldInfo field in typeof(CharacterPhysicsSetup).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                bool isModelReference = typeof(Object).IsAssignableFrom(field.FieldType);
                bool isCollection = field.FieldType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(field.FieldType);
                if (field.IsInitOnly || field.IsNotSerialized || isModelReference || isCollection)
                {
                    continue;
                }
                object value = field.GetValue(source);
                // 구조체 안의 AnimationCurve 등이 원본과 공유되지 않게 직렬화 왕복으로 깊은 복사함.
                bool isCompositeValue = field.FieldType.IsValueType && !field.FieldType.IsPrimitive && !field.FieldType.IsEnum;
                if (isCompositeValue)
                {
                    value = JsonUtility.FromJson(JsonUtility.ToJson(value), field.FieldType);
                }
                field.SetValue(target, value);
            }
        }
    }
}
