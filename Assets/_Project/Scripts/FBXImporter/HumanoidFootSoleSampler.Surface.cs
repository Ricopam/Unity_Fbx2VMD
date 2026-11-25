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
            private const int InfluencesPerVertex = 4;
            private const float WeightTolerance = 0.00001f;

            internal readonly SkinnedMeshRenderer Renderer;
            private readonly int _vertexCount;
            private readonly int[] _footVertices;
            // 발 정점별 (본 Transform, 가중치) 4쌍 — 메시 객체가 바뀌어도 같은 표면인지 판정하는 기준.
            private readonly Transform[] _influenceBones;
            private readonly float[] _influenceWeights;
            private readonly Mesh _baked;
            private readonly List<Vector3> _vertices;
            private Mesh _source;
            private Transform[] _bones;

            internal Surface(SkinnedMeshRenderer renderer, Mesh source, Transform[] bones,
                BoneWeight[] weights, List<int> footVertices)
            {
                Renderer = renderer;
                _source = source;
                _bones = bones;
                _vertexCount = source.vertexCount;
                _footVertices = footVertices.ToArray();
                _influenceBones = new Transform[_footVertices.Length * InfluencesPerVertex];
                _influenceWeights = new float[_influenceBones.Length];
                _baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                _vertices = new List<Vector3>(source.vertexCount);
                // 수집 단계에서 본 번호 유효성을 이미 검사했으므로 실패하지 않음.
                ResolveInfluences(weights, bones, _influenceBones, _influenceWeights);
            }

            internal bool TryBake()
            {
                if (Renderer == null || _source == null)
                    return false;
                if (!IsBoundToCurrentMesh() && !TryRebindToCurrentMesh())
                    return false;
                Renderer.BakeMesh(_baked);
                _baked.GetVertices(_vertices);
                return _vertices.Count == _vertexCount;
            }

            internal Vector3 GetWorldPoint(int vertex) => Renderer.localToWorldMatrix.MultiplyPoint3x4(_vertices[vertex]);
            public void Dispose() => UnityEngine.Object.DestroyImmediate(_baked);

            private bool IsBoundToCurrentMesh()
            {
                if (Renderer.sharedMesh != _source || _source.vertexCount != _vertexCount)
                    return false;
                Transform[] bones = Renderer.bones;
                if (bones.Length != _bones.Length)
                    return false;
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] != _bones[i])
                        return false;
                return true;
            }

            // MagicaCloth 커스텀 메시 교체처럼 정점 순서를 유지한 메시·본 배열 교체는
            // 발 정점의 본 영향이 같을 때만 새 메시로 다시 붙임 — 다른 메시면 실패를 유지함.
            private bool TryRebindToCurrentMesh()
            {
                Mesh mesh = Renderer.sharedMesh;
                if (mesh == null || mesh.vertexCount != _vertexCount)
                    return false;
                BoneWeight[] weights = mesh.boneWeights;
                if (weights.Length != _vertexCount)
                    return false;
                Transform[] bones = Renderer.bones;
                var influenceBones = new Transform[_influenceBones.Length];
                var influenceWeights = new float[_influenceWeights.Length];
                if (!ResolveInfluences(weights, bones, influenceBones, influenceWeights))
                    return false;
                for (int i = 0; i < influenceBones.Length; i++)
                {
                    bool isSameInfluence = influenceBones[i] == _influenceBones[i] &&
                        Mathf.Abs(influenceWeights[i] - _influenceWeights[i]) <= WeightTolerance;
                    if (!isSameInfluence)
                        return false;
                }
                _source = mesh;
                _bones = bones;
                return true;
            }

            private bool ResolveInfluences(BoneWeight[] weights, Transform[] bones,
                Transform[] influenceBones, float[] influenceWeights)
            {
                for (int i = 0; i < _footVertices.Length; i++)
                {
                    BoneWeight w = weights[_footVertices[i]];
                    int slot = i * InfluencesPerVertex;
                    if (!TryResolveInfluence(bones, w.boneIndex0, w.weight0, influenceBones, influenceWeights, slot) ||
                        !TryResolveInfluence(bones, w.boneIndex1, w.weight1, influenceBones, influenceWeights, slot + 1) ||
                        !TryResolveInfluence(bones, w.boneIndex2, w.weight2, influenceBones, influenceWeights, slot + 2) ||
                        !TryResolveInfluence(bones, w.boneIndex3, w.weight3, influenceBones, influenceWeights, slot + 3))
                        return false;
                }
                return true;
            }

            // 가중치 0인 슬롯은 본 번호와 무관하게 빈 영향으로 정규화함.
            private static bool TryResolveInfluence(Transform[] bones, int index, float weight,
                Transform[] influenceBones, float[] influenceWeights, int slot)
            {
                influenceBones[slot] = null;
                influenceWeights[slot] = 0f;
                if (weight == 0f)
                    return true;
                if (index < 0 || index >= bones.Length)
                    return false;
                influenceBones[slot] = bones[index];
                influenceWeights[slot] = weight;
                return true;
            }
        }
    }
}
