using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class NativeSkinningCorrectionPreprocessSession
    {
        // 진단 디렉터리는 워커 스레드에서 읽을 수 없으므로 생성 시점에 미리 구한다.
        private readonly string _diagnosticsDirectory;

        private bool TryCalculateSurfaceCorrections(
            int frameIndex,
            IReadOnlyList<Vector3> vertices,
            RendererWork work,
            Func<NativeSkinningDualQuaternionTransform[]> boneTransformsProvider,
            out NativeSkinningSurfaceCorrectionResult[] corrections,
            out bool usedFallback,
            out NativeSkinningDualQuaternionTransform[] boneTransforms,
            out string errorMessage)
        {
            NativeSkinningSurfaceContract[] contracts = work.Contracts;
            boneTransforms = null;
            var calculatedCorrections =
                new NativeSkinningSurfaceCorrectionResult[contracts.Length];
            corrections = calculatedCorrections;
            var successByContract = new bool[contracts.Length];
            var fallbackByContract = new bool[contracts.Length];
            var errorByContract = new string[contracts.Length];
            CalculateAllContracts(frameIndex, vertices, contracts,
                calculatedCorrections, successByContract, fallbackByContract,
                errorByContract);

            RetryFailedContracts(frameIndex, vertices, work, contracts,
                calculatedCorrections, successByContract, fallbackByContract,
                boneTransformsProvider, ref boneTransforms);

            usedFallback = fallbackByContract.Any(value => value);
            int failedContractIndex = Array.FindIndex(
                successByContract,
                success => !success);
            errorMessage = failedContractIndex < 0
                ? string.Empty
                : BuildCorrectionErrorMessage(frameIndex, vertices, work,
                    contracts, calculatedCorrections, successByContract,
                    errorByContract, failedContractIndex);
            return failedContractIndex < 0;
        }

        private void CalculateAllContracts(int frameIndex,
            IReadOnlyList<Vector3> vertices,
            NativeSkinningSurfaceContract[] contracts,
            NativeSkinningSurfaceCorrectionResult[] calculatedCorrections,
            bool[] successByContract, bool[] fallbackByContract,
            string[] errorByContract)
        {
            if (contracts.Length == 1)
            {
                bool fallback = false;
                bool success = TryCalculateValidatedSurfaceCorrection(
                    frameIndex,
                    vertices,
                    contracts[0],
                    ref fallback,
                    out calculatedCorrections[0],
                    out errorByContract[0]);
                successByContract[0] = success;
                fallbackByContract[0] = fallback;
                return;
            }
            Parallel.For(0, contracts.Length, contractIndex =>
            {
                bool fallback = false;
                successByContract[contractIndex] =
                    TryCalculateValidatedSurfaceCorrection(
                        frameIndex,
                        vertices,
                        contracts[contractIndex],
                        ref fallback,
                        out calculatedCorrections[contractIndex],
                        out errorByContract[contractIndex]);
                fallbackByContract[contractIndex] = fallback;
            });
        }

        private void RetryFailedContracts(int frameIndex,
            IReadOnlyList<Vector3> vertices, RendererWork work,
            NativeSkinningSurfaceContract[] contracts,
            NativeSkinningSurfaceCorrectionResult[] calculatedCorrections,
            bool[] successByContract, bool[] fallbackByContract,
            Func<NativeSkinningDualQuaternionTransform[]> boneTransformsProvider,
            ref NativeSkinningDualQuaternionTransform[] boneTransforms)
        {
            // 실패 계약만 재시도하고 같은 렌더러의 본 변환을 한 번만 준비함.
            bool resolvedBoneTransforms = false;
            for (int index = 0; index < contracts.Length; index++)
            {
                NativeSkinningSurfaceCorrectionResult previous = calculatedCorrections[index];
                if (successByContract[index] || previous == null || previous.ResidualSharpFoldCount == 0)
                {
                    continue;
                }

                if (!resolvedBoneTransforms)
                {
                    boneTransforms = boneTransformsProvider?.Invoke();
                    resolvedBoneTransforms = true;
                }
                long retryStart = System.Diagnostics.Stopwatch.GetTimestamp();
                if (NativeSkinningSurfaceCorrectionCalculator.TryRecoverFailedFrame(
                        vertices, contracts[index], previous, work.RestVertices, work.BoneWeights,
                        boneTransforms, _solverTiming, out NativeSkinningSurfaceCorrectionResult recovered))
                {
                    calculatedCorrections[index] = recovered;
                    successByContract[index] = true;
                    fallbackByContract[index] = true;
                }
                System.Threading.Interlocked.Add(ref _strongPathTicks,
                    System.Diagnostics.Stopwatch.GetTimestamp() - retryStart);
            }
        }

        private string BuildCorrectionErrorMessage(int frameIndex,
            IReadOnlyList<Vector3> vertices, RendererWork work,
            NativeSkinningSurfaceContract[] contracts,
            NativeSkinningSurfaceCorrectionResult[] calculatedCorrections,
            bool[] successByContract, string[] errorByContract,
            int failedContractIndex)
        {
            string failure = string.IsNullOrWhiteSpace(
                    errorByContract[failedContractIndex])
                ? $"frame {frameIndex}의 Native 표면 보정을 계산하지 못했습니다."
                : errorByContract[failedContractIndex];
            NativeSkinningSurfaceContract failedContract =
                contracts[failedContractIndex];
            NativeSkinningSurfaceCorrectionResult failedCorrection =
                calculatedCorrections[failedContractIndex];
            string errorMessage = $"{failure} renderer={work.RendererName} " +
                $"side={failedContract.Side} contract={failedContractIndex}";
            errorMessage += " failedContractFolds=" + string.Join(",",
                Enumerable.Range(0, contracts.Length)
                    .Where(index => !successByContract[index])
                    .Select(index => $"{index}:{contracts[index].Side}:" +
                        (calculatedCorrections[index] == null
                            ? "none"
                            : calculatedCorrections[index]
                                .ResidualSharpFoldCount.ToString())));
            TryDumpFailureBaselineVertices(
                frameIndex,
                work.RendererName,
                vertices);
            if (failedCorrection == null)
            {
                return errorMessage;
            }
            return errorMessage + BuildFailureDetail(
                vertices, failedContract, failedCorrection);
        }

        private static string BuildFailureDetail(
            IReadOnlyList<Vector3> vertices,
            NativeSkinningSurfaceContract failedContract,
            NativeSkinningSurfaceCorrectionResult failedCorrection)
        {
            string detail =
                $" initialFold={failedCorrection.InitialSharpFoldCount}" +
                $" residualFold={failedCorrection.ResidualSharpFoldCount}" +
                $" newFold={failedCorrection.NewSharpFoldCount}" +
                $" degenerate={failedCorrection.NewDegenerateFaceCount}" +
                $" reversed={failedCorrection.ReversedFaceCount}" +
                $" displacement={failedCorrection.MaximumVertexDisplacement:F6}" +
                $" strain={failedCorrection.MaximumEdgeLengthStrain:F6}" +
                $" restRecovery={failedCorrection.UsedRestShapeRecovery}" +
                $" restStrain={failedCorrection.MaximumRestEdgeStrainIncrease:F6}" +
                $" unresolved={failedCorrection.UnresolvedRestShapeRecoveryCount}";
            for (int pairIndex = 0;
                 pairIndex < failedContract.FacePairs.Length;
                 pairIndex++)
            {
                if (failedContract.RestAnglesDegrees[pairIndex] >
                        NativeSkinningSurfaceDefectDetector
                            .MaximumCorrectableRestAngleDegrees ||
                    !NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                        failedCorrection.CorrectedVertices,
                        failedContract.FacePairs[pairIndex],
                        out float correctedAngle) ||
                    correctedAngle <= NativeSkinningSurfaceDefectDetector
                        .MinimumSharpFoldAngleDegrees)
                {
                    continue;
                }

                NativeSkinningSurfaceContractBuilder.TryMeasureAngle(
                    vertices,
                    failedContract.FacePairs[pairIndex],
                    out float baselineAngle);
                detail += $" pair={pairIndex}" +
                    $" restAngle={failedContract.RestAnglesDegrees[pairIndex]:F2}" +
                    $" baselineAngle={baselineAngle:F2}" +
                    $" correctedAngle={correctedAngle:F2}";
                break;
            }
            return detail;
        }

        // 수집 모드 완료 시 누적된 실패 목록을 로그 파일로 남긴다.
        private void WriteCollectedFailures()
        {
            if (_collectedFailures.Count == 0)
            {
                return;
            }
            try
            {
                string directory = _diagnosticsDirectory;
                Directory.CreateDirectory(directory);
                // 연속 런 간 덮어쓰기를 피하기 위해 타임스탬프를 붙인다.
                string path = Path.Combine(
                    directory,
                    $"native-prep-failures-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
                File.WriteAllLines(path, _collectedFailures);
                Debug.LogWarning(
                    $"[NativePrepDump] 실패 {_collectedFailures.Count}건 수집 완료: {path}");
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[NativePrepDump] 실패 목록 기록 실패: {exception.Message}");
            }
        }

        internal static string GetDiagnosticsDirectory()
        {
            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "../Docs/Workflow/Local/runtime/native-prep-dumps"));
        }

        // 진단용: 플래그 파일이 있을 때만 실패 프레임의 입력 정점을 덤프함.
        private void TryDumpFailureBaselineVertices(
            int frameIndex,
            string rendererName,
            IReadOnlyList<Vector3> vertices)
        {
            try
            {
                string directory = _diagnosticsDirectory;
                if (!File.Exists(
                        Path.Combine(directory, "native-prep-dump.flag")))
                {
                    return;
                }
                Directory.CreateDirectory(directory);
                // GameObject 이름에는 파일명 비허용 문자(경로 구분자 포함)가 올 수 있어 정제한다.
                string safeName = rendererName ?? "null";
                foreach (char invalid in Path.GetInvalidFileNameChars())
                {
                    safeName = safeName.Replace(invalid, '_');
                }
                string path = Path.Combine(directory,
                    $"f{frameIndex}-{safeName}.bin");
                using var writer = new BinaryWriter(File.Create(path));
                writer.Write(vertices.Count);
                foreach (Vector3 vertex in vertices)
                {
                    writer.Write(vertex.x);
                    writer.Write(vertex.y);
                    writer.Write(vertex.z);
                }
                Debug.Log(
                    $"[NativePrepDump] frame={frameIndex} renderer={rendererName} " +
                    $"verts={vertices.Count} path={path}");
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[NativePrepDump] 실패 입력 덤프 기록 실패: {exception.Message}");
            }
        }

        private bool TryCalculateValidatedSurfaceCorrection(
            int frameIndex,
            IReadOnlyList<Vector3> vertices,
            NativeSkinningSurfaceContract contract,
            ref bool usedFallback,
            out NativeSkinningSurfaceCorrectionResult correction,
            out string errorMessage)
        {
            correction = null;
            errorMessage = string.Empty;
            var pathWatch = System.Diagnostics.Stopwatch.StartNew();
            if (!NativeSkinningSurfaceCorrectionCalculator
                    .TryCalculateFastValidatedFrameTimed(
                        vertices,
                        contract,
                        _solverTiming,
                        out correction))
            {
                errorMessage =
                    $"frame {frameIndex}의 빠른 표면 보정을 계산하지 못했습니다.";
                return false;
            }
            System.Threading.Interlocked.Add(
                ref _fastPathTicks, pathWatch.ElapsedTicks);
            if (!NativeSkinningSurfaceCorrectionCalculator
                    .IsWithinFastQuality(correction, contract))
            {
                usedFallback = true;
                pathWatch.Restart();
                if (!NativeSkinningSurfaceCorrectionCalculator
                        .TryCalculateStrongValidatedFrameTimed(
                            vertices,
                            contract,
                            _solverTiming,
                            out correction))
                {
                    errorMessage =
                        $"frame {frameIndex}의 강한 표면 보정을 계산하지 못했습니다.";
                    return false;
                }
                System.Threading.Interlocked.Add(
                    ref _strongPathTicks, pathWatch.ElapsedTicks);
            }
            if (NativeSkinningSurfaceCorrectionCalculator
                    .IsWithinStrongQuality(correction, contract))
            {
                return true;
            }

            errorMessage =
                $"frame {frameIndex}의 Native 보정이 품질 계약을 통과하지 못했습니다.";
            return false;
        }

        private static void AddSparseCorrections(
            IReadOnlyList<Vector3> baselineVertices,
            IReadOnlyList<Vector3> correctedVertices,
            IEnumerable<int> correctedVertexIndices,
            IDictionary<int, Vector3> deltaByVertex)
        {
            foreach (int vertexIndex in correctedVertexIndices)
            {
                Vector3 delta = correctedVertices[vertexIndex] -
                    baselineVertices[vertexIndex];
                if (delta.sqrMagnitude <= MinimumCorrectionSquaredMagnitude)
                {
                    continue;
                }
                deltaByVertex[vertexIndex] =
                    deltaByVertex.TryGetValue(vertexIndex, out Vector3 existing)
                        ? existing + delta
                        : delta;
            }
        }
    }
}
