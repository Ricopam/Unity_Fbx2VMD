namespace Fbx2Vmd.FBXImporter
{
    internal static partial class FBXEditorDiagnosticRiskEvaluator
    {

        internal sealed class Input
        {
            internal string FbxName { get; set; }
            internal bool HasProbe { get; set; }
            internal bool RiskDiagnosticsEnabled { get; set; }
            internal int RiskEvaluationFrameCount { get; set; }
            internal bool HasFullThumbAnatomyCoverage { get; set; }
            internal bool HasResolvedThumbHelperCoverage { get; set; }
            internal bool LeftThumbCoreAnatomyObserved { get; set; }
            internal bool RightThumbCoreAnatomyObserved { get; set; }
            internal bool LeftThumbHelperCoverageRequired { get; set; }
            internal bool RightThumbHelperCoverageRequired { get; set; }
            internal bool LeftThumbHelperCoverageSatisfied { get; set; }
            internal bool RightThumbHelperCoverageSatisfied { get; set; }
            internal float MaxGenericThumbAnatomyRisk { get; set; }
            internal float MaxYybDeformationRisk { get; set; }
            internal float MaxThumbSpreadRisk { get; set; }
            internal float MaxThumbProjectionRisk { get; set; }
            internal float MaxThumbHelperSeparationRisk { get; set; }
            internal float MaxThumbWebbingRisk { get; set; }
            internal float MaxGenericThumbAnatomyRiskThreshold { get; set; }
            internal float MaxYybDeformationRiskThreshold { get; set; }
            internal int NonBlankScreenshotCount { get; set; }
        }

    }
}
