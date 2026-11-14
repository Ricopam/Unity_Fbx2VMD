using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidThumbDeformationGuard
    {
        [SerializeField] private Animator targetAnimator;
        [SerializeField] private PoseSpaceRetargeter linkedPoseSpaceRetargeter;
        [SerializeField, Range(0f, 90f)] private float proximalMaxLocalAngle = 28f;
        [SerializeField, Range(0f, 120f)] private float intermediateMaxLocalAngle = 55f;
        [SerializeField, Range(0f, 120f)] private float distalMaxLocalAngle = 55f;
        [SerializeField] private Vector3 proximalLocalRotationOffset;
        [SerializeField] private bool mirrorRightProximalLocalRotationOffset = true;
        [SerializeField] private Vector3 leftProximalLocalRotationOffset;
        [SerializeField] private Vector3 rightProximalLocalRotationOffset;
        [SerializeField] private bool logCorrections;
        [SerializeField] private bool clampHumanoidThumbRotations = true;
        [SerializeField] private bool syncDetachedThumbBaseHelpers = true;
        [SerializeField] private bool syncDetachedThumbBaseHelperPositions = true;
        [SerializeField, Range(0f, 1f)] private float detachedThumbBaseHelperSyncWeight = 0.8f;
        [SerializeField, Range(0f, 45f)] private float detachedThumbBaseHelperMaxLocalAngle = 28f;
        [SerializeField, Range(0f, 0.02f)] private float detachedThumbBaseHelperMaxPositionOffset = 0.008f;
        [SerializeField] private Vector3 leftDetachedThumbBaseHelperDeltaAxisOffset;
        [SerializeField] private Vector3 rightDetachedThumbBaseHelperDeltaAxisOffset;
        [SerializeField] private Vector3 leftDetachedThumbBaseHelperTargetRotationOffset;
        [SerializeField] private Vector3 rightDetachedThumbBaseHelperTargetRotationOffset;
        [SerializeField] private bool stabilizeDetachedThumbBasePalm = false;
        [SerializeField, Range(0f, 1f)] private float detachedThumbBasePalmStabilizeWeight = 0f;
        [SerializeField, Range(0f, 45f)] private float detachedThumbBasePalmMaxLocalAngle = 45f;
    }
}
