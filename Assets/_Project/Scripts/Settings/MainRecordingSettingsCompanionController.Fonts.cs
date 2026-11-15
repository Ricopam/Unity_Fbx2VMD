using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fbx2Vmd.Settings
{
    public sealed partial class MainRecordingSettingsCompanionController
    {

        private bool HasReadableKoreanTextForTests()
        {
            ApplyReadableKoreanFonts();
            TextMeshProUGUI[] labels = GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (TextMeshProUGUI label in labels)
            {
                if (!KoreanUiTextFallback.IsReadable(label))
                {
                    return false;
                }
            }

            return true;
        }


        private void ApplyReadableKoreanFonts()
        {
            TextMeshProUGUI[] labels = GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (TextMeshProUGUI label in labels)
            {
                KoreanUiTextFallback.Apply(label);
            }
        }

    }
}
