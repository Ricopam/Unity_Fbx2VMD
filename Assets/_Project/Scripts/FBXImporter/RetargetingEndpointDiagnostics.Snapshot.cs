using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class RetargetingEndpointDiagnostics
    {

        private static bool TryCalculateXzDistance(Vector3 a, Vector3 b, out float distance)
        {
            distance = float.NaN;
            if (!IsFinite(a) || !IsFinite(b))
            {
                return false;
            }

            distance = Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
            return IsFinite(distance);
        }

        private static Vector3 NaNVector3 => new Vector3(float.NaN, float.NaN, float.NaN);

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(Quaternion value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) && IsFinite(value.w);
        }

        private static bool TryNormalize(Vector3 value, out Vector3 normalized)
        {
            normalized = Vector3.zero;
            if (!IsFinite(value) || value.sqrMagnitude <= 0.00000001f)
            {
                return false;
            }

            normalized = value.normalized;
            return IsFinite(normalized);
        }

    }


}
