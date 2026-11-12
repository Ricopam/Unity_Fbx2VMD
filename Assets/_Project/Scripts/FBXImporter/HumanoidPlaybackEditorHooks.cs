using System;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 에디터 전용 RootT 재생 보정이 런타임으로부터 호출받는 경계 계약.
    /// 런타임 어셈블리는 에디터 구현을 참조할 수 없으므로, 런타임이 계약을 선언하고
    /// 에디터가 [InitializeOnLoad] 시점에 팩토리를 할당한다 (RetargetingEditorReferenceHooks 패턴).
    /// 스탠드얼론에서는 팩토리가 null이며 호출부는 샘플러 없음과 동일하게 무동작한다.
    /// </summary>
    internal interface IEditorRootTranslationSampler
    {
        bool IsAvailable { get; }

        void Initialize(AnimationClip clip);

        bool TryEvaluateClipSpaceOffset(float timeSeconds, out Vector3 clipSpaceOffset);

        void Clear();
    }

    internal static class HumanoidPlaybackEditorHooks
    {
        internal static Func<IEditorRootTranslationSampler> CreateRootTranslationSampler;
    }
}
