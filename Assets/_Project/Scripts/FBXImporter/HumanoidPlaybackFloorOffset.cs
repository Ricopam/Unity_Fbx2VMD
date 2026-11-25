using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 재생할 클립에서 지지 중인 발의 밑창이 바닥과 얼마나 어긋나는지 재고, 클립 단위로 몸 높이 보정값을 정함.
    /// Humanoid 리타깃은 골반 높이를 사람 크기 비율로만 맞춰 다리 비율이 다르면 발이 바닥 아래로 내려감.
    /// 접지 보정이 골반을 두고 발만 들어 올리면 무릎·발목이 접히므로, 그 차이를 몸 전체 높이로 먼저 흡수함.
    /// </summary>
    internal static class HumanoidPlaybackFloorOffset
    {
        // 바닥에서 이 거리(사람 크기 대비) 안에 있는 아래쪽 발 밑창만 지지 표본으로 씀 — 점프 등 공중 표본 제외.
        internal const float SupportBandPerHumanScale = 0.1f;
        // 보정 상한(사람 크기 대비) — 측정 이상으로 몸이 크게 뜨거나 가라앉지 않게 함.
        internal const float MaximumOffsetPerHumanScale = 0.06f;
        // 지지 표본이 전체 표본의 이 비율보다 적으면 중앙값을 믿을 수 없어 보정하지 않음.
        internal const float MinimumSupportedFraction = 0.3f;
        // 이보다 작은 어긋남(m)은 보정하지 않아 이미 바닥에 맞는 클립·모델의 기존 동작을 유지함.
        internal const float MinimumOffset = 0.002f;
        // 클립을 이 간격(프레임)으로 표본 추출함 — 준비 시간과 중앙값 안정성의 절충.
        private const int SampleStepFrames = 10;

        /// <summary>클립을 표본 추출해 몸 높이 보정값(m, 위쪽 양수)을 구함. 측정할 수 없으면 0.</summary>
        internal static float Resolve(Animator animator, Action<float> evaluate, float clipLength,
            float frameRate, HumanoidGroundResponse ground)
        {
            if (!TryMeasureLowestSoleHeights(animator, evaluate, clipLength, frameRate, ground, out List<float> heights))
                return 0f;
            return ResolveOffset(heights, animator.humanScale);
        }

        /// <summary>표본별 아래쪽 발 밑창의 지면 높이에서 보정값을 구함(지지 대역 중앙값의 반대 부호, 상한 적용).</summary>
        internal static float ResolveOffset(IReadOnlyList<float> lowestSoleHeights, float humanScale)
        {
            bool hasValidInput = lowestSoleHeights != null && lowestSoleHeights.Count > 0 &&
                humanScale > 0f && !float.IsInfinity(humanScale);
            if (!hasValidInput)
                return 0f;
            float band = humanScale * SupportBandPerHumanScale;
            var supported = new List<float>(lowestSoleHeights.Count);
            foreach (float height in lowestSoleHeights)
                if (!float.IsNaN(height) && Mathf.Abs(height) <= band)
                    supported.Add(height);
            bool hasEnoughSupport = supported.Count > 0 &&
                supported.Count >= lowestSoleHeights.Count * MinimumSupportedFraction;
            if (!hasEnoughSupport)
                return 0f;
            float maximum = humanScale * MaximumOffsetPerHumanScale;
            float offset = Mathf.Clamp(-Median(supported), -maximum, maximum);
            return Mathf.Abs(offset) < MinimumOffset ? 0f : offset;
        }

        /// <summary>클립을 일정 간격으로 평가해 두 발 중 낮은 쪽 밑창의 지면 높이(m)를 모음. 끝나면 0초로 되돌림.</summary>
        internal static bool TryMeasureLowestSoleHeights(Animator animator, Action<float> evaluate,
            float clipLength, float frameRate, HumanoidGroundResponse ground, out List<float> heights)
        {
            heights = new List<float>();
            bool hasValidInput = animator != null && animator.isHuman && evaluate != null && ground != null &&
                clipLength >= 0f && frameRate > 0f;
            if (!hasValidInput || !TryCreateSamplers(animator, out HumanoidFootSoleSampler left, out HumanoidFootSoleSampler right))
                return false;
            try
            {
                // 지면 탐색은 캐릭터 자신의 충돌체를 제외하므로 정지한 지면 기준으로 한 번만 동기화함.
                Physics.SyncTransforms();
                int lastFrame = Mathf.FloorToInt(clipLength * frameRate);
                for (int frame = 0; frame <= lastFrame; frame += SampleStepFrames)
                {
                    evaluate(frame / frameRate);
                    bool hasLeft = TryFindSoleHeight(left, ground, out float leftHeight);
                    bool hasRight = TryFindSoleHeight(right, ground, out float rightHeight);
                    // 지면을 못 찾은 표본도 NaN으로 남겨 지지 비율의 분모에 포함함.
                    heights.Add(hasLeft || hasRight
                        ? Mathf.Min(hasLeft ? leftHeight : float.PositiveInfinity, hasRight ? rightHeight : float.PositiveInfinity)
                        : float.NaN);
                }
                return true;
            }
            finally
            {
                left.Dispose();
                right.Dispose();
                evaluate(0f);
            }
        }

        private static bool TryCreateSamplers(Animator animator, out HumanoidFootSoleSampler left,
            out HumanoidFootSoleSampler right)
        {
            right = null;
            SkinnedMeshRenderer[] renderers = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (!HumanoidFootSoleSampler.TryCreate(animator.GetBoneTransform(HumanBodyBones.LeftFoot),
                    animator.GetBoneTransform(HumanBodyBones.LeftToes), renderers, animator.transform.up, out left))
                return false;
            if (HumanoidFootSoleSampler.TryCreate(animator.GetBoneTransform(HumanBodyBones.RightFoot),
                    animator.GetBoneTransform(HumanBodyBones.RightToes), renderers, animator.transform.up, out right))
                return true;
            left.Dispose();
            left = null;
            return false;
        }

        // 현재 자세에서 발 밑창의 가장 낮은 점이 그 아래 지면보다 얼마나 위에 있는지(음수면 관통) 구함.
        private static bool TryFindSoleHeight(HumanoidFootSoleSampler sampler, HumanoidGroundResponse ground,
            out float height)
        {
            height = 0f;
            if (!sampler.TrySample() ||
                !sampler.TrySelectContact(false, Vector3.up, out _, out Vector3 rear) ||
                !sampler.TrySelectContact(true, Vector3.up, out _, out Vector3 front))
                return false;
            Vector3 lowest = rear.y <= front.y ? rear : front;
            if (!ground.TryFindGround(lowest, out RaycastHit hit))
                return false;
            height = Vector3.Dot(lowest - hit.point, hit.normal);
            return true;
        }

        private static float Median(List<float> values)
        {
            values.Sort();
            int middle = values.Count / 2;
            return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) * 0.5f;
        }
    }
}
