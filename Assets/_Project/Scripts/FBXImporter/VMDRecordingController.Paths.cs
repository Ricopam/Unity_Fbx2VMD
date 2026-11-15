using System;
using System.IO;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 프로젝트 상대 경로 변환 전담 분할.
    public partial class VMDRecordingController
    {
        private static string MakeProjectRelativePath(string path)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            return MakeProjectRelativePath(path, projectRoot);
        }

        private static string MakeProjectRelativePath(string path, string projectRoot)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            string fullPath = Path.GetFullPath(path);
            string fullRoot = Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            bool isProjectPath = string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase)
                || (fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)
                    && fullPath.Length > fullRoot.Length
                    && (fullPath[fullRoot.Length] == Path.DirectorySeparatorChar
                        || fullPath[fullRoot.Length] == Path.AltDirectorySeparatorChar));

            if (!isProjectPath)
            {
                return path.Replace("\\", "/");
            }

            return fullPath.Substring(fullRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace("\\", "/");
        }
    }
}
