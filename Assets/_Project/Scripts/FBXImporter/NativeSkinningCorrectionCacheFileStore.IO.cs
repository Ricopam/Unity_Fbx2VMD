using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal static partial class NativeSkinningCorrectionCacheFileStore
    {

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool AreEqual(byte[] first, byte[] second)
        {
            if (first.Length != second.Length)
            {
                return false;
            }
            for (int index = 0; index < first.Length; index++)
            {
                if (first[index] != second[index])
                {
                    return false;
                }
            }
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

        // 표준 CRC-32 (poly 0xEDB88320) — 저장 페이로드 손상 검출용.
        private static class Crc32
        {
            private static readonly uint[] Table = BuildTable();

            internal static uint Compute(byte[] data)
            {
                uint value = 0xFFFFFFFFu;
                for (int index = 0; index < data.Length; index++)
                {
                    value = Table[(value ^ data[index]) & 0xFF] ^
                        (value >> 8);
                }
                return ~value;
            }

            private static uint[] BuildTable()
            {
                var table = new uint[256];
                for (uint index = 0; index < 256; index++)
                {
                    uint value = index;
                    for (int bit = 0; bit < 8; bit++)
                    {
                        value = (value & 1) != 0
                            ? 0xEDB88320u ^ (value >> 1)
                            : value >> 1;
                    }
                    table[index] = value;
                }
                return table;
            }
        }
    }
}
