import { CHARACTER_ACTIONS, getCharacters, getCommandResult, sendCharacterCommand } from "./characterApi.js";

// 홈 화면 1단계 카드. Main 씬은 캐릭터 없이 시작하므로 FBX 가져오기 전에 캐릭터를 먼저 불러오게 함.
// 캐릭터 유무의 정본은 Unity 실행 상태(runtimeState.hasTargetCharacter)이고,
// 목록은 라이브러리 스냅샷(/characters), 불러오기는 캐릭터 명령 큐(/character-command)를 씀.

const RESULT_POLL_INTERVAL_MS = 700;
// PMX는 불러올 때마다 FBX로 변환해 수 분 걸릴 수 있음.
const LOAD_TIMEOUT_MS = 600000;

// 같은 이름의 캐릭터가 형식만 다르게 여러 번 등록될 수 있어 형식과 파일 이름을 함께 보여 줌.
export function formatCharacterOption(character) {
  const sourcePath = character.sourcePath || "";
  const fileName = sourcePath.split(/[\\/]/).pop() || "";
  const extension = (fileName.match(/\.([^.]+)$/)?.[1] || "").toUpperCase();
  const name = character.displayName || character.id;
  const isActive = character.isActive === true;
  return [name, extension && `(${extension})`, isActive && "· 현재"].filter(Boolean).join(" ");
}

// 실행 상태와 라이브러리로 카드 표시·버튼 가능 여부를 계산함.
export function resolveCharacterSelectView({
  library = { characters: [] },
  runtimeState = {},
  isPlaying = false,
  isLoading = false,
  selectedId = "",
  isLibraryUnavailable = false
} = {}) {
  const loadable = (library.characters ?? []).filter((character) => character.status !== "missing");
  const options = loadable.map((character) => ({
    value: character.id,
    label: formatCharacterOption({ ...character, isActive: character.id === library.activeCharacterId }),
    isPmx: /\.pmx$/i.test(character.sourcePath || "")
  }));
  const hasCharacter = runtimeState.hasTargetCharacter === true;
  const currentName = hasCharacter ? (runtimeState.targetCharacterName || "캐릭터") : "";
  const busy = isLoading || runtimeState.isImportingFbx === true || runtimeState.isRecording === true;
  const selected = options.find((option) => option.value === selectedId)
    ?? options.find((option) => option.value === library.activeCharacterId)
    ?? options[0];
  // 불러오는 동안 Unity가 멈춰 Play 상태를 놓칠 수 있으므로 진행 안내를 먼저 보여 줌.
  const message = isLoading ? (selected?.isPmx
      ? "PMX 캐릭터를 변환해 불러오는 중입니다. 몇 분 걸릴 수 있습니다."
      : "캐릭터를 불러오는 중입니다.")
    : !isPlaying ? "Unity Play 상태에서 캐릭터를 불러올 수 있습니다."
    : isLibraryUnavailable && options.length === 0
      ? "캐릭터 목록을 읽지 못했습니다. 설정창 연결을 확인한 뒤 홈으로 돌아오면 다시 읽습니다."
    : options.length === 0 ? "등록된 캐릭터가 없습니다. 라이브러리에서 캐릭터 파일을 추가해 주세요."
    : runtimeState.isImportingFbx === true ? "FBX를 처리하는 동안에는 캐릭터를 바꿀 수 없습니다."
    : runtimeState.isRecording === true ? "녹화 중에는 캐릭터를 바꿀 수 없습니다."
    : hasCharacter ? `현재 캐릭터: ${currentName}`
    : "불러올 캐릭터를 선택해 주세요.";
  return {
    options: options.map(({ value, label }) => ({ value, label })),
    selectedId: selected?.value ?? "",
    canLoad: isPlaying && !busy && options.length > 0,
    hasCharacter,
    message
  };
}

export function bootstrapCharacterSelectCard(root, {
  fetchImpl = globalThis.fetch,
  apiBaseUrl,
  setTimeoutImpl = globalThis.setTimeout,
  onFeedback = () => {},
  onLibraryUnavailable = () => {}
} = {}) {
  const $ = (selector) => root.querySelector?.(selector) ?? null;
  const elements = {
    select: $("#characterSelect"),
    loadButton: $("#loadCharacterButton"),
    status: $("#characterSelectStatus")
  };
  let library = { characters: [] };
  let isLibraryUnavailable = false;
  let runtimeState = {};
  let isPlaying = false;
  let isLoading = false;
  let pendingCommandId = "";
  let loadDeadline = 0;
  let renderedOptionsKey = "";

  function render() {
    const view = resolveCharacterSelectView({
      library, runtimeState, isPlaying, isLoading, selectedId: elements.select?.value ?? "", isLibraryUnavailable
    });
    if (elements.select) {
      const optionsKey = view.options.map((option) => `${option.value}\u0000${option.label}`).join("\u0001");
      if (optionsKey !== renderedOptionsKey) {
        renderedOptionsKey = optionsKey;
        elements.select.textContent = "";
        for (const option of view.options) {
          const element = root.createElement?.("option") ?? globalThis.document?.createElement("option");
          element.value = option.value;
          element.textContent = option.label;
          elements.select.appendChild(element);
        }
      }
      elements.select.value = view.selectedId;
      elements.select.disabled = !view.canLoad;
    }
    if (elements.loadButton) {
      elements.loadButton.disabled = !view.canLoad;
      elements.loadButton.textContent = isLoading ? "불러오는 중..." : "캐릭터 불러오기";
    }
    if (elements.status) elements.status.textContent = view.message;
  }

  async function refreshLibrary() {
    try {
      library = await getCharacters({ apiBaseUrl: apiBaseUrl(), fetchImpl });
      isLibraryUnavailable = false;
    } catch (error) {
      library = { characters: [] };
      isLibraryUnavailable = true;
      onLibraryUnavailable(error);
    }
    render();
  }

  async function loadSelected() {
    const view = resolveCharacterSelectView({
      library, runtimeState, isPlaying, isLoading, selectedId: elements.select?.value ?? "", isLibraryUnavailable
    });
    if (!view.canLoad || !view.selectedId) return;
    isLoading = true;
    render();
    try {
      const response = await sendCharacterCommand({
        apiBaseUrl: apiBaseUrl(), fetchImpl,
        action: CHARACTER_ACTIONS.loadCharacter, characterId: view.selectedId
      });
      pendingCommandId = response.commandId || "";
      loadDeadline = Date.now() + LOAD_TIMEOUT_MS;
      onFeedback("Unity에서 캐릭터를 불러오는 중입니다.", "loading");
      setTimeoutImpl(pollResult, RESULT_POLL_INTERVAL_MS);
    } catch (error) {
      finishLoad(`캐릭터 불러오기 요청 실패: ${error.message}`, "error");
    }
  }

  async function pollResult() {
    if (!pendingCommandId) return;
    try {
      const result = await getCommandResult({ apiBaseUrl: apiBaseUrl(), fetchImpl });
      if (result?.commandId === pendingCommandId && result.state && result.state !== "running") {
        const succeeded = result.state === "success";
        finishLoad(succeeded ? "캐릭터를 불러왔습니다. 이제 FBX를 가져올 수 있습니다."
          : `캐릭터 불러오기 실패: ${result.message || "Unity 로그를 확인해 주세요."}`,
        succeeded ? "success" : "error");
        void refreshLibrary();
        return;
      }
    } catch { /* 브리지 일시 실패는 다음 폴링에서 다시 확인함 */ }
    if (Date.now() > loadDeadline) {
      finishLoad("캐릭터 불러오기 응답이 지연됩니다. Unity 에디터 상태를 확인해 주세요.", "error");
      return;
    }
    setTimeoutImpl(pollResult, RESULT_POLL_INTERVAL_MS);
  }

  function finishLoad(message, tone) {
    pendingCommandId = "";
    isLoading = false;
    onFeedback(message, tone);
    render();
  }

  elements.loadButton?.addEventListener("click", () => void loadSelected());
  render();

  return {
    // Play 진입 시점에 라이브러리를 다시 읽어 새로 등록한 캐릭터도 보이게 함.
    update(nextRuntimeState, nextIsPlaying) {
      const enteredPlay = nextIsPlaying && !isPlaying;
      runtimeState = nextRuntimeState ?? {};
      isPlaying = nextIsPlaying;
      if (enteredPlay) void refreshLibrary();
      render();
    },
    refreshLibrary,
    get isLoading() {
      return isLoading;
    }
  };
}
