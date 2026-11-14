using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class HumanoidMotionPlaybackController
    {
        // 게이트 접지 보정 실행 + 진단 스냅샷 갱신. earlyResolved=true면 호출자는 즉시 return true.
        private bool ApplyGroundingGate(
            bool goalDriverActive, bool driverClaims,
            Vector3 leftBendNormal, Vector3 rightBendNormal,
            out bool earlyResolved)
        {
            earlyResolved = false;
            bool gateApplied = false;
            if (!_isGroundResponseEnabled ||
                _footGrounding == null || !_footGrounding.IsPrepared)
            {
                return gateApplied;
            }

            gateApplied = _footGrounding.TryApply(CurrentTimeSeconds, _groundResponse);
            CaptureGateDiagnostics(gateApplied);
            if (!gateApplied)
            {
                LastGroundingStatus = HumanoidFootGroundingStatus.Fallback;
                return gateApplied;
            }

            if (goalDriverActive && _footGrounding.RawGatePass)
            {
                _footGoalDriver.CaptureValidatedSupport(
                    _footGrounding.LeftDiagnostic.has_ground &&
                    _footGrounding.LeftDiagnostic.supported_contact_count > 0,
                    _footGrounding.RightDiagnostic.has_ground &&
                    _footGrounding.RightDiagnostic.supported_contact_count > 0);
            }
            LastGroundingStatus = _footGrounding.HasGround
                ? HumanoidFootGroundingStatus.Applied : HumanoidFootGroundingStatus.NoGround;
            if (driverClaims) return gateApplied;
            if (goalDriverActive)
            {
                // gate 적용 프레임도 드라이버 시간·weight는 매 프레임 갱신해
                // 다음 pin 진입의 램프와 점프 판정이 끊기지 않게 함.
                _footGoalDriver.TryApply(CurrentTimeSeconds, _footGoalAlpha,
                    leftBendNormal, rightBendNormal,
                    _isGroundResponseEnabled ? _groundResponse : null);
                // 핀 기권 중에도 발 모임 복구가 자세를 바꾸므로 최종 밑창 제약을 다시 적용함.
                _groundResponse.Apply(
                    _footGoalDriver.LastUsedLeftBendNormal,
                    _footGoalDriver.LastUsedRightBendNormal, _footGrounding);
            }
            earlyResolved = true;
            return gateApplied;
        }

        private void CaptureGateDiagnostics(bool gateApplied)
        {
            LastGroundingGate.evaluation_stage = _footGrounding.EvaluationStage;
            LastGroundingGate.measured = _footGrounding.EvaluationStage == "gate";
            LastGroundingGate.raw_pass = _footGrounding.RawGatePass;
            LastGroundingGate.held_pass = gateApplied;
            LastGroundingGate.used_physical_reach = _footGrounding.UsedPhysicalReach;
            LastGroundingGate.pelvis_offset_m = _footGrounding.PelvisOffset;
            LastGroundingGate.pelvis_maximum_offset_m = _footGrounding.PelvisMaximumOffset;
            LastGroundingGate.left = _footGrounding.LeftDiagnostic;
            LastGroundingGate.right = _footGrounding.RightDiagnostic;
            LastGroundingGate.target_error_m = _footGrounding.MaximumTargetError;
            LastGroundingGate.sole_clearance_m = _footGrounding.MinimumSoleClearance;
            LastGroundingGate.supported_contact_error_m =
                _footGrounding.MaximumSupportedContactError;
            LastGroundingGate.supported_contact_count =
                _footGrounding.FullySupportedContactCount;
        }

        // 드라이버 오버레이: pin 프레임만 IK가 목표를 주장하고 기권 구간은 gate/stabilizer 출력이 유지됨.
        private void ApplyGoalDriverOverlay(
            bool goalDriverActive, bool driverClaims,
            bool gateApplied, bool isApplied,
            Vector3 leftBendNormal, Vector3 rightBendNormal)
        {
            if (goalDriverActive)
            {
                _footGoalDriver.TryApply(CurrentTimeSeconds, _footGoalAlpha,
                    leftBendNormal, rightBendNormal,
                    _isGroundResponseEnabled ? _groundResponse : null);
                // 주장 프레임의 골반 오프셋은 gate+드라이버 누적값을 보고함.
                if (driverClaims)
                {
                    LastGroundingGate.pelvis_offset_m +=
                        _footGoalDriver.LastAppliedPelvisOffset;
                }
                // 지면 하방 침투 차단은 goal 경로에도 동일하게 적용 — 발 하단이
                // 지면 아래일 때만 작동해 pin 프레임의 IK 결과는 건드리지 않음.
                // 굽힘 법선은 드라이버의 해부학 정합 결과를 사용 — 원시 자세
                // cross는 무릎이 거의 펴진 구간에서 부호가 뒤집혀 상승 IK가
                // 무릎을 반대편에 배치함. 미solve 다리는 자세 cross 폴백으로
                // 드라이버 이후의 현재 자세 기준 방향을 유지함.
                if ((isApplied || gateApplied) && _isGroundResponseEnabled)
                    _groundResponse.Apply(
                        _footGoalDriver.LastUsedLeftBendNormal,
                        _footGoalDriver.LastUsedRightBendNormal, _footGrounding);
                // goal 경로는 gate 보정 대신 평가 전용 갱신으로 지면·지지 가중치·
                // 밑창 측정을 현재 프레임 기준으로 채움 — 스냅샷이 마지막 gate 평가나
                // 초기값을 재사용하지 않게 함. gate 판정 열은 계속 비움(measured=false).
                // 기권 프레임은 gate가 이미 진단을 채웠으므로 평가 갱신을 건너뜀.
                // grounding이 준비되지 않은 구성에서는 스냅샷을 만들 수 없으므로
                // Unavailable을 유지해 측정 불가를 정직하게 신호함.
                if (driverClaims && _footGrounding != null && _footGrounding.IsPrepared)
                {
                    bool evaluated = _footGrounding.TryEvaluate(
                        CurrentTimeSeconds, _groundResponse);
                    LastGroundingGate.evaluation_stage = evaluated
                        ? "ik_goal_drive" : $"ik_goal_{_footGrounding.EvaluationStage}";
                    LastGroundingGate.left = _footGrounding.LeftDiagnostic;
                    LastGroundingGate.right = _footGrounding.RightDiagnostic;
                    LastGroundingStatus = !evaluated
                        ? HumanoidFootGroundingStatus.Fallback
                        : _footGrounding.HasGround
                            ? HumanoidFootGroundingStatus.Applied
                            : HumanoidFootGroundingStatus.NoGround;
                }
            }
            else if (isApplied && _isGroundResponseEnabled)
            {
                _groundResponse.Apply(leftBendNormal, rightBendNormal, _footGrounding);
            }
        }

    }
}
