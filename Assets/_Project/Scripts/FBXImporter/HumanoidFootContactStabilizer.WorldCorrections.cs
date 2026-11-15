using System;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class HumanoidFootContactStabilizer
    {

        struct WorldCorrections
        {
            internal Vector3 LeftCorrection;
            internal Vector3 RightCorrection;
            internal Vector3 LeftToeDirection;
            internal Vector3 RightToeDirection;
            internal Vector3 LeftBendNormal;
            internal Vector3 RightBendNormal;
        }

    }
}