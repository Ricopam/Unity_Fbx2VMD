using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal readonly struct NativeSkinningMaterialStrainConstraint
    {
        internal NativeSkinningMaterialStrainConstraint(
            int[] localIndices,
            float[] coefficients,
            Vector3 projectedDelta,
            float weight)
        {
            LocalIndices = localIndices;
            Coefficients = coefficients;
            ProjectedDelta = projectedDelta;
            Weight = weight;
        }

        internal int[] LocalIndices { get; }

        internal float[] Coefficients { get; }

        internal Vector3 ProjectedDelta { get; }

        internal float Weight { get; }
    }
}
