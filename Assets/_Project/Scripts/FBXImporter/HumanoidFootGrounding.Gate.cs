using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class HumanoidFootGrounding
    {
        // 게이트 임계 근처의 프레임 단위 번복(교정·원본 자세 교번 출력 = 다리 떨림)을 막기 위한
        // 디바운스 상태. 연속 GateTransitionFrames프레임 동일 결과일 때만 상태를 전환한다.
        private bool _gateHoldsFallback;
        private int _gateOppositeStreak;
        private float _lastGateTimeSeconds = -1f;
        private bool _lastGateDecision;

        // 완전 지지 중에는 앵커와 실측 접촉점의 괴리도 게이트 조건임.
        // 지지 중에 부유·관통이 남은 프레임을 Applied로 보고하지 않음.
        // 계측 CSV가 같은 임계로 거절 사유를 재현할 수 있게 비율을 공개함.
        internal const float TargetErrorPerHumanScale = 0.0001f;
        internal const float SoleClearancePerHumanScale = 0.0001f;
        internal const float SupportedContactErrorPerHumanScale = 0.005f;

        internal static bool ResolveApplied(float targetError, float soleClearance,
            float supportedContactError, float humanScale)
        {
            return targetError <= humanScale * TargetErrorPerHumanScale &&
                soleClearance >= -humanScale * SoleClearancePerHumanScale &&
                supportedContactError <= humanScale * SupportedContactErrorPerHumanScale;
        }

        // 전환에 필요한 연속 반대 결과 프레임 수. 측정상 잔여 번복 대역은 전부
        // 정확히 3프레임 연속 실패(최소 섬)이므로 4로 올려 흡수한다.
        private const int GateTransitionFrames = 4;

        // 순차 프레임에서만 상태를 보류한다. 1프레임을 넘는 시간 점프(프레임 탐색)나
        // 무지면 프레임은 즉시 게이트 결과를 사용하고 보류 상태를 현재 결과로 맞춘다.
        private bool ApplyGateHold(bool gatePass, float timeSeconds)
        {
            // 캡처처럼 같은 시각을 재평가하는 경로는 첫 판정을 재사용해
            // 한 프레임이 스트릭을 두 번 올리지 않게 한다.
            if (timeSeconds == _lastGateTimeSeconds) return _lastGateDecision;
            bool jumped = _frameRate > 0f && Mathf.Abs(timeSeconds - _lastGateTimeSeconds) > 1.5f / _frameRate;
            _lastGateTimeSeconds = timeSeconds;
            bool decision;
            if (jumped || !HasGround)
            {
                _gateHoldsFallback = !gatePass;
                _gateOppositeStreak = 0;
                decision = gatePass; } else if (gatePass == !_gateHoldsFallback)
            {
                _gateOppositeStreak = 0;
                decision = gatePass; } else
            {
                _gateOppositeStreak++;
                if (_gateOppositeStreak < GateTransitionFrames)
                {
                    decision = !_gateHoldsFallback; } else
                {
                    _gateHoldsFallback = !_gateHoldsFallback;
                    _gateOppositeStreak = 0;
                    decision = gatePass; } }
            _lastGateDecision = decision;
            return decision; }
    }
}
