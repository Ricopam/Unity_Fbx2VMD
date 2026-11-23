using System;
using System.IO;
using Fbx2Vmd.FBXImporter;
using Fbx2Vmd.Settings;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 직렬화 설정 표면 — 본체에서 이관한 데이터 홀더 partial.
/// </summary>
public sealed partial class RecordingSetting
{
    [Header("수동 녹화")]
    [InspectorName("녹화 FBXVmdPipeline")]
    [Tooltip("FBX 처리 상태와 VMD 녹화 파이프라인을 보유한 FBXVmdPipeline입니다.")]
    [SerializeField] private FBXVmdPipeline recordingFBXVmdPipeline;

    [InspectorName("수동 녹화 버튼")]
    [Tooltip("GameView에서 수동 녹화를 시작하는 UI 버튼입니다.")]
    [SerializeField] private Button manualRecordButton;

    [InspectorName("녹화 대상")]
    [Tooltip("실제 수동 녹화를 시작할 HumanoidSampleCode입니다.")]
    [SerializeField] private HumanoidSampleCode recordingController;

    [Header("화면 녹화 진단")]
    [InspectorName("녹화 진단/캡처 사용")]
    [Tooltip("비교 CSV와 프레임 캡처를 남기는 진단 모드를 켭니다.")]
    [SerializeField] private bool enableRecordingDiagnostics;

    [InspectorName("테스트용 30fps 시간 고정")]
    [Tooltip("회귀 테스트용으로 녹화 중 Unity 시간을 30fps로 고정합니다.")]
    [SerializeField] private bool useDeterministicCaptureFramerateForDiagnostics;

    [InspectorName("손 close-up 캡처")]
    [Tooltip("진단 캡처를 사용할 때 손 close-up 캡처도 함께 남깁니다.")]
    [SerializeField] private bool enableDiagnosticFingerCloseups = true;

    [InspectorName("녹화 캡처 해상도")]
    [Tooltip("MotionComparisonProbe PNG 산출물 해상도입니다. VMD frame/key 품질에는 영향을 주지 않습니다.")]
    [SerializeField] private RecordingCaptureQualityPreset recordingCaptureQuality = RecordingCaptureQualityPreset.Uhd4K;

    [InspectorName("사용자 지정 캡처 폭")]
    [Tooltip("사용자 지정 preset을 선택했을 때 사용할 PNG 캡처 폭입니다.")]
    [SerializeField, Min(RecordingCaptureResolution.MinWidth)] private int customRecordingCaptureWidth = 3840;

    [InspectorName("사용자 지정 캡처 높이")]
    [Tooltip("사용자 지정 preset을 선택했을 때 사용할 PNG 캡처 높이입니다.")]
    [SerializeField, Min(RecordingCaptureResolution.MinHeight)] private int customRecordingCaptureHeight = 2160;

    [InspectorName("실행 시작 시 FBXVmdPipeline에 적용")]
    [Tooltip("Play Mode 시작 시 위 진단 설정을 FBXVmdPipeline에 반영합니다.")]
    [SerializeField] private bool applyDiagnosticsToFBXVmdPipelineOnAwake = true;

    [Header("설정 팝업")]
    [InspectorName("런타임 설정 팝업")]
    [Tooltip("Main·Main_Recoding 실행 중 자동으로 열 설정 팝업입니다. 비어 있으면 UI_Canvas 아래에 생성합니다.")]
    [SerializeField] private MainRecordingSettingsPopup settingsPopup;

    [InspectorName("시작 시 설정 팝업 열기")]
    [Tooltip("빌드 실행 또는 Play Mode 시작 시 설정 팝업을 자동으로 한 번 엽니다.")]
    [SerializeField] private bool openSettingsPopupOnStart = true;

    [Header("공유 설정 파일")]
    [InspectorName("시작 시 공유 설정 로드")]
    [Tooltip("Play 시작 시 companion/Editor mirror가 저장한 공유 설정 파일을 읽습니다.")]
    [SerializeField] private bool loadSharedSettingsOnAwake = true;

    [InspectorName("공유 설정 파일 override")]
    [Tooltip("비워 두면 기본 공유 설정 파일 경로를 사용합니다. 테스트와 진단에서만 직접 지정합니다.")]
    [SerializeField] private string sharedSettingsFilePathOverride;

    [InspectorName("공유 설정 polling 간격")]
    [Tooltip("실행 중 공유 설정 파일의 LastWriteTimeUtc 변경을 확인하는 간격입니다.")]
    [SerializeField, Min(0.1f)] private float sharedSettingsPollingIntervalSeconds = 1f;

    public FBXVmdPipeline RecordingFBXVmdPipeline => recordingFBXVmdPipeline;
    public Button ManualRecordButton => manualRecordButton;
    public HumanoidSampleCode RecordingController => recordingController;
    public bool EnableRecordingDiagnostics => enableRecordingDiagnostics;
    public bool UseDeterministicCaptureFramerateForDiagnostics => useDeterministicCaptureFramerateForDiagnostics;
    public bool EnableDiagnosticFingerCloseups => enableDiagnosticFingerCloseups;
    public RecordingCaptureQualityPreset RecordingCaptureQuality => recordingCaptureQuality;
    public int CustomRecordingCaptureWidth => customRecordingCaptureWidth;
    public int CustomRecordingCaptureHeight => customRecordingCaptureHeight;
    public MainRecordingSettingsPopup SettingsPopup => settingsPopup;
    public bool OpenSettingsPopupOnStart => openSettingsPopupOnStart;
    public bool LoadSharedSettingsOnAwake => loadSharedSettingsOnAwake;
    public float SharedSettingsPollingIntervalSeconds => sharedSettingsPollingIntervalSeconds;
}
