using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal struct RetargetingSetHumanPosePreSolveBasis
    {
        public Vector3 GhostRootWorldPosition;
        public Quaternion GhostRootWorldRotation;
        public Vector3 TargetRootWorldPosition;
        public Quaternion TargetRootWorldRotation;
        public Vector3 TargetHipsWorldPosition;
        public Vector3 TargetHipsLocalPosition;
        public Vector3 BodyPosition;
        public Quaternion BodyRotation;
        public RetargetEndpointStageWorldPositions GhostEndpointPositions;
        public RetargetEndpointStageWorldPositions CurrentEndpointPositions;
        public RetargetEndpointStageWorldPositions TargetEndpointPositions;

        public static RetargetingSetHumanPosePreSolveBasis NaN => new RetargetingSetHumanPosePreSolveBasis
        {
            GhostRootWorldPosition = BuildNaNVector3(),
            GhostRootWorldRotation = BuildNaNQuaternion(),
            TargetRootWorldPosition = BuildNaNVector3(),
            TargetRootWorldRotation = BuildNaNQuaternion(),
            TargetHipsWorldPosition = BuildNaNVector3(),
            TargetHipsLocalPosition = BuildNaNVector3(),
            BodyPosition = BuildNaNVector3(),
            BodyRotation = BuildNaNQuaternion(),
            GhostEndpointPositions = RetargetEndpointStageWorldPositions.Empty,
            CurrentEndpointPositions = RetargetEndpointStageWorldPositions.Empty,
            TargetEndpointPositions = RetargetEndpointStageWorldPositions.Empty
        };

        private static Vector3 BuildNaNVector3()
        {
            return new Vector3(float.NaN, float.NaN, float.NaN);
        }

        private static Quaternion BuildNaNQuaternion()
        {
            return new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN);
        }
    }
}
