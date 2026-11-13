using System.Collections;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public partial class VMDRecordingController
    {
        IEnumerator AwaitStartDelay()
        {
            bool earlyShouldStartVmdRecording = ShouldStartVmdRecording(
                _pipeline.ShouldRecordVmdAfterImport);
            float resolvedStartDelay = VMDRecordingController.ResolveStartDelay(_pipeline.startDelay, earlyShouldStartVmdRecording);
            _pipeline.SetSessionState(FBXVmdPipeline.FBXSessionState.Retargeting, $"녹화 시작 전 {resolvedStartDelay:F1}초 대기", 0.7f);
            if (resolvedStartDelay > 0f)
            {
                yield return new WaitForSeconds(resolvedStartDelay);
            }
        }

        bool PrepareRecorderAndPlan(
            GameObject targetObject, ref RecordingPlan plan,
            AnimationClip clip, string outputBaseName,
            out HumanoidSampleCode recorderController
#if UNITY_EDITOR
            , FBXEditorDiagnosticSession editorDiagnosticSession
#endif
            )
        {
#if UNITY_EDITOR
            float[] diagnosticSampleTimesOverride = editorDiagnosticSession.SampleTimesOverride;
#else
            float[] diagnosticSampleTimesOverride = null;
#endif
            if (!TryResolveRecorder(targetObject, out recorderController))
                return false;

            ApplyRecorderDiagnostics(recorderController, diagnosticSampleTimesOverride
#if UNITY_EDITOR
                , editorDiagnosticSession
#endif
                );
#if UNITY_EDITOR
            if (editorDiagnosticSession.IsRecordingOverrideActive)
            {
                ApplyEditorDiagnosticOverrides(
                    ref plan, clip, outputBaseName, editorDiagnosticSession, recorderController);
            }
            else
#endif
            {
                TryApplyMmdReferenceTiming(ref plan, outputBaseName, clip, recorderController);
            }
            return true;
        }

        IEnumerator PrewarmAndCaptureBaseline(
            Animation ghostAnim, AnimationClip clip, PoseSpaceRetargeter retargeter,
            RecordingPlan plan, bool shouldStartVmdRecording)
        {
            int prewarmFrameCount = ResolvePrewarmFrameCountForRecordingMode(
                _pipeline.RetargetPrewarmFrameCount,
                shouldStartVmdRecording);
            int visiblePrewarmYieldFrameCount = ResolveVisiblePrewarmYieldFrameCountForRecordingMode(
                _pipeline.RetargetPrewarmFrameCount,
                shouldStartVmdRecording);
            yield return PrewarmStartPose(
                ghostAnim,
                clip,
                retargeter,
                plan.startTime,
                plan.playbackSpeed,
                prewarmFrameCount,
                visiblePrewarmYieldFrameCount);
            retargeter?.CaptureRecordingStartBaselineSnapshot();
            retargeter?.ResetPlaybackStabilityMetrics();
        }

        void StartAutoRecordingOrFail(RecordingPlan plan, HumanoidSampleCode recorderController)
        {
            _pipeline.SetSessionState(FBXVmdPipeline.FBXSessionState.Recording, $"녹화 중: {plan.outputBaseName}", 0.75f);
            Debug.Log($"[Recording] 자동 녹화 시작됨. VMD={plan.outputBaseName}.vmd, 비교 라벨={plan.comparisonLabel}");
            bool started = recorderController.StartAutoRecording(
                plan.length,
                plan.outputBaseName,
                null,
                plan.targetFrameCount,
                comparisonLabel: plan.comparisonLabel,
                overwriteExistingOutput: true);
            if (!started)
            {
                _pipeline.FailSession("VMD 녹화를 시작하지 못했습니다.");
            }
        }

        struct RecordingPlan
        {
            public float startTime;
            public float playbackSpeed;
            public float length;
            public int targetFrameCount;
            public string outputBaseName;
            public string comparisonLabel;
        }

        bool TryResolveRecorder(GameObject targetObject, out HumanoidSampleCode recorderController)
        {
            recorderController = targetObject.GetComponent<HumanoidSampleCode>();
            if (recorderController == null)
            {
                _pipeline.FailSession("Target Character에 HumanoidSampleCode가 없습니다.");
                return false;
            }

            _activeRecorderController = recorderController;
            _activeRecorderController.RecordingFinished += OnRecordingFinished;
            return true;
        }

        void ApplyEditorDiagnosticOverrides(
            ref RecordingPlan plan, AnimationClip clip, string outputBaseName,
            FBXEditorDiagnosticSession editorDiagnosticSession,
            HumanoidSampleCode recorderController)
        {
            float requestedDuration = Mathf.Max(
                0.1f,
                editorDiagnosticSession.DurationSeconds);
            bool hasEditorSmokeTimingOverride =
                !float.IsNaN(editorDiagnosticSession.RecordingStartTimeOverrideSeconds) ||
                !float.IsNaN(editorDiagnosticSession.RecordingPlaybackSpeedOverride);
            plan.startTime = FBXEditorDiagnosticPlanner.CalculateStartTime(
                clip,
                requestedDuration,
                editorDiagnosticSession.Segment);
            if (!float.IsNaN(editorDiagnosticSession.RecordingStartTimeOverrideSeconds))
            {
                plan.startTime = Mathf.Clamp(
                    editorDiagnosticSession.RecordingStartTimeOverrideSeconds,
                    0f,
                    Mathf.Max(0f, clip.length));
            }

            if (!float.IsNaN(editorDiagnosticSession.RecordingPlaybackSpeedOverride))
            {
                plan.playbackSpeed =
                    editorDiagnosticSession.RecordingPlaybackSpeedOverride;
            }

            float safePlaybackSpeed = Mathf.Max(0.0001f, plan.playbackSpeed);
            float remainingLength = Mathf.Max(0.1f, (clip.length - plan.startTime) / safePlaybackSpeed);
            plan.length = Mathf.Min(requestedDuration, remainingLength);
            plan.targetFrameCount = Mathf.Min(
                Mathf.Max(1, editorDiagnosticSession.TargetFrameCount),
                Mathf.CeilToInt(plan.length * FBXVmdPipeline.EDITOR_DIAGNOSTIC_SMOKE_FRAME_RATE));
            plan.outputBaseName = editorDiagnosticSession.UseInputBaseName
                ? outputBaseName
                : FBXEditorDiagnosticPlanner.BuildOutputBaseName(
                    outputBaseName, plan.length, editorDiagnosticSession.Segment);
            plan.comparisonLabel = $"auto_{plan.outputBaseName}";
            Debug.Log(
                $"[Recording] 에디터 스모크 녹화 제한 적용됨. VMD={plan.outputBaseName}.vmd, " +
                $"segment={FBXEditorDiagnosticPlanner.GetSegmentLabel(editorDiagnosticSession.Segment)}, " +
                $"start={plan.startTime:F2}s, duration={plan.length:F2}s, " +
                $"targetFrameCount={plan.targetFrameCount}");

            TryApplyEditorReferenceTiming(
                ref plan, clip, outputBaseName, editorDiagnosticSession,
                recorderController, hasEditorSmokeTimingOverride);
        }

        void TryApplyEditorReferenceTiming(
            ref RecordingPlan plan, AnimationClip clip, string outputBaseName,
            FBXEditorDiagnosticSession editorDiagnosticSession,
            HumanoidSampleCode recorderController, bool hasEditorSmokeTimingOverride)
        {
            if (!hasEditorSmokeTimingOverride &&
                FBXEditorDiagnosticPlanner.TryBuildKnownReferenceRecordingPlan(
                outputBaseName,
                clip.length,
                plan.length,
                plan.targetFrameCount,
                FBXVmdPipeline.EDITOR_DIAGNOSTIC_SMOKE_FRAME_RATE,
                editorDiagnosticSession.UseKnownReferenceTiming,
                out float referenceRecordingLength,
                out int referenceTargetFrameCount,
                out float referencePlaybackSpeed))
            {
                ApplyReferenceTiming(ref plan, recorderController,
                    referenceRecordingLength, referenceTargetFrameCount, referencePlaybackSpeed);
                Debug.Log(
                    $"[Recording] 에디터 스모크 기준 녹화 시간이 적용됨. " +
                    $"clipLength={clip.length:F3}s, recordingLength={plan.length:F3}s, " +
                    $"targetFrameCount={plan.targetFrameCount}, playbackSpeed={plan.playbackSpeed:F5}");
            }
        }

        void TryApplyMmdReferenceTiming(
            ref RecordingPlan plan, string outputBaseName, AnimationClip clip,
            HumanoidSampleCode recorderController)
        {
            if (TryBuildKnownMmdReferenceRecordingPlan(
                outputBaseName,
                clip.length,
                FBXVmdPipeline.MMD_REFERENCE_FRAME_RATE,
                _pipeline.useKnownMmdReferenceTiming,
                out float referenceRecordingLength,
                out int referenceTargetFrameCount,
                out float referencePlaybackSpeed))
            {
                ApplyReferenceTiming(ref plan, recorderController,
                    referenceRecordingLength, referenceTargetFrameCount, referencePlaybackSpeed);
                Debug.Log(
                    $"[Recording] 기준 녹화 시간이 적용됨. " +
                    $"clipLength={clip.length:F3}s, recordingLength={plan.length:F3}s, " +
                    $"targetFrameCount={plan.targetFrameCount}, playbackSpeed={plan.playbackSpeed:F5}");
            }
        }

        static void ApplyReferenceTiming(
            ref RecordingPlan plan, HumanoidSampleCode recorderController,
            float referenceRecordingLength, int referenceTargetFrameCount,
            float referencePlaybackSpeed)
        {
            plan.length = referenceRecordingLength;
            plan.targetFrameCount = referenceTargetFrameCount;
            plan.playbackSpeed = referencePlaybackSpeed;

            if (recorderController.vmdRecorder != null)
            {
                recorderController.vmdRecorder.UseCaptureFramerateDuringRecording = true;
                recorderController.vmdRecorder.DropLateFrameBacklogWhenNotUsingCaptureFramerate = false;
            }
        }
    }
}
