using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    // 레퍼런스 포즈 평가·캡처 진입점 모음 partial.
    internal sealed partial class HumanoidPoseReferencePlayer
    {
        internal bool TryEvaluateFootFramesAt(float timeSeconds,
            out Quaternion leftFrame, out Quaternion rightFrame)
        {
            leftFrame = rightFrame = Quaternion.identity;
            if (!IsInitialized || !HasFootRotationReference)
                return false;
            _animationPlayer.EvaluateAt(timeSeconds);
            return _leftFootRotation.TryCaptureWorldFrame(out leftFrame) &&
                _rightFootRotation.TryCaptureWorldFrame(out rightFrame);
        }

        internal bool TryCaptureFootSupportReference(out Vector2 heights,
            out Quaternion leftFrame, out Quaternion rightFrame)
        {
            heights = Vector2.zero;
            leftFrame = rightFrame = Quaternion.identity;
            if (!IsInitialized || !HasFootRotationReference ||
                !_leftFootRotation.TryCaptureWorldFrame(out leftFrame) ||
                !_rightFootRotation.TryCaptureWorldFrame(out rightFrame))
                return false;
            // 재평가 없이 같은 원본 표본을 읽고, 기준 평면은 초기 자세에 고정함.
            Vector3 referenceUp = InitialRootRotation * Vector3.up;
            heights = new Vector2(
                Vector3.Dot(leftFrame * Vector3.forward, referenceUp) * _footForwardLengths.x,
                Vector3.Dot(rightFrame * Vector3.forward, referenceUp) * _footForwardLengths.y);
            return IsFinite(heights.x) && IsFinite(heights.y);
        }

        // 재평가 없이 마지막 평가 시점의 원본 골반 포즈를 읽음. 루트 모션이 골반에 실리므로
        // goal 스트림의 루트 상대 정규화 기준으로 씀.
        internal bool TryCaptureHipsPose(out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (!IsInitialized || _hips == null)
            {
                return false;
            }
            position = _hips.position;
            rotation = _hips.rotation;
            return IsFinite(position) && IsFinite(rotation);
        }

        internal bool TryEvaluateAt(float timeSeconds, ref HumanPose pose)
        {
            if (!IsInitialized)
            {
                return false;
            }

            _animationPlayer.EvaluateAt(timeSeconds);
            _poseHandler.GetHumanPose(ref pose);
            return IsFinite(pose);
        }

        internal bool TryEvaluateArmDirectionsAt(
            float timeSeconds,
            out HumanoidArmDirectionReference reference)
        {
            reference = default;
            if (!IsInitialized)
            {
                return false;
            }

            _animationPlayer.EvaluateAt(timeSeconds);
            return TryCaptureArmDirectionReference(_referenceAnimator, out reference);
        }

        internal bool TryEvaluateFootContactPointsAt(
            float timeSeconds,
            out Vector3 left,
            out Vector3 right)
        {
            left = Vector3.zero;
            right = Vector3.zero;
            if (!IsInitialized || _leftFoot == null || _rightFoot == null)
            {
                return false;
            }

            if (!TryEvaluateFootSupportPointsAt(
                    timeSeconds,
                    out Vector3 leftFoot,
                    out Vector3 leftToes,
                    out Vector3 rightFoot,
                    out Vector3 rightToes))
            {
                return false;
            }

            left = (leftFoot + leftToes) * 0.5f;
            right = (rightFoot + rightToes) * 0.5f;
            return IsFinite(left) && IsFinite(right);
        }

        internal bool TryEvaluateFootSupportPointsAt(
            float timeSeconds,
            out Vector3 leftFoot,
            out Vector3 leftToes,
            out Vector3 rightFoot,
            out Vector3 rightToes)
        {
            leftFoot = Vector3.zero;
            leftToes = Vector3.zero;
            rightFoot = Vector3.zero;
            rightToes = Vector3.zero;
            if (!IsInitialized || _leftFoot == null || _rightFoot == null)
            {
                return false;
            }

            _animationPlayer.EvaluateAt(timeSeconds);
            leftFoot = _leftFoot.position;
            leftToes = _leftToes == null ? leftFoot : _leftToes.position;
            rightFoot = _rightFoot.position;
            rightToes = _rightToes == null ? rightFoot : _rightToes.position;
            return IsFinite(leftFoot) && IsFinite(leftToes) &&
                IsFinite(rightFoot) && IsFinite(rightToes);
        }

    }
}
