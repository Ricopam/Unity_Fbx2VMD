using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 에디터 수동 참조 하체 세그먼트 방향 보정의 진단 스냅샷.
    // 런타임 소유 홀더 — 에디터 드라이버가 기록하고 public 진단 프로퍼티가 위임으로 읽는다.
    internal sealed class RetargetingLowerBodySegmentDiagnostics
    {
        internal float LeftFootXzDelta = float.NaN;
        internal float RightFootXzDelta = float.NaN;
        internal string MaxCorrectionSegment = string.Empty;
        internal float MaxCorrectionAngle = float.NaN;
        internal float MaxPreAngle = float.NaN;
        internal float MaxPostAngle = float.NaN;
        internal Vector3 MaxCorrectionAxis = BuildNaNVector3();
        internal Vector3 MaxReferenceDirection = BuildNaNVector3();
        internal Vector3 MaxPreDirection = BuildNaNVector3();
        internal Vector3 MaxPostDirection = BuildNaNVector3();
        internal float LeftUpperLegLowerLegCorrectionAngle = float.NaN;
        internal float RightUpperLegLowerLegCorrectionAngle = float.NaN;
        internal float LeftLowerLegFootCorrectionAngle = float.NaN;
        internal float RightLowerLegFootCorrectionAngle = float.NaN;
        internal float LeftFootToesCorrectionAngle = float.NaN;
        internal float RightFootToesCorrectionAngle = float.NaN;
        internal float LeftLowerLegToFootParentWorldRotationDeltaAngle = float.NaN;
        internal float RightLowerLegToFootParentWorldRotationDeltaAngle = float.NaN;
        internal float LeftLowerLegToFootChildFootLocalRotationDeltaAngle = float.NaN;
        internal float RightLowerLegToFootChildFootLocalRotationDeltaAngle = float.NaN;
        internal Vector3 LeftFootToToesReferenceDirection = BuildNaNVector3();
        internal Vector3 LeftFootToToesPreDirection = BuildNaNVector3();
        internal Vector3 LeftFootToToesPostDirection = BuildNaNVector3();
        internal Vector3 RightFootToToesReferenceDirection = BuildNaNVector3();
        internal Vector3 RightFootToToesPreDirection = BuildNaNVector3();
        internal Vector3 RightFootToToesPostDirection = BuildNaNVector3();
        internal Vector3 LeftLowerLegWorldPosition = BuildNaNVector3();
        internal Vector3 LeftFootWorldPosition = BuildNaNVector3();
        internal Vector3 LeftToesWorldPosition = BuildNaNVector3();
        internal Vector3 RightLowerLegWorldPosition = BuildNaNVector3();
        internal Vector3 RightFootWorldPosition = BuildNaNVector3();
        internal Vector3 RightToesWorldPosition = BuildNaNVector3();
        internal Vector3 LeftLowerLegToFootCorrectionAxis = BuildNaNVector3();
        internal Vector3 RightLowerLegToFootCorrectionAxis = BuildNaNVector3();
        internal Vector3 LeftFootForward = BuildNaNVector3();
        internal Vector3 LeftFootUp = BuildNaNVector3();
        internal Vector3 RightFootForward = BuildNaNVector3();
        internal Vector3 RightFootUp = BuildNaNVector3();

        // 프레임 리셋 경로에서 매번 초기화되는 발 XZ 이동량만 리셋
        internal void ResetFootXzDeltas()
        {
            LeftFootXzDelta = float.NaN;
            RightFootXzDelta = float.NaN;
        }

        // 상세 진단 전체 리셋 — 원본 ResetEditorLowerBodySegmentDirectionDetailedDiagnostics와 동일 집합
        internal void ResetDetailed()
        {
            MaxCorrectionSegment = string.Empty;
            (MaxCorrectionAngle, MaxPreAngle, MaxPostAngle) = (float.NaN, float.NaN, float.NaN);
            (MaxCorrectionAxis, MaxReferenceDirection, MaxPreDirection, MaxPostDirection) =
                (BuildNaNVector3(), BuildNaNVector3(), BuildNaNVector3(), BuildNaNVector3());
            (LeftUpperLegLowerLegCorrectionAngle, RightUpperLegLowerLegCorrectionAngle,
                LeftLowerLegFootCorrectionAngle, RightLowerLegFootCorrectionAngle,
                LeftFootToesCorrectionAngle, RightFootToesCorrectionAngle) =
                (float.NaN, float.NaN, float.NaN, float.NaN, float.NaN, float.NaN);
            (LeftLowerLegToFootParentWorldRotationDeltaAngle, RightLowerLegToFootParentWorldRotationDeltaAngle,
                LeftLowerLegToFootChildFootLocalRotationDeltaAngle, RightLowerLegToFootChildFootLocalRotationDeltaAngle) =
                (float.NaN, float.NaN, float.NaN, float.NaN);
            (LeftFootToToesReferenceDirection, LeftFootToToesPreDirection, LeftFootToToesPostDirection) =
                (BuildNaNVector3(), BuildNaNVector3(), BuildNaNVector3());
            (RightFootToToesReferenceDirection, RightFootToToesPreDirection, RightFootToToesPostDirection) =
                (BuildNaNVector3(), BuildNaNVector3(), BuildNaNVector3());
            (LeftLowerLegWorldPosition, LeftFootWorldPosition, LeftToesWorldPosition,
                RightLowerLegWorldPosition, RightFootWorldPosition, RightToesWorldPosition) =
                (BuildNaNVector3(), BuildNaNVector3(), BuildNaNVector3(), BuildNaNVector3(), BuildNaNVector3(), BuildNaNVector3());
            (LeftLowerLegToFootCorrectionAxis, RightLowerLegToFootCorrectionAxis) = (BuildNaNVector3(), BuildNaNVector3());
            (LeftFootForward, LeftFootUp, RightFootForward, RightFootUp) =
                (BuildNaNVector3(), BuildNaNVector3(), BuildNaNVector3(), BuildNaNVector3());
        }

        private static Vector3 BuildNaNVector3()
        {
            return new Vector3(float.NaN, float.NaN, float.NaN);
        }
    }
}
