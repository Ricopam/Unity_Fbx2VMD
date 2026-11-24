import assert from "node:assert/strict";
import test from "node:test";

import {
  advanceStartRequest,
  folderOfFile,
  formatTimecode,
  parseSeekFrame,
  resolveCorrectionSummary,
  resolvePlaybackView,
  resolveRecordingView,
  resolveWorkflowSteps
} from "../client/workflowState.js";

const ready = {
  playMode: "playing",
  hasTargetCharacter: true,
  targetCharacterName: "YYB Hatsune Miku_default_1.0ver",
  hasPreparedMotion: true,
  motionClipName: "satisfaction_2",
  currentFrameIndex: 1244,
  lastFrameIndex: 6233,
  motionFrameRate: 30,
  motionTimeSeconds: 41.5,
  motionClipLengthSeconds: 207.8,
  footGroundingStatus: "Applied",
  isThumbGuardEnabled: true,
  isSurfaceCorrectionEnabled: true,
  clothCount: 2,
  validClothCount: 2,
  hairSpringCount: 2
};
const byId = (steps) => Object.fromEntries(steps.map((step) => [step.id, step]));

test("Unity Play가 아니면 모든 단계가 이유와 함께 잠긴다", () => {
  const steps = resolveWorkflowSteps(ready, false);
  assert.equal(steps.length, 5);
  for (const step of steps) {
    assert.equal(step.locked, true);
    assert.match(step.lockReason, /Unity/);
  }
});

test("캐릭터가 없으면 캐릭터만 열리고 모션은 이유와 함께 잠긴다", () => {
  const steps = byId(resolveWorkflowSteps({ playMode: "playing" }, true));
  assert.equal(steps.character.locked, false);
  assert.equal(steps.character.badge, "ready");
  assert.equal(steps.motion.locked, true);
  assert.match(steps.motion.lockReason, /캐릭터를 먼저/);
  for (const id of ["physics", "playback", "recording"]) {
    assert.equal(steps[id].locked, true);
    assert.match(steps[id].lockReason, /모션/);
  }
});

test("캐릭터만 불러오면 모션이 열리고 나머지는 잠겨 있다", () => {
  const steps = byId(resolveWorkflowSteps({ playMode: "playing", hasTargetCharacter: true, targetCharacterName: "YYB" }, true));
  assert.equal(steps.character.badge, "done");
  assert.equal(steps.character.sub, "YYB");
  assert.equal(steps.motion.locked, false);
  assert.equal(steps.motion.badge, "ready");
  assert.equal(steps.playback.locked, true);
});

test("FBX 처리 중에는 모션이 진행 중이고 진행률을 보인다", () => {
  const steps = byId(resolveWorkflowSteps(
    { playMode: "playing", hasTargetCharacter: true, isImportingFbx: true, sessionProgress: 0.62 }, true));
  assert.equal(steps.motion.badge, "progress");
  assert.match(steps.motion.sub, /62%/);
  assert.equal(steps.playback.locked, true);
  assert.match(steps.playback.lockReason, /끝나면/);
});

test("Unity가 멈춰 상태가 끊겨도 가져오기 대기 중이면 처리 중으로 유지한다", () => {
  const steps = byId(resolveWorkflowSteps({ playMode: "stopped" }, false, { isImportPending: true }));
  assert.equal(steps.motion.badge, "progress");
  assert.equal(steps.motion.locked, false);
});

test("모션 준비 뒤에는 모든 단계가 열리고 프레임 수를 보인다", () => {
  const steps = byId(resolveWorkflowSteps(ready, true));
  assert.equal(steps.motion.badge, "done");
  assert.match(steps.motion.sub, /6,234프레임/);
  for (const id of ["physics", "playback", "recording"]) {
    assert.equal(steps[id].locked, false, id);
  }
  assert.match(steps.playback.sub, /1,245프레임/);
});

test("모션 준비 실패는 실패 배지와 Unity 오류를 보인다", () => {
  const steps = byId(resolveWorkflowSteps({ ...ready, hasPreparedMotion: false, motionError: "FBX를 읽지 못했습니다" }, true));
  assert.equal(steps.motion.badge, "failed");
  assert.match(steps.motion.sub, /읽지 못했습니다/);
});

test("녹화 중에는 녹화 외 단계를 바꿀 수 없다", () => {
  const steps = byId(resolveWorkflowSteps({ ...ready, isRecording: true, recordingProgress: 0.3 }, true));
  assert.equal(steps.recording.badge, "progress");
  assert.match(steps.recording.sub, /30%/);
  for (const id of ["character", "motion", "playback"]) {
    assert.equal(steps[id].locked, true, id);
    assert.match(steps[id].lockReason, /녹화 중/);
  }
  assert.equal(steps.physics.locked, false);
});

test("보정·물리 요약은 다섯 기능의 적용 수와 경고를 센다", () => {
  assert.deepEqual(
    pick(resolveCorrectionSummary(ready)),
    { appliedCount: 5, total: 5, warnings: [] });
  const partial = resolveCorrectionSummary({
    ...ready, footGroundingStatus: "Fallback", validClothCount: 1, surfaceCorrectionFailure: "준비 실패"
  });
  assert.equal(partial.warnings.length, 3);
  assert.equal(partial.items.find((item) => item.id === "cloth").text, "1/2 생성");
  assert.equal(partial.items.find((item) => item.id === "hair").text, "2개");
  // 재생 전에는 접지 상태를 아직 모름 — 실패로 보이지 않게 확인 전으로 둠.
  const before = resolveCorrectionSummary({ ...ready, footGroundingStatus: "Unavailable" });
  assert.equal(before.items.find((item) => item.id === "grounding").badge, "ready");
  assert.equal(before.warnings.length, 0);
});

test("재생 화면은 1부터 세는 현재/전체 프레임과 시간을 보인다", () => {
  const view = resolvePlaybackView(ready);
  assert.equal(view.timeText, "00:41.5");
  assert.equal(view.totalTimeText, "03:27.8");
  assert.equal(view.frameText, "1,245 / 6,234프레임");
  assert.equal(view.currentFrame, 1245);
  assert.equal(view.totalFrames, 6234);
  assert.equal(resolvePlaybackView({}).totalFrames, 0);
});

test("프레임 입력은 1~전체 범위 정수만 0 기반 번호로 바꾼다", () => {
  assert.deepEqual(parseSeekFrame("3120", 6234), { ok: true, frameIndex: 3119, message: "" });
  assert.deepEqual(parseSeekFrame(" 1 ", 6234), { ok: true, frameIndex: 0, message: "" });
  assert.equal(parseSeekFrame("6234", 6234).frameIndex, 6233);
  for (const text of ["0", "6235", "-1", "1.5", "", "abc", "12a"]) {
    const result = parseSeekFrame(text, 6234);
    assert.equal(result.ok, false, text);
    assert.match(result.message, /1부터 6,234까지/);
  }
  assert.equal(parseSeekFrame("1", 0).ok, false);
});

test("녹화 화면은 준비·대기·진행·저장 확인·완료·실패를 구분한다", () => {
  assert.equal(resolveRecordingView({ ...ready, hasPreparedMotion: false }, true).phase, "locked");
  const idle = resolveRecordingView(ready, true);
  assert.equal(idle.phase, "ready");
  assert.equal(idle.canStart, true);
  const waiting = resolveRecordingView({ ...ready, isPreparingMotion: true }, true, { isStartRequested: true });
  assert.equal(waiting.phase, "starting");
  assert.equal(waiting.canCancelStart, true);
  // Unity가 아직 명령을 처리하기 전에도 시작 버튼을 다시 열지 않음.
  const queued = resolveRecordingView(ready, true, { isStartRequested: true });
  assert.equal(queued.phase, "starting");
  assert.equal(queued.canStart, false);
  assert.equal(queued.canCancelStart, false);
  const live = resolveRecordingView({ ...ready, isRecording: true, recordingProgress: 0.3, currentFrameIndex: 1869 }, true);
  assert.equal(live.phase, "recording");
  assert.equal(live.canStop, true);
  assert.equal(live.progressPercent, 30);
  assert.equal(live.frameText, "1,870 / 6,234프레임");
  const saving = resolveRecordingView({ ...ready, recordingOutputPath: "C:/R/satisfaction_2.mp4" }, true);
  assert.equal(saving.phase, "saving");
  assert.equal(saving.canStart, false);
  const saved = resolveRecordingView({ ...ready, recordingOutputPath: "C:/R/satisfaction_2.mp4", isRecordingFileFinalized: true }, true);
  assert.equal(saved.phase, "saved");
  assert.equal(saved.fileName, "satisfaction_2.mp4");
  assert.equal(saved.canStart, true);
  const failed = resolveRecordingView({ ...ready, isRecordingFileFailed: true }, true);
  assert.equal(failed.phase, "failed");
  assert.equal(failed.canStart, true);
});

test("시간 표기는 분:초.십분의일초이며 잘못된 값은 0으로 본다", () => {
  assert.equal(formatTimecode(0), "00:00.0");
  assert.equal(formatTimecode(207.84), "03:27.8");
  assert.equal(formatTimecode(Number.NaN), "00:00.0");
  assert.equal(formatTimecode(-3), "00:00.0");
});

function pick(summary) {
  return { appliedCount: summary.appliedCount, total: summary.total, warnings: summary.warnings };
}

test("녹화 시작 요청은 녹화가 시작되거나 보정 준비를 본 뒤 끝나야 해제된다", () => {
  const request = advanceStartRequest(null, ready, 1000, { requested: true });
  assert.deepEqual(request, { requestedAtMs: 1000, sawPreparing: false });
  // 명령 처리 전 상태(준비 아님)로는 해제하지 않음 — 이전 결함 재현
  assert.ok(advanceStartRequest(request, ready, 2000));
  const preparing = advanceStartRequest(request, { ...ready, isPreparingMotion: true }, 3000);
  assert.equal(preparing.sawPreparing, true);
  assert.ok(advanceStartRequest(preparing, { ...ready, isPreparingMotion: true }, 400000));
  assert.equal(advanceStartRequest(preparing, ready, 400500), null);
  assert.equal(advanceStartRequest(request, { ...ready, isRecording: true }, 2500), null);
  // 준비도 녹화도 보이지 않으면 회답 대기 한도 뒤 해제
  assert.equal(advanceStartRequest(request, ready, 1000 + 15001), null);
  assert.equal(advanceStartRequest(null, ready, 5000), null);
});

test("녹화 파일 폴더는 Windows 드라이브 루트도 올바른 경로로 만든다", () => {
  assert.equal(folderOfFile("D:/Recordings/satisfaction_2.mp4"), "D:/Recordings");
  assert.equal(folderOfFile("D:\\Recordings\\a.mp4"), "D:\\Recordings");
  assert.equal(folderOfFile("C:\\a.mp4"), "C:\\");
  assert.equal(folderOfFile("a.mp4"), "");
  assert.equal(folderOfFile(""), "");
});
