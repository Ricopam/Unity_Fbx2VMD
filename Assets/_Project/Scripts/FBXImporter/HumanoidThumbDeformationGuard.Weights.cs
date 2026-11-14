using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidThumbDeformationGuard
    {
        private float GetEffectiveThumbProjectionGuardWeight(bool isRightThumb)
        {
            return ShouldSuppressPoseShapingWithManualThumbReference(isRightThumb) ? 0f : thumbProjectionGuardWeight;
        }

        private float EffectiveThumbProjectionGuardWeight
        {
            get
            {
                return Mathf.Max(
                    GetEffectiveThumbProjectionGuardWeight(false),
                    GetEffectiveThumbProjectionGuardWeight(true));
            }
        }

        private float EffectiveLeftThumbProjectionGuardWeight => GetEffectiveThumbProjectionGuardWeight(false);

        private float EffectiveRightThumbProjectionGuardWeight => GetEffectiveThumbProjectionGuardWeight(true);

        private float GetEffectiveThumbIndexSpreadGuardWeight(bool isRightThumb)
        {
            return ShouldSuppressPoseShapingWithManualThumbReference(isRightThumb) ? 0f : thumbIndexSpreadGuardWeight;
        }

        private float EffectiveThumbIndexSpreadGuardWeight
        {
            get
            {
                return Mathf.Max(
                    GetEffectiveThumbIndexSpreadGuardWeight(false),
                    GetEffectiveThumbIndexSpreadGuardWeight(true));
            }
        }

        private float EffectiveLeftThumbIndexSpreadGuardWeight => GetEffectiveThumbIndexSpreadGuardWeight(false);

        private float EffectiveRightThumbIndexSpreadGuardWeight => GetEffectiveThumbIndexSpreadGuardWeight(true);

        private float GetEffectiveThumbSegmentStraightenWeight(bool isRightThumb)
        {
            return ShouldSuppressPoseShapingWithManualThumbReference(isRightThumb) ? 0f : thumbSegmentStraightenWeight;
        }

        private float EffectiveThumbSegmentStraightenWeight
        {
            get
            {
                return Mathf.Max(
                    GetEffectiveThumbSegmentStraightenWeight(false),
                    GetEffectiveThumbSegmentStraightenWeight(true));
            }
        }

        private float EffectiveLeftThumbSegmentStraightenWeight => GetEffectiveThumbSegmentStraightenWeight(false);

        private float EffectiveRightThumbSegmentStraightenWeight => GetEffectiveThumbSegmentStraightenWeight(true);

        private int LastLeftThumbProjectionCorrectionApplyCount { get; set; }

        private int LastRightThumbProjectionCorrectionApplyCount { get; set; }

        private int LastLeftThumbProjectionCorrectionPreserveCount { get; set; }

        private int LastRightThumbProjectionCorrectionPreserveCount { get; set; }

        private int LastLeftThumbSegmentStraightenApplyCount { get; set; }

        private int LastRightThumbSegmentStraightenApplyCount { get; set; }

        private int LastLeftThumbSegmentStraightenPreserveCount { get; set; }

        private int LastRightThumbSegmentStraightenPreserveCount { get; set; }
    }
}
