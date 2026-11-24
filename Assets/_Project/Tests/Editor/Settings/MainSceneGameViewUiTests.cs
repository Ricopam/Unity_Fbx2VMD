using System.Linq;
using System.Reflection;
using Fbx2Vmd.FBXImporter;
using NUnit.Framework;
using TMPro;
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
                    // 스크립트가 빠진(Missing) 컴포넌트는 null로 와서 엉뚱한 예외를 내므로 제외함.
                    .Where(component => component != null)
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

        // 켜짐(Main_Recoding 전환 경로)은 기존처럼 FBX 버튼을 본떠 컨트롤을 만들고, 꺼짐(Main)은 만들지 않음.
        [TestCase(true, true)]
        [TestCase(false, false)]
        public void PipelineRequest_BuildsPlaybackControlsOnlyWhenEnabled(bool showControls, bool expectControls)
        {
            var settingObject = new GameObject("Setting");
            var pipelineObject = new GameObject("FBXVmdPipeline");
            var canvasObject = new GameObject("UI_Canvas", typeof(RectTransform), typeof(Canvas));
            try
            {
                CreateButtonTemplate(canvasObject.transform);
                RecordingSetting setting = settingObject.AddComponent<RecordingSetting>();
                var serialized = new SerializedObject(setting);
                serialized.FindProperty(ShowControlsField).boolValue = showControls;
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
                    Is.EqualTo(expectControls));
            }
            finally
            {
                Object.DestroyImmediate(canvasObject);
                Object.DestroyImmediate(pipelineObject);
                Object.DestroyImmediate(settingObject);
            }
        }

        private static void CreateButtonTemplate(Transform parent)
        {
            var buttonObject = new GameObject("FBX_Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var labelObject = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(buttonObject.transform, false);
            labelObject.GetComponent<TextMeshProUGUI>().text = "FBX 임포트";
        }
    }
}
