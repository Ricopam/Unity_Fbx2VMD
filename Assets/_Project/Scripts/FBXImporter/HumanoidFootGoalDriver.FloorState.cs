using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class HumanoidFootGoalDriver
    {
        // 지면 높이 두 벌 — 소스 월드값은 게이트 증거용, 골반 상대값은 goal 스냅용.
        private float[] _leftFloorWorldY = Array.Empty<float>();
        private float[] _rightFloorWorldY = Array.Empty<float>();
        private float[] _leftFloorRootSpaceY = Array.Empty<float>();
        private float[] _rightFloorRootSpaceY = Array.Empty<float>();

    }
}