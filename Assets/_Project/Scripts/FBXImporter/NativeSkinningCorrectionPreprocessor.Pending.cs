using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class NativeSkinningCorrectionPreprocessSession
    {


        private readonly struct PendingRendererFrame
        {
            internal PendingRendererFrame(
                RendererWork work,
                NativeSkinningCorrectionFrame frame)
            {
                Work = work;
                Frame = frame;
            }

            internal RendererWork Work { get; }

            internal NativeSkinningCorrectionFrame Frame { get; }
        }
    }
}