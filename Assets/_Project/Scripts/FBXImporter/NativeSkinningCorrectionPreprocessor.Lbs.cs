using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class NativeSkinningCorrectionPreprocessSession
    {
        internal sealed partial class RendererWork
        {


            // LBS 베이스라인 자가검증 상태 — 워커 표결과 드라이버 조회가 겹치므로
            // verdict는 volatile 쓰기, 비교 횟수는 Interlocked로만 갱신한다.
            internal const int LbsUndecided = 0;
            internal const int LbsUsable = 1;
            internal const int LbsRejected = 2;
            internal volatile int LbsVerdict;

        }
    }
}