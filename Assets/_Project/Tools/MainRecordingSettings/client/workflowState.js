// 작업 단계(캐릭터 → 모션 → 보정·물리 → 재생 → 녹화)의 잠금·배지·표시 문구를 Unity 실행 상태로 판정한다.
// 화면 조작과 분리된 순수 함수라 단위 테스트로 상태 전이를 검증한다.

const STEP_LABELS = Object.freeze({
  character: "캐릭터",
  motion: "모션(FBX)",
  physics: "보정·물리",
  playback: "재생",
  recording: "녹화"
});

const numberFormat = new Intl.NumberFormat("ko-KR");

export function formatTimecode(seconds) {
  const safe = Number.isFinite(seconds) && seconds > 0 ? seconds : 0;
  const tenths = Math.floor(safe * 10 + 1e-6);
  const minutes = Math.floor(tenths / 600);
  const rest = (tenths % 600) / 10;
  return `${String(minutes).padStart(2, "0")}:${rest.toFixed(1).padStart(4, "0")}`;
}

function countFrames(state) {
  return Number.isInteger(state.lastFrameIndex) && state.hasPreparedMotion === true ? state.lastFrameIndex + 1 : 0;
}

function frameText(state) {
  const total = countFrames(state);
  const current = Math.min(total, Math.max(1, (state.currentFrameIndex ?? 0) + 1));
  return `${numberFormat.format(current)} / ${numberFormat.format(total)}프레임`;
}

function percent(value) {
  return Math.round(Math.min(1, Math.max(0, Number.isFinite(value) ? value : 0)) * 100);
}

// 접지 상태는 재생한 프레임마다 정해지므로 재생 전 값은 실패가 아니라 '확인 전'으로 본다.
const GROUNDING_VIEW = Object.freeze({
  Applied: { badge: "done", text: "적용" },
  Fallback: { badge: "warning", text: "일부 프레임 대체 경로", warning: "하체 접지가 일부 프레임에서 대체 경로로 동작했어요." },
  NoGround: { badge: "warning", text: "지면 없음", warning: "하체 접지에 쓸 지면을 찾지 못했어요." },
  Disabled: { badge: "", text: "꺼짐" }
});

export function resolveCorrectionSummary(state) {
  const grounding = GROUNDING_VIEW[state.footGroundingStatus] ?? { badge: "ready", text: "재생하면 확인돼요" };
  const surfaceFailure = state.surfaceCorrectionFailure || "";
  const clothCount = state.clothCount ?? 0;
  const validCloths = state.validClothCount ?? 0;
  const hairSprings = state.hairSpringCount ?? 0;
  const items = [
    { id: "grounding", group: "보정", label: "하체 접지", ...grounding, applied: grounding.badge === "done" || grounding.badge === "warning" },
    { id: "thumb", group: "보정", label: "엄지 보정", badge: state.isThumbGuardEnabled ? "done" : "", text: state.isThumbGuardEnabled ? "적용" : "꺼짐", applied: state.isThumbGuardEnabled === true },
    surfaceFailure
      ? { id: "surface", group: "보정", label: "표면 보정", badge: "failed", text: "준비 실패", applied: false, warning: `표면 보정: ${surfaceFailure}` }
      : { id: "surface", group: "보정", label: "표면 보정", badge: state.isSurfaceCorrectionEnabled === false ? "" : "done", text: state.isSurfaceCorrectionEnabled === false ? "꺼짐(비교 중)" : "적용", applied: state.isSurfaceCorrectionEnabled !== false },
    {
      id: "cloth", group: "물리", label: "의상 물리", text: `${validCloths}/${clothCount} 생성`,
      badge: clothCount === 0 ? "" : validCloths < clothCount ? "warning" : "done",
      applied: clothCount > 0 && validCloths === clothCount,
      warning: validCloths < clothCount ? `의상 물리 ${clothCount - validCloths}개가 생성되지 않았어요.` : undefined
    },
    { id: "hair", group: "물리", label: "헤어 스프링", text: `${hairSprings}개`, badge: hairSprings > 0 ? "done" : "", applied: hairSprings > 0 }
  ];
  return {
    items,
    appliedCount: items.filter((item) => item.applied).length,
    total: items.length,
    warnings: items.map((item) => item.warning).filter(Boolean)
  };
}

export function resolvePlaybackView(state) {
  const totalFrames = countFrames(state);
  return {
    timeText: formatTimecode(state.motionTimeSeconds),
    totalTimeText: formatTimecode(state.motionClipLengthSeconds),
    frameText: frameText(state),
    currentFrame: totalFrames === 0 ? 0 : Math.min(totalFrames, (state.currentFrameIndex ?? 0) + 1),
    totalFrames,
    frameRate: state.motionFrameRate ?? 0
  };
}

// 사람이 보는 번호는 1부터, Unity 명령은 0부터 센다.
export function parseSeekFrame(text, totalFrames) {
  const trimmed = String(text ?? "").trim();
  const value = /^\d+$/.test(trimmed) ? Number(trimmed) : Number.NaN;
  if (totalFrames > 0 && Number.isInteger(value) && value >= 1 && value <= totalFrames) {
    return { ok: true, frameIndex: value - 1, message: "" };
  }
  return { ok: false, frameIndex: -1, message: `1부터 ${numberFormat.format(Math.max(totalFrames, 0))}까지 입력할 수 있어요.` };
}

function fileNameOf(path) {
  return String(path || "").split(/[\\/]/).pop();
}

export function resolveRecordingView(state, isUnityPlaying, { isStartRequested = false } = {}) {
  const base = { progressPercent: 0, frameText: frameText(state), outputPath: state.recordingOutputPath || "", fileName: fileNameOf(state.recordingOutputPath),
    canStart: false, canStop: false, canCancelStart: false };
  if (!isUnityPlaying || state.hasPreparedMotion !== true) {
    return { ...base, phase: "locked", message: "모션을 적용하면 녹화할 수 있어요." };
  }
  if (state.isRecording === true) {
    return { ...base, phase: "recording", canStop: true, progressPercent: percent(state.recordingProgress),
      message: "녹화하고 있어요. 다른 단계는 잠겨 있어요." };
  }
  // 보정 준비가 끝나야 녹화가 시작되므로 그동안은 대기 상태로 두고 취소만 허용한다.
  if (isStartRequested && state.isPreparingMotion === true) {
    return { ...base, phase: "starting", canCancelStart: true, message: "표면 보정을 준비한 뒤 녹화를 시작해요." };
  }
  if (state.isRecordingFileFailed === true) {
    return { ...base, phase: "failed", canStart: true, message: "녹화 파일을 끝까지 저장하지 못했어요. 다시 녹화해 주세요." };
  }
  if (base.outputPath && state.isRecordingFileFinalized === true) {
    return { ...base, phase: "saved", canStart: true, progressPercent: 100, message: "녹화 파일을 저장했어요. 파일이 끝까지 완성된 것을 확인했어요." };
  }
  if (base.outputPath) {
    return { ...base, phase: "saving", progressPercent: 100, message: "녹화 파일을 마무리하고 있어요." };
  }
  return { ...base, phase: "ready", canStart: state.isPreparingMotion !== true, message: "녹화할 준비가 됐어요." };
}

function step(id, fields) {
  return { id, label: STEP_LABELS[id], sub: "", badge: "", locked: false, lockReason: "", ...fields };
}

function locked(id, reason) {
  return step(id, { locked: true, lockReason: reason, sub: reason });
}

export function resolveWorkflowSteps(state, isUnityPlaying, { isImportPending = false, isCharacterLoading = false } = {}) {
  const isImporting = state.isImportingFbx === true || isImportPending;
  // Unity가 FBX를 처리하며 멈추면 Play 상태가 끊긴 것처럼 보이므로 가져오기 대기 중에는 처리 화면을 유지한다.
  if (!isUnityPlaying && !isImportPending) {
    const reason = "Unity에서 Main 씬을 Play하면 쓸 수 있어요.";
    return Object.keys(STEP_LABELS).map((id) => locked(id, reason));
  }
  if (state.isRecording === true) {
    const reason = "녹화 중에는 바꿀 수 없어요.";
    return [
      locked("character", reason),
      locked("motion", reason),
      step("physics", { sub: physicsSub(state), badge: physicsBadge(state) }),
      locked("playback", "녹화 중에는 조작할 수 없어요."),
      step("recording", { sub: `${frameText(state)} · ${percent(state.recordingProgress)}%`, badge: "progress" })
    ];
  }

  const hasCharacter = state.hasTargetCharacter === true;
  const character = isCharacterLoading
    ? step("character", { sub: "불러오는 중", badge: "progress" })
    : step("character", { sub: hasCharacter ? state.targetCharacterName || "캐릭터" : "아직 없음", badge: hasCharacter ? "done" : "ready" });
  if (isImporting) {
    const after = "모션 적용이 끝나면 쓸 수 있어요.";
    return [character,
      step("motion", { sub: `적용 중 · ${percent(state.sessionProgress)}%`, badge: "progress" }),
      locked("physics", after), locked("playback", after), locked("recording", after)];
  }
  if (!hasCharacter) {
    const afterMotion = "모션을 적용하면 쓸 수 있어요.";
    return [character, locked("motion", "캐릭터를 먼저 불러와야 해요."),
      locked("physics", afterMotion), locked("playback", afterMotion), locked("recording", afterMotion)];
  }
  if (state.hasPreparedMotion !== true) {
    const afterMotion = "모션을 적용하면 쓸 수 있어요.";
    const motion = state.motionError
      ? step("motion", { sub: state.motionError, badge: "failed" })
      : step("motion", { sub: "FBX를 골라 적용해 주세요", badge: "ready" });
    return [character, motion, locked("physics", afterMotion), locked("playback", afterMotion), locked("recording", afterMotion)];
  }

  const recording = resolveRecordingView(state, true);
  return [
    character,
    step("motion", { sub: `${state.motionClipName || "모션"} · ${numberFormat.format(countFrames(state))}프레임`, badge: "done" }),
    step("physics", { sub: physicsSub(state), badge: physicsBadge(state) }),
    step("playback", { sub: `${state.isPlayingMotion ? "재생 중" : "일시정지"} · ${numberFormat.format(resolvePlaybackView(state).currentFrame)}프레임` }),
    step("recording", {
      sub: recording.phase === "saved" ? `${recording.fileName} 저장됨`
        : recording.phase === "saving" ? "파일 마무리 중"
          : recording.phase === "failed" ? "저장 실패" : "녹화할 준비가 됐어요",
      badge: recording.phase === "saved" ? "done" : recording.phase === "saving" ? "progress" : recording.phase === "failed" ? "failed" : ""
    })
  ];
}

function physicsSub(state) {
  const summary = resolveCorrectionSummary(state);
  return `${summary.appliedCount}/${summary.total} 적용 · ${summary.warnings.length ? `경고 ${summary.warnings.length}` : "경고 없음"}`;
}

function physicsBadge(state) {
  return resolveCorrectionSummary(state).warnings.length ? "warning" : "done";
}
