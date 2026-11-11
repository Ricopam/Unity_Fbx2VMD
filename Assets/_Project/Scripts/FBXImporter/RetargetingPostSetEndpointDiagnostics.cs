using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // PostSetHumanPose 우측 엔드포인트 위치 진단 스냅샷
    internal sealed class RetargetingPostSetEndpointDiagnostics
    {
        internal Vector3 DesiredFootWorldPosition = BuildNaNVector3();
        internal Vector3 DesiredToesWorldPosition = BuildNaNVector3();
        internal Vector3 CurrentFootWorldPosition = BuildNaNVector3();
        internal Vector3 CurrentToesWorldPosition = BuildNaNVector3();
        internal Vector3 DeltaBeforeClamp = BuildNaNVector3();
        internal Vector3 DeltaAfterClamp = BuildNaNVector3();
        internal Vector3 DeltaAfterPositiveZScale = BuildNaNVector3();
        internal Vector3 Correction = BuildNaNVector3();
        internal Vector3 NextFootWorldPosition = BuildNaNVector3();
        internal float MaxYawAngle = float.NaN;
        internal float YawCorrectionAngle = float.NaN;
        internal float UpperLegRotationDeltaAngle = float.NaN;
        internal float Applied = float.NaN;
        internal float EvaluatorXzReferenceEnabled = float.NaN;
        internal Vector3 EvaluatorXzFirstOffset = BuildNaNVector3();
        internal Vector3 EvaluatorXzNormalizedDelta = BuildNaNVector3();
        internal Vector3 EvaluatorXzDesiredNormalizedDelta = BuildNaNVector3();
        internal float EvaluatorXzTargetMagnitude = float.NaN;

        internal void Reset()
        {
            DesiredFootWorldPosition = BuildNaNVector3();
            DesiredToesWorldPosition = BuildNaNVector3();
            CurrentFootWorldPosition = BuildNaNVector3();
            CurrentToesWorldPosition = BuildNaNVector3();
            DeltaBeforeClamp = BuildNaNVector3();
            DeltaAfterClamp = BuildNaNVector3();
            DeltaAfterPositiveZScale = BuildNaNVector3();
            Correction = BuildNaNVector3();
            NextFootWorldPosition = BuildNaNVector3();
            MaxYawAngle = float.NaN;
            YawCorrectionAngle = float.NaN;
            UpperLegRotationDeltaAngle = float.NaN;
            Applied = float.NaN;
            EvaluatorXzReferenceEnabled = float.NaN;
            EvaluatorXzFirstOffset = BuildNaNVector3();
            EvaluatorXzNormalizedDelta = BuildNaNVector3();
            EvaluatorXzDesiredNormalizedDelta = BuildNaNVector3();
            EvaluatorXzTargetMagnitude = float.NaN;
        }

        internal void Record(
            RetargetingEndpointDiagnosticSnapshot diagnostics,
            float maxYawAngle,
            float yawCorrectionAngle,
            float upperLegRotationDeltaAngle,
            float applied)
        {
            DesiredFootWorldPosition = diagnostics.DesiredFootPosition;
            DesiredToesWorldPosition = diagnostics.DesiredToesPosition;
            CurrentFootWorldPosition = diagnostics.CurrentFootPosition;
            CurrentToesWorldPosition = diagnostics.CurrentToesPosition;
            DeltaBeforeClamp = diagnostics.EndpointDeltaBeforeClamp;
            DeltaAfterClamp = diagnostics.EndpointDeltaAfterClamp;
            DeltaAfterPositiveZScale = diagnostics.EndpointDeltaAfterPositiveZScale;
            Correction = diagnostics.Correction;
            NextFootWorldPosition = diagnostics.NextFootPosition;
            MaxYawAngle = maxYawAngle;
            YawCorrectionAngle = yawCorrectionAngle;
            UpperLegRotationDeltaAngle = upperLegRotationDeltaAngle;
            Applied = applied;
            EvaluatorXzReferenceEnabled = diagnostics.EvaluatorXzReferenceEnabled;
            EvaluatorXzFirstOffset = diagnostics.EvaluatorXzFirstOffset;
            EvaluatorXzNormalizedDelta = diagnostics.EvaluatorXzNormalizedDelta;
            EvaluatorXzDesiredNormalizedDelta = diagnostics.EvaluatorXzDesiredNormalizedDelta;
            EvaluatorXzTargetMagnitude = diagnostics.EvaluatorXzTargetMagnitude;
        }

        private static Vector3 BuildNaNVector3()
        {
            return new Vector3(float.NaN, float.NaN, float.NaN);
        }
    }
}
