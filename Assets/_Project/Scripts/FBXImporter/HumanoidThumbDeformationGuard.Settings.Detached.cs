using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidThumbDeformationGuard
    {

        [SerializeField] private bool syncDetachedThumbBaseHelpers = true;
        [SerializeField] private bool syncDetachedThumbBaseHelperPositions = true;
        [SerializeField, Range(0f, 1f)] private float detachedThumbBaseHelperSyncWeight = 0.8f;
        [SerializeField, Range(0f, 45f)] private float detachedThumbBaseHelperMaxLocalAngle = 28f;
        [SerializeField, Range(0f, 0.02f)] private float detachedThumbBaseHelperMaxPositionOffset = 0.008f;
        [SerializeField] private Vector3 leftDetachedThumbBaseHelperDeltaAxisOffset;
        [SerializeField] private Vector3 rightDetachedThumbBaseHelperDeltaAxisOffset;
        [SerializeField] private Vector3 leftDetachedThumbBaseHelperTargetRotationOffset;
        [SerializeField] private Vector3 rightDetachedThumbBaseHelperTargetRotationOffset;

    }
}