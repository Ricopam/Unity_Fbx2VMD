using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.Rendering.Universal;

namespace Fbx2Vmd.Settings
{
    [DisallowMultipleComponent]
    public sealed partial class GraphicSetting : MonoBehaviour
    {
#if UNITY_EDITOR
        private const string DefaultPostProcessResourcesPath =
            "Packages/com.unity.postprocessing/PostProcessing/PostProcessResources.asset";
#endif

        private void Reset()
        {
            targetCamera = Camera.main;
            targetRenderPipelineAsset = ResolveCurrentUniversalRenderPipelineAsset();
            builtInPostProcessResources = ResolveDefaultPostProcessResources();
        }

        private void Awake()
        {
            if (applyOnAwake)
            {
                ApplyNow();
            }
        }

        private void OnValidate()
        {
            msaaSampleCount =
                GraphicRenderPipelineSettingsApplier.NormalizeMsaaSampleCount(msaaSampleCount);
            renderScale = Mathf.Clamp(renderScale, 0.1f, 2.0f);
            if (builtInPostProcessResources == null)
            {
                builtInPostProcessResources = ResolveDefaultPostProcessResources();
            }

            if (materialShaderProfile == null)
            {
                materialShaderProfile = new GraphicMaterialShaderProfile();
            }

            ApplySimplePresetValues();

            if (applyOnValidate && !Application.isPlaying)
            {
                ApplyNow();
            }
        }

        public void ApplyNow()
        {
            UniversalRenderPipelineAsset pipelineAsset = ResolveTargetPipelineAsset();
            ApplySimplePresetValues(pipelineAsset != null);

            Camera camera = ResolveTargetCamera();
            if (camera != null)
            {
                GraphicAntiAliasingPlan plan = new GraphicAntiAliasingPlan(
                    antiAliasing,
                    smaaQuality,
                    enableCameraPostProcessing,
                    enableCameraMsaa,
                    msaaSampleCount);
                GraphicCameraSettingsApplier.Apply(
                    camera,
                    pipelineAsset != null,
                    plan,
                    ResolveBuiltInPostProcessResources);
            }

            GraphicRenderPipelineSettingsApplier.Apply(
                pipelineAsset,
                renderScale,
                enableCameraMsaa,
                msaaSampleCount);
        }

        public GraphicTextureImportPlan CreateTextureImportPlan()
        {
            return GraphicTextureImportPlanResolver.Resolve(
                textureResolution,
                textureImportProfile);
        }

        public GraphicMaterialShaderPlan CreateMaterialShaderPlan()
        {
            return GraphicMaterialShaderPlanResolver.Resolve(
                modelEdgeAndAlpha,
                materialShaderProfile);
        }

        public GraphicMaterialShaderApplyResult ApplyMaterialShaderSettings()
        {
            if (materialShaderProfile == null)
            {
                materialShaderProfile = new GraphicMaterialShaderProfile();
            }

            GraphicMaterialShaderApplyResult result = GraphicMaterialShaderController.Apply(
                CreateMaterialShaderPlan(),
                GraphicMaterialShaderTargetCollector.Enumerate(
                    materialShaderTargets,
                    materialSourceRoots));
            Debug.Log($"GraphicSetting 모델 머티리얼 설정 적용: {result}");
            return result;
        }

        private void ApplySimplePresetValues()
        {
            ApplySimplePresetValues(ResolveTargetPipelineAsset() != null);
        }

        private void ApplySimplePresetValues(bool hasRenderScaleTarget)
        {
            ApplyAntiAliasingPreset();
            renderScale = GraphicRenderScalePresetResolver.Resolve(
                renderSharpness,
                hasRenderScaleTarget,
                renderScale);
        }

        private void ApplyAntiAliasingPreset()
        {
            GraphicAntiAliasingPlan customPlan = new GraphicAntiAliasingPlan(
                antiAliasing,
                smaaQuality,
                enableCameraPostProcessing,
                enableCameraMsaa,
                msaaSampleCount);
            GraphicAntiAliasingPlan plan = GraphicAntiAliasingPresetResolver.Resolve(
                antiAliasingPreset,
                customPlan);

            antiAliasing = plan.AntiAliasing;
            smaaQuality = plan.SmaaQuality;
            enableCameraPostProcessing = plan.EnableCameraPostProcessing;
            enableCameraMsaa = plan.EnableCameraMsaa;
            msaaSampleCount = plan.MsaaSampleCount;
        }

        private PostProcessResources ResolveBuiltInPostProcessResources()
        {
            return builtInPostProcessResources != null
                ? builtInPostProcessResources
                : ResolveDefaultPostProcessResources();
        }

        private Camera ResolveTargetCamera()
        {
            if (targetCamera != null)
            {
                return targetCamera;
            }

            targetCamera = Camera.main;
            return targetCamera;
        }

        private UniversalRenderPipelineAsset ResolveTargetPipelineAsset()
        {
            if (targetRenderPipelineAsset != null)
            {
                return targetRenderPipelineAsset;
            }

            targetRenderPipelineAsset = ResolveCurrentUniversalRenderPipelineAsset();
            return targetRenderPipelineAsset;
        }

        private static UniversalRenderPipelineAsset ResolveCurrentUniversalRenderPipelineAsset()
        {
            if (QualitySettings.renderPipeline is UniversalRenderPipelineAsset qualityAsset)
            {
                return qualityAsset;
            }

            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset graphicsAsset)
            {
                return graphicsAsset;
            }

            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset defaultAsset)
            {
                return defaultAsset;
            }

            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset currentAsset)
            {
                return currentAsset;
            }

            return null;
        }

        private static PostProcessResources ResolveDefaultPostProcessResources()
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<PostProcessResources>(DefaultPostProcessResourcesPath);
#else
            return Resources.Load<PostProcessResources>("PostProcessResources");
#endif
        }
    }
}
