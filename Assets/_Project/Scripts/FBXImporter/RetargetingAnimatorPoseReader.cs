using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // Animator 본 위치·회전 판독 유틸
    internal static class RetargetingAnimatorPoseReader
    {
        internal static Vector3 ReadBoneWorldPosition(Animator animator, HumanBodyBones bone)
        {
            if (animator == null)
            {
                return BuildNaNVector3();
            }

            Transform targetBone = animator.GetBoneTransform(bone);
            return targetBone != null ? targetBone.position : BuildNaNVector3();
        }

        internal static Vector3 ReadBoneLocalPosition(Animator animator, HumanBodyBones bone)
        {
            if (animator == null)
            {
                return BuildNaNVector3();
            }

            Transform targetBone = animator.GetBoneTransform(bone);
            if (targetBone == null)
            {
                return BuildNaNVector3();
            }

            Vector3 localPosition = targetBone.localPosition;
            return IsFinite(localPosition) ? localPosition : BuildNaNVector3();
        }

        internal static Vector3 ReadRootWorldPosition(Animator animator)
        {
            if (animator == null)
            {
                return BuildNaNVector3();
            }

            Vector3 position = animator.transform.position;
            return IsFinite(position) ? position : BuildNaNVector3();
        }

        internal static Quaternion ReadRootWorldRotation(Animator animator)
        {
            if (animator == null)
            {
                return BuildNaNQuaternion();
            }

            Quaternion rotation = animator.transform.rotation;
            return IsFinite(rotation) ? rotation : BuildNaNQuaternion();
        }

        private static Vector3 BuildNaNVector3()
        {
            return new Vector3(float.NaN, float.NaN, float.NaN);
        }

        private static Quaternion BuildNaNQuaternion()
        {
            return new Quaternion(float.NaN, float.NaN, float.NaN, float.NaN);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
