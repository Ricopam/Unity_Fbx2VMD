using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class HumanoidFootGoalDriver
    {
        // 테스트 진단용 — 마지막 solve의 목표·입력·성공 여부·오차.
        internal Vector3 DebugLeftSolveGoal => _left.LastSolveGoal;
        internal Vector3 LastUsedLeftBendNormal => _left.LastUsedBendNormal;
        internal string DebugLeftSolveInputs => _left.DebugSolveInputs;
        internal bool DebugLeftSolveSucceeded => _left.DebugSolveSucceeded;
        internal float DebugLeftTargetError => _left.DebugTargetError;
        // 캡처 경로에서 핀 가중치가 실제로 올라가는지 진단용.
        internal float DebugLeftWeight => _left.Weight;
        internal float DebugRightWeight => _right.Weight;
        // reach 계산이 현재 프레임에 앵커를 쓰는지 진단용 — 해제 감쇠·weight 소실
        // 뒤에도 true면 다음 재진입의 골반 계산이 오래된 앵커로 오염됨.
        internal bool DebugLeftPinAnchorInReach => _left.TryGetPinAnchor(out _);
        internal bool[] DebugLeftPinArray => _left.Pin;
        // 발 모임 복구 주장 weight — 핀과 무관하게 gate/기권 출력이 두 발을
        // 임계 이하로 모으면 목표 분리쪽으로 solve를 주장함. 진단·테스트용임.
        internal float DebugSeparationWeight => _separationWeight;
        internal Vector3 LastUsedRightBendNormal => _right.LastUsedBendNormal;
    }
}
