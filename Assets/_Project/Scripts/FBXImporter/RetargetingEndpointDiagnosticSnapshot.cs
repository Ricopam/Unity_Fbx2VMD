using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{

    internal struct RetargetingEndpointDiagnosticSnapshot
    {
        internal Vector3 DesiredFootPosition;
        internal Vector3 DesiredToesPosition;
        internal Vector3 CurrentFootPosition;
        internal Vector3 CurrentToesPosition;
        internal Vector3 EndpointDeltaBeforeClamp;
        internal Vector3 EndpointDeltaAfterClamp;
        internal Vector3 EndpointDeltaAfterPositiveZScale;
        internal Vector3 Correction;
        internal Vector3 NextFootPosition;
        internal float EvaluatorXzReferenceEnabled;
        internal Vector3 EvaluatorXzFirstOffset;
        internal Vector3 EvaluatorXzNormalizedDelta;
        internal Vector3 EvaluatorXzDesiredNormalizedDelta;
        internal float EvaluatorXzTargetMagnitude;

        internal static RetargetingEndpointDiagnosticSnapshot Empty =>
            new RetargetingEndpointDiagnosticSnapshot
            {
                DesiredFootPosition = NaNVector3,
                DesiredToesPosition = NaNVector3,
                CurrentFootPosition = NaNVector3,
                CurrentToesPosition = NaNVector3,
                EndpointDeltaBeforeClamp = NaNVector3,
                EndpointDeltaAfterClamp = NaNVector3,
                EndpointDeltaAfterPositiveZScale = NaNVector3,
                Correction = NaNVector3,
                NextFootPosition = NaNVector3,
                EvaluatorXzReferenceEnabled = float.NaN,
                EvaluatorXzFirstOffset = NaNVector3,
                EvaluatorXzNormalizedDelta = NaNVector3,
                EvaluatorXzDesiredNormalizedDelta = NaNVector3,
                EvaluatorXzTargetMagnitude = float.NaN
            };

        private static Vector3 NaNVector3 => new Vector3(float.NaN, float.NaN, float.NaN);

    }

}