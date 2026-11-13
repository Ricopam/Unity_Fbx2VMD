using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fbx2Vmd.Settings
{
    internal sealed partial class HumanoidMotionPlaybackControlsView
    {
        private static Slider CreateTimelineSlider(
            Button template,
            UnityEngine.Events.UnityAction<float> onValueChanged,
            out TMP_Text timelineLabel,
            out Text legacyTimelineLabel)
        {
            var sliderObject = new GameObject(
                TimelineSliderName,
                typeof(RectTransform),
                typeof(Slider));
            sliderObject.hideFlags = HideFlags.DontSave;
            sliderObject.transform.SetParent(template.transform.parent, false);

            RectTransform sliderRect = sliderObject.GetComponent<RectTransform>();
            if (template.transform is RectTransform templateRect)
            {
                sliderRect.anchorMin = new Vector2(0f, 0f);
                sliderRect.anchorMax = new Vector2(0f, 0f);
                sliderRect.pivot = new Vector2(0f, 0f);
                sliderRect.anchoredPosition =
                    new Vector2(ControlSideMargin, TimelineBottomMargin);
                sliderRect.sizeDelta = new Vector2(
                    Mathf.Max(240f, templateRect.rect.width),
                    TimelineHeight);
                sliderRect.SetAsLastSibling();
            }

            var (fill, handle) = BuildSliderVisuals(sliderRect);

            Slider slider = sliderObject.GetComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 0f;
            slider.wholeNumbers = true;
            slider.onValueChanged.AddListener(onValueChanged);

            BuildTimelineLabelBackground(sliderRect);
            CreateTimelineLabels(template, sliderRect, out timelineLabel, out legacyTimelineLabel);
            return slider;
        }

        private static (Image fill, Image handle) BuildSliderVisuals(RectTransform sliderRect)
        {
            Image background = CreateSliderImage(
                sliderRect,
                "Background",
                new Color32(40, 43, 50, 230));
            SetStretch(background.rectTransform, Vector2.zero, Vector2.zero);

            RectTransform fillArea = CreateSliderRect(sliderRect, "Fill Area");
            SetStretch(fillArea, new Vector2(6f, 6f), new Vector2(-6f, -6f));
            Image fill = CreateSliderImage(
                fillArea,
                "Fill",
                new Color32(56, 204, 190, 255));
            SetStretch(fill.rectTransform, Vector2.zero, Vector2.zero);

            RectTransform handleArea = CreateSliderRect(sliderRect, "Handle Slide Area");
            SetStretch(handleArea, new Vector2(8f, 0f), new Vector2(-8f, 0f));
            Image handle = CreateSliderImage(
                handleArea,
                "Handle",
                new Color32(240, 240, 240, 255));
            handle.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            handle.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            handle.rectTransform.sizeDelta = new Vector2(18f, TimelineHeight + 4f);
            return (fill, handle);
        }

        // 밝은 배경 위에서도 진행 수치가 읽히도록 라벨 뒤에 어두운 배경을 둠.
        private static void BuildTimelineLabelBackground(RectTransform sliderRect)
        {
            Image labelBackground = CreateSliderImage(
                sliderRect,
                "FBX_Timeline_Label_Background",
                new Color32(40, 43, 50, 220));
            labelBackground.raycastTarget = false;
            RectTransform labelBackgroundRect = labelBackground.rectTransform;
            labelBackgroundRect.anchorMin = new Vector2(0f, 1f);
            labelBackgroundRect.anchorMax = new Vector2(1f, 1f);
            labelBackgroundRect.pivot = new Vector2(0.5f, 0f);
            labelBackgroundRect.anchoredPosition = new Vector2(0f, 4f);
            labelBackgroundRect.sizeDelta = new Vector2(16f, TimelineHeight + 8f);
        }

        private static void CreateTimelineLabels(
            Button template, RectTransform sliderRect,
            out TMP_Text timelineLabel, out Text legacyTimelineLabel)
        {
            timelineLabel = null;
            legacyTimelineLabel = null;
            TMP_Text tmpTemplate = template.GetComponentInChildren<TMP_Text>(true);
            if (tmpTemplate != null)
            {
                timelineLabel = Object.Instantiate(tmpTemplate, sliderRect);
                ConfigureTimelineLabel(timelineLabel.rectTransform);
                timelineLabel.name = TimelineLabelName;
                timelineLabel.color = Color.white;
                timelineLabel.raycastTarget = false;
                return;
            }

            Text legacyTemplate = template.GetComponentInChildren<Text>(true);
            if (legacyTemplate != null)
            {
                legacyTimelineLabel = Object.Instantiate(legacyTemplate, sliderRect);
                ConfigureTimelineLabel(
                    legacyTimelineLabel.GetComponent<RectTransform>());
                legacyTimelineLabel.name = TimelineLabelName;
                legacyTimelineLabel.color = Color.white;
                legacyTimelineLabel.raycastTarget = false;
            }
        }

        private static RectTransform CreateSliderRect(RectTransform parent, string name)
        {
            var rectObject = new GameObject(name, typeof(RectTransform));
            rectObject.hideFlags = HideFlags.DontSave;
            rectObject.transform.SetParent(parent, false);
            return rectObject.GetComponent<RectTransform>();
        }

        private static Image CreateSliderImage(
            RectTransform parent,
            string name,
            Color color)
        {
            var imageObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            imageObject.hideFlags = HideFlags.DontSave;
            imageObject.transform.SetParent(parent, false);
            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            return image;
        }

        private static void SetStretch(
            RectTransform rectTransform,
            Vector2 offsetMin,
            Vector2 offsetMax)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = offsetMin;
            rectTransform.offsetMax = offsetMax;
        }

        private static void ConfigureTimelineLabel(RectTransform labelRect)
        {
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = new Vector2(0f, 8f);
            labelRect.sizeDelta = new Vector2(0f, TimelineHeight);

            TMP_Text tmpLabel = labelRect.GetComponent<TMP_Text>();
            if (tmpLabel != null)
            {
                tmpLabel.enableAutoSizing = true;
                tmpLabel.fontSizeMin = MinimumTimelineFontSize;
                tmpLabel.fontSizeMax = Mathf.Max(
                    MinimumTimelineFontSize,
                    tmpLabel.fontSize);
            }

            Text legacyLabel = labelRect.GetComponent<Text>();
            if (legacyLabel != null)
            {
                legacyLabel.resizeTextForBestFit = true;
                legacyLabel.resizeTextMinSize = Mathf.RoundToInt(MinimumTimelineFontSize);
                legacyLabel.resizeTextMaxSize = Mathf.Max(
                    legacyLabel.resizeTextMinSize,
                    legacyLabel.fontSize);
            }
        }
    }
}
