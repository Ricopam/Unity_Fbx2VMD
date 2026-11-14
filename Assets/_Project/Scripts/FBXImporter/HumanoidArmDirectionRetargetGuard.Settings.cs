using UnityEngine;
using UnityEngine.Serialization;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidArmDirectionRetargetGuard
    {
            [Header("Humanoid Arm Direction Retarget Guard")]
            [FormerlySerializedAs("enableDirectionRetarget")]
            [SerializeField] private bool _enableDirectionRetarget= true;
            public bool enableDirectionRetarget { get => _enableDirectionRetarget; set => _enableDirectionRetarget = value; }

            [Range(0f, 1f)]
            [FormerlySerializedAs("upperArmWeight")]
            [SerializeField] private float _upperArmWeight= 0.65f;
            public float upperArmWeight { get => _upperArmWeight; private set => _upperArmWeight = value; }

            [Range(0f, 1f)]
            [FormerlySerializedAs("forearmWeight")]
            [SerializeField] private float _forearmWeight= 0.75f;
            public float forearmWeight { get => _forearmWeight; private set => _forearmWeight = value; }

            [Range(0f, 120f)]
            [FormerlySerializedAs("upperArmMaxDegrees")]
            [SerializeField] private float _upperArmMaxDegrees= 65f;
            public float upperArmMaxDegrees { get => _upperArmMaxDegrees; private set => _upperArmMaxDegrees = value; }

            [Range(0f, 120f)]
            [FormerlySerializedAs("forearmMaxDegrees")]
            [SerializeField] private float _forearmMaxDegrees= 85f;
            public float forearmMaxDegrees { get => _forearmMaxDegrees; private set => _forearmMaxDegrees = value; }

            [Range(0f, 1f)]
            [FormerlySerializedAs("leftSideWeightScale")]
            [SerializeField] private float _leftSideWeightScale= 1f;
            public float leftSideWeightScale { get => _leftSideWeightScale; private set => _leftSideWeightScale = value; }

            [Range(0f, 1f)]
            [FormerlySerializedAs("rightSideWeightScale")]
            [SerializeField] private float _rightSideWeightScale= 1f;
            public float rightSideWeightScale { get => _rightSideWeightScale; private set => _rightSideWeightScale = value; }

            [FormerlySerializedAs("logConfiguration")]
            [SerializeField] private bool _logConfiguration= false;
            public bool logConfiguration { get => _logConfiguration; private set => _logConfiguration = value; }
    }
}
