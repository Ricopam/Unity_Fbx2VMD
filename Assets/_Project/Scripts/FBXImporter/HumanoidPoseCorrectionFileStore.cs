using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 모델 중립 Humanoid 보정 문서를 원자적으로 저장하고 검증해 불러옴.
    /// </summary>
    internal static class HumanoidPoseCorrectionFileStore
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        internal static bool TrySave(
            string filePath,
            HumanoidPoseCorrectionDocument document,
            out string errorMessage)
        {
            string temporaryPath = string.Empty;
            try
            {
                if (!TryValidateSaveInput(filePath, document, out string resolvedPath, out errorMessage))
                {
                    return false;
                }

                WriteDocumentAtomically(resolvedPath, document, ref temporaryPath);
                errorMessage = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                errorMessage = $"Humanoid 보정 문서를 저장하지 못했습니다: {exception.Message}";
                return false;
            }
            finally
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }

        // 저장 경로 해석과 문서 유효성을 함께 검증함.
        private static bool TryValidateSaveInput(
            string filePath, HumanoidPoseCorrectionDocument document,
            out string resolvedPath, out string errorMessage)
        {
            if (!TryResolvePath(filePath, out resolvedPath, out errorMessage))
            {
                return false;
            }

            if (document == null)
            {
                errorMessage = "저장할 Humanoid 보정 문서가 없습니다.";
                return false;
            }

            return document.TryValidate(out errorMessage);
        }

        // 임시 파일에 쓴 뒤 Replace/Move로 원자적으로 반영함.
        private static void WriteDocumentAtomically(
            string resolvedPath, HumanoidPoseCorrectionDocument document, ref string temporaryPath)
        {
            string directoryPath = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            temporaryPath = resolvedPath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(document, prettyPrint: true), Utf8NoBom);

            if (File.Exists(resolvedPath))
            {
                File.Replace(temporaryPath, resolvedPath, null);
            }
            else
            {
                File.Move(temporaryPath, resolvedPath);
            }
        }

        internal static bool TryLoad( string filePath, out HumanoidPoseCorrectionDocument document, out string errorMessage)
        {
            document = null;
            try
            {
                if (!TryResolvePath(filePath, out string resolvedPath, out errorMessage))
                {
                    return false; }
                if (!File.Exists(resolvedPath))
                {
                    errorMessage = $"Humanoid 보정 문서를 찾지 못했습니다: {resolvedPath}";
                    return false; }
                if (!TryReadDocumentJson(resolvedPath, out string json, out errorMessage))
                {
                    return false; }
                if (!TryParseDocument(json, out document, out errorMessage))
                {
                    return false; }
                errorMessage = string.Empty;
                return true; } catch (Exception exception)
            {
                errorMessage = $"Humanoid 보정 문서를 불러오지 못했습니다: {exception.Message}";
                return false; } }

        // 파일에서 JSON 본문을 읽고 비어 있지 않은지 확인함.
        private static bool TryReadDocumentJson(
            string resolvedPath, out string json, out string errorMessage)
        {
            json = File.ReadAllText(resolvedPath, Utf8NoBom);
            if (!string.IsNullOrWhiteSpace(json))
            {
                errorMessage = null;
                return true;
            }
            errorMessage = "Humanoid 보정 문서가 비어 있습니다.";
            return false;
        }

        // JSON을 문서로 역직렬화하고 형식을 검증함.
        private static bool TryParseDocument(
            string json, out HumanoidPoseCorrectionDocument document, out string errorMessage)
        {
            errorMessage = null;
            document = JsonUtility.FromJson<HumanoidPoseCorrectionDocument>(json);
            if (document == null || !document.TryValidate(out errorMessage))
            {
                errorMessage = string.IsNullOrWhiteSpace(errorMessage)
                    ? "Humanoid 보정 문서 형식이 올바르지 않습니다." : errorMessage;
                return false;
            }
            return true;
        }

        private static bool TryResolvePath(
            string filePath,
            out string resolvedPath,
            out string errorMessage)
        {
            resolvedPath = string.Empty;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                errorMessage = "Humanoid 보정 문서 경로가 필요합니다.";
                return false;
            }

            resolvedPath = Path.GetFullPath(filePath.Trim());
            errorMessage = string.Empty;
            return true;
        }

        private static void TryDeleteTemporaryFile(string temporaryPath)
        {
            if (string.IsNullOrWhiteSpace(temporaryPath) ||
                !File.Exists(temporaryPath))
            {
                return;
            }

            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // 저장 결과를 반환한 뒤 임시 파일 정리 실패로 결과를 뒤집지 않음.
            }
            catch (UnauthorizedAccessException)
            {
                // 저장 결과를 반환한 뒤 임시 파일 정리 실패로 결과를 뒤집지 않음.
            }
        }
    }
}
