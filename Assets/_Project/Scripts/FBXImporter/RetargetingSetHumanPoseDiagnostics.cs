namespace Fbx2Vmd.FBXImporter
{
    internal sealed class RetargetingSetHumanPoseDiagnostics
    {
        internal RetargetingSetHumanPoseInputSnapshot Input = RetargetingSetHumanPoseInputSnapshot.NaN;
        internal RetargetingSetHumanPoseOutputSnapshot Output = RetargetingSetHumanPoseOutputSnapshot.NaN;
        internal RetargetingSetHumanPosePreSolveBasis PreSolveBasis = RetargetingSetHumanPosePreSolveBasis.NaN;

        internal void CaptureInput(RetargetingSetHumanPoseInputSnapshot input)
        {
            Input = input;
            Output.ClearCore();
        }

        internal void CaptureOutput(RetargetingSetHumanPoseOutputSnapshot output)
        {
            Output = output;
        }

        internal void CapturePreSolveBasis(RetargetingSetHumanPosePreSolveBasis basis)
        {
            PreSolveBasis = basis;
        }

        internal void ResetExtendedMuscles()
        {
            Input.ClearExtendedMuscles();
            Output.ClearExtendedMuscles();
        }

        internal void ResetPreSolveBasis()
        {
            PreSolveBasis = RetargetingSetHumanPosePreSolveBasis.NaN;
        }
    }
}
