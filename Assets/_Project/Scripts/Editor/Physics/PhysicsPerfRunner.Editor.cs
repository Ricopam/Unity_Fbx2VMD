using MagicaCloth2;
using UnityEngine;

namespace Fbx2Vmd.ClothPhysics
{
    /// <summary>
    /// PhysicsPerfRunner의 에디터 전용 베이크 단계 — MC2 에디터 어셈블리(MagicaClothV2.Editor)를
    /// 참조해야 하므로 런타임 partial이 아닌 별도 정적 도우미로 분리한다.
    /// </summary>
    public static class PhysicsPerfRunnerEditorBake
    {
        /// <summary>
        /// 프리빌드 데이터가 없는 클로스에 ScriptableObject를 만들어 베이크한다.
        /// MC2 공식 에디터 경로(PreBuildDataCreation)와 동일한 데이터를 생성하며 저장 대화상자는 띄우지 않는다.
        /// </summary>
        public static int BakeMissingPreBuildData(PhysicsPerfRunner runner)
        {
            const string dir = "Assets/_Project/Physics/PreBuild";
            if (!UnityEditor.AssetDatabase.IsValidFolder("Assets/_Project/Physics"))
                UnityEditor.AssetDatabase.CreateFolder("Assets/_Project", "Physics");
            if (!UnityEditor.AssetDatabase.IsValidFolder(dir))
                UnityEditor.AssetDatabase.CreateFolder("Assets/_Project/Physics", "PreBuild");

            int baked = 0;
            foreach (var cloth in runner.setup.generatedCloths)
            {
                if (cloth == null)
                    continue;
                var pb = cloth.GetSerializeData2().preBuildData;
                pb.enabled = true;
                if (pb.preBuildScriptableObject == null)
                {
                    var so = ScriptableObject.CreateInstance<PreBuildScriptableObject>();
                    string path = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(
                        $"{dir}/MagicaPreBuild_{runner.label}_{cloth.name}.asset");
                    UnityEditor.AssetDatabase.CreateAsset(so, path);
                    pb.preBuildScriptableObject = so;
                }
                var res = PreBuildDataCreation.CreatePreBuildData(cloth, false);
                if (res.IsSuccess())
                    baked++;
                else
                    Debug.LogWarning(
                        $"[PhysicsPerfRunner] 프리빌드 베이크 실패({cloth.name}): {res.GetResultString()}", cloth);
            }
            return baked;
        }
    }
}
