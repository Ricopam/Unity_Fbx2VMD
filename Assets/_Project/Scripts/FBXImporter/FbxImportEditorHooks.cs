using System;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 에디터 전용 FBX 임포트 설정 구현을 런타임 컨트롤러가 호출하기 위한 슬롯 모음.
    /// 런타임 어셈블리는 에디터 구현 타입을 참조할 수 없으므로 델리게이트로 우회하고,
    /// 스탠드얼론에서는 슬롯이 비어 무동작한다.
    /// </summary>
    internal static class FbxImportEditorHooks
    {
        // 에디터에서만 ModelImporter 설정과 본 매핑을 적용함
        internal static Action<string, string> ConfigureEditorImportSettings;
    }
}
