using UnityEngine;
using UnityEngine.Serialization;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidArmVisualTwistGuard
    {
            [Header("YYB Arm Visual Twist Guard")]
            [FormerlySerializedAs("enableVisualTwistGuard")]
            [SerializeField] private bool _enableVisualTwistGuard= true;
            public bool enableVisualTwistGuard { get => _enableVisualTwistGuard; set => _enableVisualTwistGuard = value; }
            [Range(0f, 1f)] [FormerlySerializedAs("upperArmInfluence")]
            [SerializeField] private float _upperArmInfluence= 0.35f;
            public float upperArmInfluence { get => _upperArmInfluence; private set => _upperArmInfluence = value; }
            [Range(0f, 1f)] [FormerlySerializedAs("forearmInfluence")]
            [SerializeField] private float _forearmInfluence= 0.75f;
            public float forearmInfluence { get => _forearmInfluence; private set => _forearmInfluence = value; }
            [Range(0f, 120f)] [FormerlySerializedAs("upperArmMaxDegrees")]
            [SerializeField] private float _upperArmMaxDegrees= 45f;
            public float upperArmMaxDegrees { get => _upperArmMaxDegrees; private set => _upperArmMaxDegrees = value; }
            [Range(0f, 120f)] [FormerlySerializedAs("forearmMaxDegrees")]
            [SerializeField] private float _forearmMaxDegrees= 75f;
            public float forearmMaxDegrees { get => _forearmMaxDegrees; private set => _forearmMaxDegrees = value; }
            [FormerlySerializedAs("logConfiguration")]
            [SerializeField] private bool _logConfiguration= false;
            public bool logConfiguration { get => _logConfiguration; private set => _logConfiguration = value; }
    }
}
