using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{

    internal sealed class NativeSkinningRendererCorrection
    {
        internal NativeSkinningRendererCorrection(
            SkinnedMeshRenderer renderer,
            NativeSkinningCorrectionCache cache)
        {
            Renderer = renderer != null
                ? renderer
                : throw new ArgumentNullException(nameof(renderer));
            Cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        internal SkinnedMeshRenderer Renderer { get; }

        internal NativeSkinningCorrectionCache Cache { get; }
    }

    internal sealed class NativeSkinningCorrectionPreprocessResult
    {
        internal NativeSkinningCorrectionPreprocessResult(
            int frameCount,
            int correctedFrameCount,
            int fallbackFrameCount,
            int correctionEntryCount,
            NativeSkinningRendererCorrection[] rendererCorrections,
            int collectedFailureCount = 0)
        {
            FrameCount = frameCount;
            CorrectedFrameCount = correctedFrameCount;
            FallbackFrameCount = fallbackFrameCount;
            CorrectionEntryCount = correctionEntryCount;
            RendererCorrections = rendererCorrections;
            CollectedFailureCount = collectedFailureCount;
        }

        internal int FrameCount { get; }

        internal int CorrectedFrameCount { get; }

        internal int FallbackFrameCount { get; }

        internal int CorrectionEntryCount { get; }

        internal NativeSkinningRendererCorrection[] RendererCorrections { get; }

        /// <summary>
        /// 수집 모드에서 보정 없이 건너뛴 실패 수입니다. 0보다 크면 결과는 부분 보정입니다.
        /// </summary>
        internal int CollectedFailureCount { get; }
    }

    /// <summary>
    /// 병렬 사전 계산 입력. 메인스레드에서 미리 캡처한 정점 스냅샷과 스키닝 행렬을 담고,
    /// 솔버가 워커 스레드에서 Unity API 없이 처리할 수 있게 한다.
    /// 스키닝 행렬이 있으면 워커가 LBS로 베이스라인과 본 변환을 직접 계산하므로
    /// 베이스라인 맵은 검증된 렌더러에서 생략할 수 있다.
    /// </summary>
    internal sealed class NativeSkinningPreparedFrame
    {
        internal NativeSkinningPreparedFrame(
            int frameIndex,
            IReadOnlyDictionary<SkinnedMeshRenderer, Vector3[]> baselineVertices,
            IReadOnlyDictionary<
                SkinnedMeshRenderer,
                Matrix4x4[]> skinningMatrices)
        {
            FrameIndex = frameIndex;
            BaselineVertices = baselineVertices;
            SkinningMatrices = skinningMatrices;
        }

        internal int FrameIndex { get; }

        internal IReadOnlyDictionary<SkinnedMeshRenderer, Vector3[]>
            BaselineVertices { get; }

        internal IReadOnlyDictionary<
            SkinnedMeshRenderer,
            Matrix4x4[]> SkinningMatrices { get; }
    }

}
