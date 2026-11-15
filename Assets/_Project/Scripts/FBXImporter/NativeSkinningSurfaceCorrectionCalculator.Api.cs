using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 공개 진입 오버로드와 품질 게이트 — 본체에서 이관한 partial.
    /// </summary>
    internal static partial class NativeSkinningSurfaceCorrectionCalculator
    {
        internal static bool TryCalculateFast(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            return TryCalculateTimed(
                baselineVertices,
                baselineVertices,
                contract,
                FastConfiguration,
                false,
                true,
                null,
                out result);
        }

        // 계측 경로 — 리플렉션 호출과 이름 충돌하지 않게 별도 명명한다.
        internal static bool TryCalculateFastTimed(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            NativeSkinningSolverTiming timing,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            return TryCalculateTimed(
                baselineVertices,
                baselineVertices,
                contract,
                FastConfiguration,
                false,
                true,
                timing,
                out result);
        }

        internal static bool TryCalculateFastValidatedFrame(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            return TryCalculateTimed(
                baselineVertices,
                baselineVertices,
                contract,
                FastConfiguration,
                false,
                false,
                null,
                out result);
        }

        internal static bool TryCalculateFastValidatedFrameTimed(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            NativeSkinningSolverTiming timing,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            return TryCalculateTimed(
                baselineVertices,
                baselineVertices,
                contract,
                FastConfiguration,
                false,
                false,
                timing,
                out result);
        }

        internal static bool TryCalculateStrong(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            return TryCalculateTimed(
                baselineVertices,
                baselineVertices,
                contract,
                StrongConfiguration,
                false,
                true,
                null,
                out result);
        }

        internal static bool TryCalculateStrongTimed(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            NativeSkinningSolverTiming timing,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            return TryCalculateTimed(
                baselineVertices,
                baselineVertices,
                contract,
                StrongConfiguration,
                false,
                true,
                timing,
                out result);
        }

        internal static bool TryCalculateStrongValidatedFrame(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            return TryCalculateStrongValidatedFrameTimed(
                baselineVertices, contract, null, out result);
        }

        internal static bool TryCalculateStrongValidatedFrameTimed(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            NativeSkinningSolverTiming timing,
            out NativeSkinningSurfaceCorrectionResult result)
        {
            if (!TryCalculateTimed(
                    baselineVertices,
                    baselineVertices,
                    contract,
                    StrongConfiguration,
                    false,
                    false,
                    timing,
                    out result))
            {
                return false;
            }
            if (IsWithinQuality(result, contract, StrongConfiguration))
            {
                return true;
            }
            NativeSkinningSurfaceCorrectionResult headroom =
                RunStrongHeadroomChain(baselineVertices, contract, timing, ref result);
            RunStrongWideChain(baselineVertices, contract, timing, headroom, ref result);
            return true;
        }

        // 축소 재시도·헤드룸·체인 재투입 단계. headroom은 후속 블렌드/복원 단계가 쓰므로 반환한다.
        // 후보 계산 → 품질 게이트 통과 시 채택의 공통 패턴.
        private static bool TryAdoptCandidate(
            NativeSkinningSolverConfiguration candidateConfig,
            NativeSkinningSolverConfiguration qualityConfig,
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> sourceVertices,
            NativeSkinningSurfaceContract contract,
            NativeSkinningSolverTiming timing,
            ref NativeSkinningSurfaceCorrectionResult result)
        {
            if (TryCalculateTimed(
                    baselineVertices,
                    sourceVertices,
                    contract,
                    candidateConfig,
                    false,
                    false,
                    timing,
                    out NativeSkinningSurfaceCorrectionResult candidate) &&
                IsWithinQuality(candidate, contract, qualityConfig))
            {
                result = candidate;
                return true;
            }
            return false;
        }

        private static NativeSkinningSurfaceCorrectionResult RunStrongHeadroomChain(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            NativeSkinningSolverTiming timing,
            ref NativeSkinningSurfaceCorrectionResult result)
        {
            // 축소 예산 재시도가 더 엄격한 품질 게이트를 통과하면 그 결과를 채택한다.
            TryAdoptCandidate(
                StrongRetryConfiguration, StrongRetryConfiguration,
                baselineVertices, baselineVertices, contract, timing, ref result);
            NativeSkinningSurfaceCorrectionResult headroom = null;
            if (!IsWithinQuality(result, contract, StrongConfiguration) &&
                TryAdoptCandidate(
                    StrongHeadroomConfiguration, StrongConfiguration,
                    baselineVertices, baselineVertices, contract, timing,
                    ref result))
            {
                headroom = result;
            }
            // 예산과 무관하게 국소 최적해에 정체하는 입력이 있어
            // 직전 결과를 초기 추정으로 재투입해 basin 이탈을 시도한다.
            if (!IsWithinQuality(result, contract, StrongConfiguration) &&
                result?.CorrectedVertices != null)
            {
                TryAdoptCandidate(
                    StrongConfiguration, StrongConfiguration,
                    baselineVertices, result.CorrectedVertices, contract, timing,
                    ref result);
            }
            return headroom;
        }

        // 와이드 탐색·블렌드·확장 예산·복원·리시드 단계.
        private static void RunStrongWideChain(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            NativeSkinningSolverTiming timing,
            NativeSkinningSurfaceCorrectionResult headroom,
            ref NativeSkinningSurfaceCorrectionResult result)
        {
            NativeSkinningSurfaceCorrectionResult wide = null;
            if (!IsWithinQuality(result, contract, StrongConfiguration) &&
                TryAdoptCandidate(
                    StrongWideConfiguration, StrongConfiguration,
                    baselineVertices, baselineVertices, contract, timing,
                    ref result))
            {
                wide = result;
            }
            // 접힘이 해소된 후보를 원본 쪽으로 줄여 신장 한도 안의 결과만 재사용함.
            if (!IsWithinQuality(result, contract, StrongConfiguration) &&
                headroom != null && headroom.IsSafe && !headroom.UsedRestShapeRecovery &&
                TryFindQualityPreservingCoupledBlend(
                    baselineVertices,
                    baselineVertices,
                    headroom.CorrectedVertices,
                    contract,
                    StrongConfiguration,
                    BuildFrameEdges(baselineVertices, contract),
                    headroom.InitialSharpFoldCount,
                    Array.Empty<int>(),
                    out NativeSkinningSurfaceCorrectionResult blended))
            {
                result = blended;
            }
            RunExtendedRestoreReseed(
                baselineVertices, contract, timing, headroom, wide, ref result);
        }

        private static void RunExtendedRestoreReseed(
            IReadOnlyList<Vector3> baselineVertices,
            NativeSkinningSurfaceContract contract,
            NativeSkinningSolverTiming timing,
            NativeSkinningSurfaceCorrectionResult headroom,
            NativeSkinningSurfaceCorrectionResult wide,
            ref NativeSkinningSurfaceCorrectionResult result)
        {
            if (!IsWithinQuality(result, contract, StrongConfiguration))
            {
                TryAdoptCandidate(
                    StrongExtendedConfiguration, StrongExtendedConfiguration,
                    baselineVertices, baselineVertices, contract, timing,
                    ref result);
            }
            if (!IsWithinQuality(result, contract, StrongConfiguration) &&
                headroom != null && headroom.IsSafe && !headroom.UsedRestShapeRecovery &&
                TryCalculateStrainRestoredHeadroom(
                    baselineVertices, headroom, contract, timing,
                    out NativeSkinningSurfaceCorrectionResult restored))
            {
                result = restored;
            }
            // 넓은 영역으로 접힘을 줄인 결과를 다시 풀어 같은 국소 해의 반복을 피함.
            if (!IsWithinQuality(result, contract, StrongConfiguration) &&
                wide != null && !wide.UsedRestShapeRecovery &&
                wide.ResidualSharpFoldCount < result.ResidualSharpFoldCount &&
                wide.NewSharpFoldCount == 0 && wide.NewDegenerateFaceCount == 0 &&
                wide.ReversedFaceCount == 0)
            {
                TryAdoptCandidate(
                    StrongWideConfiguration, StrongConfiguration,
                    baselineVertices, wide.CorrectedVertices, contract, timing,
                    ref result);
            }
        }
    }
}
