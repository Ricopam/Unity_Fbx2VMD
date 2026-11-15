using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using Assimp;

namespace Fbx2Vmd.FBXImporter
{
    public partial class AssimpFBXImporter
    {

        #region Private 필드
        // 노드 이름으로 Transform을 찾기 위한 맵 (본 할당용)
        private Dictionary<string, Transform> _nodeMap = new Dictionary<string, Transform>();
        
        private string _sourceDirectory = string.Empty;

        // 생성된 AnimationClip 저장 (외부 접근용)
        private AnimationClip[] _animationClips;
        #endregion

    }
}