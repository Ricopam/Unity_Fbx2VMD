using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
        private readonly struct BendingLinearization
        {
            internal BendingLinearization(
                int index0,
                int index1,
                int index2,
                int index3,
                Vector3 gradient0,
                Vector3 gradient1,
                Vector3 gradient2,
                Vector3 gradient3)
            {
                Index0 = index0;
                Index1 = index1;
                Index2 = index2;
                Index3 = index3;
                Gradient0 = gradient0;
                Gradient1 = gradient1;
                Gradient2 = gradient2;
                Gradient3 = gradient3;
            }

            internal int Index0 { get; }
            internal int Index1 { get; }
            internal int Index2 { get; }
            internal int Index3 { get; }
            internal Vector3 Gradient0 { get; }
            internal Vector3 Gradient1 { get; }
            internal Vector3 Gradient2 { get; }
            internal Vector3 Gradient3 { get; }

            internal int LocalIndexAt(int index)
            {
                switch (index)
                {
                    case 0: return Index0;
                    case 1: return Index1;
                    case 2: return Index2;
                    default: return Index3;
                }
            }

            internal Vector3 GradientAt(int index)
            {
                switch (index)
                {
                    case 0: return Gradient0;
                    case 1: return Gradient1;
                    case 2: return Gradient2;
                    default: return Gradient3;
                }
            }
        }

        private readonly struct EdgeStrainLinearization
        {
            internal EdgeStrainLinearization(
                int firstLocalIndex,
                int secondLocalIndex,
                Vector3 firstGradient,
                Vector3 secondGradient)
            {
                FirstLocalIndex = firstLocalIndex;
                SecondLocalIndex = secondLocalIndex;
                FirstGradient = firstGradient;
                SecondGradient = secondGradient;
            }

            internal int FirstLocalIndex { get; }
            internal int SecondLocalIndex { get; }
            internal Vector3 FirstGradient { get; }
            internal Vector3 SecondGradient { get; }
        }

        // TryCalculate 호출 1회분의 재사용 스크래치 — 워커 스레드 한정(공유 없음)이라
        // 별도 동기화 없이 반복 할당만 제거한다.
    }
}
