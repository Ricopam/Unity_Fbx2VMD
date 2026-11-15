using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 접촉 의도 추정기의 임계값·측정값·구간 타입과 수치 유틸 partial.
    internal static partial class HumanoidFootContactIntentEstimator
    {
        // 지지 판정에 공통으로 쓰이는 스케일 정규화 임계값 묶음.
        struct SupportThresholds
        {
            public float FloorFoot;
            public float FloorToes;
            public float SupportHeight;
            public float SupportSpeed;
            public float AirborneHeight;
            public float AirborneSpeed;
            public int MinimumFrames;
        }

        // 프레임의 높이·속도·지지 증거 측정값 묶음.
        struct ContactEvidence
        {
            public float FootHeight;
            public float ToesHeight;
            public float FootSpeed;
            public float ToesSpeed;
            public bool FootPlanted;
            public bool RollEvidence;
            public bool ToeSliding;
        }

        private readonly struct IntRange
        {
            internal IntRange(int start, int end, FrameClass classification)
            {
                Start = start;
                End = end;
                Classification = classification;
            }

            internal int Start { get; }
            internal int End { get; }
            internal int Length => End - Start + 1;
            internal FrameClass Classification { get; }
        }

    }
}


