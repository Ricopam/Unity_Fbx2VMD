using System;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal readonly struct NativeSkinningEdgeKey : IEquatable<NativeSkinningEdgeKey>
    {
        internal NativeSkinningEdgeKey(int first, int second)
        {
            First = Mathf.Min(first, second);
            Second = Mathf.Max(first, second);
        }

        internal int First { get; }

        internal int Second { get; }

        public bool Equals(NativeSkinningEdgeKey other)
        {
            return First == other.First && Second == other.Second;
        }

        public override bool Equals(object value)
        {
            return value is NativeSkinningEdgeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (First * 397) ^ Second;
            }
        }
    }

    internal readonly struct NativeSkinningFacePair
    {
        internal NativeSkinningFacePair(
            NativeSkinningEdgeKey edge,
            int firstFace,
            int secondFace,
            int firstOpposite,
            int secondOpposite,
            int firstEdge,
            int secondEdge)
        {
            Edge = edge;
            FirstFace = firstFace;
            SecondFace = secondFace;
            FirstOpposite = firstOpposite;
            SecondOpposite = secondOpposite;
            FirstEdge = firstEdge;
            SecondEdge = secondEdge;
        }

        internal NativeSkinningEdgeKey Edge { get; }

        internal int FirstFace { get; }

        internal int SecondFace { get; }

        internal int FirstOpposite { get; }

        internal int SecondOpposite { get; }

        internal int FirstEdge { get; }

        internal int SecondEdge { get; }
    }

    internal readonly struct NativeSkinningFacePairRestLengths
    {
        internal NativeSkinningFacePairRestLengths(
            float sharedEdge,
            float firstOppositeToFirstEdge,
            float firstOppositeToSecondEdge,
            float secondOppositeToFirstEdge,
            float secondOppositeToSecondEdge)
        {
            SharedEdge = sharedEdge;
            FirstOppositeToFirstEdge = firstOppositeToFirstEdge;
            FirstOppositeToSecondEdge = firstOppositeToSecondEdge;
            SecondOppositeToFirstEdge = secondOppositeToFirstEdge;
            SecondOppositeToSecondEdge = secondOppositeToSecondEdge;
        }

        internal float SharedEdge { get; }

        internal float FirstOppositeToFirstEdge { get; }

        internal float FirstOppositeToSecondEdge { get; }

        internal float SecondOppositeToFirstEdge { get; }

        internal float SecondOppositeToSecondEdge { get; }
    }
}
