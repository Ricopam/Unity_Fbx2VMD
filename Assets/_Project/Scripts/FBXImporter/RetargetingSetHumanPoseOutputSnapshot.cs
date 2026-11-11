using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal struct RetargetingSetHumanPoseOutputSnapshot
    {
        public float LeftShoulderFrontBack;
        public float LeftArmTwist;
        public float LeftForearmStretch;
        public float RightForearmStretch;
        public float RightArmTwist;
        public float LeftUpperLegFrontBack;
        public float RightUpperLegFrontBack;
        public float LeftLowerLegStretch;
        public float RightLowerLegStretch;
        public float LeftFootUpDown;
        public float RightFootUpDown;
        public float RightUpperLegInOut;
        public float RightUpperLegTwistInOut;
        public float RightLowerLegTwistInOut;
        public float RightFootTwistInOut;
        public float RightToesUpDown;
        public Vector3 BodyPosition;
        public Quaternion BodyRotation;

        public static RetargetingSetHumanPoseOutputSnapshot NaN => new RetargetingSetHumanPoseOutputSnapshot
        {
            LeftShoulderFrontBack = float.NaN,
            LeftArmTwist = float.NaN,
            LeftForearmStretch = float.NaN,
            RightForearmStretch = float.NaN,
            RightArmTwist = float.NaN,
            LeftUpperLegFrontBack = float.NaN,
            RightUpperLegFrontBack = float.NaN,
            LeftLowerLegStretch = float.NaN,
            RightLowerLegStretch = float.NaN,
            LeftFootUpDown = float.NaN,
            RightFootUpDown = float.NaN,
            RightUpperLegInOut = float.NaN,
            RightUpperLegTwistInOut = float.NaN,
            RightLowerLegTwistInOut = float.NaN,
            RightFootTwistInOut = float.NaN,
            RightToesUpDown = float.NaN,
            BodyPosition = BuildNaNVector3(),
            BodyRotation = BuildNaNQuaternion()
        };

        // 입력 캡처 시점에 초기화되는 코어 근육 11개 + 본 위치/회전
        public void ClearCore()
        {
            LeftShoulderFrontBack = float.NaN;
            LeftArmTwist = float.NaN;
            LeftForearmStretch = float.NaN;
            RightForearmStretch = float.NaN;
            RightArmTwist = float.NaN;
            LeftUpperLegFrontBack = float.NaN;
            RightUpperLegFrontBack = float.NaN;
            LeftLowerLegStretch = float.NaN;
            RightLowerLegStretch = float.NaN;
            LeftFootUpDown = float.NaN;
            RightFootUpDown = float.NaN;
            BodyPosition = BuildNaNVector3();
            BodyRotation = BuildNaNQuaternion();
        }

        // 세션 리셋 시점에 초기화되는 확장 출력 근육 5개
        public void ClearExtendedMuscles()
        {
            RightUpperLegInOut = float.NaN;
            RightUpperLegTwistInOut = float.NaN;
            RightLowerLegTwistInOut = float.NaN;
            RightFootTwistInOut = float.NaN;
            RightToesUpDown = float.NaN;
        }

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
