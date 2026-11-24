using System.Linq;
using System.Reflection;
using Fbx2Vmd.FBXImporter;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Tests.Editor.Settings
{
    /// <summary>
    /// PRD 수용 기준 1: Main 씬 Play 중 Game view에 버튼·슬라이더·글자가 없어야 함.
    /// 모든 조작은 설정창으로 옮겼으므로 씬 자체 UI와 실행 중 생성되는 재생 컨트롤을 함께 막음.
    /// </summary>
    public sealed class MainSceneGameViewUiTests
    {
        private const string MainScenePath = "Assets/_Project/Scene/Main.unity";
        private const string ShowControlsField = "showGameViewMotionControls";

        [Test]
        public void MainScene_HasNoGameViewUiAndDisablesRuntimeMotionControls()
        {
            Scene scene = SceneManager.GetSceneByPath(MainScenePath);
            bool openedHere = !scene.isLoaded;
            if (openedHere)
            {
                scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Additive);
            }

            try
            {
                Component[] components = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Component>(true))
                    .ToArray();
                string[] uiObjects = components
                    .Where(component => component is Canvas || component is Selectable ||
                                        component is Graphic)
                    .Select(component => $"{component.gameObject.name}({component.GetType().Name})")
                    .ToArray();
                Assert.That(uiObjects, Is.Empty, "Main 씬에 Game view UI가 남아 있음");

                RecordingSetting setting = components.OfType<RecordingSetting>().Single();
                SerializedProperty showControls =
                    new SerializedObject(setting).FindProperty(ShowControlsField);
                Assert.That(showControls, Is.Not.Null);
                Assert.That(showControls.boolValue, Is.False, "Main 씬은 실행 중 재생 컨트롤을 만들지 않아야 함");
            }
            finally
            {
                if (openedHere)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        // 다른 씬(Main_Recoding)의 FBX 버튼을 본떠 재생 컨트롤을 만드는 경로가 꺼져 있으면 컴포넌트를 붙이지 않음.
        [Test]
        public void ControlsDisabled_PipelineRequest_DoesNotBuildPlaybackControls()
        {
            var settingObject = new GameObject("Setting");
            var pipelineObject = new GameObject("FBXVmdPipeline");
            var buttonObject = new GameObject("FBX_Button", typeof(RectTransform), typeof(Button));
            try
            {
                RecordingSetting setting = settingObject.AddComponent<RecordingSetting>();
                var serialized = new SerializedObject(setting);
                serialized.FindProperty(ShowControlsField).boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                FBXVmdPipeline pipeline = pipelineObject.AddComponent<FBXVmdPipeline>();

                MethodInfo handler = typeof(RecordingSetting).GetMethod(
                    "HandlePlaybackControlsEnsureRequested",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(handler, Is.Not.Null);
                handler.Invoke(setting, new object[] { pipeline });

                Assert.That(
                    pipelineObject.GetComponents<MonoBehaviour>()
                        .Any(component => component.GetType().Name == "HumanoidMotionPlaybackControlsView"),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(buttonObject);
                Object.DestroyImmediate(pipelineObject);
                Object.DestroyImmediate(settingObject);
            }
        }
    }
}
