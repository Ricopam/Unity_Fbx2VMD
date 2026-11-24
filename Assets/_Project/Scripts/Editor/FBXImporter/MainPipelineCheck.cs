using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Fbx2Vmd.CharacterLibrary;
using Fbx2Vmd.ClothPhysics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// Main 씬 실제 사용 흐름 점검. 빈 무대에서 라이브러리 YYB 선택 → satisfaction_2 적용 →
    /// 하체·엄지·표면 보정과 의상 물리·머리카락 스프링 상태 확인 → 10초 영상 녹화 → 저장 영상 확인을
    /// 한 번에 실행하고 결과를 Docs/Workflow/Local/evidence/main-pipeline-check 아래에 남김.
    /// 도메인 리로드가 꺼진 Play 설정을 전제로 정적 상태로 진행함.
    /// </summary>
    internal static class MainPipelineCheck
    {
        private const string MainScenePath = "Assets/_Project/Scene/Main.unity";
        private const string CharacterSourceAssetPath =
            "Assets/_Project/Model/YYB Hatsune Miku_default/YYB Hatsune Miku_default_1.0ver.fbx";
        private const string MotionAssetPath = "Assets/Resources/Import_FBX/satisfaction_2.fbx";
        private const float RecordStartSeconds = 10f;
        private const float RecordLengthSeconds = 10f;
        private const float CharacterSettleSeconds = 3f;
        // 단계별 대기 한도(초). 표면 보정 계산을 처음부터 하면 녹화 시작까지 6분 넘게 걸림.
        private const float EnteringPlayLimitSeconds = 60f;
        private const float CharacterLimitSeconds = 180f;
        private const float ImportLimitSeconds = 300f;
        private const float RecordingLimitSeconds = 900f;
        private const float VideoFileAppearLimitSeconds = 60f;
        private const float VideoFileUnlockLimitSeconds = 120f;
        private const int MaxStoredErrorLogs = 20;

        private enum Stage { EnteringPlay, SelectingCharacter, WaitingCharacter, Importing, Recording, VerifyingVideo }

        private static Stage _stage;
        private static MainPipelineCheckReport _report;
        private static DateTime _startedUtc;
        private static DateTime _stageStartedUtc;
        private static DateTime _characterSeenUtc;
        private static FBXVmdPipeline _pipeline;
        private static GameObject _character;
        private static System.Threading.Tasks.Task<bool> _selectTask;
        private static bool _isRunning;

        internal static bool IsRunning => _isRunning;

        [MenuItem("Tools/Main/파이프라인 점검 실행 (YYB · satisfaction_2)")]
        private static void RunFromMenu()
        {
            if (!TryStart(out string message))
                EditorUtility.DisplayDialog("Main 파이프라인 점검", message, "확인");
        }

        /// <summary>편집 모드의 저장된 Main 씬에서만 시작함. 시작하지 못하면 이유를 돌려줌.</summary>
        internal static bool TryStart(out string message)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (_isRunning || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                message = "이미 실행 중이거나 Play 상태입니다. Play를 멈춘 뒤 다시 실행하세요.";
                return false;
            }
            if (scene.path != MainScenePath || scene.isDirty)
            {
                message = "저장된 Main 씬을 연 상태에서 실행하세요.";
                return false;
            }
            if (EditorSettings.enterPlayModeOptionsEnabled == false ||
                !EditorSettings.enterPlayModeOptions.HasFlag(EnterPlayModeOptions.DisableDomainReload))
            {
                message = "Play 진입 시 도메인 리로드가 꺼져 있어야 점검을 이어갈 수 있습니다.";
                return false;
            }

            _report = new MainPipelineCheckReport { started_at = DateTime.Now.ToString("o", CultureInfo.InvariantCulture) };
            _startedUtc = DateTime.UtcNow;
            _isRunning = true;
            EnterStage(Stage.EnteringPlay);
            Application.logMessageReceived += CollectErrorLog;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
            message = "점검을 시작했습니다.";
            return true;
        }

        private static void Tick()
        {
            try
            {
                if (StageSeconds > StageLimitSeconds(_stage))
                    throw new TimeoutException($"{_stage} 단계가 {StageLimitSeconds(_stage)}초 안에 끝나지 않았습니다.");
                if (_stage != Stage.EnteringPlay && !EditorApplication.isPlaying)
                    throw new InvalidOperationException("점검 중 Play가 종료되었습니다.");
                if (_pipeline != null && _pipeline.SessionState == FBXVmdPipeline.FBXSessionState.Failed)
                    throw new InvalidOperationException("파이프라인 실패: " + _pipeline.LastSessionMessage);
                switch (_stage)
                {
                    case Stage.EnteringPlay: TickEnteringPlay(); break;
                    case Stage.SelectingCharacter: TickSelectingCharacter(); break;
                    case Stage.WaitingCharacter: TickWaitingCharacter(); break;
                    case Stage.Importing: TickImporting(); break;
                    case Stage.Recording: TickRecording(); break;
                    case Stage.VerifyingVideo: TickVerifyingVideo(); break;
                }
            }
            catch (Exception error)
            {
                Fail(error.Message);
            }
        }

        private static void TickEnteringPlay()
        {
            if (!EditorApplication.isPlaying)
                return;
            _pipeline = UnityEngine.Object.FindFirstObjectByType<FBXVmdPipeline>();
            if (_pipeline == null)
                throw new InvalidOperationException("Main 씬에 FBXVmdPipeline이 없습니다.");
            if (_pipeline.targetCharacter != null)
                throw new InvalidOperationException("Main 씬이 빈 무대로 시작하지 않았습니다.");
            EnterStage(Stage.SelectingCharacter);
        }

        private static void TickSelectingCharacter()
        {
            CharacterLibraryCoordinator coordinator = CharacterLibraryCoordinator.Current;
            if (coordinator == null)
                return;
            // 라이브러리는 등록 경로를 그대로 비교하므로 구분자를 운영체제 형식으로 맞춤.
            string sourcePath = Path.GetFullPath(Path.Combine(ProjectRoot, CharacterSourceAssetPath));
            CharacterLibraryEntry entry = coordinator.Registry.FindBySourcePath(sourcePath);
            if (entry == null)
                throw new InvalidOperationException("캐릭터 라이브러리에 YYB가 등록되어 있지 않습니다.");
            _selectTask = coordinator.SelectCharacterAsync(entry.id);
            EnterStage(Stage.WaitingCharacter);
        }

        // 로드 직후 물리 자동 연결까지 끝나도록 캐릭터가 나타난 뒤 잠시 기다리고 상태를 기록함.
        private static void TickWaitingCharacter()
        {
            bool isSelectFailed = _selectTask != null && _selectTask.IsCompleted &&
                (_selectTask.IsFaulted || !_selectTask.Result);
            if (isSelectFailed)
                throw new InvalidOperationException("캐릭터 불러오기 실패: " + CharacterLibraryCoordinator.Current?.LastError);
            if (_character == null)
            {
                _character = _pipeline.targetCharacter;
                _characterSeenUtc = DateTime.UtcNow;
            }
            if (_character == null || (DateTime.UtcNow - _characterSeenUtc).TotalSeconds < CharacterSettleSeconds)
                return;
            RecordCharacterState();
            if (!_pipeline.TryStartFbxImportFromSharedSettings(MotionAssetPath))
                throw new InvalidOperationException("FBX 적용 요청이 거부되었습니다.");
            EnterStage(Stage.Importing);
        }

        private static void TickImporting()
        {
            if (_pipeline.IsProcessing || !_pipeline.HasPreparedImportedMotion)
                return;
            float frameRate = _pipeline.ImportedMotionFrameRate;
            int startFrame = Mathf.RoundToInt(RecordStartSeconds * frameRate);
            int endFrame = startFrame + Mathf.RoundToInt(RecordLengthSeconds * frameRate);
            _report.expected_frames = Mathf.RoundToInt(RecordLengthSeconds * (float)_pipeline.motionVideoFrameRate);
            _pipeline.SetMotionVideoRecordingOptionsForEvidence(false, true, startFrame, endFrame, ReportDirectory);
            // 녹화 시작 요청은 표면 보정 준비가 끝난 뒤 실제 녹화로 이어짐.
            if (!_pipeline.TryStartImportedMotionRecording())
                throw new InvalidOperationException("녹화 시작 요청이 거부되었습니다.");
            EnterStage(Stage.Recording);
        }

        private static void TickRecording()
        {
            if (!_pipeline.IsImportedMotionRecording)
            {
                bool isStillPreparing = _pipeline.IsPreparingImportedMotionCorrection ||
                    _pipeline.MotionRecordingController == null || _report.playback_samples == 0;
                if (isStillPreparing)
                    return;
                _report.recorded_frames = _pipeline.MotionRecordingController.RecordedPoseTicks;
                _report.video_path = _pipeline.MotionRecordingController.OutputFilePath;
                EnterStage(Stage.VerifyingVideo);
                return;
            }
            if (_report.playback_samples == 0)
                _report.correction_prepare_seconds = StageSeconds;
            SamplePlayback();
        }

        // 녹화가 끝나도 인코더가 파일을 닫을 때까지 걸리므로 완성될 때까지 기다림.
        private static void TickVerifyingVideo()
        {
            string path = _report.video_path;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                if (StageSeconds > VideoFileAppearLimitSeconds)
                    throw new FileNotFoundException("녹화 영상 파일이 없습니다: " + path);
                return;
            }
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    _report.video_size_bytes = stream.Length;
                    _report.is_video_finalized = MainPipelineCheckReport.IsFinalizedMp4(stream);
                }
            }
            catch (IOException) when (StageSeconds < VideoFileUnlockLimitSeconds)
            {
                return;
            }
            Finish();
        }

        private static void RecordCharacterState()
        {
            _report.character_name = _character.name;
            var materials = _character.GetComponentsInChildren<Renderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null).Distinct().ToList();
            _report.material_count = materials.Count;
            _report.textured_material_count = materials.Count(material =>
                material.HasProperty("_MainTex") && material.mainTexture != null);
            var setup = _character.GetComponent<CharacterPhysicsSetup>();
            _report.cloth_count = setup != null ? setup.generatedCloths.Count : 0;
            _report.valid_cloth_count = setup != null ? setup.generatedCloths.Count(cloth => cloth != null && cloth.IsValid()) : 0;
            _report.hair_spring_count = _character.GetComponentsInChildren<SpringHairDynamics>(true).Length;
        }

        private static void SamplePlayback()
        {
            _report.playback_samples++;
            // 녹화 중에는 발 표면 스냅샷은 건너뛰지만 접지 상태 값은 그대로 채워짐.
            _pipeline.TryCaptureImportedMotionFootSurface(out _, out _, out HumanoidFootGroundingStatus status, out _);
            if (status == HumanoidFootGroundingStatus.Applied)
                _report.grounded_samples++;
            var thumbGuard = _character.GetComponent<HumanoidThumbDeformationGuard>();
            _report.thumb_guard_enabled = thumbGuard != null && thumbGuard.isActiveAndEnabled;
            _report.surface_correction_enabled = _pipeline.IsNativeSkinningCorrectionEnabled;
            _report.surface_correction_failure = _pipeline.LastNativeSkinningCorrectionFailure ?? string.Empty;
            foreach (var renderer in _character.GetComponentsInChildren<Renderer>())
                if (float.IsNaN(renderer.bounds.center.x))
                    _report.has_nan_bounds = true;
        }

        private static void CollectErrorLog(string message, string stackTrace, LogType type)
        {
            bool isError = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            // 외부 에디터 연결 도구의 접속 로그는 제품 오류가 아님.
            bool isToolNoise = message != null && message.Contains("MCP-FOR-UNITY");
            if (!isError || isToolNoise || _report == null)
                return;
            _report.error_log_count++;
            if (_report.error_logs.Count < MaxStoredErrorLogs)
                _report.error_logs.Add(message);
        }

        private static void Fail(string message)
        {
            _report.failed_stage = _stage.ToString();
            _report.failure_message = message;
            Finish();
        }

        // 보고서 쓰기가 실패해도 다음 점검이 막히지 않게 정리는 항상 실행함.
        private static void Finish()
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= CollectErrorLog;
            try
            {
                _report.finished_at = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
                _report.Evaluate();
                Directory.CreateDirectory(ReportDirectory);
                string reportPath = Path.Combine(ReportDirectory, "report.json");
                File.WriteAllText(reportPath, JsonUtility.ToJson(_report, true));
                string summary = _report.passed
                    ? "합격"
                    : "불합격 - " + string.Join(" / ", _report.failures);
                Debug.Log($"[MainPipelineCheck] {summary}. 보고서: {reportPath}");
            }
            finally
            {
                _isRunning = false;
                _reportDirectory = null;
                _pipeline = null;
                _character = null;
                _selectTask = null;
                if (EditorApplication.isPlaying)
                    EditorApplication.isPlaying = false;
            }
        }

        private static float StageLimitSeconds(Stage stage)
        {
            switch (stage)
            {
                case Stage.EnteringPlay: return EnteringPlayLimitSeconds;
                case Stage.SelectingCharacter:
                case Stage.WaitingCharacter: return CharacterLimitSeconds;
                case Stage.Importing: return ImportLimitSeconds;
                case Stage.Recording: return RecordingLimitSeconds;
                default: return VideoFileUnlockLimitSeconds + VideoFileAppearLimitSeconds;
            }
        }

        private static void EnterStage(Stage stage)
        {
            _stage = stage;
            _stageStartedUtc = DateTime.UtcNow;
        }

        private static float StageSeconds => (float)(DateTime.UtcNow - _stageStartedUtc).TotalSeconds;

        private static string ProjectRoot => Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;

        private static string _reportDirectory;

        // 실행마다 시작 시각 폴더에 보고서와 영상을 함께 둠.
        private static string ReportDirectory => _reportDirectory ??= Path.Combine(ProjectRoot, "Docs", "Workflow", "Local",
            "evidence", "main-pipeline-check", _startedUtc.ToLocalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));
    }
}
