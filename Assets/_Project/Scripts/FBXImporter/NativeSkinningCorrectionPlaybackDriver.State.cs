using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class NativeSkinningCorrectionPlaybackDriver
    {

        private void DisposePresenters()
        {
            foreach (NativeSkinningCorrectionPresenter presenter in _presenters)
            {
                presenter.Dispose();
            }
            _presenters.Clear();
        }

        private void OnDestroy()
        {
            Release();
        }

        private static void DestroyOwnedObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                DestroyImmediate(target);
                return;
            }
#endif
            Destroy(target);
        }


        private long _seekTicks;
        private long _bakeTicks;
        private long _boneSampleTicks;
        private long _solveTicks;
        private int _solverIdleTicks;
        private int _solverBackloggedTicks;
        // 디스크 캐시 — 식별자가 주입되지 않으면(테스트 등) 비활성.
        private string _cacheMotionName = string.Empty;
        private byte[] _cacheKeyHash;
        private bool _resultFromCache;
        // 빌드에서는 에셋 식별자가 없어 소스 FBX 경로가 캐시 키 입력이 된다.
        private string _runtimeCacheSourcePath = string.Empty;

    }
}
