using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 썸 근육(muscle) 값의 해부학적 클램프와 포즈셰이핑 억제 게이트를 담당하는 런타임 정책.
    /// 수동참조 보존 상태와의 상호작용만 판정하며 진단 상태는 갖지 않는다.
    /// </summary>
    internal static class ThumbMuscleGuardPolicy
    {
        internal static int ApplyAnatomicalGuard(PoseSpaceRetargeter owner, ref HumanPose pose, bool applyStretchOffset)
        {
            if (!owner.enableThumbAnatomicalGuard || pose.muscles == null ||
                ShouldPreserveManualFingerReferenceMuscles(owner))
            {
                return 0;
            }

            int changed = ClampThumbMuscles(owner, ref pose, applyStretchOffset);
            LogAnatomicalGuardCorrection(owner, changed);
            return changed;
        }

        private static int ClampThumbMuscles(PoseSpaceRetargeter owner, ref HumanPose pose, bool applyStretchOffset)
        {
            float safeStretchMin = Mathf.Min(owner.thumbStretchMin, owner.thumbStretchMax);
            float safeStretchMax = Mathf.Max(owner.thumbStretchMin, owner.thumbStretchMax);
            float safeSpreadMin = Mathf.Min(owner.thumbSpreadMin, owner.thumbSpreadMax);
            float safeSpreadMax = Mathf.Max(owner.thumbSpreadMin, owner.thumbSpreadMax);
            int count = Mathf.Min(pose.muscles.Length, HumanTrait.MuscleCount);
            int changed = 0;

            for (int i = 0; i < count; i++)
            {
                changed += ClampThumbMuscleSlot(owner, pose.muscles, i, applyStretchOffset, safeStretchMin, safeStretchMax, safeSpreadMin, safeSpreadMax) ? 1 : 0;
            }

            return changed;
        }

        private static bool ClampThumbMuscleSlot(PoseSpaceRetargeter owner, float[] muscles, int index, bool applyStretchOffset, float safeStretchMin, float safeStretchMax, float safeSpreadMin, float safeSpreadMax)
        {
            string muscleName = HumanTrait.MuscleName[index];
            if (string.IsNullOrEmpty(muscleName))
            {
                return false;
            }

            string normalizedName = muscleName.Replace(" ", "").ToLowerInvariant();
            if (!normalizedName.Contains("thumb"))
            {
                return false;
            }

            float before = muscles[index];
            float after = ResolveClampedThumbMuscle(owner, normalizedName, before, applyStretchOffset, safeStretchMin, safeStretchMax, safeSpreadMin, safeSpreadMax);
            if (Mathf.Approximately(before, after))
            {
                return false;
            }

            muscles[index] = after;
            return true;
        }

        private static float ResolveClampedThumbMuscle(PoseSpaceRetargeter owner, string normalizedName, float before, bool applyStretchOffset, float safeStretchMin, float safeStretchMax, float safeSpreadMin, float safeSpreadMax)
        {
            if (normalizedName.Contains("spread"))
            {
                return Mathf.Clamp(before, safeSpreadMin, safeSpreadMax);
            }

            if (normalizedName.Contains("stretch"))
            {
                float offset = applyStretchOffset ? owner.thumbStretchOffset : 0f;
                return Mathf.Clamp(before + offset, safeStretchMin, safeStretchMax);
            }

            return before;
        }

        private static void LogAnatomicalGuardCorrection(PoseSpaceRetargeter owner, int changed)
        {
            if (changed > 0 && owner.logThumbAnatomicalGuardCorrections && !owner._thumbGuardWarningLogged)
            {
                Debug.LogWarning($"[PoseSpaceRetargeter] 엄지 해부학적 제한으로 thumb muscle {changed}개를 보정했습니다.");
                owner._thumbGuardWarningLogged = true;
            }
        }

        internal static bool ShouldApplyStretchOffset(PoseSpaceRetargeter owner)
        {
            return !ShouldPreserveManualFingerReferenceMuscles(owner);
        }

        internal static bool ShouldPreserveManualFingerReferenceMuscles(PoseSpaceRetargeter owner)
        {
            return owner._useEditorFingerPoseReference && owner.preserveManualFingerReferenceThumbMuscles;
        }

        internal static bool ShouldSuppressPoseShapingGuardForHand(PoseSpaceRetargeter owner, bool leftHand)
        {
#if UNITY_EDITOR
            if (!owner.IsManualThumbLocalRotationReferenceActive ||
                !owner.preserveManualFingerReferenceThumbMuscles)
            {
                return false;
            }

            if (owner.ShouldSuppressCompetingManualThumbOverride(leftHand))
            {
                return false;
            }

            float risk = float.NaN;
            return !(RetargetingEditorReferenceHooks.ThumbReference?.TryEvaluateManualOverrideRisk(owner, leftHand, out risk) ?? false) ||
                risk < PoseSpaceRetargeter.ManualThumbPoseShapingSuppressMaxRisk;
#else
            return false;
#endif
        }
    }
}
