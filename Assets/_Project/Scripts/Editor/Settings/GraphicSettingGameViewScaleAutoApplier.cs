using Fbx2Vmd.Settings;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fbx2Vmd.Settings.EditorTools
{
    [InitializeOnLoad]
    public static class GraphicSettingGameViewScaleAutoApplier
    {
        private static bool isScheduled;

        static GraphicSettingGameViewScaleAutoApplier()
        {
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            ScheduleApply();
        }

        public static void ScheduleApply()
        {
            if (isScheduled)
            {
                return;
            }

            isScheduled = true;
            EditorApplication.delayCall += ApplyScheduled;
        }

        public static bool ApplyActiveSceneSettingGameViewScale()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return false;
            }

            GraphicSetting setting = FindActiveSceneGraphicSetting();
            return setting != null && GameViewScaleController.TryApply(setting.GameViewScaleMode);
        }

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (MainRecordingSettingsCompanionLauncher.ShouldOpenForScene(scene.path))
            {
                ScheduleApply();
            }
        }

        private static void ApplyScheduled()
        {
            isScheduled = false;
            ApplyActiveSceneSettingGameViewScale();
        }

        private static GraphicSetting FindActiveSceneGraphicSetting()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid() || !MainRecordingSettingsCompanionLauncher.ShouldOpenForScene(activeScene.path))
            {
                return null;
            }

            GameObject[] roots = activeScene.GetRootGameObjects();
            foreach (GameObject root in roots)
            {
                if (root == null)
                {
                    continue;
                }

                var setting = root.GetComponent<GraphicSetting>();
                if (setting != null)
                {
                    return setting;
                }

                setting = root.GetComponentInChildren<GraphicSetting>(true);
                if (setting != null)
                {
                    return setting;
                }
            }

            return null;
        }
    }
}
