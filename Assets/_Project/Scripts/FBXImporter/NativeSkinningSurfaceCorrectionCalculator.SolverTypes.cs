namespace Fbx2Vmd.FBXImporter
{
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
        private readonly struct CorrectionConfiguration
        {
            // 진단 테스트가 이 6인자 시그니처로 인스턴스를 만들므로 유지한다.
            internal CorrectionConfiguration(
                int localGlobalIterations,
                int conjugateGradientIterations,
                int edgeLengthProjectionIterations,
                float maximumEdgeLengthStrain,
                float maximumTotalDisplacementToArmLengthRatio,
                int worksetRingCount = DefaultWorksetRingCount)
                : this(
                    localGlobalIterations,
                    conjugateGradientIterations,
                    edgeLengthProjectionIterations,
                    maximumEdgeLengthStrain,
                    maximumTotalDisplacementToArmLengthRatio,
                    worksetRingCount,
                    false,
                    false,
                    false)
            {
            }

            internal CorrectionConfiguration(
                int localGlobalIterations,
                int conjugateGradientIterations,
                int edgeLengthProjectionIterations,
                float maximumEdgeLengthStrain,
                float maximumTotalDisplacementToArmLengthRatio,
                int worksetRingCount,
                bool allowsEarlyConvergence,
                bool allowsWarmStart,
                bool allowsBurst = false)
            {
                LocalGlobalIterations = localGlobalIterations;
                ConjugateGradientIterations = conjugateGradientIterations;
                EdgeLengthProjectionIterations = edgeLengthProjectionIterations;
                MaximumEdgeLengthStrain = maximumEdgeLengthStrain;
                MaximumTotalDisplacementToArmLengthRatio =
                    maximumTotalDisplacementToArmLengthRatio;
                WorksetRingCount = worksetRingCount;
                AllowsEarlyConvergence = allowsEarlyConvergence;
                AllowsWarmStart = allowsWarmStart;
                AllowsBurst = allowsBurst;
            }

            internal int LocalGlobalIterations { get; }
            internal int ConjugateGradientIterations { get; }
            internal int EdgeLengthProjectionIterations { get; }
            internal float MaximumEdgeLengthStrain { get; }
            internal float MaximumTotalDisplacementToArmLengthRatio { get; }
            internal int WorksetRingCount { get; }
            internal bool AllowsEarlyConvergence { get; }
            internal bool AllowsWarmStart { get; }
            internal bool AllowsBurst { get; }
        }

        private readonly struct FrameEdge
        {
            internal FrameEdge(
                int firstVertex,
                int secondVertex,
                float baselineLength,
                float restLength)
            {
                FirstVertex = firstVertex;
                SecondVertex = secondVertex;
                BaselineLength = baselineLength;
                RestLength = restLength;
            }

            internal int FirstVertex { get; }
            internal int SecondVertex { get; }
            internal float BaselineLength { get; }
            internal float RestLength { get; }
        }

        private readonly struct LocalEdge
        {
            internal LocalEdge(
                int firstVertex,
                int secondVertex,
                int firstLocalIndex,
                int secondLocalIndex,
                float baselineLength,
                float restLength)
            {
                FirstVertex = firstVertex;
                SecondVertex = secondVertex;
                FirstLocalIndex = firstLocalIndex;
                SecondLocalIndex = secondLocalIndex;
                BaselineLength = baselineLength;
                RestLength = restLength;
            }

            internal int FirstVertex { get; }
            internal int SecondVertex { get; }
            internal int FirstLocalIndex { get; }
            internal int SecondLocalIndex { get; }
            internal float BaselineLength { get; }
            internal float RestLength { get; }
        }
    }
}
