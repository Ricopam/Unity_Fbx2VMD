using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidArmSleeveAnchorGuard
    {

        private struct AnchorCorrection
        {
            public Transform Source;
            public Transform Node;
            public Quaternion BaselineLocalRotation;
            public Quaternion BaselineSourceRelativeRotation;
            public float Influence;

            public AnchorCorrection(
                Transform source,
                Transform node,
                Quaternion baselineLocalRotation,
                Quaternion baselineSourceRelativeRotation,
                float influence)
            {
                Source = source;
                Node = node;
                BaselineLocalRotation = baselineLocalRotation;
                BaselineSourceRelativeRotation = baselineSourceRelativeRotation;
                Influence = influence;
            }
        }

    }
}
