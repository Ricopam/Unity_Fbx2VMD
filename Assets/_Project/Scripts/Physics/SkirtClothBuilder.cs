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
    /// <summary>
    /// 런타임 추출로 생성된 스커트 전용 렌더러의 마커.
    /// 원본 렌더러와 원본 메시 참조를 들고 있어 정리 시 복원할 수 있다.
    /// </summary>
    public class ExtractedSkirtMesh : MonoBehaviour
    {
        public SkinnedMeshRenderer sourceRenderer;
        public Mesh originalMesh;
    }

    /// <summary>
    /// 스커트 전용 튜닝값. 모든 기본값이 "템플릿 그대로"를 의미한다.
    /// MeshCloth·BoneClothLoop 경로가 공유한다.
    /// </summary>
    [System.Serializable]
    public struct SkirtTuning
    {
        [Header("깊이 커브")]
        [Range(0f, 2f)]
        [Tooltip("복원 강성 커브의 끝단(깊이 1.0) 배율. 작으면 스커트 끝이 더 자유롭게 회전해 실루엣이 산다")]
        public float tipStiffnessScale;

        [Header("포즈/관성")]
        [Range(0f, 1f)]
        [Tooltip("애니메이션 포즈 반영률. VMD처럼 포즈가 확정된 클립은 0.5 전후가 안정적")]
        public float animationPoseRatio;

        [Tooltip("깊이 관성을 수동 설정 (기본 템플릿 0.7 — 허리 근처 흔들림 억제)")]
        public bool overrideDepthInertia;

        [Range(0f, 1f)]
        public float depthInertia;

        [Header("캐릭터 프리셋")]
        [Tooltip("캐릭터 전용 프리셋 키. 지정하면 Resources/PhysicsPresets/Character/" +
                 "{키}_{클로스명|프리셋명}.json을 공식 프리셋보다 우선 로드")]
        public string characterPresetKey;

        public static SkirtTuning Default => new SkirtTuning
        {
            tipStiffnessScale = 1f, animationPoseRatio = 0f,
            overrideDepthInertia = false, depthInertia = 0.7f, characterPresetKey = null
        };
    }

    /// <summary>
    /// 스커트 본 체인과 스키닝 가중치로 MeshCloth를 자동 생성한다.
    /// MC2 공식 런타임 경로(ClothSerializeData2.vertexAttributeList)를 사용해
    /// 수동 버텍스 페인팅 없이 고정/이동 속성을 결정한다.
    /// - 스커트 체인 루트(fixedChainDepth 이하)에 스키닝된 버텍스 → Fixed (허리 고정부)
    /// - 더 깊은 스커트 본에 스키닝된 버텍스 → Move
    /// - 스커트 가중치 합이 임계 미만 → Invalid (본래 본 스키닝 유지, 프록시 메시에서 제외)
    /// MC2 렌더러 한도(65535 버텍스)를 넘는 메시는 스커트 삼각형만 전용
    /// 렌더러로 런타임 추출해 등록한다(ExtractSkirtRenderer).
    /// 파라미터는 공식 MC2_Preset_Skirt / 런타임 구축 예제 값을 따른다.
    /// </summary>
    public static partial class SkirtClothBuilder
    {
        public static readonly Regex SkirtBoneRegex =
            new Regex(@"skirt|スカート", RegexOptions.IgnoreCase);

        /// <summary>MC2 RenderSetupData의 렌더러당 최대 버텍스 수</summary>
        public const int MaxRendererVertices = 65535;

        public struct Result
        {
            public MagicaCloth cloth;
            public int skirtBoneCount;
            public int fixedVertexCount;
            public int moveVertexCount;
            public int rendererCount;
            public List<ExtractedSkirtMesh> extractedMeshes;
            public List<string> warnings;
        }

        /// <summary>
        /// 루트 하위에서 스커트 본을 수집하고 각 본의 체인 깊이를 계산한다.
        /// 깊이 0 = 스커트 체인의 루트(부모가 비스커트 본, 보통 허리).
        /// 연속 스커트 부모 수를 깊이로 사용 — 명명 숫자 규칙에 의존하지 않는다.
        /// </summary>
        public static Dictionary<Transform, int> CollectSkirtBoneDepths(Transform root)
        {
            var depths = new Dictionary<Transform, int>();
            if (root == null)
                return depths;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!SkirtBoneRegex.IsMatch(t.name) || t.name.StartsWith("MC2"))
                    continue;
                int depth = 0;
                for (var p = t.parent; p != null && SkirtBoneRegex.IsMatch(p.name); p = p.parent)
                    depth++;
                depths[t] = depth;
            }
            return depths;
        }

        /// <summary>
        /// 렌더러 메시의 버텍스별 속성을 계산한다.
        /// 스커트 본 가중치 합이 threshold 이상인 버텍스만 클로스 대상 —
        /// 그 중 주도 본의 체인 깊이가 fixedChainDepth 이하면 Fixed, 아니면 Move.
        /// 반환 배열 길이는 항상 메시 버텍스 수와 동일 (MC2 계약).
        /// </summary>
        public static VertexAttribute[] BuildVertexAttributes(
            SkinnedMeshRenderer smr,
            Dictionary<Transform, int> skirtBoneDepths,
            float skirtWeightThreshold,
            int fixedChainDepth,
            out int fixedCount,
            out int moveCount)
        {
            fixedCount = 0;
            moveCount = 0;
            var mesh = smr.sharedMesh;
            int vcnt = mesh.vertexCount;
            var attrs = new VertexAttribute[vcnt]; // 기본값 = Invalid (Flag 없음)

            var bones = smr.bones;
            if (bones == null || bones.Length == 0)
                return attrs;

            // 본 인덱스 → 스커트 깊이 (비스커트 본은 사전 부재)
            var boneDepthByIndex = BuildBoneDepthIndex(bones, skirtBoneDepths);
            if (boneDepthByIndex.Count == 0)
                return attrs;

            using var bonesPerVertex = mesh.GetBonesPerVertex();
            using var allWeights = mesh.GetAllBoneWeights();
            if (bonesPerVertex.Length != vcnt)
                return attrs;

            int wi = 0;
            for (int v = 0; v < vcnt; v++)
            {
                (float skirtWeight, int bestDepth) = MeasureSkirtInfluence(
                    bonesPerVertex[v], allWeights, ref wi, boneDepthByIndex);
                if (skirtWeight >= skirtWeightThreshold)
                {
                    attrs[v] = bestDepth <= fixedChainDepth
                        ? VertexAttribute.Fixed : VertexAttribute.Move;
                    if (bestDepth <= fixedChainDepth) fixedCount++;
                    else moveCount++;
                }
                // 임계 미만 → Invalid 유지 (스커트 아님 — 본래 스키닝 유지)
            }
            return attrs;
        }

        static (float skirtWeight, int bestDepth) MeasureSkirtInfluence(
            int boneCount, NativeArray<BoneWeight1> allWeights, ref int wi,
            Dictionary<int, int> boneDepthByIndex)
        {
            float skirtWeight = 0f;
            int bestDepth = int.MaxValue;
            float bestW = -1f;
            for (int k = 0; k < boneCount; k++, wi++)
            {
                var bw = allWeights[wi];
                if (!boneDepthByIndex.TryGetValue(bw.boneIndex, out int depth))
                    continue;
                skirtWeight += bw.weight;
                if (bw.weight > bestW)
                {
                    bestW = bw.weight;
                    bestDepth = depth;
                }
            }
            return (skirtWeight, bestDepth);
        }

        static Dictionary<int, int> BuildBoneDepthIndex(
            Transform[] bones, Dictionary<Transform, int> skirtBoneDepths)
        {
            var map = new Dictionary<int, int>(bones.Length);
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null && skirtBoneDepths.TryGetValue(bones[i], out int d))
                    map[i] = d;
            }
            return map;
        }

    }
}
