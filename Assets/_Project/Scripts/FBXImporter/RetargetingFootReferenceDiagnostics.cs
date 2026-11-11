using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 에디터 발 참조(hips local·손/발 로컬회전·FootHips yaw·PreSet 엔드포인트) 경로가 기록하는
    /// 최근 진단 값의 런타임 소유 홀더. 공개 프로퍼티와 녹화 기준선이 읽으므로 PSR에 상주한다.
    /// </summary>
    internal sealed class RetargetingFootReferenceDiagnostics
    {
        internal float FootLocalRotationLeftFootXzDelta = float.NaN;
        internal float FootLocalRotationRightFootXzDelta = float.NaN;
        internal float FootHipsAlignedResidualYawLeftFootXzDelta = float.NaN;
        internal float FootHipsAlignedResidualYawRightFootXzDelta = float.NaN;
        internal float HipsLocalReferenceBeforeLocalY = float.NaN;
        internal float HipsLocalReferenceAfterLocalY = float.NaN;
        internal float HipsLocalReferenceDeltaY = float.NaN;
        internal Vector3 PreSetEndpointBodyPositionBefore = BuildNaNVector3();
        internal Vector3 PreSetEndpointBodyPositionAfter = BuildNaNVector3();
        internal Vector3 PreSetEndpointBodyPositionDelta = BuildNaNVector3();

        internal void ResetHipsLocal()
        {
            HipsLocalReferenceBeforeLocalY = float.NaN;
            HipsLocalReferenceAfterLocalY = float.NaN;
            HipsLocalReferenceDeltaY = float.NaN;
        }

        internal void ResetXzDeltas()
        {
            (FootLocalRotationLeftFootXzDelta, FootLocalRotationRightFootXzDelta,
                FootHipsAlignedResidualYawLeftFootXzDelta, FootHipsAlignedResidualYawRightFootXzDelta) =
                (float.NaN, float.NaN, float.NaN, float.NaN);
        }

        internal void ResetPreSetEndpoint()
        {
            (PreSetEndpointBodyPositionBefore, PreSetEndpointBodyPositionAfter, PreSetEndpointBodyPositionDelta) =
                (BuildNaNVector3(), BuildNaNVector3(), BuildNaNVector3());
        }

        private static Vector3 BuildNaNVector3()
        {
            return new Vector3(float.NaN, float.NaN, float.NaN);
        }
    }
}
