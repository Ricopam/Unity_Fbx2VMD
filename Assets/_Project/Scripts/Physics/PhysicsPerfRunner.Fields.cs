using UnityEngine;

namespace Fbx2Vmd.ClothPhysics
{
    public partial class PhysicsPerfRunner
    {

        /// <summary>씬 로드 직후 첫 빌드(직렬화 클로스)의 초기화 계측 — 프레임 수와 InitCloth/PreBuild 누적(ns)</summary>
        public int sceneLoadInitFrames = -1;
        public long sceneLoadInitNs;
        public long sceneLoadPreBuildNs;

        [Tooltip("시작 프레임 스파이크 분해용 — 시작부터 기록할 프레임 수")]
        [Range(5, 120)]
        public int startupProbeFrames = 40;

    }
}
