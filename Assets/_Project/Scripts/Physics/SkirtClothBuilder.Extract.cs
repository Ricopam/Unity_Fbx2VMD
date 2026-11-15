using System.Collections.Generic;
using System.Text.RegularExpressions;
using MagicaCloth2;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using VertexAttribute = MagicaCloth2.VertexAttribute;
using UVertexAttribute = UnityEngine.Rendering.VertexAttribute;

namespace Fbx2Vmd.ClothPhysics
{
    public static partial class SkirtClothBuilder
    {
        /// <summary>
        /// 스커트 버텍스를 1개 이상 참조하는 삼각형을 모아 전용
        /// SkinnedMeshRenderer로 추출한다. 원본 메시는 클론 후 해당
        /// 삼각형을 제거하고 원본 렌더러에 적용한다(에셋 자체는 변경하지 않음).
        /// 혼합 삼각형의 무효 버텍스는 추출본에서 Fixed로 격상되므로
        /// 본체의 인접 삼각형과 이음매 위치가 항상 일치한다(균열 방지).
        /// 반환된 렌더러의 모든 버텍스는 스커트 대상 — remappedAttrs는
        /// 추출 메시의 버텍스 순서에 맞춰 재배열된 속성이다.
        /// </summary>
        public static ExtractedSkirtMesh ExtractSkirtRenderer(
            SkinnedMeshRenderer source, VertexAttribute[] attrs,
            out VertexAttribute[] remappedAttrs)
        {
            remappedAttrs = null;
            var mesh = source.sharedMesh;
            if (mesh == null || attrs == null || attrs.Length != mesh.vertexCount)
                return null;

            int subCount = mesh.subMeshCount;
            var extractTris = new List<int>[subCount];
            var remainTris = new List<int>[subCount];
            var topology = new MeshTopology[subCount];
            var used = new bool[mesh.vertexCount];
            if (!ClassifySkirtTriangles(mesh, attrs, extractTris, remainTris, topology, used))
                return null;

            int vcnt = mesh.vertexCount;
            int ncnt;
            var remap = BuildVertexRemap(used, out ncnt);
            Mesh newMesh = BuildSkirtMesh(
                mesh, attrs, extractTris, used, remap, ncnt, subCount, out remappedAttrs);
            Mesh remainMesh = CloneRemainMesh(mesh, remainTris, topology);
            return CreateExtractedRenderer(source, mesh, newMesh, remainMesh);
        }

        // 서브메시별로 스커트/잔여 삼각형을 분류한다. 1개 이상의 스커트 버텍스를 가진
        // 삼각형도 추출 — 무효 버텍스는 추출본에서 Fixed로 격상돼 본 스키닝 위치를
        // 그대로 추적하므로 본체의 인접 삼각형과 이음매가 어긋나지 않는다 (균열 방지)
        static bool ClassifySkirtTriangles(
            Mesh mesh, VertexAttribute[] attrs,
            List<int>[] extractTris, List<int>[] remainTris,
            MeshTopology[] topology, bool[] used)
        {
            int subCount = mesh.subMeshCount;
            bool any = false;
            for (int s = 0; s < subCount; s++)
            {
                extractTris[s] = new List<int>();
                remainTris[s] = new List<int>();
                topology[s] = mesh.GetTopology(s);
                if (topology[s] != MeshTopology.Triangles)
                {
                    // 비삼각형 서브메시는 스커트 추출 대상에서 제외하고 원형 유지
                    remainTris[s].AddRange(mesh.GetIndices(s));
                    continue;
                }
                var tris = mesh.GetTriangles(s);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    bool skirt = !attrs[tris[i]].IsInvalid()
                        || !attrs[tris[i + 1]].IsInvalid()
                        || !attrs[tris[i + 2]].IsInvalid();
                    var dst = skirt ? extractTris[s] : remainTris[s];
                    dst.Add(tris[i]); dst.Add(tris[i + 1]); dst.Add(tris[i + 2]);
                    if (skirt)
                    {
                        used[tris[i]] = used[tris[i + 1]] = used[tris[i + 2]] = true;
                        any = true;
                    }
                }
            }
            return any;
        }

        static int[] BuildVertexRemap(bool[] used, out int ncnt)
        {
            var remap = new int[used.Length];
            ncnt = 0;
            for (int v = 0; v < used.Length; v++)
                if (used[v]) remap[v] = ncnt++;
            return remap;
        }

        static Mesh BuildSkirtMesh(
            Mesh mesh, VertexAttribute[] attrs, List<int>[] extractTris,
            bool[] used, int[] remap, int ncnt, int subCount,
            out VertexAttribute[] remappedAttrs)
        {
            int vcnt = mesh.vertexCount;
            var newMesh = new Mesh { name = mesh.name + "_skirt" };
            remappedAttrs = FillVerticesAndChannels(mesh, newMesh, attrs, used, remap, ncnt, vcnt);
            CopyBoneWeights(mesh, newMesh, used, remap, ncnt, vcnt);
            SetRemappedTriangles(newMesh, extractTris, remap, subCount);
            newMesh.RecalculateBounds();
            CopyBlendShapes(mesh, newMesh, used, remap, vcnt, ncnt);
            return newMesh;
        }

        static VertexAttribute[] FillVerticesAndChannels(
            Mesh mesh, Mesh newMesh, VertexAttribute[] attrs,
            bool[] used, int[] remap, int ncnt, int vcnt)
        {
            var verts = mesh.vertices;
            var newVerts = new Vector3[ncnt];
            var remappedAttrs = new VertexAttribute[ncnt];
            for (int v = 0; v < vcnt; v++)
                if (used[v])
                {
                    newVerts[remap[v]] = verts[v];
                    // 혼합 삼각형의 무효 버텍스는 Fixed 격상 — 본체와 동일 위치 유지
                    remappedAttrs[remap[v]] = attrs[v].IsInvalid()
                        ? VertexAttribute.Fixed : attrs[v];
                }
            newMesh.vertices = newVerts;
            CopyVertexChannel(mesh, newMesh, remap, used, vcnt, UVertexAttribute.Normal);
            CopyVertexChannel(mesh, newMesh, remap, used, vcnt, UVertexAttribute.Tangent);
            CopyVertexChannel(mesh, newMesh, remap, used, vcnt, UVertexAttribute.Color);
            for (var uv = UVertexAttribute.TexCoord0; uv <= UVertexAttribute.TexCoord7; uv++)
                CopyVertexChannel(mesh, newMesh, remap, used, vcnt, uv);
            return remappedAttrs;
        }

        // 본 가중치 복사 (추출 메시도 원본 bones 배열을 공유)
        static void CopyBoneWeights(
            Mesh mesh, Mesh newMesh, bool[] used, int[] remap, int ncnt, int vcnt)
        {
            using var allWeights = mesh.GetAllBoneWeights();
            using var bonesPerVertex = mesh.GetBonesPerVertex();
            var newBpv = new NativeArray<byte>(ncnt, Allocator.Temp);
            var newWeights = new List<BoneWeight1>();
            int woff = 0;
            for (int v = 0; v < vcnt; v++)
            {
                int n = bonesPerVertex[v];
                if (used[v])
                {
                    newBpv[remap[v]] = (byte)n;
                    for (int k = 0; k < n; k++)
                        newWeights.Add(allWeights[woff + k]);
                }
                woff += n;
            }
            using (var weightsNA = new NativeArray<BoneWeight1>(newWeights.ToArray(), Allocator.Temp))
                newMesh.SetBoneWeights(newBpv, weightsNA);
            newBpv.Dispose();
            newMesh.bindposes = mesh.bindposes;
        }

        // 서브메시 구조/머티리얼 순서 유지
        static void SetRemappedTriangles(
            Mesh newMesh, List<int>[] extractTris, int[] remap, int subCount)
        {
            newMesh.subMeshCount = subCount;
            for (int s = 0; s < subCount; s++)
            {
                var tris = extractTris[s];
                var remapped = new int[tris.Count];
                for (int i = 0; i < tris.Count; i++)
                    remapped[i] = remap[tris[i]];
                newMesh.SetTriangles(remapped, s);
            }
        }

        // 블렌드셰이프 복사 — 스커트 버텍스에 델타가 있는 셰이프는
        // 프레임 구성을 유지한 채 전체 프레임을 이관한다
        static void CopyBlendShapes(
            Mesh mesh, Mesh newMesh, bool[] used, int[] remap, int vcnt, int ncnt)
        {
            for (int s = 0; s < mesh.blendShapeCount; s++)
            {
                TryCopyBlendShape(mesh, newMesh, s, used, remap, vcnt, ncnt);
            }
        }

        // 원본 메시 클론에서 스커트 삼각형 제거 (토폴로지 유지)
        static Mesh CloneRemainMesh(Mesh mesh, List<int>[] remainTris, MeshTopology[] topology)
        {
            var remainMesh = Object.Instantiate(mesh);
            remainMesh.name = mesh.name + "_body";
            for (int s = 0; s < mesh.subMeshCount; s++)
                remainMesh.SetIndices(remainTris[s], topology[s], s);
            return remainMesh;
        }

        // 추출 렌더러 생성 — 원본 설정/본/머티리얼 상속
        static ExtractedSkirtMesh CreateExtractedRenderer(
            SkinnedMeshRenderer source, Mesh mesh, Mesh newMesh, Mesh remainMesh)
        {
            var go = new GameObject(CharacterPhysicsSetup.ClothNamePrefix + "SkirtMesh_" + source.name);
            go.transform.SetParent(source.transform.parent, false);
            go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = newMesh;
            smr.bones = source.bones;
            smr.rootBone = source.rootBone;
            smr.sharedMaterials = source.sharedMaterials;
            smr.localBounds = mesh.bounds;
            smr.updateWhenOffscreen = source.updateWhenOffscreen;
            smr.shadowCastingMode = source.shadowCastingMode;
            smr.receiveShadows = source.receiveShadows;

            var marker = go.AddComponent<ExtractedSkirtMesh>();
            marker.sourceRenderer = source;
            marker.originalMesh = mesh;
            source.sharedMesh = remainMesh;
            return marker;
        }

        static void TryCopyBlendShape(
            Mesh mesh, Mesh newMesh, int s, bool[] used, int[] remap, int vcnt, int ncnt)
        {
            (Vector3[] fdv, Vector3[] fdn, Vector3[] fdt, bool hasDelta) =
                ReadShapeFrames(mesh, s, used, vcnt);
            if (!hasDelta)
                return;
            int frames = fdv.Length;
            string shapeName = mesh.GetBlendShapeName(s);
            for (int f = 0; f < frames; f++)
            {
                var ndv = new Vector3[ncnt];
                var ndn = new Vector3[ncnt];
                var ndt = new Vector3[ncnt];
                for (int v = 0; v < vcnt; v++)
                    if (used[v])
                    {
                        int nv = remap[v];
                        ndv[nv] = fdv[f][v]; ndn[nv] = fdn[f][v]; ndt[nv] = fdt[f][v];
                    }
                newMesh.AddBlendShapeFrame(
                    shapeName, mesh.GetBlendShapeFrameWeight(s, f), ndv, ndn, ndt);
            }
        }

        static (Vector3[] fdv, Vector3[] fdn, Vector3[] fdt, bool hasDelta) ReadShapeFrames(
            Mesh mesh, int s, bool[] used, int vcnt)
        {
            int frames = mesh.GetBlendShapeFrameCount(s);
            var fdv = new Vector3[frames][];
            var fdn = new Vector3[frames][];
            var fdt = new Vector3[frames][];
            bool hasDelta = false;
            for (int f = 0; f < frames; f++)
            {
                fdv[f] = new Vector3[vcnt];
                fdn[f] = new Vector3[vcnt];
                fdt[f] = new Vector3[vcnt];
                mesh.GetBlendShapeFrameVertices(s, f, fdv[f], fdn[f], fdt[f]);
                if (!hasDelta)
                    for (int v = 0; v < vcnt; v++)
                        if (used[v] && fdv[f][v] != Vector3.zero) { hasDelta = true; break; }
            }
            return (fdv, fdn, fdt, hasDelta);
        }

        static void CopyVertexChannel(
            Mesh src, Mesh dst, int[] remap, bool[] used, int vcnt, UVertexAttribute attr)
        {
            if (!src.HasVertexAttribute(attr))
                return;
            switch (attr)
            {
                case UVertexAttribute.Normal:
                    var n = src.normals;
                    var dn = new Vector3[dst.vertexCount];
                    for (int v = 0; v < vcnt; v++) if (used[v]) dn[remap[v]] = n[v];
                    dst.normals = dn;
                    break;
                case UVertexAttribute.Tangent:
                    var t = src.tangents;
                    var dt = new Vector4[dst.vertexCount];
                    for (int v = 0; v < vcnt; v++) if (used[v]) dt[remap[v]] = t[v];
                    dst.tangents = dt;
                    break;
                case UVertexAttribute.Color:
                    var c = src.colors32;
                    var dc = new Color32[dst.vertexCount];
                    for (int v = 0; v < vcnt; v++) if (used[v]) dc[remap[v]] = c[v];
                    dst.colors32 = dc;
                    break;
                default:
                    CopyUvChannel(src, dst, remap, used, vcnt,
                        attr - UVertexAttribute.TexCoord0);
                    break;
            }
        }

        /// <summary>
        /// 추출로 생성된 렌더러를 제거하고 원본 메시를 복원한다.
        /// </summary>
        // TexCoord0~7 — 차원 수 유지
        static void CopyUvChannel(
            Mesh src, Mesh dst, int[] remap, bool[] used, int vcnt, int channel)
        {
            var uv = new List<Vector4>(vcnt);
            src.GetUVs(channel, uv);
            var duv = new Vector4[dst.vertexCount];
            for (int v = 0; v < vcnt; v++) if (used[v]) duv[remap[v]] = uv[v];
            dst.SetUVs(channel, new List<Vector4>(duv));
        }

        public static void CleanupExtractions(Transform root)
        {
            if (root == null)
                return;
            foreach (var m in root.GetComponentsInChildren<ExtractedSkirtMesh>(true))
            {
                if (m.sourceRenderer != null && m.originalMesh != null)
                {
                    var remain = m.sourceRenderer.sharedMesh;
                    m.sourceRenderer.sharedMesh = m.originalMesh;
                    if (remain != null && remain != m.originalMesh)
                        DestroyObject(remain);
                }
                var smr = m.GetComponent<SkinnedMeshRenderer>();
                if (smr != null && smr.sharedMesh != null)
                    DestroyObject(smr.sharedMesh);
                DestroyObject(m.gameObject);
            }
        }

        static void DestroyObject(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
