using Fbx2Vmd.Settings;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fbx2Vmd.Settings.EditorTools
{
    public static class GraphicSettingSceneInstaller
    {
        // 최종 기준 씬. 비교 기준 Main_Recoding에는 이미 설치돼 있어 설치 메뉴는 Main을 대상으로 함.
        private const string MainScenePath = "Assets/_Project/Scene/Main.unity";

        [MenuItem("Tools/Graphics/Install Graphic Setting In Main")]
        public static void InstallInMainScene()
        {
            var scene = EditorSceneManager.OpenScene(MainScenePath);
            GraphicSetting setting = EnsureInActiveScene();
            setting.ApplyNow();
            GameViewScaleController.TryApply(setting.GameViewScaleMode);
            BackgroundColorSetting backgroundSetting = setting.GetComponent<BackgroundColorSetting>();
            backgroundSetting?.ApplyNow();
            GraphicTextureImportEditorController.Apply(setting);
            GraphicMaterialShaderEditorController.Apply(setting);
            Selection.activeObject = setting.gameObject;
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"GraphicSetting 설치 완료: {setting.gameObject.name}");
        }

        public static GraphicSetting EnsureInActiveScene()
        {
            GameObject root = ResolveSettingRoot();
            if (root == null)
            {
                root = new GameObject("Setting");
            }
            else if (root.name != "Setting")
            {
                root.name = "Setting";
                EditorUtility.SetDirty(root);
            }

            GraphicSetting setting = root.GetComponent<GraphicSetting>();
            if (setting == null)
            {
                setting = root.AddComponent<GraphicSetting>();
            }

            BackgroundColorSetting backgroundSetting = EnsureComponent<BackgroundColorSetting>(root);
            RecordingSetting recordingSetting = EnsureComponent<RecordingSetting>(root);
            RemoveLegacyGraphicSettingChild(root);
            GraphicSettingSceneConfigurator.Configure(setting, backgroundSetting, recordingSetting);
            return setting;
        }

        private static GameObject ResolveSettingRoot()
        {
            GameObject root = GameObject.Find("Setting");
            if (root != null)
            {
                return root;
            }

            return GameObject.Find("SettingManager");
        }

        private static T EnsureComponent<T>(GameObject root) where T : Component
        {
            T component = root.GetComponent<T>();
            return component != null ? component : root.AddComponent<T>();
        }

        private static void RemoveLegacyGraphicSettingChild(GameObject root)
        {
            Transform child = root.transform.Find("Graphic Setting");
            if (child == null || child.GetComponent<GraphicSetting>() == null || child.childCount > 0)
            {
                return;
            }

            UnityEngine.Object.DestroyImmediate(child.gameObject);
        }
    }
}
