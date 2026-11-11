namespace Fbx2Vmd.FBXImporter
{
    /// <summary>손가락/전신 참조 구성의 선택 인자 묶음 — 런타임이 선언해 에디터 구현에 전달한다.</summary>
    internal struct EditorFingerPoseReferenceOptions
    {
        internal bool EnableFingerPoseReference;
        internal bool EnableFullBodyPoseReference;
        internal float FullBodyPoseReferenceWeight;
        internal bool FullBodyPoseExcludeLowerBodyMuscles;
        internal bool FullBodyPoseLowerBodyMusclesOnly;
        internal bool FullBodyPoseLegTwistMusclesOnly;
        internal bool FullBodyPoseRightArmMusclesOnly;
        internal bool FullBodyPoseLeftArmMusclesOnly;
        internal bool FullBodyPoseRightSleeveChainMusclesOnly;
        internal float FullBodyPoseFrameGateStart;
        internal float FullBodyPoseFrameGateEnd;
    }
}
