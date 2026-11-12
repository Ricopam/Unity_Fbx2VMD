using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Playables;
using UnityEngine.Animations;
using System;
using System.Collections.Generic;
using Fbx2Vmd.Retargeting;
using RootMotion;
using RootMotion.FinalIK;

namespace Fbx2Vmd.FBXImporter
{
    [DefaultExecutionOrder(CorrectionPassOrder.PoseSpaceRetarget)]
    public class PoseSpaceRetargeter : MonoBehaviour
    {
        private const float RecordingStartHipsBaselineFlipWarningThreshold = 0.02f;
        internal const float HipsLocalPositionTargetGapGuardMaxIncreaseMeters = 0.0005f;
        private const string RecordingStartHipsReferenceStagePrewarmComplete = "prewarm-complete";
        [Header("--- CORE COMPONENTS ---")]
        [FormerlySerializedAs("ghostAnimator")]
        [SerializeField] private Animator _ghostAnimator;  // (Container 내부의 모델)
        public Animator ghostAnimator { get => _ghostAnimator; private set => _ghostAnimator = value; }
        [FormerlySerializedAs("targetAnimator")]
        [SerializeField] private Animator _targetAnimator; // 내 캐릭터
        public Animator targetAnimator { get => _targetAnimator; set => _targetAnimator = value; }

        [Header("--- FINAL TUNING ---")]
        [Tooltip("캐릭터가 뒤를 보고 있다면 체크 (180도 회전)")]
        [FormerlySerializedAs("fixReverseRotation")]
        [SerializeField] private bool _fixReverseRotation= true;
        public bool fixReverseRotation { get => _fixReverseRotation; private set => _fixReverseRotation = value; }

        [Tooltip("Sub_Manual 직접 Animator 재생처럼 FBX HumanPose의 body/root 회전을 보존합니다.")]
        [FormerlySerializedAs("ShouldPreserveFbxRootRotation")]
        [SerializeField] private bool _ShouldPreserveFbxRootRotation= false;
        public bool ShouldPreserveFbxRootRotation { get => _ShouldPreserveFbxRootRotation; private set => _ShouldPreserveFbxRootRotation = value; }

        [Tooltip("Keep target HumanPose bodyPosition Y stable while preserving FBX X/Z body sway.")]
        [FormerlySerializedAs("preserveTargetBodyPosition")]
        [SerializeField] private bool _preserveTargetBodyPosition= true;
        public bool preserveTargetBodyPosition { get => _preserveTargetBodyPosition; private set => _preserveTargetBodyPosition = value; }

        [Tooltip("Use HumanPose bodyPosition X/Z delta as target root motion to reduce visible foot sliding.")]
        [FormerlySerializedAs("useBodyPositionXZRootMotion")]
        [SerializeField] private bool _useBodyPositionXZRootMotion= false;
        public bool useBodyPositionXZRootMotion { get => _useBodyPositionXZRootMotion; private set => _useBodyPositionXZRootMotion = value; }

        [Tooltip("Editor-only experimental RootT X/Z root motion reference. Keep disabled until visual_body_arc_jitter passes without increasing jitter.")]
        [FormerlySerializedAs("ShouldUseEditorHumanoidRootTranslationReference")]
        [SerializeField] private bool _ShouldUseEditorHumanoidRootTranslationReference= false;
        public bool ShouldUseEditorHumanoidRootTranslationReference { get => _ShouldUseEditorHumanoidRootTranslationReference; private set => _ShouldUseEditorHumanoidRootTranslationReference = value; }

        [Tooltip("Weight for Editor Humanoid RootT translation reference.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("editorHumanoidRootTranslationWeight")]
        [SerializeField] private float _editorHumanoidRootTranslationWeight= 0.25f;
        public float editorHumanoidRootTranslationWeight { get => _editorHumanoidRootTranslationWeight; private set => _editorHumanoidRootTranslationWeight = value; }

        [Tooltip("Current-frame blend for smoothed Editor Humanoid RootT translation delta.")]
        [Range(0.05f, 1f)]
        [FormerlySerializedAs("editorHumanoidRootTranslationCurrentWeight")]
        [SerializeField] private float _editorHumanoidRootTranslationCurrentWeight= 0.35f;
        public float editorHumanoidRootTranslationCurrentWeight { get => _editorHumanoidRootTranslationCurrentWeight; private set => _editorHumanoidRootTranslationCurrentWeight = value; }

        [Tooltip("When a foot is visually grounded, add a small X/Z root correction to reduce skating.")]
        [FormerlySerializedAs("ShouldStabilizeGroundedFootXZ")]
        [SerializeField] private bool _ShouldStabilizeGroundedFootXZ= false;
        public bool ShouldStabilizeGroundedFootXZ { get => _ShouldStabilizeGroundedFootXZ; private set => _ShouldStabilizeGroundedFootXZ = value; }

        [Tooltip("Foot-lock correction strength. Lower values preserve dance motion, higher values reduce skating.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("groundedFootLockWeight")]
        [SerializeField] private float _groundedFootLockWeight= 0.45f;
        public float groundedFootLockWeight { get => _groundedFootLockWeight; private set => _groundedFootLockWeight = value; }

        [Tooltip("Maximum X/Z root correction per frame for grounded foot lock.")]
        [Range(0.001f, 0.1f)]
        [FormerlySerializedAs("maxGroundedFootLockStep")]
        [SerializeField] private float _maxGroundedFootLockStep= 0.025f;
        public float maxGroundedFootLockStep { get => _maxGroundedFootLockStep; private set => _maxGroundedFootLockStep = value; }

        [Tooltip("체크 시 공중 부양/박힘을 모두 해결 (Raycast 사용)")]
        [FormerlySerializedAs("useSmartGrounding")]
        [SerializeField] private bool _useSmartGrounding= true;
        public bool useSmartGrounding { get => _useSmartGrounding; private set => _useSmartGrounding = value; }

        [Tooltip("발바닥 높이 미세 조절 (양수: 띄움, 음수: 박음)")]
        [Range(-0.1f, 0.1f)]
        [FormerlySerializedAs("groundOffset")]
        [SerializeField] private float _groundOffset= 0.0f;
        public float groundOffset { get => _groundOffset; private set => _groundOffset = value; }

        [Tooltip("FBX Avatar에서 비정상적으로 튀는 Humanoid muscle 값을 안전 범위로 제한합니다.")]
        [FormerlySerializedAs("clampMusclesToHumanRange")]
        [SerializeField] private bool _clampMusclesToHumanRange= false;
        public bool clampMusclesToHumanRange { get => _clampMusclesToHumanRange; private set => _clampMusclesToHumanRange = value; }

        [Header("--- ANATOMY GUARD ---")]
        [Tooltip("Target 팔이 늘어나거나 비정상적으로 비틀리는 Humanoid muscle 값을 제한합니다.")]
        [FormerlySerializedAs("enableAnatomicalArmGuard")]
        [SerializeField] private bool _enableAnatomicalArmGuard= true;
        public bool enableAnatomicalArmGuard { get => _enableAnatomicalArmGuard; set => _enableAnatomicalArmGuard = value; }

        [Tooltip("Humanoid 팔 Stretch muscle 허용치입니다. Forearm Stretch는 팔꿈치 굽힘에 가까우므로 기본적으로 제한하지 않습니다.")]
        [Range(0f, 0.5f)]
        [FormerlySerializedAs("armStretchMuscleLimit")]
        [SerializeField] private float _armStretchMuscleLimit= 0f;
        public float armStretchMuscleLimit { get => _armStretchMuscleLimit; set => _armStretchMuscleLimit = value; }

        [Tooltip("Forearm Stretch muscle 제한 여부입니다. Unity Humanoid에서는 팔꿈치 굽힘에 가까우므로 기본값은 꺼야 합니다.")]
        [FormerlySerializedAs("clampArmStretchMuscles")]
        [SerializeField] private bool _clampArmStretchMuscles= false;
        public bool clampArmStretchMuscles { get => _clampArmStretchMuscles; private set => _clampArmStretchMuscles = value; }

        [Tooltip("상완 Twist muscle 허용치입니다.")]
        [Range(0.1f, 1f)]
        [FormerlySerializedAs("upperArmTwistMuscleLimit")]
        [SerializeField] private float _upperArmTwistMuscleLimit= 0.75f;
        public float upperArmTwistMuscleLimit { get => _upperArmTwistMuscleLimit; private set => _upperArmTwistMuscleLimit = value; }

        [Tooltip("전완 Twist muscle 허용치입니다.")]
        [Range(0.1f, 1f)]
        [FormerlySerializedAs("lowerArmTwistMuscleLimit")]
        [SerializeField] private float _lowerArmTwistMuscleLimit= 0.65f;
        public float lowerArmTwistMuscleLimit { get => _lowerArmTwistMuscleLimit; private set => _lowerArmTwistMuscleLimit = value; }

        [Header("--- THUMB ANATOMY GUARD ---")]
        [Tooltip("수동 기준 손가락 pose를 유지하되, YYB 손 구조에서 엄지가 과하게 꺾이는 범위만 제한합니다.")]
        [FormerlySerializedAs("enableThumbAnatomicalGuard")]
        [SerializeField] private bool _enableThumbAnatomicalGuard= true;
        public bool enableThumbAnatomicalGuard { get => _enableThumbAnatomicalGuard; private set => _enableThumbAnatomicalGuard = value; }

        [Tooltip("엄지 굽힘 muscle 최소값입니다.")]
        [Range(-2.5f, 0f)]
        [FormerlySerializedAs("thumbStretchMin")]
        [SerializeField] private float _thumbStretchMin= -2.1f;
        public float thumbStretchMin { get => _thumbStretchMin; private set => _thumbStretchMin = value; }

        [Tooltip("엄지 굽힘 muscle 최대값입니다.")]
        [Range(0f, 2.5f)]
        [FormerlySerializedAs("thumbStretchMax")]
        [SerializeField] private float _thumbStretchMax= 1.0f;
        public float thumbStretchMax { get => _thumbStretchMax; private set => _thumbStretchMax = value; }

        [Tooltip("엄지 굽힘 muscle에 더하는 offset입니다. YYB 엄지 rest pose가 수동 기준보다 과하게 펴져 보일 때만 사용합니다.")]
        [Range(-0.5f, 0.5f)]
        [FormerlySerializedAs("thumbStretchOffset")]
        [SerializeField] private float _thumbStretchOffset= 0f;
        public float thumbStretchOffset { get => _thumbStretchOffset; private set => _thumbStretchOffset = value; }

        [Tooltip("Manual Animator finger reference를 사용할 때는 엄지 stretch offset을 추가하지 않고 수동 기준 엄지 muscle을 보존합니다.")]
        [FormerlySerializedAs("preserveManualFingerReferenceThumbMuscles")]
        [SerializeField] private bool _preserveManualFingerReferenceThumbMuscles= true;
        public bool preserveManualFingerReferenceThumbMuscles { get => _preserveManualFingerReferenceThumbMuscles; private set => _preserveManualFingerReferenceThumbMuscles = value; }

        [FormerlySerializedAs("ShouldUseManualAnimatorFullBodyPoseReference")]
        [SerializeField] private bool _ShouldUseManualAnimatorFullBodyPoseReference= false;
        public bool ShouldUseManualAnimatorFullBodyPoseReference { get => _ShouldUseManualAnimatorFullBodyPoseReference; set => _ShouldUseManualAnimatorFullBodyPoseReference = value; }

        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorFullBodyPoseReferenceWeight")]
        [SerializeField] private float _manualAnimatorFullBodyPoseReferenceWeight= 1f;
        public float manualAnimatorFullBodyPoseReferenceWeight { get => _manualAnimatorFullBodyPoseReferenceWeight; set => _manualAnimatorFullBodyPoseReferenceWeight = value; }

        [Tooltip("Runtime diagnostic: keep manual full-body reference active but skip lower-body muscles.")]
        [FormerlySerializedAs("ShouldExcludeManualAnimatorFullBodyLowerMuscles")]
        [SerializeField] private bool _ShouldExcludeManualAnimatorFullBodyLowerMuscles= false;
        public bool ShouldExcludeManualAnimatorFullBodyLowerMuscles { get => _ShouldExcludeManualAnimatorFullBodyLowerMuscles; set => _ShouldExcludeManualAnimatorFullBodyLowerMuscles = value; }

        [Tooltip("Runtime diagnostic: apply manual full-body reference only to lower-body muscles.")]
        [FormerlySerializedAs("ShouldApplyManualAnimatorFullBodyLowerMusclesOnly")]
        [SerializeField] private bool _ShouldApplyManualAnimatorFullBodyLowerMusclesOnly= false;
        public bool ShouldApplyManualAnimatorFullBodyLowerMusclesOnly { get => _ShouldApplyManualAnimatorFullBodyLowerMusclesOnly; set => _ShouldApplyManualAnimatorFullBodyLowerMusclesOnly = value; }

        [Tooltip("Runtime diagnostic: apply manual full-body reference only to leg in-out/twist muscles.")]
        [FormerlySerializedAs("ShouldApplyManualAnimatorFullBodyLegTwistMusclesOnly")]
        [SerializeField] private bool _ShouldApplyManualAnimatorFullBodyLegTwistMusclesOnly= false;
        public bool ShouldApplyManualAnimatorFullBodyLegTwistMusclesOnly { get => _ShouldApplyManualAnimatorFullBodyLegTwistMusclesOnly; set => _ShouldApplyManualAnimatorFullBodyLegTwistMusclesOnly = value; }

        [Tooltip("Runtime diagnostic: apply manual full-body reference only to right arm and shoulder muscles.")]
        [FormerlySerializedAs("manualAnimatorFullBodyPoseRightArmMusclesOnly")]
        [SerializeField] private bool _manualAnimatorFullBodyPoseRightArmMusclesOnly= false;
        public bool manualAnimatorFullBodyPoseRightArmMusclesOnly { get => _manualAnimatorFullBodyPoseRightArmMusclesOnly; set => _manualAnimatorFullBodyPoseRightArmMusclesOnly = value; }

        [Tooltip("Runtime diagnostic: apply manual full-body reference only to left arm and shoulder muscles.")]
        [FormerlySerializedAs("manualAnimatorFullBodyPoseLeftArmMusclesOnly")]
        [SerializeField] private bool _manualAnimatorFullBodyPoseLeftArmMusclesOnly= false;
        public bool manualAnimatorFullBodyPoseLeftArmMusclesOnly { get => _manualAnimatorFullBodyPoseLeftArmMusclesOnly; set => _manualAnimatorFullBodyPoseLeftArmMusclesOnly = value; }

        [Tooltip("Runtime diagnostic: apply manual full-body reference only to spine and right sleeve chain muscles.")]
        [FormerlySerializedAs("manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly")]
        [SerializeField] private bool _manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly= false;
        public bool manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly { get => _manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly; set => _manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly = value; }

        [Range(0f, 6000f)]
        [FormerlySerializedAs("manualAnimatorFullBodyPoseFrameGateStart")]
        [SerializeField] private float _manualAnimatorFullBodyPoseFrameGateStart= 0f;
        public float manualAnimatorFullBodyPoseFrameGateStart { get => _manualAnimatorFullBodyPoseFrameGateStart; set => _manualAnimatorFullBodyPoseFrameGateStart = value; }

        [Range(0f, 6000f)]
        [FormerlySerializedAs("manualAnimatorFullBodyPoseFrameGateEnd")]
        [SerializeField] private float _manualAnimatorFullBodyPoseFrameGateEnd= 0f;
        public float manualAnimatorFullBodyPoseFrameGateEnd { get => _manualAnimatorFullBodyPoseFrameGateEnd; set => _manualAnimatorFullBodyPoseFrameGateEnd = value; }

        [Tooltip("Runtime diagnostic: after SetHumanPose, blend only right upper/lower leg twist output muscles back toward the solver input within a small cap.")]
        [FormerlySerializedAs("ShouldUseSetHumanPoseRightLegTwistOutputReference")]
        [SerializeField] private bool _ShouldUseSetHumanPoseRightLegTwistOutputReference= false;
        public bool ShouldUseSetHumanPoseRightLegTwistOutputReference { get => _ShouldUseSetHumanPoseRightLegTwistOutputReference; set => _ShouldUseSetHumanPoseRightLegTwistOutputReference = value; }

        [Range(0f, 1f)]
        [FormerlySerializedAs("setHumanPoseRightLegTwistOutputReferenceWeight")]
        [SerializeField] private float _setHumanPoseRightLegTwistOutputReferenceWeight= 1f;
        public float setHumanPoseRightLegTwistOutputReferenceWeight { get => _setHumanPoseRightLegTwistOutputReferenceWeight; set => _setHumanPoseRightLegTwistOutputReferenceWeight = value; }

        [Range(0f, 0.1f)]
        [FormerlySerializedAs("setHumanPoseRightLegTwistOutputReferenceMaxDelta")]
        [SerializeField] private float _setHumanPoseRightLegTwistOutputReferenceMaxDelta= 0.02f;
        public float setHumanPoseRightLegTwistOutputReferenceMaxDelta { get => _setHumanPoseRightLegTwistOutputReferenceMaxDelta; set => _setHumanPoseRightLegTwistOutputReferenceMaxDelta = value; }

        [Tooltip("Manual Animator finger reference의 엄지 체인 localRotation도 Target에 적용해 모델별 엄지 축 차이를 줄입니다.")]
        [FormerlySerializedAs("useManualAnimatorThumbLocalRotationReference")]
        [SerializeField] private bool _useManualAnimatorThumbLocalRotationReference= true;
        public bool useManualAnimatorThumbLocalRotationReference { get => _useManualAnimatorThumbLocalRotationReference; set => _useManualAnimatorThumbLocalRotationReference = value; }

        [Tooltip("손목 localRotation을 Sub_Manual/testPrefab Animator가 같은 FBX clip에서 평가한 값을 기준으로 덮어씁니다. t13.2 hand pose parity 회귀 보호용입니다.")]
        [FormerlySerializedAs("useManualAnimatorHandLocalRotationReference")]
        [SerializeField] private bool _useManualAnimatorHandLocalRotationReference= true;
        public bool useManualAnimatorHandLocalRotationReference { get => _useManualAnimatorHandLocalRotationReference; set => _useManualAnimatorHandLocalRotationReference = value; }

        [Tooltip("Manual Animator finger reference의 엄지 세그먼트 방향을 Target 손 기준 방향에 맞춰 모델별 bind axis 차이를 줄입니다.")]
        [FormerlySerializedAs("useManualAnimatorThumbSegmentDirectionReference")]
        [SerializeField] private bool _useManualAnimatorThumbSegmentDirectionReference= true;
        public bool useManualAnimatorThumbSegmentDirectionReference { get => _useManualAnimatorThumbSegmentDirectionReference; private set => _useManualAnimatorThumbSegmentDirectionReference = value; }

        [Tooltip("엄지 세그먼트 방향 보정 강도입니다.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorThumbSegmentDirectionWeight")]
        [SerializeField] private float _manualAnimatorThumbSegmentDirectionWeight= 1f;
        public float manualAnimatorThumbSegmentDirectionWeight { get => _manualAnimatorThumbSegmentDirectionWeight; private set => _manualAnimatorThumbSegmentDirectionWeight = value; }

        [Tooltip("Manual Animator finger reference의 손바닥 기준 Hand->ThumbIntermediate 방향을 Target에 적용합니다.")]
        [FormerlySerializedAs("useManualAnimatorThumbHandDirectionReference")]
        [SerializeField] private bool _useManualAnimatorThumbHandDirectionReference= true;
        public bool useManualAnimatorThumbHandDirectionReference { get => _useManualAnimatorThumbHandDirectionReference; private set => _useManualAnimatorThumbHandDirectionReference = value; }

        [Tooltip("손바닥 기준 엄지 시작 방향 보정 강도입니다.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorThumbHandDirectionWeight")]
        [SerializeField] private float _manualAnimatorThumbHandDirectionWeight= 1f;
        public float manualAnimatorThumbHandDirectionWeight { get => _manualAnimatorThumbHandDirectionWeight; private set => _manualAnimatorThumbHandDirectionWeight = value; }

        [Tooltip("Manual Animator finger reference의 손바닥 전체 프레임을 Target 손에 적용합니다.")]
        [FormerlySerializedAs("useManualAnimatorHandPalmFrameReference")]
        [SerializeField] private bool _useManualAnimatorHandPalmFrameReference= true;
        public bool useManualAnimatorHandPalmFrameReference { get => _useManualAnimatorHandPalmFrameReference; set => _useManualAnimatorHandPalmFrameReference = value; }

        [Tooltip("손바닥 프레임 보정 강도입니다.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorHandPalmFrameWeight")]
        [SerializeField] private float _manualAnimatorHandPalmFrameWeight= 1f;
        public float manualAnimatorHandPalmFrameWeight { get => _manualAnimatorHandPalmFrameWeight; set => _manualAnimatorHandPalmFrameWeight = value; }

        [Tooltip("Manual Animator finger reference의 손 기준 엄지 시작 위치를 Target에 적용합니다.")]
        [FormerlySerializedAs("useManualAnimatorThumbBasePositionReference")]
        [SerializeField] private bool _useManualAnimatorThumbBasePositionReference= true;
        public bool useManualAnimatorThumbBasePositionReference { get => _useManualAnimatorThumbBasePositionReference; private set => _useManualAnimatorThumbBasePositionReference = value; }

        [Tooltip("수동 기준 Animator의 Hips localPosition을 target Hips에 선택적으로 적용합니다. testprefab Hips delta가 YYB에 전달되어 발 호 궤적이 심해지므로 기본 비활성화합니다.")]
        [FormerlySerializedAs("ShouldUseManualAnimatorHipsLocalPositionReference")]
        [SerializeField] private bool _ShouldUseManualAnimatorHipsLocalPositionReference= false;
        public bool ShouldUseManualAnimatorHipsLocalPositionReference { get => _ShouldUseManualAnimatorHipsLocalPositionReference; set => _ShouldUseManualAnimatorHipsLocalPositionReference = value; }

        [Tooltip("Sub_Manual/testPrefab Animator의 HumanPose bodyRotation을 retarget pose 기준으로 사용해 팔꿈치 bend plane 기준축 차이를 줄입니다.")]
        [FormerlySerializedAs("ShouldUseManualAnimatorBodyRotationReference")]
        [SerializeField] private bool _ShouldUseManualAnimatorBodyRotationReference= true;
        public bool ShouldUseManualAnimatorBodyRotationReference { get => _ShouldUseManualAnimatorBodyRotationReference; set => _ShouldUseManualAnimatorBodyRotationReference = value; }

        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorBodyRotationReferenceWeight")]
        [SerializeField] private float _manualAnimatorBodyRotationReferenceWeight= 1f;
        public float manualAnimatorBodyRotationReferenceWeight { get => _manualAnimatorBodyRotationReferenceWeight; set => _manualAnimatorBodyRotationReferenceWeight = value; }

        [Tooltip("preserveTargetBodyPosition=true 일 때 body Y 높이를 수동 기준 Animator의 HumanPose bodyPosition.y로 대체합니다. ghost Legacy-animation bodyPos 스파이크 없이 상체 높이를 애니메이션에 맞게 따라가도록 합니다.")]
        [FormerlySerializedAs("ShouldUseManualAnimatorBodyPositionYReference")]
        [SerializeField] private bool _ShouldUseManualAnimatorBodyPositionYReference= false;
        public bool ShouldUseManualAnimatorBodyPositionYReference { get => _ShouldUseManualAnimatorBodyPositionYReference; private set => _ShouldUseManualAnimatorBodyPositionYReference = value; }

        [Tooltip("Runtime diagnostic: blend HumanPose bodyPosition X/Z toward the manual Animator reference before SetHumanPose.")]
        [FormerlySerializedAs("ShouldUseManualAnimatorBodyPositionXzReference")]
        [SerializeField] private bool _ShouldUseManualAnimatorBodyPositionXzReference= false;
        public bool ShouldUseManualAnimatorBodyPositionXzReference { get => _ShouldUseManualAnimatorBodyPositionXzReference; set => _ShouldUseManualAnimatorBodyPositionXzReference = value; }

        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorBodyPositionXzReferenceWeight")]
        [SerializeField] private float _manualAnimatorBodyPositionXzReferenceWeight= 1f;
        public float manualAnimatorBodyPositionXzReferenceWeight { get => _manualAnimatorBodyPositionXzReferenceWeight; set => _manualAnimatorBodyPositionXzReferenceWeight = value; }

        [Range(0f, 0.2f)] [FormerlySerializedAs("manualAnimatorBodyPositionXzReferenceMaxOffset")]
        [SerializeField] private float _manualAnimatorBodyPositionXzReferenceMaxOffset= 0.025f;
        public float manualAnimatorBodyPositionXzReferenceMaxOffset { get => _manualAnimatorBodyPositionXzReferenceMaxOffset; set => _manualAnimatorBodyPositionXzReferenceMaxOffset = value; }

        [Range(0f, 6000f)] [FormerlySerializedAs("manualAnimatorBodyPositionXzReferenceFrameGateStart")]
        [SerializeField] private float _manualAnimatorBodyPositionXzReferenceFrameGateStart= 0f;
        public float manualAnimatorBodyPositionXzReferenceFrameGateStart { get => _manualAnimatorBodyPositionXzReferenceFrameGateStart; set => _manualAnimatorBodyPositionXzReferenceFrameGateStart = value; }

        [Range(0f, 6000f)] [FormerlySerializedAs("manualAnimatorBodyPositionXzReferenceFrameGateEnd")]
        [SerializeField] private float _manualAnimatorBodyPositionXzReferenceFrameGateEnd= 0f;
        public float manualAnimatorBodyPositionXzReferenceFrameGateEnd { get => _manualAnimatorBodyPositionXzReferenceFrameGateEnd; set => _manualAnimatorBodyPositionXzReferenceFrameGateEnd = value; }

        [Tooltip("Runtime diagnostic blend width in recorder frames for manual Animator bodyPosition X/Z frame gates. Zero keeps the legacy hard gate.")]
        [Range(0f, 600f)] [FormerlySerializedAs("manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames")]
        [SerializeField] private float _manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames= 0f;
        public float manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames { get => _manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames; set => _manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames = value; }

        [Tooltip("Runtime diagnostic scale for the manual Animator bodyPosition X solver-input basis. One keeps the legacy X contribution.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorBodyPositionXzReferenceAxisXScale")]
        [SerializeField] private float _manualAnimatorBodyPositionXzReferenceAxisXScale= 1f;
        public float manualAnimatorBodyPositionXzReferenceAxisXScale { get => _manualAnimatorBodyPositionXzReferenceAxisXScale; set => _manualAnimatorBodyPositionXzReferenceAxisXScale = value; }

        [Tooltip("Runtime diagnostic scale for the manual Animator bodyPosition Z solver-input basis. One keeps the legacy Z contribution.")]
        [Range(0f, 1f)] [FormerlySerializedAs("manualAnimatorBodyPositionXzReferenceAxisZScale")]
        [SerializeField] private float _manualAnimatorBodyPositionXzReferenceAxisZScale= 1f;
        public float manualAnimatorBodyPositionXzReferenceAxisZScale { get => _manualAnimatorBodyPositionXzReferenceAxisZScale; set => _manualAnimatorBodyPositionXzReferenceAxisZScale = value; }

        [Tooltip("Runtime diagnostic: apply a frame-local local-X offset to the right sleeve helper silhouette after SetHumanPose.")]
        [FormerlySerializedAs("useYybRightSleeveSilhouetteLocalOffsetReference")]
        [SerializeField] private bool _useYybRightSleeveSilhouetteLocalOffsetReference= false;
        public bool useYybRightSleeveSilhouetteLocalOffsetReference { get => _useYybRightSleeveSilhouetteLocalOffsetReference; set => _useYybRightSleeveSilhouetteLocalOffsetReference = value; }

        [Tooltip("Local X offset in meters for the frame-local right sleeve silhouette probe.")]
        [Range(-0.2f, 0.2f)] [FormerlySerializedAs("yybRightSleeveSilhouetteLocalOffsetX")]
        [SerializeField] private float _yybRightSleeveSilhouetteLocalOffsetX= 0f;
        public float yybRightSleeveSilhouetteLocalOffsetX { get => _yybRightSleeveSilhouetteLocalOffsetX; set => _yybRightSleeveSilhouetteLocalOffsetX = value; }

        [Tooltip("Runtime diagnostic start recorder frame for the right sleeve silhouette local-X offset. Zero with end zero keeps the full clip.")]
        [Range(0f, 6000f)] [FormerlySerializedAs("yybRightSleeveSilhouetteLocalOffsetFrameGateStart")]
        [SerializeField] private float _yybRightSleeveSilhouetteLocalOffsetFrameGateStart= 0f;
        public float yybRightSleeveSilhouetteLocalOffsetFrameGateStart { get => _yybRightSleeveSilhouetteLocalOffsetFrameGateStart; set => _yybRightSleeveSilhouetteLocalOffsetFrameGateStart = value; }

        [Tooltip("Runtime diagnostic end recorder frame for the right sleeve silhouette local-X offset. Zero with start zero keeps the full clip.")]
        [Range(0f, 6000f)] [FormerlySerializedAs("yybRightSleeveSilhouetteLocalOffsetFrameGateEnd")]
        [SerializeField] private float _yybRightSleeveSilhouetteLocalOffsetFrameGateEnd= 0f;
        public float yybRightSleeveSilhouetteLocalOffsetFrameGateEnd { get => _yybRightSleeveSilhouetteLocalOffsetFrameGateEnd; set => _yybRightSleeveSilhouetteLocalOffsetFrameGateEnd = value; }

        [Tooltip("수동 기준 Hips localPosition 보정 강도입니다.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorHipsLocalPositionWeight")]
        [SerializeField] private float _manualAnimatorHipsLocalPositionWeight= 1f;
        public float manualAnimatorHipsLocalPositionWeight { get => _manualAnimatorHipsLocalPositionWeight; set => _manualAnimatorHipsLocalPositionWeight = value; }

        [Tooltip("프레임당 수동 기준 Hips localPosition으로 이동할 수 있는 최대 보정 거리입니다.")]
        [Range(0.001f, 0.5f)]
        [FormerlySerializedAs("manualAnimatorHipsLocalPositionMaxOffset")]
        [SerializeField] private float _manualAnimatorHipsLocalPositionMaxOffset= 0.12f;
        public float manualAnimatorHipsLocalPositionMaxOffset { get => _manualAnimatorHipsLocalPositionMaxOffset; set => _manualAnimatorHipsLocalPositionMaxOffset = value; }

        [Tooltip("Use the manual Animator lowest-foot lift as the grounding target height so jump/foot-height arcs are not flattened to the floor.")]
        [FormerlySerializedAs("ShouldUseManualAnimatorFootHeightGroundingReference")]
        [SerializeField] private bool _ShouldUseManualAnimatorFootHeightGroundingReference= false;
        public bool ShouldUseManualAnimatorFootHeightGroundingReference { get => _ShouldUseManualAnimatorFootHeightGroundingReference; set => _ShouldUseManualAnimatorFootHeightGroundingReference = value; }

        [Tooltip("Blend weight for the manual Animator lowest-foot grounding height reference.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorFootHeightGroundingReferenceWeight")]
        [SerializeField] private float _manualAnimatorFootHeightGroundingReferenceWeight= 1f;
        public float manualAnimatorFootHeightGroundingReferenceWeight { get => _manualAnimatorFootHeightGroundingReferenceWeight; private set => _manualAnimatorFootHeightGroundingReferenceWeight = value; }

        [Tooltip("Maximum positive grounding target lift from the manual Animator lowest-foot reference.")]
        [Range(0f, 0.12f)]
        [FormerlySerializedAs("manualAnimatorFootHeightGroundingReferenceMaxLift")]
        [SerializeField] private float _manualAnimatorFootHeightGroundingReferenceMaxLift= 0.08f;
        public float manualAnimatorFootHeightGroundingReferenceMaxLift { get => _manualAnimatorFootHeightGroundingReferenceMaxLift; private set => _manualAnimatorFootHeightGroundingReferenceMaxLift = value; }

        [Tooltip("Apply the manual Animator lower-body leg-chain localRotation to the target as an isolated runtime candidate.")]
        [FormerlySerializedAs("ShouldUseManualAnimatorFootLocalRotationReference")]
        [SerializeField] private bool _ShouldUseManualAnimatorFootLocalRotationReference= false;
        public bool ShouldUseManualAnimatorFootLocalRotationReference { get => _ShouldUseManualAnimatorFootLocalRotationReference; set => _ShouldUseManualAnimatorFootLocalRotationReference = value; }

        [Tooltip("Blend weight for the manual Animator lower-body leg-chain localRotation reference.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorFootLocalRotationReferenceWeight")]
        [SerializeField] private float _manualAnimatorFootLocalRotationReferenceWeight= 1f;
        public float manualAnimatorFootLocalRotationReferenceWeight { get => _manualAnimatorFootLocalRotationReferenceWeight; set => _manualAnimatorFootLocalRotationReferenceWeight = value; }

        [Tooltip("Apply manual Animator lower-body segment directions as an isolated runtime candidate without changing bone lengths or scale.")]
        [FormerlySerializedAs("ShouldUseManualAnimatorLowerBodySegmentDirectionReference")]
        [SerializeField] private bool _ShouldUseManualAnimatorLowerBodySegmentDirectionReference= false;
        public bool ShouldUseManualAnimatorLowerBodySegmentDirectionReference { get => _ShouldUseManualAnimatorLowerBodySegmentDirectionReference; set => _ShouldUseManualAnimatorLowerBodySegmentDirectionReference = value; }

        [Tooltip("Blend weight for the manual Animator lower-body segment direction correction.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorLowerBodySegmentDirectionReferenceWeight")]
        [SerializeField] private float _manualAnimatorLowerBodySegmentDirectionReferenceWeight= 1f;
        public float manualAnimatorLowerBodySegmentDirectionReferenceWeight { get => _manualAnimatorLowerBodySegmentDirectionReferenceWeight; set => _manualAnimatorLowerBodySegmentDirectionReferenceWeight = value; }

        [Tooltip("Maximum per-frame lower-body segment direction correction angle in degrees.")]
        [Range(0f, 20f)]
        [FormerlySerializedAs("manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle")]
        [SerializeField] private float _manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle= 6.2f;
        public float manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle { get => _manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle; set => _manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle = value; }

        [Tooltip("Skip only the upper-leg-to-lower-leg segments from the manual Animator lower-body segment direction correction.")]
        [FormerlySerializedAs("ShouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference")]
        [SerializeField] private bool _ShouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference= false;
        public bool ShouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference { get => _ShouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference; set => _ShouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference = value; }

        [Tooltip("Optional upper-leg-to-lower-leg segment direction max angle in degrees. Zero keeps the shared lower-body segment cap.")]
        [Range(0f, 20f)]
        [FormerlySerializedAs("manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle")]
        [SerializeField] private float _manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle= 0f;
        public float manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle { get => _manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle; set => _manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle = value; }

        [Tooltip("Skip only the lower-leg-to-foot segments from the manual Animator lower-body segment direction correction.")]
        [FormerlySerializedAs("ShouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference")]
        [SerializeField] private bool _ShouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference= false;
        public bool ShouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference { get => _ShouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference; set => _ShouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference = value; }

        [Tooltip("Optional lower-leg-to-foot segment direction max angle in degrees. Zero keeps the shared lower-body segment cap.")]
        [Range(0f, 20f)]
        [FormerlySerializedAs("manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle")]
        [SerializeField] private float _manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle= 0f;
        public float manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle { get => _manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle; set => _manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle = value; }

        [Tooltip("Optional left lower-leg-to-foot segment direction max angle in degrees. Zero keeps the lower-leg-to-foot segment cap.")]
        [Range(0f, 20f)]
        [FormerlySerializedAs("manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle")]
        [SerializeField] private float _manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle= 0f;
        public float manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle { get => _manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle; set => _manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle = value; }

        [Tooltip("Optional right lower-leg-to-foot segment direction max angle in degrees. Zero keeps the lower-leg-to-foot segment cap.")]
        [Range(0f, 20f)]
        [FormerlySerializedAs("manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle")]
        [SerializeField] private float _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle= 0f;
        public float manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle { get => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle; set => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle = value; }

        [Tooltip("Runtime diagnostic scale for right lower-leg-to-foot correction axis X/Z components. One keeps the original axis.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale")]
        [SerializeField] private float _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale= 1f;
        public float manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale { get => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale; set => _manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale = value; }

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

        [Header("--- ROOT MOTION SPIKE GUARD ---")]
        [Tooltip("Ghost root delta가 한 프레임에 과도하게 튀면 순간이동으로 보고 해당 프레임의 추가 root 이동을 무시합니다.")]
        [FormerlySerializedAs("clampRootDeltaSpikes")]
        [SerializeField] private bool _clampRootDeltaSpikes= true;
        public bool clampRootDeltaSpikes { get => _clampRootDeltaSpikes; private set => _clampRootDeltaSpikes = value; }

        [Tooltip("한 프레임에 허용할 최대 root 이동량입니다.")]
        [Range(0.001f, 1.0f)]
        [FormerlySerializedAs("maxRootDeltaPerFrame")]
        [SerializeField] private float _maxRootDeltaPerFrame= 0.25f;
        public float maxRootDeltaPerFrame { get => _maxRootDeltaPerFrame; private set => _maxRootDeltaPerFrame = value; }

        [Tooltip("root delta spike를 무시했을 때 최초 1회 진단 로그를 출력합니다.")]
        [FormerlySerializedAs("logRootDeltaSpikes")]
        [SerializeField] private bool _logRootDeltaSpikes= false;
        public bool logRootDeltaSpikes { get => _logRootDeltaSpikes; private set => _logRootDeltaSpikes = value; }

        [Header("--- HIPS LOCAL POSITION SPIKE GUARD ---")]
        [Tooltip("Clamp one-frame target Hips localPosition outliers after SetHumanPose.")]
        [FormerlySerializedAs("clampTargetHipsLocalPositionSpikes")]
        [SerializeField] private bool _clampTargetHipsLocalPositionSpikes= false;
        public bool clampTargetHipsLocalPositionSpikes { get => _clampTargetHipsLocalPositionSpikes; private set => _clampTargetHipsLocalPositionSpikes = value; }

        [Tooltip("Maximum target Hips localPosition delta allowed per frame.")]
        [Range(0.005f, 0.25f)]
        [FormerlySerializedAs("maxTargetHipsLocalPositionDeltaPerFrame")]
        [SerializeField] private float _maxTargetHipsLocalPositionDeltaPerFrame= 0.02f;
        public float maxTargetHipsLocalPositionDeltaPerFrame { get => _maxTargetHipsLocalPositionDeltaPerFrame; private set => _maxTargetHipsLocalPositionDeltaPerFrame = value; }

        [Header("--- GROUNDING STABILITY GUARD ---")]
        [Tooltip("발바닥 접지 보정이 한 프레임에 크게 튀지 않도록 부드럽게 반영합니다.")]
        [FormerlySerializedAs("smoothGrounding")]
        [SerializeField] private bool _smoothGrounding= true;
        public bool smoothGrounding { get => _smoothGrounding; private set => _smoothGrounding = value; }

        [Tooltip("한 프레임에 허용할 최대 수직 접지 보정값입니다.")]
        [Range(0.001f, 0.2f)]
        [FormerlySerializedAs("maxGroundingVerticalStepPerFrame")]
        [SerializeField] private float _maxGroundingVerticalStepPerFrame= 0.01f;
        public float maxGroundingVerticalStepPerFrame { get => _maxGroundingVerticalStepPerFrame; private set => _maxGroundingVerticalStepPerFrame = value; }

        [Tooltip("접지 보정 목표값을 현재 위치에 반영하는 비율입니다.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("groundingSmoothing")]
        [SerializeField] private float _groundingSmoothing= 0.25f;
        public float groundingSmoothing { get => _groundingSmoothing; private set => _groundingSmoothing = value; }

        [Tooltip("이 값보다 작은 발바닥 떨림은 무시합니다.")]
        [Range(0f, 0.05f)]
        [FormerlySerializedAs("groundingDeadZone")]
        [SerializeField] private float _groundingDeadZone= 0.005f;
        public float groundingDeadZone { get => _groundingDeadZone; private set => _groundingDeadZone = value; }

        [Tooltip("초기 접지 확정 뒤에는 root Y를 고정합니다. MMD VMD export에서는 이후 프레임의 발 빠짐을 막기 위해 기본 비활성화합니다.")]
        [FormerlySerializedAs("freezeRootYAfterInitialGrounding")]
        [SerializeField] private bool _freezeRootYAfterInitialGrounding= false;
        public bool freezeRootYAfterInitialGrounding { get => _freezeRootYAfterInitialGrounding; private set => _freezeRootYAfterInitialGrounding = value; }

        [Tooltip("Editor/GameView 프레임이 밀려도 Ghost clip time이 한 프레임에 크게 건너뛰지 않게 제한합니다.")]
        [FormerlySerializedAs("clampLegacyAnimationVisualStep")]
        [SerializeField] private bool _clampLegacyAnimationVisualStep= false;
        public bool clampLegacyAnimationVisualStep { get => _clampLegacyAnimationVisualStep; private set => _clampLegacyAnimationVisualStep = value; }

        [Tooltip("Ghost clip time이 한 렌더 프레임에 전진할 수 있는 기준 FPS입니다.")]
        [Range(15f, 120f)]
        [FormerlySerializedAs("legacyAnimationVisualFrameRate")]
        [SerializeField] private float _legacyAnimationVisualFrameRate= 30f;
        public float legacyAnimationVisualFrameRate { get => _legacyAnimationVisualFrameRate; private set => _legacyAnimationVisualFrameRate = value; }

        [Tooltip("프레임 지연으로 pose가 한 번에 크게 바뀌면 clip time은 보존하고 target pose만 부드럽게 따라가게 합니다.")]
        [FormerlySerializedAs("smoothPoseOnLegacyAnimationStepSpike")]
        [SerializeField] private bool _smoothPoseOnLegacyAnimationStepSpike= true;
        public bool smoothPoseOnLegacyAnimationStepSpike { get => _smoothPoseOnLegacyAnimationStepSpike; private set => _smoothPoseOnLegacyAnimationStepSpike = value; }

        [Tooltip("pose spike smoothing 때 현재 FBX pose를 반영하는 비율입니다.")]
        [Range(0.1f, 1f)]
        [FormerlySerializedAs("poseVisualSpikeCurrentWeight")]
        [SerializeField] private float _poseVisualSpikeCurrentWeight= 0.65f;
        public float poseVisualSpikeCurrentWeight { get => _poseVisualSpikeCurrentWeight; private set => _poseVisualSpikeCurrentWeight = value; }

        [Tooltip("Optional forearm stretch clamp around the current pose during visual spike smoothing. 0 disables the clamp.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("poseVisualSpikeForearmStretchClampMaxOffset")]
        [SerializeField] private float _poseVisualSpikeForearmStretchClampMaxOffset= 0f;
        public float poseVisualSpikeForearmStretchClampMaxOffset { get => _poseVisualSpikeForearmStretchClampMaxOffset; private set => _poseVisualSpikeForearmStretchClampMaxOffset = value; }

        [Tooltip("이 값보다 큰 muscle delta가 발생하면 frame-time spike가 아니어도 pose smoothing을 적용합니다.")]
        [Range(0.05f, 1f)]
        [FormerlySerializedAs("poseVisualMuscleDeltaThreshold")]
        [SerializeField] private float _poseVisualMuscleDeltaThreshold= 0.35f;
        public float poseVisualMuscleDeltaThreshold { get => _poseVisualMuscleDeltaThreshold; private set => _poseVisualMuscleDeltaThreshold = value; }

        [Tooltip("Renderer bounds 하단이 발바닥 추정치에서 과하게 멀면 접지 기준에서 제외합니다.")]
        [FormerlySerializedAs("rejectRendererGroundingOutliers")]
        [SerializeField] private bool _rejectRendererGroundingOutliers= true;
        public bool rejectRendererGroundingOutliers { get => _rejectRendererGroundingOutliers; private set => _rejectRendererGroundingOutliers = value; }

        [Tooltip("Renderer bounds 하단과 발바닥 추정치 사이에 허용할 최대 거리입니다.")]
        [Range(0.02f, 0.3f)]
        [FormerlySerializedAs("maxRendererFootGroundingSeparation")]
        [SerializeField] private float _maxRendererFootGroundingSeparation= 0.12f;
        public float maxRendererFootGroundingSeparation { get => _maxRendererFootGroundingSeparation; private set => _maxRendererFootGroundingSeparation = value; }

        [Tooltip("LateUpdate 후반의 손/팔 보호 로직이 끝난 뒤 메시 bounds 기준으로 루트 Y만 한 번 더 보정합니다.")]
        [FormerlySerializedAs("enableLateVisualGroundingCorrection")]
        [SerializeField] private bool _enableLateVisualGroundingCorrection= true;
        public bool enableLateVisualGroundingCorrection { get => _enableLateVisualGroundingCorrection; private set => _enableLateVisualGroundingCorrection = value; }

        [Tooltip("최종 메시 bounds 보정이 한 프레임에 적용할 수 있는 최대 Y 이동량입니다.")]
        [Range(0.01f, 0.2f)]
        [FormerlySerializedAs("maxLateVisualGroundingCorrection")]
        [SerializeField] private float _maxLateVisualGroundingCorrection= 0.2f;
        public float maxLateVisualGroundingCorrection { get => _maxLateVisualGroundingCorrection; private set => _maxLateVisualGroundingCorrection = value; }

        [Tooltip("최종 메시 bounds 접지 보정의 작은 잔여 오차를 부드럽게 반영해 모델 전체 떨림을 줄입니다.")]
        [FormerlySerializedAs("smoothLateVisualGroundingCorrection")]
        [SerializeField] private bool _smoothLateVisualGroundingCorrection= true;
        public bool smoothLateVisualGroundingCorrection { get => _smoothLateVisualGroundingCorrection; private set => _smoothLateVisualGroundingCorrection = value; }

        [Tooltip("Late visual grounding 잔여 오차가 이 값보다 작으면 smoothing 대상으로 봅니다. 큰 오차는 공중 부유 방지를 위해 즉시 보정합니다.")]
        [Range(0.005f, 0.1f)]
        [FormerlySerializedAs("lateVisualGroundingSnapThreshold")]
        [SerializeField] private float _lateVisualGroundingSnapThreshold= 0.03f;
        public float lateVisualGroundingSnapThreshold { get => _lateVisualGroundingSnapThreshold; private set => _lateVisualGroundingSnapThreshold = value; }

        [Tooltip("작은 late visual grounding 잔여 오차를 현재 위치에 반영하는 비율입니다.")]
        [Range(0f, 1f)]
        [FormerlySerializedAs("lateVisualGroundingSmoothing")]
        [SerializeField] private float _lateVisualGroundingSmoothing= 0.25f;
        public float lateVisualGroundingSmoothing { get => _lateVisualGroundingSmoothing; private set => _lateVisualGroundingSmoothing = value; }

        [Tooltip("작은 late visual grounding smoothing 보정이 한 프레임에 움직일 수 있는 최대 Y 이동량입니다.")]
        [Range(0.001f, 0.05f)]
        [FormerlySerializedAs("maxLateVisualGroundingStepPerFrame")]
        [SerializeField] private float _maxLateVisualGroundingStepPerFrame= 0.003f;
        public float maxLateVisualGroundingStepPerFrame { get => _maxLateVisualGroundingStepPerFrame; private set => _maxLateVisualGroundingStepPerFrame = value; }

        public float LastRootDeltaMagnitude => _lastRootDeltaMagnitude;
        public float MaxRootDeltaMagnitude => _maxRootDeltaMagnitude;
        public int RootDeltaSpikeSkippedCount => _rootDeltaSpikeSkippedCount;
        public float LastRootPositionPoseDeltaMagnitude => _lastRootPositionPoseDeltaMagnitude;
        public float MaxRootPositionPoseDeltaMagnitude => _maxRootPositionPoseDeltaMagnitude;
        public int RootPositionSpikeClampedCount => _rootPositionSpikeClampedCount;
        public float LastTargetHipsLocalPositionDelta => _lastTargetHipsLocalPositionDelta;
        public float MaxTargetHipsLocalPositionDelta => _maxTargetHipsLocalPositionDelta;
        public int TargetHipsLocalPositionSpikeClampedCount => _targetHipsLocalPositionSpikeClampedCount;
        public float LastGroundingAdjustment => _grounding._lastGroundingAdjustment;
        public float MaxGroundingAdjustment => _grounding._maxGroundingAdjustment;
        public int GroundingStepClampedCount => _grounding._groundingStepClampedCount;
        public int GroundingSmoothedCount => _grounding._groundingSmoothedCount;
        public float LastGroundingVerticalStep => _grounding._lastGroundingVerticalStep;
        public float MaxGroundingVerticalStep => _grounding._maxGroundingVerticalStep;
        public float InitialGroundingVerticalStep => _grounding._initialGroundingVerticalStep;
        public float MaxGroundingVerticalStepAfterInitial => _grounding._maxGroundingVerticalStepAfterInitial;
        public float LastGroundingTargetY => _grounding._lastGroundingTargetY;
        public float LastGroundingLowestFootBottomY => _grounding._lastGroundingLowestFootBottomY;
        public float LastEditorFootHeightGroundingReferenceLift => _lastEditorFootHeightGroundingReferenceLift;
        public float RecordingStartRootY => _recordingStartRootY;
        public float RecordingStartBodyPositionY => _recordingStartBodyPositionY;
        public float RecordingStartHipsLocalY => _recordingStartHipsLocalY;
        public float RecordingStartHipsY => _recordingStartHipsY;
        public float RecordingStartHipsReferenceBeforeLocalY => _recordingStartHipsReferenceBeforeLocalY;
        public float RecordingStartHipsReferenceAfterLocalY => _recordingStartHipsReferenceAfterLocalY;
        public float RecordingStartHipsReferenceDeltaY => _recordingStartHipsReferenceDeltaY;
        public int RecordingStartHipsReferenceFlipDetected => _recordingStartHipsReferenceFlipDetected ? 1 : 0;
        public string RecordingStartHipsReferenceStage => _recordingStartHipsReferenceStage;
        public float LastLegacyAnimationStep => _legacyAnimationDriver.LastStep;
        public float MaxLegacyAnimationStep => _legacyAnimationDriver.MaxStep;
        public int LegacyAnimationStepSpikeCount => _legacyAnimationDriver.StepSpikeCount;
        public float CurrentLegacyAnimationTime => _legacyAnimationDriver.CurrentTime;
        public float CurrentLegacyAnimationClipLength => _legacyAnimationDriver.CurrentClipLength;
        public string CurrentLegacyAnimationClipName => _legacyAnimationDriver.CurrentClipName;
        public bool IsCompleteEditorHumanoidPoseReferenceActive
        {
            get
            {
                return _useCompleteEditorHumanoidMuscleReference &&
                    _hasEditorHumanoidPoseReferenceForFrame &&
                    _editorHumanoidPoseReferencePlayer != null &&
                    _editorHumanoidPoseReferencePlayer.IsInitialized;
            }
        }
        public int PoseVisualSmoothingCount => _poseVisualSmoothingCount;
        public int PoseVisualMuscleDeltaOnlySkippedCount => _poseVisualMuscleDeltaOnlySkippedCount;
        public float LastPoseVisualMaxMuscleDelta => _lastPoseVisualMaxMuscleDelta;
        public float MaxPoseVisualMaxMuscleDelta => _maxPoseVisualMaxMuscleDelta;
        public int LastLeftThumbLocalRotationGuardClampCount => _thumbGuardDiagnostics.LastLeftThumbLocalRotationGuardClampCount;
        public int LastRightThumbLocalRotationGuardClampCount => _thumbGuardDiagnostics.LastRightThumbLocalRotationGuardClampCount;
        public int LastLeftThumbLocalRotationGuardPreserveCount => _thumbGuardDiagnostics.LastLeftThumbLocalRotationGuardPreserveCount;
        public int LastRightThumbLocalRotationGuardPreserveCount => _thumbGuardDiagnostics.LastRightThumbLocalRotationGuardPreserveCount;
        public float LastLeftThumbLocalRotationGuardCurrentRisk => _thumbGuardDiagnostics.LastLeftThumbLocalRotationGuardCurrentRisk;
        public float LastRightThumbLocalRotationGuardCurrentRisk => _thumbGuardDiagnostics.LastRightThumbLocalRotationGuardCurrentRisk;
        public float LastLeftThumbLocalRotationGuardLimitedRisk => _thumbGuardDiagnostics.LastLeftThumbLocalRotationGuardLimitedRisk;
        public float LastRightThumbLocalRotationGuardLimitedRisk => _thumbGuardDiagnostics.LastRightThumbLocalRotationGuardLimitedRisk;
        public bool LastLeftThumbWorldRotationSuppressCompetingOverride => _thumbGuardDiagnostics.LastLeftThumbWorldRotationSuppressCompetingOverride;
        public bool LastRightThumbWorldRotationSuppressCompetingOverride => _thumbGuardDiagnostics.LastRightThumbWorldRotationSuppressCompetingOverride;
        public bool LastLeftThumbWorldRotationKeepDetachedHelperOverride => _thumbGuardDiagnostics.LastLeftThumbWorldRotationKeepDetachedHelperOverride;
        public bool LastRightThumbWorldRotationKeepDetachedHelperOverride => _thumbGuardDiagnostics.LastRightThumbWorldRotationKeepDetachedHelperOverride;
        public float LastLeftThumbWorldRotationCurrentReferenceFrameDeviation => _thumbGuardDiagnostics.LastLeftThumbWorldRotationCurrentReferenceFrameDeviation;
        public float LastRightThumbWorldRotationCurrentReferenceFrameDeviation => _thumbGuardDiagnostics.LastRightThumbWorldRotationCurrentReferenceFrameDeviation;
        public float LastLeftThumbWorldRotationCandidateReferenceFrameDeviation => _thumbGuardDiagnostics.LastLeftThumbWorldRotationCandidateReferenceFrameDeviation;
        public float LastRightThumbWorldRotationCandidateReferenceFrameDeviation => _thumbGuardDiagnostics.LastRightThumbWorldRotationCandidateReferenceFrameDeviation;
        public int LastLeftThumbProximalWorldRotationPreserveReason => (int)_thumbGuardDiagnostics.LastLeftThumbProximalWorldRotationPreserveReason;
        public int LastRightThumbProximalWorldRotationPreserveReason => (int)_thumbGuardDiagnostics.LastRightThumbProximalWorldRotationPreserveReason;
        public int LastLeftThumbIntermediateWorldRotationPreserveReason => (int)_thumbGuardDiagnostics.LastLeftThumbIntermediateWorldRotationPreserveReason;
        public int LastRightThumbIntermediateWorldRotationPreserveReason => (int)_thumbGuardDiagnostics.LastRightThumbIntermediateWorldRotationPreserveReason;
        public float LastLeftThumbProximalWorldRotationCurrentReferenceAngle => _thumbGuardDiagnostics.LastLeftThumbProximalWorldRotationCurrentReferenceAngle;
        public float LastRightThumbProximalWorldRotationCurrentReferenceAngle => _thumbGuardDiagnostics.LastRightThumbProximalWorldRotationCurrentReferenceAngle;
        public float LastLeftThumbIntermediateWorldRotationCurrentReferenceAngle => _thumbGuardDiagnostics.LastLeftThumbIntermediateWorldRotationCurrentReferenceAngle;
        public float LastRightThumbIntermediateWorldRotationCurrentReferenceAngle => _thumbGuardDiagnostics.LastRightThumbIntermediateWorldRotationCurrentReferenceAngle;
        public float LastLeftThumbProximalWorldRotationCandidateReferenceAngle => _thumbGuardDiagnostics.LastLeftThumbProximalWorldRotationCandidateReferenceAngle;
        public float LastRightThumbProximalWorldRotationCandidateReferenceAngle => _thumbGuardDiagnostics.LastRightThumbProximalWorldRotationCandidateReferenceAngle;
        public float LastLeftThumbIntermediateWorldRotationCandidateReferenceAngle => _thumbGuardDiagnostics.LastLeftThumbIntermediateWorldRotationCandidateReferenceAngle;
        public float LastRightThumbIntermediateWorldRotationCandidateReferenceAngle => _thumbGuardDiagnostics.LastRightThumbIntermediateWorldRotationCandidateReferenceAngle;
        public float LastLeftThumbProximalWorldRotationPreserveCurrentRisk => _thumbGuardDiagnostics.LastLeftThumbProximalWorldRotationPreserveCurrentRisk;
        public float LastRightThumbProximalWorldRotationPreserveCurrentRisk => _thumbGuardDiagnostics.LastRightThumbProximalWorldRotationPreserveCurrentRisk;
        public float LastLeftThumbIntermediateWorldRotationPreserveCurrentRisk => _thumbGuardDiagnostics.LastLeftThumbIntermediateWorldRotationPreserveCurrentRisk;
        public float LastRightThumbIntermediateWorldRotationPreserveCurrentRisk => _thumbGuardDiagnostics.LastRightThumbIntermediateWorldRotationPreserveCurrentRisk;
        public float LastLeftThumbProximalWorldRotationPreserveLimitedRisk => _thumbGuardDiagnostics.LastLeftThumbProximalWorldRotationPreserveLimitedRisk;
        public float LastRightThumbProximalWorldRotationPreserveLimitedRisk => _thumbGuardDiagnostics.LastRightThumbProximalWorldRotationPreserveLimitedRisk;
        public float LastLeftThumbIntermediateWorldRotationPreserveLimitedRisk => _thumbGuardDiagnostics.LastLeftThumbIntermediateWorldRotationPreserveLimitedRisk;
        public float LastRightThumbIntermediateWorldRotationPreserveLimitedRisk => _thumbGuardDiagnostics.LastRightThumbIntermediateWorldRotationPreserveLimitedRisk;
        public float LastPoseInputLeftShoulderFrontBackMuscle => _stageMuscleDiagnostics.PoseInput.LeftShoulderFrontBack;
        public float LastAfterEditorMuscleReferenceLeftShoulderFrontBackMuscle => _stageMuscleDiagnostics.AfterEditorMuscleReference.LeftShoulderFrontBack;
        public float LastAfterClampPoseMusclesLeftShoulderFrontBackMuscle => _stageMuscleDiagnostics.AfterClampPoseMuscles.LeftShoulderFrontBack;
        public float LastAfterAnatomicalArmGuardLeftShoulderFrontBackMuscle => _stageMuscleDiagnostics.AfterAnatomicalArmGuard.LeftShoulderFrontBack;
        public float LastAfterVisualSpikeSmoothingLeftShoulderFrontBackMuscle => _stageMuscleDiagnostics.AfterVisualSpikeSmoothing.LeftShoulderFrontBack;
        public float LastSetHumanPoseInputLeftShoulderFrontBackMuscle => _setHumanPoseDiagnostics.Input.LeftShoulderFrontBack;
        public float LastSetHumanPoseOutputLeftShoulderFrontBackMuscle => _setHumanPoseDiagnostics.Output.LeftShoulderFrontBack;
        public float LastSetHumanPoseLeftShoulderFrontBackDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.LeftShoulderFrontBack,
            _setHumanPoseDiagnostics.Output.LeftShoulderFrontBack);
        public float LastPoseInputLeftArmTwistMuscle => _stageMuscleDiagnostics.PoseInput.LeftArmTwist;
        public float LastAfterEditorMuscleReferenceLeftArmTwistMuscle => _stageMuscleDiagnostics.AfterEditorMuscleReference.LeftArmTwist;
        public float LastAfterClampPoseMusclesLeftArmTwistMuscle => _stageMuscleDiagnostics.AfterClampPoseMuscles.LeftArmTwist;
        public float LastAfterAnatomicalArmGuardLeftArmTwistMuscle => _stageMuscleDiagnostics.AfterAnatomicalArmGuard.LeftArmTwist;
        public float LastAfterVisualSpikeSmoothingLeftArmTwistMuscle => _stageMuscleDiagnostics.AfterVisualSpikeSmoothing.LeftArmTwist;
        public float LastSetHumanPoseInputLeftArmTwistMuscle => _setHumanPoseDiagnostics.Input.LeftArmTwist;
        public float LastSetHumanPoseOutputLeftArmTwistMuscle => _setHumanPoseDiagnostics.Output.LeftArmTwist;
        public float LastSetHumanPoseLeftArmTwistDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.LeftArmTwist,
            _setHumanPoseDiagnostics.Output.LeftArmTwist);
        public float LastPoseInputLeftForearmStretchMuscle => _stageMuscleDiagnostics.PoseInput.LeftForearmStretch;
        public float LastAfterEditorMuscleReferenceLeftForearmStretchMuscle => _stageMuscleDiagnostics.AfterEditorMuscleReference.LeftForearmStretch;
        public float LastAfterClampPoseMusclesLeftForearmStretchMuscle => _stageMuscleDiagnostics.AfterClampPoseMuscles.LeftForearmStretch;
        public float LastAfterAnatomicalArmGuardLeftForearmStretchMuscle => _stageMuscleDiagnostics.AfterAnatomicalArmGuard.LeftForearmStretch;
        public float LastAfterVisualSpikeSmoothingLeftForearmStretchMuscle => _stageMuscleDiagnostics.AfterVisualSpikeSmoothing.LeftForearmStretch;
        public float LastSetHumanPoseInputLeftForearmStretchMuscle => _setHumanPoseDiagnostics.Input.LeftForearmStretch;
        public float LastSetHumanPoseOutputLeftForearmStretchMuscle => _setHumanPoseDiagnostics.Output.LeftForearmStretch;
        public float LastSetHumanPoseLeftForearmStretchDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.LeftForearmStretch,
            _setHumanPoseDiagnostics.Output.LeftForearmStretch);
        public float LastPoseInputRightForearmStretchMuscle => _stageMuscleDiagnostics.PoseInput.RightForearmStretch;
        public float LastAfterEditorMuscleReferenceRightForearmStretchMuscle => _stageMuscleDiagnostics.AfterEditorMuscleReference.RightForearmStretch;
        public float LastAfterClampPoseMusclesRightForearmStretchMuscle => _stageMuscleDiagnostics.AfterClampPoseMuscles.RightForearmStretch;
        public float LastAfterAnatomicalArmGuardRightForearmStretchMuscle => _stageMuscleDiagnostics.AfterAnatomicalArmGuard.RightForearmStretch;
        public float LastAfterVisualSpikeSmoothingRightForearmStretchMuscle => _stageMuscleDiagnostics.AfterVisualSpikeSmoothing.RightForearmStretch;
        public float LastSetHumanPoseInputRightForearmStretchMuscle => _setHumanPoseDiagnostics.Input.RightForearmStretch;
        public float LastSetHumanPoseOutputRightForearmStretchMuscle => _setHumanPoseDiagnostics.Output.RightForearmStretch;
        public float LastSetHumanPoseRightForearmStretchDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.RightForearmStretch,
            _setHumanPoseDiagnostics.Output.RightForearmStretch);
        public float LastPoseInputRightArmTwistMuscle => _stageMuscleDiagnostics.PoseInput.RightArmTwist;
        public float LastAfterEditorMuscleReferenceRightArmTwistMuscle => _stageMuscleDiagnostics.AfterEditorMuscleReference.RightArmTwist;
        public float LastAfterClampPoseMusclesRightArmTwistMuscle => _stageMuscleDiagnostics.AfterClampPoseMuscles.RightArmTwist;
        public float LastAfterAnatomicalArmGuardRightArmTwistMuscle => _stageMuscleDiagnostics.AfterAnatomicalArmGuard.RightArmTwist;
        public float LastAfterVisualSpikeSmoothingRightArmTwistMuscle => _stageMuscleDiagnostics.AfterVisualSpikeSmoothing.RightArmTwist;
        public float LastSetHumanPoseInputRightArmTwistMuscle => _setHumanPoseDiagnostics.Input.RightArmTwist;
        public float LastSetHumanPoseOutputRightArmTwistMuscle => _setHumanPoseDiagnostics.Output.RightArmTwist;
        public float LastSetHumanPoseRightArmTwistDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.RightArmTwist,
            _setHumanPoseDiagnostics.Output.RightArmTwist);
        public float LastSetHumanPoseInputLeftUpperLegFrontBackMuscle => _setHumanPoseDiagnostics.Input.LeftUpperLegFrontBack;
        public float LastSetHumanPoseOutputLeftUpperLegFrontBackMuscle => _setHumanPoseDiagnostics.Output.LeftUpperLegFrontBack;
        public float LastSetHumanPoseLeftUpperLegFrontBackDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.LeftUpperLegFrontBack,
            _setHumanPoseDiagnostics.Output.LeftUpperLegFrontBack);
        public float LastSetHumanPoseInputRightUpperLegFrontBackMuscle => _setHumanPoseDiagnostics.Input.RightUpperLegFrontBack;
        public float LastSetHumanPoseOutputRightUpperLegFrontBackMuscle => _setHumanPoseDiagnostics.Output.RightUpperLegFrontBack;
        public float LastSetHumanPoseRightUpperLegFrontBackDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.RightUpperLegFrontBack,
            _setHumanPoseDiagnostics.Output.RightUpperLegFrontBack);
        public float LastSetHumanPoseInputLeftLowerLegStretchMuscle => _setHumanPoseDiagnostics.Input.LeftLowerLegStretch;
        public float LastSetHumanPoseOutputLeftLowerLegStretchMuscle => _setHumanPoseDiagnostics.Output.LeftLowerLegStretch;
        public float LastSetHumanPoseLeftLowerLegStretchDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.LeftLowerLegStretch,
            _setHumanPoseDiagnostics.Output.LeftLowerLegStretch);
        public float LastSetHumanPoseInputRightLowerLegStretchMuscle => _setHumanPoseDiagnostics.Input.RightLowerLegStretch;
        public float LastSetHumanPoseOutputRightLowerLegStretchMuscle => _setHumanPoseDiagnostics.Output.RightLowerLegStretch;
        public float LastSetHumanPoseRightLowerLegStretchDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.RightLowerLegStretch,
            _setHumanPoseDiagnostics.Output.RightLowerLegStretch);
        public float LastSetHumanPoseInputLeftFootUpDownMuscle => _setHumanPoseDiagnostics.Input.LeftFootUpDown;
        public float LastSetHumanPoseOutputLeftFootUpDownMuscle => _setHumanPoseDiagnostics.Output.LeftFootUpDown;
        public float LastSetHumanPoseLeftFootUpDownDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.LeftFootUpDown,
            _setHumanPoseDiagnostics.Output.LeftFootUpDown);
        public float LastSetHumanPoseInputRightFootUpDownMuscle => _setHumanPoseDiagnostics.Input.RightFootUpDown;
        public float LastSetHumanPoseOutputRightFootUpDownMuscle => _setHumanPoseDiagnostics.Output.RightFootUpDown;
        public float LastSetHumanPoseRightFootUpDownDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.RightFootUpDown,
            _setHumanPoseDiagnostics.Output.RightFootUpDown);
        public float LastSetHumanPoseInputBodyPositionX => _setHumanPoseDiagnostics.Input.BodyPosition.x;
        public float LastSetHumanPoseInputBodyPositionY => _setHumanPoseDiagnostics.Input.BodyPosition.y;
        public float LastSetHumanPoseInputBodyPositionZ => _setHumanPoseDiagnostics.Input.BodyPosition.z;
        public float LastSetHumanPoseOutputBodyPositionX => _setHumanPoseDiagnostics.Output.BodyPosition.x;
        public float LastSetHumanPoseOutputBodyPositionY => _setHumanPoseDiagnostics.Output.BodyPosition.y;
        public float LastSetHumanPoseOutputBodyPositionZ => _setHumanPoseDiagnostics.Output.BodyPosition.z;
        public float LastSetHumanPoseBodyPositionDeltaXZ => CalculateFiniteXzDelta(
            _setHumanPoseDiagnostics.Input.BodyPosition,
            _setHumanPoseDiagnostics.Output.BodyPosition);
        public float LastSetHumanPoseInputBodyRotationYaw => ReadBodyRotationYaw(_setHumanPoseDiagnostics.Input.BodyRotation);
        public float LastSetHumanPoseOutputBodyRotationYaw => ReadBodyRotationYaw(_setHumanPoseDiagnostics.Output.BodyRotation);
        public float LastSetHumanPoseBodyRotationDeltaAngle => CalculateFiniteAngleDelta(
            _setHumanPoseDiagnostics.Input.BodyRotation,
            _setHumanPoseDiagnostics.Output.BodyRotation);
        public float LastSetHumanPosePreSolveGhostRootWorldX => _setHumanPoseDiagnostics.PreSolveBasis.GhostRootWorldPosition.x;
        public float LastSetHumanPosePreSolveGhostRootWorldY => _setHumanPoseDiagnostics.PreSolveBasis.GhostRootWorldPosition.y;
        public float LastSetHumanPosePreSolveGhostRootWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.GhostRootWorldPosition.z;
        public float LastSetHumanPosePreSolveGhostRootYaw => ReadBodyRotationYaw(_setHumanPoseDiagnostics.PreSolveBasis.GhostRootWorldRotation);
        public float LastSetHumanPosePreSolveTargetRootWorldX => _setHumanPoseDiagnostics.PreSolveBasis.TargetRootWorldPosition.x;
        public float LastSetHumanPosePreSolveTargetRootWorldY => _setHumanPoseDiagnostics.PreSolveBasis.TargetRootWorldPosition.y;
        public float LastSetHumanPosePreSolveTargetRootWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.TargetRootWorldPosition.z;
        public float LastSetHumanPosePreSolveTargetRootYaw => ReadBodyRotationYaw(_setHumanPoseDiagnostics.PreSolveBasis.TargetRootWorldRotation);
        public float LastSetHumanPosePreSolveTargetHipsWorldX => _setHumanPoseDiagnostics.PreSolveBasis.TargetHipsWorldPosition.x;
        public float LastSetHumanPosePreSolveTargetHipsWorldY => _setHumanPoseDiagnostics.PreSolveBasis.TargetHipsWorldPosition.y;
        public float LastSetHumanPosePreSolveTargetHipsWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.TargetHipsWorldPosition.z;
        public float LastSetHumanPosePreSolveTargetHipsLocalX => _setHumanPoseDiagnostics.PreSolveBasis.TargetHipsLocalPosition.x;
        public float LastSetHumanPosePreSolveTargetHipsLocalY => _setHumanPoseDiagnostics.PreSolveBasis.TargetHipsLocalPosition.y;
        public float LastSetHumanPosePreSolveTargetHipsLocalZ => _setHumanPoseDiagnostics.PreSolveBasis.TargetHipsLocalPosition.z;
        public float LastSetHumanPosePreSolveBodyPositionX => _setHumanPoseDiagnostics.PreSolveBasis.BodyPosition.x;
        public float LastSetHumanPosePreSolveBodyPositionY => _setHumanPoseDiagnostics.PreSolveBasis.BodyPosition.y;
        public float LastSetHumanPosePreSolveBodyPositionZ => _setHumanPoseDiagnostics.PreSolveBasis.BodyPosition.z;
        public float LastSetHumanPosePreSolveBodyRotationYaw => ReadBodyRotationYaw(_setHumanPoseDiagnostics.PreSolveBasis.BodyRotation);
        public float LastPreSetHumanPoseEndpointBodyPositionBeforeX => _footReferenceDiagnostics.PreSetEndpointBodyPositionBefore.x;
        public float LastPreSetHumanPoseEndpointBodyPositionBeforeZ => _footReferenceDiagnostics.PreSetEndpointBodyPositionBefore.z;
        public float LastPreSetHumanPoseEndpointBodyPositionAfterX => _footReferenceDiagnostics.PreSetEndpointBodyPositionAfter.x;
        public float LastPreSetHumanPoseEndpointBodyPositionAfterZ => _footReferenceDiagnostics.PreSetEndpointBodyPositionAfter.z;
        public float LastPreSetHumanPoseEndpointBodyPositionDeltaX => _footReferenceDiagnostics.PreSetEndpointBodyPositionDelta.x;
        public float LastPreSetHumanPoseEndpointBodyPositionDeltaZ => _footReferenceDiagnostics.PreSetEndpointBodyPositionDelta.z;
        public float LastPreSetHumanPoseEndpointBodyPositionDeltaMagnitudeXZ => CalculateFiniteXzDelta(
            Vector3.zero,
            _footReferenceDiagnostics.PreSetEndpointBodyPositionDelta);
        public float LastSetHumanPosePreSolveGhostLeftFootWorldX => _setHumanPoseDiagnostics.PreSolveBasis.GhostEndpointPositions.LeftFoot.x;
        public float LastSetHumanPosePreSolveGhostLeftFootWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.GhostEndpointPositions.LeftFoot.z;
        public float LastSetHumanPosePreSolveGhostLeftToesWorldX => _setHumanPoseDiagnostics.PreSolveBasis.GhostEndpointPositions.LeftToes.x;
        public float LastSetHumanPosePreSolveGhostLeftToesWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.GhostEndpointPositions.LeftToes.z;
        public float LastSetHumanPosePreSolveCurrentLeftFootWorldX => _setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions.LeftFoot.x;
        public float LastSetHumanPosePreSolveCurrentLeftFootWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions.LeftFoot.z;
        public float LastSetHumanPosePreSolveCurrentLeftToesWorldX => _setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions.LeftToes.x;
        public float LastSetHumanPosePreSolveCurrentLeftToesWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions.LeftToes.z;
        public float LastSetHumanPosePreSolveTargetLeftFootWorldX => _setHumanPoseDiagnostics.PreSolveBasis.TargetEndpointPositions.LeftFoot.x;
        public float LastSetHumanPosePreSolveTargetLeftFootWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.TargetEndpointPositions.LeftFoot.z;
        public float LastSetHumanPosePreSolveTargetLeftToesWorldX => _setHumanPoseDiagnostics.PreSolveBasis.TargetEndpointPositions.LeftToes.x;
        public float LastSetHumanPosePreSolveTargetLeftToesWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.TargetEndpointPositions.LeftToes.z;
        public float LastSetHumanPosePreSolveGhostRightFootWorldX => _setHumanPoseDiagnostics.PreSolveBasis.GhostEndpointPositions.RightFoot.x;
        public float LastSetHumanPosePreSolveGhostRightFootWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.GhostEndpointPositions.RightFoot.z;
        public float LastSetHumanPosePreSolveGhostRightToesWorldX => _setHumanPoseDiagnostics.PreSolveBasis.GhostEndpointPositions.RightToes.x;
        public float LastSetHumanPosePreSolveGhostRightToesWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.GhostEndpointPositions.RightToes.z;
        public float LastSetHumanPosePreSolveCurrentRightFootWorldX => _setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions.RightFoot.x;
        public float LastSetHumanPosePreSolveCurrentRightFootWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions.RightFoot.z;
        public float LastSetHumanPosePreSolveCurrentRightToesWorldX => _setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions.RightToes.x;
        public float LastSetHumanPosePreSolveCurrentRightToesWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions.RightToes.z;
        public float LastSetHumanPosePreSolveTargetRightFootWorldX => _setHumanPoseDiagnostics.PreSolveBasis.TargetEndpointPositions.RightFoot.x;
        public float LastSetHumanPosePreSolveTargetRightFootWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.TargetEndpointPositions.RightFoot.z;
        public float LastSetHumanPosePreSolveTargetRightToesWorldX => _setHumanPoseDiagnostics.PreSolveBasis.TargetEndpointPositions.RightToes.x;
        public float LastSetHumanPosePreSolveTargetRightToesWorldZ => _setHumanPoseDiagnostics.PreSolveBasis.TargetEndpointPositions.RightToes.z;
        public float LastSetHumanPoseInputSpineFrontBackMuscle => _setHumanPoseDiagnostics.Input.SpineFrontBack;
        public float LastSetHumanPoseInputSpineLeftRightMuscle => _setHumanPoseDiagnostics.Input.SpineLeftRight;
        public float LastSetHumanPoseInputSpineTwistLeftRightMuscle => _setHumanPoseDiagnostics.Input.SpineTwistLeftRight;
        public float LastSetHumanPoseInputChestFrontBackMuscle => _setHumanPoseDiagnostics.Input.ChestFrontBack;
        public float LastSetHumanPoseInputChestLeftRightMuscle => _setHumanPoseDiagnostics.Input.ChestLeftRight;
        public float LastSetHumanPoseInputChestTwistLeftRightMuscle => _setHumanPoseDiagnostics.Input.ChestTwistLeftRight;
        public float LastSetHumanPoseInputUpperChestFrontBackMuscle => _setHumanPoseDiagnostics.Input.UpperChestFrontBack;
        public float LastSetHumanPoseInputUpperChestLeftRightMuscle => _setHumanPoseDiagnostics.Input.UpperChestLeftRight;
        public float LastSetHumanPoseInputUpperChestTwistLeftRightMuscle => _setHumanPoseDiagnostics.Input.UpperChestTwistLeftRight;
        public float LastSetHumanPoseInputLeftUpperLegInOutMuscle => _setHumanPoseDiagnostics.Input.LeftUpperLegInOut;
        public float LastSetHumanPoseInputRightUpperLegInOutMuscle => _setHumanPoseDiagnostics.Input.RightUpperLegInOut;
        public float LastSetHumanPoseInputLeftUpperLegTwistInOutMuscle => _setHumanPoseDiagnostics.Input.LeftUpperLegTwistInOut;
        public float LastSetHumanPoseInputRightUpperLegTwistInOutMuscle => _setHumanPoseDiagnostics.Input.RightUpperLegTwistInOut;
        public float LastSetHumanPoseInputLeftLowerLegTwistInOutMuscle => _setHumanPoseDiagnostics.Input.LeftLowerLegTwistInOut;
        public float LastSetHumanPoseInputRightLowerLegTwistInOutMuscle => _setHumanPoseDiagnostics.Input.RightLowerLegTwistInOut;
        public float LastSetHumanPoseInputLeftFootTwistInOutMuscle => _setHumanPoseDiagnostics.Input.LeftFootTwistInOut;
        public float LastSetHumanPoseInputRightFootTwistInOutMuscle => _setHumanPoseDiagnostics.Input.RightFootTwistInOut;
        public float LastSetHumanPoseInputLeftToesUpDownMuscle => _setHumanPoseDiagnostics.Input.LeftToesUpDown;
        public float LastSetHumanPoseInputRightToesUpDownMuscle => _setHumanPoseDiagnostics.Input.RightToesUpDown;
        public float LastSetHumanPoseOutputRightUpperLegInOutMuscle => _setHumanPoseDiagnostics.Output.RightUpperLegInOut;
        public float LastSetHumanPoseRightUpperLegInOutDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.RightUpperLegInOut,
            _setHumanPoseDiagnostics.Output.RightUpperLegInOut);
        public float LastSetHumanPoseOutputRightUpperLegTwistInOutMuscle => _setHumanPoseDiagnostics.Output.RightUpperLegTwistInOut;
        public float LastSetHumanPoseRightUpperLegTwistInOutDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.RightUpperLegTwistInOut,
            _setHumanPoseDiagnostics.Output.RightUpperLegTwistInOut);
        public float LastSetHumanPoseOutputRightLowerLegTwistInOutMuscle => _setHumanPoseDiagnostics.Output.RightLowerLegTwistInOut;
        public float LastSetHumanPoseRightLowerLegTwistInOutDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.RightLowerLegTwistInOut,
            _setHumanPoseDiagnostics.Output.RightLowerLegTwistInOut);
        public float LastSetHumanPoseOutputRightFootTwistInOutMuscle => _setHumanPoseDiagnostics.Output.RightFootTwistInOut;
        public float LastSetHumanPoseRightFootTwistInOutDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.RightFootTwistInOut,
            _setHumanPoseDiagnostics.Output.RightFootTwistInOut);
        public float LastSetHumanPoseOutputRightToesUpDownMuscle => _setHumanPoseDiagnostics.Output.RightToesUpDown;
        public float LastSetHumanPoseRightToesUpDownDelta => CalculateFiniteAbsDelta(
            _setHumanPoseDiagnostics.Input.RightToesUpDown,
            _setHumanPoseDiagnostics.Output.RightToesUpDown);
        public float LastRetargetStageGhostLeftFootWorldX => _endpointStageDiagnostics.GhostPositions.LeftFoot.x;
        public float LastRetargetStageGhostLeftFootWorldZ => _endpointStageDiagnostics.GhostPositions.LeftFoot.z;
        public float LastRetargetStageGhostLeftToesWorldX => _endpointStageDiagnostics.GhostPositions.LeftToes.x;
        public float LastRetargetStageGhostLeftToesWorldZ => _endpointStageDiagnostics.GhostPositions.LeftToes.z;
        public float LastRetargetStageGhostRightFootWorldX => _endpointStageDiagnostics.GhostPositions.RightFoot.x;
        public float LastRetargetStageGhostRightFootWorldZ => _endpointStageDiagnostics.GhostPositions.RightFoot.z;
        public float LastRetargetStageGhostRightToesWorldX => _endpointStageDiagnostics.GhostPositions.RightToes.x;
        public float LastRetargetStageGhostRightToesWorldZ => _endpointStageDiagnostics.GhostPositions.RightToes.z;
        public float LastRetargetStageAfterSetHumanPoseLeftFootWorldX => _endpointStageDiagnostics.AfterSetHumanPosePositions.LeftFoot.x;
        public float LastRetargetStageAfterSetHumanPoseLeftFootWorldZ => _endpointStageDiagnostics.AfterSetHumanPosePositions.LeftFoot.z;
        public float LastRetargetStageAfterSetHumanPoseLeftToesWorldX => _endpointStageDiagnostics.AfterSetHumanPosePositions.LeftToes.x;
        public float LastRetargetStageAfterSetHumanPoseLeftToesWorldZ => _endpointStageDiagnostics.AfterSetHumanPosePositions.LeftToes.z;
        public float LastRetargetStageAfterSetHumanPoseRightFootWorldX => _endpointStageDiagnostics.AfterSetHumanPosePositions.RightFoot.x;
        public float LastRetargetStageAfterSetHumanPoseRightFootWorldZ => _endpointStageDiagnostics.AfterSetHumanPosePositions.RightFoot.z;
        public float LastRetargetStageAfterSetHumanPoseRightToesWorldX => _endpointStageDiagnostics.AfterSetHumanPosePositions.RightToes.x;
        public float LastRetargetStageAfterSetHumanPoseRightToesWorldZ => _endpointStageDiagnostics.AfterSetHumanPosePositions.RightToes.z;
        public float LastRetargetStageAfterManualReferencesLeftFootWorldX => _endpointStageDiagnostics.AfterManualReferencesPositions.LeftFoot.x;
        public float LastRetargetStageAfterManualReferencesLeftFootWorldZ => _endpointStageDiagnostics.AfterManualReferencesPositions.LeftFoot.z;
        public float LastRetargetStageAfterManualReferencesLeftToesWorldX => _endpointStageDiagnostics.AfterManualReferencesPositions.LeftToes.x;
        public float LastRetargetStageAfterManualReferencesLeftToesWorldZ => _endpointStageDiagnostics.AfterManualReferencesPositions.LeftToes.z;
        public float LastRetargetStageAfterManualReferencesRightFootWorldX => _endpointStageDiagnostics.AfterManualReferencesPositions.RightFoot.x;
        public float LastRetargetStageAfterManualReferencesRightFootWorldZ => _endpointStageDiagnostics.AfterManualReferencesPositions.RightFoot.z;
        public float LastRetargetStageAfterManualReferencesRightToesWorldX => _endpointStageDiagnostics.AfterManualReferencesPositions.RightToes.x;
        public float LastRetargetStageAfterManualReferencesRightToesWorldZ => _endpointStageDiagnostics.AfterManualReferencesPositions.RightToes.z;
        public float LastRetargetStageAfterRootRestoreLeftFootWorldX => _endpointStageDiagnostics.AfterRootRestorePositions.LeftFoot.x;
        public float LastRetargetStageAfterRootRestoreLeftFootWorldZ => _endpointStageDiagnostics.AfterRootRestorePositions.LeftFoot.z;
        public float LastRetargetStageAfterRootRestoreLeftToesWorldX => _endpointStageDiagnostics.AfterRootRestorePositions.LeftToes.x;
        public float LastRetargetStageAfterRootRestoreLeftToesWorldZ => _endpointStageDiagnostics.AfterRootRestorePositions.LeftToes.z;
        public float LastRetargetStageAfterRootRestoreRightFootWorldX => _endpointStageDiagnostics.AfterRootRestorePositions.RightFoot.x;
        public float LastRetargetStageAfterRootRestoreRightFootWorldZ => _endpointStageDiagnostics.AfterRootRestorePositions.RightFoot.z;
        public float LastRetargetStageAfterRootRestoreRightToesWorldX => _endpointStageDiagnostics.AfterRootRestorePositions.RightToes.x;
        public float LastRetargetStageAfterRootRestoreRightToesWorldZ => _endpointStageDiagnostics.AfterRootRestorePositions.RightToes.z;
        public float LastRetargetStageAfterRootDeltaLeftFootWorldX => _endpointStageDiagnostics.AfterRootDeltaPositions.LeftFoot.x;
        public float LastRetargetStageAfterRootDeltaLeftFootWorldZ => _endpointStageDiagnostics.AfterRootDeltaPositions.LeftFoot.z;
        public float LastRetargetStageAfterRootDeltaLeftToesWorldX => _endpointStageDiagnostics.AfterRootDeltaPositions.LeftToes.x;
        public float LastRetargetStageAfterRootDeltaLeftToesWorldZ => _endpointStageDiagnostics.AfterRootDeltaPositions.LeftToes.z;
        public float LastRetargetStageAfterRootDeltaRightFootWorldX => _endpointStageDiagnostics.AfterRootDeltaPositions.RightFoot.x;
        public float LastRetargetStageAfterRootDeltaRightFootWorldZ => _endpointStageDiagnostics.AfterRootDeltaPositions.RightFoot.z;
        public float LastRetargetStageAfterRootDeltaRightToesWorldX => _endpointStageDiagnostics.AfterRootDeltaPositions.RightToes.x;
        public float LastRetargetStageAfterRootDeltaRightToesWorldZ => _endpointStageDiagnostics.AfterRootDeltaPositions.RightToes.z;
        public float LastRetargetStageAfterGroundingLeftFootWorldX => _endpointStageDiagnostics.AfterGroundingPositions.LeftFoot.x;
        public float LastRetargetStageAfterGroundingLeftFootWorldZ => _endpointStageDiagnostics.AfterGroundingPositions.LeftFoot.z;
        public float LastRetargetStageAfterGroundingLeftToesWorldX => _endpointStageDiagnostics.AfterGroundingPositions.LeftToes.x;
        public float LastRetargetStageAfterGroundingLeftToesWorldZ => _endpointStageDiagnostics.AfterGroundingPositions.LeftToes.z;
        public float LastRetargetStageAfterGroundingRightFootWorldX => _endpointStageDiagnostics.AfterGroundingPositions.RightFoot.x;
        public float LastRetargetStageAfterGroundingRightFootWorldZ => _endpointStageDiagnostics.AfterGroundingPositions.RightFoot.z;
        public float LastRetargetStageAfterGroundingRightToesWorldX => _endpointStageDiagnostics.AfterGroundingPositions.RightToes.x;
        public float LastRetargetStageAfterGroundingRightToesWorldZ => _endpointStageDiagnostics.AfterGroundingPositions.RightToes.z;
        public float LastRetargetStageAfterBipedIKLeftFootWorldX => _endpointStageDiagnostics.AfterBipedIKPositions.LeftFoot.x;
        public float LastRetargetStageAfterBipedIKLeftFootWorldZ => _endpointStageDiagnostics.AfterBipedIKPositions.LeftFoot.z;
        public float LastRetargetStageAfterBipedIKLeftToesWorldX => _endpointStageDiagnostics.AfterBipedIKPositions.LeftToes.x;
        public float LastRetargetStageAfterBipedIKLeftToesWorldZ => _endpointStageDiagnostics.AfterBipedIKPositions.LeftToes.z;
        public float LastRetargetStageAfterBipedIKRightFootWorldX => _endpointStageDiagnostics.AfterBipedIKPositions.RightFoot.x;
        public float LastRetargetStageAfterBipedIKRightFootWorldZ => _endpointStageDiagnostics.AfterBipedIKPositions.RightFoot.z;
        public float LastRetargetStageAfterBipedIKRightToesWorldX => _endpointStageDiagnostics.AfterBipedIKPositions.RightToes.x;
        public float LastRetargetStageAfterBipedIKRightToesWorldZ => _endpointStageDiagnostics.AfterBipedIKPositions.RightToes.z;
        public float LastRetargetStageAfterLateVisualGroundingLeftFootWorldX => _endpointStageDiagnostics.AfterLateVisualGroundingPositions.LeftFoot.x;
        public float LastRetargetStageAfterLateVisualGroundingLeftFootWorldZ => _endpointStageDiagnostics.AfterLateVisualGroundingPositions.LeftFoot.z;
        public float LastRetargetStageAfterLateVisualGroundingLeftToesWorldX => _endpointStageDiagnostics.AfterLateVisualGroundingPositions.LeftToes.x;
        public float LastRetargetStageAfterLateVisualGroundingLeftToesWorldZ => _endpointStageDiagnostics.AfterLateVisualGroundingPositions.LeftToes.z;
        public float LastRetargetStageAfterLateVisualGroundingRightFootWorldX => _endpointStageDiagnostics.AfterLateVisualGroundingPositions.RightFoot.x;
        public float LastRetargetStageAfterLateVisualGroundingRightFootWorldZ => _endpointStageDiagnostics.AfterLateVisualGroundingPositions.RightFoot.z;
        public float LastRetargetStageAfterLateVisualGroundingRightToesWorldX => _endpointStageDiagnostics.AfterLateVisualGroundingPositions.RightToes.x;
        public float LastRetargetStageAfterLateVisualGroundingRightToesWorldZ => _endpointStageDiagnostics.AfterLateVisualGroundingPositions.RightToes.z;
        public string LastRetargetEndpointFirstJumpStage => _endpointStageDiagnostics.FirstJumpStage;
        public string LastRetargetEndpointFirstJumpEndpoint => _endpointStageDiagnostics.FirstJumpEndpoint;
        public float LastRetargetEndpointFirstJumpMagnitude => _endpointStageDiagnostics.FirstJumpMagnitude;
        public float LastRetargetEndpointFirstJumpDeltaX => _endpointStageDiagnostics.FirstJumpDelta.x;
        public float LastRetargetEndpointFirstJumpDeltaY => _endpointStageDiagnostics.FirstJumpDelta.y;
        public float LastRetargetEndpointFirstJumpDeltaZ => _endpointStageDiagnostics.FirstJumpDelta.z;
        public float LastEditorFootLocalRotationLeftFootXzDelta => _footReferenceDiagnostics.FootLocalRotationLeftFootXzDelta;
        public float LastEditorFootLocalRotationRightFootXzDelta => _footReferenceDiagnostics.FootLocalRotationRightFootXzDelta;
        public float LastEditorLowerBodySegmentDirectionLeftFootXzDelta => _lowerBodySegmentDiagnostics.LeftFootXzDelta;
        public float LastEditorLowerBodySegmentDirectionRightFootXzDelta => _lowerBodySegmentDiagnostics.RightFootXzDelta;
        public string LastEditorLowerBodySegmentDirectionMaxCorrectionSegment => _lowerBodySegmentDiagnostics.MaxCorrectionSegment;
        public float LastEditorLowerBodySegmentDirectionMaxCorrectionAngle => _lowerBodySegmentDiagnostics.MaxCorrectionAngle;
        public float LastEditorLowerBodySegmentDirectionMaxPreAngle => _lowerBodySegmentDiagnostics.MaxPreAngle;
        public float LastEditorLowerBodySegmentDirectionMaxPostAngle => _lowerBodySegmentDiagnostics.MaxPostAngle;
        public float LastEditorLowerBodySegmentDirectionMaxCorrectionAxisX => _lowerBodySegmentDiagnostics.MaxCorrectionAxis.x;
        public float LastEditorLowerBodySegmentDirectionMaxCorrectionAxisY => _lowerBodySegmentDiagnostics.MaxCorrectionAxis.y;
        public float LastEditorLowerBodySegmentDirectionMaxCorrectionAxisZ => _lowerBodySegmentDiagnostics.MaxCorrectionAxis.z;
        public float LastEditorLowerBodySegmentDirectionMaxReferenceDirectionX => _lowerBodySegmentDiagnostics.MaxReferenceDirection.x;
        public float LastEditorLowerBodySegmentDirectionMaxReferenceDirectionY => _lowerBodySegmentDiagnostics.MaxReferenceDirection.y;
        public float LastEditorLowerBodySegmentDirectionMaxReferenceDirectionZ => _lowerBodySegmentDiagnostics.MaxReferenceDirection.z;
        public float LastEditorLowerBodySegmentDirectionMaxPreDirectionX => _lowerBodySegmentDiagnostics.MaxPreDirection.x;
        public float LastEditorLowerBodySegmentDirectionMaxPreDirectionY => _lowerBodySegmentDiagnostics.MaxPreDirection.y;
        public float LastEditorLowerBodySegmentDirectionMaxPreDirectionZ => _lowerBodySegmentDiagnostics.MaxPreDirection.z;
        public float LastEditorLowerBodySegmentDirectionMaxPostDirectionX => _lowerBodySegmentDiagnostics.MaxPostDirection.x;
        public float LastEditorLowerBodySegmentDirectionMaxPostDirectionY => _lowerBodySegmentDiagnostics.MaxPostDirection.y;
        public float LastEditorLowerBodySegmentDirectionMaxPostDirectionZ => _lowerBodySegmentDiagnostics.MaxPostDirection.z;
        public float LastEditorLowerBodySegmentDirectionLeftUpperLegLowerLegCorrectionAngle => _lowerBodySegmentDiagnostics.LeftUpperLegLowerLegCorrectionAngle;
        public float LastEditorLowerBodySegmentDirectionRightUpperLegLowerLegCorrectionAngle => _lowerBodySegmentDiagnostics.RightUpperLegLowerLegCorrectionAngle;
        public float LastEditorLowerBodySegmentDirectionLeftLowerLegFootCorrectionAngle => _lowerBodySegmentDiagnostics.LeftLowerLegFootCorrectionAngle;
        public float LastEditorLowerBodySegmentDirectionRightLowerLegFootCorrectionAngle => _lowerBodySegmentDiagnostics.RightLowerLegFootCorrectionAngle;
        public float LastEditorLowerBodySegmentDirectionLeftFootToesCorrectionAngle => _lowerBodySegmentDiagnostics.LeftFootToesCorrectionAngle;
        public float LastEditorLowerBodySegmentDirectionRightFootToesCorrectionAngle => _lowerBodySegmentDiagnostics.RightFootToesCorrectionAngle;
        public float LastEditorLowerBodySegmentDirectionLeftLowerLegToFootParentWorldRotationDeltaAngle => _lowerBodySegmentDiagnostics.LeftLowerLegToFootParentWorldRotationDeltaAngle;
        public float LastEditorLowerBodySegmentDirectionRightLowerLegToFootParentWorldRotationDeltaAngle => _lowerBodySegmentDiagnostics.RightLowerLegToFootParentWorldRotationDeltaAngle;
        public float LastEditorLowerBodySegmentDirectionLeftLowerLegToFootChildFootLocalRotationDeltaAngle => _lowerBodySegmentDiagnostics.LeftLowerLegToFootChildFootLocalRotationDeltaAngle;
        public float LastEditorLowerBodySegmentDirectionRightLowerLegToFootChildFootLocalRotationDeltaAngle => _lowerBodySegmentDiagnostics.RightLowerLegToFootChildFootLocalRotationDeltaAngle;
        public float LastEditorLowerBodySegmentDirectionLeftFootToToesReferenceDirectionX => _lowerBodySegmentDiagnostics.LeftFootToToesReferenceDirection.x;
        public float LastEditorLowerBodySegmentDirectionLeftFootToToesReferenceDirectionY => _lowerBodySegmentDiagnostics.LeftFootToToesReferenceDirection.y;
        public float LastEditorLowerBodySegmentDirectionLeftFootToToesReferenceDirectionZ => _lowerBodySegmentDiagnostics.LeftFootToToesReferenceDirection.z;
        public float LastEditorLowerBodySegmentDirectionLeftFootToToesPreDirectionX => _lowerBodySegmentDiagnostics.LeftFootToToesPreDirection.x;
        public float LastEditorLowerBodySegmentDirectionLeftFootToToesPreDirectionY => _lowerBodySegmentDiagnostics.LeftFootToToesPreDirection.y;
        public float LastEditorLowerBodySegmentDirectionLeftFootToToesPreDirectionZ => _lowerBodySegmentDiagnostics.LeftFootToToesPreDirection.z;
        public float LastEditorLowerBodySegmentDirectionLeftFootToToesPostDirectionX => _lowerBodySegmentDiagnostics.LeftFootToToesPostDirection.x;
        public float LastEditorLowerBodySegmentDirectionLeftFootToToesPostDirectionY => _lowerBodySegmentDiagnostics.LeftFootToToesPostDirection.y;
        public float LastEditorLowerBodySegmentDirectionLeftFootToToesPostDirectionZ => _lowerBodySegmentDiagnostics.LeftFootToToesPostDirection.z;
        public float LastEditorLowerBodySegmentDirectionRightFootToToesReferenceDirectionX => _lowerBodySegmentDiagnostics.RightFootToToesReferenceDirection.x;
        public float LastEditorLowerBodySegmentDirectionRightFootToToesReferenceDirectionY => _lowerBodySegmentDiagnostics.RightFootToToesReferenceDirection.y;
        public float LastEditorLowerBodySegmentDirectionRightFootToToesReferenceDirectionZ => _lowerBodySegmentDiagnostics.RightFootToToesReferenceDirection.z;
        public float LastEditorLowerBodySegmentDirectionRightFootToToesPreDirectionX => _lowerBodySegmentDiagnostics.RightFootToToesPreDirection.x;
        public float LastEditorLowerBodySegmentDirectionRightFootToToesPreDirectionY => _lowerBodySegmentDiagnostics.RightFootToToesPreDirection.y;
        public float LastEditorLowerBodySegmentDirectionRightFootToToesPreDirectionZ => _lowerBodySegmentDiagnostics.RightFootToToesPreDirection.z;
        public float LastEditorLowerBodySegmentDirectionRightFootToToesPostDirectionX => _lowerBodySegmentDiagnostics.RightFootToToesPostDirection.x;
        public float LastEditorLowerBodySegmentDirectionRightFootToToesPostDirectionY => _lowerBodySegmentDiagnostics.RightFootToToesPostDirection.y;
        public float LastEditorLowerBodySegmentDirectionRightFootToToesPostDirectionZ => _lowerBodySegmentDiagnostics.RightFootToToesPostDirection.z;
        public float LastEditorLowerBodySegmentDirectionLeftLowerLegWorldX => _lowerBodySegmentDiagnostics.LeftLowerLegWorldPosition.x;
        public float LastEditorLowerBodySegmentDirectionLeftLowerLegWorldY => _lowerBodySegmentDiagnostics.LeftLowerLegWorldPosition.y;
        public float LastEditorLowerBodySegmentDirectionLeftLowerLegWorldZ => _lowerBodySegmentDiagnostics.LeftLowerLegWorldPosition.z;
        public float LastEditorLowerBodySegmentDirectionLeftFootWorldX => _lowerBodySegmentDiagnostics.LeftFootWorldPosition.x;
        public float LastEditorLowerBodySegmentDirectionLeftFootWorldY => _lowerBodySegmentDiagnostics.LeftFootWorldPosition.y;
        public float LastEditorLowerBodySegmentDirectionLeftFootWorldZ => _lowerBodySegmentDiagnostics.LeftFootWorldPosition.z;
        public float LastEditorLowerBodySegmentDirectionLeftToesWorldX => _lowerBodySegmentDiagnostics.LeftToesWorldPosition.x;
        public float LastEditorLowerBodySegmentDirectionLeftToesWorldY => _lowerBodySegmentDiagnostics.LeftToesWorldPosition.y;
        public float LastEditorLowerBodySegmentDirectionLeftToesWorldZ => _lowerBodySegmentDiagnostics.LeftToesWorldPosition.z;
        public float LastEditorLowerBodySegmentDirectionRightLowerLegWorldX => _lowerBodySegmentDiagnostics.RightLowerLegWorldPosition.x;
        public float LastEditorLowerBodySegmentDirectionRightLowerLegWorldY => _lowerBodySegmentDiagnostics.RightLowerLegWorldPosition.y;
        public float LastEditorLowerBodySegmentDirectionRightLowerLegWorldZ => _lowerBodySegmentDiagnostics.RightLowerLegWorldPosition.z;
        public float LastEditorLowerBodySegmentDirectionRightFootWorldX => _lowerBodySegmentDiagnostics.RightFootWorldPosition.x;
        public float LastEditorLowerBodySegmentDirectionRightFootWorldY => _lowerBodySegmentDiagnostics.RightFootWorldPosition.y;
        public float LastEditorLowerBodySegmentDirectionRightFootWorldZ => _lowerBodySegmentDiagnostics.RightFootWorldPosition.z;
        public float LastEditorLowerBodySegmentDirectionRightToesWorldX => _lowerBodySegmentDiagnostics.RightToesWorldPosition.x;
        public float LastEditorLowerBodySegmentDirectionRightToesWorldY => _lowerBodySegmentDiagnostics.RightToesWorldPosition.y;
        public float LastEditorLowerBodySegmentDirectionRightToesWorldZ => _lowerBodySegmentDiagnostics.RightToesWorldPosition.z;
        public float LastEditorLowerBodySegmentDirectionLeftLowerLegToFootCorrectionAxisX => _lowerBodySegmentDiagnostics.LeftLowerLegToFootCorrectionAxis.x;
        public float LastEditorLowerBodySegmentDirectionLeftLowerLegToFootCorrectionAxisY => _lowerBodySegmentDiagnostics.LeftLowerLegToFootCorrectionAxis.y;
        public float LastEditorLowerBodySegmentDirectionLeftLowerLegToFootCorrectionAxisZ => _lowerBodySegmentDiagnostics.LeftLowerLegToFootCorrectionAxis.z;
        public float LastEditorLowerBodySegmentDirectionRightLowerLegToFootCorrectionAxisX => _lowerBodySegmentDiagnostics.RightLowerLegToFootCorrectionAxis.x;
        public float LastEditorLowerBodySegmentDirectionRightLowerLegToFootCorrectionAxisY => _lowerBodySegmentDiagnostics.RightLowerLegToFootCorrectionAxis.y;
        public float LastEditorLowerBodySegmentDirectionRightLowerLegToFootCorrectionAxisZ => _lowerBodySegmentDiagnostics.RightLowerLegToFootCorrectionAxis.z;
        public float LastEditorLowerBodySegmentDirectionLeftFootForwardX => _lowerBodySegmentDiagnostics.LeftFootForward.x;
        public float LastEditorLowerBodySegmentDirectionLeftFootForwardY => _lowerBodySegmentDiagnostics.LeftFootForward.y;
        public float LastEditorLowerBodySegmentDirectionLeftFootForwardZ => _lowerBodySegmentDiagnostics.LeftFootForward.z;
        public float LastEditorLowerBodySegmentDirectionLeftFootUpX => _lowerBodySegmentDiagnostics.LeftFootUp.x;
        public float LastEditorLowerBodySegmentDirectionLeftFootUpY => _lowerBodySegmentDiagnostics.LeftFootUp.y;
        public float LastEditorLowerBodySegmentDirectionLeftFootUpZ => _lowerBodySegmentDiagnostics.LeftFootUp.z;
        public float LastEditorLowerBodySegmentDirectionRightFootForwardX => _lowerBodySegmentDiagnostics.RightFootForward.x;
        public float LastEditorLowerBodySegmentDirectionRightFootForwardY => _lowerBodySegmentDiagnostics.RightFootForward.y;
        public float LastEditorLowerBodySegmentDirectionRightFootForwardZ => _lowerBodySegmentDiagnostics.RightFootForward.z;
        public float LastEditorLowerBodySegmentDirectionRightFootUpX => _lowerBodySegmentDiagnostics.RightFootUp.x;
        public float LastEditorLowerBodySegmentDirectionRightFootUpY => _lowerBodySegmentDiagnostics.RightFootUp.y;
        public float LastEditorLowerBodySegmentDirectionRightFootUpZ => _lowerBodySegmentDiagnostics.RightFootUp.z;
        public float LastEditorFootHipsAlignedResidualYawLeftFootXzDelta => _footReferenceDiagnostics.FootHipsAlignedResidualYawLeftFootXzDelta;
        public float LastEditorFootHipsAlignedResidualYawRightFootXzDelta => _footReferenceDiagnostics.FootHipsAlignedResidualYawRightFootXzDelta;
        public float LastPostSetHumanPoseRightEndpointDesiredFootWorldX => _postSetEndpointDiagnostics.DesiredFootWorldPosition.x;
        public float LastPostSetHumanPoseRightEndpointDesiredFootWorldZ => _postSetEndpointDiagnostics.DesiredFootWorldPosition.z;
        public float LastPostSetHumanPoseRightEndpointDesiredToesWorldX => _postSetEndpointDiagnostics.DesiredToesWorldPosition.x;
        public float LastPostSetHumanPoseRightEndpointDesiredToesWorldZ => _postSetEndpointDiagnostics.DesiredToesWorldPosition.z;
        public float LastPostSetHumanPoseRightEndpointCurrentFootWorldX => _postSetEndpointDiagnostics.CurrentFootWorldPosition.x;
        public float LastPostSetHumanPoseRightEndpointCurrentFootWorldZ => _postSetEndpointDiagnostics.CurrentFootWorldPosition.z;
        public float LastPostSetHumanPoseRightEndpointCurrentToesWorldX => _postSetEndpointDiagnostics.CurrentToesWorldPosition.x;
        public float LastPostSetHumanPoseRightEndpointCurrentToesWorldZ => _postSetEndpointDiagnostics.CurrentToesWorldPosition.z;
        public float LastPostSetHumanPoseRightEndpointDeltaBeforeClampX => _postSetEndpointDiagnostics.DeltaBeforeClamp.x;
        public float LastPostSetHumanPoseRightEndpointDeltaBeforeClampZ => _postSetEndpointDiagnostics.DeltaBeforeClamp.z;
        public float LastPostSetHumanPoseRightEndpointDeltaAfterClampX => _postSetEndpointDiagnostics.DeltaAfterClamp.x;
        public float LastPostSetHumanPoseRightEndpointDeltaAfterClampZ => _postSetEndpointDiagnostics.DeltaAfterClamp.z;
        public float LastPostSetHumanPoseRightEndpointDeltaAfterPositiveZScaleX => _postSetEndpointDiagnostics.DeltaAfterPositiveZScale.x;
        public float LastPostSetHumanPoseRightEndpointDeltaAfterPositiveZScaleZ => _postSetEndpointDiagnostics.DeltaAfterPositiveZScale.z;
        public float LastPostSetHumanPoseRightEndpointCorrectionX => _postSetEndpointDiagnostics.Correction.x;
        public float LastPostSetHumanPoseRightEndpointCorrectionZ => _postSetEndpointDiagnostics.Correction.z;
        public float LastPostSetHumanPoseRightEndpointNextFootWorldX => _postSetEndpointDiagnostics.NextFootWorldPosition.x;
        public float LastPostSetHumanPoseRightEndpointNextFootWorldZ => _postSetEndpointDiagnostics.NextFootWorldPosition.z;
        public float LastPostSetHumanPoseRightEndpointMaxYawAngle => _postSetEndpointDiagnostics.MaxYawAngle;
        public float LastPostSetHumanPoseRightEndpointYawCorrectionAngle => _postSetEndpointDiagnostics.YawCorrectionAngle;
        public float LastPostSetHumanPoseRightEndpointUpperLegRotationDeltaAngle => _postSetEndpointDiagnostics.UpperLegRotationDeltaAngle;
        public float LastPostSetHumanPoseRightEndpointApplied => _postSetEndpointDiagnostics.Applied;
        public float LastPostSetHumanPoseRightEndpointEvaluatorXzReferenceEnabled => _postSetEndpointDiagnostics.EvaluatorXzReferenceEnabled;
        public float LastPostSetHumanPoseRightEndpointEvaluatorXzFirstOffsetX => _postSetEndpointDiagnostics.EvaluatorXzFirstOffset.x;
        public float LastPostSetHumanPoseRightEndpointEvaluatorXzFirstOffsetZ => _postSetEndpointDiagnostics.EvaluatorXzFirstOffset.z;
        public float LastPostSetHumanPoseRightEndpointEvaluatorXzNormalizedDeltaX => _postSetEndpointDiagnostics.EvaluatorXzNormalizedDelta.x;
        public float LastPostSetHumanPoseRightEndpointEvaluatorXzNormalizedDeltaZ => _postSetEndpointDiagnostics.EvaluatorXzNormalizedDelta.z;
        public float LastPostSetHumanPoseRightEndpointEvaluatorXzNormalizedMagnitude => _postSetEndpointDiagnostics.EvaluatorXzNormalizedDelta.magnitude;
        public float LastPostSetHumanPoseRightEndpointEvaluatorXzDesiredNormalizedDeltaX => _postSetEndpointDiagnostics.EvaluatorXzDesiredNormalizedDelta.x;
        public float LastPostSetHumanPoseRightEndpointEvaluatorXzDesiredNormalizedDeltaZ => _postSetEndpointDiagnostics.EvaluatorXzDesiredNormalizedDelta.z;
        public float LastPostSetHumanPoseRightEndpointEvaluatorXzTargetMagnitude => _postSetEndpointDiagnostics.EvaluatorXzTargetMagnitude;

        public void ResetPlaybackStabilityMetrics()
        {
            _legacyAnimationDriver.ResetStabilityMetrics();
            ResetEditorHumanoidRootTranslationReferenceState();
            ResetTargetHipsLocalPositionSpikeState();
            ResetVisualPoseHistory();
            _poseVisualSmoothingCount = 0;
            _poseVisualMuscleDeltaOnlySkippedCount = 0;
            _lastPoseVisualMaxMuscleDelta = float.NaN;
            _maxPoseVisualMaxMuscleDelta = 0f;
            _grounding.ResetMetrics();
            _lastEditorFootHeightGroundingReferenceLift = float.NaN;
            _hasEditorReferenceLowestFootRestY = false;
            _allowEditorFootHeightGroundingReference = true;
            ResetRetargetPoseStageDiagnostics();
        }

        public bool PrepareRecordingStartPose(float startTimeSeconds, float playbackSpeed, bool holdPose)
        {
            if (!_legacyAnimationDriver.TryPrepareRecordingStartPose(startTimeSeconds, playbackSpeed, holdPose))
            {
                return false;
            }

            ResetVisualPoseHistory();
            return true;
        }

        public void CaptureRecordingStartBaselineSnapshot()
        {
            ResetRecordingStartHipsBaselineDiagnostics();
            _recordingStartHipsReferenceStage = RecordingStartHipsReferenceStagePrewarmComplete;
            if (targetAnimator == null)
            {
                return;
            }

            Vector3 rootPosition = targetAnimator.transform.position;
            _recordingStartRootY = IsFinite(rootPosition) ? rootPosition.y : float.NaN;
            _recordingStartBodyPositionY = TryGetTargetBodyPositionY(out float bodyPositionY)
                ? bodyPositionY
                : float.NaN;

            Transform targetHips = targetAnimator.GetBoneTransform(HumanBodyBones.Hips);
            if (targetHips != null)
            {
                Vector3 hipsLocalPosition = targetHips.localPosition;
                Vector3 hipsPosition = targetHips.position;
                _recordingStartHipsLocalY = IsFinite(hipsLocalPosition) ? hipsLocalPosition.y : float.NaN;
                _recordingStartHipsY = IsFinite(hipsPosition) ? hipsPosition.y : float.NaN;
            }

            _recordingStartHipsReferenceBeforeLocalY = _footReferenceDiagnostics.HipsLocalReferenceBeforeLocalY;
            _recordingStartHipsReferenceAfterLocalY = _footReferenceDiagnostics.HipsLocalReferenceAfterLocalY;
            _recordingStartHipsReferenceDeltaY = _footReferenceDiagnostics.HipsLocalReferenceDeltaY;
            _recordingStartHipsReferenceFlipDetected = IsRecordingStartHipsBaselineFlip(
                _recordingStartHipsReferenceBeforeLocalY,
                _recordingStartHipsReferenceAfterLocalY,
                RecordingStartHipsBaselineFlipWarningThreshold);
        }

        [Tooltip("Target Humanoid 본의 localPosition을 초기값으로 되돌려 팔/다리 길이 변형을 막습니다.")]
        [FormerlySerializedAs("ShouldLockTargetHumanoidBonePositions")]
        [SerializeField] private bool _ShouldLockTargetHumanoidBonePositions= true;
        public bool ShouldLockTargetHumanoidBonePositions { get => _ShouldLockTargetHumanoidBonePositions; set => _ShouldLockTargetHumanoidBonePositions = value; }

        // --- 내부 변수 ---
        private HumanPoseHandler _ghostHandler;
        private HumanPoseHandler _targetHandler;
        private HumanPose _humanPose;
        private HumanPose _appliedTargetPose;

        private Vector3 _prevGhostPos;
        private static readonly Quaternion LegacyFacingCorrection = Quaternion.Euler(0f, 180f, 0f);
        internal const string LeftThumbBaseHelperNameSuffix = "joint_LeftThumb0";
        internal const string RightThumbBaseHelperNameSuffix = "joint_RightThumb0";
        internal const float ManualThumbOverrideSpreadWarningAngle = 38f;
        internal const float ManualThumbOverrideSpreadFullRiskAngle = 52f;
        internal const float ManualThumbOverrideProjectionMin = 0.358f;
        internal const float ManualThumbOverrideProjectionMax = 0.5f;
        internal const float ManualThumbWorldRotationReferenceToleranceDegrees = 1.5f;
        internal const float ManualThumbDetachedHelperPreserveCurrentReferenceAngleMax = 12f;
        internal const float ManualThumbDetachedHelperOverrideKeepSpreadDeltaMax = 14f;
        internal const float ManualThumbDetachedHelperOverrideKeepProjectionDeltaMax = 0.1f;
        internal const float ManualThumbPoseShapingSuppressMaxRisk = 0.35f;
        internal const float ManualThumbOverrideSuppressRiskThreshold = 0.2f;
        internal const float ManualThumbOverrideRiskIncreaseTolerance = 0.05f;


        private Quaternion _facingCorrection = LegacyFacingCorrection;
        private Quaternion _poseRootRotationCorrection = Quaternion.identity;
        private Vector3 _targetReferenceBodyPosition;
        private bool _hasTargetReferenceBodyPosition;
        private Vector3 _targetRootPoseGuardAnchorPosition;
        private bool _hasTargetRootPoseGuardAnchorPosition;
        private Vector3 _previousBodyRootMotionPosition;
        private bool _hasPreviousBodyRootMotionPosition;
        private float _scaleRatio = 1.0f; // 체형 차이 비율
        private float _movementScaleMultiplier = 1.0f;
        private float _initialGhostHipHeight = 1.0f;
        private float _initialTargetHipHeight = 1.0f;
        private readonly Dictionary<Transform, Vector3> _targetInitialScales = new Dictionary<Transform, Vector3>();
        internal readonly Dictionary<Transform, Vector3> _targetInitialHumanoidLocalPositions = new Dictionary<Transform, Vector3>();
        internal readonly Dictionary<Transform, Vector3> _targetInitialThumbBaseHelperLocalPositions = new Dictionary<Transform, Vector3>();
        internal readonly Dictionary<Transform, Quaternion> _targetInitialThumbLocalRotations = new Dictionary<Transform, Quaternion>();
        internal Vector3 _targetHipsRestLocalPosition;
        internal bool _hasTargetHipsRestLocalPosition;
        internal readonly Dictionary<bool, Transform> _cachedThumbBaseHelpers = new Dictionary<bool, Transform>();
        internal readonly Dictionary<bool, Transform> _cachedThumbBaseExplicitSources = new Dictionary<bool, Transform>();
        internal readonly Dictionary<bool, float> _initialThumbBaseHelperSourceDistances = new Dictionary<bool, float>();
        internal readonly Dictionary<bool, Quaternion> _initialThumbBaseHelperSourceRelativeRotations = new Dictionary<bool, Quaternion>();
        private bool _scaleWarningLogged;
        private bool _positionWarningLogged;
        private bool _poseWarningLogged;
        private bool _muscleClampWarningLogged;
        private bool _anatomyGuardWarningLogged;
        internal bool _thumbGuardWarningLogged;
        internal bool _thumbLocalRotationGuardWarningLogged;
        private bool _rootDeltaSpikeWarningLogged;
        private bool _appliedPoseClampWarningLogged;
        private bool _hasPoseRootRotationCorrection;
        private float _lastRootDeltaMagnitude = float.NaN;
        private float _maxRootDeltaMagnitude;
        private int _rootDeltaSpikeSkippedCount;
        private float _lastRootPositionPoseDeltaMagnitude = float.NaN;
        private float _maxRootPositionPoseDeltaMagnitude;
        private int _rootPositionSpikeClampedCount;
        private Vector3 _previousTargetHipsLocalPosition;
        private bool _hasPreviousTargetHipsLocalPosition;
        private float _lastTargetHipsLocalPositionDelta = float.NaN;
        private float _maxTargetHipsLocalPositionDelta;
        private int _targetHipsLocalPositionSpikeClampedCount;
        private readonly RetargetingGroundingProcessor _grounding = new RetargetingGroundingProcessor();
        internal float _lastEditorFootHeightGroundingReferenceLift = float.NaN;
        private float _recordingStartRootY = float.NaN;
        private float _recordingStartBodyPositionY = float.NaN;
        private float _recordingStartHipsLocalY = float.NaN;
        private float _recordingStartHipsY = float.NaN;
        private float _recordingStartHipsReferenceBeforeLocalY = float.NaN;
        private float _recordingStartHipsReferenceAfterLocalY = float.NaN;
        private float _recordingStartHipsReferenceDeltaY = float.NaN;
        private bool _recordingStartHipsReferenceFlipDetected;
        private string _recordingStartHipsReferenceStage = string.Empty;
        private bool _hasPreviousVisualPose;
        private float[] _previousVisualPoseMuscles;
        private Vector3 _previousVisualPoseBodyPosition;
        private Quaternion _previousVisualPoseBodyRotation = Quaternion.identity;
        private int _poseVisualSmoothingCount;
        private int _poseVisualMuscleDeltaOnlySkippedCount;
        private float _lastPoseVisualMaxMuscleDelta = float.NaN;
        private float _maxPoseVisualMaxMuscleDelta;
        internal readonly RetargetingLowerBodySegmentDiagnostics _lowerBodySegmentDiagnostics = new RetargetingLowerBodySegmentDiagnostics();
        internal readonly RetargetingFootReferenceDiagnostics _footReferenceDiagnostics = new RetargetingFootReferenceDiagnostics();
        internal readonly RetargetingEndpointStageDiagnostics _endpointStageDiagnostics = new RetargetingEndpointStageDiagnostics();
        private readonly RetargetingStageMuscleDiagnostics _stageMuscleDiagnostics = new RetargetingStageMuscleDiagnostics();
        internal readonly RetargetingThumbGuardDiagnostics _thumbGuardDiagnostics = new RetargetingThumbGuardDiagnostics();
        internal readonly RetargetingSetHumanPoseDiagnostics _setHumanPoseDiagnostics = new RetargetingSetHumanPoseDiagnostics();
        internal readonly RetargetingPostSetEndpointDiagnostics _postSetEndpointDiagnostics = new RetargetingPostSetEndpointDiagnostics();
        private readonly RetargetingMuscleIndexMap _muscleIndexMap = new RetargetingMuscleIndexMap();

        internal static Vector3 BuildNaNVector3()
        {
            return new Vector3(float.NaN, float.NaN, float.NaN);
        }

        private static Quaternion BuildNaNQuaternion()
        {
            return new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN);
        }

        internal static readonly HumanBodyBones[] ThumbRotationBones =
        {
            HumanBodyBones.LeftThumbProximal,
            HumanBodyBones.LeftThumbIntermediate,
            HumanBodyBones.LeftThumbDistal,
            HumanBodyBones.RightThumbProximal,
            HumanBodyBones.RightThumbIntermediate,
            HumanBodyBones.RightThumbDistal
        };
#if UNITY_EDITOR
        internal object _editorPoseReferenceSession;
        internal object _editorFootReferenceSession;
        internal object _editorSleeveReferenceSession;
        internal readonly Dictionary<int, AnimationCurve> _editorHumanoidMuscleCurves = new Dictionary<int, AnimationCurve>();
        internal bool _useEditorHumanoidMuscleReference;
        internal bool _useCompleteEditorHumanoidMuscleReference;
        internal bool _editorHumanoidMuscleReferenceLogged;
        internal HumanoidPoseReferencePlayer _editorHumanoidPoseReferencePlayer;
        internal HumanPose _editorHumanoidPoseReference;
        internal bool _hasEditorHumanoidPoseReferenceForFrame;
        internal bool _editorRootTranslationReferenceLogged;
        internal Animator _editorFingerReferenceAnimator;
        internal bool _useEditorFingerPoseReference;
        internal bool _editorFingerPoseReferenceLogged;
        internal bool _editorBodyRotationReferenceLogged;
        internal bool _hasEditorReferenceBodyPosition;
        internal Vector3 _editorReferenceBodyPosition;
        internal bool _hasEditorReferenceLowestFootRestY;
        internal float _editorReferenceLowestFootRestY;
        internal bool _allowEditorFootHeightGroundingReference;
        internal bool _hasEditorReferenceHipsRestLocalPosition;
        internal Vector3 _editorReferenceHipsRestLocalPosition;
        internal bool _editorHandLocalRotationReferenceLogged;
        internal bool _editorFootLocalRotationReferenceLogged;
        internal bool _editorFootHipsAlignedResidualYawReferenceLogged;
        internal bool _editorHipsLocalPositionReferenceLogged;
        internal bool _editorFootIkPositionReferenceLogged;
        internal bool _editorLowerBodySegmentDirectionReferenceLogged;
        internal bool _editorThumbLocalRotationReferenceLogged;
        internal bool _editorThumbSegmentDirectionReferenceLogged;
        internal bool _editorHandPalmFrameReferenceLogged;
        internal bool _editorThumbBasePositionReferenceLogged;
        internal bool _editorBodyPositionXzReferenceLogged;
#else
        internal Animator _editorFingerReferenceAnimator;
        internal bool _useEditorFingerPoseReference;
        private bool _hasEditorReferenceBodyPosition;
        private Vector3 _editorReferenceBodyPosition;
        private bool _hasEditorReferenceLowestFootRestY;
        private bool _allowEditorFootHeightGroundingReference;
        // 빌드에서는 muscle curve 추출이 불가해 HumanPoseHandler 샘플링 결과만 참조로 사용함.
        private HumanoidPoseReferencePlayer _editorHumanoidPoseReferencePlayer;
        private HumanPose _editorHumanoidPoseReference;
        private bool _useCompleteEditorHumanoidMuscleReference;
        private bool _hasEditorHumanoidPoseReferenceForFrame;
#endif

        // --- 초기화 ---
        internal bool _isInitialized = false;
        internal readonly LegacyAnimationDriver _legacyAnimationDriver = new LegacyAnimationDriver();

        public void Initialize(RetargetingContext context, RetargetingSettings settings)
        {
            GameObject ghostRoot = context.GhostRoot;
            GameObject targetRoot = context.TargetRoot;
            AnimationClip clip = context.Clip;

            _legacyAnimationDriver.Dispose();

            ghostAnimator = ghostRoot.GetComponent<Animator>();
            targetAnimator = targetRoot.GetComponent<Animator>();
            CaptureTargetInitialTransforms(targetRoot);
            _targetRootPoseGuardAnchorPosition = targetAnimator != null ? targetAnimator.transform.position : Vector3.zero;
            _hasTargetRootPoseGuardAnchorPosition = targetAnimator != null && IsFinite(_targetRootPoseGuardAnchorPosition);

            if (clip == null) return;

            _legacyAnimationDriver.Initialize(ghostRoot, ghostAnimator, clip);

            // 포즈 핸들러 초기화
            if (!ghostAnimator.avatar || !targetAnimator.avatar) return;
            _ghostHandler = new HumanPoseHandler(ghostAnimator.avatar, ghostAnimator.transform);
            _targetHandler = new HumanPoseHandler(targetAnimator.avatar, targetAnimator.transform);
            _humanPose = new HumanPose();
            _appliedTargetPose = new HumanPose();
            CacheTargetReferenceBodyPosition();
            _grounding.CalibrateTargetFootRadius(this);

            // 초기 위치 저장
            _prevGhostPos = ghostAnimator.transform.position;
            ResetEditorHumanoidRootTranslationReferenceState();
            ResetRecordingStartHipsBaselineDiagnostics();
            ResetLastEditorHipsLocalReferenceDiagnostics();
            CacheInitialHipHeights();
            _hasEditorReferenceLowestFootRestY = false;
            _allowEditorFootHeightGroundingReference = false;
            _facingCorrection = settings != null && settings.ShouldUseLegacyPoseSpaceFacingCorrection
                ? LegacyFacingCorrection
                : Quaternion.Inverse(ghostAnimator.transform.rotation) * targetAnimator.transform.rotation;
            _poseRootRotationCorrection = Quaternion.identity;
            _hasPoseRootRotationCorrection = false;
            if (settings != null)
            {
                groundOffset = settings.HeightOffset;
                _movementScaleMultiplier = RootMotionGuard.NormalizeMovementScaleMultiplier(settings.MovementScaleMultiplier);
                ShouldPreserveFbxRootRotation = settings.ShouldPreserveFbxRootRotation && !settings.ShouldUseLegacyPoseSpaceFacingCorrection;
                preserveTargetBodyPosition = settings.ShouldPreserveRetargetBodyPosition;
                useBodyPositionXZRootMotion = settings.ShouldUseRetargetBodyPositionXZRootMotion;
                ShouldUseEditorHumanoidRootTranslationReference = settings.ShouldUseEditorHumanoidRootTranslationReference;
                editorHumanoidRootTranslationWeight = Mathf.Clamp01(settings.editorHumanoidRootTranslationWeight);
                editorHumanoidRootTranslationCurrentWeight = Mathf.Clamp(settings.editorHumanoidRootTranslationCurrentWeight, 0.05f, 1f);
                ShouldStabilizeGroundedFootXZ = settings.ShouldStabilizeGroundedFootXZ;
                groundedFootLockWeight = Mathf.Clamp01(settings.GroundedFootLockWeight);
                maxGroundedFootLockStep = Mathf.Max(0.001f, settings.MaxGroundedFootLockStep);
                clampMusclesToHumanRange = settings.clampRetargetMusclesToHumanRange;
                enableAnatomicalArmGuard = settings.enableAnatomicalArmGuard;
                armStretchMuscleLimit = settings.ArmStretchMuscleLimit;
                clampArmStretchMuscles = settings.clampRetargetArmStretchMuscles;
                upperArmTwistMuscleLimit = settings.UpperArmTwistMuscleLimit;
                lowerArmTwistMuscleLimit = settings.LowerArmTwistMuscleLimit;
                enableThumbAnatomicalGuard = settings.enableThumbAnatomicalGuard;
                thumbStretchMin = settings.ThumbStretchMin;
                thumbStretchMax = settings.ThumbStretchMax;
                thumbStretchOffset = settings.EffectiveThumbStretchOffset;
                preserveManualFingerReferenceThumbMuscles = settings.preserveManualFingerReferenceThumbMuscles;
                ShouldUseManualAnimatorFullBodyPoseReference = settings.ShouldUseManualAnimatorFullBodyPoseReference;
                manualAnimatorFullBodyPoseReferenceWeight = Mathf.Clamp01(settings.manualAnimatorFullBodyPoseReferenceWeight);
                ShouldExcludeManualAnimatorFullBodyLowerMuscles =
                    settings.ShouldExcludeManualAnimatorFullBodyLowerMuscles;
                ShouldApplyManualAnimatorFullBodyLowerMusclesOnly =
                    settings.ShouldApplyManualAnimatorFullBodyLowerMusclesOnly;
                ShouldApplyManualAnimatorFullBodyLegTwistMusclesOnly =
                    settings.ShouldApplyManualAnimatorFullBodyLegTwistMusclesOnly;
                manualAnimatorFullBodyPoseRightArmMusclesOnly =
                    settings.manualAnimatorFullBodyPoseRightArmMusclesOnly;
                manualAnimatorFullBodyPoseLeftArmMusclesOnly =
                    settings.manualAnimatorFullBodyPoseLeftArmMusclesOnly;
                manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly =
                    settings.manualAnimatorFullBodyPoseRightSleeveChainMusclesOnly;
                manualAnimatorFullBodyPoseFrameGateStart =
                    Mathf.Max(0f, settings.manualAnimatorFullBodyPoseFrameGateStart);
                manualAnimatorFullBodyPoseFrameGateEnd =
                    Mathf.Max(0f, settings.manualAnimatorFullBodyPoseFrameGateEnd);
                ShouldUseSetHumanPoseRightLegTwistOutputReference =
                    settings.ShouldUseSetHumanPoseRightLegTwistOutputReference;
                setHumanPoseRightLegTwistOutputReferenceWeight =
                    Mathf.Clamp01(settings.setHumanPoseRightLegTwistOutputReferenceWeight);
                setHumanPoseRightLegTwistOutputReferenceMaxDelta =
                    Mathf.Max(0f, settings.setHumanPoseRightLegTwistOutputReferenceMaxDelta);
                useManualAnimatorThumbLocalRotationReference = settings.useManualAnimatorThumbLocalRotationReference;
                useManualAnimatorHandLocalRotationReference = settings.useManualAnimatorHandLocalRotationReference;
                useManualAnimatorThumbSegmentDirectionReference = settings.useManualAnimatorThumbSegmentDirectionReference;
                manualAnimatorThumbSegmentDirectionWeight = settings.manualAnimatorThumbSegmentDirectionWeight;
                useManualAnimatorThumbHandDirectionReference = settings.useManualAnimatorThumbHandDirectionReference;
                manualAnimatorThumbHandDirectionWeight = settings.manualAnimatorThumbHandDirectionWeight;
                useManualAnimatorHandPalmFrameReference = settings.useManualAnimatorHandPalmFrameReference;
                manualAnimatorHandPalmFrameWeight = settings.manualAnimatorHandPalmFrameWeight;
                useManualAnimatorThumbBasePositionReference = settings.useManualAnimatorThumbBasePositionReference;
                ShouldUseManualAnimatorHipsLocalPositionReference = settings.ShouldUseManualAnimatorHipsLocalPositionReference;
                ShouldUseManualAnimatorBodyRotationReference = settings.ShouldUseManualAnimatorBodyRotationReference;
                manualAnimatorBodyRotationReferenceWeight = Mathf.Clamp01(settings.manualAnimatorBodyRotationReferenceWeight);
                ShouldUseManualAnimatorBodyPositionYReference = settings.ShouldUseManualAnimatorBodyPositionYReference;
                ShouldUseManualAnimatorBodyPositionXzReference = settings.ShouldUseManualAnimatorBodyPositionXzReference;
                manualAnimatorBodyPositionXzReferenceWeight =
                    Mathf.Clamp01(settings.manualAnimatorBodyPositionXzReferenceWeight);
                manualAnimatorBodyPositionXzReferenceMaxOffset =
                    Mathf.Max(0f, settings.manualAnimatorBodyPositionXzReferenceMaxOffset);
                manualAnimatorBodyPositionXzReferenceFrameGateStart =
                    Mathf.Max(0f, settings.manualAnimatorBodyPositionXzReferenceFrameGateStart);
                manualAnimatorBodyPositionXzReferenceFrameGateEnd =
                    Mathf.Max(0f, settings.manualAnimatorBodyPositionXzReferenceFrameGateEnd);
                manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames =
                    Mathf.Max(0f, settings.manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames);
                manualAnimatorBodyPositionXzReferenceAxisXScale =
                    Mathf.Clamp01(settings.manualAnimatorBodyPositionXzReferenceAxisXScale);
                manualAnimatorBodyPositionXzReferenceAxisZScale =
                    Mathf.Clamp01(settings.manualAnimatorBodyPositionXzReferenceAxisZScale);
                manualAnimatorHipsLocalPositionWeight = Mathf.Clamp01(settings.manualAnimatorHipsLocalPositionWeight);
                manualAnimatorHipsLocalPositionMaxOffset = Mathf.Max(0.001f, settings.manualAnimatorHipsLocalPositionMaxOffset);
                ShouldUseManualAnimatorFootHeightGroundingReference = settings.ShouldUseManualAnimatorFootHeightGroundingReference;
                manualAnimatorFootHeightGroundingReferenceWeight = Mathf.Clamp01(settings.manualAnimatorFootHeightGroundingReferenceWeight);
                manualAnimatorFootHeightGroundingReferenceMaxLift = Mathf.Max(0f, settings.manualAnimatorFootHeightGroundingReferenceMaxLift);
                ShouldUseManualAnimatorFootLocalRotationReference = settings.ShouldUseManualAnimatorFootLocalRotationReference;
                manualAnimatorFootLocalRotationReferenceWeight = Mathf.Clamp01(settings.manualAnimatorFootLocalRotationReferenceWeight);
                ShouldUseManualAnimatorLowerBodySegmentDirectionReference = settings.ShouldUseManualAnimatorLowerBodySegmentDirectionReference;
                manualAnimatorLowerBodySegmentDirectionReferenceWeight = Mathf.Clamp01(settings.manualAnimatorLowerBodySegmentDirectionReferenceWeight);
                manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle = Mathf.Max(0f, settings.manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle);
                ShouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference =
                    settings.ShouldDisableManualAnimatorUpperLegToLowerLegSegmentDirectionReference;
                manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle =
                    Mathf.Max(0f, settings.manualAnimatorUpperLegToLowerLegSegmentDirectionReferenceMaxAngle);
                ShouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference =
                    settings.ShouldDisableManualAnimatorLowerLegToFootSegmentDirectionReference;
                manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle =
                    Mathf.Max(0f, settings.manualAnimatorLowerLegToFootSegmentDirectionReferenceMaxAngle);
                manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle =
                    Mathf.Max(0f, settings.manualAnimatorLeftLowerLegToFootSegmentDirectionReferenceMaxAngle);
                manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle =
                    Mathf.Max(0f, settings.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceMaxAngle);
                manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale =
                    Mathf.Clamp01(settings.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceAxisXzScale);
                manualAnimatorRightLowerLegToFootSegmentDirectionReferenceBlendWeight =
                    Mathf.Clamp01(settings.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceBlendWeight);
                manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateStart =
                    Mathf.Max(0f, settings.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateStart);
                manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateEnd =
                    Mathf.Max(0f, settings.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceFrameGateEnd);
                manualAnimatorRightLowerLegToFootSegmentDirectionReferenceEndpointBlendWeight =
                    Mathf.Clamp01(settings.manualAnimatorRightLowerLegToFootSegmentDirectionReferenceEndpointBlendWeight);
                ShouldDisableManualAnimatorFootToToesSegmentDirectionReference =
                    settings.ShouldDisableManualAnimatorFootToToesSegmentDirectionReference;
                manualAnimatorFootToToesSegmentDirectionReferenceMaxAngle =
                    Mathf.Max(0f, settings.manualAnimatorFootToToesSegmentDirectionReferenceMaxAngle);
                ShouldUseManualAnimatorFootHipsAlignedResidualYawReference = settings.ShouldUseManualAnimatorFootHipsAlignedResidualYawReference;
                manualAnimatorFootHipsAlignedResidualYawReferenceWeight = Mathf.Clamp01(settings.manualAnimatorFootHipsAlignedResidualYawReferenceWeight);
                manualAnimatorFootHipsAlignedResidualYawReferenceMaxAngle = Mathf.Max(0f, settings.manualAnimatorFootHipsAlignedResidualYawReferenceMaxAngle);
                useManualAnimatorBipedIkFootPositionReference = settings.useManualAnimatorBipedIkFootPositionReference;
                manualAnimatorBipedIkFootPositionReferenceWeight = Mathf.Clamp01(settings.manualAnimatorBipedIkFootPositionReferenceWeight);
                manualAnimatorBipedIkFootPositionReferenceMaxOffset = Mathf.Max(0f, settings.manualAnimatorBipedIkFootPositionReferenceMaxOffset);
                usePostSetHumanPoseRightEndpointPositionReference =
                    settings.usePostSetHumanPoseRightEndpointPositionReference;
                postSetHumanPoseRightEndpointPositionReferenceWeight =
                    Mathf.Clamp01(settings.postSetHumanPoseRightEndpointPositionReferenceWeight);
                postSetHumanPoseRightEndpointPositionReferenceMaxOffset =
                    Mathf.Max(0f, settings.postSetHumanPoseRightEndpointPositionReferenceMaxOffset);
                postSetHumanPoseRightEndpointPositionReferencePositiveZScale =
                    Mathf.Clamp01(settings.postSetHumanPoseRightEndpointPositionReferencePositiveZScale);
                postSetHumanPoseRightEndpointPositionReferenceToesBlendWeight =
                    Mathf.Clamp01(settings.postSetHumanPoseRightEndpointPositionReferenceToesBlendWeight);
                postSetHumanPoseRightEndpointPositionReferenceFrameGateStart =
                    Mathf.Max(0f, settings.postSetHumanPoseRightEndpointPositionReferenceFrameGateStart);
                postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd =
                    Mathf.Max(0f, settings.postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd);
                ShouldUseLeftSideForPostSetHumanPoseEndpointPosition =
                    settings.ShouldUseLeftSideForPostSetHumanPoseEndpointPosition;
                usePostSetHumanPoseRightFootEvaluatorXzReference =
                    settings.usePostSetHumanPoseRightFootEvaluatorXzReference;
                postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude =
                    Mathf.Max(0f, settings.postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude);
                usePreSetHumanPoseRightEndpointPositionReference =
                    settings.usePreSetHumanPoseRightEndpointPositionReference;
                preSetHumanPoseRightEndpointPositionReferenceWeight =
                    Mathf.Clamp01(settings.preSetHumanPoseRightEndpointPositionReferenceWeight);
                preSetHumanPoseRightEndpointPositionReferenceMaxOffset =
                    Mathf.Max(0f, settings.preSetHumanPoseRightEndpointPositionReferenceMaxOffset);
                preSetHumanPoseRightEndpointPositionReferencePositiveZScale =
                    Mathf.Clamp01(settings.preSetHumanPoseRightEndpointPositionReferencePositiveZScale);
                preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight =
                    Mathf.Clamp01(settings.preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight);
                preSetHumanPoseRightEndpointPositionReferenceFrameGateStart =
                    Mathf.Max(0f, settings.preSetHumanPoseRightEndpointPositionReferenceFrameGateStart);
                preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd =
                    Mathf.Max(0f, settings.preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd);
                ShouldUseLeftSideForPreSetHumanPoseEndpointPosition =
                    settings.ShouldUseLeftSideForPreSetHumanPoseEndpointPosition;
                preSetHumanPoseEndpointPositionUseGhostCurrentBasis =
                    settings.preSetHumanPoseEndpointPositionUseGhostCurrentBasis;
                ShouldInvertPreSetHumanPoseEndpointPositionBodyX =
                    settings.ShouldInvertPreSetHumanPoseEndpointPositionBodyX;
                ShouldInvertPreSetHumanPoseEndpointPositionBodyZ =
                    settings.ShouldInvertPreSetHumanPoseEndpointPositionBodyZ;
                manualAnimatorThumbBasePositionWeight = settings.manualAnimatorThumbBasePositionWeight;
                manualAnimatorThumbBasePositionMaxOffset = settings.manualAnimatorThumbBasePositionMaxOffset;
                thumbSpreadMin = settings.ThumbSpreadMin;
                thumbSpreadMax = settings.ThumbSpreadMax;
                logThumbAnatomicalGuardCorrections = settings.logThumbAnatomicalGuardCorrections;
                enableThumbLocalRotationGuard = settings.EffectiveThumbLocalRotationGuard;
                thumbProximalMaxLocalAngle = settings.EffectiveThumbProximalMaxLocalAngle;
                thumbIntermediateMaxLocalAngle = settings.ThumbIntermediateMaxLocalAngle;
                thumbDistalMaxLocalAngle = settings.ThumbDistalMaxLocalAngle;
                logThumbLocalRotationGuardCorrections = settings.logThumbLocalRotationGuardCorrections;
                clampRootDeltaSpikes = settings.clampRetargetRootDeltaSpikes;
                maxRootDeltaPerFrame = settings.MaxRetargetRootDeltaPerFrame;
                logRootDeltaSpikes = settings.logRetargetRootDeltaSpikes;
                clampTargetHipsLocalPositionSpikes = settings.clampRetargetHipsLocalPositionSpikes;
                maxTargetHipsLocalPositionDeltaPerFrame = Mathf.Max(0.005f, settings.MaxRetargetHipsLocalPositionDeltaPerFrame);
                smoothGrounding = settings.smoothRetargetGrounding;
                maxGroundingVerticalStepPerFrame = Mathf.Max(0.001f, settings.MaxGroundingVerticalStepPerFrame);
                groundingSmoothing = Mathf.Clamp01(settings.GroundingSmoothing);
                groundingDeadZone = Mathf.Max(0f, settings.GroundingDeadZone);
                freezeRootYAfterInitialGrounding = settings.FreezeRootYAfterInitialGrounding;
                clampLegacyAnimationVisualStep = settings.clampRetargetVisualClipStep;
                legacyAnimationVisualFrameRate = Mathf.Clamp(settings.RetargetVisualClipFrameRate, 15f, 120f);
                smoothPoseOnLegacyAnimationStepSpike = settings.smoothRetargetPoseOnVisualStepSpike;
                poseVisualSpikeCurrentWeight = Mathf.Clamp(settings.RetargetPoseVisualSpikeCurrentWeight, 0.1f, 1f);
                poseVisualSpikeForearmStretchClampMaxOffset =
                    Mathf.Clamp01(settings.RetargetPoseVisualSpikeForearmStretchClampMaxOffset);
                poseVisualMuscleDeltaThreshold = Mathf.Clamp(settings.RetargetPoseVisualMuscleDeltaThreshold, 0.05f, 1f);
                rejectRendererGroundingOutliers = settings.rejectRendererGroundingOutliers;
                maxRendererFootGroundingSeparation = Mathf.Max(0.02f, settings.MaxRendererFootGroundingSeparation);
                smoothLateVisualGroundingCorrection = settings.smoothLateVisualGroundingCorrection;
                lateVisualGroundingSnapThreshold = Mathf.Max(0.005f, settings.LateVisualGroundingSnapThreshold);
                lateVisualGroundingSmoothing = Mathf.Clamp01(settings.LateVisualGroundingSmoothing);
                maxLateVisualGroundingStepPerFrame = Mathf.Max(0.001f, settings.MaxLateVisualGroundingStepPerFrame);
                ShouldLockTargetHumanoidBonePositions = settings.ShouldLockTargetHumanoidBonePositions;
            }

            _isInitialized = true;
            EnsureLateVisualGroundingCorrection();
            Debug.Log("[Master Stage] 시스템 초기화됨. 첫 Update 대기 중...");
        }

        private void OnDestroy()
        {
            _editorHumanoidPoseReferencePlayer?.Dispose();
#if UNITY_EDITOR
            DisposeEditorHumanoidFingerPoseReference();
#endif
            _legacyAnimationDriver.Dispose();
        }

#if UNITY_EDITOR
        public void ConfigureEditorHumanoidMuscleReference(
            AnimationClip referenceClip,
            bool shouldUseCompletePoseReference = false)
        {
            RetargetingEditorReferenceHooks.PoseReference?.ConfigureMuscleReference(this, referenceClip, shouldUseCompletePoseReference);
        }

        public void ConfigureEditorHumanoidRootTranslationReference(AnimationClip referenceClip)
        {
            RetargetingEditorReferenceHooks.PoseReference?.ConfigureRootTranslationReference(this, referenceClip);
        }

        public void ConfigureEditorHumanoidFingerPoseReference(
            GameObject referencePrefab,
            RuntimeAnimatorController referenceController,
            AnimationClip referenceClip,
            bool enableFingerPoseReference = true,
            bool enableFullBodyPoseReference = true,
            float fullBodyPoseReferenceWeight = 1f,
            bool fullBodyPoseExcludeLowerBodyMuscles = false,
            bool fullBodyPoseLowerBodyMusclesOnly = false,
            bool fullBodyPoseLegTwistMusclesOnly = false,
            bool fullBodyPoseRightArmMusclesOnly = false,
            bool fullBodyPoseLeftArmMusclesOnly = false,
            bool fullBodyPoseRightSleeveChainMusclesOnly = false,
            float fullBodyPoseFrameGateStart = 0f,
            float fullBodyPoseFrameGateEnd = 0f)
        {
            EditorFingerPoseReferenceOptions options = new EditorFingerPoseReferenceOptions
            {
                EnableFingerPoseReference = enableFingerPoseReference,
                EnableFullBodyPoseReference = enableFullBodyPoseReference,
                FullBodyPoseReferenceWeight = fullBodyPoseReferenceWeight,
                FullBodyPoseExcludeLowerBodyMuscles = fullBodyPoseExcludeLowerBodyMuscles,
                FullBodyPoseLowerBodyMusclesOnly = fullBodyPoseLowerBodyMusclesOnly,
                FullBodyPoseLegTwistMusclesOnly = fullBodyPoseLegTwistMusclesOnly,
                FullBodyPoseRightArmMusclesOnly = fullBodyPoseRightArmMusclesOnly,
                FullBodyPoseLeftArmMusclesOnly = fullBodyPoseLeftArmMusclesOnly,
                FullBodyPoseRightSleeveChainMusclesOnly = fullBodyPoseRightSleeveChainMusclesOnly,
                FullBodyPoseFrameGateStart = fullBodyPoseFrameGateStart,
                FullBodyPoseFrameGateEnd = fullBodyPoseFrameGateEnd,
            };
            RetargetingEditorReferenceHooks.PoseReference?.ConfigureFingerPoseReference(
                this,
                referencePrefab,
                referenceController,
                referenceClip,
                options);
        }

#endif

        // 빌드에서는 에디터 muscle 클립이 없어 임포트된 소스 모델을 HumanPoseHandler로 샘플링해 참조로 사용함.
        internal void ConfigureRuntimeHumanoidPoseReference(
            GameObject referenceSourceModel,
            AnimationClip referenceClip)
        {
            _editorHumanoidPoseReferencePlayer?.Dispose();
            _editorHumanoidPoseReferencePlayer = null;
            _useCompleteEditorHumanoidMuscleReference = false;
            _hasEditorHumanoidPoseReferenceForFrame = false;
            _hasEditorReferenceBodyPosition = false;

            if (referenceSourceModel == null || referenceClip == null)
            {
                return;
            }

            try
            {
                _editorHumanoidPoseReferencePlayer = new HumanoidPoseReferencePlayer();
                _editorHumanoidPoseReferencePlayer.InitializeFromSourceModel(
                    referenceSourceModel,
                    referenceClip);
                _useCompleteEditorHumanoidMuscleReference =
                    _editorHumanoidPoseReferencePlayer.IsInitialized;
            }
            catch (Exception exception)
            {
                _editorHumanoidPoseReferencePlayer?.Dispose();
                _editorHumanoidPoseReferencePlayer = null;
                Debug.LogWarning(
                    $"[PoseSpaceRetargeter] 런타임 Humanoid 포즈 참조를 준비하지 못했습니다: {exception.Message}");
            }
        }

        void LateUpdate()
        {
            if (!_isInitialized || ghostAnimator == null || targetAnimator == null || _ghostHandler == null || _targetHandler == null) return;

            if (_legacyAnimationDriver.Tick(
                Time.deltaTime,
                Application.isPlaying,
                clampLegacyAnimationVisualStep,
                legacyAnimationVisualFrameRate))
            {
                ResetVisualPoseHistory();
            }

            // 스케일 비율 계산 (매 프레임 체크하여 안정성 확보)
            // Container가 작동 중이라면 ghostHip.position.y는 ~0.8m 수준이어야 함.
            Transform ghostHip = ghostAnimator.GetBoneTransform(HumanBodyBones.Hips);
            Transform targetHip = targetAnimator.GetBoneTransform(HumanBodyBones.Hips);

            _scaleRatio = CalculateSafeScaleRatio(ghostHip, targetHip);

            // 포즈(근육) 동기화
            ResetRetargetPoseStageDiagnostics();
            _ghostHandler.GetHumanPose(ref _humanPose);
#if UNITY_EDITOR
            _endpointStageDiagnostics.GhostPositions = RetargetingEndpointStageDiagnostics.CapturePositions(ghostAnimator);
#endif
            if (!IsFinite(_humanPose))
            {
                LogPoseWarning("Ghost HumanPose contains non-finite values. Skipping this retarget frame.");
                RestoreTargetLocalScales();
                return;
            }

            RetargetingPoseInputTransformer.TransformInPlace(_humanPose.muscles);
#if UNITY_EDITOR
            if (_useEditorHumanoidMuscleReference &&
                _humanPose.muscles != null &&
                _editorHumanoidMuscleCurves.Count > 0)
            {
                RetargetingPoseInputTransformer.AlignWithReferenceCurvesInPlace(
                    _humanPose.muscles,
                    _editorHumanoidMuscleCurves,
                    _legacyAnimationDriver.CurrentTime);
            }
#endif
            _stageMuscleDiagnostics.PoseInput = _muscleIndexMap.ReadStageSnapshot(_humanPose);
            ApplyEditorHumanoidMuscleReference(ref _humanPose);
            _stageMuscleDiagnostics.AfterEditorMuscleReference = _muscleIndexMap.ReadStageSnapshot(_humanPose);
            ApplyEditorHumanoidFingerPoseReference(ref _humanPose);
            ApplyEditorHumanoidBodyRotationReference(ref _humanPose);
            if (ShouldApplyRetargetSafetyGuards())
            {
                ThumbMuscleGuardPolicy.ApplyAnatomicalGuard(this, ref _humanPose, ThumbMuscleGuardPolicy.ShouldApplyStretchOffset(this));
            }
            ClampPoseMuscles(ref _humanPose);
            _stageMuscleDiagnostics.AfterClampPoseMuscles = _muscleIndexMap.ReadStageSnapshot(_humanPose);
            ApplyAnatomicalArmGuard(ref _humanPose);
            _stageMuscleDiagnostics.AfterAnatomicalArmGuard = _muscleIndexMap.ReadStageSnapshot(_humanPose);
            SmoothPoseOnVisualSpike(ref _humanPose);
            _stageMuscleDiagnostics.AfterVisualSpikeSmoothing = _muscleIndexMap.ReadStageSnapshot(_humanPose);
            Quaternion poseRootRotation = _humanPose.bodyRotation;
            if (ShouldPreserveFbxRootRotation && !_hasPoseRootRotationCorrection && IsFinite(poseRootRotation) && _legacyAnimationDriver.IsPlaying)
            {
                _poseRootRotationCorrection = Quaternion.Inverse(poseRootRotation);
                _hasPoseRootRotationCorrection = true;
            }

            // Y축은 target 기준으로 안정화하고, X/Z 체중 이동은 FBX 값을 유지한다.
            Vector3 bodyPos = _humanPose.bodyPosition;
            bodyPos.x *= _scaleRatio;
            bodyPos.z *= _scaleRatio;
            Vector3 bodyRootMotionSource = bodyPos;
#if UNITY_EDITOR
            bodyRootMotionSource = RootMotionGuard.SelectBodyPositionRootMotionSource(
                bodyPos,
                _editorReferenceBodyPosition,
                _hasEditorReferenceBodyPosition,
                ShouldUseManualAnimatorBodyRotationReference);
#endif
            Vector3 bodyRootDelta = ExtractBodyPositionXZRootDelta(bodyRootMotionSource);
            if (preserveTargetBodyPosition && _hasTargetReferenceBodyPosition)
            {
                bodyPos = _targetReferenceBodyPosition;
                // 수동 기준 Animator의 bodyPos.y로 Y를 대체: ghost Legacy bodyPos 스파이크 없이 애니메이션 높이를 따른다.
                if (ShouldUseManualAnimatorBodyPositionYReference && _hasEditorReferenceBodyPosition)
                {
                    bodyPos.y = _editorReferenceBodyPosition.y;
                }
            }
            else
            {
                bodyPos.y *= _scaleRatio;
            }
#if UNITY_EDITOR
            float manualBodyPositionXzFrameGateWeight =
                ResolveManualAnimatorBodyPositionXzFrameGateWeight();
            if (ShouldUseManualAnimatorBodyPositionXzReference &&
                manualBodyPositionXzFrameGateWeight > 0f &&
                _hasEditorReferenceBodyPosition &&
                ManualPoseReferenceApplier.TryCalculateBodyPositionXzReference(
                    bodyPos,
                    _editorReferenceBodyPosition,
                    manualAnimatorBodyPositionXzReferenceWeight * manualBodyPositionXzFrameGateWeight,
                    manualAnimatorBodyPositionXzReferenceMaxOffset,
                    manualAnimatorBodyPositionXzReferenceAxisXScale,
                    manualAnimatorBodyPositionXzReferenceAxisZScale,
                    out Vector3 manualBodyPositionXz))
            {
                bodyPos = manualBodyPositionXz;
                if (!_editorBodyPositionXzReferenceLogged)
                {
                    Debug.Log(
                        $"[PoseSpaceRetargeter] Manual Animator bodyPosition X/Z reference applied. " +
                        $"weight={manualAnimatorBodyPositionXzReferenceWeight:F2}, " +
                        $"maxOffset={manualAnimatorBodyPositionXzReferenceMaxOffset:F3}m, " +
                        $"frameGate={manualAnimatorBodyPositionXzReferenceFrameGateStart:F0}-{manualAnimatorBodyPositionXzReferenceFrameGateEnd:F0}, " +
                        $"blendFrames={manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames:F0}, " +
                        $"axisScale={manualAnimatorBodyPositionXzReferenceAxisXScale:F2}/{manualAnimatorBodyPositionXzReferenceAxisZScale:F2}");
                    _editorBodyPositionXzReferenceLogged = true;
                }
            }
#endif
            if (!IsFinite(bodyPos))
            {
                LogPoseWarning("Retarget body position became non-finite. Skipping this retarget frame.");
                RestoreTargetLocalScales();
                return;
            }
            _humanPose.bodyPosition = bodyPos;

            Vector3 rootMotionCarrierPositionBeforePose = targetAnimator.transform.position;
            Vector3 poseSolveRootPosition = RootMotionGuard.SelectPoseSolveRootPosition(
                rootMotionCarrierPositionBeforePose,
                _hasTargetRootPoseGuardAnchorPosition ? _targetRootPoseGuardAnchorPosition : rootMotionCarrierPositionBeforePose,
                useBodyPositionXZRootMotion);
            if (IsFinite(poseSolveRootPosition))
            {
                targetAnimator.transform.position = poseSolveRootPosition;
            }

            Vector3 targetPositionBeforePose = targetAnimator.transform.position;
#if UNITY_EDITOR
            _setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions = RetargetingEndpointStageDiagnostics.CapturePositions(targetAnimator);
            RetargetingEditorReferenceHooks.FootReference?.ApplyPreSetSignCorrectedRowLocalBodyPositionReference(this, ref _humanPose);
            if (!preSetHumanPoseEndpointPositionUseGhostCurrentBasis)
            {
                RetargetingEditorReferenceHooks.FootReference?.ApplyPreSetEndpointPositionReference(this);
            }
#endif
            _setHumanPoseDiagnostics.CaptureInput(ReadSetHumanPoseInputSnapshot(_humanPose));
            CaptureSetHumanPosePreSolveBasisDiagnostics(_humanPose);
            _targetHandler.SetHumanPose(ref _humanPose);
            ApplySetHumanPoseRightLegTwistOutputReference(_humanPose);
            _setHumanPoseDiagnostics.CaptureOutput(ReadSetHumanPoseOutputSnapshot());
#if UNITY_EDITOR
            _endpointStageDiagnostics.AfterSetHumanPosePositions = RetargetingEndpointStageDiagnostics.CapturePositions(targetAnimator);
#endif
            ClampAppliedTargetPose();
#if UNITY_EDITOR
            ApplyEditorHumanoidBoneLocalRotationReference();
#endif
            RestoreTargetHumanoidLocalPositions();
#if UNITY_EDITOR
            RetargetingEditorReferenceHooks.FootReference?.ApplyHipsLocalPositionReference(this);
            RetargetingEditorReferenceHooks.FootReference?.ApplyFootLocalRotationReference(this);
            RetargetingEditorReferenceHooks.LowerBodyDirection?.Apply(this);
            RetargetingEditorReferenceHooks.FootReference?.ApplyFootHipsAlignedResidualYawReference(this);
            RetargetingEditorReferenceHooks.FootReference?.ApplyPostSetEndpointPositionReference(this);
            RetargetingEditorReferenceHooks.ThumbReference?.ApplyThumbBasePositionReference(this);
#endif
#if UNITY_EDITOR
            _endpointStageDiagnostics.AfterManualReferencesPositions = RetargetingEndpointStageDiagnostics.CapturePositions(targetAnimator);
#endif
            ClampTargetHipsLocalPositionSpike();
            ThumbGuardPolicy.ClampTargetLocalRotations(this);
#if UNITY_EDITOR
            RetargetingEditorReferenceHooks.ThumbReference?.ApplyThumbLocalRotationReference(this);
            RetargetingEditorReferenceHooks.ThumbReference?.ApplyHandPalmFrameReference(this);
            RetargetingEditorReferenceHooks.FootReference?.ApplyHandLocalRotationReference(this);
            RetargetingEditorReferenceHooks.ThumbReference?.ApplyThumbSegmentDirectionReference(this);
            RetargetingEditorReferenceHooks.ThumbReference?.ApplyThumbHandDirectionReference(this);
            ApplyRightSleeveSilhouetteLocalOffsetReference();
#endif
            ClampTargetRootPositionSpike(targetPositionBeforePose, "SetHumanPose");
            Vector3 implicitRootGuardReference = RootMotionGuard.SelectImplicitRootGuardReference(
                _hasTargetRootPoseGuardAnchorPosition ? _targetRootPoseGuardAnchorPosition : targetPositionBeforePose,
                targetPositionBeforePose,
                _movementScaleMultiplier);
            targetAnimator.transform.position = RootMotionGuard.ApplyImplicitBodyPositionRootGuard(
                implicitRootGuardReference,
                targetAnimator.transform.position,
                useBodyPositionXZRootMotion,
                bodyRootDelta);
            targetAnimator.transform.position = RootMotionGuard.RestoreRootMotionCarrierPositionAfterPose(
                rootMotionCarrierPositionBeforePose,
                targetAnimator.transform.position,
                useBodyPositionXZRootMotion);
#if UNITY_EDITOR
            _endpointStageDiagnostics.AfterRootRestorePositions = RetargetingEndpointStageDiagnostics.CapturePositions(targetAnimator);
#endif

            // 월드 회전 동기화 (180도 문제 해결)
            if (ShouldPreserveFbxRootRotation && _hasPoseRootRotationCorrection && IsFinite(poseRootRotation))
            {
                Quaternion correctedRootRotation = _poseRootRotationCorrection * poseRootRotation;
                if (IsFinite(correctedRootRotation))
                {
                    targetAnimator.transform.rotation = correctedRootRotation;
                }
            }
            else if (!ShouldPreserveFbxRootRotation && fixReverseRotation)
            {
                // Ghost 회전 * 180도 보정
                targetAnimator.transform.rotation = ghostAnimator.transform.rotation * _facingCorrection;
            }
            else if (!ShouldPreserveFbxRootRotation)
            {
                targetAnimator.transform.rotation = ghostAnimator.transform.rotation;
            }

            // 루트 모션 동기화 (호 그리기 방지)
            // Ghost 이동량 계산
            Vector3 ghostDelta = ghostAnimator.transform.position - _prevGhostPos;
            Vector3 editorRootTranslationDelta = ExtractEditorRootTranslationDelta(ghostDelta);

            // 내 캐릭터 크기에 맞춰 이동량 스케일링
            Vector3 targetDelta = RootMotionGuard.CalculateRetargetRootDelta(
                ghostDelta,
                _scaleRatio,
                editorRootTranslationDelta,
                bodyRootDelta,
                _movementScaleMultiplier,
                useBodyPositionXZRootMotion,
                clampRootDeltaSpikes,
                maxRootDeltaPerFrame,
                out float targetDeltaMagnitude,
                out bool skippedByNonFinite,
                out bool limitedBySpike);
            if (skippedByNonFinite)
            {
                LogPoseWarning("Retarget root delta became non-finite. Skipping root motion for this frame.");
                _lastRootDeltaMagnitude = float.NaN;
                _rootDeltaSpikeSkippedCount++;
            }
            else
            {
                _lastRootDeltaMagnitude = targetDeltaMagnitude;
                _maxRootDeltaMagnitude = Mathf.Max(_maxRootDeltaMagnitude, _lastRootDeltaMagnitude);

                if (limitedBySpike)
                {
                    _rootDeltaSpikeSkippedCount++;
                    if (logRootDeltaSpikes && !_rootDeltaSpikeWarningLogged)
                    {
                        Debug.LogWarning($"[PoseSpaceRetargeter] Root delta spike {_lastRootDeltaMagnitude:F3}m limited. ghostDelta={ghostDelta.magnitude:F3}m, editorRootDelta={editorRootTranslationDelta.magnitude:F3}m, limit={maxRootDeltaPerFrame:F3}m");
                        _rootDeltaSpikeWarningLogged = true;
                    }
                }
            }

            // 이동 적용
            targetAnimator.transform.position += targetDelta;
#if UNITY_EDITOR
            _endpointStageDiagnostics.AfterRootDeltaPositions = RetargetingEndpointStageDiagnostics.CapturePositions(targetAnimator);
#endif

            // 위치 갱신
            _prevGhostPos = ghostAnimator.transform.position;

            // 스마트 접지 (Raycast Grounding) - 공중 부양 해결
            if (useSmartGrounding)
            {
                _grounding.ApplyRaycastGrounding(this);
            }
#if UNITY_EDITOR
            _endpointStageDiagnostics.AfterGroundingPositions = RetargetingEndpointStageDiagnostics.CapturePositions(targetAnimator);
#endif

            RestoreTargetLocalScales();
#if UNITY_EDITOR
            RetargetingEditorReferenceHooks.FootReference?.ApplyBipedIkFootPositionReference(this);
            _endpointStageDiagnostics.AfterBipedIKPositions = RetargetingEndpointStageDiagnostics.CapturePositions(targetAnimator);
            _endpointStageDiagnostics.CaptureAttribution();
#endif
        }

        private void SmoothPoseOnVisualSpike(ref HumanPose pose)
        {
            if (!smoothPoseOnLegacyAnimationStepSpike || pose.muscles == null || pose.muscles.Length == 0)
            {
                RememberVisualPose(pose);
                return;
            }

            if (!_hasPreviousVisualPose ||
                _previousVisualPoseMuscles == null ||
                _previousVisualPoseMuscles.Length != pose.muscles.Length)
            {
                RememberVisualPose(pose);
                return;
            }

            float maxMuscleDelta = 0f;
            for (int i = 0; i < pose.muscles.Length; i++)
            {
                float delta = Mathf.Abs(pose.muscles[i] - _previousVisualPoseMuscles[i]);
                if (delta > maxMuscleDelta)
                {
                    maxMuscleDelta = delta;
                }
            }

            _lastPoseVisualMaxMuscleDelta = maxMuscleDelta;
            _maxPoseVisualMaxMuscleDelta = Mathf.Max(_maxPoseVisualMaxMuscleDelta, maxMuscleDelta);

            float bodyPositionDelta = Vector3.Distance(_previousVisualPoseBodyPosition, pose.bodyPosition);
            float bodyRotationDelta = Quaternion.Angle(_previousVisualPoseBodyRotation, pose.bodyRotation);
            bool shouldSmooth = RetargetingPoseSmoothing.ShouldSmoothVisualPoseSpike(
                maxMuscleDelta,
                bodyPositionDelta,
                bodyRotationDelta,
                poseVisualMuscleDeltaThreshold,
                _legacyAnimationDriver.StepSpikeThisFrame,
                out bool muscleDeltaOnlySpike);

            if (shouldSmooth)
            {
                float currentWeight = RetargetingPoseSmoothing.CalculateVisualPoseSpikeCurrentWeight(
                    poseVisualSpikeCurrentWeight,
                    bodyPositionDelta,
                    bodyRotationDelta,
                    _legacyAnimationDriver.StepSpikeThisFrame);
                bool useEditorHumanoidMuscleReference = false;
                bool useCompleteEditorHumanoidMuscleReference = false;
                bool hasEditorHumanoidBodyRotationReference = false;
#if UNITY_EDITOR
                useEditorHumanoidMuscleReference = _useEditorHumanoidMuscleReference;
                useCompleteEditorHumanoidMuscleReference = _useCompleteEditorHumanoidMuscleReference;
                hasEditorHumanoidBodyRotationReference =
                    _hasEditorHumanoidPoseReferenceForFrame;
#else
                useCompleteEditorHumanoidMuscleReference = _useCompleteEditorHumanoidMuscleReference;
                hasEditorHumanoidBodyRotationReference =
                    _hasEditorHumanoidPoseReferenceForFrame;
#endif
                for (int i = 0; i < pose.muscles.Length; i++)
                {
                    bool hasEditorHumanoidMuscleReferenceCurve = false;
#if UNITY_EDITOR
                    hasEditorHumanoidMuscleReferenceCurve = _editorHumanoidMuscleCurves.ContainsKey(i);
#endif
                    bool shouldPreserveCurrentValue = RetargetingMuscleReferencePolicy.ShouldPreserveHumanoidMuscleDuringVisualSmoothing(
                        i,
                        useEditorHumanoidMuscleReference,
                        hasEditorHumanoidMuscleReferenceCurve,
                        useCompleteEditorHumanoidMuscleReference);
                    bool isForearmStretchMuscle = !shouldPreserveCurrentValue &&
                        poseVisualSpikeForearmStretchClampMaxOffset > 0f &&
                        RetargetingMuscleReferencePolicy.IsForearmStretchMuscle(i);
                    pose.muscles[i] = RetargetingPoseSmoothing.BlendVisualPoseSpikeMuscle(
                        _previousVisualPoseMuscles[i],
                        pose.muscles[i],
                        currentWeight,
                        shouldPreserveCurrentValue,
                        isForearmStretchMuscle,
                        poseVisualSpikeForearmStretchClampMaxOffset);
                }

                pose.bodyPosition = Vector3.Lerp(_previousVisualPoseBodyPosition, pose.bodyPosition, currentWeight);
                if (!RetargetingMuscleReferencePolicy.ShouldPreserveBodyRotationDuringVisualSmoothing(
                    hasEditorHumanoidBodyRotationReference))
                {
                    pose.bodyRotation = Quaternion.Slerp(
                        _previousVisualPoseBodyRotation,
                        pose.bodyRotation,
                        currentWeight);
                }
                _poseVisualSmoothingCount++;
            }
            else if (muscleDeltaOnlySpike)
            {
                // 빠른 손/팔 동작 자체를 smoothing하면 의도한 동작이 멈칫하고 몸통이 늦게 따라오는 것처럼 보인다.
                _poseVisualMuscleDeltaOnlySkippedCount++;
            }

            RememberVisualPose(pose);
        }

        private void RememberVisualPose(HumanPose pose)
        {
            if (pose.muscles == null || pose.muscles.Length == 0 || !IsFinite(pose))
            {
                return;
            }

            if (_previousVisualPoseMuscles == null || _previousVisualPoseMuscles.Length != pose.muscles.Length)
            {
                _previousVisualPoseMuscles = new float[pose.muscles.Length];
            }

            Array.Copy(pose.muscles, _previousVisualPoseMuscles, pose.muscles.Length);
            _previousVisualPoseBodyPosition = pose.bodyPosition;
            _previousVisualPoseBodyRotation = pose.bodyRotation;
            _hasPreviousVisualPose = true;
        }

        private void ResetVisualPoseHistory()
        {
            _hasPreviousVisualPose = false;
            _previousVisualPoseBodyPosition = Vector3.zero;
            _previousVisualPoseBodyRotation = Quaternion.identity;
        }

        private void ApplyEditorHumanoidMuscleReference(ref HumanPose pose)
        {
#if UNITY_EDITOR
            ApplyEditorHumanoidMuscleReferenceEditor(ref pose);
#else
            ApplyRuntimeHumanoidPoseReference(ref pose);
#endif
        }

#if !UNITY_EDITOR
        private void ApplyRuntimeHumanoidPoseReference(ref HumanPose pose)
        {
            _hasEditorHumanoidPoseReferenceForFrame = false;
            if (pose.muscles == null ||
                !_useCompleteEditorHumanoidMuscleReference ||
                _editorHumanoidPoseReferencePlayer == null ||
                !_editorHumanoidPoseReferencePlayer.TryEvaluateAt(
                    _legacyAnimationDriver.CurrentTime,
                    ref _editorHumanoidPoseReference))
            {
                return;
            }

            int muscleCount = Mathf.Min(
                pose.muscles.Length,
                _editorHumanoidPoseReference.muscles.Length);
            Array.Copy(
                _editorHumanoidPoseReference.muscles,
                pose.muscles,
                muscleCount);
            pose.bodyRotation = _editorHumanoidPoseReference.bodyRotation;
            _editorReferenceBodyPosition = _editorHumanoidPoseReference.bodyPosition;
            _hasEditorReferenceBodyPosition = IsFinite(_editorReferenceBodyPosition);
            _hasEditorHumanoidPoseReferenceForFrame = true;
        }
#endif

        private void ApplyEditorHumanoidFingerPoseReference(ref HumanPose pose)
        {
#if UNITY_EDITOR
            ApplyEditorHumanoidFingerPoseReferenceEditor(ref pose);
#endif
        }

        private void ApplyEditorHumanoidBodyRotationReference(ref HumanPose pose)
        {
#if UNITY_EDITOR
            ApplyEditorHumanoidBodyRotationReferenceEditor(ref pose);
#endif
        }

        private void ResetLastEditorHipsLocalReferenceDiagnostics()
        {
            _footReferenceDiagnostics.ResetHipsLocal();
        }

        private void ResetRecordingStartHipsBaselineDiagnostics()
        {
            _recordingStartRootY = float.NaN;
            _recordingStartBodyPositionY = float.NaN;
            _recordingStartHipsLocalY = float.NaN;
            _recordingStartHipsY = float.NaN;
            _recordingStartHipsReferenceBeforeLocalY = float.NaN;
            _recordingStartHipsReferenceAfterLocalY = float.NaN;
            _recordingStartHipsReferenceDeltaY = float.NaN;
            _recordingStartHipsReferenceFlipDetected = false;
            _recordingStartHipsReferenceStage = string.Empty;
        }

        private bool TryGetTargetBodyPositionY(out float bodyPositionY)
        {
            bodyPositionY = float.NaN;
            if (_targetHandler == null)
            {
                return false;
            }

            HumanPose targetPose = new HumanPose();
            _targetHandler.GetHumanPose(ref targetPose);
            if (!IsFinite(targetPose.bodyPosition))
            {
                return false;
            }

            bodyPositionY = targetPose.bodyPosition.y;
            return true;
        }

        private static bool IsRecordingStartHipsBaselineFlip(float beforeLocalY, float afterLocalY, float warningThreshold)
        {
            if (!IsFinite(beforeLocalY) || !IsFinite(afterLocalY) || !IsFinite(warningThreshold))
            {
                return false;
            }

            return Mathf.Abs(afterLocalY - beforeLocalY) > Mathf.Max(0f, warningThreshold);
        }

        internal bool ShouldSuppressCompetingManualThumbOverride(bool leftHand)
        {
#if UNITY_EDITOR
            return RetargetingEditorReferenceHooks.ThumbReference?.ShouldSuppressCompetingManualThumbOverride(this, leftHand) ?? false;
#else
            return false;
#endif
        }

        internal bool ShouldKeepDetachedHelperManualThumbOverrides(bool leftHand)
        {
#if UNITY_EDITOR
            return RetargetingEditorReferenceHooks.ThumbReference?.ShouldKeepDetachedHelperManualThumbOverrides(this, leftHand) ?? false;
#else
            return false;
#endif
        }

        public bool TryGetHighRiskManualThumbPoseConstraintOverrides(
            bool leftHand,
            out float projectionMin,
            out float projectionMax,
            out float maxSpreadAngle)
        {
#if UNITY_EDITOR
            projectionMin = 0f;
            projectionMax = 0f;
            maxSpreadAngle = 0f;
            return RetargetingEditorReferenceHooks.ThumbReference?.TryGetHighRiskManualThumbPoseConstraintOverrides(
                this,
                leftHand,
                out projectionMin,
                out projectionMax,
                out maxSpreadAngle) ?? false;
#else
            projectionMin = 0f;
            projectionMax = 0f;
            maxSpreadAngle = 0f;
            return false;
#endif
        }

        public string BuildThumbHelperRelationshipDebugSummary(bool leftHand)
        {
#if UNITY_EDITOR
            return RetargetingEditorReferenceHooks.ThumbReference?.BuildThumbHelperRelationshipDebugSummary(this, leftHand);
#else
            return leftHand
                ? "side=L, state=editor-only"
                : "side=R, state=editor-only";
#endif
        }

        internal Transform GetCachedThumbBaseHelper(bool leftHand)
        {
#if UNITY_EDITOR
            return RetargetingEditorReferenceHooks.ThumbReference?.GetCachedThumbBaseHelper(this, leftHand);
#else
            return null;
#endif
        }

        internal Transform GetCachedExplicitThumbBaseSource(bool leftHand)
        {
#if UNITY_EDITOR
            return RetargetingEditorReferenceHooks.ThumbReference?.GetCachedExplicitThumbBaseSource(this, leftHand);
#else
            return null;
#endif
        }

        internal bool TryEvaluateThumbManualReferenceFrameDeviation(
            bool leftHand,
            Transform targetThumb,
            Quaternion candidateWorldRotation,
            out float currentDeviation,
            out float candidateDeviation)
        {
#if UNITY_EDITOR
            currentDeviation = float.NaN;
            candidateDeviation = float.NaN;
            return RetargetingEditorReferenceHooks.ThumbReference?.TryEvaluateManualReferenceFrameDeviation(
                this,
                leftHand,
                targetThumb,
                candidateWorldRotation,
                out currentDeviation,
                out candidateDeviation) ?? false;
#else
            currentDeviation = float.NaN;
            candidateDeviation = float.NaN;
            return false;
#endif
        }

        internal bool TryEvaluateThumbLocalRotationOverrideRisk(
            bool leftHand,
            Transform targetThumb,
            Quaternion candidateRotation,
            out float risk)
        {
#if UNITY_EDITOR
            risk = float.NaN;
            return RetargetingEditorReferenceHooks.ThumbReference?.TryEvaluateThumbLocalRotationOverrideRisk(this, leftHand, targetThumb, candidateRotation, out risk) ?? false;
#else
            risk = float.NaN;
            return false;
#endif
        }

#if UNITY_EDITOR
        private void ApplyEditorHumanoidMuscleReferenceEditor(ref HumanPose pose)
        {
            RetargetingEditorReferenceHooks.PoseReference?.ApplyMuscleReference(this, ref pose);
        }

        private void ApplyEditorHumanoidFingerPoseReferenceEditor(ref HumanPose pose)
        {
            RetargetingEditorReferenceHooks.PoseReference?.ApplyFingerPoseReference(this, ref pose);
        }

        private void ApplyEditorHumanoidBodyRotationReferenceEditor(ref HumanPose pose)
        {
            RetargetingEditorReferenceHooks.PoseReference?.ApplyBodyRotationReference(this, ref pose);
        }

        internal void CaptureTargetFootPositions(out Vector3 leftFootPosition, out Vector3 rightFootPosition)
        {
            leftFootPosition = ReadTargetBoneWorldPosition(HumanBodyBones.LeftFoot);
            rightFootPosition = ReadTargetBoneWorldPosition(HumanBodyBones.RightFoot);
        }

        internal Vector3 ReadTargetBoneWorldPosition(HumanBodyBones bone)
        {
            if (targetAnimator == null)
            {
                return BuildNaNVector3();
            }

            Transform targetBone = targetAnimator.GetBoneTransform(bone);
            return targetBone != null ? targetBone.position : BuildNaNVector3();
        }

        internal float CalculateTargetFootXzDelta(Vector3 beforePosition, HumanBodyBones footBone)
        {
            Vector3 afterPosition = ReadTargetBoneWorldPosition(footBone);
            if (!IsFinite(beforePosition) || !IsFinite(afterPosition))
            {
                return float.NaN;
            }

            Vector2 beforeXz = new Vector2(beforePosition.x, beforePosition.z);
            Vector2 afterXz = new Vector2(afterPosition.x, afterPosition.z);
            return Vector2.Distance(beforeXz, afterXz);
        }


        internal Vector3 ReadTargetBoneWorldForward(HumanBodyBones bone)
        {
            Transform targetBone = targetAnimator != null ? targetAnimator.GetBoneTransform(bone) : null;
            return targetBone != null && IsFinite(targetBone.forward) ? targetBone.forward : BuildNaNVector3();
        }

        internal Vector3 ReadTargetBoneWorldUp(HumanBodyBones bone)
        {
            Transform targetBone = targetAnimator != null ? targetAnimator.GetBoneTransform(bone) : null;
            return targetBone != null && IsFinite(targetBone.up) ? targetBone.up : BuildNaNVector3();
        }

        internal bool ShouldApplyPostSetHumanPoseRightEndpointPositionFrameGate()
        {
            return ShouldApplyManualReferenceFrameGate(
                postSetHumanPoseRightEndpointPositionReferenceFrameGateStart,
                postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd);
        }

        internal bool ShouldApplyPreSetHumanPoseRightEndpointPositionFrameGate()
        {
            return ShouldApplyManualReferenceFrameGate(
                preSetHumanPoseRightEndpointPositionReferenceFrameGateStart,
                preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd);
        }

        internal bool ShouldApplyManualReferenceFrameGate(float startFrame, float endFrame)
        {
            if (!ManualPoseReferenceApplier.HasActiveFrameGate(startFrame, endFrame))
            {
                return true;
            }

            float frameRate = Mathf.Clamp(legacyAnimationVisualFrameRate, 15f, 120f);
            int currentFrame = Mathf.RoundToInt(_legacyAnimationDriver.CurrentTime * frameRate);
            return ManualPoseReferenceApplier.IsFrameWithinGate(
                currentFrame,
                startFrame,
                endFrame);
        }

        internal float ResolveManualAnimatorBodyPositionXzFrameGateWeight()
        {
            float start = Mathf.Max(0f, manualAnimatorBodyPositionXzReferenceFrameGateStart);
            float end = Mathf.Max(0f, manualAnimatorBodyPositionXzReferenceFrameGateEnd);
            float frameRate = Mathf.Clamp(legacyAnimationVisualFrameRate, 15f, 120f);
            float currentFrame = Mathf.RoundToInt(_legacyAnimationDriver.CurrentTime * frameRate);
            return ManualPoseReferenceApplier.CalculateBodyPositionXzFrameGateWeight(
                currentFrame,
                start,
                end,
                Mathf.Max(0f, manualAnimatorBodyPositionXzReferenceFrameGateBlendFrames));
        }

        internal static bool TryNormalize(Vector3 value, out Vector3 normalized)
        {
            normalized = Vector3.zero;
            if (!IsFinite(value) || value.sqrMagnitude <= 0.00000001f)
            {
                return false;
            }

            normalized = value.normalized;
            return IsFinite(normalized);
        }

        private void DisposeEditorHumanoidFingerPoseReference()
        {
            RetargetingEditorReferenceHooks.PoseReference?.DisposeFingerPoseReference(this);
        }

        private bool ShouldApplyRightSleeveSilhouetteLocalOffsetFrameGate()
        {
            return RetargetingEditorReferenceHooks.SleeveReference?.ShouldApplyRightSleeveSilhouetteLocalOffsetFrameGate(this) ?? false;
        }

        internal bool ShouldApplySingleFrameFallbackReferenceFrameGate(float startFrame, float endFrame)
        {
            if (!ManualPoseReferenceApplier.HasConfiguredFrameGate(startFrame, endFrame))
            {
                return true;
            }

            float frameRate = Mathf.Clamp(legacyAnimationVisualFrameRate, 1f, 240f);
            int currentFrame = Mathf.RoundToInt(_legacyAnimationDriver.CurrentTime * frameRate);
            return ManualPoseReferenceApplier.IsFrameWithinSingleFrameFallbackGate(
                currentFrame,
                startFrame,
                endFrame);
        }

        private void ApplyRightSleeveSilhouetteLocalOffsetReference()
        {
            RetargetingEditorReferenceHooks.SleeveReference?.ApplyRightSleeveSilhouetteLocalOffsetReference(this);
        }

        private void ApplyRightSleeveSilhouetteLocalOffsetToTransform(Transform target, Vector3 offset)
        {
            RetargetingEditorReferenceHooks.SleeveReference?.ApplyRightSleeveSilhouetteLocalOffsetToTransform(this, target, offset);
        }

        private void RestoreRightSleeveSilhouetteLocalOffsetReference()
        {
            RetargetingEditorReferenceHooks.SleeveReference?.RestoreRightSleeveSilhouetteLocalOffsetReference(this);
        }

#endif

        private float CalculateSafeScaleRatio(Transform ghostHip, Transform targetHip)
        {
            bool hasAnimatorScale = ghostAnimator != null && targetAnimator != null;
            float ghostHumanScale = hasAnimatorScale ? ghostAnimator.humanScale : 0f;
            float targetHumanScale = hasAnimatorScale ? targetAnimator.humanScale : 0f;
            bool hasHipPositions = ghostHip != null && targetHip != null;
            float ghostHipY = hasHipPositions ? ghostHip.position.y : 0f;
            float targetHipY = hasHipPositions ? targetHip.position.y : 0f;

            float ratio = RetargetingScaleRatioCalculator.CalculateSafeScaleRatio(
                _scaleRatio,
                hasAnimatorScale,
                ghostHumanScale,
                targetHumanScale,
                _initialGhostHipHeight,
                _initialTargetHipHeight,
                hasHipPositions,
                ghostHipY,
                targetHipY,
                out bool usedInvalidFallback);
            if (usedInvalidFallback)
            {
                LogPoseWarning("Invalid retarget scale ratio. Falling back to 1.0.");
            }

            return ratio;
        }

        private void CacheInitialHipHeights()
        {
            Transform ghostHip = ghostAnimator != null ? ghostAnimator.GetBoneTransform(HumanBodyBones.Hips) : null;
            Transform targetHip = targetAnimator != null ? targetAnimator.GetBoneTransform(HumanBodyBones.Hips) : null;

            if (ghostHip != null && IsFinite(ghostHip.position.y) && Mathf.Abs(ghostHip.position.y) > 0.01f)
            {
                _initialGhostHipHeight = Mathf.Abs(ghostHip.position.y);
            }

            if (targetHip != null && IsFinite(targetHip.position.y) && Mathf.Abs(targetHip.position.y) > 0.01f)
            {
                _initialTargetHipHeight = Mathf.Abs(targetHip.position.y);
            }
        }

        private void CacheTargetReferenceBodyPosition()
        {
            _hasTargetReferenceBodyPosition = false;
            _hasPreviousBodyRootMotionPosition = false;

            if (_targetHandler == null)
            {
                return;
            }

            var targetPose = new HumanPose();
            _targetHandler.GetHumanPose(ref targetPose);
            if (!IsFinite(targetPose.bodyPosition))
            {
                return;
            }

            _targetReferenceBodyPosition = targetPose.bodyPosition;
            _hasTargetReferenceBodyPosition = true;
        }

        private Vector3 ExtractBodyPositionXZRootDelta(Vector3 bodyPosition)
        {
            if (!useBodyPositionXZRootMotion || !_hasTargetReferenceBodyPosition)
            {
                _hasPreviousBodyRootMotionPosition = false;
                return Vector3.zero;
            }

            Vector3 current = new Vector3(bodyPosition.x, 0f, bodyPosition.z);
            if (!IsFinite(current))
            {
                _hasPreviousBodyRootMotionPosition = false;
                return Vector3.zero;
            }

            if (!_hasPreviousBodyRootMotionPosition)
            {
                _previousBodyRootMotionPosition = current;
                _hasPreviousBodyRootMotionPosition = true;
                return Vector3.zero;
            }

            Vector3 delta = current - _previousBodyRootMotionPosition;
            _previousBodyRootMotionPosition = current;
            if (!IsFinite(delta))
            {
                return Vector3.zero;
            }

            delta.y = 0f;
            return delta;
        }

        private Vector3 ExtractEditorRootTranslationDelta(Vector3 ghostDelta)
        {
#if UNITY_EDITOR
            return RetargetingEditorReferenceHooks.PoseReference?.ExtractRootTranslationDelta(this, ghostDelta) ?? Vector3.zero;
#else
            return Vector3.zero;
#endif
        }

        private void ResetEditorHumanoidRootTranslationReferenceState()
        {
#if UNITY_EDITOR
            RetargetingEditorReferenceHooks.PoseReference?.ResetRootTranslationState(this);
#endif
        }

#if UNITY_EDITOR
#endif

        private static Vector3 FlattenXZ(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        internal void LogPoseWarning(string message)
        {
            if (_poseWarningLogged)
            {
                return;
            }

            Debug.LogWarning($"[PoseSpaceRetargeter] {message}");
            _poseWarningLogged = true;
        }

        internal static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        internal static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        internal static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(HumanPose pose)
        {
            if (!IsFinite(pose.bodyPosition) || !IsFinite(pose.bodyRotation))
            {
                return false;
            }

            if (pose.muscles == null)
            {
                return true;
            }

            foreach (float muscle in pose.muscles)
            {
                if (!IsFinite(muscle))
                {
                    return false;
                }
            }

            return true;
        }

        private void ResetRetargetPoseStageDiagnostics()
        {
            _stageMuscleDiagnostics.Reset();
            _setHumanPoseDiagnostics.Input.ClearCore();
            _setHumanPoseDiagnostics.Output.ClearCore();
            _setHumanPoseDiagnostics.ResetPreSolveBasis();
#if UNITY_EDITOR
            _footReferenceDiagnostics.ResetPreSetEndpoint();
#endif
            _setHumanPoseDiagnostics.ResetExtendedMuscles();
            _footReferenceDiagnostics.ResetXzDeltas();
            _lowerBodySegmentDiagnostics.ResetFootXzDeltas();
#if UNITY_EDITOR
            _lowerBodySegmentDiagnostics.ResetDetailed();
#endif
#if UNITY_EDITOR
            _postSetEndpointDiagnostics.Reset();
#endif
            _endpointStageDiagnostics.GhostPositions = RetargetEndpointStageWorldPositions.Empty;
            _endpointStageDiagnostics.AfterSetHumanPosePositions = RetargetEndpointStageWorldPositions.Empty;
            _endpointStageDiagnostics.AfterManualReferencesPositions = RetargetEndpointStageWorldPositions.Empty;
            _endpointStageDiagnostics.AfterRootRestorePositions = RetargetEndpointStageWorldPositions.Empty;
            _endpointStageDiagnostics.AfterRootDeltaPositions = RetargetEndpointStageWorldPositions.Empty;
            _endpointStageDiagnostics.AfterGroundingPositions = RetargetEndpointStageWorldPositions.Empty;
            _endpointStageDiagnostics.AfterBipedIKPositions = RetargetEndpointStageWorldPositions.Empty;
            _endpointStageDiagnostics.AfterLateVisualGroundingPositions = RetargetEndpointStageWorldPositions.Empty;
#if UNITY_EDITOR
            _endpointStageDiagnostics.ResetAttribution();
#endif
        }

        private void CaptureSetHumanPosePreSolveBasisDiagnostics(HumanPose pose)
        {
#if UNITY_EDITOR
            _setHumanPoseDiagnostics.CapturePreSolveBasis(new RetargetingSetHumanPosePreSolveBasis
            {
                GhostRootWorldPosition = RetargetingAnimatorPoseReader.ReadRootWorldPosition(ghostAnimator),
                GhostRootWorldRotation = RetargetingAnimatorPoseReader.ReadRootWorldRotation(ghostAnimator),
                TargetRootWorldPosition = RetargetingAnimatorPoseReader.ReadRootWorldPosition(targetAnimator),
                TargetRootWorldRotation = RetargetingAnimatorPoseReader.ReadRootWorldRotation(targetAnimator),
                TargetHipsWorldPosition = RetargetingAnimatorPoseReader.ReadBoneWorldPosition(targetAnimator, HumanBodyBones.Hips),
                TargetHipsLocalPosition = RetargetingAnimatorPoseReader.ReadBoneLocalPosition(targetAnimator, HumanBodyBones.Hips),
                BodyPosition = IsFinite(pose.bodyPosition) ? pose.bodyPosition : BuildNaNVector3(),
                BodyRotation = IsFinite(pose.bodyRotation) ? pose.bodyRotation : BuildNaNQuaternion(),
                GhostEndpointPositions = RetargetingEndpointStageDiagnostics.CapturePositions(ghostAnimator),
                CurrentEndpointPositions = _setHumanPoseDiagnostics.PreSolveBasis.CurrentEndpointPositions,
                TargetEndpointPositions = RetargetingEndpointStageDiagnostics.CapturePositions(targetAnimator)
            });
#endif
        }

        private RetargetingSetHumanPoseInputSnapshot ReadSetHumanPoseInputSnapshot(HumanPose pose)
        {
            return new RetargetingSetHumanPoseInputSnapshot
            {
                LeftShoulderFrontBack = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftShoulderFrontBack),
                LeftArmTwist = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftArmTwist),
                LeftForearmStretch = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftForearmStretch),
                RightForearmStretch = _muscleIndexMap.Read(pose, HumanMuscleSlot.RightForearmStretch),
                RightArmTwist = _muscleIndexMap.Read(pose, HumanMuscleSlot.RightArmTwist),
                LeftUpperLegFrontBack = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftUpperLegFrontBack),
                RightUpperLegFrontBack = _muscleIndexMap.Read(pose, HumanMuscleSlot.RightUpperLegFrontBack),
                LeftLowerLegStretch = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftLowerLegStretch),
                RightLowerLegStretch = _muscleIndexMap.Read(pose, HumanMuscleSlot.RightLowerLegStretch),
                LeftFootUpDown = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftFootUpDown),
                RightFootUpDown = _muscleIndexMap.Read(pose, HumanMuscleSlot.RightFootUpDown),
                SpineFrontBack = _muscleIndexMap.Read(pose, HumanMuscleSlot.SpineFrontBack),
                SpineLeftRight = _muscleIndexMap.Read(pose, HumanMuscleSlot.SpineLeftRight),
                SpineTwistLeftRight = _muscleIndexMap.Read(pose, HumanMuscleSlot.SpineTwistLeftRight),
                ChestFrontBack = _muscleIndexMap.Read(pose, HumanMuscleSlot.ChestFrontBack),
                ChestLeftRight = _muscleIndexMap.Read(pose, HumanMuscleSlot.ChestLeftRight),
                ChestTwistLeftRight = _muscleIndexMap.Read(pose, HumanMuscleSlot.ChestTwistLeftRight),
                UpperChestFrontBack = _muscleIndexMap.Read(pose, HumanMuscleSlot.UpperChestFrontBack),
                UpperChestLeftRight = _muscleIndexMap.Read(pose, HumanMuscleSlot.UpperChestLeftRight),
                UpperChestTwistLeftRight = _muscleIndexMap.Read(pose, HumanMuscleSlot.UpperChestTwistLeftRight),
                LeftUpperLegInOut = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftUpperLegInOut),
                RightUpperLegInOut = _muscleIndexMap.Read(pose, HumanMuscleSlot.RightUpperLegInOut),
                LeftUpperLegTwistInOut = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftUpperLegTwistInOut),
                RightUpperLegTwistInOut = _muscleIndexMap.Read(pose, HumanMuscleSlot.RightUpperLegTwistInOut),
                LeftLowerLegTwistInOut = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftLowerLegTwistInOut),
                RightLowerLegTwistInOut = _muscleIndexMap.Read(pose, HumanMuscleSlot.RightLowerLegTwistInOut),
                LeftFootTwistInOut = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftFootTwistInOut),
                RightFootTwistInOut = _muscleIndexMap.Read(pose, HumanMuscleSlot.RightFootTwistInOut),
                LeftToesUpDown = _muscleIndexMap.Read(pose, HumanMuscleSlot.LeftToesUpDown),
                RightToesUpDown = _muscleIndexMap.Read(pose, HumanMuscleSlot.RightToesUpDown),
                BodyPosition = IsFinite(pose.bodyPosition) ? pose.bodyPosition : BuildNaNVector3(),
                BodyRotation = IsFinite(pose.bodyRotation) ? pose.bodyRotation : BuildNaNQuaternion()
            };
        }

        private RetargetingSetHumanPoseOutputSnapshot ReadSetHumanPoseOutputSnapshot()
        {
            RetargetingSetHumanPoseOutputSnapshot output = RetargetingSetHumanPoseOutputSnapshot.NaN;
            if (_targetHandler == null)
            {
                return output;
            }

            _targetHandler.GetHumanPose(ref _appliedTargetPose);
            output.LeftShoulderFrontBack = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.LeftShoulderFrontBack);
            output.LeftArmTwist = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.LeftArmTwist);
            output.LeftForearmStretch = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.LeftForearmStretch);
            output.RightForearmStretch = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.RightForearmStretch);
            output.RightArmTwist = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.RightArmTwist);
            output.LeftUpperLegFrontBack = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.LeftUpperLegFrontBack);
            output.RightUpperLegFrontBack = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.RightUpperLegFrontBack);
            output.LeftLowerLegStretch = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.LeftLowerLegStretch);
            output.RightLowerLegStretch = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.RightLowerLegStretch);
            output.LeftFootUpDown = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.LeftFootUpDown);
            output.RightFootUpDown = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.RightFootUpDown);
            output.RightUpperLegInOut = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.RightUpperLegInOut);
            output.RightUpperLegTwistInOut = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.RightUpperLegTwistInOut);
            output.RightLowerLegTwistInOut = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.RightLowerLegTwistInOut);
            output.RightFootTwistInOut = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.RightFootTwistInOut);
            output.RightToesUpDown = _muscleIndexMap.Read(_appliedTargetPose, HumanMuscleSlot.RightToesUpDown);
            output.BodyPosition = IsFinite(_appliedTargetPose.bodyPosition)
                ? _appliedTargetPose.bodyPosition
                : BuildNaNVector3();
            output.BodyRotation = IsFinite(_appliedTargetPose.bodyRotation)
                ? _appliedTargetPose.bodyRotation
                : BuildNaNQuaternion();
            return output;
        }

        private static float CalculateFiniteAbsDelta(float a, float b)
        {
            return IsFinite(a) && IsFinite(b) ? Mathf.Abs(a - b) : float.NaN;
        }

        private static float CalculateFiniteXzDelta(Vector3 a, Vector3 b)
        {
            if (!IsFinite(a) || !IsFinite(b))
            {
                return float.NaN;
            }

            float deltaX = b.x - a.x;
            float deltaZ = b.z - a.z;
            return Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
        }

        private static float CalculateFiniteAngleDelta(Quaternion a, Quaternion b)
        {
            return IsFinite(a) && IsFinite(b) ? Quaternion.Angle(a, b) : float.NaN;
        }

        private static float ReadBodyRotationYaw(Quaternion rotation)
        {
            return IsFinite(rotation) ? rotation.eulerAngles.y : float.NaN;
        }

        private void ClampPoseMuscles(ref HumanPose pose)
        {
            if (!ShouldApplyRetargetSafetyGuards() ||
                !clampMusclesToHumanRange ||
                pose.muscles == null)
            {
                return;
            }

            int clampedCount =
                HumanoidMuscleRangeLimiter.ClampNonFingerMusclesInPlace(pose.muscles);

            if (clampedCount > 0 && !_muscleClampWarningLogged)
            {
                Debug.LogWarning($"[PoseSpaceRetargeter] Humanoid muscle 값 {clampedCount}개가 안전 범위를 벗어나 [-1, 1]로 제한되었습니다.");
                _muscleClampWarningLogged = true;
            }
        }

        private void ApplySetHumanPoseRightLegTwistOutputReference(HumanPose inputPose)
        {
            if (!ShouldUseSetHumanPoseRightLegTwistOutputReference ||
                _targetHandler == null ||
                inputPose.muscles == null)
            {
                return;
            }

            _targetHandler.GetHumanPose(ref _appliedTargetPose);
            if (_appliedTargetPose.muscles == null)
            {
                return;
            }

            float weight = Mathf.Clamp01(setHumanPoseRightLegTwistOutputReferenceWeight);
            float maxDelta = Mathf.Max(0f, setHumanPoseRightLegTwistOutputReferenceMaxDelta);
            if (weight <= 0f || maxDelta <= 0f)
            {
                return;
            }

            bool changed = ManualPoseReferenceApplier.TryApplyBoundedMuscleOutputReference(
                ref _appliedTargetPose,
                inputPose,
                _muscleIndexMap.Get(HumanMuscleSlot.RightUpperLegTwistInOut),
                weight,
                maxDelta);
            changed |= ManualPoseReferenceApplier.TryApplyBoundedMuscleOutputReference(
                ref _appliedTargetPose,
                inputPose,
                _muscleIndexMap.Get(HumanMuscleSlot.RightLowerLegTwistInOut),
                weight,
                maxDelta);

            if (changed)
            {
                _targetHandler.SetHumanPose(ref _appliedTargetPose);
            }
        }

        private void ApplyEditorHumanoidBoneLocalRotationReference()
        {
#if UNITY_EDITOR
            if (!IsCompleteEditorHumanoidPoseReferenceActive)
            {
                return;
            }

            // 완전한 Native 기준 포즈는 HumanPose 재해석 과정에서 범위를 벗어난 muscle이 손실될 수 있음.
            // 동일 모델의 Humanoid 본 회전만 복구하고 root 이동, 본 위치와 scale은 기존 경로가 소유함.
            _editorHumanoidPoseReferencePlayer.TryApplyHumanoidBoneLocalRotationsTo(targetAnimator);
#endif
        }

        private void ApplyAnatomicalArmGuard(ref HumanPose pose)
        {
            if (!ShouldApplyRetargetSafetyGuards() ||
                !enableAnatomicalArmGuard ||
                pose.muscles == null)
            {
                return;
            }

            int changed = HumanoidArmDeformationGuard.ClampAnatomicalArmMuscles(
                ref pose,
                armStretchMuscleLimit,
                upperArmTwistMuscleLimit,
                lowerArmTwistMuscleLimit,
                clampArmStretchMuscles);

            if (changed > 0 && !_anatomyGuardWarningLogged)
            {
                Debug.LogWarning($"[PoseSpaceRetargeter] 팔 변형 방지를 위해 Humanoid arm muscle {changed}개를 제한했습니다.");
                _anatomyGuardWarningLogged = true;
            }
        }

        private void ClampAppliedTargetPose()
        {
            if (!ShouldApplyRetargetSafetyGuards() ||
                (!clampMusclesToHumanRange && !enableAnatomicalArmGuard && !enableThumbAnatomicalGuard) ||
                _targetHandler == null)
            {
                return;
            }

            _targetHandler.GetHumanPose(ref _appliedTargetPose);
            if (!IsFinite(_appliedTargetPose))
            {
                LogPoseWarning("Applied target HumanPose contains non-finite values. Skipping applied pose clamp.");
                return;
            }

            int changed = 0;
            if (clampMusclesToHumanRange)
            {
                changed += HumanoidMuscleRangeLimiter.ClampNonFingerMusclesInPlace(
                    _appliedTargetPose.muscles);
            }

            if (enableAnatomicalArmGuard)
            {
                changed += HumanoidArmDeformationGuard.ClampAnatomicalArmMuscles(
                    ref _appliedTargetPose,
                    armStretchMuscleLimit,
                    upperArmTwistMuscleLimit,
                    lowerArmTwistMuscleLimit,
                    clampArmStretchMuscles);
            }

            changed += ThumbMuscleGuardPolicy.ApplyAnatomicalGuard(this, ref _appliedTargetPose, false);

            if (changed <= 0)
            {
                return;
            }

            _targetHandler.SetHumanPose(ref _appliedTargetPose);
            if (!_appliedPoseClampWarningLogged)
            {
                Debug.LogWarning($"[PoseSpaceRetargeter] Target 적용 후 범위를 벗어난 Humanoid muscle {changed}개를 추가 보정했습니다.");
                _appliedPoseClampWarningLogged = true;
            }
        }

        private bool ShouldApplyRetargetSafetyGuards()
        {
#if UNITY_EDITOR
            return RetargetingMuscleReferencePolicy.ShouldApplyRetargetSafetyGuards(
                _useCompleteEditorHumanoidMuscleReference);
#else
            return true;
#endif
        }

        public bool IsManualThumbLocalRotationReferenceActive
        {
            get
            {
#if UNITY_EDITOR
                return useManualAnimatorThumbLocalRotationReference &&
                    _useEditorFingerPoseReference &&
                    _editorFingerReferenceAnimator != null;
#else
                return false;
#endif
            }
        }

        public bool ShouldSuppressThumbPoseShapingGuard
        {
            get
            {
#if UNITY_EDITOR
                return ShouldSuppressThumbPoseShapingGuardForHand(true) ||
                    ShouldSuppressThumbPoseShapingGuardForHand(false);
#else
                return false;
#endif
            }
        }

        public bool ShouldSuppressLeftThumbPoseShapingGuard
        {
            get
            {
#if UNITY_EDITOR
                return ShouldSuppressThumbPoseShapingGuardForHand(true);
#else
                return false;
#endif
            }
        }

        public bool ShouldSuppressRightThumbPoseShapingGuard
        {
            get
            {
#if UNITY_EDITOR
                return ShouldSuppressThumbPoseShapingGuardForHand(false);
#else
                return false;
#endif
            }
        }

        public bool ShouldSuppressThumbPoseShapingGuardForHand(bool leftHand)
        {
            return ThumbMuscleGuardPolicy.ShouldSuppressPoseShapingGuardForHand(this, leftHand);
        }


        public void ResetThumbWorldRotationPreserveDiagnostics()
        {
            _thumbGuardDiagnostics.ResetWorldRotationPreserve();
        }



        public bool ShouldPreserveManualThumbWorldRotationCorrection(
            HumanBodyBones thumbBone,
            Transform targetThumb,
            Quaternion candidateWorldRotation)
        {
            return ThumbWorldRotationPreservePolicy.ShouldPreserveManualWorldRotationCorrection(this, thumbBone, targetThumb, candidateWorldRotation);
        }


        internal bool HasDetachedThumbBaseHelperRelationship(HumanBodyBones thumbBone, bool leftHand)
        {
            return ThumbBaseHelperResolver.HasDetachedRelationship(this, thumbBone, leftHand);
        }

        internal static bool TryGetThumbBoneSide(HumanBodyBones thumbBone, out bool leftHand)
        {
            return ThumbGuardPolicy.TryGetBoneSide(thumbBone, out leftHand);
        }


        private void ClampTargetRootPositionSpike(Vector3 positionBeforePose, string source)
        {
            if (!clampRootDeltaSpikes || targetAnimator == null)
            {
                return;
            }

            Vector3 currentPosition = targetAnimator.transform.position;
            bool shouldClamp = RetargetingPoseSmoothing.TryCalculateRootPositionSpikeClamp(
                positionBeforePose,
                currentPosition,
                maxRootDeltaPerFrame,
                out Vector3 clampedPosition,
                out float poseDeltaMagnitude);

            _lastRootPositionPoseDeltaMagnitude = poseDeltaMagnitude;
            if (!IsFinite(poseDeltaMagnitude))
            {
                return;
            }

            _maxRootPositionPoseDeltaMagnitude = Mathf.Max(_maxRootPositionPoseDeltaMagnitude, _lastRootPositionPoseDeltaMagnitude);
            if (!shouldClamp)
            {
                return;
            }

            _rootPositionSpikeClampedCount++;
            if (logRootDeltaSpikes && !_rootDeltaSpikeWarningLogged)
            {
                Debug.LogWarning($"[PoseSpaceRetargeter] {source} root position spike {_lastRootPositionPoseDeltaMagnitude:F3}m clamped. limit={maxRootDeltaPerFrame:F3}m");
                _rootDeltaSpikeWarningLogged = true;
            }

            targetAnimator.transform.position = clampedPosition;
        }

        private void ClampTargetHipsLocalPositionSpike()
        {
            if (!clampTargetHipsLocalPositionSpikes || targetAnimator == null || !targetAnimator.isHuman)
            {
                ResetTargetHipsLocalPositionSpikeState();
                return;
            }

            Transform targetHips = targetAnimator.GetBoneTransform(HumanBodyBones.Hips);
            if (targetHips == null)
            {
                ResetTargetHipsLocalPositionSpikeState();
                return;
            }

            Vector3 currentLocalPosition = targetHips.localPosition;
            if (!_hasPreviousTargetHipsLocalPosition)
            {
                RememberTargetHipsLocalPosition(currentLocalPosition);
                return;
            }

            bool shouldClamp = RetargetingPoseSmoothing.TryCalculateHipsLocalPositionSpikeClamp(
                _previousTargetHipsLocalPosition,
                currentLocalPosition,
                maxTargetHipsLocalPositionDeltaPerFrame,
                out Vector3 clampedLocalPosition,
                out float deltaMagnitude);

            _lastTargetHipsLocalPositionDelta = deltaMagnitude;
            if (!IsFinite(deltaMagnitude))
            {
                return;
            }

            _maxTargetHipsLocalPositionDelta = Mathf.Max(_maxTargetHipsLocalPositionDelta, deltaMagnitude);
            if (shouldClamp)
            {
                targetHips.localPosition = clampedLocalPosition;
                _targetHipsLocalPositionSpikeClampedCount++;
            }

            RememberTargetHipsLocalPosition(targetHips.localPosition);
        }

        private void RememberTargetHipsLocalPosition(Vector3 localPosition)
        {
            if (!IsFinite(localPosition))
            {
                return;
            }

            _previousTargetHipsLocalPosition = localPosition;
            _hasPreviousTargetHipsLocalPosition = true;
        }

        private void ResetTargetHipsLocalPositionSpikeState()
        {
            _previousTargetHipsLocalPosition = Vector3.zero;
            _hasPreviousTargetHipsLocalPosition = false;
            _lastTargetHipsLocalPositionDelta = float.NaN;
            _maxTargetHipsLocalPositionDelta = 0f;
            _targetHipsLocalPositionSpikeClampedCount = 0;
        }

        private void CaptureTargetInitialTransforms(GameObject targetRoot)
        {
            _targetInitialScales.Clear();
            _targetInitialHumanoidLocalPositions.Clear();
            _targetInitialThumbBaseHelperLocalPositions.Clear();
            _targetInitialThumbLocalRotations.Clear();
            _cachedThumbBaseHelpers.Clear();
            _cachedThumbBaseExplicitSources.Clear();
            _initialThumbBaseHelperSourceDistances.Clear();
            _initialThumbBaseHelperSourceRelativeRotations.Clear();
            _scaleWarningLogged = false;
            _positionWarningLogged = false;
            _muscleClampWarningLogged = false;
            _anatomyGuardWarningLogged = false;
            _thumbGuardWarningLogged = false;
            _thumbLocalRotationGuardWarningLogged = false;
            _rootDeltaSpikeWarningLogged = false;
            _hasPreviousBodyRootMotionPosition = false;
            ResetTargetHipsLocalPositionSpikeState();
            _lastRootDeltaMagnitude = float.NaN;
            _maxRootDeltaMagnitude = 0f;
            _rootDeltaSpikeSkippedCount = 0;
            _lastRootPositionPoseDeltaMagnitude = float.NaN;
            _maxRootPositionPoseDeltaMagnitude = 0f;
            _rootPositionSpikeClampedCount = 0;
            _legacyAnimationDriver.ResetStabilityMetrics();
            _hasPreviousVisualPose = false;
            _poseVisualSmoothingCount = 0;
            _poseVisualMuscleDeltaOnlySkippedCount = 0;
            _lastPoseVisualMaxMuscleDelta = float.NaN;
            _maxPoseVisualMaxMuscleDelta = 0f;
            _grounding.ResetSession();
            _lastEditorFootHeightGroundingReferenceLift = float.NaN;
            _appliedPoseClampWarningLogged = false;
            _targetHipsRestLocalPosition = Vector3.zero;
            _hasTargetHipsRestLocalPosition = false;

            foreach (Transform targetBone in targetRoot.GetComponentsInChildren<Transform>(true))
            {
                _targetInitialScales[targetBone] = targetBone.localScale;
            }

            if (targetAnimator == null || !targetAnimator.isHuman)
            {
                return;
            }

            for (int i = (int)HumanBodyBones.Hips; i < (int)HumanBodyBones.LastBone; i++)
            {
                HumanBodyBones bone = (HumanBodyBones)i;
                Transform targetBone = targetAnimator.GetBoneTransform(bone);
                if (bone == HumanBodyBones.Hips)
                {
                    if (targetBone != null && IsFinite(targetBone.localPosition))
                    {
                        _targetHipsRestLocalPosition = targetBone.localPosition;
                        _hasTargetHipsRestLocalPosition = true;
                    }

                    continue;
                }

                if (targetBone == null || _targetInitialHumanoidLocalPositions.ContainsKey(targetBone))
                {
                    continue;
                }

                _targetInitialHumanoidLocalPositions[targetBone] = targetBone.localPosition;
            }

            foreach (HumanBodyBones thumbBone in ThumbRotationBones)
            {
                Transform thumbTransform = targetAnimator.GetBoneTransform(thumbBone);
                if (thumbTransform == null || _targetInitialThumbLocalRotations.ContainsKey(thumbTransform))
                {
                    continue;
                }

                _targetInitialThumbLocalRotations[thumbTransform] = thumbTransform.localRotation;
            }

            ThumbBaseHelperResolver.CaptureState(this, true);
            ThumbBaseHelperResolver.CaptureState(this, false);
        }

        internal static string FormatDebugFloat(float value)
        {
            return IsFinite(value) ? value.ToString("F4") : "NaN";
        }

        internal static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return "<none>";
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder(transform.name);
            Transform current = transform.parent;
            while (current != null)
            {
                builder.Insert(0, current.name + "/");
                current = current.parent;
            }

            return builder.ToString();
        }

        internal Transform FindTargetTransformByNameSuffix(string nameSuffix)
        {
            if (targetAnimator == null || targetAnimator.gameObject == null || string.IsNullOrEmpty(nameSuffix))
            {
                return null;
            }

            foreach (Transform candidate in targetAnimator.gameObject.GetComponentsInChildren<Transform>(true))
            {
                if (candidate != null && candidate.name.EndsWith(nameSuffix, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        internal Transform FindTargetTransform(Func<Transform, bool> predicate)
        {
            if (targetAnimator == null || targetAnimator.gameObject == null || predicate == null)
            {
                return null;
            }

            foreach (Transform candidate in targetAnimator.gameObject.GetComponentsInChildren<Transform>(true))
            {
                if (candidate != null && predicate(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }


        internal Transform FindThumbBaseHelperByName(bool leftHand)
        {
            string sideToken = leftHand ? "left" : "right";
            return FindTargetTransform(candidate =>
            {
                string normalizedName = NormalizeTransformName(candidate.name);
                return normalizedName.Contains(sideToken) &&
                    ThumbTransformNamePolicy.IsDetachedBaseHelper(normalizedName);
            });
        }

        internal static bool IsAncestorWithinHand(Transform ancestor, Transform descendant, Transform hand)
        {
            if (ancestor == null || descendant == null || hand == null || ancestor == descendant)
            {
                return false;
            }

            Transform current = descendant.parent;
            while (current != null)
            {
                if (current == ancestor)
                {
                    return true;
                }

                if (current == hand)
                {
                    break;
                }

                current = current.parent;
            }

            return false;
        }

        internal static string NormalizeTransformName(string value)
        {
            return string.IsNullOrEmpty(value) ? "" : value.ToLowerInvariant();
        }

        private void RestoreTargetHumanoidLocalPositions()
        {
            if (!ShouldLockTargetHumanoidBonePositions)
            {
                return;
            }

            foreach (KeyValuePair<Transform, Vector3> positionSnapshot in _targetInitialHumanoidLocalPositions)
            {
                Transform targetBone = positionSnapshot.Key;
                if (targetBone == null)
                {
                    continue;
                }

                if ((targetBone.localPosition - positionSnapshot.Value).sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                if (!_positionWarningLogged)
                {
                    Debug.LogWarning($"[PoseSpaceRetargeter] Target humanoid bone localPosition changed during retargeting. Restoring original bone length. First bone: {targetBone.name}");
                    _positionWarningLogged = true;
                }

                targetBone.localPosition = positionSnapshot.Value;
            }
        }

        private void RestoreTargetLocalScales()
        {
            foreach (KeyValuePair<Transform, Vector3> scaleSnapshot in _targetInitialScales)
            {
                Transform targetBone = scaleSnapshot.Key;
                if (targetBone == null)
                {
                    continue;
                }

                if ((targetBone.localScale - scaleSnapshot.Value).sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                if (!_scaleWarningLogged)
                {
                    Debug.LogWarning($"[PoseSpaceRetargeter] Target bone scale changed during retargeting. Restoring original localScale. First bone: {targetBone.name}");
                    _scaleWarningLogged = true;
                }

                targetBone.localScale = scaleSnapshot.Value;
            }
        }

        private void EnsureLateVisualGroundingCorrection()
        {
            if (!enableLateVisualGroundingCorrection)
            {
                return;
            }

            PoseSpaceLateVisualGroundingCorrection correction = GetComponent<PoseSpaceLateVisualGroundingCorrection>();
            if (correction == null)
            {
                correction = gameObject.AddComponent<PoseSpaceLateVisualGroundingCorrection>();
            }

            correction.Initialize(this);
        }

        public void ApplyLateVisualGroundingCorrection()
        {
            _grounding.ApplyLateVisualCorrection(this);
        }
    }

}
