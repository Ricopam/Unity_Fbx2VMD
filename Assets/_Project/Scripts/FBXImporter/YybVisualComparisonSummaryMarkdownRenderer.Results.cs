using System;
using System.Globalization;
using System.Text;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class YybVisualComparisonSummaryMarkdownRenderer
    {

        private static void AppendResults(
            StringBuilder builder,
            YybVisualComparisonCaptureResultData[] results)
        {
            builder.AppendLine("## Results");
            builder.AppendLine();
            builder.AppendLine("| job | scene | target | success | session | csv | frames | vmd |");
            builder.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (YybVisualComparisonCaptureResultData result in results ?? Array.Empty<YybVisualComparisonCaptureResultData>())
            {
                if (result == null)
                {
                    continue;
                }

                builder.AppendLine(
                    $"| {EscapeMarkdown(result.jobDisplayName)} | {EscapeMarkdown(result.sceneName)} | {EscapeMarkdown(result.targetName)} | {result.success} | " +
                    $"`{EscapeMarkdown(result.comparisonSessionId)}` | `{EscapeMarkdown(result.comparisonMetricsCsvPath)}` | " +
                    $"`{EscapeMarkdown(result.comparisonFrameFolderPath)}` | `{EscapeMarkdown(result.vmdPath)}` |");
            }
        }

        private static void AppendEffectiveSettings(
            StringBuilder builder,
            YybVisualComparisonCaptureResultData[] results)
        {
            if (!HasEffectiveSettings(results))
            {
                return;
            }

            builder.AppendLine();
            builder.AppendLine("## Main Scene Effective Settings");
            builder.AppendLine();
            builder.AppendLine("| job | foot local rot | full-body pose | body rot | lower segment | foot yaw | post-set endpoint | pre-set endpoint | evaluator X/Z | arm swing | sleeve | visual twist |");
            builder.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (YybVisualComparisonCaptureResultData result in results)
            {
                if (result == null || !result.hasFBXVmdPipelineEffectiveSettings)
                {
                    continue;
                }

                builder.AppendLine(
                    $"| {EscapeMarkdown(result.jobDisplayName)} | " +
                    $"{VisualComparisonSummaryValueFormatter.FormatEnabledWeight(result.ShouldUseManualAnimatorFootLocalRotationReference, result.manualAnimatorFootLocalRotationReferenceWeight)} | " +
                    $"{VisualComparisonSummaryValueFormatter.FormatEnabledWeight(result.ShouldUseManualAnimatorFullBodyPoseReference, result.manualAnimatorFullBodyPoseReferenceWeight)} | " +
                    $"{VisualComparisonSummaryValueFormatter.FormatEnabledWeight(result.ShouldUseManualAnimatorBodyRotationReference, result.manualAnimatorBodyRotationReferenceWeight)} | " +
                    $"{VisualComparisonSummaryValueFormatter.FormatEnabledWeightCap(result.ShouldUseManualAnimatorLowerBodySegmentDirectionReference, result.manualAnimatorLowerBodySegmentDirectionReferenceWeight, result.manualAnimatorLowerBodySegmentDirectionReferenceMaxAngle)} | " +
                    $"{VisualComparisonSummaryValueFormatter.FormatEnabledWeightCap(result.ShouldUseManualAnimatorFootHipsAlignedResidualYawReference, result.manualAnimatorFootHipsAlignedResidualYawReferenceWeight, result.manualAnimatorFootHipsAlignedResidualYawReferenceMaxAngle)} | " +
                    $"{VisualComparisonSummaryValueFormatter.FormatEnabledWeightCapScaleBlendGate(result.usePostSetHumanPoseRightEndpointPositionReference, result.postSetHumanPoseRightEndpointPositionReferenceWeight, result.postSetHumanPoseRightEndpointPositionReferenceMaxOffset, result.postSetHumanPoseRightEndpointPositionReferencePositiveZScale, result.postSetHumanPoseRightEndpointPositionReferenceToesBlendWeight, result.postSetHumanPoseRightEndpointPositionReferenceFrameGateStart, result.postSetHumanPoseRightEndpointPositionReferenceFrameGateEnd)} | " +
                    $"{VisualComparisonSummaryValueFormatter.FormatEnabledWeightCapScaleBlendGate(result.usePreSetHumanPoseRightEndpointPositionReference, result.preSetHumanPoseRightEndpointPositionReferenceWeight, result.preSetHumanPoseRightEndpointPositionReferenceMaxOffset, result.preSetHumanPoseRightEndpointPositionReferencePositiveZScale, result.preSetHumanPoseRightEndpointPositionReferenceToesBlendWeight, result.preSetHumanPoseRightEndpointPositionReferenceFrameGateStart, result.preSetHumanPoseRightEndpointPositionReferenceFrameGateEnd)} | " +
                    $"{FormatEvaluatorXzReferenceSettings(result)} | " +
                    $"{FormatArmSwingSettings(result)} | " +
                    $"{result.enableYybArmSleeveAnchorCorrection} | " +
                    $"{result.enableYybArmVisualTwistCorrection} |");
            }
        }

        private static bool HasEffectiveSettings(
            YybVisualComparisonCaptureResultData[] results)
        {
            foreach (YybVisualComparisonCaptureResultData result in results ?? Array.Empty<YybVisualComparisonCaptureResultData>())
            {
                if (result != null && result.hasFBXVmdPipelineEffectiveSettings)
                {
                    return true;
                }
            }
            return false;
        }

        private static void AppendSampleOrderingDiagnostics(
            StringBuilder builder,
            VisualComparisonSampleOrderingDiagnosticData[] diagnostics)
        {
            if (diagnostics == null || diagnostics.Length == 0)
            {
                return;
            }

            builder.AppendLine();
            builder.AppendLine("## Sample Ordering Diagnostics");
            builder.AppendLine();
            builder.AppendLine("| job | scene | rows | first reason | first recorderFrame | first engine frame | recorder span | engine span | first clip time | first grounding step | first step/max | first step at max | grounding clamp delta | grounding smooth delta | finish recorderFrame | finish engine frame |");
            builder.AppendLine("|---|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|---:|");
            foreach (VisualComparisonSampleOrderingDiagnosticData diagnostic in diagnostics)
            {
                if (diagnostic == null)
                {
                    continue;
                }

                builder.AppendLine(
                    $"| {EscapeMarkdown(diagnostic.job_mode)} | {EscapeMarkdown(diagnostic.scene_name)} | {diagnostic.metric_row_count} | " +
                    $"{EscapeMarkdown(diagnostic.first_metric_reason)} | {diagnostic.first_metric_recorder_frame} | " +
                    $"{diagnostic.first_metric_engine_frame_count} | {diagnostic.recording_metric_recorder_frame_span} | " +
                    $"{diagnostic.recording_metric_engine_frame_span} | {FormatQualityFloat(diagnostic.first_metric_animation_clip_time)} | " +
                    $"{FormatQualityFloat(diagnostic.first_metric_grounding_vertical_step_last)} | " +
                    $"{FormatQualityFloat(diagnostic.first_metric_grounding_vertical_step_to_max_ratio)} | " +
                    $"{diagnostic.first_metric_grounding_vertical_step_at_max_step} | " +
                    $"{diagnostic.recording_grounding_step_clamp_delta} | {diagnostic.recording_grounding_smoothed_delta} | " +
                    $"{diagnostic.finish_metric_recorder_frame} | {diagnostic.finish_metric_engine_frame_count} |");
            }
        }

        private static void AppendSelectedCandidate(
            StringBuilder builder,
            VisualComparisonCandidateArtifactSelectionData selectedCandidate)
        {
            if (selectedCandidate == null || string.IsNullOrWhiteSpace(selectedCandidate.selected_candidate_vmd_path))
            {
                return;
            }

            builder.AppendLine();
            builder.AppendLine("## Selected Candidate Artifact");
            builder.AppendLine();
            builder.AppendLine("| selected role | output role | status | acceptance artifact | metrics | vmd | manifest | files | raw status | corrected status | preserves raw diagnostic | basis |");
            builder.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|");
            builder.AppendLine(
                $"| {EscapeMarkdown(selectedCandidate.selected_candidate_role)} | {EscapeMarkdown(selectedCandidate.selected_candidate_output_role)} | " +
                $"{EscapeMarkdown(selectedCandidate.selected_candidate_status)} | {selectedCandidate.selected_candidate_is_acceptance_artifact} | " +
                $"`{EscapeMarkdown(selectedCandidate.selected_candidate_metrics_csv)}` | " +
                $"`{EscapeMarkdown(selectedCandidate.selected_candidate_vmd_path)}` | " +
                $"`{EscapeMarkdown(selectedCandidate.selected_candidate_manifest_path)}` | " +
                $"vmd={selectedCandidate.selected_candidate_vmd_exists}, metrics={selectedCandidate.selected_candidate_metrics_exists}, manifest={selectedCandidate.selected_candidate_manifest_exists}, rawVmdDiff={selectedCandidate.selected_candidate_differs_from_raw_vmd}, rawMetricsDiff={selectedCandidate.selected_candidate_differs_from_raw_metrics} | " +
                $"{EscapeMarkdown(selectedCandidate.raw_candidate_status)} | {EscapeMarkdown(selectedCandidate.corrected_candidate_status)} | " +
                $"{selectedCandidate.selected_candidate_preserves_raw_diagnostic} | " +
                $"{EscapeMarkdown(selectedCandidate.selected_candidate_acceptance_basis)}; {EscapeMarkdown(selectedCandidate.selection_basis)} |");
        }

        private static void AppendFrameQualityGate(
            StringBuilder builder,
            MotionComparisonFrameQualitySummary[] summaries)
        {
            if (summaries == null || summaries.Length == 0)
            {
                return;
            }

            builder.AppendLine();
            builder.AppendLine("## Frame Quality Gate");
            builder.AppendLine();
            builder.AppendLine("| baseline | candidate | evaluation | status | mmd | compared frames | foot min Y | root delta | center step | local foot IK min Y | effective foot IK min Y | metrics | mmd screenshot | mmd report | vmd | reason |");
            builder.AppendLine("|---|---|---|---|---:|---:|---:|---:|---:|---:|---|---|---|---|---|---|");
            foreach (MotionComparisonFrameQualitySummary summary in summaries)
            {
                if (summary == null)
                {
                    continue;
                }

                builder.AppendLine(
                    $"| {EscapeMarkdown(summary.baseline_label)} | {EscapeMarkdown(summary.candidate_label)} | {EscapeMarkdown(summary.frame_quality_evaluation_role)} | {EscapeMarkdown(summary.status)} | " +
                    $"{EscapeMarkdown(summary.mmd_result_status)} | {summary.compared_frames} | {FormatQualityFloat(summary.min_candidate_foot_bottom_y)} | " +
                    $"{FormatQualityFloat(summary.max_same_frame_root_position_delta)} | {FormatQualityFloat(summary.max_candidate_vmd_center_step)} | " +
                    $"{FormatQualityFloat(summary.min_candidate_vmd_foot_ik_y)} | {FormatQualityFloat(summary.min_candidate_vmd_effective_foot_ik_y)} | " +
                    $"`{EscapeMarkdown(summary.candidate_metrics_csv)}` | " +
                    $"`{EscapeMarkdown(summary.mmd_after_play_screenshot_path)}` | `{EscapeMarkdown(summary.mmd_report_path)}` | " +
                    $"`{EscapeMarkdown(summary.candidate_vmd_path)}` | " +
                    $"{EscapeMarkdown(summary.status_reason)} |");
            }
        }

        private static void AppendFailures(StringBuilder builder, string[] failures)
        {
            if (failures == null || failures.Length == 0)
            {
                return;
            }

            builder.AppendLine();
            builder.AppendLine("## Failures");
            builder.AppendLine();
            foreach (string failure in failures)
            {
                builder.AppendLine($"- {EscapeMarkdown(failure)}");
            }
        }

        private static string FormatQualityFloat(float value)
        {
            return VisualComparisonSummaryValueFormatter.FormatFloat(value);
        }

        private static string FormatEvaluatorXzReferenceSettings(YybVisualComparisonCaptureResultData result)
        {
            return result == null
                ? "False/n/a"
                : $"{result.usePostSetHumanPoseRightFootEvaluatorXzReference}/{FormatQualityFloat(result.postSetHumanPoseRightFootEvaluatorXzReferenceTargetMagnitude)}";
        }

        private static string FormatArmSwingSettings(YybVisualComparisonCaptureResultData result)
        {
            if (result == null)
            {
                return "n/a";
            }

            return
                $"{result.enableYybArmSwingLimitCorrection}/" +
                $"{FormatQualityFloat(result.yybArmSwingLimitWeight)}/" +
                $"{FormatQualityFloat(result.yybArmSwingMaxDownDot)}/" +
                $"{FormatQualityFloat(result.yybArmSwingMinHandHorizontalRatio)}/" +
                $"{FormatQualityFloat(result.yybArmSwingMaxHandBelowShoulderRatio)}/" +
                $"{FormatQualityFloat(result.yybArmSwingHorizontalReachLimitWeight)}/" +
                $"{FormatQualityFloat(result.yybArmSwingMaxHandHorizontalReachRatio)}/" +
                $"{FormatQualityFloat(result.yybArmSwingHorizontalReachMaxHandBelowShoulderRatio)}/" +
                $"{FormatQualityFloat(result.yybArmSwingHorizontalReachMinElbowAngleAfterApply)}/" +
                $"{FormatQualityFloat(result.yybArmSwingRaisedPoseHorizontalReachLimitWeight)}/" +
                $"{FormatQualityFloat(result.yybArmSwingRaisedPoseMinUpperArmDownDot)}/" +
                $"{FormatQualityFloat(result.yybArmSwingRaisedPoseMaxHandBelowShoulderRatio)}/" +
                $"{FormatQualityFloat(result.yybArmSwingRaisedPoseMaxHandHorizontalReachRatio)}";
        }

        private static string FormatGeneratedAt(string generatedAt)
        {
            return DateTime.TryParse(
                generatedAt,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime parsed)
                ? parsed.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                : generatedAt ?? string.Empty;
        }

        private static string FormatRuntimeOverride(float value)
        {
            return VmdIkDeltaGuardRuntimeOverrideApplier.HasLimit(value)
                ? value.ToString("0.###", CultureInfo.InvariantCulture)
                : "none";
        }

        private static string FormatRuntimeOverride(int value)
        {
            return value > 0 ? value.ToString(CultureInfo.InvariantCulture) : "none";
        }

        private static string FormatFramingOverride(float value)
        {
            return VisualComparisonScreenshotOverridePolicy.HasFiniteFramingOverride(value)
                ? value.ToString("0.###", CultureInfo.InvariantCulture)
                : "none";
        }

        private static string EscapeMarkdown(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("|", "\\|");
        }
    }
}
