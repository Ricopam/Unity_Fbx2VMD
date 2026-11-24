import {
  DEFAULT_API_BASE_URL,
  DEFAULT_WS_URL,
  createSettingsAutoConnectionController,
  getRuntimeState,
  postImportFbx,
  postMotionCommand
} from "./settingsApi.js";
import { bootstrapCharacterSelectCard } from "./characterSelectCard.js";
import { bootstrapWorkflowPanel } from "./workflowPanel.js";

const statusLabels = {
  idle: "연결 안 됨",
  connecting: "연결 중",
  open: "연결됨",
  closed: "연결 안 됨",
  error: "연결 오류"
};

// 패널 전환 시 상단 헤더에 표시할 제목
const panelTitles = {
  onboarding: "캐릭터",
  "wf-motion": "모션(FBX)",
  "wf-physics": "보정·물리",
  "wf-playback": "재생",
  "wf-recording": "녹화",
  camera: "카메라 파라미터",
  light: "Directional Light & Ambient",
  upperbody: "상체 보정 파라미터",
  lowerbody: "하체 보정 파라미터",
  physics: "캐릭터 물리 파라미터",
  library: "캐릭터 라이브러리 및 프리셋 관리",
  lipsync: "립싱크 및 오디오 파이프라인",
  audioio: "전역 설정 및 I/O 매핑",
  shader: "툰 셰이더 및 무대 룩",
  developer: "개발자 모드"
};

// 패널별 프리셋 셀렉트 옵션 (프로토타입 디자인 기준)
const panelPresets = {
  lowerbody: [
    "프리셋: 기본값 (권장 설정)",
    "발 미끄럼 방지 중심",
    "댄스 모션 보존 모드",
    "사용자 정의"
  ],
  upperbody: [
    "기본값 (표준 리타기팅)",
    "손가락 정밀 표현 (V-Tuber 특화)",
    "모션캡처 고각 회전 소매 보호",
    "YYB 롱슬리브 최적화"
  ],
  physics: [
    "프리셋: 기본값 (권장 설정)",
    "가볍게 / 실시간 성능 우선",
    "고품질 / 디테일 시뮬레이션",
    "사용자 커스텀 세팅"
  ],
  shader: [
    "프리셋: 표준 (lilToon 기본)",
    "세카이풍 (무그림자 얼굴)",
    "무대 강조 (림·백라이트)",
    "사용자 정의"
  ],
  camera: [
    "프리셋: 기본값 (스튜디오 표준)",
    "투명 녹화 (MOV ProRes 4444)",
    "확대 검수 (고품질 렌더)",
    "사용자 정의"
  ],
  light: [
    "프리셋: 무대 조명 (Trilight 권장)",
    "자연광 (Skybox 앰비언트)",
    "플랫 톤 (Flat 앰비언트)",
    "사용자 정의"
  ],
  library: [
    "◆ 기본 프리셋 (Default Stage)",
    "댄스 모션 최적화 프리셋 (High Damping)",
    "어쿠스틱 라이브 프리셋 (Relaxed Spine)",
    "사용자 정의"
  ],
  lipsync: [
    "프리셋: 기본 파이프라인 (권장)",
    "고품질 분리 모드",
    "사용자 정의"
  ],
  audioio: [
    "프리셋: 기본 I/O",
    "사용자 정의"
  ]
};

export function bootstrapSettingsUi(root = document, {
  workbenchLoadTimeoutMs = 15000,
  createWebSocketChannel
} = {}) {
  const elements = {
    shell: root.querySelector(".app-shell"),
    importButton: root.querySelector("#importButton"),
    importStatus: root.querySelector("#importStatus"),
    playMotionButton: root.querySelector("#playMotionButton"),
    pauseMotionButton: root.querySelector("#pauseMotionButton"),
    stopMotionButton: root.querySelector("#stopMotionButton"),
    motionStatus: root.querySelector("#motionStatus"),
    surfaceCorrectionButton: root.querySelector("#surfaceCorrectionButton"),
    statusBadge: root.querySelector("#statusBadge"),
    statusText: root.querySelector("#statusText"),
    feedback: root.querySelector("#feedback"),
    log: root.querySelector("#eventLog"),
    workbenchFrame: root.querySelector("#boogleFrame"),
    workbenchStatus: root.querySelector("#workbenchStatus"),
    workbenchRetry: root.querySelector("#workbenchRetry"),
    workbenchReload: root.querySelector("#workbenchReload")
  };
  let workbenchRequested = false;
  let workbenchLoadTimer = null;
  // 다시 불러오기마다 세대를 올려, 진행 중이던 URL 요청의 늦은 결과가 새 시도를 덮지 못하게 한다.
  let workbenchGeneration = 0;
  let expectedWorkbenchUrl = "";
  elements.workbenchRetry?.addEventListener("click", reloadWorkbench);
  elements.workbenchReload?.addEventListener("click", reloadWorkbench);
  elements.workbenchFrame?.addEventListener("load", () => {
    // about:blank·오류 페이지처럼 기대한 주소가 아닌 탐색의 load는 무시한다.
    if (!expectedWorkbenchUrl ||
        String(elements.workbenchFrame.src).replace(/\/+$/, "") !== expectedWorkbenchUrl) {
      return;
    }
    // 주소를 바꾸기 직전 커밋된 about:blank의 늦은 load는 제외한다. 같은 출처라
    // 읽을 수 있으면 href로 확인하고, 다른 출처면 src 일치만으로 판정한다.
    let loadedHref = null;
    try {
      loadedHref = elements.workbenchFrame.contentWindow?.location?.href ?? null;
    } catch {
      loadedHref = null;
    }
    if (loadedHref != null &&
        String(loadedHref).replace(/\/+$/, "") !== expectedWorkbenchUrl) {
      return;
    }
    clearTimeout(workbenchLoadTimer);
    workbenchLoadTimer = null;
    if (elements.workbenchStatus) {
      elements.workbenchStatus.textContent = "";
      elements.workbenchStatus.hidden = true;
    }
  });
  const panelTargets = Array.from(root.querySelectorAll?.("[data-panel-target]") ?? []);
  const panelViews = Array.from(root.querySelectorAll?.("[data-panel-view]") ?? []);
  const railPageButtons = Array.from(root.querySelectorAll?.("[data-rail-page]") ?? []);
  const sidebarPages = Array.from(root.querySelectorAll?.("[data-sidebar-page]") ?? []);
  const lastPanelByPage = {};
  // 사이드바 항목 없이 레일 버튼이 직접 여는 패널의 소속 페이지
  const panelPageFallback = { onboarding: "assistant" };

  let isSubmitting = false;
  let isPlaying = false;
  let motionState = {};
  let isMotionSubmitting = false;
  let pendingMotionCommandId = "";
  let pendingMotionSuccessMessage = "";
  let motionCommandDeadline = 0;
  let pendingImport = null;
  const characterCard = bootstrapCharacterSelectCard(root, {
    apiBaseUrl: () => getBridgeConfig().apiBaseUrl ?? DEFAULT_API_BASE_URL,
    onFeedback: (message, tone) => {
      setFeedback(message, tone);
      refreshControls();
    },
    onLibraryUnavailable: (error) => appendLog("캐릭터 목록 조회 실패", error.message)
  });
  const workflow = bootstrapWorkflowPanel(root, {
    sendMotionCommand: submitMotionCommand,
    openPath: globalThis.window?.settingsShell?.openPath,
    onFeedback: setFeedback
  });
  const autoConnection = createSettingsAutoConnectionController({
    createWebSocketChannel,
    apiBaseUrl: () => getBridgeConfig().apiBaseUrl ?? DEFAULT_API_BASE_URL,
    wsUrl: () => getBridgeConfig().wsUrl ?? DEFAULT_WS_URL,
    fetchRuntimeState: () => getRuntimeState({
      apiBaseUrl: getBridgeConfig().apiBaseUrl ?? DEFAULT_API_BASE_URL
    }),
    onStatusChange: setConnectionStatus,
    onPlayModeChange: (playMode) => {
      const wasPlaying = isPlaying;
      isPlaying = playMode === "playing";
      refreshControls();
      // 가져오기·캐릭터 불러오기 중 Unity가 멈춰 Play가 끊긴 것처럼 보여도 진행 안내를 덮지 않음.
      const isWaitingForUnity = !!pendingImport || characterCard.isLoading;
      if (wasPlaying !== isPlaying && !isWaitingForUnity) {
        setFeedback(isPlaying
          ? "가져오기 버튼을 누르면 FBX 파일 선택 창이 열립니다."
          : "Unity Play 상태에서 FBX 가져오기를 사용할 수 있습니다.", "info");
      }
    },
    onMessage: (message) => appendLog("수신", message),
    onStateChange: (document) => {
      motionState = document.runtimeState ?? {};
      const progress = resolveImportProgress(pendingImport, motionState, Date.now());
      pendingImport = progress.pending;
      if (progress.feedback) setFeedback(progress.feedback.message, progress.feedback.tone);
      const result = document.lastCommandResult;
      if (pendingMotionCommandId && result?.commandId === pendingMotionCommandId && result.state !== "running") {
        pendingMotionCommandId = "";
        const succeeded = result.state === "success";
        setFeedback(succeeded && pendingMotionSuccessMessage ? pendingMotionSuccessMessage : result.message, succeeded ? "success" : "error");
      } else if (pendingMotionCommandId && Date.now() >= motionCommandDeadline) {
        pendingMotionCommandId = "";
        setFeedback("모션 명령 응답이 지연됩니다. 현재 상태를 확인한 뒤 다시 시도해 주세요.", "error");
      }
      refreshControls();
    },
    onError: () => {
      isPlaying = false;
      motionState = {};
      // 브리지가 끊기면 결과를 받을 수 없으므로 가져오기 대기를 정리해 버튼이 잠긴 채 남지 않게 함.
      pendingImport = null;
      refreshControls();
      setFeedback("Unity 상태 확인 또는 WebSocket 연결에 실패했습니다.", "error");
    }
  });

  setConnectionStatus("idle");
  setFeedback("Unity Play 상태에서 FBX 가져오기를 사용할 수 있습니다.", "info");
  renderLogEmptyState();
  initializePanelSwitching();
  refreshControls();
  autoConnection.start();
  globalThis.window?.addEventListener?.("beforeunload", () => autoConnection.stop());

  for (const [button, action] of [
    [elements.playMotionButton, "PlayImportedMotion"],
    [elements.pauseMotionButton, "PauseImportedMotion"],
    [elements.stopMotionButton, "StopImportedMotion"],
    [elements.surfaceCorrectionButton, () => motionState.isSurfaceCorrectionEnabled === false
      ? "EnableNativeSkinningCorrection" : "DisableNativeSkinningCorrection"]
  ]) {
    button?.addEventListener("click", () => {
      if (button.disabled) return;
      void submitMotionCommand(typeof action === "function" ? action() : action);
    });
  }

  // 모든 모션 명령(재생·녹화·프레임 이동)은 한 번에 하나만 보내고 Unity 회답으로 결과를 표시한다.
  async function submitMotionCommand(action, extra = {}, successMessage = "") {
    if (isMotionSubmitting || pendingMotionCommandId) return false;
    isMotionSubmitting = true;
    refreshControls();
    try {
      const result = await postMotionCommand({
        apiBaseUrl: getBridgeConfig().apiBaseUrl ?? DEFAULT_API_BASE_URL,
        action,
        ...extra
      });
      pendingMotionCommandId = result.commandId;
      pendingMotionSuccessMessage = successMessage;
      motionCommandDeadline = Date.now() + 15000;
      setFeedback("Unity 응답을 기다리는 중입니다.", "loading");
      return true;
    } catch (error) {
      setFeedback(error.message, "error");
      return false;
    } finally {
      isMotionSubmitting = false;
      refreshControls();
    }
  }

  elements.importButton.addEventListener("click", async () => {
    if (!resolveImportGate(motionState, isPlaying, importGateOptions()).canImport) {
      return;
    }

    const chooser = globalThis.window?.settingsShell?.chooseFbxFile;
    if (typeof chooser !== "function") {
      setFeedback("현재 환경에서는 파일 선택 창을 열 수 없습니다.", "error");
      return;
    }

    isSubmitting = true;
    setFeedback("FBX 파일 선택 창을 여는 중입니다.", "loading");
    refreshControls();

    try {
      const selectedPath = await chooser();
      if (!selectedPath) {
        setFeedback("파일 선택을 취소했습니다.", "info");
        return;
      }

      setFeedback("FBX 가져오기 요청을 보내는 중입니다.", "loading");
      const result = await postImportFbx({
        apiBaseUrl: getBridgeConfig().apiBaseUrl ?? DEFAULT_API_BASE_URL,
        fbxPath: selectedPath
      });
      appendLog("HTTP 응답", result);
      pendingImport = {
        sentAtMs: Date.now(),
        sawImporting: false,
        errorAtSend: motionState.motionError || "",
        preparedAtSend: motionState.hasPreparedMotion === true
      };
      setFeedback("FBX 가져오기 요청을 보냈습니다. Unity 처리 결과를 기다리는 중입니다.", "loading");
    } catch (error) {
      setFeedback(error.message, "error");
    } finally {
      isSubmitting = false;
      refreshControls();
    }
  });

  function initializePanelSwitching() {
    if (!elements.shell || panelTargets.length === 0 || panelViews.length === 0) {
      return;
    }

    for (const target of panelTargets) {
      target.addEventListener("click", () => {
        selectPanel(target.dataset.panelTarget);
      });
    }

    for (const button of railPageButtons) {
      button.addEventListener("click", () => {
        const pageName = button.dataset.railPage;
        const defaultPanel = sidebarPages.find(
          (page) => page.dataset.sidebarPage === pageName
        )?.dataset.defaultPanel;
        selectPanel(lastPanelByPage[pageName] || defaultPanel);
      });
    }

    selectPanel(elements.shell.dataset.activePanel || "onboarding");
  }

  function selectPanel(panelName) {
    if (!panelName) {
      return;
    }

    elements.shell.dataset.activePanel = panelName;
    // 라이브러리 패널에서 새로 등록한 캐릭터가 홈 목록에도 보이도록 홈 복귀 때 다시 읽음.
    if (panelName === "onboarding" && isPlaying) void characterCard.refreshLibrary();
    const title = panelTitles[panelName];
    const titleElement = elements.shell.querySelector?.("#settingsTitle");
    if (title && titleElement) {
      titleElement.textContent = title;
    }
    applyPresetOptions(panelName);

    for (const target of panelTargets) {
      const isActive = target.dataset.panelTarget === panelName;
      setPanelTargetState(target, isActive);
    }

    for (const view of panelViews) {
      view.hidden = view.dataset.panelView !== panelName;
    }

    // 선택된 항목이 속한 사이드바 페이지만 표시하고 레일 활성 상태를 맞춘다.
    const activeTarget = panelTargets.find((t) => t.dataset.panelTarget === panelName);
    const pageName = activeTarget?.closest?.("[data-sidebar-page]")?.dataset?.sidebarPage
      ?? panelPageFallback[panelName];
    if (pageName) {
      lastPanelByPage[pageName] = panelName;
      for (const page of sidebarPages) {
        page.hidden = page.dataset.sidebarPage !== pageName;
      }
      for (const button of railPageButtons) {
        button.className = toggleClassName(
          button.className,
          "rail-button-active",
          button.dataset.railPage === pageName
        );
      }
    }

    if (panelName === "developer") {
      void ensureWorkbenchLoaded();
    }
  }

  // 개발자 모드 패널이 처음 열릴 때만 Workbench URL을 가져와 iframe에 탑재한다.
  // 실패하면 다시 시도 버튼을 열어 재기동할 수 있게 한다.
  async function ensureWorkbenchLoaded() {
    if (workbenchRequested || !elements.workbenchFrame || !elements.workbenchStatus) {
      return;
    }
    workbenchRequested = true;
    const generation = workbenchGeneration;
    elements.workbenchStatus.dataset.tone = "info";
    elements.workbenchStatus.hidden = false;
    elements.workbenchStatus.textContent = "Workbench를 시작하는 중입니다.";
    if (elements.workbenchRetry) {
      elements.workbenchRetry.hidden = true;
    }

    const getUrl = globalThis.window?.settingsShell?.getWorkbenchUrl;
    if (typeof getUrl !== "function") {
      showWorkbenchError("현재 환경에서는 Workbench를 사용할 수 없습니다.");
      return;
    }

    try {
      const url = await getUrl();
      // 다시 불러오기로 새 요청이 시작됐다면 늦게 도착한 이전 결과는 버린다.
      if (generation !== workbenchGeneration) {
        return;
      }
      if (!url) {
        showWorkbenchError("Workbench 서버를 시작하지 못했습니다. boogle-sdk 설치를 확인하세요.");
        return;
      }
      expectedWorkbenchUrl = String(url).replace(/\/+$/, "");
      elements.workbenchFrame.src = url;
      elements.workbenchFrame.hidden = false;
      // iframe load가 끝나지 않으면 오류 상태로 전환해 다시 시도 경로를 연다.
      workbenchLoadTimer = setTimeout(() => {
        workbenchLoadTimer = null;
        showWorkbenchError("Workbench 화면 로드 시간이 초과됐습니다.");
      }, workbenchLoadTimeoutMs);
      workbenchLoadTimer.unref?.();
    } catch {
      if (generation === workbenchGeneration) {
        showWorkbenchError("Workbench 주소를 가져오지 못했습니다.");
      }
    }
  }

  // 다시 시도·다시 불러오기는 요청 상태를 초기화하고 iframe을 비운 뒤 URL부터 다시 받는다.
  function reloadWorkbench() {
    workbenchGeneration += 1;
    clearTimeout(workbenchLoadTimer);
    workbenchLoadTimer = null;
    workbenchRequested = false;
    expectedWorkbenchUrl = "";
    if (elements.workbenchFrame) {
      elements.workbenchFrame.hidden = true;
      elements.workbenchFrame.src = "about:blank";
    }
    void ensureWorkbenchLoaded();
  }

  function showWorkbenchError(message) {
    elements.workbenchStatus.dataset.tone = "error";
    elements.workbenchStatus.hidden = false;
    elements.workbenchStatus.textContent = message;
    if (elements.workbenchRetry) {
      elements.workbenchRetry.hidden = false;
    }
  }

  // 활성 패널에 맞는 프리셋 목록을 셀렉트에 채운다.
  function applyPresetOptions(panelName) {
    const select = elements.shell?.querySelector?.("#panelPresetSelect");
    const options = panelPresets[panelName];
    if (!select || !options) return;
    select.innerHTML = "";
    for (const label of options) {
      const option = document.createElement("option");
      option.textContent = label;
      select.appendChild(option);
    }
  }

  function setPanelTargetState(target, isActive) {
    const activeClass = target.className.includes("tree-item") ? "selected" : "active";
    target.className = toggleClassName(target.className, activeClass, isActive);
    if (isActive) {
      target.setAttribute("aria-current", "page");
      return;
    }

    target.removeAttribute("aria-current");
  }

  function importGateOptions() {
    return { isSubmitting, isPending: !!pendingImport, isCharacterLoading: characterCard.isLoading };
  }

  function refreshControls() {
    const importGate = resolveImportGate(motionState, isPlaying, importGateOptions());
    elements.importButton.disabled = !importGate.canImport;
    elements.importButton.textContent = isSubmitting || pendingImport ? "가져오는 중..." : "FBX 가져오기";
    if (elements.importStatus) elements.importStatus.textContent = importGate.message;
    characterCard.update(motionState, isPlaying);
    const controls = resolveMotionControls(motionState, isPlaying, isMotionSubmitting || !!pendingMotionCommandId);
    if (elements.playMotionButton) elements.playMotionButton.disabled = !controls.canPlay;
    if (elements.pauseMotionButton) elements.pauseMotionButton.disabled = !controls.canPause;
    if (elements.stopMotionButton) elements.stopMotionButton.disabled = !controls.canStop;
    if (elements.motionStatus) elements.motionStatus.textContent = controls.message;
    if (elements.surfaceCorrectionButton) {
      elements.surfaceCorrectionButton.disabled = !controls.canStop;
      elements.surfaceCorrectionButton.textContent = motionState.isSurfaceCorrectionEnabled === false ? "표면 보정 켜기" : "표면 보정 끄기";
    }
    workflow.update(motionState, isPlaying, {
      isBusy: isMotionSubmitting || !!pendingMotionCommandId,
      isImportPending: !!pendingImport,
      isCharacterLoading: characterCard.isLoading
    });
  }

  function setConnectionStatus(status) {
    elements.statusBadge.dataset.status = status;
    elements.statusText.textContent = statusLabels[status] ?? status;
  }

  function setFeedback(message, tone) {
    elements.feedback.dataset.tone = tone;
    elements.feedback.textContent = message;
  }

  function appendLog(label, payload) {
    if (elements.log.dataset.empty === "true") {
      elements.log.textContent = "";
      elements.log.dataset.empty = "false";
    }

    const line = document.createElement("div");
    line.className = "log-line";
    line.textContent = `[${new Date().toLocaleTimeString()}] ${label}: ${formatPayload(payload)}`;
    elements.log.prepend(line);
  }

  function renderLogEmptyState() {
    elements.log.dataset.empty = "true";
    elements.log.textContent = "아직 수신한 메시지가 없습니다.";
  }
}

function toggleClassName(className, classToken, enabled) {
  const tokens = new Set(String(className || "").split(/\s+/).filter(Boolean));
  if (enabled) {
    tokens.add(classToken);
  } else {
    tokens.delete(classToken);
  }

  return Array.from(tokens).join(" ");
}

function formatPayload(payload) {
  if (typeof payload === "string") {
    return payload;
  }

  return JSON.stringify(payload);
}

// FBX 가져오기 버튼 가능 여부와 안내. 캐릭터가 없으면 Unity가 실패하므로 먼저 막음.
export function resolveImportGate(state, isUnityPlaying, { isSubmitting = false, isPending = false, isCharacterLoading = false } = {}) {
  const hasCharacter = state.hasTargetCharacter === true;
  const busy = isSubmitting || isPending || isCharacterLoading || state.isImportingFbx === true || state.isRecording === true;
  const message = !isUnityPlaying ? "Unity Play 상태에서 사용할 수 있습니다."
    : isCharacterLoading ? "캐릭터를 불러오는 중입니다."
    : !hasCharacter ? "먼저 1단계에서 캐릭터를 불러와 주세요."
    : state.isImportingFbx === true || isPending ? "Unity가 FBX를 처리하고 있습니다."
    : state.isRecording === true ? "녹화 중에는 FBX를 가져올 수 없습니다."
    : `대상 캐릭터: ${state.targetCharacterName || "캐릭터"}`;
  return { canImport: isUnityPlaying && hasCharacter && !busy, message };
}

// FBX 임포트는 Unity 메인 스레드를 1~2분 멈추게 해 상태 갱신이 끊길 수 있음.
// 그동안은 실패로 보지 않고, 갱신이 살아 있는데도 시작 신호가 없을 때만 실패로 봄.
const IMPORT_START_TIMEOUT_MS = 300000;
// Play 종료·Unity 종료로 응답이 영영 없을 때 대기를 끝내는 전체 한도.
const IMPORT_TOTAL_TIMEOUT_MS = 600000;
const IMPORT_BUSY_MESSAGE = "Unity가 FBX를 불러오고 있습니다. 처리 중에는 Unity가 잠시 멈출 수 있습니다.";

// 가져오기 요청 뒤 Unity 실행 상태로 실제 결과를 판정함.
// 요청 전 오류 문구는 이전 실패일 수 있어 새로 바뀐 오류만 이번 결과로 봄.
export function resolveImportProgress(pending, state, nowMs) {
  if (!pending) return { pending: null, feedback: null };
  if (nowMs - pending.sentAtMs > IMPORT_TOTAL_TIMEOUT_MS) {
    return { pending: null, feedback: { message: "FBX 가져오기 응답이 없습니다. Unity Play 상태와 콘솔을 확인한 뒤 다시 시도해 주세요.", tone: "error" } };
  }
  const busyFeedback = { message: IMPORT_BUSY_MESSAGE, tone: "loading" };
  // 상태 파일이 오래되면 브리지가 기본값(Play 아님)을 돌려줌 — 처리 중 멈춤으로 보고 계속 기다림.
  if (state.playMode !== "playing") {
    return { pending, feedback: busyFeedback };
  }
  const error = state.motionError || "";
  if (error && error !== pending.errorAtSend) {
    return { pending: null, feedback: { message: `FBX 가져오기 실패: ${error}`, tone: "error" } };
  }
  // 처리가 시작됐거나 오류가 비면 이전 오류는 사라진 것이므로, 같은 문구의 새 실패도 잡도록 비움.
  if (state.isImportingFbx === true) {
    return { pending: { ...pending, sawImporting: true, errorAtSend: "" }, feedback: busyFeedback };
  }
  if (!error && pending.errorAtSend) {
    pending = { ...pending, errorAtSend: "" };
  }
  // 멈춘 동안 시작·끝을 모두 놓쳐도, 요청 전에 없던 준비 완료가 생겼으면 이번 가져오기 결과로 봄.
  const isNewlyPrepared = pending.sawImporting || pending.preparedAtSend !== true;
  if (isNewlyPrepared && state.hasPreparedMotion === true) {
    return { pending: null, feedback: { message: "FBX 가져오기 완료. 모션을 재생할 수 있습니다.", tone: "success" } };
  }
  if (!pending.sawImporting && nowMs - pending.sentAtMs > IMPORT_START_TIMEOUT_MS) {
    return { pending: null, feedback: { message: "Unity가 FBX 가져오기를 시작하지 않았습니다. 이벤트 로그와 Unity 콘솔을 확인해 주세요.", tone: "error" } };
  }
  return { pending, feedback: null };
}

export function resolveMotionControls(state, isUnityPlaying, isBusy = false) {
  const ready = isUnityPlaying && state.hasPreparedMotion === true;
  const available = ready && !state.isRecording && !isBusy;
  const time = Number.isFinite(state.motionTimeSeconds) ? Math.max(0, state.motionTimeSeconds).toFixed(1) : "0.0";
  const message = !isUnityPlaying ? "Unity Play 상태에서 사용할 수 있습니다."
    : state.motionError ? `모션 준비 실패: ${state.motionError}`
    : state.isRecording ? "녹화 중에는 재생 제어를 사용할 수 없습니다."
    : state.isPreparingMotion ? "모션 보정을 준비하고 있습니다."
    : !ready ? "FBX를 먼저 가져와 주세요."
    : `${state.isPlayingMotion ? "재생 중" : "정지 / 일시정지"} · ${time}초${state.isSurfaceCorrectionEnabled === false ? " · 표면 보정 OFF 비교 · 품질 미검증" : ""}`;
  return {
    canPlay: available && !state.isPreparingMotion && !state.isPlayingMotion,
    canPause: available && !state.isPreparingMotion && state.isPlayingMotion === true,
    canStop: available,
    message
  };
}

function getBridgeConfig() {
  const config = globalThis.window?.mainRecordingSettingsBridgeConfig;
  return config && typeof config === "object" ? config : {};
}

if (typeof document !== "undefined") {
  bootstrapSettingsUi(document);
}
