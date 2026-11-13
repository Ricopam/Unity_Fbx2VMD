using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.Rendering.Universal;

namespace Fbx2Vmd.Settings
{
    /// <summary>
    /// 직렬화 설정 표면 — 본체에서 이관한 데이터 홀더 partial.
    /// </summary>
    public sealed partial class GraphicSetting
    {
        [Header("Targets")]
        [SerializeField] private Camera targetCamera;
        [SerializeField] private UniversalRenderPipelineAsset targetRenderPipelineAsset;
        [SerializeField] private PostProcessResources builtInPostProcessResources;

        [Header("Apply")]
        [SerializeField] private bool applyOnAwake = true;
        [SerializeField] private bool applyOnValidate;

        [Header("Simple Presets")]
        [SerializeField] private GraphicSettingQualityPreset textureResolution = GraphicSettingQualityPreset.Balanced;
        [SerializeField] private GraphicSettingQualityPreset antiAliasingPreset = GraphicSettingQualityPreset.Quality;
        [SerializeField] private GraphicSettingQualityPreset renderSharpness = GraphicSettingQualityPreset.Balanced;
        [SerializeField] private GraphicSettingQualityPreset modelEdgeAndAlpha = GraphicSettingQualityPreset.Balanced;

        [Header("Anti Aliasing")]
        [SerializeField] private GraphicAntiAliasingMode antiAliasing = GraphicAntiAliasingMode.SMAA;
        [SerializeField] private AntialiasingQuality smaaQuality = AntialiasingQuality.High;
        [SerializeField] private bool enableCameraPostProcessing = true;
        [SerializeField] private bool enableCameraMsaa = true;
        [SerializeField] private int msaaSampleCount = 8;
        [SerializeField, Range(0.1f, 2.0f)] private float renderScale = 1.0f;

        [Header("Editor GameView")]
        [SerializeField] private GraphicGameViewScaleMode gameViewScaleMode = GraphicGameViewScaleMode.OneX;

        [Header("Texture Import")]
        [SerializeField] private GraphicTextureImportProfile textureImportProfile = new GraphicTextureImportProfile();
        [SerializeField] private GameObject[] textureSourceRoots = Array.Empty<GameObject>();
        [SerializeField] private Texture2D[] textureImportTargets = Array.Empty<Texture2D>();
        [SerializeField] private string[] textureAssetFolders = Array.Empty<string>();

        [Header("Material Shader")]
        [SerializeField] private GraphicMaterialShaderProfile materialShaderProfile = new GraphicMaterialShaderProfile();
        [SerializeField] private GameObject[] materialSourceRoots = Array.Empty<GameObject>();
        [SerializeField] private Material[] materialShaderTargets = Array.Empty<Material>();
        [SerializeField] private string[] materialAssetFolders = Array.Empty<string>();

        public Camera TargetCamera => targetCamera;
        public UniversalRenderPipelineAsset TargetRenderPipelineAsset => targetRenderPipelineAsset;
        public GraphicTextureImportProfile TextureImportProfile => textureImportProfile;
        public GameObject[] TextureSourceRoots => textureSourceRoots;
        public Texture2D[] TextureImportTargets => textureImportTargets;
        public string[] TextureAssetFolders => textureAssetFolders;
        public GraphicMaterialShaderProfile MaterialShaderProfile => materialShaderProfile;
        public GameObject[] MaterialSourceRoots => materialSourceRoots;
        public Material[] MaterialShaderTargets => materialShaderTargets;
        public string[] MaterialAssetFolders => materialAssetFolders;
        public GraphicGameViewScaleMode GameViewScaleMode => gameViewScaleMode;

    }
}
