using System;
using System.IO;
using Fbx2Vmd.FBXImporter;
using Fbx2Vmd.Settings;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed partial class RecordingSetting : MonoBehaviour
{
    private const string ManualRecordButtonName = "MMD_Record_Button";



    private void Reset()
    {
        manualRecordButton = ResolveManualRecordButton();
        recordingController = ResolveRecordingController();
        PullDiagnosticsFromFBXVmdPipeline();
    }

    private void HandlePlaybackControlsEnsureRequested(FBXVmdPipeline pipeline)
    {
        if (!showGameViewMotionControls)
        {
            return;
        }

        HumanoidMotionPlaybackControlsView.Ensure(pipeline);
    }

    private void Awake()
    {
        FBXVmdPipeline.PlaybackControlsEnsureRequested += HandlePlaybackControlsEnsureRequested;

        if (Application.isPlaying)
        {
            WriteRuntimePlayModeStateQuietly(MainRecordingSettingsState.Playing);
        }

        if (loadSharedSettingsOnAwake)
        {
            LoadSharedSettings();
        }

        if (applyDiagnosticsToFBXVmdPipelineOnAwake)
        {
            ApplyDiagnosticsToFBXVmdPipeline();
        }
    }

    private void OnDestroy()
    {
        FBXVmdPipeline.PlaybackControlsEnsureRequested -= HandlePlaybackControlsEnsureRequested;

        if (Application.isPlaying)
        {
            WriteRuntimePlayModeStateQuietly(MainRecordingSettingsState.Stopped);
            if (!Application.isEditor)
            {
                MainRecordingSettingsLauncher.CloseStartedProcessQuietly();
            }
        }
    }

    private void Start()
    {
        if (MainRecordingSettingsSurfacePolicy.ShouldOpenRuntimePopupFallback(
                openSettingsPopupOnStart,
                Application.isEditor,
                Application.isBatchMode,
                MainRecordingSettingsLauncher.IsSettingsProcessRunning()))
        {
            OpenSettingsPopup();
        }
    }

    private void Update()
    {
        if (!loadSharedSettingsOnAwake)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (now < nextSharedSettingsPollTime)
        {
            return;
        }

        nextSharedSettingsPollTime = now + Mathf.Max(0.1f, sharedSettingsPollingIntervalSeconds);
        PollSharedSettingsIfChanged();
        WriteMotionStateQuietly();
    }

    public void StartManualRecording()
    {
        FBXVmdPipeline fileManager = recordingFBXVmdPipeline;
        HumanoidSampleCode controller = ResolveRecordingController();

        if (fileManager != null && fileManager.IsProcessing)
        {
            const string message = "FBX 처리 중에는 수동 녹화를 시작할 수 없습니다.";
            controller?.SetProcessingStatus(message, 0.1f);
            Debug.LogWarning($"[RecordingSetting] {message}");
            return;
        }

        if (controller == null)
        {
            Debug.LogWarning("[RecordingSetting] 수동 녹화를 시작할 HumanoidSampleCode가 연결되어 있지 않습니다.");
            return;
        }

        ApplyDiagnosticsToFBXVmdPipeline();
        controller.OnManualRecordButtonClick();
    }

    public void SetRecordingFBXVmdPipeline(FBXVmdPipeline fileManager)
    {
        recordingFBXVmdPipeline = fileManager;
        recordingController = ResolveRecordingController();
    }

    public void SetManualRecordButton(Button button)
    {
        manualRecordButton = button;
    }

    public void SetRecordingController(HumanoidSampleCode controller)
    {
        recordingController = controller;
    }

    public MainRecordingSettingsPopup EnsureSettingsPopup()
    {
        if (settingsPopup == null)
        {
            settingsPopup = MainRecordingSettingsPopupSceneResolver.EnsurePopup(this);
        }

        settingsPopup.Bind(this, recordingFBXVmdPipeline);
        return settingsPopup;
    }

    public void OpenSettingsPopup()
    {
        EnsureSettingsPopup()?.Open();
    }

    public void CloseSettingsPopup()
    {
        if (settingsPopup != null)
        {
            settingsPopup.Close();
        }
    }



    private HumanoidSampleCode ResolveRecordingController()
    {
        if (recordingController != null)
        {
            return recordingController;
        }

        FBXVmdPipeline fileManager = recordingFBXVmdPipeline;
        if (fileManager != null && fileManager.targetCharacter != null)
        {
            recordingController = fileManager.targetCharacter.GetComponent<HumanoidSampleCode>();
        }

        return recordingController;
    }

    private static Button ResolveManualRecordButton()
    {
        GameObject buttonObject = GameObject.Find(ManualRecordButtonName);
        return buttonObject != null ? buttonObject.GetComponent<Button>() : null;
    }

}
