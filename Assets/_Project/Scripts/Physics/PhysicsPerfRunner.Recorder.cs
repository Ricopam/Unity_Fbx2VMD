using System;
using Unity.Profiling;
using UnityEngine;

namespace Fbx2Vmd.ClothPhysics
{
    // ProfilerRecorder 수명주기(시작·판독·해제) 전담 분할.
    public partial class PhysicsPerfRunner
    {
        static ProfilerRecorder StartRecorder(string markerName)
        {
            try
            {
                var rec = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, markerName, 1);
                if (!rec.Valid)
                    Debug.LogWarning(
                        $"[PhysicsPerfRunner] 마커 '{markerName}' 미검출 — " +
                        "Development Build가 아니면 0으로 기록됩니다.");
                return rec;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PhysicsPerfRunner] 레코더 시작 실패({markerName}): {e.Message}");
                return default;
            }
        }

        static double ReadNs(ProfilerRecorder rec)
        {
            try
            {
                return rec.Valid ? rec.LastValue : 0;
            }
            catch
            {
                return 0;
            }
        }

        static void DisposeRecorder(ref ProfilerRecorder rec)
        {
            if (rec.Valid)
                rec.Dispose();
            rec = default;
        }
    }
}
