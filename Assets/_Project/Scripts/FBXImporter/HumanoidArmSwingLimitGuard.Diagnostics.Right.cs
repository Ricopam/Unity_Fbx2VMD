using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidArmSwingLimitGuard
    {

            public int LastRightApplied { get; private set; }
            public int LastRightHorizontalReachApplied { get; private set; }
            public int LastRightRaisedReachApplied { get; private set; }
            public float LastRightForearmStretchBefore { get; private set; } = float.NaN;
            public float LastRightForearmStretchAfter { get; private set; } = float.NaN;
            public float LastRightForearmStretchDelta { get; private set; } = float.NaN;

    }
}