using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEditor;

namespace Fbx2Vmd.FBXImporter
{
    // 캐시 키용 파일·에셋·정점 해시 계산 partial.
    internal static partial class NativeSkinningCorrectionCacheKey
    {
        // 파일 내용 해시 — 소스 FBX 변경 감지의 공용 수단.
        internal static string ComputeFileHash(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            {
                return string.Empty;
            }
            using (var hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(fullPath))
            {
                return Convert.ToBase64String(hash.ComputeHash(stream)) +
                    ":" + stream.Length;
            }
        }

#if UNITY_EDITOR
        internal static string GetAssetIdentity(UnityEngine.Object asset)
        {
            if (asset == null)
            {
                return string.Empty;
            }
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path))
            {
                // 비영속 자산은 이름+내용 해시(정점 해시)가 실체를 대표한다.
                return "nonasset:" + asset.name;
            }
            // 같은 FBX도 Avatar·클립 임포트 설정에 따라 평가 자세가 달라져 결과 해시를 함께 식별함.
            return "guid:" + AssetDatabase.AssetPathToGUID(path) +
                ",path:" + path +
                ",dependency:" + AssetDatabase.GetAssetDependencyHash(path);
        }

        // 모션 FBX 같은 소스 파일은 내용 해시로 변경을 감지한다.
        private static string GetAssetFileHash(UnityEngine.Object asset)
        {
            if (asset == null)
            {
                return string.Empty;
            }
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }
            return ComputeFileHash(Path.GetFullPath(path));
        }
#endif

        private static string ComputeVertexHash(Mesh mesh)
        {
            // 정점 인덱스 재배열·임포트 설정·토폴로지 변경을 감지하기 위해
            // 정점 위치와 삼각형 인덱스를 함께 해시한다.
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            using (var stream = new MemoryStream(
                       vertices.Length * 12 + triangles.Length * 4 + 8))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(vertices.Length);
                foreach (Vector3 vertex in vertices)
                {
                    writer.Write(vertex.x);
                    writer.Write(vertex.y);
                    writer.Write(vertex.z);
                }
                writer.Write(triangles.Length);
                foreach (int index in triangles)
                {
                    writer.Write(index);
                }
                writer.Flush();
                using (var hash = SHA256.Create())
                {
                    return Convert.ToBase64String(
                        hash.ComputeHash(stream.GetBuffer(), 0,
                            (int)stream.Length)); } } }

    }
}
