using System;
using System.IO;

namespace Fbx2Vmd.CharacterLibrary
{
    /// <summary>
    /// 확장자에 따라 모델 메타 리더를 선택하는 진입점.
    /// 레지스트리의 기본 metaReader로 주입된다.
    /// </summary>
    public static class CharacterFileMetaReader
    {
        public static VrmCharacterMetaResult Read(string path)
        {
            string extension = Path.GetExtension(path ?? string.Empty);
            if (string.Equals(extension, ".fbx", StringComparison.OrdinalIgnoreCase))
            {
                return FbxCharacterMetaReader.Read(path);
            }
            if (string.Equals(extension, ".pmx", StringComparison.OrdinalIgnoreCase))
            {
                return FbxCharacterMetaReader.Read(path, "pmx");
            }
            return VrmCharacterMetaReader.Read(path);
        }
    }
}
