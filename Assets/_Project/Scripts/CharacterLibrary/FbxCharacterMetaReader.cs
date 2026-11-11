using System.IO;

namespace Fbx2Vmd.CharacterLibrary
{
    /// <summary>
    /// 사람형 FBX 캐릭터의 메타 읽기. FBX에는 VRM 같은 내장 메타가 없으므로
    /// 표시 이름만 파일명에서 유도하고 썸네일은 비워 둔다.
    /// PMX도 동일 방식으로 읽고 specVersion만 구분한다.
    /// Humanoid 검증은 등록이 아닌 로드 단계에서 수행한다.
    /// </summary>
    public static class FbxCharacterMetaReader
    {
        public static VrmCharacterMetaResult Read(string path, string specVersion = "fbx")
        {
            var result = new VrmCharacterMetaResult { success = true, specVersion = specVersion };
            result.meta.title = Path.GetFileNameWithoutExtension(path ?? string.Empty);
            return result;
        }
    }
}
