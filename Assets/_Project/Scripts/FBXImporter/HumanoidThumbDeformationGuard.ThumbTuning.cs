using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidThumbDeformationGuard
    {
        [SerializeField] private bool stabilizeThumbWebbingCrease = true;
        [SerializeField, Range(0f, 1f)] private float thumbWebbingCreaseStabilizeWeight = 0.35f;
        [SerializeField, Range(0f, 45f)] private float thumbWebbingCreaseMaxLocalAngle = 18f;
        [SerializeField, Range(0f, 0.02f)] private float thumbWebbingCreaseMaxPositionOffset = 0.005f;
        [SerializeField] private bool enableThumbVisualLengthGuard = true;
        [SerializeField, Range(0f, 1f)] private float thumbProjectionMinPalmNormal = 0.358f;
        [SerializeField, Range(0f, 1f)] private float thumbProjectionMaxPalmNormal = 0.58f;
        [SerializeField, Range(0f, 1f)] private float thumbProjectionGuardWeight = 1f;
        [SerializeField, Range(0f, 90f)] private float thumbIndexMaxSpreadAngle = 70f;
        [SerializeField, Range(0f, 1f)] private float thumbIndexSpreadGuardWeight = 1f;
        [SerializeField, Range(0f, 60f)] private float thumbMaxSegmentBendAngle = 10f;
        [SerializeField, Range(0f, 1f)] private float thumbSegmentStraightenWeight = 0.9f;
        [SerializeField] private bool suppressPoseShapingWithManualThumbReference = false;
    }
}
