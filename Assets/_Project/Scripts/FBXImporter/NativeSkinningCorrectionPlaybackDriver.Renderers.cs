using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class NativeSkinningCorrectionPlaybackDriver
    {
        private readonly Dictionary<SkinnedMeshRenderer, Mesh> _bakedMeshes =
            new Dictionary<SkinnedMeshRenderer, Mesh>();

        // 준비 시작 시점의 소스 메시 — 캡처 중 토폴로지 교체를 감지한다.
        private readonly Dictionary<SkinnedMeshRenderer, Mesh> _sessionMeshes =
            new Dictionary<SkinnedMeshRenderer, Mesh>();

        private Renderer[] _hiddenRenderers = Array.Empty<Renderer>();

        private bool[] _rendererEnabledStates = Array.Empty<bool>();

        private bool _hasCapturedRendererStates;

        private void CaptureAndHideRenderers()
        {
            _hiddenRenderers = _animator.GetComponentsInChildren<Renderer>(true);
            _rendererEnabledStates = _hiddenRenderers
                .Select(renderer => renderer.enabled)
                .ToArray();
            for (int index = 0; index < _hiddenRenderers.Length; index++)
            {
                _hiddenRenderers[index].enabled = false;
            }
            _hasCapturedRendererStates = true;
        }

        private void RestoreRendererStates()
        {
            if (!_hasCapturedRendererStates)
            {
                return;
            }
            for (int index = 0; index < _hiddenRenderers.Length; index++)
            {
                if (_hiddenRenderers[index] != null)
                {
                    _hiddenRenderers[index].enabled = _rendererEnabledStates[index];
                }
            }
            _hiddenRenderers = Array.Empty<Renderer>();
            _rendererEnabledStates = Array.Empty<bool>();
            _hasCapturedRendererStates = false;
        }

        private void DestroyBakedMeshes()
        {
            foreach (Mesh mesh in _bakedMeshes.Values)
            {
                DestroyOwnedObject(mesh);
            }
            _bakedMeshes.Clear();
            _sessionMeshes.Clear();
        }

    }
}
