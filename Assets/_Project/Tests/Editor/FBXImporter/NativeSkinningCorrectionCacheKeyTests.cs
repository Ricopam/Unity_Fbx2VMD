using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Tests.Editor.FBXImporter
{
    public class NativeSkinningCorrectionCacheKeyTests
    {
        [Test]
        public void Given_PreparedContactLabels_When_ComputingPoseIdentity_Then_SeparatesEffectiveLabels()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            Assembly assembly = typeof(Fbx2Vmd.FBXImporter.FBXVmdPipeline).Assembly;
            Type controllerType = assembly.GetType("Fbx2Vmd.FBXImporter.HumanoidMotionPlaybackController", true);
            Type labelType = assembly.GetType("Fbx2Vmd.FBXImporter.HumanoidFootContactIntentLabel", true);
            Type modeType = assembly.GetType("Fbx2Vmd.FBXImporter.HumanoidFootContactIntentMode", true);
            Type setType = assembly.GetType("Fbx2Vmd.FBXImporter.HumanoidFootContactIntentLabelSet", true);
            using var controller = (IDisposable)Activator.CreateInstance(controllerType, true);
            object stabilizer = controllerType.GetField("_footContactStabilizer", flags).GetValue(controller);
            PropertyInfo prepared = stabilizer.GetType().GetProperty("IntentLabels", flags);
            MethodInfo poseIdentity = controllerType.GetMethod("GetNativePoseCacheIdentity", flags);
            MethodInfo curveIdentity = controllerType.GetMethod("GetSupportCurveCacheIdentity", flags);

            object Label(int end, bool? support, string mode = null) => Activator.CreateInstance(
                labelType, flags, null, new[] { (object)13, end, support,
                    mode == null ? null : Enum.Parse(modeType, mode) }, null);

            Array Side(params object[] labels)
            {
                Array side = Array.CreateInstance(labelType, labels.Length);
                for (int index = 0; index < labels.Length; index++) side.SetValue(labels[index], index);
                return side;
            }

            string Identity(Array left, Array right, int files = 1)
            {
                object set = Activator.CreateInstance(setType, flags, null,
                    new object[] { left, right, files, left.Length + right.Length }, null);
                prepared.SetValue(stabilizer, set);
                return (string)poseIdentity.Invoke(controller, null);
            }

            Array empty = Side();
            object airborne = Label(24, false);
            object plant = Label(24, true, "Plant");
            string labeled = Identity(Side(airborne), empty);
            Assert.That(Identity(empty, empty), Is.EqualTo(curveIdentity.Invoke(controller, null)));
            Assert.That(labeled, Is.Not.EqualTo(Identity(empty, empty)));
            Assert.That(labeled, Is.EqualTo(Identity(Side(Label(24, false)), empty, 2)));
            Assert.That(labeled, Is.Not.EqualTo(Identity(empty, Side(airborne))));
            Assert.That(labeled, Is.Not.EqualTo(Identity(Side(Label(25, false)), empty)));
            Assert.That(labeled, Is.Not.EqualTo(Identity(Side(Label(24, null)), empty)));
            Assert.That(Identity(Side(plant), empty), Is.Not.EqualTo(Identity(Side(Label(24, true, "Slide")), empty)));
            Assert.That(Identity(Side(airborne, plant), empty), Is.Not.EqualTo(Identity(Side(plant, airborne), empty)));
        }

        [Test]
        public void Given_MetaOnlyImporterChange_When_ComputingAssetIdentity_Then_InvalidatesPreviousIdentity()
        {
            string path = "Assets/NativeSkinningCacheIdentity_" + Guid.NewGuid().ToString("N") + ".anim";
            var clip = new AnimationClip();
            try
            {
                AssetDatabase.CreateAsset(clip, path);
                byte[] source = File.ReadAllBytes(path);
                string guid = AssetDatabase.AssetPathToGUID(path);
                Hash128 dependencyHash = AssetDatabase.GetAssetDependencyHash(path);
                string before = GetIdentity(clip);
                Assert.That(GetIdentity(clip), Is.EqualTo(before));

                AssetImporter importer = AssetImporter.GetAtPath(path);
                importer.userData = "임포트 설정 변경 검증";
                importer.SaveAndReimport();
                clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);

                Assert.That(File.ReadAllBytes(path), Is.EqualTo(source), "원본 내용 변경과 메타 변경을 구분해야 함");
                Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(guid));
                Assert.That(AssetDatabase.GetAssetDependencyHash(path), Is.Not.EqualTo(dependencyHash));
                string after = GetIdentity(clip);
                Assert.That(after, Is.Not.EqualTo(before), "같은 원본이라도 임포트 설정 변경 시 캐시를 분리해야 함");
                Assert.That(GetIdentity(clip), Is.EqualTo(after));
            }
            finally
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                    Assert.That(AssetDatabase.DeleteAsset(path), Is.True, "검증용 에셋 정리 실패");
                else if (clip != null)
                    UnityEngine.Object.DestroyImmediate(clip);
            }
        }

        private static string GetIdentity(UnityEngine.Object asset)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("Fbx2Vmd.FBXImporter.NativeSkinningCorrectionCacheKey"))
                .First(candidate => candidate != null);
            MethodInfo method = type.GetMethod("GetAssetIdentity", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (string)method.Invoke(null, new object[] { asset });
        }
    }
}
