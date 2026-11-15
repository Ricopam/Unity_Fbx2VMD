using MagicaCloth2;
using UnityEngine;

namespace Fbx2Vmd.ClothPhysics
{
    // MC2 대칭(미러) 콜라이더의 포즈 재현·침투 측정 전담 분할.
    public partial class PhysicsValidationProbe
    {
        /// <summary>
        /// MC2 대칭 콜라이더의 월드 포즈를 계산한다.
        /// ColliderManager.UpdateColliders의 시뮬레이션 수식을 재현한다:
        /// 소스 콜라이더의 로컬 포즈를 대칭 축 기준으로 반사하고
        /// ActiveSymmetryTarget의 월드 포즈를 부모로 사용한다.
        /// </summary>
        public static bool TryComputeSymmetryPose(
            ColliderComponent col,
            out Vector3 worldPos,
            out Quaternion worldRot,
            out Vector3 worldScale)
        {
            if (col == null)
            {
                worldPos = default;
                worldRot = default;
                worldScale = default;
                return false;
            }
            // ActiveSymmetryMode는 MC2 런타임이 계산하기 전까지 null — 미해석은 None과 동일하게 취급한다.
            return TryComputeSymmetryPose(
                col.ActiveSymmetryMode ?? ColliderSymmetryMode.None,
                col.transform, col.ActiveSymmetryTarget,
                out worldPos, out worldRot, out worldScale);
        }

        /// <summary>대칭 모드·소스 포즈·대칭 타겟으로 대칭 콜라이더 월드 포즈를 계산한다.</summary>
        public static bool TryComputeSymmetryPose( ColliderSymmetryMode mode, Transform src, Transform target, out Vector3 worldPos, out Quaternion worldRot, out Vector3 worldScale)
        {
            worldPos = default;
            worldRot = default;
            worldScale = default;
            if (target == null || src == null || mode == ColliderSymmetryMode.None) return false;
            Vector3 lpos = src.localPosition;
            Vector3 lrotDeg = src.localEulerAngles;
            if (!TryMirrorLocalPose(mode, ref lpos, ref lrotDeg)) return false;
            Vector3 pscl = target.lossyScale;
            Vector3 sclSign = new Vector3( Mathf.Sign(pscl.x), Mathf.Sign(pscl.y), Mathf.Sign(pscl.z));
            Vector3 sclEulerSign = Vector3.one;
            if (pscl.x < 0f || pscl.y < 0f || pscl.z < 0f) sclEulerSign = -sclSign;
            Quaternion prot = target.rotation;
            worldPos = prot * Vector3.Scale(pscl, lpos) + target.position;
            worldRot = prot * Quaternion.Euler(Vector3.Scale(lrotDeg, sclEulerSign));
            worldScale = Vector3.Scale(pscl, src.localScale);
            return true; }

        /// <summary>대칭 모드에 따라 로컬 위치·회전을 반전한다.</summary>
        private static bool TryMirrorLocalPose(
            ColliderSymmetryMode mode, ref Vector3 lpos, ref Vector3 lrotDeg)
        {
            switch (mode)
            {
                case ColliderSymmetryMode.X_Symmetry: lpos.x = -lpos.x;
                    lrotDeg.y = -lrotDeg.y;
                    lrotDeg.z = -lrotDeg.z;
                    break;
                case ColliderSymmetryMode.Y_Symmetry: lpos.y = -lpos.y;
                    lrotDeg.x = -lrotDeg.x;
                    lrotDeg.z = -lrotDeg.z;
                    break;
                case ColliderSymmetryMode.Z_Symmetry: lpos.z = -lpos.z;
                    lrotDeg.x = -lrotDeg.x;
                    lrotDeg.y = -lrotDeg.y;
                    break;
                case ColliderSymmetryMode.XYZ_Symmetry: lpos = -lpos;
                    break;
                default:
                    // AutomaticHumanBody/AutomaticTarget은 런타임에 구체 축으로 해석됨.
                    // 해석되지 않은 상태면 대칭 인스턴스가 없으므로 측정 대상 아님.
                    return false; }
            return true;
        }

        /// <summary>대칭 콜라이더 로컬 공간 점을 월드로 변환한다 (MC2 center 오프셋 규칙 재현).</summary>
        static Vector3 SymmetryLocalToWorld(
            Vector3 local, Vector3 wpos, Quaternion wrot, Vector3 wscl, Vector3 sclSign)
        {
            return wpos + Vector3.Scale(
                wrot * Vector3.Scale(local, sclSign),
                Vector3.Scale(wscl, sclSign));
        }

        // 대칭 콜라이더 인스턴스에 대한 침투 검사 — MC2의 대칭 포즈 수식을 재현해 측정한다.
        static float MirrorPenetration(Vector3 point, ColliderComponent col)
        {
            if (!TryComputeSymmetryPose(col, out Vector3 wpos, out Quaternion wrot, out Vector3 wscl)) return 0f;
            Transform target = col.ActiveSymmetryTarget;
            Vector3 pscl = target.lossyScale;
            var sclSign = new Vector3( Mathf.Sign(pscl.x), Mathf.Sign(pscl.y), Mathf.Sign(pscl.z));
            float radiusScale = Mathf.Max( Mathf.Abs(wscl.x), Mathf.Abs(wscl.y), Mathf.Abs(wscl.z));
            if (col is MagicaSphereCollider sphere)
            {
                // 소스 center의 대칭 축 반사 (ColliderManager와 동일 규칙)
                Vector3 c = MirrorLocalOffset( col.ActiveSymmetryMode ?? ColliderSymmetryMode.None, sphere.center);
                Vector3 cWorld = SymmetryLocalToWorld(c, wpos, wrot, wscl, sclSign);
                float r = sphere.GetSize().x * radiusScale;
                return -ColliderMath.SignedDistanceSphere(point, cWorld, r); }
            if (col is MagicaCapsuleCollider cap)
            {
                var size = cap.GetSize(); // (startR, endR, len)
                Vector3 localDir = cap.GetLocalDir();
                Vector3 center = MirrorLocalOffset( col.ActiveSymmetryMode ?? ColliderSymmetryMode.None, cap.center);
                // 캡슐은 축 대칭 도형이라 방향 반전 플래그는 형상에 영향 없음
                Vector3 aLocal = center - localDir * (size.z * 0.5f);
                Vector3 bLocal = center + localDir * (size.z * 0.5f);
                Vector3 a = SymmetryLocalToWorld(aLocal, wpos, wrot, wscl, sclSign);
                Vector3 b = SymmetryLocalToWorld(bLocal, wpos, wrot, wscl, sclSign);
                float r = Mathf.Max(size.x, size.y) * radiusScale;
                return -ColliderMath.SignedDistanceCapsule(point, a, b, r); }
            return 0f; }

        /// <summary>콜라이더 로컬 오프셋(center)을 대칭 축 기준으로 반사한다.</summary>
        static Vector3 MirrorLocalOffset(ColliderSymmetryMode mode, Vector3 v)
        {
            switch (mode)
            {
                case ColliderSymmetryMode.X_Symmetry: v.x = -v.x; break;
                case ColliderSymmetryMode.Y_Symmetry: v.y = -v.y; break;
                case ColliderSymmetryMode.Z_Symmetry: v.z = -v.z; break;
                case ColliderSymmetryMode.XYZ_Symmetry: v = -v; break;
            }
            return v;
        }
    }
}
