using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidThumbDeformationGuard
    {
        private readonly Dictionary<Transform, Quaternion> _initialLocalRotations = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, Vector3> _initialLocalPositions = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<Transform, Quaternion> _lastRawLocalRotations = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, Quaternion> _lastCorrectedLocalRotations = new Dictionary<Transform, Quaternion>();
        private readonly Dictionary<Transform, bool> _cachedThumbSides = new Dictionary<Transform, bool>();
        private bool _warningLogged;
        private int _lastFrameCorrectionCount;
        private int _correctionsAppliedTotal;
        private int _framesWithCorrections;
        private const float ManualThumbReferenceMinProximalMaxLocalAngle = 28f;
        private const float ManualThumbProjectionPreserveSmokeRiskLimit = 0.35f;

        private static readonly HumanBodyBones[] ThumbBones =
        {
            HumanBodyBones.LeftThumbProximal,
            HumanBodyBones.LeftThumbIntermediate,
            HumanBodyBones.LeftThumbDistal,
            HumanBodyBones.RightThumbProximal,
            HumanBodyBones.RightThumbIntermediate,
            HumanBodyBones.RightThumbDistal
        };
    }
}
