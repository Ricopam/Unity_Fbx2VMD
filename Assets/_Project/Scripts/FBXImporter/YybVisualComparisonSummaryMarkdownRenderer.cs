using System;
using System.Globalization;
using System.Text;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class YybVisualComparisonSummaryMarkdownRenderer
    {
        internal static string Render(
            YybVisualComparisonSummaryData summary,
            float keypointDeltaThreshold)
        {
            if (summary == null)
            {
                throw new ArgumentNullException(nameof(summary));
            }

            StringBuilder builder = new StringBuilder();
            builder.Append(RenderHeader(summary));
            AppendFrameDiagnostics(builder, summary.frame_count_roles, keypointDeltaThreshold);
            AppendResults(builder, summary.results);
            AppendEffectiveSettings(builder, summary.results);
            AppendSampleOrderingDiagnostics(builder, summary.sample_ordering_diagnostics);
            AppendSelectedCandidate(builder, summary.selected_candidate_artifact);
            AppendFrameQualityGate(builder, summary.frame_quality_summaries);
            AppendFailures(builder, summary.failures);
            return builder.ToString();
        }

        private static void AppendSessionLines(StringBuilder builder, YybVisualComparisonSummaryData summary)
        {
            builder.AppendLine($"- session id: `{EscapeMarkdown(summary.session_id)}`");
            builder.AppendLine($"- generated at: `{FormatGeneratedAt(summary.generated_at)}`");
            builder.AppendLine($"- fbx file: `{EscapeMarkdown(summary.fbx_file)}`");
            builder.AppendLine($"- duration seconds: `{summary.duration_seconds:F2}`");
            builder.AppendLine($"- target frames: `{summary.target_frame_count}`");
            builder.AppendLine($"- segment: `{EscapeMarkdown(summary.segment)}`");
            builder.AppendLine($"- finger closeups: `{summary.finger_closeups}`");
            builder.AppendLine($"- recorder parent IK offsets (center-parented): `{summary.recorder_parent_ik_offsets_when_center_parented}`");
            builder.AppendLine($"- MMD IK delta guard runtime override VMD: `{FormatRuntimeOverride(summary.mmd_ik_delta_guard_limit_override_vmd)}`");
            builder.AppendLine($"- MMD IK delta guard recovery trigger VMD: `{FormatRuntimeOverride(summary.mmd_ik_delta_guard_recovery_trigger_vmd)}`");
            builder.AppendLine($"- MMD IK delta guard recovery debt VMD: `{FormatRuntimeOverride(summary.mmd_ik_delta_guard_recovery_debt_vmd)}`");
            builder.AppendLine($"- MMD IK delta guard recovery hold frames: `{FormatRuntimeOverride(summary.mmd_ik_delta_guard_recovery_hold_frames)}`");
            builder.AppendLine($"- Final IK foot grounding runtime override: `{summary.final_ik_foot_grounding_enabled}`");
        }

        private static void AppendManualAnimatorLines(StringBuilder builder, YybVisualComparisonSummaryData summary)
        {
            builder.AppendLine($"- Manual Animator lower-body localRotation runtime override: `{summary.manual_animator_foot_local_rotation_enabled}`");
            builder.AppendLine($"- Manual Animator lower-body localRotation runtime disable: `{summary.manual_animator_foot_local_rotation_disabled}`");
            builder.AppendLine($"- Manual Animator full-body pose runtime override: `{summary.manual_animator_full_body_pose_enabled}`");
            builder.AppendLine($"- Manual Animator full-body pose runtime disable: `{summary.manual_animator_full_body_pose_disabled}`");
            builder.AppendLine($"- Manual Animator full-body pose weight: `{summary.manual_animator_full_body_pose_weight:F3}`");
            builder.AppendLine($"- Manual Animator full-body pose exclude lower-body muscles: `{summary.manual_animator_full_body_pose_exclude_lower_body_muscles}`");
            builder.AppendLine($"- Manual Animator full-body pose lower-body muscles only: `{summary.manual_animator_full_body_pose_lower_body_muscles_only}`");
            builder.AppendLine($"- Manual Animator full-body pose leg twist muscles only: `{summary.manual_animator_full_body_pose_leg_twist_muscles_only}`");
            builder.AppendLine($"- Manual Animator full-body pose right arm muscles only: `{summary.manual_animator_full_body_pose_right_arm_muscles_only}`");
            builder.AppendLine($"- Manual Animator full-body pose left arm muscles only: `{summary.manual_animator_full_body_pose_left_arm_muscles_only}`");
            builder.AppendLine($"- Manual Animator full-body pose right sleeve chain muscles only: `{summary.manual_animator_full_body_pose_right_sleeve_chain_muscles_only}`");
            builder.AppendLine($"- Manual Animator full-body pose frame gate: `{summary.manual_animator_full_body_pose_frame_gate_start:F1}-{summary.manual_animator_full_body_pose_frame_gate_end:F1}`");
        }

        private static void AppendHumanPoseOutputLines(StringBuilder builder, YybVisualComparisonSummaryData summary)
        {
            builder.AppendLine($"- SetHumanPose right leg twist output reference: `{summary.set_human_pose_right_leg_twist_output_enabled}`");
            builder.AppendLine($"- SetHumanPose right leg twist output reference weight: `{summary.set_human_pose_right_leg_twist_output_weight:F3}`");
            builder.AppendLine($"- SetHumanPose right leg twist output reference max delta: `{summary.set_human_pose_right_leg_twist_output_max_delta:F3}`");
        }

        private static void AppendBodyRotationHandLines(StringBuilder builder, YybVisualComparisonSummaryData summary)
        {
            builder.AppendLine($"- Manual Animator body rotation runtime override: `{summary.manual_animator_body_rotation_enabled}`");
            builder.AppendLine($"- Manual Animator body rotation runtime disable: `{summary.manual_animator_body_rotation_disabled}`");
            builder.AppendLine($"- Manual Animator body rotation weight: `{summary.manual_animator_body_rotation_weight:F3}`");
            builder.AppendLine($"- Manual Animator hand local rotation runtime override: `{summary.manual_animator_hand_local_rotation_enabled}`");
            builder.AppendLine($"- Manual Animator thumb local rotation runtime override: `{summary.manual_animator_thumb_local_rotation_enabled}`");
            builder.AppendLine($"- Manual Animator hand palm-frame runtime override: `{summary.manual_animator_hand_palm_frame_enabled}`");
            builder.AppendLine($"- Manual Animator hand palm-frame weight: `{summary.manual_animator_hand_palm_frame_weight:F3}`");
        }

        private static void AppendRetargetSpikeLines(StringBuilder builder, YybVisualComparisonSummaryData summary)
        {
            builder.AppendLine($"- Retarget pose visual spike smoothing runtime settings override: `{summary.retarget_pose_visual_spike_smoothing_override}`");
            builder.AppendLine($"- Retarget pose visual spike smoothing enabled: `{summary.retarget_pose_visual_spike_smoothing_enabled}`");
            builder.AppendLine($"- Retarget pose visual spike current weight: `{summary.retarget_pose_visual_spike_current_weight:F3}`");
            builder.AppendLine($"- Retarget pose visual spike forearm stretch clamp max offset: `{summary.retarget_pose_visual_spike_forearm_stretch_clamp_max_offset:F3}`");
            builder.AppendLine($"- Retarget arm stretch clamp runtime override: `{summary.retarget_arm_stretch_clamp_enabled}`");
            builder.AppendLine($"- Retarget arm stretch muscle limit: `{summary.retarget_arm_stretch_muscle_limit:F3}`");
        }

        private static void AppendYybArmLines(StringBuilder builder, YybVisualComparisonSummaryData summary)
        {
            builder.AppendLine($"- YYB arm swing limit runtime override: `{summary.yyb_arm_swing_limit_enabled}`");
            builder.AppendLine($"- YYB arm swing limit weight: `{summary.yyb_arm_swing_limit_weight:F3}`");
            builder.AppendLine($"- YYB arm swing max down dot: `{summary.yyb_arm_swing_max_down_dot:F3}`");
            builder.AppendLine($"- YYB arm swing min hand horizontal ratio: `{summary.yyb_arm_swing_min_hand_horizontal_ratio:F3}`");
            builder.AppendLine($"- YYB arm swing max hand below shoulder ratio: `{summary.yyb_arm_swing_max_hand_below_shoulder_ratio:F3}`");
            builder.AppendLine($"- YYB arm swing horizontal reach limit weight: `{summary.yyb_arm_swing_horizontal_reach_limit_weight:F3}`");
            builder.AppendLine($"- YYB arm swing max hand horizontal reach ratio: `{summary.yyb_arm_swing_max_hand_horizontal_reach_ratio:F3}`");
            builder.AppendLine($"- YYB arm swing horizontal reach max hand below shoulder ratio: `{summary.yyb_arm_swing_horizontal_reach_max_hand_below_shoulder_ratio:F3}`");
            builder.AppendLine($"- YYB arm swing horizontal reach min elbow angle after apply: `{summary.yyb_arm_swing_horizontal_reach_min_elbow_angle_after_apply:F3}`");
            builder.AppendLine($"- YYB arm swing raised-pose horizontal reach limit weight: `{summary.yyb_arm_swing_raised_pose_horizontal_reach_limit_weight:F3}`");
            builder.AppendLine($"- YYB arm swing raised-pose min upper-arm down dot: `{summary.yyb_arm_swing_raised_pose_min_upper_arm_down_dot:F3}`");
            builder.AppendLine($"- YYB arm swing raised-pose max hand below shoulder ratio: `{summary.yyb_arm_swing_raised_pose_max_hand_below_shoulder_ratio:F3}`");
            builder.AppendLine($"- YYB arm swing raised-pose max hand horizontal reach ratio: `{summary.yyb_arm_swing_raised_pose_max_hand_horizontal_reach_ratio:F3}`");
            builder.AppendLine($"- YYB arm direction retarget runtime override: `{summary.yyb_arm_direction_retarget_enabled}`");
            builder.AppendLine($"- YYB arm direction upper-arm weight: `{summary.yyb_arm_direction_upper_arm_weight:F3}`");
            builder.AppendLine($"- YYB arm direction forearm weight: `{summary.yyb_arm_direction_forearm_weight:F3}`");
            builder.AppendLine($"- YYB arm direction upper-arm max degrees: `{summary.yyb_arm_direction_upper_arm_max_degrees:F3}`");
            builder.AppendLine($"- YYB arm direction forearm max degrees: `{summary.yyb_arm_direction_forearm_max_degrees:F3}`");
            builder.AppendLine($"- YYB arm direction left-side weight scale: `{summary.yyb_arm_direction_left_side_weight_scale:F3}`");
            builder.AppendLine($"- YYB arm direction right-side weight scale: `{summary.yyb_arm_direction_right_side_weight_scale:F3}`");
            AppendYybArmAnchorLines(builder, summary);
        }

        private static void AppendYybArmAnchorLines(StringBuilder builder, YybVisualComparisonSummaryData summary)
        {
            builder.AppendLine($"- YYB arm sleeve anchor runtime settings override: `{summary.yyb_arm_sleeve_anchor_override}`");
            builder.AppendLine($"- YYB arm sleeve anchor runtime enabled: `{summary.yyb_arm_sleeve_anchor_enabled}`");
            builder.AppendLine($"- YYB arm sleeve anchor influence: `{summary.yyb_arm_sleeve_anchor_influence:F3}`");
            builder.AppendLine($"- YYB arm shoulder cap anchor influence: `{summary.yyb_arm_shoulder_cap_anchor_influence:F3}`");
            builder.AppendLine($"- YYB arm sleeve anchor max degrees: `{summary.yyb_arm_sleeve_anchor_max_degrees:F3}`");
            builder.AppendLine($"- YYB arm visual twist runtime settings override: `{summary.yyb_arm_visual_twist_override}`");
            builder.AppendLine($"- YYB arm visual twist runtime enabled: `{summary.yyb_arm_visual_twist_enabled}`");
            builder.AppendLine($"- YYB arm visual upper-arm influence: `{summary.yyb_arm_visual_upper_arm_influence:F3}`");
            builder.AppendLine($"- YYB arm visual forearm influence: `{summary.yyb_arm_visual_forearm_influence:F3}`");
            builder.AppendLine($"- YYB arm visual upper-arm max degrees: `{summary.yyb_arm_visual_upper_arm_max_degrees:F3}`");
            builder.AppendLine($"- YYB arm visual forearm max degrees: `{summary.yyb_arm_visual_forearm_max_degrees:F3}`");
        }

        private static void AppendSegmentDirectionLines(StringBuilder builder, YybVisualComparisonSummaryData summary)
        {
            builder.AppendLine($"- Manual Animator lower-body segment direction runtime override: `{summary.manual_animator_lower_body_segment_direction_enabled}`");
            builder.AppendLine($"- Manual Animator lower-body segment direction runtime disable: `{summary.manual_animator_lower_body_segment_direction_disabled}`");
            builder.AppendLine($"- Manual Animator lower-body segment direction weight: `{summary.manual_animator_lower_body_segment_direction_weight:F3}`");
            builder.AppendLine($"- Manual Animator lower-body segment direction max angle: `{summary.manual_animator_lower_body_segment_direction_max_angle:F3}`");
            builder.AppendLine($"- Manual Animator UpperLegToLowerLeg segment direction runtime disable: `{summary.manual_animator_upper_leg_to_lower_leg_segment_direction_disabled}`");
            builder.AppendLine($"- Manual Animator UpperLegToLowerLeg segment direction max angle override: `{summary.manual_animator_upper_leg_to_lower_leg_segment_direction_max_angle:F3}`");
            builder.AppendLine($"- Manual Animator LowerLegToFoot segment direction runtime disable: `{summary.manual_animator_lower_leg_to_foot_segment_direction_disabled}`");
            builder.AppendLine($"- Manual Animator LowerLegToFoot segment direction max angle override: `{summary.manual_animator_lower_leg_to_foot_segment_direction_max_angle:F3}`");
            builder.AppendLine($"- Manual Animator Left LowerLegToFoot segment direction max angle override: `{summary.manual_animator_left_lower_leg_to_foot_segment_direction_max_angle:F3}`");
            builder.AppendLine($"- Manual Animator Right LowerLegToFoot segment direction max angle override: `{summary.manual_animator_right_lower_leg_to_foot_segment_direction_max_angle:F3}`");
            builder.AppendLine($"- Manual Animator Right LowerLegToFoot segment direction axis X/Z scale: `{summary.manual_animator_right_lower_leg_to_foot_segment_direction_axis_xz_scale:F3}`");
            builder.AppendLine($"- Manual Animator Right LowerLegToFoot segment direction blend weight: `{summary.manual_animator_right_lower_leg_to_foot_segment_direction_blend_weight:F3}`");
            builder.AppendLine($"- Manual Animator Right LowerLegToFoot segment direction frame gate: `{summary.manual_animator_right_lower_leg_to_foot_segment_direction_frame_gate_start:F0}-{summary.manual_animator_right_lower_leg_to_foot_segment_direction_frame_gate_end:F0}`");
            builder.AppendLine($"- Manual Animator Right LowerLegToFoot segment direction endpoint blend weight: `{summary.manual_animator_right_lower_leg_to_foot_segment_direction_endpoint_blend_weight:F3}`");
            builder.AppendLine($"- Manual Animator FootToToes segment direction runtime disable: `{summary.manual_animator_foot_to_toes_segment_direction_disabled}`");
            builder.AppendLine($"- Manual Animator FootToToes segment direction max angle override: `{summary.manual_animator_foot_to_toes_segment_direction_max_angle:F3}`");
            builder.AppendLine($"- Manual Animator foot hips-aligned residual yaw runtime override: `{summary.manual_animator_foot_hips_aligned_residual_yaw_enabled}`");
            builder.AppendLine($"- Manual Animator foot hips-aligned residual yaw runtime disable: `{summary.manual_animator_foot_hips_aligned_residual_yaw_disabled}`");
            builder.AppendLine($"- Manual Animator foot hips-aligned residual yaw weight: `{summary.manual_animator_foot_hips_aligned_residual_yaw_weight:F3}`");
            builder.AppendLine($"- Manual Animator foot hips-aligned residual yaw max angle: `{summary.manual_animator_foot_hips_aligned_residual_yaw_max_angle:F3}`");
        }

        private static void AppendPostEndpointLines(StringBuilder builder, YybVisualComparisonSummaryData summary)
        {
            builder.AppendLine($"- Post-SetHumanPose right endpoint position runtime override: `{summary.post_set_human_pose_right_endpoint_position_enabled}`");
            builder.AppendLine($"- Post-SetHumanPose right endpoint position weight: `{summary.post_set_human_pose_right_endpoint_position_weight:F3}`");
            builder.AppendLine($"- Post-SetHumanPose right endpoint position max offset: `{summary.post_set_human_pose_right_endpoint_position_max_offset:F3}`");
            builder.AppendLine($"- Post-SetHumanPose right endpoint position positive-Z scale: `{summary.post_set_human_pose_right_endpoint_position_positive_z_scale:F3}`");
            builder.AppendLine($"- Post-SetHumanPose right endpoint position toes blend weight: `{summary.post_set_human_pose_right_endpoint_position_toes_blend_weight:F3}`");
            builder.AppendLine($"- Post-SetHumanPose right endpoint position frame gate: `{summary.post_set_human_pose_right_endpoint_position_frame_gate_start:F0}-{summary.post_set_human_pose_right_endpoint_position_frame_gate_end:F0}`");
            builder.AppendLine($"- Post-SetHumanPose endpoint position use left side: `{summary.post_set_human_pose_endpoint_position_use_left_side}`");
        }

        private static void AppendPreEndpointLines(StringBuilder builder, YybVisualComparisonSummaryData summary)
        {
            builder.AppendLine($"- Pre-SetHumanPose right endpoint position runtime override: `{summary.pre_set_human_pose_right_endpoint_position_enabled}`");
            builder.AppendLine($"- Pre-SetHumanPose right endpoint position weight: `{summary.pre_set_human_pose_right_endpoint_position_weight:F3}`");
            builder.AppendLine($"- Pre-SetHumanPose right endpoint position max offset: `{summary.pre_set_human_pose_right_endpoint_position_max_offset:F3}`");
            builder.AppendLine($"- Pre-SetHumanPose right endpoint position positive-Z scale: `{summary.pre_set_human_pose_right_endpoint_position_positive_z_scale:F3}`");
            builder.AppendLine($"- Pre-SetHumanPose right endpoint position toes blend weight: `{summary.pre_set_human_pose_right_endpoint_position_toes_blend_weight:F3}`");
            builder.AppendLine($"- Pre-SetHumanPose right endpoint position frame gate: `{summary.pre_set_human_pose_right_endpoint_position_frame_gate_start:F0}-{summary.pre_set_human_pose_right_endpoint_position_frame_gate_end:F0}`");
            builder.AppendLine($"- Pre-SetHumanPose endpoint position use left side: `{summary.pre_set_human_pose_endpoint_position_use_left_side}`");
            builder.AppendLine($"- Pre-SetHumanPose endpoint position use ghost/current basis: `{summary.pre_set_human_pose_endpoint_position_use_ghost_current_basis}`");
            builder.AppendLine($"- Pre-SetHumanPose endpoint bodyPosition invert X/Z: `{summary.pre_set_human_pose_endpoint_position_invert_body_position_x}/{summary.pre_set_human_pose_endpoint_position_invert_body_position_z}`");
            builder.AppendLine($"- Post-SetHumanPose right foot evaluator X/Z reference: `{summary.post_set_human_pose_right_foot_evaluator_xz_reference_enabled}`");
            builder.AppendLine($"- Post-SetHumanPose right foot evaluator X/Z target magnitude: `{summary.post_set_human_pose_right_foot_evaluator_xz_reference_target_magnitude:F3}`");
            AppendBodyPositionLines(builder, summary);
        }

        private static void AppendBodyPositionLines(StringBuilder builder, YybVisualComparisonSummaryData summary)
        {
            builder.AppendLine($"- Manual Animator BipedIK foot position runtime override: `{summary.manual_animator_biped_ik_foot_position_enabled}`");
            builder.AppendLine($"- Manual Animator BipedIK foot position weight: `{summary.manual_animator_biped_ik_foot_position_weight:F3}`");
            builder.AppendLine($"- Manual Animator BipedIK foot position max offset: `{summary.manual_animator_biped_ik_foot_position_max_offset:F3}`");
            builder.AppendLine($"- Manual Animator Hips local-position runtime override: `{summary.manual_animator_hips_local_position_enabled}`");
            builder.AppendLine($"- Manual Animator Hips local-position weight: `{summary.manual_animator_hips_local_position_weight:F3}`");
            builder.AppendLine($"- Manual Animator Hips local-position max offset: `{summary.manual_animator_hips_local_position_max_offset:F3}`");
            builder.AppendLine($"- Manual Animator bodyPosition X/Z runtime override: `{summary.manual_animator_body_position_xz_enabled}`");
            builder.AppendLine($"- Manual Animator bodyPosition X/Z weight: `{summary.manual_animator_body_position_xz_weight:F3}`");
            builder.AppendLine($"- Manual Animator bodyPosition X/Z max offset: `{summary.manual_animator_body_position_xz_max_offset:F3}`");
            builder.AppendLine($"- Manual Animator bodyPosition X/Z frame gate: `{summary.manual_animator_body_position_xz_frame_gate_start:F0}-{summary.manual_animator_body_position_xz_frame_gate_end:F0}`");
            builder.AppendLine($"- Manual Animator bodyPosition X/Z frame gate blend frames: `{summary.manual_animator_body_position_xz_frame_gate_blend_frames:F0}`");
            builder.AppendLine($"- Manual Animator bodyPosition X/Z axis scale: `{summary.manual_animator_body_position_xz_axis_x_scale:F3}/{summary.manual_animator_body_position_xz_axis_z_scale:F3}`");
            builder.AppendLine($"- Retarget bodyPosition X/Z root motion runtime override: `{summary.retarget_body_position_xz_root_motion_enabled}`");
            builder.AppendLine($"- Target humanoid bone position lock disabled runtime override: `{summary.target_humanoid_bone_position_lock_disabled}`");
            builder.AppendLine($"- VMD playback probe runtime override: `{summary.vmd_playback_probe_enabled}`");
            builder.AppendLine($"- VMD playback probe apply IK targets: `{summary.vmd_playback_probe_apply_ik_targets}`");
            builder.AppendLine($"- VMD playback probe source VMD: `{EscapeMarkdown(summary.vmd_playback_probe_source_vmd_path)}`");
            builder.AppendLine($"- reference MMD timing runtime override: `{summary.reference_mmd_timing_enabled}`");
            builder.AppendLine($"- diagnostic capture width override: `{FormatRuntimeOverride(summary.diagnostic_capture_width_override)}`");
            builder.AppendLine($"- diagnostic capture height override: `{FormatRuntimeOverride(summary.diagnostic_capture_height_override)}`");
            builder.AppendLine($"- diagnostic screenshot padding override: `{FormatFramingOverride(summary.diagnostic_screenshot_padding_override)}`");
            builder.AppendLine($"- diagnostic screenshot viewport center override: `{FormatFramingOverride(summary.diagnostic_screenshot_vertical_viewport_center_override)}`");
            builder.AppendLine($"- reference clip: `{EscapeMarkdown(summary.reference_clip_name)}`");
            builder.AppendLine($"- reference clip asset: `{EscapeMarkdown(summary.reference_clip_asset_path)}`");
            builder.AppendLine();
        }


        internal static string RenderHeader(YybVisualComparisonSummaryData summary)
        {
            if (summary == null)
            {
                throw new ArgumentNullException(nameof(summary));
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# YYB Visual Comparison Batch");
            builder.AppendLine();
            AppendSessionLines(builder, summary);
            AppendManualAnimatorLines(builder, summary);
            AppendHumanPoseOutputLines(builder, summary);
            AppendBodyRotationHandLines(builder, summary);
            AppendRetargetSpikeLines(builder, summary);
            AppendYybArmLines(builder, summary);
            AppendSegmentDirectionLines(builder, summary);
            AppendPostEndpointLines(builder, summary);
            AppendPreEndpointLines(builder, summary);
            return builder.ToString();
        }

        private static void AppendFrameDiagnostics(
            StringBuilder builder,
            VisualComparisonFrameRoleDiagnosticsData diagnostics,
            float keypointDeltaThreshold)
        {
            diagnostics = diagnostics ?? new VisualComparisonFrameRoleDiagnosticsData();
            builder.AppendLine("## Frame Count Roles");
            builder.AppendLine();
            builder.AppendLine($"- ref target: `{diagnostics.reference_target_frame_count}` ({EscapeMarkdown(diagnostics.target_frame_count_role)})");
            builder.AppendLine($"- Sub_Manual baseline recorded frames: `{diagnostics.baseline_recorded_frame_count}` ({EscapeMarkdown(diagnostics.baseline_recorded_frame_count_role)})");
            builder.AppendLine($"- Main_Recoding candidate recorded frames: `{diagnostics.candidate_recorded_frame_count}` ({EscapeMarkdown(diagnostics.candidate_recorded_frame_count_role)})");
            builder.AppendLine($"- metric basis: {EscapeMarkdown(diagnostics.frame_quality_metric_basis)}");
            builder.AppendLine();
            AppendReferenceMp4Diagnostics(builder, diagnostics);
            AppendCandidateRefDiagnostics(builder, diagnostics, keypointDeltaThreshold);
        }

        static void AppendReferenceMp4Diagnostics(
            StringBuilder builder,
            VisualComparisonFrameRoleDiagnosticsData diagnostics)
        {
            builder.AppendLine("## Reference MP4 Diagnostics");
            builder.AppendLine();
            builder.AppendLine($"- provenance: `{EscapeMarkdown(diagnostics.reference_mp4_provenance_evidence_path)}` (exists={diagnostics.reference_mp4_provenance_evidence_exists})");
            builder.AppendLine($"- analysis result: `{EscapeMarkdown(diagnostics.reference_mp4_analysis_result_path)}` (exists={diagnostics.reference_mp4_analysis_result_exists})");
            builder.AppendLine($"- frame metrics: `{EscapeMarkdown(diagnostics.reference_mp4_frame_metrics_path)}` (exists={diagnostics.reference_mp4_frame_metrics_exists})");
            builder.AppendLine($"- contact sheet: `{EscapeMarkdown(diagnostics.reference_mp4_contact_sheet_path)}` (exists={diagnostics.reference_mp4_contact_sheet_exists})");
            builder.AppendLine($"- canonical context: {EscapeMarkdown(diagnostics.reference_mp4_canonical_context)}");
            builder.AppendLine($"- video: `{diagnostics.reference_mp4_width}x{diagnostics.reference_mp4_height}`, `{EscapeMarkdown(diagnostics.reference_mp4_avg_frame_rate)}`, frames `{diagnostics.reference_mp4_total_video_frames}`, duration `{FormatQualityFloat(diagnostics.reference_mp4_stream_duration_seconds)}`");
            builder.AppendLine($"- bbox metrics: samples `{diagnostics.reference_mp4_frame_metrics_sample_count}`, avg height `{FormatQualityFloat(diagnostics.reference_mp4_avg_bbox_height_ratio)}`, avg width `{FormatQualityFloat(diagnostics.reference_mp4_avg_bbox_width_ratio)}`, center X range `{FormatQualityFloat(diagnostics.reference_mp4_center_x_range_ratio)}`, max bottom gap `{FormatQualityFloat(diagnostics.reference_mp4_max_bottom_gap_ratio)}`");
            builder.AppendLine($"- current clip coverage: start `{FormatQualityFloat(diagnostics.reference_mp4_current_clip_start_seconds)}`, end `{FormatQualityFloat(diagnostics.reference_mp4_current_clip_end_seconds)}`, duration `{FormatQualityFloat(diagnostics.reference_mp4_current_clip_duration_seconds)}`, samples `{diagnostics.reference_mp4_current_clip_sample_count}`, first local `{FormatQualityFloat(diagnostics.reference_mp4_current_clip_first_sample_seconds)}`, last local `{FormatQualityFloat(diagnostics.reference_mp4_current_clip_last_sample_seconds)}`, coverage `{FormatQualityFloat(diagnostics.reference_mp4_current_clip_sample_coverage_ratio)}`, gap `{FormatQualityFloat(diagnostics.reference_mp4_current_clip_sample_gap_seconds)}`");
            builder.AppendLine($"- current clip bbox metrics: avg height `{FormatQualityFloat(diagnostics.reference_mp4_current_clip_avg_bbox_height_ratio)}`, avg width `{FormatQualityFloat(diagnostics.reference_mp4_current_clip_avg_bbox_width_ratio)}`, center X range `{FormatQualityFloat(diagnostics.reference_mp4_current_clip_center_x_range_ratio)}`, max bottom gap `{FormatQualityFloat(diagnostics.reference_mp4_current_clip_max_bottom_gap_ratio)}`, avg bright area `{FormatQualityFloat(diagnostics.reference_mp4_current_clip_avg_bright_area_ratio)}`");
            builder.AppendLine($"- current clip coverage basis: {EscapeMarkdown(diagnostics.reference_mp4_current_clip_sample_basis)}");
            builder.AppendLine($"- current clip framing basis: {EscapeMarkdown(diagnostics.reference_mp4_current_clip_framing_metric_basis)}");
            builder.AppendLine($"- candidate screenshot framing: index `{EscapeMarkdown(diagnostics.candidate_screenshot_frame_index_path)}` (exists={diagnostics.candidate_screenshot_frame_index_exists}), view `{EscapeMarkdown(diagnostics.candidate_screenshot_frame_metrics_view)}`, samples `{diagnostics.candidate_screenshot_frame_metrics_sample_count}`, nonblank `{diagnostics.candidate_screenshot_nonblank_frame_count}`, avg height `{FormatQualityFloat(diagnostics.candidate_screenshot_avg_bbox_height_ratio)}`, avg width `{FormatQualityFloat(diagnostics.candidate_screenshot_avg_bbox_width_ratio)}`, center X range `{FormatQualityFloat(diagnostics.candidate_screenshot_center_x_range_ratio)}`, max bottom gap `{FormatQualityFloat(diagnostics.candidate_screenshot_max_bottom_gap_ratio)}`, max top gap `{FormatQualityFloat(diagnostics.candidate_screenshot_max_top_gap_ratio)}`, avg bright area `{FormatQualityFloat(diagnostics.candidate_screenshot_avg_bright_area_ratio)}`");
            builder.AppendLine($"- candidate screenshot timing: samples `{diagnostics.candidate_screenshot_time_sample_count}`, first `{FormatQualityFloat(diagnostics.candidate_screenshot_first_sample_seconds)}`, last `{FormatQualityFloat(diagnostics.candidate_screenshot_last_sample_seconds)}`, coverage `{FormatQualityFloat(diagnostics.candidate_screenshot_sample_coverage_ratio)}`, gap `{FormatQualityFloat(diagnostics.candidate_screenshot_sample_gap_seconds)}`, max ref gap `{FormatQualityFloat(diagnostics.candidate_screenshot_max_ref_sample_seconds_gap)}`, avg ref gap `{FormatQualityFloat(diagnostics.candidate_screenshot_avg_ref_sample_seconds_gap)}`");
        }

        static void AppendCandidateRefDiagnostics(
            StringBuilder builder,
            VisualComparisonFrameRoleDiagnosticsData diagnostics,
            float keypointDeltaThreshold)
        {
            builder.AppendLine($"- candidate/ref time-matched framing: samples `{diagnostics.candidate_vs_reference_time_matched_sample_count}`, max time gap `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_max_seconds_gap)}`, avg bbox height abs delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_avg_bbox_height_ratio_abs_delta)}`, max bbox height abs delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_max_bbox_height_ratio_abs_delta)}`, avg bbox width abs delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_avg_bbox_width_ratio_abs_delta)}`, max bbox width abs delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_max_bbox_width_ratio_abs_delta)}`, avg center X abs delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_avg_center_x_ratio_abs_delta)}`, max bottom gap abs delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_max_bottom_gap_ratio_abs_delta)}`, avg bright area abs delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_avg_bright_area_ratio_abs_delta)}`");
            builder.AppendLine($"- candidate/ref time-matched limb bands: samples `{diagnostics.candidate_vs_reference_time_matched_limb_band_sample_count}`, avg upper span abs delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_avg_upper_limb_span_ratio_abs_delta)}`, max upper span abs delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_max_upper_limb_span_ratio_abs_delta)}`, avg lower span abs delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_avg_lower_limb_span_ratio_abs_delta)}`, max lower span abs delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_max_lower_limb_span_ratio_abs_delta)}`");
            builder.AppendLine($"- candidate/ref time-matched silhouette profile: bands `{diagnostics.candidate_vs_reference_time_matched_silhouette_profile_band_count}`, samples `{diagnostics.candidate_vs_reference_time_matched_silhouette_profile_sample_count}`, avg L1 abs delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_avg_silhouette_profile_l1_abs_delta)}`, max L1 abs delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_max_silhouette_profile_l1_abs_delta)}`, max band abs delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_max_silhouette_profile_band_abs_delta)}`");
            builder.AppendLine($"- candidate/ref time-matched silhouette landmarks: bands `{diagnostics.candidate_vs_reference_time_matched_silhouette_landmark_band_count}`, samples `{diagnostics.candidate_vs_reference_time_matched_silhouette_landmark_sample_count}`, avg endpoint abs delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_avg_silhouette_landmark_endpoint_abs_delta)}`, max endpoint abs delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_max_silhouette_landmark_endpoint_abs_delta)}`");
            builder.AppendLine($"- candidate/ref time-matched image-space keypoints: keypoints `{diagnostics.candidate_vs_reference_time_matched_image_space_keypoint_count}`, samples `{diagnostics.candidate_vs_reference_time_matched_image_space_keypoint_sample_count}`, avg L1 delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_avg_image_space_keypoint_l1_delta)}`, max L1 delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_max_image_space_keypoint_l1_delta)}`");
            builder.AppendLine($"- candidate/ref bbox-normalized keypoints: keypoints `{diagnostics.candidate_vs_reference_time_matched_bbox_normalized_image_space_keypoint_count}`, samples `{diagnostics.candidate_vs_reference_time_matched_bbox_normalized_image_space_keypoint_sample_count}`, avg L1 delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_avg_bbox_normalized_image_space_keypoint_l1_delta)}`, max L1 delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_max_bbox_normalized_image_space_keypoint_l1_delta)}`, avg removed `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_avg_image_space_keypoint_l1_delta_removed_by_bbox_normalization)}`, max removed `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_max_image_space_keypoint_l1_delta_removed_by_bbox_normalization)}`");
            builder.AppendLine($"- candidate/ref non-hair bbox-normalized keypoints: keypoints `{diagnostics.candidate_vs_reference_time_matched_non_hair_bbox_normalized_image_space_keypoint_count}`, samples `{diagnostics.candidate_vs_reference_time_matched_non_hair_bbox_normalized_image_space_keypoint_sample_count}`, avg L1 delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_non_hair_avg_bbox_normalized_image_space_keypoint_l1_delta)}`, max L1 delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_non_hair_max_bbox_normalized_image_space_keypoint_l1_delta)}`, max label `{EscapeMarkdown(diagnostics.candidate_vs_reference_time_matched_non_hair_max_bbox_normalized_image_space_keypoint_label)}`");
            builder.AppendLine($"- candidate/ref bbox-normalized max keypoint attribution: label `{EscapeMarkdown(diagnostics.candidate_vs_reference_time_matched_max_bbox_normalized_image_space_keypoint_label)}`, keypoint `{diagnostics.candidate_vs_reference_time_matched_max_bbox_normalized_image_space_keypoint_index}`, ref seconds `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_max_bbox_normalized_image_space_keypoint_reference_seconds)}`, candidate seconds `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_max_bbox_normalized_image_space_keypoint_candidate_seconds)}`, recorder frame `{diagnostics.candidate_vs_reference_time_matched_max_bbox_normalized_image_space_keypoint_recorder_frame}`, x delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_max_bbox_normalized_image_space_keypoint_x_delta)}`, y delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_max_bbox_normalized_image_space_keypoint_y_delta)}`");
            builder.AppendLine($"- candidate/ref bbox-normalized max keypoint crop context: ref touches edge `{diagnostics.candidate_vs_reference_time_matched_max_bbox_normalized_keypoint_reference_touches_frame_edge}`, candidate touches edge `{diagnostics.candidate_vs_reference_time_matched_max_bbox_normalized_keypoint_candidate_touches_frame_edge}`, ref bottom gap `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_max_bbox_normalized_keypoint_reference_bottom_gap)}`, ref top gap `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_max_bbox_normalized_keypoint_reference_top_gap)}`, candidate bottom gap `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_max_bbox_normalized_keypoint_candidate_bottom_gap)}`, candidate top gap `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_max_bbox_normalized_keypoint_candidate_top_gap)}`");
            builder.AppendLine($"- candidate/ref non-hair bbox-normalized max keypoint attribution: label `{EscapeMarkdown(diagnostics.candidate_vs_reference_time_matched_non_hair_max_bbox_normalized_image_space_keypoint_label)}`, keypoint `{diagnostics.candidate_vs_reference_time_matched_non_hair_max_bbox_normalized_image_space_keypoint_index}`, ref seconds `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_non_hair_max_bbox_normalized_image_space_keypoint_reference_seconds)}`, candidate seconds `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_non_hair_max_bbox_normalized_image_space_keypoint_candidate_seconds)}`, recorder frame `{diagnostics.candidate_vs_reference_time_matched_non_hair_max_bbox_normalized_image_space_keypoint_recorder_frame}`, x delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_non_hair_max_bbox_normalized_image_space_keypoint_x_delta)}`, y delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_non_hair_max_bbox_normalized_image_space_keypoint_y_delta)}`");
            builder.AppendLine($"- candidate/ref non-hair bbox-normalized max keypoint crop context: ref touches edge `{diagnostics.candidate_vs_reference_time_matched_non_hair_max_bbox_normalized_keypoint_reference_touches_frame_edge}`, candidate touches edge `{diagnostics.candidate_vs_reference_time_matched_non_hair_max_bbox_normalized_keypoint_candidate_touches_frame_edge}`, ref bottom gap `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_non_hair_max_bbox_normalized_keypoint_reference_bottom_gap)}`, ref top gap `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_non_hair_max_bbox_normalized_keypoint_reference_top_gap)}`, candidate bottom gap `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_non_hair_max_bbox_normalized_keypoint_candidate_bottom_gap)}`, candidate top gap `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_non_hair_max_bbox_normalized_keypoint_candidate_top_gap)}`");
            builder.AppendLine($"- candidate/ref crop-safe time-matched: samples `{diagnostics.candidate_vs_reference_time_matched_crop_safe_sample_count}`, avg bbox width abs delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_crop_safe_avg_bbox_width_ratio_abs_delta)}`, max bbox width abs delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_crop_safe_max_bbox_width_ratio_abs_delta)}`, silhouette samples `{diagnostics.candidate_vs_reference_time_matched_crop_safe_silhouette_profile_sample_count}`, avg silhouette L1 `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_crop_safe_avg_silhouette_profile_l1_abs_delta)}`, max silhouette L1 `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_crop_safe_max_silhouette_profile_l1_abs_delta)}`");
            builder.AppendLine($"- candidate/ref crop-safe keypoints: image samples `{diagnostics.candidate_vs_reference_time_matched_crop_safe_image_space_keypoint_sample_count}`, avg image L1 `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_crop_safe_avg_image_space_keypoint_l1_delta)}`, max image L1 `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_crop_safe_max_image_space_keypoint_l1_delta)}`, bbox-normalized samples `{diagnostics.candidate_vs_reference_time_matched_crop_safe_bbox_normalized_image_space_keypoint_sample_count}`, avg bbox-normalized L1 `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_crop_safe_avg_bbox_normalized_image_space_keypoint_l1_delta)}`, max bbox-normalized L1 `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_crop_safe_max_bbox_normalized_image_space_keypoint_l1_delta)}`");
            builder.AppendLine($"- candidate/ref keypoint-local crop-safe bbox-normalized keypoints: samples `{diagnostics.candidate_vs_reference_time_matched_keypoint_local_crop_safe_bbox_normalized_image_space_keypoint_sample_count}`, keypoints `{diagnostics.candidate_vs_reference_time_matched_keypoint_local_crop_safe_bbox_normalized_image_space_keypoint_count}`, excluded keypoints `{diagnostics.candidate_vs_reference_time_matched_keypoint_local_crop_safe_bbox_normalized_image_space_keypoint_excluded_count}`, avg L1 `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_keypoint_local_crop_safe_avg_bbox_normalized_image_space_keypoint_l1_delta)}`, max L1 `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_keypoint_local_crop_safe_max_bbox_normalized_image_space_keypoint_l1_delta)}`, max label `{EscapeMarkdown(diagnostics.candidate_vs_reference_time_matched_keypoint_local_crop_safe_max_bbox_normalized_image_space_keypoint_label)}`");
            builder.AppendLine($"- candidate/ref non-hair keypoint-local crop-safe bbox-normalized keypoints: samples `{diagnostics.candidate_vs_reference_time_matched_non_hair_keypoint_local_crop_safe_bbox_normalized_image_space_keypoint_sample_count}`, keypoints `{diagnostics.candidate_vs_reference_time_matched_non_hair_keypoint_local_crop_safe_bbox_normalized_image_space_keypoint_count}`, excluded keypoints `{diagnostics.candidate_vs_reference_time_matched_non_hair_keypoint_local_crop_safe_bbox_normalized_image_space_keypoint_excluded_count}`, avg L1 `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_non_hair_keypoint_local_crop_safe_avg_bbox_normalized_image_space_keypoint_l1_delta)}`, max L1 `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_non_hair_keypoint_local_crop_safe_max_bbox_normalized_image_space_keypoint_l1_delta)}`, max label `{EscapeMarkdown(diagnostics.candidate_vs_reference_time_matched_non_hair_keypoint_local_crop_safe_max_bbox_normalized_image_space_keypoint_label)}`");
            builder.AppendLine($"- candidate/ref non-hair keypoint-local crop-safe max attribution: keypoint `{diagnostics.candidate_vs_reference_time_matched_non_hair_keypoint_local_crop_safe_max_bbox_normalized_image_space_keypoint_index}`, x delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_non_hair_keypoint_local_crop_safe_max_bbox_normalized_image_space_keypoint_x_delta)}`, y delta `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_non_hair_keypoint_local_crop_safe_max_bbox_normalized_image_space_keypoint_y_delta)}`, candidate x/y `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_non_hair_keypoint_local_crop_safe_max_bbox_normalized_image_space_keypoint_candidate_x)}`/`{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_non_hair_keypoint_local_crop_safe_max_bbox_normalized_image_space_keypoint_candidate_y)}`, reference x/y `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_non_hair_keypoint_local_crop_safe_max_bbox_normalized_image_space_keypoint_reference_x)}`/`{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_non_hair_keypoint_local_crop_safe_max_bbox_normalized_image_space_keypoint_reference_y)}`, required x reduction to `{keypointDeltaThreshold:F2}` `{FormatQualityFloat(diagnostics.candidate_vs_reference_time_matched_non_hair_keypoint_local_crop_safe_max_bbox_normalized_image_space_keypoint_required_x_reduction_to_threshold)}`");
            AppendCandidateRefBasisLines(builder, diagnostics);
        }

        private static void AppendCandidateRefBasisLines(
            StringBuilder builder,
            VisualComparisonFrameRoleDiagnosticsData diagnostics)
        {
            builder.AppendLine($"- candidate vs ref framing deltas: avg height `{FormatQualityFloat(diagnostics.candidate_vs_reference_avg_bbox_height_ratio_delta)}`, avg width `{FormatQualityFloat(diagnostics.candidate_vs_reference_avg_bbox_width_ratio_delta)}`, center X range `{FormatQualityFloat(diagnostics.candidate_vs_reference_center_x_range_ratio_delta)}`, max bottom gap `{FormatQualityFloat(diagnostics.candidate_vs_reference_max_bottom_gap_ratio_delta)}`, avg bright area `{FormatQualityFloat(diagnostics.candidate_vs_reference_avg_bright_area_ratio_delta)}`");
            builder.AppendLine($"- candidate vs current-clip ref framing deltas: avg height `{FormatQualityFloat(diagnostics.candidate_vs_reference_current_clip_avg_bbox_height_ratio_delta)}`, avg width `{FormatQualityFloat(diagnostics.candidate_vs_reference_current_clip_avg_bbox_width_ratio_delta)}`, center X range `{FormatQualityFloat(diagnostics.candidate_vs_reference_current_clip_center_x_range_ratio_delta)}`, max bottom gap `{FormatQualityFloat(diagnostics.candidate_vs_reference_current_clip_max_bottom_gap_ratio_delta)}`, avg bright area `{FormatQualityFloat(diagnostics.candidate_vs_reference_current_clip_avg_bright_area_ratio_delta)}`");
            builder.AppendLine($"- candidate screenshot basis: {EscapeMarkdown(diagnostics.candidate_screenshot_frame_metrics_basis)}");
            builder.AppendLine($"- candidate screenshot timing basis: {EscapeMarkdown(diagnostics.candidate_screenshot_sample_timing_basis)}");
            builder.AppendLine($"- candidate/ref time-matched basis: {EscapeMarkdown(diagnostics.candidate_vs_reference_time_matched_framing_metric_basis)}");
            builder.AppendLine($"- candidate/ref image-space limb span basis: {EscapeMarkdown(diagnostics.candidate_vs_reference_time_matched_image_space_limb_span_basis)}");
            builder.AppendLine($"- candidate/ref image-space limb band basis: {EscapeMarkdown(diagnostics.candidate_vs_reference_time_matched_image_space_limb_band_basis)}");
            builder.AppendLine($"- candidate/ref silhouette profile basis: {EscapeMarkdown(diagnostics.candidate_vs_reference_time_matched_silhouette_profile_basis)}");
            builder.AppendLine($"- candidate/ref silhouette landmark basis: {EscapeMarkdown(diagnostics.candidate_vs_reference_time_matched_silhouette_landmark_basis)}");
            builder.AppendLine($"- candidate/ref image-space keypoint basis: {EscapeMarkdown(diagnostics.candidate_vs_reference_time_matched_image_space_keypoint_basis)}");
            builder.AppendLine($"- candidate/ref bbox-normalized keypoint basis: {EscapeMarkdown(diagnostics.candidate_vs_reference_time_matched_bbox_normalized_image_space_keypoint_basis)}");
            builder.AppendLine($"- candidate/ref non-hair bbox-normalized keypoint basis: {EscapeMarkdown(diagnostics.candidate_vs_reference_time_matched_non_hair_bbox_normalized_image_space_keypoint_basis)}");
            builder.AppendLine($"- candidate/ref crop-safe basis: {EscapeMarkdown(diagnostics.candidate_vs_reference_time_matched_crop_safe_basis)}");
            builder.AppendLine($"- candidate/ref keypoint-local crop-safe basis: {EscapeMarkdown(diagnostics.candidate_vs_reference_time_matched_keypoint_local_crop_safe_basis)}");
            builder.AppendLine($"- candidate/ref non-hair keypoint-local crop-safe basis: {EscapeMarkdown(diagnostics.candidate_vs_reference_time_matched_non_hair_keypoint_local_crop_safe_basis)}");
            builder.AppendLine($"- basis: {EscapeMarkdown(diagnostics.reference_mp4_analysis_metric_basis)}");
            builder.AppendLine();
        }
    }
}

