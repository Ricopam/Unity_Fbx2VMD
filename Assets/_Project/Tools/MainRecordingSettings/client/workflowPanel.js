import {
  advanceStartRequest,
  folderOfFile,
  parseSeekFrame,
  recordingFeedbackFor,
  resolveCorrectionSummary,
  resolveImportView,
  resolvePlaybackView,
  resolveRecordingView,
  resolveWorkflowSteps
} from "./workflowState.js";

// 작업 단계 목록·단계별 화면을 Unity 실행 상태로 그리고, 녹화·프레임 이동 조작을 명령으로 보낸다.
// 판정은 workflowState.js가 맡고 이 모듈은 화면 반영과 입력 연결만 담당한다.

const BADGE_TEXT = Object.freeze({ done: "완료", progress: "진행 중", ready: "준비 전", failed: "실패", warning: "경고" });

export function bootstrapWorkflowPanel(root, { sendMotionCommand, openPath, onFeedback } = {}) {
  const $ = (selector) => root.querySelector?.(selector) ?? null;
  let state = {};
  let isUnityPlaying = false;
  let isBusy = false;
  // 녹화 시작은 보정 준비가 끝나야 실제로 시작되므로 요청 사실을 따로 기억한다.
  let startRequest = null;
  let isDraggingTimeline = false;
  let stepContext = {};
  let lastRecordingPhase;
  // Unity가 멈춰 상태가 오지 않아도 경과 시간이 흐르도록 처리 표시 중에만 1초마다 다시 그림.
  let importTicker = null;

  $("#wfSeekButton")?.addEventListener("click", seekFromInput);
  $("#wfSeekInput")?.addEventListener("keydown", (event) => {
    if (event.key === "Enter") seekFromInput();
  });
  const timeline = $("#wfTimeline");
  timeline?.addEventListener("input", () => {
    isDraggingTimeline = true;
    const frameLine = $("#wfFrameLine");
    if (frameLine) frameLine.textContent = `${Number(timeline.value).toLocaleString("ko-KR")}프레임으로 이동`;
  });
  // 끄는 동안은 명령을 보내지 않고 놓을 때 한 번만 보내 대기 명령 충돌을 막는다.
  timeline?.addEventListener("change", async () => {
    isDraggingTimeline = false;
    // 끄는 사이 잠기거나 다른 명령이 대기 중이면 보내지 않고 현재 위치로 되돌린다.
    if (timeline.disabled || isBusy) {
      onFeedback?.("지금은 이동할 수 없어요. 진행 중인 요청이 끝난 뒤 다시 시도해 주세요.", "error");
      render();
      return;
    }
    await send("SeekMotionFrame", { frameIndex: Number(timeline.value) - 1 }, "선택한 프레임으로 이동했어요.");
  });
  // Esc 등으로 change 없이 끝나도 드래그 상태가 남지 않게 한다.
  timeline?.addEventListener("blur", () => {
    if (!isDraggingTimeline) return;
    isDraggingTimeline = false;
    render();
  });
  $("#wfRecordStart")?.addEventListener("click", async () => {
    if (await send("StartMotionRecording", {}, "녹화 시작 요청을 Unity가 받았어요.")) {
      startRequest = advanceStartRequest(null, state, Date.now(), { requested: true });
    }
    render();
  });
  $("#wfRecordStop")?.addEventListener("click", () => void send("StopMotionRecording", {}, "녹화를 멈췄어요. 파일을 마무리하는 중이에요."));
  // 보정 준비 중 대기한 녹화는 재생 정지 명령이 대기 요청까지 취소한다.
  $("#wfRecordCancel")?.addEventListener("click", async () => {
    if (await send("StopImportedMotion", {}, "녹화 시작을 취소했어요.")) startRequest = null;
    render();
  });
  $("#wfOpenRecordingFolder")?.addEventListener("click", async () => {
    const folder = folderOfFile(state.recordingOutputPath);
    if (!folder || typeof openPath !== "function") {
      onFeedback?.("현재 환경에서는 폴더를 열 수 없어요.", "error");
      return;
    }
    try {
      // 셸은 성공하면 빈 문자열, 실패하면 오류 문구를 돌려준다.
      const failure = await openPath(folder);
      if (failure) {
        onFeedback?.(`폴더를 열지 못했어요: ${failure}`, "error");
        return;
      }
      onFeedback?.("녹화 폴더를 열었어요.", "success");
    } catch (error) {
      onFeedback?.(`폴더를 열지 못했어요: ${error.message}`, "error");
    }
  });

  function seekFromInput() {
    const input = $("#wfSeekInput");
    const parsed = parseSeekFrame(input?.value, resolvePlaybackView(state).totalFrames);
    if (!parsed.ok) {
      input?.setAttribute?.("aria-invalid", "true");
      onFeedback?.(parsed.message, "error");
      return;
    }
    input?.removeAttribute?.("aria-invalid");
    void send("SeekMotionFrame", { frameIndex: parsed.frameIndex }, "입력한 프레임으로 이동했어요.");
  }

  async function send(action, extra, successMessage) {
    if (isBusy || typeof sendMotionCommand !== "function") return false;
    return await sendMotionCommand(action, extra, successMessage);
  }

  function render() {
    const steps = resolveWorkflowSteps(state, isUnityPlaying, { ...stepContext, nowMs: Date.now() });
    for (const item of steps) {
      renderStepRow(item);
      renderStepPanel(item);
    }
    renderMotion();
    renderCorrections();
    renderPlayback(steps.find((item) => item.id === "playback"));
    renderRecording();
  }

  function renderStepRow(item) {
    const row = $(`[data-wf-step="${item.id}"]`);
    if (!row) return;
    row.dataset.locked = String(item.locked);
    setText(row.querySelector?.("[data-wf-sub]"), item.sub);
    setBadge(row.querySelector?.("[data-wf-step-badge]"), item.badge);
  }

  function renderStepPanel(item) {
    const panel = $(`[data-wf-panel="${item.id}"]`);
    if (!panel) return;
    setBadge(panel.querySelector?.(`[data-wf-badge="${item.id}"]`), item.badge);
    const lockNote = panel.querySelector?.("[data-wf-lock]");
    if (lockNote) {
      lockNote.hidden = !item.locked;
      setText(lockNote.querySelector?.("[data-wf-lock-text]"), item.lockReason);
    }
    for (const body of Array.from(panel.querySelectorAll?.("[data-wf-body]") ?? [])) {
      body.hidden = item.locked;
    }
  }

  function renderMotion() {
    const importView = resolveImportView(state, { ...stepContext, nowMs: Date.now() });
    const progress = $("#wfImportProgress");
    if (progress) progress.hidden = !importView.visible;
    setText($("#wfImportPercent"), importView.text);
    const meter = $("#wfImportMeter");
    if (meter) {
      // 진행률을 모르면 값 속성을 빼 막대를 '진행 중' 표시로 둠.
      if (importView.meterPercent === null) {
        meter.removeAttribute?.("value");
      } else {
        meter.value = importView.meterPercent;
      }
      meter.textContent = importView.text;
    }
    syncImportTicker(importView.visible);
    const facts = $("#wfMotionFacts");
    const playback = resolvePlaybackView(state);
    if (facts) facts.hidden = state.hasPreparedMotion !== true;
    setText($("#wfMotionClip"), state.motionClipName || "-");
    setText($("#wfMotionLength"), playback.totalTimeText);
    setText($("#wfMotionFrames"), `${playback.totalFrames.toLocaleString("ko-KR")}프레임 · ${Math.round(playback.frameRate)}fps`);
  }

  function renderCorrections() {
    const list = $("#wfCorrectionList");
    const warnings = $("#wfCorrectionWarnings");
    if (!list || !warnings || typeof document === "undefined") return;
    const summary = resolveCorrectionSummary(state);
    list.replaceChildren(...summary.items.flatMap((item) => {
      const term = document.createElement("dt");
      term.textContent = `${item.group} · ${item.label}`;
      const detail = document.createElement("dd");
      detail.textContent = item.text;
      if (item.badge) {
        const badge = document.createElement("span");
        setBadge(badge, item.badge);
        detail.append(" ", badge);
      }
      return [term, detail];
    }));
    const lines = summary.warnings.length ? summary.warnings : ["경고가 없어요."];
    warnings.replaceChildren(...lines.map((text) => {
      const line = document.createElement("li");
      line.textContent = text;
      return line;
    }));
  }

  function renderPlayback(item) {
    const view = resolvePlaybackView(state);
    setText($("#wfTime"), view.timeText);
    setText($("#wfTimeTotal"), view.totalTimeText);
    const canSeek = !item?.locked && view.totalFrames > 0 && !isBusy && state.isPreparingMotion !== true;
    if (timeline) {
      timeline.max = String(Math.max(1, view.totalFrames));
      timeline.disabled = !canSeek;
      timeline.setAttribute?.("aria-valuetext", `${view.timeText}, ${view.frameText}`);
      if (!isDraggingTimeline) {
        timeline.value = String(Math.max(1, view.currentFrame));
        setText($("#wfFrameLine"), view.frameText);
      }
    }
    const seekInput = $("#wfSeekInput");
    if (seekInput) {
      seekInput.max = String(Math.max(1, view.totalFrames));
      seekInput.disabled = !canSeek;
    }
    const seekButton = $("#wfSeekButton");
    if (seekButton) seekButton.disabled = !canSeek;
    setText($("#wfSeekHint"), view.totalFrames > 0
      ? `1부터 ${view.totalFrames.toLocaleString("ko-KR")}까지 입력할 수 있어요. 입력 후 Enter를 눌러도 이동해요.`
      : "모션을 적용하면 프레임으로 이동할 수 있어요.");
  }

  function renderRecording() {
    startRequest = advanceStartRequest(startRequest, state, Date.now());
    const view = resolveRecordingView(state, isUnityPlaying, { isStartRequested: startRequest !== null });
    const feedback = recordingFeedbackFor(lastRecordingPhase, view.phase);
    lastRecordingPhase = view.phase;
    if (feedback) onFeedback?.(feedback.message, feedback.tone);
    setText($("#wfRecordStatus"), view.message);
    const showProgress = view.phase === "recording";
    const progress = $("#wfRecordProgress");
    if (progress) progress.hidden = !showProgress;
    setText($("#wfRecordPercent"), `${view.progressPercent}%`);
    const meter = $("#wfRecordMeter");
    if (meter) {
      meter.value = view.progressPercent;
      meter.textContent = `${view.progressPercent}%`;
    }
    setText($("#wfRecordFrame"), view.frameText);
    const fileFacts = $("#wfRecordFileFacts");
    if (fileFacts) fileFacts.hidden = !view.outputPath;
    setText($("#wfRecordFile"), view.outputPath || "-");
    setButton("#wfRecordStart", { show: view.phase !== "recording" && view.phase !== "starting", enabled: view.canStart && !isBusy,
      label: view.phase === "saved" || view.phase === "failed" ? "다시 녹화하기" : "녹화 시작하기" });
    setButton("#wfRecordStop", { show: view.phase === "recording", enabled: view.canStop && !isBusy });
    setButton("#wfRecordCancel", { show: view.phase === "starting", enabled: view.canCancelStart && !isBusy });
    setButton("#wfOpenRecordingFolder", { show: view.phase === "saved", enabled: true });
  }

  function syncImportTicker(isVisible) {
    if (isVisible && !importTicker) {
      importTicker = setInterval(render, 1000);
      importTicker.unref?.();
    } else if (!isVisible && importTicker) {
      clearInterval(importTicker);
      importTicker = null;
    }
  }

  function setButton(selector, { show, enabled, label }) {
    const button = $(selector);
    if (!button) return;
    button.hidden = !show;
    button.disabled = !enabled;
    if (label) button.textContent = label;
  }

  return {
    update(nextState, nextIsUnityPlaying, { isBusy: nextIsBusy = false, isImportPending = false, isCharacterLoading = false, importStartedAtMs } = {}) {
      state = nextState ?? {};
      isUnityPlaying = nextIsUnityPlaying === true;
      isBusy = nextIsBusy;
      stepContext = { isImportPending, isCharacterLoading, importStartedAtMs };
      render();
    }
  };
}

function setText(element, text) {
  if (element) element.textContent = text;
}

function setBadge(element, badge) {
  if (!element) return;
  element.dataset.badge = badge || "";
  element.textContent = BADGE_TEXT[badge] ?? "";
  element.hidden = !BADGE_TEXT[badge];
}
