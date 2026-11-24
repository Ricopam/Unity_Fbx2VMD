import assert from "node:assert/strict";
import test from "node:test";

import { bootstrapCharacterSelectCard, resolveCharacterSelectView } from "../client/characterSelectCard.js";
import { resolveImportGate, resolveImportProgress } from "../client/settingsUi.js";

const library = {
  characters: [
    { id: "yyb", displayName: "YYB Miku", status: "ready" },
    { id: "lost", displayName: "Missing", status: "missing" }
  ]
};

test("캐릭터 선택 카드는 Play 중에만, 경로가 살아 있는 캐릭터만 불러올 수 있다", () => {
  const stopped = resolveCharacterSelectView({ library, isPlaying: false });
  assert.equal(stopped.canLoad, false);
  assert.match(stopped.message, /Play/);

  const playing = resolveCharacterSelectView({ library, isPlaying: true });
  assert.deepEqual(playing.options, [{ value: "yyb", label: "YYB Miku" }]);
  assert.equal(playing.selectedId, "yyb");
  assert.equal(playing.canLoad, true);
  assert.match(playing.message, /선택해 주세요/);

  const loaded = resolveCharacterSelectView({
    library, isPlaying: true, runtimeState: { hasTargetCharacter: true, targetCharacterName: "Library Character" }
  });
  assert.equal(loaded.hasCharacter, true);
  assert.match(loaded.message, /현재 캐릭터: Library Character/);

  const empty = resolveCharacterSelectView({ library: { characters: [] }, isPlaying: true });
  assert.equal(empty.canLoad, false);
  assert.match(empty.message, /라이브러리/);

  assert.equal(resolveCharacterSelectView({ library, isPlaying: true, runtimeState: { isImportingFbx: true } }).canLoad, false);
});

test("FBX 가져오기는 캐릭터를 불러온 뒤에만 가능하다", () => {
  const noCharacter = resolveImportGate({}, true);
  assert.equal(noCharacter.canImport, false);
  assert.match(noCharacter.message, /캐릭터를 불러와/);

  const ready = resolveImportGate({ hasTargetCharacter: true, targetCharacterName: "Library Character" }, true);
  assert.equal(ready.canImport, true);
  assert.match(ready.message, /Library Character/);

  assert.equal(resolveImportGate({ hasTargetCharacter: true }, false).canImport, false);
  assert.equal(resolveImportGate({ hasTargetCharacter: true, isImportingFbx: true }, true).canImport, false);
  assert.equal(resolveImportGate({ hasTargetCharacter: true }, true, { isPending: true }).canImport, false);
  assert.equal(resolveImportGate({ hasTargetCharacter: true }, true, { isCharacterLoading: true }).canImport, false);
});

test("FBX 가져오기 결과는 Unity 실행 상태로 판정하고 이전 오류는 무시한다", () => {
  const live = (state) => ({ playMode: "playing", ...state });
  const sent = { sentAtMs: 0, sawImporting: false, errorAtSend: "이전 실패", preparedAtSend: true };

  // 요청 전부터 있던 오류는 이번 결과가 아님
  const stale = resolveImportProgress(sent, live({ motionError: "이전 실패", hasPreparedMotion: true }), 1000);
  assert.equal(stale.feedback, null);
  assert.ok(stale.pending);

  const importing = resolveImportProgress(sent, live({ isImportingFbx: true, motionError: "이전 실패" }), 1000);
  assert.equal(importing.pending.sawImporting, true);
  assert.equal(importing.feedback.tone, "loading");

  const done = resolveImportProgress(importing.pending, live({ hasPreparedMotion: true }), 2000);
  assert.equal(done.pending, null);
  assert.equal(done.feedback.tone, "success");

  const failed = resolveImportProgress(sent, live({ motionError: "캐릭터를 먼저 불러와 주세요." }), 1000);
  assert.equal(failed.pending, null);
  assert.equal(failed.feedback.tone, "error");
  assert.match(failed.feedback.message, /캐릭터를 먼저/);

  // 이전 FBX의 준비 완료 상태만으로 성공 처리하지 않음
  assert.equal(resolveImportProgress(sent, live({ hasPreparedMotion: true }), 1000).feedback, null);

  const timedOut = resolveImportProgress(sent, live({}), 301000);
  assert.equal(timedOut.pending, null);
  assert.equal(timedOut.feedback.tone, "error");
});

test("FBX 처리로 Unity가 멈춰 상태가 끊겨도 실패로 보지 않고 끝나면 완료로 본다", () => {
  const sent = { sentAtMs: 0, sawImporting: false, errorAtSend: "", preparedAtSend: false };

  // 상태가 오래되면 브리지는 Play 아님 기본값을 돌려줌
  const frozen = resolveImportProgress(sent, { playMode: "stopped" }, 400000);
  assert.ok(frozen.pending);
  assert.equal(frozen.feedback.tone, "loading");

  // 시작·끝 신호를 모두 놓쳤어도 요청 전에 없던 준비 완료는 이번 결과임
  const finished = resolveImportProgress(frozen.pending, { playMode: "playing", hasPreparedMotion: true }, 400500);
  assert.equal(finished.pending, null);
  assert.equal(finished.feedback.tone, "success");
});

test("같은 이름 캐릭터는 파일 형식으로 구분하고 현재 캐릭터를 기본 선택한다", () => {
  const twins = {
    activeCharacterId: "fbx",
    characters: [
      { id: "pmx", displayName: "YYB Miku", status: "ready", sourcePath: "C:\Downloads\YYB.pmx" },
      { id: "fbx", displayName: "YYB Miku", status: "ready", sourcePath: "D:/Project/YYB.fbx" }
    ]
  };
  const view = resolveCharacterSelectView({ library: twins, isPlaying: true });
  assert.deepEqual(view.options.map((option) => option.label), ["YYB Miku (PMX)", "YYB Miku (FBX) · 현재"]);
  assert.equal(view.selectedId, "fbx");

  const loadingPmx = resolveCharacterSelectView({ library: twins, isPlaying: false, isLoading: true, selectedId: "pmx" });
  assert.match(loadingPmx.message, /PMX/);
});

test("캐릭터 불러오기 버튼은 명령을 보내고 Unity 결과를 받아 안내한다", async () => {
  const elements = {
    "#characterSelect": createFakeElement("select"),
    "#loadCharacterButton": createFakeElement("button"),
    "#characterSelectStatus": createFakeElement("p")
  };
  const root = {
    querySelector: (selector) => elements[selector] ?? null,
    createElement: (tag) => createFakeElement(tag)
  };
  const posts = [];
  const fetchImpl = async (url, init = {}) => {
    if (url.endsWith("/characters")) return jsonResponse(library);
    if (url.endsWith("/command-result")) {
      return jsonResponse({ commandId: "cmd-load", state: "success", message: "캐릭터 로드 완료: yyb" });
    }
    posts.push({ url, body: JSON.parse(init.body) });
    return jsonResponse({ accepted: true, commandId: "cmd-load" });
  };
  const feedback = [];
  const card = bootstrapCharacterSelectCard(root, {
    fetchImpl,
    apiBaseUrl: () => "http://localhost:4100",
    setTimeoutImpl: (callback) => { void callback(); return 0; },
    onFeedback: (message, tone) => feedback.push({ message, tone })
  });

  card.update({ playMode: "playing" }, true);
  await card.refreshLibrary();
  assert.equal(elements["#loadCharacterButton"].disabled, false);
  assert.equal(elements["#characterSelect"].children.length, 1);

  await elements["#loadCharacterButton"].dispatch("click");
  await new Promise((resolve) => setTimeout(resolve, 0));

  assert.deepEqual(posts.map((post) => post.body), [{ action: "LoadCharacter", characterId: "yyb" }]);
  assert.equal(feedback.at(-1).tone, "success");
  assert.equal(card.isLoading, false);
});

function jsonResponse(body) {
  return { ok: true, status: 200, json: async () => body };
}

function createFakeElement(tagName) {
  const listeners = {};
  const element = {
    tagName,
    disabled: false,
    textContent: "",
    value: "",
    dataset: {},
    children: [],
    appendChild(child) {
      element.children.push(child);
      if (!element.value && child.value) element.value = child.value;
      return child;
    },
    addEventListener(type, handler) {
      listeners[type] = handler;
    },
    async dispatch(type) {
      await listeners[type]?.();
    }
  };
  // textContent 비우기는 자식 목록 초기화와 같게 취급함
  return new Proxy(element, {
    set(target, key, value) {
      if (key === "textContent" && value === "") target.children = [];
      target[key] = value;
      return true;
    }
  });
}

test("같은 문구의 실패가 다시 나도 대기가 남지 않고 실패로 끝난다", () => {
  const sameError = "FBX 로드에 실패했습니다.";
  const sent = { sentAtMs: 0, sawImporting: false, errorAtSend: sameError, preparedAtSend: false };
  // 처리 시작 신호를 보면 이전 오류를 지움
  const started = resolveImportProgress(sent, { playMode: "playing", isImportingFbx: true, motionError: sameError }, 1000);
  assert.equal(started.pending.errorAtSend, "");
  const failedAgain = resolveImportProgress(started.pending, { playMode: "playing", motionError: sameError }, 2000);
  assert.equal(failedAgain.pending, null);
  assert.equal(failedAgain.feedback.tone, "error");

  // 시작 신호를 놓쳐도 오류가 한 번 비면 같은 문구의 새 실패를 잡음
  const cleared = resolveImportProgress(sent, { playMode: "playing" }, 1000);
  assert.equal(cleared.pending.errorAtSend, "");
  assert.equal(resolveImportProgress(cleared.pending, { playMode: "playing", motionError: sameError }, 2000).feedback.tone, "error");
});

test("Play가 끝나 응답이 없으면 전체 한도 뒤 대기를 끝낸다", () => {
  const sent = { sentAtMs: 0, sawImporting: true, errorAtSend: "", preparedAtSend: false };
  assert.ok(resolveImportProgress(sent, { playMode: "stopped" }, 500000).pending);
  const ended = resolveImportProgress(sent, { playMode: "stopped" }, 601000);
  assert.equal(ended.pending, null);
  assert.equal(ended.feedback.tone, "error");
});

test("버튼이 잠긴 이유를 안내한다", () => {
  assert.match(resolveImportGate({ hasTargetCharacter: true, isRecording: true }, true).message, /녹화 중/);
  assert.match(resolveImportGate({ hasTargetCharacter: true }, true, { isPending: true }).message, /처리/);
  assert.match(resolveImportGate({}, true, { isCharacterLoading: true }).message, /불러오는 중/);
  const importing = resolveCharacterSelectView({ library, isPlaying: true, runtimeState: { isImportingFbx: true } });
  assert.match(importing.message, /FBX를 처리/);
  const unavailable = resolveCharacterSelectView({ library: { characters: [] }, isPlaying: true, isLibraryUnavailable: true });
  assert.match(unavailable.message, /읽지 못했습니다/);
});

test("캐릭터 불러오기 실패 결과를 오류로 안내하고 다시 누를 수 있게 한다", async () => {
  const elements = {
    "#characterSelect": createFakeElement("select"),
    "#loadCharacterButton": createFakeElement("button"),
    "#characterSelectStatus": createFakeElement("p")
  };
  const root = { querySelector: (selector) => elements[selector] ?? null, createElement: (tag) => createFakeElement(tag) };
  const fetchImpl = async (url) => {
    if (url.endsWith("/characters")) return jsonResponse(library);
    if (url.endsWith("/command-result")) return jsonResponse({ commandId: "cmd-fail", state: "error", message: "캐릭터 파일이 없습니다" });
    return jsonResponse({ accepted: true, commandId: "cmd-fail" });
  };
  const feedback = [];
  const card = bootstrapCharacterSelectCard(root, {
    fetchImpl, apiBaseUrl: () => "http://localhost:4100",
    setTimeoutImpl: (callback) => { void callback(); return 0; },
    onFeedback: (message, tone) => feedback.push({ message, tone })
  });
  card.update({ playMode: "playing" }, true);
  await card.refreshLibrary();
  await elements["#loadCharacterButton"].dispatch("click");
  await new Promise((resolve) => setTimeout(resolve, 0));

  assert.equal(feedback.at(-1).tone, "error");
  assert.match(feedback.at(-1).message, /캐릭터 파일이 없습니다/);
  assert.equal(card.isLoading, false);
  assert.equal(elements["#loadCharacterButton"].disabled, false);
});
