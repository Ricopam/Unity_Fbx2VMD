using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal struct RetargetingSetHumanPoseInputSnapshot
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
        public float SpineFrontBack;
        public float SpineLeftRight;
        public float SpineTwistLeftRight;
        public float ChestFrontBack;
        public float ChestLeftRight;
        public float ChestTwistLeftRight;
        public float UpperChestFrontBack;
        public float UpperChestLeftRight;
        public float UpperChestTwistLeftRight;
        public float LeftUpperLegInOut;
        public float RightUpperLegInOut;
        public float LeftUpperLegTwistInOut;
        public float RightUpperLegTwistInOut;
        public float LeftLowerLegTwistInOut;
        public float RightLowerLegTwistInOut;
        public float LeftFootTwistInOut;
        public float RightFootTwistInOut;
        public float LeftToesUpDown;
        public float RightToesUpDown;
        public Vector3 BodyPosition;
        public Quaternion BodyRotation;

        public static RetargetingSetHumanPoseInputSnapshot NaN => new RetargetingSetHumanPoseInputSnapshot
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
            SpineFrontBack = float.NaN,
            SpineLeftRight = float.NaN,
            SpineTwistLeftRight = float.NaN,
            ChestFrontBack = float.NaN,
            ChestLeftRight = float.NaN,
            ChestTwistLeftRight = float.NaN,
            UpperChestFrontBack = float.NaN,
            UpperChestLeftRight = float.NaN,
            UpperChestTwistLeftRight = float.NaN,
            LeftUpperLegInOut = float.NaN,
            RightUpperLegInOut = float.NaN,
            LeftUpperLegTwistInOut = float.NaN,
            RightUpperLegTwistInOut = float.NaN,
            LeftLowerLegTwistInOut = float.NaN,
            RightLowerLegTwistInOut = float.NaN,
            LeftFootTwistInOut = float.NaN,
            RightFootTwistInOut = float.NaN,
            LeftToesUpDown = float.NaN,
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

        // 세션 리셋 시점에 초기화되는 확장 입력 근육 19개
        public void ClearExtendedMuscles()
        {
            SpineFrontBack = float.NaN;
            SpineLeftRight = float.NaN;
            SpineTwistLeftRight = float.NaN;
            ChestFrontBack = float.NaN;
            ChestLeftRight = float.NaN;
            ChestTwistLeftRight = float.NaN;
            UpperChestFrontBack = float.NaN;
            UpperChestLeftRight = float.NaN;
            UpperChestTwistLeftRight = float.NaN;
            LeftUpperLegInOut = float.NaN;
            RightUpperLegInOut = float.NaN;
            LeftUpperLegTwistInOut = float.NaN;
            RightUpperLegTwistInOut = float.NaN;
            LeftLowerLegTwistInOut = float.NaN;
            RightLowerLegTwistInOut = float.NaN;
            LeftFootTwistInOut = float.NaN;
            RightFootTwistInOut = float.NaN;
            LeftToesUpDown = float.NaN;
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
