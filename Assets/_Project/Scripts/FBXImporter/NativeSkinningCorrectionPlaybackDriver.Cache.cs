using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class NativeSkinningCorrectionPlaybackDriver
    {
        // 캐시 복원 시도 — 실패 사유는 전부 미스로 처리해 기존 계산 경로로 넘긴다.
        private bool TryRestoreResultFromCache(
            out NativeSkinningCorrectionPreprocessResult result)
        {
            result = null;
            _resultFromCache = false;
            _cacheKeyHash = null;
            Debug.Log("[NativePrepCache] 캐시 조회를 시작합니다.", this);
            if (!TryLoadCacheDocument(
                    out NativeSkinningCorrectionCacheDocument document))
            {
                return false;
            }
            // 복원 경로의 어떤 예외도 미스로 내려 기존 계산 경로를 보존한다.
            if (!TryBindAndBuildResult(document,
                    out NativeSkinningRendererCorrection[] corrections))
            {
                return false;
            }
            result = new NativeSkinningCorrectionPreprocessResult(
                document.FrameCount,
                document.CorrectedFrameCount,
                document.FallbackFrameCount,
                document.CorrectionEntryCount,
                corrections);
            _resultFromCache = true;
            Debug.Log(
                $"[NativePrepCache] 캐시 히트 — 렌더러 {corrections.Length}개, " + $"보정 프레임 {document.CorrectedFrameCount}개를 복원했습니다.", this);
            return true; }

        bool TryLoadCacheDocument(
            out NativeSkinningCorrectionCacheDocument document)
        {
            document = null;
            // 수집 모드 실행이 수동 재계산 플래그를 소비하지 않도록 먼저 확인한다.
            if (NativeSkinningCorrectionPreprocessor.IsFailureCollectionRequested())
            {
                return false;
            }
#if UNITY_EDITOR
            if (ForceCacheRecalculate)
            {
                ForceCacheRecalculate = false;
                Debug.Log( "[NativePrepCache] 수동 재계산 요청 — 캐시 로드를 건너뜁니다.", this);
                // 강제 재계산이어도 결과는 다시 저장해야 하므로 키는 계산한다.
                ComputeCacheKey();
                return false;
            }
#endif
            if (!ComputeCacheKey())
            {
                return false; }
            string path = NativeSkinningCorrectionCacheFileStore.GetCachePath( _cacheKeyHash);
            if (!NativeSkinningCorrectionCacheFileStore.TryLoad( path, _cacheKeyHash, out document, out string loadError))
            {
                Debug.Log( $"[NativePrepCache] 캐시 미스 — {loadError}", this);
                return false; }
            return true; }

        bool TryBindAndBuildResult(
            NativeSkinningCorrectionCacheDocument document,
            out NativeSkinningRendererCorrection[] corrections)
        {
            try
            {
                if (!TryBindDocument(document, out corrections,
                        out string bindError))
                {
                    Debug.Log(
                        "[NativePrepCache] 캐시 렌더러 재바인딩 실패 — " +
                        $"재계산합니다: {bindError}",
                        this);
                    return false;
                }
                return true;
            }
            catch (Exception exception)
            {
                Debug.Log(
                    $"[NativePrepCache] 캐시 복원 중 예외 — 재계산합니다: " +
                    $"{exception.Message}",
                    this);
                corrections = null;
                return false;
            }
        }

        private bool ComputeCacheKey()
        {
            if (_cacheKeyHash != null)
            {
                return true;
            }
            if (_animator == null || _playbackController == null || _contracts.Length == 0)
            {
                Debug.Log( "[NativePrepCache] 캐시 식별자가 주입되지 않아 " + "캐시를 사용하지 않습니다.", this);
                return false;
            }
            if (!TryComputeKeyHash(out string keyError))
            {
                if (string.IsNullOrEmpty(keyError))
                {
                    Debug.Log( "[NativePrepCache] 캐시 식별자가 주입되지 않아 " + "캐시를 사용하지 않습니다.", this);
                }
                else
                {
                    Debug.LogWarning( $"[NativePrepCache] 캐시 키 계산 실패 — {keyError}", this);
                }
                return false;
            }
            return true;
        }

        bool TryComputeKeyHash(out string keyError)
        {
            keyError = null;
#if UNITY_EDITOR
            if (_cacheMotionClip == null)
            {
                return false;
            }
            return NativeSkinningCorrectionCacheKey.TryCompute( _cacheMotionClip, _cacheMotionName, TotalFrameCount, _playbackController.ClipFrameRate, _cacheSourceModelAsset, _animator, _contracts,
                _playbackController.GetNativePoseCacheIdentity(), out _cacheKeyHash, out keyError);
#else
            if (string.IsNullOrEmpty(_runtimeCacheSourcePath))
            {
                return false;
            }
            return NativeSkinningCorrectionCacheKey.TryComputeRuntime( _runtimeCacheSourcePath, _cacheMotionName, TotalFrameCount, _playbackController.ClipFrameRate, _animator, _contracts,
                _playbackController.GetNativePoseCacheIdentity(), out _cacheKeyHash, out keyError);
#endif
        }

        // 캐시 문서·바인딩이 쓰는 메시 식별자 — 에디터는 에셋 id,
        // 빌드는 정점 내용 해시로 같은 메시를 식별한다.
        private static string GetMeshCacheIdentity(Mesh mesh)
        {
#if UNITY_EDITOR
            return NativeSkinningCorrectionCacheKey.GetAssetIdentity(mesh);
#else
            return NativeSkinningCorrectionCacheKey.GetRuntimeMeshIdentity(mesh);
#endif
        }

        // 캐시 문서의 렌더러 항목을 현재 계약의 Renderer에 재바인딩한다.
        private bool TryBindDocument(
            NativeSkinningCorrectionCacheDocument document,
            out NativeSkinningRendererCorrection[] corrections, out string failReason)
        {
            corrections = null;
            failReason = null;
            if (document.FrameCount != TotalFrameCount)
            {
                failReason = $"프레임 수 불일치 문서={document.FrameCount} 현재={TotalFrameCount}";
                return false; }
            // 경로→렌더러 1:1 맵 — 씬 쪽 경로 충돌(모호)은 미스로 처리한다.
            // 문서는 보정 프레임이 생긴 렌더러만 담으므로 부분 집합이 정상이다 —
            // 계약 렌더러 집합 자체는 캐시 키에 이미 포함돼 다른 조합은 파일이
            // 열리지 않는다.
            var byPath = new Dictionary<string, SkinnedMeshRenderer>( StringComparer.Ordinal);
            foreach (SkinnedMeshRenderer candidate in _contracts .Select(contract => contract.Renderer) .Where(renderer => renderer != null) .Distinct()) {
                string path = NativeSkinningCorrectionCacheKey .GetRendererPath(_animator, candidate);
                if (candidate.sharedMesh == null || string.IsNullOrEmpty(path) || byPath.ContainsKey(path))
                {
                    failReason = $"씬 렌더러 경로 구성 실패 path='{path}' mesh={(candidate.sharedMesh == null ? "null" : "ok")} 중복={byPath.ContainsKey(path)}";
                    return false; }
                byPath.Add(path, candidate); }
            if (!TryBindEntries(document, byPath, out List<NativeSkinningRendererCorrection> bound, out failReason))
            {
                return false; }
            corrections = bound.ToArray();
            return true; }

        static bool TryBindEntries(
            NativeSkinningCorrectionCacheDocument document,
            Dictionary<string, SkinnedMeshRenderer> byPath,
            out List<NativeSkinningRendererCorrection> bound,
            out string failReason)
        {
            bound = new List<NativeSkinningRendererCorrection>( document.Renderers.Length);
            failReason = null;
            foreach (NativeSkinningCorrectionCacheRendererEntry entry in document.Renderers) {
                if (!byPath.TryGetValue(entry.RendererPath, out SkinnedMeshRenderer renderer))
                {
                    failReason = $"문서 경로 '{entry.RendererPath}'에 대응하는 씬 렌더러 없음";
                    return false; }
                if (renderer.sharedMesh.vertexCount != entry.VertexCount || GetMeshCacheIdentity(renderer.sharedMesh) != entry.MeshAssetId)
                {
                    failReason = $"메시 식별 불일치 path='{entry.RendererPath}' 정점={renderer.sharedMesh.vertexCount}/{entry.VertexCount}";
                    return false; }
                var frames = new NativeSkinningCorrectionFrame[ entry.Frames.Length];
                for (int index = 0; index < entry.Frames.Length; index++)
                {
                    NativeSkinningCorrectionCacheFrameData frame = entry.Frames[index];
                    var items = new NativeSkinningVertexCorrection[ frame.VertexIndices.Length];
                    for (int item = 0; item < items.Length; item++)
                    {
                        items[item] = new NativeSkinningVertexCorrection( frame.VertexIndices[item], frame.Deltas[item]); }
                    frames[index] = new NativeSkinningCorrectionFrame( frame.FrameIndex, items); }
                bound.Add(new NativeSkinningRendererCorrection( renderer, new NativeSkinningCorrectionCache( entry.VertexCount, frames))); }
            return true; }

        // 계산 결과를 캐시 파일로 기록 — 부분 결과·수집 모드·캐시 재사용분은 제외.
        private void TrySaveResultToCache(
            NativeSkinningCorrectionPreprocessResult result)
        {
            try
            {
                if (_resultFromCache || result == null || result.CollectedFailureCount > 0 || NativeSkinningCorrectionPreprocessor .IsFailureCollectionRequested() || !ComputeCacheKey()) {
                    return;
                }
                var document = new NativeSkinningCorrectionCacheDocument
                {
                    FrameCount = result.FrameCount, CorrectedFrameCount = result.CorrectedFrameCount, FallbackFrameCount = result.FallbackFrameCount,
                    CorrectionEntryCount = result.CorrectionEntryCount, Renderers = result.RendererCorrections .Select(BuildRendererEntry) .ToArray()
                };
                string path = NativeSkinningCorrectionCacheFileStore .GetCachePath(_cacheKeyHash);
                if (NativeSkinningCorrectionCacheFileStore.TrySave( path, _cacheKeyHash, document, out string errorMessage))
                {
                    Debug.Log( $"[NativePrepCache] 보정 캐시 저장: {path}", this);
                }
                else
                {
                    Debug.LogWarning( $"[NativePrepCache] 보정 캐시 저장 실패: {errorMessage}", this);
                }
            }
            catch (Exception exception)
            {
                // 캐시 기록 실패는 준비 결과를 실패로 뒤집지 않는다.
                Debug.LogWarning( $"[NativePrepCache] 보정 캐시 저장 중 예외: {exception.Message}", this); } }

        private NativeSkinningCorrectionCacheRendererEntry BuildRendererEntry(
            NativeSkinningRendererCorrection correction)
        {
            return new NativeSkinningCorrectionCacheRendererEntry
            {
                RendererPath =
                    NativeSkinningCorrectionCacheKey.GetRendererPath(
                        _animator, correction.Renderer),
                MeshAssetId =
                    GetMeshCacheIdentity(correction.Renderer.sharedMesh),
                VertexCount = correction.Cache.VertexCount,
                Frames = correction.Cache.ExportFrames()
                    .Select(frame =>
                    {
                        NativeSkinningVertexCorrection[] items =
                            frame.ExportCorrections();
                        return new NativeSkinningCorrectionCacheFrameData
                        {
                            FrameIndex = frame.FrameIndex,
                            VertexIndices = items
                                .Select(item => item.VertexIndex)
                                .ToArray(),
                            Deltas = items
                                .Select(item => item.Delta)
                                .ToArray()
                        };
                    })
                    .ToArray() }; }
        bool PrepareBakedMeshes()
        {
            foreach (SkinnedMeshRenderer renderer in _contracts
                         .Select(contract => contract.Renderer)
                         .Distinct())
            {
                if (renderer == null || renderer.sharedMesh == null)
                {
                    return false;
                }
                var bakedMesh = new Mesh
                {
                    name = $"{renderer.sharedMesh.name} Native Skinning Preprocess",
                    hideFlags = HideFlags.HideAndDontSave
                };
                _bakedMeshes.Add(renderer, bakedMesh);
                _sessionMeshes[renderer] = renderer.sharedMesh;
            }
            return true;
        }

        bool CreatePreprocessSession()
        {
            return NativeSkinningCorrectionPreprocessor
                .IsFailureCollectionRequested()
                ? NativeSkinningCorrectionPreprocessor
                    .TryCreateFailureCollectingSession(
                        TotalFrameCount,
                        _contracts,
                        ReadFrameVertices,
                        null,
                        out _preprocessSession)
                : NativeSkinningCorrectionPreprocessor.TryCreateSession(
                        TotalFrameCount,
                        _contracts,
                        ReadFrameVertices,
                        null,
                        out _preprocessSession);
        }

        bool CapturePendingFrames()
        {
            int capturedCount = 0;
            while (_nextCaptureFrame < TotalFrameCount &&
                   _pendingPreparedFrames.Count < MaxPendingPreparedFrames &&
                   capturedCount < MaxCapturesPerTick)
            {
                if (!CapturePreparedFrame(_nextCaptureFrame))
                {
                    return false;
                }
                _nextCaptureFrame++;
                capturedCount++;
            }
            return true;
        }

        void DispatchSolveBatch()
        {
            if (_solveTask != null || _pendingPreparedFrames.Count == 0)
            {
                return;
            }
            int batchCount = Math.Min( SolveBatchFrameCount, _pendingPreparedFrames.Count);
            var batch = new List<NativeSkinningPreparedFrame>(batchCount);
            for (int index = 0; index < batchCount; index++)
            {
                batch.Add(_pendingPreparedFrames.Dequeue());
            }
            NativeSkinningCorrectionPreprocessSession session = _preprocessSession;
            int generation = System.Threading.Volatile.Read( ref _preparationGeneration);
            _solveTask = Task.Run(() => {
                var solveWatch = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    return session.TryProcessPreparedFrames(batch);
                }
                finally
                {
                    // 파이프라인이 리셋된 뒤 끝난 버려진 작업은
                    // 다음 세션의 계측에 합산하지 않는다.
                    if (System.Threading.Volatile.Read( ref _preparationGeneration) == generation)
                    {
                        System.Threading.Interlocked.Add( ref _solveTicks, solveWatch.ElapsedTicks);
                    } } }); }

        bool CaptureRendererInputs( int frameIndex, System.Diagnostics.Stopwatch stageWatch, bool revalidateLbs, Dictionary<SkinnedMeshRenderer, Vector3[]> baselineVertices, Dictionary<SkinnedMeshRenderer, Matrix4x4[]> skinningMatricesByRenderer)
        {
            foreach (SkinnedMeshRenderer renderer in _contracts .Select(contract => contract.Renderer) .Distinct()) {
                // 세션 시작 때 쥔 메시와 달라지면 스냅샷과 행렬이 섞여
                // 조용히 잘못된 베이스라인이 만들어진다 — 캡처에서 차단한다.
                if (renderer == null || renderer.sharedMesh == null || !_sessionMeshes.TryGetValue(renderer, out Mesh sessionMesh) || renderer.sharedMesh != sessionMesh)
                {
                    Fail("사전 계산 중 Renderer 토폴로지가 변경됐습니다.");
                    return false; }
                stageWatch.Restart();
                if (revalidateLbs || _preprocessSession.RequiresBakedBaseline(renderer))
                {
                    if (!_bakedMeshes.TryGetValue(renderer, out Mesh bakedMesh))
                    {
                        Fail("Native 보정 대상 Renderer가 사라졌습니다.");
                        return false; }
                    renderer.BakeMesh(bakedMesh, false);
                    baselineVertices[renderer] = bakedMesh.vertices; }
                _bakeTicks += stageWatch.ElapsedTicks;
                // LBS 베이스라인과 지연 본 변환의 공통 입력 — 매 프레임 표본화한다.
                stageWatch.Restart();
                if (!NativeSkinningBoneTransformSampler.TrySample( renderer, out Matrix4x4[] skinningMatrices))
                {
                    Fail( $"Native 보정 준비 중 frame {frameIndex}의 " + "스키닝 본 자세를 표본화하지 못했습니다.");
                    return false; }
                _boneSampleTicks += stageWatch.ElapsedTicks;
                skinningMatricesByRenderer[renderer] = skinningMatrices; }
            return true; }

    }
}
