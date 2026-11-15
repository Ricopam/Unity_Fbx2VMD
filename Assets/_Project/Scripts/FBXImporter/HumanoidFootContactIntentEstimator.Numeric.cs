using System;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 접촉 의도 추정기의 수치 유틸리티 partial.
    internal static partial class HumanoidFootContactIntentEstimator
    {
        private static float Percentile(Vector3[] points, float fraction)
        {
            var heights = new float[points.Length];
            for (int index = 0; index < points.Length; index++)
            {
                heights[index] = points[index].y;
            }

            Array.Sort(heights);
            return heights[(int)((heights.Length - 1) * fraction)];
        }

        private static Vector2 HorizontalDelta(Vector3 from, Vector3 to)
        {
            return new Vector2(to.x - from.x, to.z - from.z);
        }

        private static bool IsFinite(Vector3 value) =>
            IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
