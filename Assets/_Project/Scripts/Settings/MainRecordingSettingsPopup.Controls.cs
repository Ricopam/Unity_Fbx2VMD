using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Fbx2Vmd.Settings
{
    public sealed partial class MainRecordingSettingsPopup
    {

        public void OnBeginDrag(PointerEventData eventData)
        {
            EnsureBuilt();
            dragStartAnchoredPosition = panelRoot.anchoredPosition;
            dragStartPointerPosition = eventData.position;
        }

        public void OnDrag(PointerEventData eventData)
        {
            EnsureBuilt();
            Vector2 pointerDelta = eventData.position - dragStartPointerPosition;
            panelRoot.anchoredPosition = dragStartAnchoredPosition + pointerDelta;
        }

        private void SetVisible(bool visible)
        {
            if (canvasGroup == null)
            {
                canvasGroup = gameObject.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                {
                    canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }

            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
        }

        private void BindCloseButton(Button button)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(Close);
        }

        private void BindCardButton(Button button, MainRecordingSettingsActionType action)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => HandleCardAction(action));
        }

        private void HandleCardAction(MainRecordingSettingsActionType action)
        {
            if (action == MainRecordingSettingsActionType.Close)
            {
                Close();
                return;
            }

            MainRecordingSettingsActions.Execute(action, recordingSetting, fileManager, ShowNotification);
        }

        private void ShowNotification(string message)
        {
            EnsureBuilt();
            notificationText.text = message;
            KoreanUiTextFallback.Apply(notificationText);
        }

        private static Rect RectFull()
        {
            return new Rect(
                0f,
                0f,
                MainRecordingSettingsLayoutSpec.ReferenceWidth,
                MainRecordingSettingsLayoutSpec.ReferenceHeight);
        }

        private static RectTransform EnsureRectTransform(GameObject target)
        {
            RectTransform rectTransform = target.GetComponent<RectTransform>();
            return rectTransform != null ? rectTransform : target.AddComponent<RectTransform>();
        }

    }
}
