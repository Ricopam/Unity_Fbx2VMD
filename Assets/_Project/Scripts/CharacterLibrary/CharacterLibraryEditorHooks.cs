using System;

namespace Fbx2Vmd.CharacterLibrary
{
    /// <summary>
    /// 에디터 전용 캐릭터 임포트 보조 구현을 런타임 쪽에서 호출하기 위한 슬롯 모음.
    /// 런타임 어셈블리는 에디터 구현 타입을 참조할 수 없으므로 델리게이트로 우회하고,
    /// 스탠드얼론에서는 슬롯이 비어 무동작한다.
    /// </summary>
    internal static class CharacterLibraryEditorHooks
    {
        // 에디터에서만 ModelImporter Humanoid 아바타 설정을 수행함
        internal static Action<string> EnsureHumanoidAvatar;
    }
}
