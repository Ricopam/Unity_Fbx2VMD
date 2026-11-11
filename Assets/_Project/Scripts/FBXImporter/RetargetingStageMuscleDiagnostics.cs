namespace Fbx2Vmd.FBXImporter
{
    // 스테이지별 팔 근육 스냅샷 1벌
    internal struct RetargetingStageMuscleSnapshot
    {
        public float LeftShoulderFrontBack;
        public float LeftArmTwist;
        public float LeftForearmStretch;
        public float RightForearmStretch;
        public float RightArmTwist;

        public static RetargetingStageMuscleSnapshot NaN => new RetargetingStageMuscleSnapshot
        {
            LeftShoulderFrontBack = float.NaN,
            LeftArmTwist = float.NaN,
            LeftForearmStretch = float.NaN,
            RightForearmStretch = float.NaN,
            RightArmTwist = float.NaN
        };
    }

    // 파이프라인 스테이지별 팔 근육 진단 스냅샷
    internal sealed class RetargetingStageMuscleDiagnostics
    {
        internal RetargetingStageMuscleSnapshot PoseInput = RetargetingStageMuscleSnapshot.NaN;
        internal RetargetingStageMuscleSnapshot AfterEditorMuscleReference = RetargetingStageMuscleSnapshot.NaN;
        internal RetargetingStageMuscleSnapshot AfterClampPoseMuscles = RetargetingStageMuscleSnapshot.NaN;
        internal RetargetingStageMuscleSnapshot AfterAnatomicalArmGuard = RetargetingStageMuscleSnapshot.NaN;
        internal RetargetingStageMuscleSnapshot AfterVisualSpikeSmoothing = RetargetingStageMuscleSnapshot.NaN;

        internal void Reset()
        {
            PoseInput = RetargetingStageMuscleSnapshot.NaN;
            AfterEditorMuscleReference = RetargetingStageMuscleSnapshot.NaN;
            AfterClampPoseMuscles = RetargetingStageMuscleSnapshot.NaN;
            AfterAnatomicalArmGuard = RetargetingStageMuscleSnapshot.NaN;
            AfterVisualSpikeSmoothing = RetargetingStageMuscleSnapshot.NaN;
        }
    }
}
