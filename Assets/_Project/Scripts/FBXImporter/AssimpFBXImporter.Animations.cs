using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using Assimp;

namespace Fbx2Vmd.FBXImporter
{
    public partial class AssimpFBXImporter
    {
        #region 애니메이션 처리
        private void ProcessAnimations(Scene scene, GameObject rootObject)
        {
            if (!scene.HasAnimations)
            {
                _animationClips = new AnimationClip[0];
                return;
            }

            // Ghost Retargeting을 위해 Legacy Animation 컴포넌트 사용
            // Ghost가 '가방' 역할과 '재생기' 역할을 동시에 하도록 생산 즉시 부착
            UnityEngine.Animation animComp = rootObject.GetComponent<UnityEngine.Animation>();
            if (animComp == null) animComp = rootObject.AddComponent<UnityEngine.Animation>();

            List<AnimationClip> clips = BuildAndRegisterClips(
                scene, rootObject, animComp);
            _animationClips = clips.ToArray();

            if (clips.Count > 0)
            {
                animComp.clip = clips[0];
                Debug.Log($"[FBXImport] 애니메이션 클립 생성됨. 개수={clips.Count}");
            }
            else
            {
                Debug.LogWarning("[FBXImport] 애니메이션 클립이 생성되지 않음.");
            }
        }

        private List<AnimationClip> BuildAndRegisterClips(
            Scene scene, GameObject rootObject, UnityEngine.Animation animComp)
        {
            var clips = new List<AnimationClip>();
            foreach (var anim in scene.Animations)
            {
                AnimationClip clip = CreateAnimationClip(anim, scene);
                ApplyNodeChannels(clip, anim, rootObject);
                clip.frameRate = 60;

                // 컴포넌트에 클립 등록
                animComp.AddClip(clip, clip.name);
                clips.Add(clip);
            }
            return clips;
        }

        static AnimationClip CreateAnimationClip(Assimp.Animation anim, Scene scene)
        {
            var clip = new AnimationClip { name = anim.Name };
            if (string.IsNullOrEmpty(clip.name))
            {
                clip.name = "Animation_" + scene.Animations.IndexOf(anim);
            }

            // 런타임 Legacy 재생을 위해 true 설정
            clip.legacy = true;
            clip.wrapMode = WrapMode.Loop;
            return clip;
        }

        void ApplyNodeChannels(AnimationClip clip, Assimp.Animation anim, GameObject rootObject)
        {
            // FBX tick → Unity seconds 변환
            double ticksPerSecond = anim.TicksPerSecond;
            if (ticksPerSecond <= 1.0)
            {
                ticksPerSecond = 60.0;
                Debug.LogWarning($"[FBXImport] TicksPerSecond 데이터가 없어 기본값 60 FPS 사용함. 값={anim.TicksPerSecond}");
            }
            float timeScale = 1.0f / (float)ticksPerSecond;

            foreach (var channel in anim.NodeAnimationChannels)
            {
                ApplyChannel(clip, channel, rootObject, timeScale);
            }
        }

        private void ApplyChannel(
            AnimationClip clip, Assimp.NodeAnimationChannel channel,
            GameObject rootObject, float timeScale)
        {
            if (!_nodeMap.TryGetValue(channel.NodeName, out Transform targetNode))
            {
                return;
            }
            string relativePath = GetRelativePath(rootObject.transform, targetNode);
            // 위치 애니메이션
            if (channel.HasPositionKeys)
            {
                string positionCurveNodeName = targetNode != null
                    ? targetNode.name
                    : Path.GetFileName(relativePath);
                if (ImportNonRootPositionCurves ||
                    RuntimeAnimationPositionCurvePolicy.ShouldImport(
                        relativePath, positionCurveNodeName))
                {
                    SetPositionCurves(clip, relativePath, channel.PositionKeys, timeScale); } }
            // 회전 애니메이션
            if (channel.HasRotationKeys)
            {
                SetRotationCurves(clip, relativePath, channel.RotationKeys, timeScale); }
            // 스케일 애니메이션
            if (channel.HasScalingKeys && ImportScaleCurves)
            {
                SetScaleCurves(clip, relativePath, channel.ScalingKeys, timeScale); } }

        private void SetPositionCurves(
            AnimationClip clip, string relativePath, List<VectorKey> positionKeys, float timeScale)
        {
            var curveX = new AnimationCurve();
            var curveY = new AnimationCurve();
            var curveZ = new AnimationCurve();

            foreach (var key in positionKeys)
            {
                float time = (float)key.Time * timeScale;
                curveX.AddKey(time, key.Value.X * FBX_TO_UNITY_UNIT_SCALE);
                curveY.AddKey(time, key.Value.Y * FBX_TO_UNITY_UNIT_SCALE);
                curveZ.AddKey(time, key.Value.Z * FBX_TO_UNITY_UNIT_SCALE);
            }

            clip.SetCurve(relativePath, typeof(Transform), "localPosition.x", curveX);
            clip.SetCurve(relativePath, typeof(Transform), "localPosition.y", curveY);
            clip.SetCurve(relativePath, typeof(Transform), "localPosition.z", curveZ);
        }

        private void SetRotationCurves(AnimationClip clip, string relativePath, List<QuaternionKey> rotationKeys, float timeScale)
        {
            var curveX = new AnimationCurve();
            var curveY = new AnimationCurve();
            var curveZ = new AnimationCurve();
            var curveW = new AnimationCurve();

            foreach (var key in rotationKeys)
            {
                float time = (float)key.Time * timeScale;
                curveX.AddKey(time, key.Value.X);
                curveY.AddKey(time, key.Value.Y);
                curveZ.AddKey(time, key.Value.Z);
                curveW.AddKey(time, key.Value.W);
            }

            clip.SetCurve(relativePath, typeof(Transform), "localRotation.x", curveX);
            clip.SetCurve(relativePath, typeof(Transform), "localRotation.y", curveY);
            clip.SetCurve(relativePath, typeof(Transform), "localRotation.z", curveZ);
            clip.SetCurve(relativePath, typeof(Transform), "localRotation.w", curveW);
        }

        private void SetScaleCurves(AnimationClip clip, string relativePath, List<VectorKey> scaleKeys, float timeScale)
        {
            var curveX = new AnimationCurve();
            var curveY = new AnimationCurve();
            var curveZ = new AnimationCurve();

            foreach (var key in scaleKeys)
            {
                float time = (float)key.Time * timeScale;
                curveX.AddKey(time, key.Value.X);
                curveY.AddKey(time, key.Value.Y);
                curveZ.AddKey(time, key.Value.Z);
            }

            clip.SetCurve(relativePath, typeof(Transform), "localScale.x", curveX);
            clip.SetCurve(relativePath, typeof(Transform), "localScale.y", curveY);
            clip.SetCurve(relativePath, typeof(Transform), "localScale.z", curveZ);
        }
        #endregion

        #region 유틸리티 메서드
        private string GetRelativePath(Transform root, Transform target)
        {
            if (root == target) return "";

            string path = target.name;
            while (target.parent != null && target.parent != root)
            {
                target = target.parent;
                path = target.name + "/" + path;
            }
            return path;
        }
        #endregion
    }

}
