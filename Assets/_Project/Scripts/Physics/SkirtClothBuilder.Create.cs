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
        /// 스커트 MeshCloth를 생성한다. 스커트 버텍스가 있는 렌더러만 등록한다.
        /// </summary>
        public static Result Create(
            Transform parent,
            SkinnedMeshRenderer[] renderers,
            Dictionary<Transform, int> skirtBoneDepths,
            List<ColliderComponent> colliders,
            float torso,
            float skirtWeightThreshold,
            int fixedChainDepth,
            float reductionScale = 1.0f,
            bool useBackstop = false,
            float backstopDistance = 0.005f,
            bool usePresetBaseline = true,
            SkirtTuning? tuning = null)
        {
            var result = new Result
            {
                warnings = new List<string>(),
                extractedMeshes = new List<ExtractedSkirtMesh>()
            };
            result.skirtBoneCount = skirtBoneDepths.Count;

            var inputs = new List<(SkinnedMeshRenderer smr, VertexAttribute[] attrs)>();
            CollectInputs(renderers, skirtBoneDepths, skirtWeightThreshold, fixedChainDepth,
                result, inputs);
            if (inputs.Count == 0)
                return result; // 스커트 버텍스 없음 — MeshCloth 불가
            result.rendererCount = inputs.Count;

            result.cloth = BuildCloth(parent, colliders, torso, reductionScale,
                useBackstop, backstopDistance, usePresetBaseline, tuning, inputs);
            if (result.fixedVertexCount == 0)
                result.warnings.Add("고정 버텍스 없음 — 스커트가 몸에서 떨어질 수 있음. fixedChainDepth 상향 검토");
            return result;
        }

        // 렌더러별 버텍스 속성을 계산하고 한도 초과 메시는 스커트 삼각형 추출본으로 대체한다.
        static void CollectInputs(
            SkinnedMeshRenderer[] renderers,
            Dictionary<Transform, int> skirtBoneDepths,
            float skirtWeightThreshold, int fixedChainDepth,
            Result result,
            List<(SkinnedMeshRenderer smr, VertexAttribute[] attrs)> inputs)
        {
            foreach (var smr in renderers)
            {
                if (!IsUsableSkirtRenderer(smr, result))
                    continue;
                var attrs = BuildVertexAttributes(
                    smr, skirtBoneDepths, skirtWeightThreshold, fixedChainDepth,
                    out int fc, out int mc);
                if (fc + mc == 0)
                {
                    result.warnings.Add($"{smr.name}: 스커트 본에 스키닝된 버텍스 0 — 스커트 가중치 미달(임계 {skirtWeightThreshold:F2})");
                    continue;
                }
                // MC2 렌더러 한도(65535) 초과 메시는 스커트 삼각형만 추출해 등록
                if (smr.sharedMesh.vertexCount > MaxRendererVertices &&
                    !TryCollectExtractedInput(smr, attrs, result, inputs))
                {
                    continue;
                }
                else if (smr.sharedMesh.vertexCount <= MaxRendererVertices)
                {
                    inputs.Add((smr, attrs));
                }
                result.fixedVertexCount += fc;
                result.moveVertexCount += mc;
            }
        }

        static bool IsUsableSkirtRenderer(SkinnedMeshRenderer smr, Result result)
        {
            if (smr == null)
                return false;
            if (smr.sharedMesh == null)
            {
                result.warnings.Add($"{smr.name}: sharedMesh 없음 — 메시 소스(FBX/PMX) 미포함 에셋, MeshCloth 불가");
                return false;
            }
            if (!smr.sharedMesh.isReadable)
            {
                result.warnings.Add($"{smr.name}: 메시 Read/Write 비활성 — 임포터 설정에서 Read/Write Enabled 필요");
                return false;
            }
            return true;
        }

        static bool TryCollectExtractedInput(
            SkinnedMeshRenderer smr, VertexAttribute[] attrs,
            Result result,
            List<(SkinnedMeshRenderer smr, VertexAttribute[] attrs)> inputs)
        {
            var extracted = ExtractSkirtRenderer(smr, attrs, out var remapped);
            if (extracted == null)
            {
                result.warnings.Add($"{smr.name}: 스커트 삼각형 추출 실패");
                return false;
            }
            var newSmr = extracted.GetComponent<SkinnedMeshRenderer>();
            if (newSmr.sharedMesh.vertexCount > MaxRendererVertices)
            {
                // 추출본도 한도 초과 — MC2 등록 불가이므로 추출을 되돌린다
                result.warnings.Add($"{smr.name}: 추출 스커트 메시도 {MaxRendererVertices}버텍스 초과 — 메시 분할 필요");
                SkirtClothBuilder.CleanupExtractions(extracted.transform);
                return false;
            }
            result.extractedMeshes.Add(extracted);
            inputs.Add((newSmr, remapped));
            return true;
        }

        static MagicaCloth BuildCloth(
            Transform parent, List<ColliderComponent> colliders,
            float torso, float reductionScale, bool useBackstop,
            float backstopDistance, bool usePresetBaseline, SkirtTuning? tuning,
            List<(SkinnedMeshRenderer smr, VertexAttribute[] attrs)> inputs)
        {
            var go = new GameObject(CharacterPhysicsSetup.ClothNamePrefix + "Skirt");
            go.transform.SetParent(parent, false);
            var cloth = go.AddComponent<MagicaCloth>();
            var sdata = cloth.SerializeData;
            sdata.clothType = ClothProcess.ClothType.MeshCloth;
            // 공식 Skirt 프리셋을 베이스라인으로 — 아래에서 관리하는 필드는 덮어쓴다.
            if (usePresetBaseline)
                ClothPresetLibrary.TryImport(sdata, ClothPresetLibrary.Skirt,
                    tuning?.characterPresetKey, go.name);
            foreach (var (smr, _) in inputs)
                sdata.sourceRenderers.Add(smr);

            var sdata2 = cloth.GetSerializeData2();
            sdata2.vertexAttributeList.Clear();
            foreach (var (_, attrs) in inputs)
                sdata2.vertexAttributeList.Add(attrs);

            // 프록시 메시 리덕션 — 체형 기준 ~3% (공식 예제 0.02m 내외)
            if (torso > 0f)
            {
                sdata.reductionSetting.simpleDistance = torso * 0.035f * reductionScale;
                sdata.reductionSetting.shapeDistance = torso * 0.04f * reductionScale;
            }

            ApplySimParams(sdata, colliders, useBackstop, backstopDistance,
                tuning ?? SkirtTuning.Default);
            return cloth;
        }

        /// <summary>
        /// 스커트 공통 시뮬레이션 파라미터 — MeshCloth·BoneCloth 루프 경로가 공유한다.
        /// 공식 Skirt 프리셋을 베이스라인으로 두고 프로젝트 관리 필드를 덮어쓴다.
        /// </summary>
        internal static void ApplySimParams(
            ClothSerializeData sdata,
            List<ColliderComponent> colliders,
            bool useBackstop,
            float backstopDistance,
            in SkirtTuning tuning)
        {
            // 공식 MeshCloth_Skirt 프리셋 + 런타임 예제 파라미터
            sdata.updateMode = ClothUpdateMode.AnimatorLinkage;
            sdata.normalAxis = ClothNormalAxis.Up;
            sdata.gravity = 5.0f;
            sdata.damping.SetValue(0.1f);
            sdata.radius.SetValue(0.02f);
            sdata.stablizationTimeAfterReset = 0.1f;

            var inertia = sdata.inertiaConstraint;
            inertia.movementInertiaSmoothing = 0.4f;
            inertia.movementSpeedLimit.SetValue(true, 5.0f);
            inertia.rotationSpeedLimit.SetValue(true, 720.0f);
            inertia.localMovementSpeedLimit.SetValue(true, 3.0f);
            inertia.localRotationSpeedLimit.SetValue(true, 360.0f);
            inertia.depthInertia = 0.7f;
            inertia.centrifualAcceleration = 0.1f;
            inertia.particleSpeedLimit.SetValue(true, 4.0f);
            // 헤어와 동일하게 Keep — 텔레포트 오감지보다 폭주 방지가 우선
            inertia.teleportMode = InertiaConstraint.TeleportMode.Keep;
            inertia.teleportDistance = 0.5f;
            inertia.teleportRotation = 90.0f;

            sdata.tetherConstraint.distanceCompression = 0.3f;
            sdata.distanceConstraint.stiffness.SetValue(1.0f, 1.0f, 0.5f, true);
            sdata.triangleBendingConstraint.stiffness = 1.0f; // MeshCloth 사실상 필수

            ApplyAngleConstraints(sdata, tuning);
            ApplyMotionParams(sdata, useBackstop, backstopDistance);
            ApplyColliderAndSpringParams(sdata, colliders);
        }

        static void ApplyAngleConstraints(ClothSerializeData sdata, in SkirtTuning tuning)
        {
            sdata.angleRestorationConstraint.useAngleRestoration = true;
            sdata.angleRestorationConstraint.stiffness.SetValue(0.2f, 1.0f, 0.5f, true);
            HairPhysicsParameters.ScaleCurveTip(
                sdata.angleRestorationConstraint.stiffness, tuning.tipStiffnessScale);
            sdata.angleRestorationConstraint.velocityAttenuation = 0.7f;
            sdata.animationPoseRatio = Mathf.Clamp01(tuning.animationPoseRatio);
            if (tuning.overrideDepthInertia)
                sdata.inertiaConstraint.depthInertia = Mathf.Clamp01(tuning.depthInertia);

            sdata.angleLimitConstraint.useAngleLimit = true;
            sdata.angleLimitConstraint.limitAngle.SetValue(60.0f, 0.0f, 1.0f, true);
            sdata.angleLimitConstraint.stiffness = 1.0f;
        }

        static void ApplyMotionParams(
            ClothSerializeData sdata, bool useBackstop, float backstopDistance)
        {
            sdata.motionConstraint.useMaxDistance = false;
            // 백스톱: 스킨 포즈 기준 법선 내측 진입을 제한해 다리 관통을 막는다.
            // 설치 버전(2.18.1)에 Penetration 제약이 없어 이것이 대체 수단.
            // 스커트 옆살이 다리 사이로 들어가는 정상 동작도 제한될 수 있어
            // 과하면 끄는 옵션으로 노출한다.
            sdata.motionConstraint.useBackstop = useBackstop;
            if (useBackstop)
            {
                sdata.motionConstraint.backstopRadius = 0.02f;
                sdata.motionConstraint.backstopDistance.SetValue(
                    Mathf.Max(0f, backstopDistance));
            }
            sdata.motionConstraint.stiffness = 1.0f;
        }

        static void ApplyColliderAndSpringParams(
            ClothSerializeData sdata, List<ColliderComponent> colliders)
        {
            sdata.colliderCollisionConstraint.mode = ColliderCollisionConstraint.Mode.Point;
            sdata.colliderCollisionConstraint.friction = 0.05f;
            sdata.colliderCollisionConstraint.colliderList.Clear();
            sdata.colliderCollisionConstraint.colliderList.AddRange(colliders);

            sdata.springConstraint.useSpring = true;
            sdata.springConstraint.springPower = 0.03f;
            sdata.springConstraint.limitDistance = 0.05f;
        }
    }
}
