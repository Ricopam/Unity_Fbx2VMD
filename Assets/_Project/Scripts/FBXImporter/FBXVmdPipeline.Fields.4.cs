using UnityEngine;
using UnityEngine.Serialization;
using System;
using System.IO;
using System.Linq;

namespace Fbx2Vmd.FBXImporter
{
    public partial class FBXVmdPipeline
    {
        [Tooltip("Runtime diagnostic: apply the post-SetHumanPose endpoint correction to the left foot row instead of the right foot row.")]
        [FormerlySerializedAs("postSetHumanPoseEndpointPositionUseLeftSide")] [SerializeField] private bool _shouldUseLeftSideForPostSetHumanPoseEndpointPosition = false;

        [Tooltip("Use the first matched reference foot X/Z offset as the post-SetHumanPose right-foot correction basis.")]
        [FormerlySerializedAs("usePostSetHumanPoseRightFootEvaluatorXzReference")]
        [SerializeField] private bool _usePostSetHumanPoseRightFootEvaluatorXzReference= false;
        public bool usePostSetHumanPoseRightFootEvaluatorXzReference { get => _usePostSetHumanPoseRightFootEvaluatorXzReference; set => _usePostSetHumanPoseRightFootEvaluatorXzReference = value; }

        [Tooltip("Target normalized right-foot X/Z magnitude for the first-offset evaluator-basis post-SetHumanPose prototype.")]
        [Range(0f, 0.2f)] [FormerlySerializedAs("postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude")]
        [SerializeField] private float _postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude= 0.049f;
        public float postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude { get => _postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude; set => _postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude = value; }

        [Tooltip("Apply a right-foot endpoint X/Z correction immediately before SetHumanPose as an isolated runtime candidate.")]
        [FormerlySerializedAs("usePreSetHumanPoseRightEndpointPositionReference")]
        [SerializeField] private bool _usePreSetHumanPoseRightEndpointPositionReference= false;
        public bool usePreSetHumanPoseRightEndpointPositionReference { get => _usePreSetHumanPoseRightEndpointPositionReference; set => _usePreSetHumanPoseRightEndpointPositionReference = value; }

        [Tooltip("Blend weight for pre-SetHumanPose right-foot endpoint X/Z correction.")]
        [Range(0f, 1f)] [FormerlySerializedAs("preSetHumanPoseRightEndpointPositionReferenceWeight")]
        [SerializeField] private float _preSetHumanPoseRightEndpointPositionReferenceWeight= 1f;
        public float preSetHumanPoseRightEndpointPositionReferenceWeight { get => _preSetHumanPoseRightEndpointPositionReferenceWeight; set => _preSetHumanPoseRightEndpointPositionReferenceWeight = value; }

        [Tooltip("Maximum per-frame pre-SetHumanPose right-foot endpoint X/Z correction distance.")]
        [Range(0f, 0.2f)] [FormerlySerializedAs("preSetHumanPoseRightEndpointPositionReferenceMaxOffset")]
        [SerializeField] private float _preSetHumanPoseRightEndpointPositionReferenceMaxOffset= 0.025f;
        public float preSetHumanPoseRightEndpointPositionReferenceMaxOffset { get => _preSetHumanPoseRightEndpointPositionReferenceMaxOffset; set => _preSetHumanPoseRightEndpointPositionReferenceMaxOffset = value; }

        [Tooltip("Scale applied only to positive world-Z endpoint correction before SetHumanPose; 1 keeps existing behavior.")]
        [Range(0f, 1f)] [FormerlySerializedAs("preSetHumanPoseRightEndpointPositionReferencePositiveZScale")]
        [SerializeField] private float _preSetHumanPoseRightEndpointPositionReferencePositiveZScale= 1f;
        public float preSetHumanPoseRightEndpointPositionReferencePositiveZScale { get => _preSetHumanPoseRightEndpointPositionReferencePositiveZScale; set => _preSetHumanPoseRightEndpointPositionReferencePositiveZScale = value; }

        [Tooltip("Blend from foot-only endpoint delta to the foot/toes average before SetHumanPose.")]
        [Range(0f, 1f)] [FormerlySerializedAs("preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight")]
        [SerializeField] private float _preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight= 1f;
        public float preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight { get => _preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight; set => _preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight = value; }

        [Tooltip("First legacy animation frame for pre-SetHumanPose right-foot endpoint correction; 0 with end 0 keeps existing behavior.")]
        [Range(0f, 6000f)] [FormerlySerializedAs("preSetHumanPoseRightEndpointPositionReferenceFrameGateStart")]
        [SerializeField] private float _preSetHumanPoseRightEndpointPositionReferenceFrameGateStart= 0f;
        public float preSetHumanPoseRightEndpointPositionReferenceFrameGateStart { get => _preSetHumanPoseRightEndpointPositionReferenceFrameGateStart; set => _preSetHumanPoseRightEndpointPositionReferenceFrameGateStart = value; }

        [Tooltip("Last legacy animation frame for pre-SetHumanPose right-foot endpoint correction; 0 with start 0 keeps existing behavior.")]
        [Range(0f, 6000f)] [FormerlySerializedAs("preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd")]
        [SerializeField] private float _preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd= 0f;
        public float preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd { get => _preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd; set => _preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd = value; }

        [Tooltip("Runtime diagnostic: apply the pre-SetHumanPose endpoint correction to the left foot row instead of the right foot row.")]
        [FormerlySerializedAs("preSetHumanPoseEndpointPositionUseLeftSide")] [SerializeField] private bool _shouldUseLeftSideForPreSetHumanPoseEndpointPosition = false;

        [Tooltip("Runtime diagnostic: use ghost/current endpoint rows as a sign-corrected bodyPosition X/Z translation basis before SetHumanPose.")]
        [FormerlySerializedAs("preSetHumanPoseEndpointPositionUseGhostCurrentBasis")]
        [SerializeField] private bool _preSetHumanPoseEndpointPositionUseGhostCurrentBasis= false;
        public bool preSetHumanPoseEndpointPositionUseGhostCurrentBasis { get => _preSetHumanPoseEndpointPositionUseGhostCurrentBasis; set => _preSetHumanPoseEndpointPositionUseGhostCurrentBasis = value; }

        [Tooltip("Runtime diagnostic: invert the pre-SetHumanPose endpoint bodyPosition X input delta.")]
        [FormerlySerializedAs("preSetHumanPoseEndpointPositionInvertBodyPositionX")] [SerializeField] private bool _shouldInvertPreSetHumanPoseEndpointPositionBodyX = false;

        [Tooltip("Runtime diagnostic: invert the pre-SetHumanPose endpoint bodyPosition Z input delta.")]
        [FormerlySerializedAs("preSetHumanPoseEndpointPositionInvertBodyPositionZ")] [SerializeField] private bool _shouldInvertPreSetHumanPoseEndpointPositionBodyZ = false;

        [Tooltip("엄지 체인의 localRotation도 Sub_Manual/testPrefab Animator가 같은 FBX clip에서 평가한 값을 기준으로 덮어씁니다. YYB와 testPrefab의 Humanoid muscle은 같지만 엄지 로컬 축 해석이 달라 보일 때 사용합니다.")]
        [FormerlySerializedAs("useManualAnimatorThumbLocalRotationReference")]
        [SerializeField] private bool _useManualAnimatorThumbLocalRotationReference= false;
        public bool useManualAnimatorThumbLocalRotationReference { get => _useManualAnimatorThumbLocalRotationReference; set => _useManualAnimatorThumbLocalRotationReference = value; }

        [Tooltip("손목 localRotation을 Sub_Manual/testPrefab Animator가 같은 FBX clip에서 평가한 값을 기준으로 덮어씁니다. t13.2 hand pose parity 회귀 보호용입니다.")]
        [FormerlySerializedAs("useManualAnimatorHandLocalRotationReference")]
        [SerializeField] private bool _useManualAnimatorHandLocalRotationReference= false;
        public bool useManualAnimatorHandLocalRotationReference { get => _useManualAnimatorHandLocalRotationReference; set => _useManualAnimatorHandLocalRotationReference = value; }

        [Tooltip("엄지 세그먼트 방향을 Sub_Manual/testPrefab의 손 기준 방향에 맞춥니다. 모델별 bind axis 차이 때문에 localRotation 숫자가 같아도 손 모양이 달라질 때 사용합니다.")]
        [FormerlySerializedAs("useManualAnimatorThumbSegmentDirectionReference")]
        [SerializeField] private bool _useManualAnimatorThumbSegmentDirectionReference= false;
        public bool useManualAnimatorThumbSegmentDirectionReference { get => _useManualAnimatorThumbSegmentDirectionReference; private set => _useManualAnimatorThumbSegmentDirectionReference = value; }

        [Tooltip("엄지 세그먼트 방향 보정 강도입니다. 1이면 testPrefab 손 기준 방향에 맞추고, 0이면 보정하지 않습니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorThumbSegmentDirectionWeight")]
        [SerializeField] private float _manualAnimatorThumbSegmentDirectionWeight= 1f;
        public float manualAnimatorThumbSegmentDirectionWeight { get => _manualAnimatorThumbSegmentDirectionWeight; private set => _manualAnimatorThumbSegmentDirectionWeight = value; }

        [Tooltip("손바닥 기준 Hand->ThumbIntermediate 방향을 Sub_Manual/testPrefab의 손 기준 방향에 맞춥니다. 엄지 시작 방향이 손바닥 밖으로 탈골된 것처럼 보일 때 사용합니다.")]
        [FormerlySerializedAs("useManualAnimatorThumbHandDirectionReference")]
        [SerializeField] private bool _useManualAnimatorThumbHandDirectionReference= false;
        public bool useManualAnimatorThumbHandDirectionReference { get => _useManualAnimatorThumbHandDirectionReference; private set => _useManualAnimatorThumbHandDirectionReference = value; }

        [Tooltip("손바닥 기준 엄지 시작 방향 보정 강도입니다. 1이면 testPrefab의 Hand->ThumbIntermediate 방향에 맞춥니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorThumbHandDirectionWeight")]
        [SerializeField] private float _manualAnimatorThumbHandDirectionWeight= 1f;
        public float manualAnimatorThumbHandDirectionWeight { get => _manualAnimatorThumbHandDirectionWeight; private set => _manualAnimatorThumbHandDirectionWeight = value; }

        [Tooltip("손바닥 전체 프레임을 Sub_Manual/testPrefab의 손바닥 방향에 맞춥니다. MMD 기준과 손목/엄지 뿌리 실루엣이 다를 때 사용합니다.")]
        [FormerlySerializedAs("useManualAnimatorHandPalmFrameReference")]
        [SerializeField] private bool _useManualAnimatorHandPalmFrameReference= false;
        public bool useManualAnimatorHandPalmFrameReference { get => _useManualAnimatorHandPalmFrameReference; set => _useManualAnimatorHandPalmFrameReference = value; }

        [Tooltip("손바닥 프레임 보정 강도입니다. MMD 396프레임 직접 비교 기준으로 기본값은 1.00입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorHandPalmFrameWeight")]
        [SerializeField] private float _manualAnimatorHandPalmFrameWeight= 1f;
        public float manualAnimatorHandPalmFrameWeight { get => _manualAnimatorHandPalmFrameWeight; set => _manualAnimatorHandPalmFrameWeight = value; }

        [Tooltip("엄지 첫 본 위치를 Sub_Manual/testPrefab의 손 기준 위치 비율로 맞춥니다. YYB 엄지 시작부가 손바닥 안쪽으로 붙어 보일 때 사용합니다.")]
        [FormerlySerializedAs("useManualAnimatorThumbBasePositionReference")]
        [SerializeField] private bool _useManualAnimatorThumbBasePositionReference= false;
        public bool useManualAnimatorThumbBasePositionReference { get => _useManualAnimatorThumbBasePositionReference; private set => _useManualAnimatorThumbBasePositionReference = value; }

        [Tooltip("엄지 첫 본 위치 보정 강도입니다. 1이면 testPrefab 손 기준 위치 비율을 그대로 적용합니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorThumbBasePositionWeight")]
        [SerializeField] private float _manualAnimatorThumbBasePositionWeight= 1f;
        public float manualAnimatorThumbBasePositionWeight { get => _manualAnimatorThumbBasePositionWeight; private set => _manualAnimatorThumbBasePositionWeight = value; }

        [Tooltip("엄지 첫 본 위치가 원본 위치에서 한 프레임에 벗어날 수 있는 최대 거리입니다.")]
        [Range(0f, 0.03f)] [FormerlySerializedAs("manualAnimatorThumbBasePositionMaxOffset")]
        [SerializeField] private float _manualAnimatorThumbBasePositionMaxOffset= 0.03f;
        public float manualAnimatorThumbBasePositionMaxOffset { get => _manualAnimatorThumbBasePositionMaxOffset; private set => _manualAnimatorThumbBasePositionMaxOffset = value; }

        [Tooltip("손가락 기준 평가에 사용할 수동 기준 프리팹입니다. 비워두면 VMDRecorderSample testPrefab을 자동으로 찾습니다.")]
        [FormerlySerializedAs("manualFingerReferencePrefab")]
        [SerializeField] private GameObject _manualFingerReferencePrefab;
        public GameObject manualFingerReferencePrefab { get => _manualFingerReferencePrefab; private set => _manualFingerReferencePrefab = value; }

        [Tooltip("손가락 기준 평가에 사용할 수동 Animator Controller입니다. 비워두면 TestAnimator1_Manual 또는 TestAnimator1을 자동으로 찾습니다.")]
        [FormerlySerializedAs("manualFingerReferenceController")]
        [SerializeField] private RuntimeAnimatorController _manualFingerReferenceController;
        public RuntimeAnimatorController manualFingerReferenceController { get => _manualFingerReferenceController; private set => _manualFingerReferenceController = value; }

        [Tooltip("Retarget/녹화 중 MMD4Mecanim의 어깨 PPH 보정을 잠시 끕니다. 수동 기준 모션과 어깨 형태가 달라질 수 있어 기본값은 끕니다.")]
        [FormerlySerializedAs("disableMmdShoulderPostPoseDuringRetarget")]
        [SerializeField] private bool _disableMmdShoulderPostPoseDuringRetarget= false;
        public bool disableMmdShoulderPostPoseDuringRetarget { get => _disableMmdShoulderPostPoseDuringRetarget; private set => _disableMmdShoulderPostPoseDuringRetarget = value; }

        [Header("Anatomical Retarget Guard")]
        [Tooltip("FBX Humanoid muscle 값을 [-1, 1] 범위로 제한합니다.")]
        [FormerlySerializedAs("clampRetargetMusclesToHumanRange")]
        [SerializeField] private bool _clampRetargetMusclesToHumanRange= true;
        public bool clampRetargetMusclesToHumanRange { get => _clampRetargetMusclesToHumanRange; private set => _clampRetargetMusclesToHumanRange = value; }

        [Tooltip("팔이 늘어나거나 비정상적으로 비틀리는 Humanoid arm muscle 값을 제한합니다.")]
        [FormerlySerializedAs("enableAnatomicalArmGuard")]
        [SerializeField] private bool _enableAnatomicalArmGuard= true;
        public bool enableAnatomicalArmGuard { get => _enableAnatomicalArmGuard; set => _enableAnatomicalArmGuard = value; }

        [Tooltip("Humanoid 팔 Stretch muscle 허용치입니다. Forearm Stretch는 팔꿈치 굽힘에 가까우므로 기본적으로 제한하지 않습니다.")]
        [Range(0f, 0.5f)] [FormerlySerializedAs("ArmStretchMuscleLimit")]
        [SerializeField] private float _ArmStretchMuscleLimit= 0f;
        public float ArmStretchMuscleLimit { get => _ArmStretchMuscleLimit; set => _ArmStretchMuscleLimit = value; }

        [Tooltip("Retarget 단계에서 Forearm Stretch muscle을 제한합니다. 팔 길이가 아니라 팔꿈치 굽힘에 가까워 기본값은 꺼야 합니다.")]
        [FormerlySerializedAs("clampRetargetArmStretchMuscles")]
        [SerializeField] private bool _clampRetargetArmStretchMuscles= false;
        public bool clampRetargetArmStretchMuscles { get => _clampRetargetArmStretchMuscles; set => _clampRetargetArmStretchMuscles = value; }

        [Tooltip("상완 Twist muscle 허용치입니다.")]
        [Range(0.1f, 1f)] [FormerlySerializedAs("UpperArmTwistMuscleLimit")]
        [SerializeField] private float _UpperArmTwistMuscleLimit= 0.75f;
        public float UpperArmTwistMuscleLimit { get => _UpperArmTwistMuscleLimit; private set => _UpperArmTwistMuscleLimit = value; }

        [Tooltip("전완 Twist muscle 허용치입니다.")]
        [Range(0.1f, 1f)] [FormerlySerializedAs("LowerArmTwistMuscleLimit")]
        [SerializeField] private float _LowerArmTwistMuscleLimit= 0.65f;
        public float LowerArmTwistMuscleLimit { get => _LowerArmTwistMuscleLimit; private set => _LowerArmTwistMuscleLimit = value; }

        [Tooltip("Retarget 중 Target Humanoid 본의 localPosition을 초기값으로 복구해 팔/다리 길이 변형을 막습니다.")]
        [FormerlySerializedAs("lockTargetHumanoidBonePositions")] [SerializeField] private bool _shouldLockTargetHumanoidBonePositions = true;

        [Tooltip("팔/다리 하위의 비-Humanoid 보조본 localPosition도 초기값으로 복구해 소매/팔 메시가 가늘어지는 변형을 막습니다.")]
        [FormerlySerializedAs("lockTargetLimbChildLocalPositions")]
        [SerializeField] private bool _lockTargetLimbChildLocalPositions= true;
        public bool lockTargetLimbChildLocalPositions { get => _lockTargetLimbChildLocalPositions; private set => _lockTargetLimbChildLocalPositions = value; }

        [Tooltip("팔/다리 하위의 비-Humanoid 보조본 localRotation을 초기값으로 복구합니다. YYB 소매/팔 twist 보조본 움직임을 막을 수 있어 기본값은 끕니다.")]
        [FormerlySerializedAs("lockTargetLimbChildLocalRotations")]
        [SerializeField] private bool _lockTargetLimbChildLocalRotations= false;
        public bool lockTargetLimbChildLocalRotations { get => _lockTargetLimbChildLocalRotations; private set => _lockTargetLimbChildLocalRotations = value; }

        [Tooltip("Target 캐릭터에 HumanoidArmDeformationGuard를 자동 부착해 Retarget 외 경로에서도 팔 가늘어짐을 막습니다.")]
        [FormerlySerializedAs("attachTargetArmDeformationGuard")]
        [SerializeField] private bool _attachTargetArmDeformationGuard= true;
        public bool attachTargetArmDeformationGuard { get => _attachTargetArmDeformationGuard; private set => _attachTargetArmDeformationGuard = value; }

        [Tooltip("자동 부착된 Target 가드에서 HumanPose arm muscle도 제한합니다. 기본 Retargeter 보정과 중복될 수 있어 기본값은 끕니다.")]
        [FormerlySerializedAs("targetGuardClampAnatomicalArmMuscles")]
        [SerializeField] private bool _targetGuardClampAnatomicalArmMuscles= false;
        public bool targetGuardClampAnatomicalArmMuscles { get => _targetGuardClampAnatomicalArmMuscles; set => _targetGuardClampAnatomicalArmMuscles = value; }

        [Tooltip("자동 부착된 Target 가드도 arm stretch muscle을 제한합니다. 직접 Animator 모션이 굳을 수 있어 기본값은 끕니다.")]
        [FormerlySerializedAs("targetGuardClampArmStretchMuscles")]
        [SerializeField] private bool _targetGuardClampArmStretchMuscles= false;
        public bool targetGuardClampArmStretchMuscles { get => _targetGuardClampArmStretchMuscles; set => _targetGuardClampArmStretchMuscles = value; }

        [Tooltip("팔 변형 가드가 제한/복구를 처음 수행할 때 진단 로그를 출력합니다.")]
        [FormerlySerializedAs("logArmDeformationGuardCorrections")]
        [SerializeField] private bool _logArmDeformationGuardCorrections= false;
        public bool logArmDeformationGuardCorrections { get => _logArmDeformationGuardCorrections; private set => _logArmDeformationGuardCorrections = value; }

        [Header("Animation Rigging Arm Twist Correction")]
        [Tooltip("YYB 팔 twist 보조본을 Animation Rigging TwistCorrection으로 보정합니다. 현재 자동 리타겟 경로에서는 RigBuilder가 SetHumanPose 포즈를 초기화할 수 있어 기본 비활성화합니다.")]
        [FormerlySerializedAs("enableAnimationRiggingArmTwistCorrection")]
        [SerializeField] private bool _enableAnimationRiggingArmTwistCorrection= false;
        public bool enableAnimationRiggingArmTwistCorrection { get => _enableAnimationRiggingArmTwistCorrection; private set => _enableAnimationRiggingArmTwistCorrection = value; }

        [Tooltip("Animation Rigging 팔 twist 보정 전체 영향도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("AnimationRiggingArmTwistRigWeight")]
        [SerializeField] private float _AnimationRiggingArmTwistRigWeight= 0.65f;
        public float AnimationRiggingArmTwistRigWeight { get => _AnimationRiggingArmTwistRigWeight; private set => _AnimationRiggingArmTwistRigWeight = value; }

        [Tooltip("상완 twist 보조본 영향도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("AnimationRiggingUpperArmTwistWeight")]
        [SerializeField] private float _AnimationRiggingUpperArmTwistWeight= 0.45f;
        public float AnimationRiggingUpperArmTwistWeight { get => _AnimationRiggingUpperArmTwistWeight; private set => _AnimationRiggingUpperArmTwistWeight = value; }

        [Tooltip("전완/손목 twist 보조본 영향도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("AnimationRiggingForearmTwistWeight")]
        [SerializeField] private float _AnimationRiggingForearmTwistWeight= 0.85f;
        public float AnimationRiggingForearmTwistWeight { get => _AnimationRiggingForearmTwistWeight; private set => _AnimationRiggingForearmTwistWeight = value; }

        [Tooltip("Animation Rigging 팔 twist 보정 구성 로그를 출력합니다.")]
        [FormerlySerializedAs("logAnimationRiggingArmTwistCorrection")]
        [SerializeField] private bool _logAnimationRiggingArmTwistCorrection= false;
        public bool logAnimationRiggingArmTwistCorrection { get => _logAnimationRiggingArmTwistCorrection; private set => _logAnimationRiggingArmTwistCorrection = value; }

        [Header("Humanoid Arm Direction Retarget Correction")]
        [Tooltip("실험 옵션입니다. Ghost 팔 세그먼트 방향을 Target Character의 Humanoid 팔 방향에 제한적으로 다시 맞춥니다. 현재는 일부 정상 프레임을 망가뜨릴 수 있어 기본 비활성화합니다.")]
        [FormerlySerializedAs("enableYybArmDirectionRetargetCorrection")]
        [SerializeField] private bool _enableYybArmDirectionRetargetCorrection= false;
        public bool enableYybArmDirectionRetargetCorrection { get => _enableYybArmDirectionRetargetCorrection; set => _enableYybArmDirectionRetargetCorrection = value; }

        [Tooltip("상완 방향 보정 영향도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmDirectionUpperArmWeight")]
        [SerializeField] private float _YybArmDirectionUpperArmWeight= 0.65f;
        public float YybArmDirectionUpperArmWeight { get => _YybArmDirectionUpperArmWeight; set => _YybArmDirectionUpperArmWeight = value; }

        [Tooltip("전완 방향 보정 영향도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmDirectionForearmWeight")]
        [SerializeField] private float _YybArmDirectionForearmWeight= 0.75f;
        public float YybArmDirectionForearmWeight { get => _YybArmDirectionForearmWeight; set => _YybArmDirectionForearmWeight = value; }

        [Tooltip("상완이 한 프레임에 따라갈 수 있는 최대 각도입니다.")]
        [Range(0f, 120f)] [FormerlySerializedAs("YybArmDirectionUpperArmMaxDegrees")]
        [SerializeField] private float _YybArmDirectionUpperArmMaxDegrees= 65f;
        public float YybArmDirectionUpperArmMaxDegrees { get => _YybArmDirectionUpperArmMaxDegrees; set => _YybArmDirectionUpperArmMaxDegrees = value; }

        [Tooltip("전완이 한 프레임에 따라갈 수 있는 최대 각도입니다.")]
        [Range(0f, 120f)] [FormerlySerializedAs("YybArmDirectionForearmMaxDegrees")]
        [SerializeField] private float _YybArmDirectionForearmMaxDegrees= 85f;
        public float YybArmDirectionForearmMaxDegrees { get => _YybArmDirectionForearmMaxDegrees; set => _YybArmDirectionForearmMaxDegrees = value; }

        [Tooltip("Humanoid 팔 방향 보정의 왼쪽 팔 영향도 배율입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmDirectionLeftSideWeightScale")]
        [SerializeField] private float _YybArmDirectionLeftSideWeightScale= 1f;
        public float YybArmDirectionLeftSideWeightScale { get => _YybArmDirectionLeftSideWeightScale; set => _YybArmDirectionLeftSideWeightScale = value; }

        [Tooltip("Humanoid 팔 방향 보정의 오른쪽 팔 영향도 배율입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmDirectionRightSideWeightScale")]
        [SerializeField] private float _YybArmDirectionRightSideWeightScale= 1f;
        public float YybArmDirectionRightSideWeightScale { get => _YybArmDirectionRightSideWeightScale; set => _YybArmDirectionRightSideWeightScale = value; }

        [Tooltip("Humanoid 팔 방향 보정 구성 로그를 출력합니다.")]
        [FormerlySerializedAs("logYybArmDirectionRetargetCorrection")]
        [SerializeField] private bool _logYybArmDirectionRetargetCorrection= false;
        public bool logYybArmDirectionRetargetCorrection { get => _logYybArmDirectionRetargetCorrection; private set => _logYybArmDirectionRetargetCorrection = value; }

        [Header("YYB Arm Visual Twist Correction")]
        [Tooltip("RigBuilder 없이 YYB 팔/소매 보조본을 전완-손목 회전에 맞춰 직접 분배해 소매가 가늘어 보이는 현상을 줄입니다.")]
        [FormerlySerializedAs("enableYybArmVisualTwistCorrection")]
        [SerializeField] private bool _enableYybArmVisualTwistCorrection= true;
        public bool enableYybArmVisualTwistCorrection { get => _enableYybArmVisualTwistCorrection; set => _enableYybArmVisualTwistCorrection = value; }

        [Tooltip("상완 보조본 회전 분배 영향도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmVisualUpperArmInfluence")]
        [SerializeField] private float _YybArmVisualUpperArmInfluence= 0.35f;
        public float YybArmVisualUpperArmInfluence { get => _YybArmVisualUpperArmInfluence; set => _YybArmVisualUpperArmInfluence = value; }

        [Tooltip("전완/손목 보조본 회전 분배 영향도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmVisualForearmInfluence")]
        [SerializeField] private float _YybArmVisualForearmInfluence= 0.75f;
        public float YybArmVisualForearmInfluence { get => _YybArmVisualForearmInfluence; set => _YybArmVisualForearmInfluence = value; }

        [Tooltip("상완 보조본에 허용할 최대 회전각입니다.")]
        [Range(0f, 120f)] [FormerlySerializedAs("YybArmVisualUpperArmMaxDegrees")]
        [SerializeField] private float _YybArmVisualUpperArmMaxDegrees= 45f;
        public float YybArmVisualUpperArmMaxDegrees { get => _YybArmVisualUpperArmMaxDegrees; set => _YybArmVisualUpperArmMaxDegrees = value; }

        [Tooltip("전완/손목 보조본에 허용할 최대 회전각입니다.")]
        [Range(0f, 120f)] [FormerlySerializedAs("YybArmVisualForearmMaxDegrees")]
        [SerializeField] private float _YybArmVisualForearmMaxDegrees= 75f;
        public float YybArmVisualForearmMaxDegrees { get => _YybArmVisualForearmMaxDegrees; set => _YybArmVisualForearmMaxDegrees = value; }

        [Tooltip("YYB 팔 시각 보정 구성 로그를 출력합니다.")]
        [FormerlySerializedAs("logYybArmVisualTwistCorrection")]
        [SerializeField] private bool _logYybArmVisualTwistCorrection= false;
        public bool logYybArmVisualTwistCorrection { get => _logYybArmVisualTwistCorrection; private set => _logYybArmVisualTwistCorrection = value; }

        [Header("휴머노이드 팔 해부학적 스윙 보정")]
        [Tooltip("손이 몸 밖/어깨 근처에 있는데 상완만 아래로 크게 떨어지는 포즈를 제한합니다.")]
        [FormerlySerializedAs("enableYybArmSwingLimitCorrection")]
        [SerializeField] private bool _enableYybArmSwingLimitCorrection= false;
        public bool enableYybArmSwingLimitCorrection { get => _enableYybArmSwingLimitCorrection; set => _enableYybArmSwingLimitCorrection = value; }

        [Tooltip("상완 하강 제한 보정 강도입니다. Target Humanoid 포즈를 직접 바꾸므로 기본 자동 경로에서는 끄고, 진단/긴급 보정 때만 사용합니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmSwingLimitWeight")]
        [SerializeField] private float _YybArmSwingLimitWeight= 0.85f;
        public float YybArmSwingLimitWeight { get => _YybArmSwingLimitWeight; set => _YybArmSwingLimitWeight = value; }

        [Tooltip("상완 방향이 아래 방향과 이 값보다 더 가까우면 보정 후보로 봅니다. 0.68은 수동 기준 모션과의 차이를 줄이는 균형값입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmSwingMaxDownDot")]
        [SerializeField] private float _YybArmSwingMaxDownDot= 0.68f;
        public float YybArmSwingMaxDownDot { get => _YybArmSwingMaxDownDot; set => _YybArmSwingMaxDownDot = value; }

        [Tooltip("손이 어깨에서 팔 길이 대비 이 비율 이상 옆/앞으로 떨어져 있을 때만 보정합니다. 몸 가까이에서 소매가 무너지는 체형도 감지할 수 있도록 기본값을 낮게 둡니다.")]
        [Range(0f, 1.5f)] [FormerlySerializedAs("YybArmSwingMinHandHorizontalRatio")]
        [SerializeField] private float _YybArmSwingMinHandHorizontalRatio= 0.05f;
        public float YybArmSwingMinHandHorizontalRatio { get => _YybArmSwingMinHandHorizontalRatio; set => _YybArmSwingMinHandHorizontalRatio = value; }

        [Tooltip("손이 어깨보다 팔 길이 대비 이 비율보다 더 낮으면 자연스럽게 내려간 팔로 보고 보정하지 않습니다.")]
        [Range(0f, 1.5f)] [FormerlySerializedAs("YybArmSwingMaxHandBelowShoulderRatio")]
        [SerializeField] private float _YybArmSwingMaxHandBelowShoulderRatio= 0.75f;
        public float YybArmSwingMaxHandBelowShoulderRatio { get => _YybArmSwingMaxHandBelowShoulderRatio; set => _YybArmSwingMaxHandBelowShoulderRatio = value; }

        [Tooltip("손이 몸 밖으로 과하게 벌어진 경우 수평 도달 거리를 제한하는 보정 강도입니다. 0이면 비활성화합니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmSwingHorizontalReachLimitWeight")]
        [SerializeField] private float _YybArmSwingHorizontalReachLimitWeight= 0f;
        public float YybArmSwingHorizontalReachLimitWeight { get => _YybArmSwingHorizontalReachLimitWeight; set => _YybArmSwingHorizontalReachLimitWeight = value; }

        [Tooltip("팔 길이 대비 허용할 최대 손 수평 도달 거리 비율입니다. 0이면 수평 도달 거리 제한을 사용하지 않습니다.")]
        [Range(0f, 1.5f)] [FormerlySerializedAs("YybArmSwingMaxHandHorizontalReachRatio")]
        [SerializeField] private float _YybArmSwingMaxHandHorizontalReachRatio= 0f;
        public float YybArmSwingMaxHandHorizontalReachRatio { get => _YybArmSwingMaxHandHorizontalReachRatio; set => _YybArmSwingMaxHandHorizontalReachRatio = value; }

        [Tooltip("수평 도달 거리 제한에만 적용하는 손 하강 비율입니다. 0이면 기본 손 하강 비율 설정을 사용합니다.")]
        [Range(0f, 1.5f)] [FormerlySerializedAs("YybArmSwingHorizontalReachMaxHandBelowShoulderRatio")]
        [SerializeField] private float _YybArmSwingHorizontalReachMaxHandBelowShoulderRatio= 0f;
        public float YybArmSwingHorizontalReachMaxHandBelowShoulderRatio { get => _YybArmSwingHorizontalReachMaxHandBelowShoulderRatio; set => _YybArmSwingHorizontalReachMaxHandBelowShoulderRatio = value; }

        [Tooltip("수평 도달 거리 보정 뒤 팔꿈치 각도가 이 값보다 작으면 해당 보정을 되돌립니다. 0이면 비활성화합니다.")]
        [Range(0f, 180f)] [FormerlySerializedAs("YybArmSwingHorizontalReachMinElbowAngleAfterApply")]
        [SerializeField] private float _YybArmSwingHorizontalReachMinElbowAngleAfterApply= 0f;
        public float YybArmSwingHorizontalReachMinElbowAngleAfterApply { get => _YybArmSwingHorizontalReachMinElbowAngleAfterApply; set => _YybArmSwingHorizontalReachMinElbowAngleAfterApply = value; }

        [Tooltip("기존 상완 하강 제한이 동작하지 않는 들어 올린 팔 자세에서 수평 도달 거리를 제한하는 보정 강도입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmSwingRaisedPoseHorizontalReachLimitWeight")]
        [SerializeField] private float _YybArmSwingRaisedPoseHorizontalReachLimitWeight= 0f;
        public float YybArmSwingRaisedPoseHorizontalReachLimitWeight { get => _YybArmSwingRaisedPoseHorizontalReachLimitWeight; set => _YybArmSwingRaisedPoseHorizontalReachLimitWeight = value; }

        [Tooltip("들어 올린 팔 자세의 수평 도달 거리 제한을 적용할 최소 상완 하강 내적값입니다.")]
        [Range(0f, 1f)] [FormerlySerializedAs("YybArmSwingRaisedPoseMinUpperArmDownDot")]
        [SerializeField] private float _YybArmSwingRaisedPoseMinUpperArmDownDot= 0.55f;
        public float YybArmSwingRaisedPoseMinUpperArmDownDot { get => _YybArmSwingRaisedPoseMinUpperArmDownDot; set => _YybArmSwingRaisedPoseMinUpperArmDownDot = value; }

        [Tooltip("손이 어깨 아래로 이 비율보다 더 내려가 있으면 들어 올린 팔 자세의 수평 도달 거리 제한을 건너뜁니다.")]
        [Range(0f, 1.5f)] [FormerlySerializedAs("YybArmSwingRaisedPoseMaxHandBelowShoulderRatio")]
        [SerializeField] private float _YybArmSwingRaisedPoseMaxHandBelowShoulderRatio= 0.05f;
        public float YybArmSwingRaisedPoseMaxHandBelowShoulderRatio { get => _YybArmSwingRaisedPoseMaxHandBelowShoulderRatio; set => _YybArmSwingRaisedPoseMaxHandBelowShoulderRatio = value; }

        [Tooltip("들어 올린 팔 자세에서 허용할 최대 손 수평 도달 거리 비율입니다.")]
        [Range(0f, 1.5f)] [FormerlySerializedAs("YybArmSwingRaisedPoseMaxHandHorizontalReachRatio")]
        [SerializeField] private float _YybArmSwingRaisedPoseMaxHandHorizontalReachRatio= 0f;
        public float YybArmSwingRaisedPoseMaxHandHorizontalReachRatio { get => _YybArmSwingRaisedPoseMaxHandHorizontalReachRatio; set => _YybArmSwingRaisedPoseMaxHandHorizontalReachRatio = value; }

        #endregion
    }
}
