using UnityEditor;

namespace Fbx2Vmd.ClothPhysics
{
    // 에디터 어셈블리의 구현을 런타임 훅에 연결한다.
    [InitializeOnLoad]
    internal static class PhysicsEditorWiring
    {
        static PhysicsEditorWiring()
        {
            PhysicsPerfRunner.EditorBakeMissingPreBuildDataHook =
                PhysicsPerfRunnerEditorBake.BakeMissingPreBuildData;
        }
    }
}
