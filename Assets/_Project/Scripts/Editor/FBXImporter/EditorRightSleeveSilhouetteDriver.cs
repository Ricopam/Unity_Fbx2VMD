using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter.Editor
{
    /// <summary>
    /// 우측 슬리브 실루엣 로컬 오프셋의 에디터 구현.
    /// 베이스 로컬 위치 딕셔너리는 오너별 세션이 소유한다.
    /// </summary>
    internal sealed class EditorRightSleeveSilhouetteDriver : IEditorRightSleeveSilhouetteDriver
    {
        private static readonly string[] RightSleeveSilhouetteLocalOffsetTransformSuffixes =
        {
            "joint_RightArmM",
            "!joint_RightShoulderC"
        };

        private sealed class Session
        {
            internal readonly Dictionary<Transform, Vector3> BaseLocalPositions =
                new Dictionary<Transform, Vector3>();
        }

        private static Session GetSession(PoseSpaceRetargeter owner)
        {
            return owner._editorSleeveReferenceSession as Session ??
                (Session)(owner._editorSleeveReferenceSession = new Session());
        }

        public bool ShouldApplyRightSleeveSilhouetteLocalOffsetFrameGate(PoseSpaceRetargeter owner)
        {
            return owner.ShouldApplySingleFrameFallbackReferenceFrameGate(
                owner.yybRightSleeveSilhouetteLocalOffsetFrameGateStart,
                owner.yybRightSleeveSilhouetteLocalOffsetFrameGateEnd);
        }

        public void ApplyRightSleeveSilhouetteLocalOffsetReference(PoseSpaceRetargeter owner)
        {
            RestoreRightSleeveSilhouetteLocalOffsetReference(owner);
            if (!owner.useYybRightSleeveSilhouetteLocalOffsetReference ||
                !ShouldApplyRightSleeveSilhouetteLocalOffsetFrameGate(owner))
            {
                return;
            }

            float offsetX = Mathf.Clamp(owner.yybRightSleeveSilhouetteLocalOffsetX, -0.2f, 0.2f);
            if (Mathf.Abs(offsetX) <= 0.00001f)
            {
                return;
            }

            Vector3 offset = new Vector3(offsetX, 0f, 0f);
            ApplyRightSleeveSilhouetteLocalOffsetToTransform(
                owner,
                owner.targetAnimator != null ? owner.targetAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm) : null,
                offset);
            ApplyRightSleeveSilhouetteLocalOffsetToTransform(
                owner,
                owner.targetAnimator != null ? owner.targetAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm) : null,
                offset);
            ApplyRightSleeveSilhouetteLocalOffsetToTransform(
                owner,
                owner.targetAnimator != null ? owner.targetAnimator.GetBoneTransform(HumanBodyBones.RightHand) : null,
                offset);
            for (int i = 0; i < RightSleeveSilhouetteLocalOffsetTransformSuffixes.Length; i++)
            {
                Transform target = owner.FindTargetTransformByNameSuffix(
                    RightSleeveSilhouetteLocalOffsetTransformSuffixes[i]);
                ApplyRightSleeveSilhouetteLocalOffsetToTransform(owner, target, offset);
            }
        }

        public void ApplyRightSleeveSilhouetteLocalOffsetToTransform(PoseSpaceRetargeter owner, Transform target, Vector3 offset)
        {
            Session session = GetSession(owner);
            if (target == null ||
                !IsFinite(target.localPosition) ||
                !IsFinite(offset) ||
                session.BaseLocalPositions.ContainsKey(target))
            {
                return;
            }

            session.BaseLocalPositions[target] = target.localPosition;
            target.localPosition += offset;
        }

        public void RestoreRightSleeveSilhouetteLocalOffsetReference(PoseSpaceRetargeter owner)
        {
            Session session = GetSession(owner);
            if (session.BaseLocalPositions.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<Transform, Vector3> entry in session.BaseLocalPositions)
            {
                if (entry.Key != null && IsFinite(entry.Value))
                {
                    entry.Key.localPosition = entry.Value;
                }
            }

            session.BaseLocalPositions.Clear();
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
