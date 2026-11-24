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

// 사용자 결정(2026-11-24): 표시 이름이 같으면 같은 모델로 보고 카드 한 장으로 묶는다(라이브러리 데이터는 그대로).
test("표시 이름이 같은 캐릭터는 카드 한 장으로 묶고 지금 캐릭터를 대표로 쓴다", () => {
  const twins = {
    activeCharacterId: "pmx",
    characters: [
      { id: "pmx", displayName: "YYB Miku", status: "ready", sourcePath: "C:/Downloads/YYB.pmx" },
      { id: "fbx", displayName: "YYB Miku ", status: "ready", sourcePath: "D:/Project/YYB.fbx", thumbnailPath: "C:/Lib/fbx.png" }
    ]
  };
  const view = resolveCharacterSelectView({
    library: twins, isPlaying: true, runtimeState: { hasTargetCharacter: true, targetCharacterName: "YYB Miku" }
  });
  assert.deepEqual(view.cards, [
    { value: "pmx", name: "YYB Miku", format: "PMX", thumbnailUrl: "file:///C:/Lib/fbx.png", isCurrent: true }
  ]);
  assert.equal(view.options.length, 1);
  assert.equal(view.selectedId, "pmx");
});

test("지금 캐릭터가 없으면 빨리 불러오는 형식(FBX → VRM → PMX)을 대표로 쓴다", () => {
  const library = { characters: [
    { id: "pmx", displayName: "YYB Miku", status: "ready", sourcePath: "C:/YYB.pmx", thumbnailPath: "C:/Lib/pmx.png" },
    { id: "fbx", displayName: "YYB Miku", status: "ready", sourcePath: "C:/YYB.fbx" },
    { id: "vrm-a", displayName: "NT", status: "ready", sourcePath: "C:/NT.pmx" },
    { id: "vrm-b", displayName: "NT", status: "ready", sourcePath: "C:/NT.vrm" },
    { id: "other", displayName: "Other", status: "ready", sourcePath: "C:/Other.pmx" }
  ] };
  const view = resolveCharacterSelectView({ library, isPlaying: true });
  assert.deepEqual(view.cards.map((card) => [card.value, card.format]), [["fbx", "FBX"], ["vrm-b", "VRM"], ["other", "PMX"]]);
  // 대표에 썸네일이 없으면 같은 모델의 다른 항목 썸네일을 빌려 씀
  assert.equal(view.cards[0].thumbnailUrl, "file:///C:/Lib/pmx.png");
  assert.equal(view.selectedId, "fbx");
});

test("PMX만 있는 캐릭터를 불러올 때는 변환 시간을 안내한다", () => {
  const library = { characters: [{ id: "pmx", displayName: "YYB Miku", status: "ready", sourcePath: "C:/YYB.pmx" }] };
  const loadingPmx = resolveCharacterSelectView({ library, isPlaying: false, isLoading: true, selectedId: "pmx" });
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
    attributes: {},
    children: [],
    setAttribute(name, value) {
      element.attributes[name] = String(value);
    },
    getAttribute(name) {
      return element.attributes[name] ?? null;
    },
    appendChild(child) {
      element.children.push(child);
      if (!element.value && child.value) element.value = child.value;
      return child;
    },
    addEventListener(type, handler) {
      listeners[type] = handler;
    },
    async dispatch(type, event = {}) {
      await listeners[type]?.({ target: element, preventDefault() {}, ...event });
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

// 확정 시안: 라이브러리 캐릭터를 이미지 카드로 보여 주고 고른 카드와 지금 캐릭터를 구분함.
test("캐릭터 카드는 이미지·이름·형식·지금 캐릭터 여부를 보여 준다", () => {
  const twins = {
    activeCharacterId: "nt",
    characters: [
      { id: "yyb", displayName: "YYB Miku", status: "ready", sourcePath: "C:/Models/YYB.pmx", thumbnailPath: "" },
      { id: "nt", displayName: "NT", status: "ready", sourcePath: "D:/Models/NT.vrm", thumbnailPath: "C:\\Lib\\thumbnails\\nt.png" }
    ]
  };
  const view = resolveCharacterSelectView({
    library: twins, isPlaying: true, runtimeState: { hasTargetCharacter: true, targetCharacterName: "NT" }
  });
  assert.deepEqual(view.cards, [
    { value: "yyb", name: "YYB Miku", format: "PMX", thumbnailUrl: "", isCurrent: false },
    { value: "nt", name: "NT", format: "VRM", thumbnailUrl: "file:///C:/Lib/thumbnails/nt.png", isCurrent: true }
  ]);
  // 캐릭터를 아직 불러오지 않았으면 활성 기록이 있어도 '지금 캐릭터'로 표시하지 않음
  const notLoaded = resolveCharacterSelectView({ library: twins, isPlaying: true });
  assert.equal(notLoaded.cards.every((card) => !card.isCurrent), true);
});

test("다른 카드를 고르면 그 캐릭터를 불러오고 고른 카드만 선택 상태가 된다", async () => {
  const two = { characters: [
    { id: "yyb", displayName: "YYB Miku", status: "ready", sourcePath: "C:/YYB.pmx" },
    { id: "nt", displayName: "NT", status: "ready", sourcePath: "C:/NT.vrm" }
  ] };
  const elements = {
    "#characterSelect": createFakeElement("div"),
    "#loadCharacterButton": createFakeElement("button"),
    "#characterSelectStatus": createFakeElement("p")
  };
  const root = { querySelector: (selector) => elements[selector] ?? null, createElement: (tag) => createFakeElement(tag) };
  const posts = [];
  const fetchImpl = async (url, init = {}) => {
    if (url.endsWith("/characters")) return jsonResponse(two);
    if (url.endsWith("/command-result")) return jsonResponse({ commandId: "cmd-nt", state: "success", message: "ok" });
    posts.push(JSON.parse(init.body));
    return jsonResponse({ accepted: true, commandId: "cmd-nt" });
  };
  const card = bootstrapCharacterSelectCard(root, {
    fetchImpl, apiBaseUrl: () => "http://localhost:4100",
    setTimeoutImpl: (callback) => { void callback(); return 0; }
  });
  card.update({ playMode: "playing" }, true);
  await card.refreshLibrary();
  const cards = elements["#characterSelect"].children;
  assert.equal(cards.length, 2);
  assert.deepEqual(cards.map((item) => item.getAttribute("aria-checked")), ["true", "false"]);
  assert.deepEqual(cards.map((item) => item.tabIndex), [0, -1]);

  await cards[1].dispatch("click");
  assert.deepEqual(cards.map((item) => item.getAttribute("aria-checked")), ["false", "true"]);
  // 화살표 키로도 선택이 움직임(시안의 라디오 묶음 규칙)
  await elements["#characterSelect"].dispatch("keydown", { key: "ArrowRight", target: cards[1] });
  assert.deepEqual(cards.map((item) => item.getAttribute("aria-checked")), ["true", "false"]);
  await elements["#characterSelect"].dispatch("keydown", { key: "ArrowLeft", target: cards[0] });

  await elements["#loadCharacterButton"].dispatch("click");
  await new Promise((resolve) => setTimeout(resolve, 0));
  assert.deepEqual(posts, [{ action: "LoadCharacter", characterId: "nt" }]);
});
