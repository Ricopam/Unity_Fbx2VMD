using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public partial class FBXConversionCoordinator
    {
        internal HumanoidArmSleeveAnchorGuard ConfigureArmSleeveAnchorGuard(
            GameObject targetObject,
            Animator targetAnimator)
        {
            if (targetObject == null)
            {
                return null;
            }

            HumanoidArmSleeveAnchorGuard sleeveAnchorGuard =
                targetObject.GetComponent<HumanoidArmSleeveAnchorGuard>();
            if (!_pipeline.enableYybArmSleeveAnchorCorrection)
            {
                if (sleeveAnchorGuard != null)
                {
                    sleeveAnchorGuard.DisableCorrection();
                    sleeveAnchorGuard.enabled = false;
                }

                return null;
            }

            if (sleeveAnchorGuard == null)
            {
                sleeveAnchorGuard = targetObject.AddComponent<HumanoidArmSleeveAnchorGuard>();
            }

            sleeveAnchorGuard.enableSleeveAnchor = true;
            sleeveAnchorGuard.enabled = true;
            bool configured = sleeveAnchorGuard.Configure(
                targetAnimator,
                _pipeline.YybArmSleeveAnchorInfluence,
                _pipeline.YybArmShoulderCapAnchorInfluence,
                _pipeline.YybArmSleeveAnchorMaxDegrees,
                _pipeline.logYybArmSleeveAnchorCorrection);

            if (!configured)
            {
                sleeveAnchorGuard.enabled = false;
                return null;
            }

            return sleeveAnchorGuard;
        }

        internal HumanoidArmVisualTwistGuard ConfigureArmVisualTwistGuard(
            GameObject targetObject,
            Animator targetAnimator)
        {
            if (targetObject == null)
            {
                return null;
            }

            HumanoidArmVisualTwistGuard visualTwistGuard =
                targetObject.GetComponent<HumanoidArmVisualTwistGuard>();
            if (!_pipeline.enableYybArmVisualTwistCorrection)
            {
                if (visualTwistGuard != null)
                {
                    visualTwistGuard.DisableCorrection();
                    visualTwistGuard.enabled = false;
                }

                return null;
            }

            if (visualTwistGuard == null)
            {
                visualTwistGuard = targetObject.AddComponent<HumanoidArmVisualTwistGuard>();
            }

            visualTwistGuard.enableVisualTwistGuard = true;
            visualTwistGuard.enabled = true;
            bool configured = visualTwistGuard.Configure(
                targetAnimator,
                _pipeline.YybArmVisualUpperArmInfluence,
                _pipeline.YybArmVisualForearmInfluence,
                _pipeline.YybArmVisualUpperArmMaxDegrees,
                _pipeline.YybArmVisualForearmMaxDegrees,
                _pipeline.logYybArmVisualTwistCorrection);

            if (!configured)
            {
                visualTwistGuard.enabled = false;
                return null;
            }

            return visualTwistGuard;
        }

    }
}
