using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class NativeSkinningSurfaceContract
    {

        internal int[] EvaluatedVertexIndices { get; }

        internal int ConnectedVertexCount { get; }

        internal int LowerArmBoneIndex { get; }

        internal int[][] FacePairIndicesByVertex { get; }

        internal int[][] AffectedFaceIndicesByVertex { get; }

        internal float ArmChainLength { get; }

        internal int AffectedFaceCount => AffectedFaceIndices.Length;

        internal int FacePairCount => FacePairs.Length;

        internal int EvaluatedVertexCount => EvaluatedVertexIndices.Length;

    }
}