using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidThumbDeformationGuard
    {
        [SerializeField] private Animator targetAnimator;
        [SerializeField] private PoseSpaceRetargeter linkedPoseSpaceRetargeter;
        // 연결된 리타게터가 없음을 이미 확인했는지. 재설정 때 초기화함(직렬화하지 않음).
        private bool _hasSearchedMissingRetargeter;
        [SerializeField, Range(0f, 90f)] private float proximalMaxLocalAngle = 28f;
        [SerializeField, Range(0f, 120f)] private float intermediateMaxLocalAngle = 55f;
        [SerializeField, Range(0f, 120f)] private float distalMaxLocalAngle = 55f;
        [SerializeField] private Vector3 proximalLocalRotationOffset;
        [SerializeField] private bool mirrorRightProximalLocalRotationOffset = true;
        [SerializeField] private Vector3 leftProximalLocalRotationOffset;
        [SerializeField] private Vector3 rightProximalLocalRotationOffset;
        [SerializeField] private bool logCorrections;
        [SerializeField] private bool clampHumanoidThumbRotations = true;
        [SerializeField] private bool stabilizeDetachedThumbBasePalm = false;
        [SerializeField, Range(0f, 1f)] private float detachedThumbBasePalmStabilizeWeight = 0f;
        [SerializeField, Range(0f, 45f)] private float detachedThumbBasePalmMaxLocalAngle = 45f;
    }
}
