using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 발바닥 표면 베이크 결과를 보관하는 중첩 표면 타입 partial.
    internal sealed partial class HumanoidFootSoleSampler
    {
        private sealed class Surface : IDisposable
        {
            internal readonly SkinnedMeshRenderer Renderer;
            private readonly Mesh _source;
            private readonly Transform[] _bones;
            private readonly int _vertexCount;
            private readonly Mesh _baked;
            private readonly List<Vector3> _vertices;

            internal Surface(SkinnedMeshRenderer renderer, Mesh source, Transform[] bones)
            {
                Renderer = renderer;
                _source = source;
                _bones = bones;
                _vertexCount = source.vertexCount;
                _baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                _vertices = new List<Vector3>(source.vertexCount);
            }

            internal bool TryBake()
            {
                if (Renderer == null || _source == null || Renderer.sharedMesh != _source || _source.vertexCount != _vertexCount)
                    return false;
                Transform[] bones = Renderer.bones;
                if (bones.Length != _bones.Length)
                    return false;
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] != _bones[i])
                        return false;
                Renderer.BakeMesh(_baked);
                _baked.GetVertices(_vertices);
                return _vertices.Count == _source.vertexCount;
            }

            internal Vector3 GetWorldPoint(int vertex) => Renderer.localToWorldMatrix.MultiplyPoint3x4(_vertices[vertex]);
            public void Dispose() => UnityEngine.Object.DestroyImmediate(_baked);
        }
    }
}
