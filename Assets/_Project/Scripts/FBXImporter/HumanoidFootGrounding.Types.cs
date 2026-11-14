using System;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    [Serializable]
    internal sealed class HumanoidFootGroundingSnapshot
    {
        public bool has_ground;
        public string support_role;
        public float rear_weight;
        public float front_weight;
        public Vector3 ground_point;
        public Vector3 ground_normal;
        public Vector3 rear_anchor;
        public Vector3 front_anchor;
        public Vector3 rear_point;
        public Vector3 front_point;
        public float rear_distance_mm;
        public float front_distance_mm;
        public float minimum_distance_mm;
        public string rear_renderer;
        public string front_renderer;
        public int rear_vertex;
        public int front_vertex;
    }

    [Serializable]
    internal sealed class HumanoidFootGroundingGate
    {
        public bool measured;
        public float target_error_m;
        public float sole_clearance_m;
        public float supported_contact_error_m;
        public int supported_contact_count;
        public string evaluation_stage;
        public bool raw_pass;
        public bool held_pass;
        public bool used_physical_reach;
        public float pelvis_offset_m;
        public float pelvis_maximum_offset_m;
        public HumanoidFootGroundingLegDiagnostic left;
        public HumanoidFootGroundingLegDiagnostic right;
    }

    // Fallback에서 후보 자세를 복원하기 전에 발별 원인 수치를 보존함.
    internal sealed class HumanoidFootGroundingLegDiagnostic
    {
        public bool has_ground;
        public int rear_point_id = -1;
        public int front_point_id = -1;
        public bool mixed_ground_collider;
        public int contact_ground_misses;
        public float maximum_ground_normal_angle_deg;
        public float maximum_anchor_ground_shift_m;
        public float pair_span_error_m;
        public float weighted_contact_error_m;
        public float lift_m;
        public float lifted_contact_error_m;
        public float foot_offset_m;
        public float offset_contact_error_m;
        public float hip_to_target_m;
        public float damped_reach_m;
        public float physical_reach_m;
        public float target_error_m;
        public float supported_contact_error_m;
        public int supported_contact_count;

        internal void Reset()
        {
            has_ground = false;
            rear_point_id = front_point_id = -1;
            mixed_ground_collider = false;
            contact_ground_misses = 0;
            maximum_ground_normal_angle_deg = maximum_anchor_ground_shift_m = 0f;
            pair_span_error_m = weighted_contact_error_m = 0f;
            lift_m = lifted_contact_error_m = foot_offset_m = offset_contact_error_m = 0f;
            hip_to_target_m = damped_reach_m = physical_reach_m = target_error_m = 0f;
            supported_contact_error_m = 0f;
            supported_contact_count = 0;
        }
    }
}