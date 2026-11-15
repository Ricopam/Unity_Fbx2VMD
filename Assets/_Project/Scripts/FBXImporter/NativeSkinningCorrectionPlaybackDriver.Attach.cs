using System;
using System.Collections.Generic;
using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    internal sealed partial class NativeSkinningCorrectionPlaybackDriver
    {
        internal static bool TryAttach( GameObject host, Animator animator, HumanoidMotionPlaybackController playbackController, out NativeSkinningCorrectionPlaybackDriver driver, out string errorMessage)
        {
            driver = null;
            errorMessage = string.Empty;
            if (host == null || animator == null || playbackController == null || !playbackController.IsPrepared)
            {
                errorMessage = "Native 보정 표시기를 구성할 재생 대상이 없습니다.";
                return false; }
            try
            {
                if (!TryBuildContracts(animator, out List<NativeSkinningSurfaceContract> contracts))
                {
                    errorMessage = "팔 표면의 Native 보정 계약을 만들지 못했습니다.";
                    return false; }
                if (contracts == null)
                {
                    return true; }
                driver = host.GetComponent<NativeSkinningCorrectionPlaybackDriver>();
                if (driver == null)
                {
                    driver = host.AddComponent<NativeSkinningCorrectionPlaybackDriver>();
                    driver.hideFlags = HideFlags.DontSave; }
                driver.Configure(animator, playbackController, contracts.ToArray());
                return true; } catch (Exception exception)
            {
                errorMessage = $"Native 보정 표시기 구성 중 예외가 발생했습니다: {exception.Message}";
                if (driver != null)
                {
                    driver.Release();
                    driver = null; }
                return false; } }

        // contracts==null이면 선택 없음(성공), false+비null이면 계약 생성 실패.
        static bool TryBuildContracts(
            Animator animator,
            out List<NativeSkinningSurfaceContract> contracts)
        {
            contracts = null;
            NativeSkinningArmSurfaceSelection[] selections =
                NativeSkinningArmSurfaceSelector.FindAll(animator);
            if (selections.Length == 0)
            {
                return true;
            }
            contracts = new List<NativeSkinningSurfaceContract>(selections.Length);
            foreach (NativeSkinningArmSurfaceSelection selection in selections)
            {
                if (!NativeSkinningSurfaceContractBuilder.TryBuild(
                        selection,
                        out NativeSkinningSurfaceContract contract))
                {
                    return false;
                }
                contracts.Add(contract);
            }
            return true;
        }

    }
}
