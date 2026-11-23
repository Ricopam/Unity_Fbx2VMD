using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.ClothPhysics
{
    /// <summary>
    /// 머리카락 본 체인 하나의 스프링 계산. 참고 본 스프링 구현(Docs/ref)의 체인 계산을 가져와 정리함.
    /// 각 본 위치는 '부모 스프링 위치 + 애니메이션 자세 오프셋'을 목표로 감쇠 스프링으로 따라가고,
    /// 결과는 뿌리→끝 혼합 곡선으로 애니메이션 자세와 섞음. 본에는 회전만 써서 길이·스케일을 보존함.
    /// </summary>
    internal sealed class SpringHairChain
    {
        // 충돌 구 밖으로 밀어내는 최대 속도(m/s). 한 번에 표면까지 밀면 튕기듯 꺾임.
        private const float MaxCollisionResolutionSpeed = 3f;
        private const float Epsilon = 1e-6f;

        // 부모가 자식보다 앞에 오는 순서(너비 우선).
        private readonly Transform[] bones;
        private readonly int[] parents;
        private readonly int[][] children;
        // 뿌리 0 ~ 끝 1. 뿌리에서 잰 본 길이 누적 비율.
        private readonly float[] depths;
        // 뿌리에서 가장 먼 본까지의 단계 수. 본마다 스프링 지연이 쌓이므로 주파수를 이만큼 올림.
        private readonly int linkCount;
        private readonly Vector3[] animatedPositions;
        private readonly Quaternion[] animatedRotations;
        private readonly Quaternion[] animatedLocalRotations;
        private readonly Vector3[] springPositions;
        private readonly Vector3[] springVelocities;
        private readonly Quaternion[] springRotations;
        private readonly Vector3[] previousPositions;
        private readonly Quaternion[] targetRotations;
        private readonly Vector3[] blendedPositions;
        private bool isInitialized;
        private bool isPoseWritten;

        public SpringHairChain(Transform root)
        {
            var order = new List<Transform>();
            var parentList = new List<int>();
            var queue = new Queue<(Transform bone, int parent)>();
            queue.Enqueue((root, -1));
            while (queue.Count > 0)
            {
                var (bone, parent) = queue.Dequeue();
                int index = order.Count;
                order.Add(bone);
                parentList.Add(parent);
                foreach (Transform child in bone)
                    queue.Enqueue((child, index));
            }

            bones = order.ToArray();
            parents = parentList.ToArray();
            int count = bones.Length;
            children = new int[count][];
            var childLists = new List<int>[count];
            for (int i = 0; i < count; i++)
                childLists[i] = new List<int>();
            for (int i = 1; i < count; i++)
                childLists[parents[i]].Add(i);
            for (int i = 0; i < count; i++)
                children[i] = childLists[i].ToArray();

            depths = MeasureDepths(bones, parents);
            linkCount = CountLinks(parents);
            animatedPositions = new Vector3[count];
            animatedRotations = new Quaternion[count];
            animatedLocalRotations = new Quaternion[count];
            springPositions = new Vector3[count];
            springVelocities = new Vector3[count];
            springRotations = new Quaternion[count];
            previousPositions = new Vector3[count];
            targetRotations = new Quaternion[count];
            blendedPositions = new Vector3[count];
        }

        public Transform Root => bones[0];

        public IReadOnlyList<Transform> Bones => bones;

        /// <summary>다음 프레임에 스프링을 현재 애니메이션 자세에서 다시 시작함(리스폰·순간이동).</summary>
        public void Reset()
        {
            isInitialized = false;
        }

        /// <summary>
        /// 직전에 쓴 회전을 애니메이션 값으로 되돌림. 애니메이션이 쓰지 않는 머리카락 본에
        /// 물리 결과가 다음 프레임의 '애니메이션 자세'로 누적되는 것을 막음.
        /// </summary>
        public void RestoreAnimatedPose()
        {
            if (!isPoseWritten)
                return;
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null)
                    bones[i].localRotation = animatedLocalRotations[i];
            isPoseWritten = false;
        }

        /// <summary>애니메이션 자세를 읽고 스프링을 deltaTime만큼 진행한 뒤 결과 회전을 씀.</summary>
        public void Step(float deltaTime, in SpringHairSettings settings,
            IReadOnlyList<(Vector3 center, float radius)> spheres)
        {
            if (!HasAllBones())
                return;
            CaptureAnimatedPose();
            if (!isInitialized)
                ResetSprings();
            if (deltaTime > 0f)
            {
                TrackTargets(deltaTime, settings, spheres);
                KeepBoneLengths(settings);
                ResolveCollisions(deltaTime, settings, spheres);
            }
            WriteBlendedPose(settings, spheres);
        }

        private bool HasAllBones()
        {
            foreach (var bone in bones)
                if (bone == null)
                    return false;
            return true;
        }

        private void CaptureAnimatedPose()
        {
            for (int i = 0; i < bones.Length; i++)
            {
                animatedPositions[i] = bones[i].position;
                animatedRotations[i] = bones[i].rotation;
                animatedLocalRotations[i] = bones[i].localRotation;
            }
        }

        private void ResetSprings()
        {
            for (int i = 0; i < bones.Length; i++)
            {
                springPositions[i] = animatedPositions[i];
                springVelocities[i] = Vector3.zero;
                springRotations[i] = animatedRotations[i];
            }
            isInitialized = true;
        }

        // 참고 구현은 모든 목표를 직전 프레임 값으로 구해 본마다 한 프레임씩 늦어짐.
        // 본이 20개 넘는 긴 머리카락은 끝이 수십 프레임 늦어지므로, 부모를 먼저 갱신하고
        // 그 새 위치·회전을 기준으로 자식 목표를 구함.
        private void TrackTargets(float deltaTime, in SpringHairSettings settings,
            IReadOnlyList<(Vector3 center, float radius)> spheres)
        {
            System.Array.Copy(springPositions, previousPositions, springPositions.Length);
            springPositions[0] = animatedPositions[0];
            springVelocities[0] = Vector3.zero;
            targetRotations[0] = animatedRotations[0];

            // 본마다 스프링 지연이 단계 수만큼 쌓이므로, 체인 전체 응답 시간이
            // 본 개수와 무관하도록 본 주파수를 단계 수배로 올림.
            float boneFrequency = settings.AngularFrequency * linkCount;
            Vector3 gravityStep = settings.Gravity * deltaTime;
            for (int i = 1; i < bones.Length; i++)
            {
                // 목표가 움직이는 팔·다리 충돌 구 안이면 스프링이 매 프레임 안쪽으로 되당겨
                // 밀어내기 속도 상한으로는 빠져나오지 못하므로 목표를 구 밖으로 옮김.
                Vector3 target = PushOutOfSpheres(ComputeTarget(i, settings), BoneRadius(i, settings), spheres);
                springVelocities[i] += gravityStep;
                TrackDampingRatio(ref springPositions[i], ref springVelocities[i], target,
                    boneFrequency, settings.DampingRatio, deltaTime);
            }
            System.Array.Copy(targetRotations, springRotations, targetRotations.Length);
        }

        // 부모 스프링 좌표계에서 '직전 스프링 오프셋'과 '애니메이션 오프셋'을 포즈 강성으로 섞어
        // 이번 프레임 부모 위치·회전에 붙임.
        private Vector3 ComputeTarget(int index, in SpringHairSettings settings)
        {
            int parent = parents[index];
            Quaternion inverseParentPrevious = Quaternion.Inverse(springRotations[parent]);
            Quaternion inverseParentAnimated = Quaternion.Inverse(animatedRotations[parent]);

            Vector3 springOffset = inverseParentPrevious * (previousPositions[index] - previousPositions[parent]);
            Vector3 animatedOffset = inverseParentAnimated * (animatedPositions[index] - animatedPositions[parent]);
            Quaternion springLocal = inverseParentPrevious * springRotations[index];
            Quaternion animatedLocal = inverseParentAnimated * animatedRotations[index];

            float stiffness = Mathf.Clamp01(settings.PoseStiffness.Evaluate(depths[index]));
            Quaternion parentRotation = targetRotations[parent];
            targetRotations[index] = parentRotation * Quaternion.Slerp(springLocal, animatedLocal, stiffness);
            Vector3 target = springPositions[parent] +
                parentRotation * Vector3.Lerp(springOffset, animatedOffset, stiffness);
            return ClampBendFromRoot(index, target, settings);
        }

        // 부모와의 거리를 애니메이션 본 길이로 되돌리고 그 방향 속도를 없앰(길이 강성 1).
        private void KeepBoneLengths(in SpringHairSettings settings)
        {
            for (int i = 1; i < bones.Length; i++)
            {
                int parent = parents[i];
                Vector3 toParent = springPositions[parent] - springPositions[i];
                float length = toParent.magnitude;
                if (length < Epsilon)
                    continue;
                Vector3 direction = toParent / length;
                float restLength = Vector3.Distance(animatedPositions[parent], animatedPositions[i]);
                springPositions[i] += (length - restLength) * direction;
                springVelocities[i] -= Vector3.Project(springVelocities[i], direction);
                springPositions[i] = ClampBendFromRoot(i, springPositions[i], settings);
            }
        }

        // 겹친 충돌 구 밖으로 프레임당 최대 해소 속도만큼만 밀고 그 방향 속도를 없앰.
        private void ResolveCollisions(float deltaTime, in SpringHairSettings settings,
            IReadOnlyList<(Vector3 center, float radius)> spheres)
        {
            if (spheres == null || spheres.Count == 0)
                return;
            float maxPush = MaxCollisionResolutionSpeed * deltaTime;
            for (int i = 1; i < bones.Length; i++)
            {
                float radius = BoneRadius(i, settings);
                foreach (var (center, sphereRadius) in spheres)
                {
                    if (!ColliderMath.TrySphereSpherePush(springPositions[i], radius, center, sphereRadius, out Vector3 push))
                        continue;
                    push = Vector3.ClampMagnitude(push, maxPush);
                    springPositions[i] += push;
                    springVelocities[i] -= Vector3.Project(springVelocities[i], push);
                }
            }
        }

        private float BoneRadius(int index, in SpringHairSettings settings)
        {
            return settings.MaxCollisionRadius * Mathf.Max(0f, settings.CollisionRadius.Evaluate(depths[index]));
        }

        // 섞은 위치로 향하도록 부모부터 회전만 씀. 뿌리 위치는 그대로 두고 뿌리도 회전시켜
        // 본 2개짜리 체인(뿌리+끝)도 움직이게 함. 긴 체인은 뿌리 근처가 애니메이션에 붙어 거의 그대로임.
        private void WriteBlendedPose(in SpringHairSettings settings,
            IReadOnlyList<(Vector3 center, float radius)> spheres)
        {
            blendedPositions[0] = animatedPositions[0];
            for (int i = 1; i < bones.Length; i++)
            {
                float blend = Mathf.Clamp01(settings.AnimationBlend.Evaluate(depths[i]));
                blendedPositions[i] = Vector3.Lerp(springPositions[i], animatedPositions[i], blend);
                // 애니메이션 쪽으로 섞이며 스프링 위치보다 충돌 구 안쪽으로 들어간 만큼만 밖으로 냄.
                // 스프링 자체의 밀어내기는 속도 상한을 그대로 따름.
                blendedPositions[i] = LimitToSpringDepth(blendedPositions[i], springPositions[i],
                    BoneRadius(i, settings), spheres);
            }

            for (int i = 0; i < bones.Length; i++)
            {
                if (children[i].Length == 0)
                    continue;
                bones[i].rotation = BlendedRotation(i) * animatedRotations[i];
            }
            isPoseWritten = true;
        }

        // 자식마다 애니메이션 방향→섞은 방향 회전을 구해 끝까지 남은 길이로 가중 평균함.
        private Quaternion BlendedRotation(int index)
        {
            Vector3 origin = bones[index].position;
            Quaternion average = Quaternion.identity;
            float totalWeight = 0f;
            foreach (int child in children[index])
            {
                Vector3 from = animatedPositions[child] - animatedPositions[index];
                Vector3 to = blendedPositions[child] - origin;
                if (from.sqrMagnitude < Epsilon || to.sqrMagnitude < Epsilon)
                    continue;
                float weight = Mathf.Max(Epsilon, 1f - depths[child]);
                totalWeight += weight;
                average = Quaternion.Slerp(average, Quaternion.FromToRotation(from, to), weight / totalWeight);
            }
            return average;
        }

        private static Vector3 PushOutOfSpheres(Vector3 position, float radius,
            IReadOnlyList<(Vector3 center, float radius)> spheres)
        {
            if (spheres == null)
                return position;
            foreach (var (center, sphereRadius) in spheres)
                if (ColliderMath.TrySphereSpherePush(position, radius, center, sphereRadius, out Vector3 push))
                    position += push;
            return position;
        }

        private static Vector3 LimitToSpringDepth(Vector3 blended, Vector3 spring, float radius,
            IReadOnlyList<(Vector3 center, float radius)> spheres)
        {
            if (spheres == null)
                return blended;
            foreach (var (center, sphereRadius) in spheres)
            {
                if (!ColliderMath.TrySphereSpherePush(blended, radius, center, sphereRadius, out Vector3 push))
                    continue;
                float reach = radius + sphereRadius;
                float springDepth = Mathf.Max(0f, reach - Vector3.Distance(spring, center));
                float extraDepth = push.magnitude - springDepth;
                if (extraDepth > 0f)
                    blended += push.normalized * extraDepth;
            }
            return blended;
        }

        private Vector3 ClampBendFromRoot(int index, Vector3 position, in SpringHairSettings settings)
        {
            if (!settings.HasBendLimit)
                return position;
            float cap = settings.MaxBendRadians * Mathf.Clamp01(settings.BendAngleCap.Evaluate(depths[index]));
            Vector3 root = animatedPositions[0];
            return root + ClampBend(position - root, animatedPositions[index] - root, cap);
        }

        /// <summary>vector가 reference와 이루는 각을 maxRadians 이하로 줄임. 길이는 유지함.</summary>
        public static Vector3 ClampBend(Vector3 vector, Vector3 reference, float maxRadians)
        {
            float length = vector.magnitude;
            if (length < Epsilon || reference.sqrMagnitude < Epsilon)
                return vector;
            if (Vector3.Angle(vector, reference) * Mathf.Deg2Rad <= maxRadians)
                return vector;
            return Vector3.RotateTowards(reference.normalized, vector / length, maxRadians, 0f) * length;
        }

        /// <summary>
        /// 감쇠비로 정한 2차 스프링을 암시적 오일러로 한 단계 진행함(참고 구현과 같은 식).
        /// 큰 프레임 시간에도 발산하지 않음.
        /// </summary>
        public static void TrackDampingRatio(ref Vector3 value, ref Vector3 velocity, Vector3 target,
            float angularFrequency, float dampingRatio, float deltaTime)
        {
            if (angularFrequency < Epsilon)
            {
                velocity = Vector3.zero;
                return;
            }
            float f = 1f + 2f * deltaTime * dampingRatio * angularFrequency;
            float omegaSquared = angularFrequency * angularFrequency;
            float hOmegaSquared = deltaTime * omegaSquared;
            float hhOmegaSquared = deltaTime * hOmegaSquared;
            float inverse = 1f / (f + hhOmegaSquared);
            Vector3 nextValue = (f * value + deltaTime * velocity + hhOmegaSquared * target) * inverse;
            velocity = (velocity + hOmegaSquared * (target - value)) * inverse;
            value = nextValue;
        }

        private static int CountLinks(int[] parents)
        {
            var links = new int[parents.Length];
            int max = 1;
            for (int i = 1; i < parents.Length; i++)
            {
                links[i] = links[parents[i]] + 1;
                max = Mathf.Max(max, links[i]);
            }
            return max;
        }

        private static float[] MeasureDepths(Transform[] bones, int[] parents)
        {
            var lengths = new float[bones.Length];
            float maxLength = 0f;
            for (int i = 1; i < bones.Length; i++)
            {
                lengths[i] = lengths[parents[i]] + Vector3.Distance(bones[parents[i]].position, bones[i].position);
                maxLength = Mathf.Max(maxLength, lengths[i]);
            }
            if (maxLength > Epsilon)
                for (int i = 0; i < lengths.Length; i++)
                    lengths[i] /= maxLength;
            return lengths;
        }
    }
}
