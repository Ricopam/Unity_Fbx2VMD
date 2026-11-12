using UnityEngine;
using UnityEngine.Serialization;

namespace Fbx2Vmd.FBXImporter
{
    // 세그먼트·엔드포인트 레퍼런스 직렬화 표면을 담당하는 partial 분할.
    public partial class PoseSpaceRetargeter
    {
        [Tooltip("Blend for right lower-leg-to-foot correction strength. The measured default reduces right-foot X/Z residual without worsening hips-aligned foot residual.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorRightLowerLegToFootSegmentDirectionReferenceBlendWeight")]
        [SerializeField] private float _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceBlendWeight= 0.125f;
        public float manualAnimatorRightLowerLegToFootSegmentDirectionReferenceBlendWeight { get => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceBlendWeight; set => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceBlendWeight = value; }

        [Tooltip("Runtime diagnostic start recorder frame for right lower-leg-to-foot cap. Zero disables frame gating.")]
        [Range(0f, 2000f)]
        [FormerlySerializedAs("manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateStart")]
        [SerializeField] private float _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateStart= 0f;
        public float manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateStart { get => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateStart; set => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateStart = value; }

        [Tooltip("Runtime diagnostic end recorder frame for right lower-leg-to-foot cap. Zero disables frame gating.")]
        [Range(0f, 2000f)]
        [FormerlySerializedAs("manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateEnd")]
        [SerializeField] private float _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateEnd= 0f;
        public float manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateEnd { get => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateEnd; set => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateEnd = value; }

        [Tooltip("Runtime diagnostic blend for preserving right foot world rotation after lower-leg-to-foot correction. One keeps the existing endpoint drift.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorRightLowerLegToFootSegmentDirectionReferenceEndpointBlendWeight")]
        [SerializeField] private float _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceEndpointBlendWeight= 1f;
        public float manualAnimatorRightLowerLegToFootSegmentDirectionReferenceEndpointBlendWeight { get => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceEndpointBlendWeight; set => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceEndpointBlendWeight = value; }

        [Tooltip("Skip only the foot-to-toes segment from the manual Animator lower-body segment direction correction.")]
        [FormerlySerializedAs("ShouldDisableManualAnimatorFootToToesSegmentDirectionReference")]
        [SerializeField] private bool _ShouldDisableManualAnimatorFootToToesSegmentDirectionReference= false;
        public bool ShouldDisableManualAnimatorFootToToesSegmentDirectionReference { get => _ShouldDisableManualAnimatorFootToToesSegmentDirectionReference; set => _ShouldDisableManualAnimatorFootToToesSegmentDirectionReference = value; }

        [Tooltip("Optional foot-to-toes-only segment direction max angle in degrees. Zero keeps the shared lower-body segment cap.")]
        [Range(0f, 20f)]
        [FormerlySerializedAs("manualAnimatorFootToToesSegmentDirectionReferenceMaxAngle")]
        [SerializeField] private float _manualAnimatorFootToToesSegmentDirectionReferenceMaxAngle= 0f;
        public float manualAnimatorFootToToesSegmentDirectionReferenceMaxAngle { get => _manualAnimatorFootToToesSegmentDirectionReferenceMaxAngle; set => _manualAnimatorFootToToesSegmentDirectionReferenceMaxAngle = value; }

        [Tooltip("Apply a yaw-only upper-leg correction toward the manual Animator hips-relative foot X/Z path.")]
        [FormerlySerializedAs("ShouldUseManualAnimatorFootHipsAlignedResidualYawReference")]
        [SerializeField] private bool _ShouldUseManualAnimatorFootHipsAlignedResidualYawReference= false;
        public bool ShouldUseManualAnimatorFootHipsAlignedResidualYawReference { get => _ShouldUseManualAnimatorFootHipsAlignedResidualYawReference; set => _ShouldUseManualAnimatorFootHipsAlignedResidualYawReference = value; }

        [Tooltip("Blend weight for the hips-aligned foot X/Z residual yaw correction.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorFootHipsAlignedResidualYawReferenceWeight")]
        [SerializeField] private float _manualAnimatorFootHipsAlignedResidualYawReferenceWeight= 1f;
        public float manualAnimatorFootHipsAlignedResidualYawReferenceWeight { get => _manualAnimatorFootHipsAlignedResidualYawReferenceWeight; set => _manualAnimatorFootHipsAlignedResidualYawReferenceWeight = value; }

        [Tooltip("Maximum per-frame yaw correction angle for each upper leg in degrees.")]
        [Range(0f, 45f)]
        [FormerlySerializedAs("manualAnimatorFootHipsAlignedResidualYawReferenceMaxAngle")]
        [SerializeField] private float _manualAnimatorFootHipsAlignedResidualYawReferenceMaxAngle= 15f;
        public float manualAnimatorFootHipsAlignedResidualYawReferenceMaxAngle { get => _manualAnimatorFootHipsAlignedResidualYawReferenceMaxAngle; set => _manualAnimatorFootHipsAlignedResidualYawReferenceMaxAngle = value; }

        [Tooltip("Apply manual Animator hips-relative foot positions through BipedIK as an isolated runtime candidate.")]
        [FormerlySerializedAs("useManualAnimatorBipedIkFootPositionReference")]
        [SerializeField] private bool _useManualAnimatorBipedIkFootPositionReference= false;
        public bool useManualAnimatorBipedIkFootPositionReference { get => _useManualAnimatorBipedIkFootPositionReference; set => _useManualAnimatorBipedIkFootPositionReference = value; }

        [Tooltip("Blend weight for manual Animator BipedIK foot position targets.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorBipedIkFootPositionReferenceWeight")]
        [SerializeField] private float _manualAnimatorBipedIkFootPositionReferenceWeight= 0.65f;
        public float manualAnimatorBipedIkFootPositionReferenceWeight { get => _manualAnimatorBipedIkFootPositionReferenceWeight; set => _manualAnimatorBipedIkFootPositionReferenceWeight = value; }

        [Tooltip("Maximum per-frame BipedIK foot target correction distance from the current target foot position.")]
        [Range(0f, 0.2f)]
        [FormerlySerializedAs("manualAnimatorBipedIkFootPositionReferenceMaxOffset")]
        [SerializeField] private float _manualAnimatorBipedIkFootPositionReferenceMaxOffset= 0.12f;
        public float manualAnimatorBipedIkFootPositionReferenceMaxOffset { get => _manualAnimatorBipedIkFootPositionReferenceMaxOffset; set => _manualAnimatorBipedIkFootPositionReferenceMaxOffset = value; }

        [Tooltip("Runtime diagnostic: apply a bounded right foot/toes endpoint X/Z correction immediately after SetHumanPose.")]
        [FormerlySerializedAs("usePostSetHumanPoseRightEndpointPositionReference")]
        [SerializeField] private bool _usePostSetHumanPoseRightEndpointPositionReference= false;
        public bool usePostSetHumanPoseRightEndpointPositionReference { get => _usePostSetHumanPoseRightEndpointPositionReference; set => _usePostSetHumanPoseRightEndpointPositionReference = value; }

        [Tooltip("Blend weight for post-SetHumanPose right-foot endpoint X/Z correction.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("postSetHumanPoseRightEndpointPositionReferenceWeight")]
        [SerializeField] private float _postSetHumanPoseRightEndpointPositionReferenceWeight= 1f;
        public float postSetHumanPoseRightEndpointPositionReferenceWeight { get => _postSetHumanPoseRightEndpointPositionReferenceWeight; set => _postSetHumanPoseRightEndpointPositionReferenceWeight = value; }

        [Tooltip("Maximum per-frame post-SetHumanPose right-foot endpoint X/Z correction distance.")]
        [Range(0f, 0.2f)]
        [FormerlySerializedAs("postSetHumanPoseRightEndpointPositionReferenceMaxOffset")]
        [SerializeField] private float _postSetHumanPoseRightEndpointPositionReferenceMaxOffset= 0.04f;
        public float postSetHumanPoseRightEndpointPositionReferenceMaxOffset { get => _postSetHumanPoseRightEndpointPositionReferenceMaxOffset; set => _postSetHumanPoseRightEndpointPositionReferenceMaxOffset = value; }

        [Tooltip("Scale applied only to positive world-Z endpoint correction after SetHumanPose; 1 keeps existing behavior.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("postSetHumanPoseRightEndpointPositionReferencePositiveZScale")]
        [SerializeField] private float _postSetHumanPoseRightEndpointPositionReferencePositiveZScale= 1f;
        public float postSetHumanPoseRightEndpointPositionReferencePositiveZScale { get => _postSetHumanPoseRightEndpointPositionReferencePositiveZScale; set => _postSetHumanPoseRightEndpointPositionReferencePositiveZScale = value; }

        [Tooltip("Blend from foot-only endpoint delta to the existing foot/toes average after SetHumanPose; 1 keeps existing behavior.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("postSetHumanPoseRightEndpointPositionReferenceToesBlendWeight")]
        [SerializeField] private float _postSetHumanPoseRightEndpointPositionReferenceToesBlendWeight= 1f;
        public float postSetHumanPoseRightEndpointPositionReferenceToesBlendWeight { get => _postSetHumanPoseRightEndpointPositionReferenceToesBlendWeight; set => _postSetHumanPoseRightEndpointPositionReferenceToesBlendWeight = value; }

        [Tooltip("First legacy animation frame for post-SetHumanPose right-foot endpoint correction; 0 with end 0 keeps existing behavior.")]
        [Range(0f, 6000f)]
        [FormerlySerializedAs("postSetHumanPoseRightEndpointPositionReferenceFrameGateStart")]
        [SerializeField] private float _postSetHumanPoseRightEndpointPositionReferenceFrameGateStart= 0f;
        public float postSetHumanPoseRightEndpointPositionReferenceFrameGateStart { get => _postSetHumanPoseRightEndpointPositionReferenceFrameGateStart; set => _postSetHumanPoseRightEndpointPositionReferenceFrameGateStart = value; }

        [Tooltip("Last legacy animation frame for post-SetHumanPose right-foot endpoint correction; 0 with start 0 keeps existing behavior.")]
        [Range(0f, 6000f)]
        [FormerlySerializedAs("postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd")]
        [SerializeField] private float _postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd= 0f;
        public float postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd { get => _postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd; set => _postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd = value; }

        [Tooltip("Runtime diagnostic: apply the post-SetHumanPose endpoint correction to the left foot row instead of the right foot row.")]
        [FormerlySerializedAs("ShouldUseLeftSideForPostSetHumanPoseEndpointPosition")]
        [SerializeField] private bool _ShouldUseLeftSideForPostSetHumanPoseEndpointPosition= false;
        public bool ShouldUseLeftSideForPostSetHumanPoseEndpointPosition { get => _ShouldUseLeftSideForPostSetHumanPoseEndpointPosition; set => _ShouldUseLeftSideForPostSetHumanPoseEndpointPosition = value; }

        [Tooltip("Use the first matched reference foot X/Z offset as the post-SetHumanPose right-foot correction basis.")]
        [FormerlySerializedAs("usePostSetHumanPoseRightFootEvaluatorXzReference")]
        [SerializeField] private bool _usePostSetHumanPoseRightFootEvaluatorXzReference= false;
        public bool usePostSetHumanPoseRightFootEvaluatorXzReference { get => _usePostSetHumanPoseRightFootEvaluatorXzReference; set => _usePostSetHumanPoseRightFootEvaluatorXzReference = value; }

        [Tooltip("Target normalized right-foot X/Z magnitude for the first-offset evaluator-basis post-SetHumanPose prototype.")]
        [Range(0f, 0.2f)]
        [FormerlySerializedAs("postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude")]
        [SerializeField] private float _postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude= 0.049f;
        public float postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude { get => _postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude; set => _postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude = value; }

        [Tooltip("Apply a right-foot endpoint X/Z correction immediately before SetHumanPose as an isolated runtime candidate.")]
        [FormerlySerializedAs("usePreSetHumanPoseRightEndpointPositionReference")]
        [SerializeField] private bool _usePreSetHumanPoseRightEndpointPositionReference= false;
        public bool usePreSetHumanPoseRightEndpointPositionReference { get => _usePreSetHumanPoseRightEndpointPositionReference; set => _usePreSetHumanPoseRightEndpointPositionReference = value; }

        [Tooltip("Blend weight for pre-SetHumanPose right-foot endpoint X/Z correction.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("preSetHumanPoseRightEndpointPositionReferenceWeight")]
        [SerializeField] private float _preSetHumanPoseRightEndpointPositionReferenceWeight= 1f;
        public float preSetHumanPoseRightEndpointPositionReferenceWeight { get => _preSetHumanPoseRightEndpointPositionReferenceWeight; set => _preSetHumanPoseRightEndpointPositionReferenceWeight = value; }

        [Tooltip("Maximum per-frame pre-SetHumanPose right-foot endpoint X/Z correction distance.")]
        [Range(0f, 0.2f)]
        [FormerlySerializedAs("preSetHumanPoseRightEndpointPositionReferenceMaxOffset")]
        [SerializeField] private float _preSetHumanPoseRightEndpointPositionReferenceMaxOffset= 0.025f;
        public float preSetHumanPoseRightEndpointPositionReferenceMaxOffset { get => _preSetHumanPoseRightEndpointPositionReferenceMaxOffset; set => _preSetHumanPoseRightEndpointPositionReferenceMaxOffset = value; }

        [Tooltip("Scale applied only to positive world-Z endpoint correction before SetHumanPose; 1 keeps existing behavior.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("preSetHumanPoseRightEndpointPositionReferencePositiveZScale")]
        [SerializeField] private float _preSetHumanPoseRightEndpointPositionReferencePositiveZScale= 1f;
        public float preSetHumanPoseRightEndpointPositionReferencePositiveZScale { get => _preSetHumanPoseRightEndpointPositionReferencePositiveZScale; set => _preSetHumanPoseRightEndpointPositionReferencePositiveZScale = value; }

        [Tooltip("Blend from foot-only endpoint delta to the foot/toes average before SetHumanPose.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight")]
        [SerializeField] private float _preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight= 1f;
        public float preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight { get => _preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight; set => _preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight = value; }

        [Tooltip("First legacy animation frame for pre-SetHumanPose right-foot endpoint correction; 0 with end 0 keeps existing behavior.")]
        [Range(0f, 6000f)]
        [FormerlySerializedAs("preSetHumanPoseRightEndpointPositionReferenceFrameGateStart")]
        [SerializeField] private float _preSetHumanPoseRightEndpointPositionReferenceFrameGateStart= 0f;
        public float preSetHumanPoseRightEndpointPositionReferenceFrameGateStart { get => _preSetHumanPoseRightEndpointPositionReferenceFrameGateStart; set => _preSetHumanPoseRightEndpointPositionReferenceFrameGateStart = value; }

        [Tooltip("Last legacy animation frame for pre-SetHumanPose right-foot endpoint correction; 0 with start 0 keeps existing behavior.")]
        [Range(0f, 6000f)]
        [FormerlySerializedAs("preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd")]
        [SerializeField] private float _preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd= 0f;
        public float preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd { get => _preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd; set => _preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd = value; }

        [Tooltip("Runtime diagnostic: apply the pre-SetHumanPose endpoint correction to the left foot row instead of the right foot row.")]
        [FormerlySerializedAs("ShouldUseLeftSideForPreSetHumanPoseEndpointPosition")]
        [SerializeField] private bool _ShouldUseLeftSideForPreSetHumanPoseEndpointPosition= false;
        public bool ShouldUseLeftSideForPreSetHumanPoseEndpointPosition { get => _ShouldUseLeftSideForPreSetHumanPoseEndpointPosition; set => _ShouldUseLeftSideForPreSetHumanPoseEndpointPosition = value; }

        [Tooltip("Runtime diagnostic: use ghost/current endpoint rows as a sign-corrected bodyPosition X/Z translation basis before SetHumanPose.")]
        [FormerlySerializedAs("preSetHumanPoseEndpointPositionUseGhostCurrentBasis")]
        [SerializeField] private bool _preSetHumanPoseEndpointPositionUseGhostCurrentBasis= false;
        public bool preSetHumanPoseEndpointPositionUseGhostCurrentBasis { get => _preSetHumanPoseEndpointPositionUseGhostCurrentBasis; set => _preSetHumanPoseEndpointPositionUseGhostCurrentBasis = value; }

        [Tooltip("Runtime diagnostic: invert the pre-SetHumanPose endpoint bodyPosition X input delta.")]
        [FormerlySerializedAs("ShouldInvertPreSetHumanPoseEndpointPositionBodyX")]
        [SerializeField] private bool _ShouldInvertPreSetHumanPoseEndpointPositionBodyX= false;
        public bool ShouldInvertPreSetHumanPoseEndpointPositionBodyX { get => _ShouldInvertPreSetHumanPoseEndpointPositionBodyX; set => _ShouldInvertPreSetHumanPoseEndpointPositionBodyX = value; }

        [Tooltip("Runtime diagnostic: invert the pre-SetHumanPose endpoint bodyPosition Z input delta.")]
        [FormerlySerializedAs("ShouldInvertPreSetHumanPoseEndpointPositionBodyZ")]
        [SerializeField] private bool _ShouldInvertPreSetHumanPoseEndpointPositionBodyZ= false;
        public bool ShouldInvertPreSetHumanPoseEndpointPositionBodyZ { get => _ShouldInvertPreSetHumanPoseEndpointPositionBodyZ; set => _ShouldInvertPreSetHumanPoseEndpointPositionBodyZ = value; }

        [Tooltip("엄지 시작 위치 보정 강도입니다.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorThumbBasePositionWeight")]
        [SerializeField] private float _manualAnimatorThumbBasePositionWeight= 1f;
        public float manualAnimatorThumbBasePositionWeight { get => _manualAnimatorThumbBasePositionWeight; private set => _manualAnimatorThumbBasePositionWeight = value; }

        [Tooltip("엄지 시작 위치가 원본 위치에서 벗어날 수 있는 최대 거리입니다.")]
        [Range(0f, 0.03f)]
        [FormerlySerializedAs("manualAnimatorThumbBasePositionMaxOffset")]
        [SerializeField] private float _manualAnimatorThumbBasePositionMaxOffset= 0.03f;
        public float manualAnimatorThumbBasePositionMaxOffset { get => _manualAnimatorThumbBasePositionMaxOffset; private set => _manualAnimatorThumbBasePositionMaxOffset = value; }

        [Tooltip("엄지 벌림 muscle 최소값입니다.")]
        [Range(-1.5f, 0f)]
        [FormerlySerializedAs("thumbSpreadMin")]
        [SerializeField] private float _thumbSpreadMin= -0.9f;
        public float thumbSpreadMin { get => _thumbSpreadMin; private set => _thumbSpreadMin = value; }

        [Tooltip("엄지 벌림 muscle 최대값입니다.")]
        [Range(0f, 1.5f)]
        [FormerlySerializedAs("thumbSpreadMax")]
        [SerializeField] private float _thumbSpreadMax= 0.9f;
        public float thumbSpreadMax { get => _thumbSpreadMax; private set => _thumbSpreadMax = value; }

        [Tooltip("엄지 해부학적 제한이 값을 바꿨을 때 최초 1회 진단 로그를 출력합니다.")]
        [FormerlySerializedAs("logThumbAnatomicalGuardCorrections")]
        [SerializeField] private bool _logThumbAnatomicalGuardCorrections= false;
        public bool logThumbAnatomicalGuardCorrections { get => _logThumbAnatomicalGuardCorrections; private set => _logThumbAnatomicalGuardCorrections = value; }

        [Tooltip("엄지 muscle 제한 이후에도 YYB 엄지 본이 손 구조상 이상하게 꺾이면, 실제 엄지 본 localRotation을 기준 자세 근처로 제한합니다.")]
        [FormerlySerializedAs("enableThumbLocalRotationGuard")]
        [SerializeField] private bool _enableThumbLocalRotationGuard= true;
        public bool enableThumbLocalRotationGuard { get => _enableThumbLocalRotationGuard; private set => _enableThumbLocalRotationGuard = value; }

        [Tooltip("엄지 첫 번째 관절이 기준 자세에서 벗어날 수 있는 최대 각도입니다.")]
        [Range(0f, 90f)]
        [FormerlySerializedAs("thumbProximalMaxLocalAngle")]
        [SerializeField] private float _thumbProximalMaxLocalAngle= 10f;
        public float thumbProximalMaxLocalAngle { get => _thumbProximalMaxLocalAngle; private set => _thumbProximalMaxLocalAngle = value; }

        [Tooltip("엄지 두 번째 관절이 기준 자세에서 벗어날 수 있는 최대 각도입니다.")]
        [Range(0f, 120f)]
        [FormerlySerializedAs("thumbIntermediateMaxLocalAngle")]
        [SerializeField] private float _thumbIntermediateMaxLocalAngle= 55f;
        public float thumbIntermediateMaxLocalAngle { get => _thumbIntermediateMaxLocalAngle; private set => _thumbIntermediateMaxLocalAngle = value; }

        [Tooltip("엄지 끝 관절이 기준 자세에서 벗어날 수 있는 최대 각도입니다.")]
        [Range(0f, 120f)]
        [FormerlySerializedAs("thumbDistalMaxLocalAngle")]
        [SerializeField] private float _thumbDistalMaxLocalAngle= 55f;
        public float thumbDistalMaxLocalAngle { get => _thumbDistalMaxLocalAngle; private set => _thumbDistalMaxLocalAngle = value; }

        [Tooltip("엄지 본 localRotation 제한이 값을 바꿨을 때 최초 1회 진단 로그를 출력합니다.")]
        [FormerlySerializedAs("logThumbLocalRotationGuardCorrections")]
        [SerializeField] private bool _logThumbLocalRotationGuardCorrections= false;
        public bool logThumbLocalRotationGuardCorrections { get => _logThumbLocalRotationGuardCorrections; private set => _logThumbLocalRotationGuardCorrections = value; }
    }
}
