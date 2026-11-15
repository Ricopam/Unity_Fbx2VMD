using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MagicaCloth2;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Fbx2Vmd.ClothPhysics
{
    public partial class ClothClipValidationRunner : MonoBehaviour
    {

        [System.Serializable]
        public class ClipResult
        {
            public string clip;
            public int sampledFrames;
            public float worstExcessPenetrationMm;
            public int warningFrames;
            public int suspectedStuckBones;
            public float baselineWorstMm;
            public int validCloths;
            public float avgFrameMs;
            public string golden;
        }
        [System.Serializable]
        public class Report { public string character; public List<ClipResult> clips = new List<ClipResult>(); }

    }
}