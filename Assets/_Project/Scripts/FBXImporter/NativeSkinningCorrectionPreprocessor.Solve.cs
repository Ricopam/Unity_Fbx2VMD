using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class NativeSkinningCorrectionPreprocessSession
    {
        // 솔버 경로 계측 — 빠른 보정과 강한 fallback의 누적 시간을 분리한다.
        private long _fastPathTicks;

        private long _strongPathTicks;

        private long _lbsTicks;

        private long _convertTicks;

        // 솔버 커널 구간별 누적 — 워커 병렬 호출에서 Interlocked로만 쓴다.
        private readonly NativeSkinningSolverTiming _solverTiming =
            new NativeSkinningSolverTiming();

        // 워커 병렬 누적이라 경로별 합계는 근사치로 읽는다 (표시 전용).
        internal string DescribeSolveTiming()
        {
            double fast = _fastPathTicks * 1000.0 /
                System.Diagnostics.Stopwatch.Frequency;
            double strong = _strongPathTicks * 1000.0 /
                System.Diagnostics.Stopwatch.Frequency;
            int usable = 0;
            int rejected = 0;
            foreach (RendererWork work in _rendererWorks)
            {
                if (work.LbsVerdict == RendererWork.LbsUsable)
                {
                    usable++;
                }
                else if (work.LbsVerdict == RendererWork.LbsRejected)
                {
                    rejected++;
                }
            }
            double lbs = _lbsTicks * 1000.0 /
                System.Diagnostics.Stopwatch.Frequency;
            double convert = _convertTicks * 1000.0 /
                System.Diagnostics.Stopwatch.Frequency;
            return $"fast={fast:F0}ms strong={strong:F0}ms " +
                $"lbs={lbs:F0}ms convert={convert:F0}ms " +
                $"fallbackFrames={_fallbackFrameCount} | " +
                $"lbsUsable={usable} lbsRejected={rejected} | " +
                _solverTiming.Describe();
        }

        private bool TryCalculateRendererFrame(
            int frameIndex,
            RendererWork work,
            out NativeSkinningCorrectionFrame frame,
            out bool usedFallback,
            out string errorMessage)
        {
            frame = null;
            usedFallback = false;
            errorMessage = string.Empty;
            if (work.Renderer == null ||
                work.Renderer.sharedMesh == null ||
                work.Renderer.sharedMesh.vertexCount != work.VertexCount)
            {
                errorMessage = "사전 계산 중 Renderer 토폴로지가 변경됐습니다.";
                return false;
            }

            Vector3[] baselineVertices = _readFrameVertices(
                frameIndex,
                work.Renderer);
            return TryCalculateRendererFrameCore(
                frameIndex,
                baselineVertices,
                () => TrySampleBoneTransforms(work.Renderer),
                work,
                out frame,
                out usedFallback,
                out errorMessage);
        }

        // 프레임 독립 계산부. Unity API에 접근하지 않아 워커 스레드에서도 실행 가능하다.
        private bool TryCalculateRendererFrameCore(
            int frameIndex,
            Vector3[] baselineVertices,
            Func<NativeSkinningDualQuaternionTransform[]> boneTransformsProvider,
            RendererWork work,
            out NativeSkinningCorrectionFrame frame,
            out bool usedFallback,
            out string errorMessage)
        {
            frame = null;
            usedFallback = false;
            errorMessage = string.Empty;
            if (baselineVertices == null || baselineVertices.Length != work.VertexCount)
            {
                errorMessage =
                    $"frame {frameIndex}의 스키닝 정점 수가 계약과 다릅니다.";
                return false;
            }
            if (!NativeSkinningSurfaceCorrectionCalculator.AreVerticesFinite(
                    baselineVertices))
            {
                errorMessage =
                    $"frame {frameIndex}의 스키닝 정점에 유한하지 않은 값이 있습니다.";
                return false;
            }

            var deltaByVertex = new Dictionary<int, Vector3>();
            var surfaceDeltaByVertex = new Dictionary<int, Vector3>();
            if (!TryCalculateSurfaceCorrections(
                    frameIndex,
                    baselineVertices,
                    work,
                    boneTransformsProvider,
                    out NativeSkinningSurfaceCorrectionResult[] surfaceCorrections,
                    out bool surfaceUsedFallback,
                    out NativeSkinningDualQuaternionTransform[] boneTransforms,
                    out errorMessage))
            {
                return false;
            }
            usedFallback |= surfaceUsedFallback;
            if (!AccumulateContractCorrections(frameIndex, baselineVertices, work,
                    surfaceCorrections, boneTransformsProvider, ref boneTransforms,
                    deltaByVertex, surfaceDeltaByVertex, out errorMessage))
            {
                return false;
            }

            if (!TryValidateCombinedQuality(frameIndex, baselineVertices,
                    ref deltaByVertex, surfaceDeltaByVertex, work.Contracts,
                    ref usedFallback, out errorMessage))
            {
                return false;
            }

            frame = BuildCorrectionFrame(frameIndex, deltaByVertex);
            return true;
        }

        private static bool TryValidateCombinedQuality(int frameIndex,
            Vector3[] baselineVertices,
            ref Dictionary<int, Vector3> deltaByVertex,
            Dictionary<int, Vector3> surfaceDeltaByVertex,
            NativeSkinningSurfaceContract[] contracts,
            ref bool usedFallback, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (AreCombinedCorrectionsWithinQuality(baselineVertices, deltaByVertex, contracts))
            {
                return true;
            }
            // 계약별로 안전해도 공유 정점의 delta 합산으로 품질이 깨질 수 있음.
            usedFallback = true;
            deltaByVertex = surfaceDeltaByVertex;
            if (AreCombinedCorrectionsWithinQuality(baselineVertices, deltaByVertex, contracts))
            {
                return true;
            }
            errorMessage = $"frame {frameIndex}의 합산 Native 표면 보정이 품질 기준을 넘었습니다.";
            return false;
        }

        private static NativeSkinningCorrectionFrame BuildCorrectionFrame(
            int frameIndex, Dictionary<int, Vector3> deltaByVertex)
        {
            NativeSkinningVertexCorrection[] corrections = deltaByVertex
                .OrderBy(entry => entry.Key)
                .Select(entry => new NativeSkinningVertexCorrection(
                    entry.Key,
                    entry.Value))
                .Where(correction =>
                    correction.Delta.sqrMagnitude > MinimumCorrectionSquaredMagnitude)
                .ToArray();
            return corrections.Length == 0
                ? null
                : new NativeSkinningCorrectionFrame(frameIndex, corrections);
        }

        private bool AccumulateContractCorrections(
            int frameIndex, Vector3[] baselineVertices, RendererWork work,
            NativeSkinningSurfaceCorrectionResult[] surfaceCorrections,
            Func<NativeSkinningDualQuaternionTransform[]> boneTransformsProvider,
            ref NativeSkinningDualQuaternionTransform[] boneTransforms,
            Dictionary<int, Vector3> deltaByVertex,
            Dictionary<int, Vector3> surfaceDeltaByVertex,
            out string errorMessage)
        {
            errorMessage = string.Empty;
            for (int contractIndex = 0;
                 contractIndex < work.Contracts.Length;
                 contractIndex++)
            {
                NativeSkinningSurfaceContract contract =
                    work.Contracts[contractIndex];
                NativeSkinningSurfaceCorrectionResult correction =
                    surfaceCorrections[contractIndex];
                IReadOnlyList<Vector3> correctedVertices =
                    correction.CorrectedVertices;
                IReadOnlyCollection<int> correctedVertexIndices =
                    correction.CorrectedVertexIndices;
                AddSparseCorrections(baselineVertices, correctedVertices,
                    correctedVertexIndices, surfaceDeltaByVertex);

                if (correction.UsedRestShapeRecovery &&
                    !ApplyRestShapeRecovery(frameIndex, baselineVertices, work,
                        contractIndex, correctedVertices, correctedVertexIndices,
                        boneTransformsProvider, ref boneTransforms,
                        out correctedVertices, out correctedVertexIndices,
                        out errorMessage))
                {
                    return false;
                }
                AddSparseCorrections(
                    baselineVertices,
                    correctedVertices,
                    correctedVertexIndices,
                    deltaByVertex);
            }
            return true;
        }

        private static bool ApplyRestShapeRecovery(
            int frameIndex, Vector3[] baselineVertices, RendererWork work,
            int contractIndex,
            IReadOnlyList<Vector3> correctedVertices,
            IReadOnlyCollection<int> correctedVertexIndices,
            Func<NativeSkinningDualQuaternionTransform[]> boneTransformsProvider,
            ref NativeSkinningDualQuaternionTransform[] boneTransforms,
            out IReadOnlyList<Vector3> outCorrectedVertices,
            out IReadOnlyCollection<int> outCorrectedVertexIndices,
            out string errorMessage)
        {
            errorMessage = string.Empty;
            outCorrectedVertices = correctedVertices;
            outCorrectedVertexIndices = correctedVertexIndices;
            if (boneTransforms == null)
            {
                boneTransforms = boneTransformsProvider?.Invoke();
            }
            if (boneTransforms == null)
            {
                errorMessage =
                    $"frame {frameIndex}의 Native 스키닝 본 자세를 변환하지 못했습니다.";
                return false;
            }

            if (!NativeSkinningDualQuaternionBlendCalculator
                    .TryCalculateWithPreparedTransforms(
                        correctedVertices,
                        work.RestVertices,
                        boneTransforms,
                        work.BlendContracts[contractIndex],
                        out NativeSkinningDualQuaternionBlendResult
                            volumeCorrection,
                        baselineVertices))
            {
                errorMessage =
                    $"frame {frameIndex}의 Native 체적 보정을 계산하지 못했습니다.";
                return false;
            }
            outCorrectedVertices = volumeCorrection.BlendedVertices;
            outCorrectedVertexIndices = correctedVertexIndices
                .Concat(volumeCorrection.ChangedVertexIndices)
                .Distinct()
                .ToArray();
            return true;
        }

        private static bool AreCombinedCorrectionsWithinQuality(
            Vector3[] baselineVertices,
            IReadOnlyDictionary<int, Vector3> deltaByVertex,
            NativeSkinningSurfaceContract[] contracts)
        {
            var correctedVertices = (Vector3[])baselineVertices.Clone();
            foreach (KeyValuePair<int, Vector3> correction in deltaByVertex)
            {
                if (correction.Value.sqrMagnitude > MinimumCorrectionSquaredMagnitude)
                    correctedVertices[correction.Key] += correction.Value;
            }
            return contracts.All(contract => NativeSkinningSurfaceCorrectionCalculator
                .IsCorrectedFrameWithinStrongQuality(baselineVertices, correctedVertices, contract));
        }

        // 순차 경로에서만 사용: 필요할 때만 본 자세를 표본화한다 (메인스레드 전용).
        private static NativeSkinningDualQuaternionTransform[]
            TrySampleBoneTransforms(SkinnedMeshRenderer renderer)
        {
            return NativeSkinningBoneTransformSampler.TrySample(
                    renderer,
                    out Matrix4x4[] skinningMatrices) &&
                NativeSkinningDualQuaternionVertexCalculator.TryBuildTransforms(
                    skinningMatrices,
                    out NativeSkinningDualQuaternionTransform[] transforms)
                ? transforms
                : null;
        }

        private bool TryResolvePreparedBaseline(int frameIndex,
            RendererWork work, Matrix4x4[] skinningMatrices,
            ref Vector3[] baselineVertices, out string fatalMessage)
        {
            fatalMessage = null;
            if (baselineVertices != null)
            {
                // 함께 캡처된 베이크 베이스라인하고만 표결한다 — 워커가 방금 채운
                // LBS 결과를 자기 자신과 비교하는 표결은 의미가 없다.
                VoteLbsCalibration(work, skinningMatrices, baselineVertices);
                return true;
            }
            // 행렬 전용 프레임은 LBS 사용 확정 상태에서만 풀 수 있다 —
            // 미검증·거부 상태로 넘어온 입력은 베이크 없이 복원할 수 없으므로
            // 세션을 실패시켜 베이크 경로 재준비로 수렴시킨다.
            if (work.LbsVerdict != RendererWork.LbsUsable)
            {
                fatalMessage =
                    $"frame {frameIndex}은 LBS가 미검증·거부된 상태의 " +
                    "행렬 전용 프레임이라 베이스라인을 복원할 수 없습니다.";
                return false;
            }
            // 베이크 베이스라인이 없으면 워커가 LBS로 직접 계산한다 —
            // 스키닝 행렬이 캡처돼 있으면 Unity API 없이 복원 가능하다.
            var lbsWatch = System.Diagnostics.Stopwatch.StartNew();
            if (!TryComputeLbsBaseline(
                    work,
                    skinningMatrices,
                    out baselineVertices))
            {
                fatalMessage =
                    $"frame {frameIndex}의 LBS 베이스라인을 계산하지 못했습니다.";
                return false;
            }
            System.Threading.Interlocked.Add(
                ref _lbsTicks,
                lbsWatch.ElapsedTicks);
            return true;
        }

        private Func<NativeSkinningDualQuaternionTransform[]> CreateBoneTransformsProvider(
            Matrix4x4[] skinningMatrices)
        {
            NativeSkinningDualQuaternionTransform[] capturedTransforms = null;
            return () =>
            {
                if (capturedTransforms == null)
                {
                    var convertWatch =
                        System.Diagnostics.Stopwatch.StartNew();
                    capturedTransforms =
                        ConvertSkinningMatrices(skinningMatrices);
                    System.Threading.Interlocked.Add(
                        ref _convertTicks,
                        convertWatch.ElapsedTicks);
                }
                return capturedTransforms;
            };
        }

        // 워커에서 LBS 베이스라인을 복원한다 — 베이크 없이도 솔버 입력을 채운다.
        private static bool TryComputeLbsBaseline(
            RendererWork work,
            Matrix4x4[] skinningMatrices,
            out Vector3[] baselineVertices)
        {
            baselineVertices = null;
            if (work == null || skinningMatrices == null || work.BoneWeights == null)
            {
                return false;
            }
            var computed = new Vector3[work.VertexCount];
            if (!NativeSkinningLinearBlendCalculator.TryComputeBaseline(
                    work.RestVertices,
                    work.BoneWeights,
                    skinningMatrices,
                    computed))
            {
                return false;
            }
            baselineVertices = computed;
            return true;
        }

        // 스키닝 행렬 → 이중 쿼터니언 변환을 워커에서 지연 수행한다 (순수 수학).
        private static NativeSkinningDualQuaternionTransform[]
            ConvertSkinningMatrices(Matrix4x4[] skinningMatrices)
        {
            return skinningMatrices != null &&
                NativeSkinningDualQuaternionVertexCalculator.TryBuildTransforms(
                    skinningMatrices,
                    out NativeSkinningDualQuaternionTransform[] transforms)
                ? transforms
                : null;
        }

        // 베이크 베이스라인과 LBS 결과를 전 정점 비교해 LBS 사용 가능 여부를 표결한다.
        // 미검증 구간에서는 비교 횟수가 임계에 도달하면 사용으로 확정하고,
        // 언제든 편차 초과가 나오면 거부로 전환해 베이크 경로로 되돌린다.
        private void VoteLbsCalibration(
            RendererWork work,
            Matrix4x4[] skinningMatrices,
            Vector3[] bakedBaselineVertices)
        {
            if (work == null ||
                skinningMatrices == null ||
                work.LbsVerdict == RendererWork.LbsRejected)
            {
                return;
            }
            var lbsWatch = System.Diagnostics.Stopwatch.StartNew();
            bool withinTolerance =
                TryComputeLbsBaseline(
                    work,
                    skinningMatrices,
                    out Vector3[] lbsBaseline) &&
                NativeSkinningLinearBlendCalculator.MaxDeviation(
                    lbsBaseline,
                    bakedBaselineVertices) <=
                    NativeSkinningLinearBlendCalculator
                        .BaselineMaxDeviationMeters;
            System.Threading.Interlocked.Add(
                ref _lbsTicks,
                lbsWatch.ElapsedTicks);
            if (withinTolerance)
            {
                // 사용 확정은 미결정 상태에서만 — 거부는 어느 표결보다 우선한다.
                if (System.Threading.Interlocked.Increment(
                        ref work.LbsComparedFrameCount) >=
                    LbsCalibrationFrameCount)
                {
                    System.Threading.Interlocked.CompareExchange(
                        ref work.LbsVerdict,
                        RendererWork.LbsUsable,
                        RendererWork.LbsUndecided);
                }
            }
            else
            {
                // 거부는 즉시 확정 — 동시 표결이 사용으로 되돌리지 못한다.
                System.Threading.Interlocked.Exchange(
                    ref work.LbsVerdict,
                    RendererWork.LbsRejected);
            }
        }

    }
}
