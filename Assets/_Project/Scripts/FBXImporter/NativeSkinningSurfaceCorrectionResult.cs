using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed class NativeSkinningSurfaceCorrectionResult
    {
        internal NativeSkinningSurfaceCorrectionResult(
            IReadOnlyList<Vector3> correctedVertices,
            int[] correctedVertexIndices,
            int initialSharpFoldCount,
            int residualSharpFoldCount,
            int newSharpFoldCount,
            int newDegenerateFaceCount,
            int reversedFaceCount,
            float maximumVertexDisplacement,
            float maximumEdgeLengthStrain,
            bool usedRestShapeRecovery,
            float maximumRestEdgeStrainIncrease,
            float minimumRestShapeAngleImprovementDegrees,
            float minimumRestEdgeLengthRatioBeforeRecovery,
            float minimumRestEdgeLengthRatioAfterRecovery,
            int unresolvedRestShapeRecoveryCount)
        {
            CorrectedVertices = correctedVertices;
            CorrectedVertexIndices = correctedVertexIndices;
            InitialSharpFoldCount = initialSharpFoldCount;
            ResidualSharpFoldCount = residualSharpFoldCount;
            NewSharpFoldCount = newSharpFoldCount;
            NewDegenerateFaceCount = newDegenerateFaceCount;
            ReversedFaceCount = reversedFaceCount;
            MaximumVertexDisplacement = maximumVertexDisplacement;
            MaximumEdgeLengthStrain = maximumEdgeLengthStrain;
            UsedRestShapeRecovery = usedRestShapeRecovery;
            MaximumRestEdgeStrainIncrease = maximumRestEdgeStrainIncrease;
            MinimumRestShapeAngleImprovementDegrees =
                minimumRestShapeAngleImprovementDegrees;
            MinimumRestEdgeLengthRatioBeforeRecovery =
                minimumRestEdgeLengthRatioBeforeRecovery;
            MinimumRestEdgeLengthRatioAfterRecovery =
                minimumRestEdgeLengthRatioAfterRecovery;
            UnresolvedRestShapeRecoveryCount = unresolvedRestShapeRecoveryCount;
        }

        internal IReadOnlyList<Vector3> CorrectedVertices { get; }

        internal int[] CorrectedVertexIndices { get; }

        internal int InitialSharpFoldCount { get; }

        internal int ResidualSharpFoldCount { get; }

        internal int NewSharpFoldCount { get; }

        internal int NewDegenerateFaceCount { get; }

        internal int ReversedFaceCount { get; }

        internal float MaximumVertexDisplacement { get; }

        internal float MaximumEdgeLengthStrain { get; }

        internal bool UsedRestShapeRecovery { get; }

        internal float MaximumRestEdgeStrainIncrease { get; }

        internal float MinimumRestShapeAngleImprovementDegrees { get; }

        internal float MinimumRestEdgeLengthRatioBeforeRecovery { get; }

        internal float MinimumRestEdgeLengthRatioAfterRecovery { get; }

        internal int UnresolvedRestShapeRecoveryCount { get; }

        internal bool IsSafe =>
            ResidualSharpFoldCount == 0 &&
            NewSharpFoldCount == 0 &&
            NewDegenerateFaceCount == 0 &&
            ReversedFaceCount == 0;
    }
}
