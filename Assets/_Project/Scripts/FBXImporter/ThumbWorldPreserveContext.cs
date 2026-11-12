using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// 수동참조 세계회전 보존 판정 한 건의 입력·중간 산출물을 담는 순수 데이터 컨텍스트.
    /// </summary>
    internal struct ThumbWorldPreserveContext
    {
        internal HumanBodyBones thumbBone;
        internal Transform targetThumb;
        internal Quaternion candidateWorldRotation, candidateLocalRotation, referenceLocalRotation;
        internal bool leftHand, proximalBone;
        internal bool suppressCompeting, keepDetached, allowManualReferenceWorldPreserve, allowDetachedHelperShortcutPreserve;
        internal bool hasDetachedHelperRelationship, hasReferenceLocalRotation, hasReferenceFrameDeviation;
        internal float currentReferenceAngle, candidateReferenceAngle, currentReferenceFrameDeviation, candidateReferenceFrameDeviation;
    }
}
