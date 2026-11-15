using System;
using Fbx2Vmd.FBXImporter;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Fbx2Vmd.Settings
{
    /// <summary>
    /// 기존 FBX 임포트 버튼과 같은 모양의 재생 제어를 Play Mode에 구성함.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed partial class HumanoidMotionPlaybackControlsView : MonoBehaviour
    {
        private const string ImportButtonName = "FBX_Button";
        private const string LegacyRecordButtonName = "MMD_Record_Button";
        private const string RecordButtonName = "FBX_Record_Button";
        private const string PlayPauseButtonName = "FBX_PlayPause_Button";
        private const string StopButtonName = "FBX_Stop_Button";
        private const string TimelineSliderName = "FBX_Timeline_Slider";
        private const string TimelineLabelName = "FBX_Timeline_Label";
        private const float ButtonVerticalSpacing = 100f;
        private const float ControlSideMargin = 20f;
        private const float ControlBottomMargin = 100f;
        private const float TimelineHeight = 36f;
        private const float TimelineBottomMargin = 170f;
        private const float MinimumTimelineFontSize = 12f;

        private FBXVmdPipeline _pipeline;
        private Button _legacyRecordButton;
        private bool _wasLegacyRecordButtonActive;
        private Button _recordButton;
        private Button _playPauseButton;
        private Button _stopButton;
        private Slider _timelineSlider;
        private TMP_Text _timelineLabel;
        private Text _legacyTimelineLabel;

        internal static HumanoidMotionPlaybackControlsView Ensure(
            FBXVmdPipeline pipeline,
            Button importButton = null)
        {
            if (pipeline == null)
            {
                return null;
            }

            HumanoidMotionPlaybackControlsView view =
                pipeline.GetComponent<HumanoidMotionPlaybackControlsView>();
            if (view != null)
            {
                view.Bind(pipeline);
                view.RebindCallbacks(importButton);
                return view;
            }
            Button template = importButton ?? ResolveImportButton();
            if (template == null)
            {
                Debug.LogWarning(
                    $"[FBXImport] 재생 제어를 만들 {ImportButtonName}을 찾지 못했습니다.");
                return null;
            }
            view = pipeline.gameObject.AddComponent<HumanoidMotionPlaybackControlsView>();
            view.Build(pipeline, template);
            return view;
        }

        private void Update()
        {
            Refresh();
        }

        private void OnDestroy()
        {
            DestroyControlButton(_playPauseButton);
            DestroyControlButton(_recordButton);
            DestroyControlButton(_stopButton);
            DestroyControlObject(_timelineSlider);
            if (_legacyRecordButton != null)
            {
                _legacyRecordButton.gameObject.SetActive(_wasLegacyRecordButtonActive);
            }
        }

        private void Build(FBXVmdPipeline pipeline, Button template)
        {
            Bind(pipeline);
            _legacyRecordButton = FindButton( template.transform.parent, LegacyRecordButtonName);
            if (_legacyRecordButton != null)
            {
                _wasLegacyRecordButtonActive = _legacyRecordButton.gameObject.activeSelf;
                _legacyRecordButton.gameObject.SetActive(false);
            }

            // 조작 버튼은 우하단에 위에서 아래로 쌓고, 타임라인은 좌하단에 둬
            // 상단·중앙의 캐릭터 표시 영역과 겹치지 않게 함.
            _recordButton = CreateControlButton( template, RecordButtonName, "녹화", ControlBottomMargin + ButtonVerticalSpacing * 2f, HandleRecordClick);
            _playPauseButton = CreateControlButton( template, PlayPauseButtonName, "재생", ControlBottomMargin + ButtonVerticalSpacing, HandlePlayPauseClick);
            _stopButton = CreateControlButton( template, StopButtonName, "정지", ControlBottomMargin, HandleStopClick);
            _timelineSlider = CreateTimelineSlider( template, HandleTimelineValueChanged, out _timelineLabel, out _legacyTimelineLabel);
            Refresh();
        }

        private void Bind(FBXVmdPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        private void RebindCallbacks(Button importButton)
        {
            Transform parent = importButton != null
                ? importButton.transform.parent
                : _playPauseButton != null
                    ? _playPauseButton.transform.parent : ResolveImportButton()?.transform.parent;
            _playPauseButton ??= FindButton(parent, PlayPauseButtonName);
            _recordButton ??= FindButton(parent, RecordButtonName);
            _stopButton ??= FindButton(parent, StopButtonName);
            _timelineSlider ??= FindSlider(parent, TimelineSliderName);
            ResolveTimelineLabel();
            if (_playPauseButton != null)
            {
                _playPauseButton.onClick = new Button.ButtonClickedEvent();
                _playPauseButton.onClick.AddListener(HandlePlayPauseClick); }
            if (_stopButton != null)
            {
                _stopButton.onClick = new Button.ButtonClickedEvent();
                _stopButton.onClick.AddListener(HandleStopClick); }
            if (_recordButton != null)
            {
                _recordButton.onClick = new Button.ButtonClickedEvent();
                _recordButton.onClick.AddListener(HandleRecordClick); }
            if (_timelineSlider != null)
            {
                _timelineSlider.onValueChanged = new Slider.SliderEvent();
                _timelineSlider.onValueChanged.AddListener(HandleTimelineValueChanged); }
            Refresh(); }

        private void HandlePlayPauseClick()
        {
            if (_pipeline == null)
            {
                return;
            }

            if (_pipeline.IsImportedMotionPlaying)
            {
                _pipeline.TryPauseImportedMotion();
            }
            else
            {
                _pipeline.TryPlayImportedMotion();
            }

            Refresh();
        }

        private void HandleStopClick()
        {
            _pipeline?.TryStopImportedMotion();
            Refresh();
        }

        private void HandleRecordClick()
        {
            if (_pipeline == null)
            {
                return;
            }

            if (_pipeline.IsImportedMotionRecording)
            {
                _pipeline.TryStopImportedMotionRecording();
            }
            else
            {
                _pipeline.TryStartImportedMotionRecording();
            }

            Refresh();
        }

        private void HandleTimelineValueChanged(float frameValue)
        {
            if (_pipeline == null || !_pipeline.HasPreparedImportedMotion)
            {
                return;
            }

            _pipeline.TrySeekImportedMotionFrame(Mathf.RoundToInt(frameValue));
            Refresh();
        }

        private void Refresh()
        {
            bool hasPreparedMotion = _pipeline != null && _pipeline.HasPreparedImportedMotion;
            bool isRecording = _pipeline != null && _pipeline.IsImportedMotionRecording;
            bool isPreparingCorrection = _pipeline != null && _pipeline.IsPreparingImportedMotionCorrection;
            if (_playPauseButton != null)
            {
                _playPauseButton.interactable = hasPreparedMotion && !isRecording && !isPreparingCorrection;
                SetLabel( _playPauseButton, isPreparingCorrection ? "보정 준비 중" : _pipeline != null && _pipeline.IsImportedMotionPlaying ? "일시정지" : "재생"); }
            if (_stopButton != null)
            {
                _stopButton.interactable = hasPreparedMotion;
            }
            if (_recordButton != null)
            {
                _recordButton.interactable = hasPreparedMotion && !isPreparingCorrection;
                SetLabel(_recordButton, isRecording ? "녹화 중지" : "녹화");
            }
            if (_timelineSlider != null)
            {
                _timelineSlider.interactable = hasPreparedMotion && !isRecording && !isPreparingCorrection;
                _timelineSlider.minValue = 0f;
                _timelineSlider.maxValue = hasPreparedMotion
                    ? _pipeline.ImportedMotionLastFrameIndex
                    : 0f;
                _timelineSlider.SetValueWithoutNotify( hasPreparedMotion ? _pipeline.ImportedMotionCurrentFrameIndex : 0f); }
            SetTimelineLabel(hasPreparedMotion, isPreparingCorrection);
        }

        private void SetTimelineLabel(
            bool hasPreparedMotion,
            bool isPreparingCorrection)
        {
            string label = isPreparingCorrection
                ? $"표면 보정 " +
                  $"{_pipeline.ImportedMotionCorrectionProcessedFrameCount}/" +
                  $"{_pipeline.ImportedMotionCorrectionTotalFrameCount}\n" +
                  $"{_pipeline.ImportedMotionCorrectionPreparationProgress:P0}"
                : hasPreparedMotion
                ? $"프레임 {_pipeline.ImportedMotionCurrentFrameIndex}/" +
                  $"{_pipeline.ImportedMotionLastFrameIndex}\n" +
                  $"{FormatTime(_pipeline.ImportedMotionCurrentTimeSeconds)}/" +
                  FormatTime(_pipeline.ImportedMotionClipLengthSeconds)
                : "프레임 - / -";
            if (_timelineLabel != null)
            {
                _timelineLabel.text = label;
                if (_timelineLabel is TextMeshProUGUI timelineLabel)
                {
                    KoreanUiTextFallback.Apply(timelineLabel);
                }
            }
            else if (_legacyTimelineLabel != null)
            {
                _legacyTimelineLabel.text = label;
                ApplyLegacyKoreanFont(_legacyTimelineLabel);
            } }

        private static Button CreateControlButton(
            Button template, string objectName, string label, float verticalOffset, UnityEngine.Events.UnityAction onClick)
        {
            Button button = Instantiate(template, template.transform.parent);
            button.name = objectName;
            button.gameObject.hideFlags = HideFlags.DontSave;
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(onClick);
            if (button.transform is RectTransform rectTransform)
            {
                rectTransform.anchorMin = new Vector2(1f, 0f);
                rectTransform.anchorMax = new Vector2(1f, 0f);
                rectTransform.pivot = new Vector2(1f, 0f);
                rectTransform.anchoredPosition = new Vector2(-ControlSideMargin, verticalOffset);
                rectTransform.SetAsLastSibling(); }
            TMP_Text tmpLabel = button.GetComponentInChildren<TMP_Text>(true);
            if (tmpLabel != null)
            {
                tmpLabel.enableAutoSizing = true;
                tmpLabel.fontSizeMin = 18f; } else
            {
                Text legacyLabel = button.GetComponentInChildren<Text>(true);
                if (legacyLabel != null)
                {
                    legacyLabel.resizeTextForBestFit = true;
                    legacyLabel.resizeTextMinSize = 18; } }
            SetLabel(button, label);
            return button; }

        private static void SetLabel(Button button, string label)
        {
            TMP_Text tmpLabel = button.GetComponentInChildren<TMP_Text>(true);
            if (tmpLabel != null)
            {
                tmpLabel.text = label;
                if (tmpLabel is TextMeshProUGUI tmpLabelUi)
                {
                    KoreanUiTextFallback.Apply(tmpLabelUi);
                }
                return;
            }

            Text legacyLabel = button.GetComponentInChildren<Text>(true);
            if (legacyLabel != null)
            {
                legacyLabel.text = label;
                ApplyLegacyKoreanFont(legacyLabel);
            }
        }

        private static void ApplyLegacyKoreanFont(Text label)
        {
            if (label != null &&
                KoreanUiFontResolver.ContainsKorean(label.text) &&
                KoreanUiFontResolver.TryGetLegacyFont(out Font koreanFont))
            {
                label.font = koreanFont;
            }
        }

    }
}
