namespace Fbx2Vmd.FBXImporter
{
    // 보정 패스 실행 순서 — 위에서 아래가 실행 순서.
    // 새 패스는 실행되어야 할 위치에 한 줄 추가하고, 번호 간격이 부족하면 해당 구간을 재배정한다.
    internal static class CorrectionPassOrder
    {
        internal const int PoseSpaceRetarget = 20000;
        internal const int ArmDirectionRetarget = 22500;
        internal const int ArmSwingLimit = 23000;
        internal const int ArmTwistRigging = 24000;
        internal const int ArmDeformation = 25000;
        internal const int ArmSleeveAnchor = 25500;
        internal const int ArmVisualTwist = 26000;
        internal const int ThumbDeformation = 29900;
        internal const int LateVisualGrounding = 29950;
        internal const int PlaybackDriver = 32000;
    }
}
