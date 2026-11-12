using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 레이트 비주얼 그라운딩 보정 위임을 담당하는 partial 분할.
    public partial class PoseSpaceRetargeter
    {
        private void EnsureLateVisualGroundingCorrection()
        {
            if (!enableLateVisualGroundingCorrection)
            {
                return;
            }

            PoseSpaceLateVisualGroundingCorrection correction = GetComponent<PoseSpaceLateVisualGroundingCorrection>();
            if (correction == null)
            {
                correction = gameObject.AddComponent<PoseSpaceLateVisualGroundingCorrection>();
            }

            correction.Initialize(this);
        }

        public void ApplyLateVisualGroundingCorrection()
        {
            _grounding.ApplyLateVisualCorrection(this);
        }
    }
}
