using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Animations.Rigging;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidArmTwistRiggingGuard
    {
        [Header("Animation Rigging Arm Twist")]
        [FormerlySerializedAs("enableTwistRigging")]
        [SerializeField] private bool _enableTwistRigging= true;
        public bool enableTwistRigging { get => _enableTwistRigging; set => _enableTwistRigging = value; }
        [Range(0f, 1f)] [FormerlySerializedAs("rigWeight")]
        [SerializeField] private float _rigWeight= 0.65f;
        public float rigWeight { get => _rigWeight; private set => _rigWeight = value; }
        [Range(0f, 1f)] [FormerlySerializedAs("upperArmTwistWeight")]
        [SerializeField] private float _upperArmTwistWeight= 0.45f;
        public float upperArmTwistWeight { get => _upperArmTwistWeight; private set => _upperArmTwistWeight = value; }
        [Range(0f, 1f)] [FormerlySerializedAs("forearmTwistWeight")]
        [SerializeField] private float _forearmTwistWeight= 0.85f;
        public float forearmTwistWeight { get => _forearmTwistWeight; private set => _forearmTwistWeight = value; }
        [FormerlySerializedAs("fallbackTwistAxis")]
        [SerializeField] private TwistCorrectionData.Axis _fallbackTwistAxis= TwistCorrectionData.Axis.X;
        public TwistCorrectionData.Axis fallbackTwistAxis { get => _fallbackTwistAxis; private set => _fallbackTwistAxis = value; }
        [FormerlySerializedAs("logConfiguration")]
        [SerializeField] private bool _logConfiguration= false;
        public bool logConfiguration { get => _logConfiguration; private set => _logConfiguration = value; }
    }
}
