using System;

namespace Fbx2Vmd.FBXImporter
{
    internal static class YybVisualComparisonRunOptionsResetter
    {
        public static void ResetTransientSettings(
            YybVisualComparisonRunOptions options,
            YybVisualComparisonRunOptions defaults)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (defaults == null)
            {
                throw new ArgumentNullException(nameof(defaults));
            }

            var preserved = PreservedFields.From(options);

            YybVisualComparisonRunOptionsCopier.Copy(defaults, options);

            preserved.ApplyTo(options);
        }

        private struct PreservedFields
        {
            private string fbxFileName;
            private float durationSeconds;
            private int targetFrameCount;
            private bool enableFingerCloseups;
            private bool enableRecorderParentFrameIkOffsetsWhenCenterParented;
            private string editorDiagnosticSmokeSegment;
            private int diagnosticCaptureWidthOverride;
            private int diagnosticCaptureHeightOverride;
            private float diagnosticScreenshotPaddingOverride;
            private float diagnosticScreenshotVerticalViewportCenterOverride;

            internal static PreservedFields From(YybVisualComparisonRunOptions options)
            {
                return new PreservedFields
                {
                    fbxFileName = options.fbxFileName,
                    durationSeconds = options.durationSeconds,
                    targetFrameCount = options.targetFrameCount,
                    enableFingerCloseups = options.enableFingerCloseups,
                    enableRecorderParentFrameIkOffsetsWhenCenterParented =
                        options.enableRecorderParentFrameIkOffsetsWhenCenterParented,
                    editorDiagnosticSmokeSegment = options.editorDiagnosticSmokeSegment,
                    diagnosticCaptureWidthOverride = options.diagnosticCaptureWidthOverride,
                    diagnosticCaptureHeightOverride = options.diagnosticCaptureHeightOverride,
                    diagnosticScreenshotPaddingOverride = options.diagnosticScreenshotPaddingOverride,
                    diagnosticScreenshotVerticalViewportCenterOverride =
                        options.diagnosticScreenshotVerticalViewportCenterOverride
                };
            }

            internal void ApplyTo(YybVisualComparisonRunOptions options)
            {
                options.fbxFileName = fbxFileName;
                options.durationSeconds = durationSeconds;
                options.targetFrameCount = targetFrameCount;
                options.enableFingerCloseups = enableFingerCloseups;
                options.enableRecorderParentFrameIkOffsetsWhenCenterParented =
                    enableRecorderParentFrameIkOffsetsWhenCenterParented;
                options.editorDiagnosticSmokeSegment = editorDiagnosticSmokeSegment;
                options.diagnosticCaptureWidthOverride = diagnosticCaptureWidthOverride;
                options.diagnosticCaptureHeightOverride = diagnosticCaptureHeightOverride;
                options.diagnosticScreenshotPaddingOverride = diagnosticScreenshotPaddingOverride;
                options.diagnosticScreenshotVerticalViewportCenterOverride =
                    diagnosticScreenshotVerticalViewportCenterOverride;
            }
        }
    }
}
