using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal struct RetargetEndpointStageWorldPositions
    {
        public Vector3 LeftFoot;
        public Vector3 LeftToes;
        public Vector3 RightFoot;
        public Vector3 RightToes;

        public static RetargetEndpointStageWorldPositions Empty => new RetargetEndpointStageWorldPositions
        {
            LeftFoot = BuildNaNVector3(),
            LeftToes = BuildNaNVector3(),
            RightFoot = BuildNaNVector3(),
            RightToes = BuildNaNVector3()
        };

        private static Vector3 BuildNaNVector3()
        {
            return new Vector3(float.NaN, float.NaN, float.NaN);
        }
    }
}
