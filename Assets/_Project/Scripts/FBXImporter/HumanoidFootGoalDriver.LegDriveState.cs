using System;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class HumanoidFootGoalDriver
    {
        private sealed partial class LegDrive
        {
            private Vector3 _smoothedGoal;
            private bool _hasSmoothedGoal;
            // pin 재진입 프레임의 목표를 수평 앵커로 유지 — 핀 구간 내내 정박함.
            private Vector3 _pinAnchor;
            private bool _pinAnchorValid;
            // 마지막 핀 프레임의 목표 발 회전 — 해제 감쇠 중 목표 프레임 이행의
            // 기준으로 써서 회전도 위치처럼 weight 비례로 연속 이행하게 함.
            private Quaternion _pinAnchorFrame;
            // 해제 감쇠 진입 표시 — 같은 핀 안에서만 앵커를 유지하고 감쇠 중
            // 재핀되면 새 목표로 앵커를 다시 잡게 함.
            private bool _pinReleased;
            // 발목 로컬 좌표의 밑창 정점 — grounding 샘플러가 넘겨준 측정값임.
            private Vector3[] _soleProbesLocal = Array.Empty<Vector3>();
            // 무릎이 거의 펴진 프레임은 cross가 퇴화해 방향이 프레임마다 뒤집히므로
            // 마지막으로 유효했던 굽힘 법선을 유지함 — stabilizer의 init 폴백과 같은 의도임.
            private Vector3 _lastBendNormal;
            // 해부학 기준 굽힘 법선(허벅지 로컬) — HumanoidLegBendCalibration이
            // 바인드 자세에서 취득한 gate와 같은 규약임. 미설정 시 zero로 두고 폴백함.
            private Vector3 _referenceNormal;
            private float _stableNormalSquaredThreshold;

            // 최근 solve에 실제 사용된 굽힘 법선 — 해부학 기준 정합 결과라 사후
            // 지면 상승 IK도 같은 굽힘면을 쓸 수 있게 컨트롤러에 공개함.
            internal Vector3 LastUsedBendNormal { get; private set; }
            internal string DebugSolveInputs { get; private set; }
            internal bool DebugSolveSucceeded { get; private set; }
            internal float DebugTargetError { get; private set; }

            private Quaternion _supportedFrame;
            internal float SupportWeight { get; private set; }
            internal Vector3 EffectiveGoal { get; private set; }
            private Quaternion _effectiveFrame;
            internal bool[] Pin = Array.Empty<bool>();
            internal bool[] Grounded = Array.Empty<bool>();

            private HumanoidFootRotationBinding _binding;

        }
    }
}
