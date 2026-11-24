import assert from "node:assert/strict";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import WebSocket from "ws";

import { createSettingsBridgeServer } from "../server/settingsBridgeServer.js";

test("모션 명령은 허용된 동작만 접수하고 대기 명령을 보존한다", async (t) => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "motion-bridge-"));
  const settingsPath = path.join(root, "settings.json");
  const bridge = createSettingsBridgeServer({ settingsPath, port: 0 });
  await bridge.listen();
  t.after(async () => { await bridge.close(); await fs.rm(root, { recursive: true, force: true }); });
  const post = action => fetch(`${bridge.baseUrl}/motion-command`, {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ action })
  });
  for (const action of ["PlayImportedMotion", "PauseImportedMotion", "StopImportedMotion", "EnableNativeSkinningCorrection", "DisableNativeSkinningCorrection", "StartMotionRecording"]) {
    await fs.writeFile(settingsPath, JSON.stringify({ runtimeState: { playMode: "playing" }, fbxPath: "fixture.fbx" }));
    const accepted = await post(action);
    assert.equal(accepted.status, 200);
    const response = await accepted.json();
    const saved = await fs.readFile(settingsPath, "utf8");
    assert.equal(JSON.parse(saved).pendingCommand.action, action);
    assert.equal(JSON.parse(saved).pendingCommand.commandId, response.commandId);
    assert.equal(JSON.parse(saved).fbxPath, "fixture.fbx");
    assert.equal((await post("StopImportedMotion")).status, 400);
    assert.equal(await fs.readFile(settingsPath, "utf8"), saved);
  }
});

test("모션 명령은 Edit·녹화·잘못된 동작에서 파일을 변경하지 않는다", async (t) => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "motion-bridge-guard-"));
  const settingsPath = path.join(root, "settings.json");
  const bridge = createSettingsBridgeServer({ settingsPath, port: 0 });
  await bridge.listen();
  t.after(async () => { await bridge.close(); await fs.rm(root, { recursive: true, force: true }); });
  for (const [runtimeState, action, status] of [
    [{ playMode: "stopped" }, "PlayImportedMotion", 409],
    [{ playMode: "playing", isRecording: true }, "StopImportedMotion", 400],
    [{ playMode: "playing" }, "StartRecording", 400],
    [{ playMode: "playing", isRecording: true }, "StartMotionRecording", 400],
    [{ playMode: "playing" }, "StopMotionRecording", 400]
  ]) {
    const original = JSON.stringify({ runtimeState, captureWidth: 3840 });
    await fs.writeFile(settingsPath, original);
    const response = await fetch(`${bridge.baseUrl}/motion-command`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ action })
    });
    assert.equal(response.status, status);
    assert.equal(await fs.readFile(settingsPath, "utf8"), original);
  }
});

// 녹화 중지는 녹화 중에만 받는다 — 다른 모션 명령과 잠금 규칙이 반대다.
test("녹화 중에는 녹화 중지 명령만 접수한다", async (t) => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "motion-bridge-stop-"));
  const settingsPath = path.join(root, "settings.json");
  const bridge = createSettingsBridgeServer({ settingsPath, port: 0, createCommandId: () => "stop-1" });
  await bridge.listen();
  t.after(async () => { await bridge.close(); await fs.rm(root, { recursive: true, force: true }); });
  await fs.writeFile(settingsPath, JSON.stringify({ runtimeState: { playMode: "playing", isRecording: true } }));
  const response = await fetch(`${bridge.baseUrl}/motion-command`, {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ action: "StopMotionRecording" })
  });
  assert.equal(response.status, 200);
  assert.equal(JSON.parse(await fs.readFile(settingsPath, "utf8")).pendingCommand.action, "StopMotionRecording");
});

test("프레임 이동 명령은 0 이상 정수 프레임만 paramsJson으로 전달한다", async (t) => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "motion-bridge-seek-"));
  const settingsPath = path.join(root, "settings.json");
  const bridge = createSettingsBridgeServer({ settingsPath, port: 0, createCommandId: () => "seek-1" });
  await bridge.listen();
  t.after(async () => { await bridge.close(); await fs.rm(root, { recursive: true, force: true }); });
  const post = body => fetch(`${bridge.baseUrl}/motion-command`, {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body)
  });
  for (const frameIndex of [-1, 1.5, "12", undefined, 1e20, 6234]) {
    const original = JSON.stringify({ runtimeState: { playMode: "playing", lastFrameIndex: 6233 } });
    await fs.writeFile(settingsPath, original);
    assert.equal((await post({ action: "SeekMotionFrame", frameIndex })).status, 400);
    assert.equal(await fs.readFile(settingsPath, "utf8"), original);
  }
  await fs.writeFile(settingsPath, JSON.stringify({ runtimeState: { playMode: "playing" } }));
  assert.equal((await post({ action: "SeekMotionFrame", frameIndex: 1244 })).status, 200);
  const saved = JSON.parse(await fs.readFile(settingsPath, "utf8"));
  assert.equal(saved.pendingCommand.action, "SeekMotionFrame");
  assert.deepEqual(JSON.parse(saved.pendingCommand.paramsJson), { frameIndex: 1244 });
});

test("settings bridge accepts POST /import-fbx and writes command document", async () => {
  const tempRoot = await fs.mkdtemp(path.join(os.tmpdir(), "settings-bridge-"));
  const settingsPath = path.join(tempRoot, "settings.json");
  await fs.writeFile(settingsPath, JSON.stringify({ runtimeState: { playMode: "playing" } }), "utf8");
  const bridge = createSettingsBridgeServer({
    settingsPath,
    createCommandId: () => "cmd-http",
    now: () => new Date("2026-06-17T01:00:00.000Z")
  });

  await bridge.listen(0);
  try {
    const response = await fetch(`${bridge.baseUrl}/import-fbx`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ fbxPath: "C:/Motion/http.fbx" })
    });
    const body = await response.json();
    const saved = JSON.parse(await fs.readFile(settingsPath, "utf8"));

    assert.equal(response.status, 200);
    assert.equal(response.headers.get("access-control-allow-origin"), "*");
    assert.deepEqual(body, {
      accepted: true,
      commandId: "cmd-http",
      fbxPath: "C:/Motion/http.fbx",
      settingsPath
    });
    assert.equal(saved.pendingCommand.commandId, "cmd-http");
    assert.equal(saved.pendingCommand.action, "ImportFbx");
  } finally {
    await bridge.close();
  }
});

test("settings bridge broadcasts import command to WebSocket clients", async () => {
  const tempRoot = await fs.mkdtemp(path.join(os.tmpdir(), "settings-bridge-"));
  const settingsPath = path.join(tempRoot, "settings.json");
  await fs.writeFile(settingsPath, JSON.stringify({ runtimeState: { playMode: "playing" } }), "utf8");
  const bridge = createSettingsBridgeServer({
    settingsPath,
    createCommandId: () => "cmd-ws",
    now: () => new Date("2026-06-17T01:10:00.000Z")
  });

  await bridge.listen(0);
  const socket = new WebSocket(`${bridge.wsUrl}/settings`);
  const messages = [];
  socket.on("message", (data) => messages.push(JSON.parse(data.toString())));

  try {
    await waitForSocketOpen(socket);
    await waitUntil(() => messages.some((message) => message.type === "status"));

    await fetch(`${bridge.baseUrl}/import-fbx`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ fbxPath: "C:/Motion/ws.fbx" })
    });

    await waitUntil(() => messages.some((message) => message.type === "import-fbx"));
    assert.deepEqual(messages.at(-1), {
      type: "import-fbx",
      accepted: true,
      commandId: "cmd-ws",
      fbxPath: "C:/Motion/ws.fbx",
      settingsPath
    });
  } finally {
    socket.close();
    await bridge.close();
  }
});

test("settings bridge exposes normalized runtime state through GET /state", async () => {
  const tempRoot = await fs.mkdtemp(path.join(os.tmpdir(), "settings-bridge-"));
  const settingsPath = path.join(tempRoot, "settings.json");
  await fs.writeFile(
    settingsPath,
    JSON.stringify({
      runtimeState: {
        playMode: "playing",
        updatedAtUtc: "2026-06-18T04:00:00.000Z"
      },
      lastCommandResult: { commandId: "", action: "", state: "", message: "", finishedAtUtc: "", settingsPath }
    }),
    "utf8"
  );
  const bridge = createSettingsBridgeServer({ settingsPath });

  await bridge.listen(0);
  try {
    const response = await fetch(`${bridge.baseUrl}/state`);
    const body = await response.json();

    assert.equal(response.status, 200);
    assert.deepEqual(body, {
      ok: true,
      settingsPath,
      runtimeState: {
        playMode: "playing",
        updatedAtUtc: "2026-06-18T04:00:00.000Z"
      },
      lastCommandResult: { commandId: "", action: "", state: "", message: "", finishedAtUtc: "", settingsPath }
    });
  } finally {
    await bridge.close();
  }
});

test("settings bridge rejects import in Edit Mode without changing the command", async () => {
  const tempRoot = await fs.mkdtemp(path.join(os.tmpdir(), "settings-bridge-"));
  const settingsPath = path.join(tempRoot, "settings.json");
  const bridge = createSettingsBridgeServer({ settingsPath });
  await bridge.listen(0);
  try {
    const response = await fetch(`${bridge.baseUrl}/import-fbx`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ fbxPath: "C:/Motion/edit.fbx" })
    });
    assert.equal(response.status, 409);
    assert.equal((await response.json()).error, "Unity Play 상태에서만 사용할 수 있습니다.");
    await assert.rejects(fs.readFile(settingsPath, "utf8"), { code: "ENOENT" });
  } finally {
    await bridge.close();
  }
});

function waitForSocketOpen(socket) {
  return new Promise((resolve, reject) => {
    socket.once("open", resolve);
    socket.once("error", reject);
  });
}

async function waitUntil(predicate, timeoutMs = 3000) {
  const startedAt = Date.now();
  while (!predicate()) {
    if (Date.now() - startedAt > timeoutMs) {
      throw new Error("Timed out waiting for condition.");
    }

    await new Promise((resolve) => setTimeout(resolve, 25));
  }
}

test("settings bridge queues character command regardless of play mode", async () => {
  const tempRoot = await fs.mkdtemp(path.join(os.tmpdir(), "settings-bridge-"));
  const settingsPath = path.join(tempRoot, "settings.json");
  await fs.writeFile(settingsPath, JSON.stringify({ runtimeState: { playMode: "stopped" } }), "utf8");
  const bridge = createSettingsBridgeServer({
    settingsPath,
    createCommandId: () => "cmd-char",
    now: () => new Date("2026-10-09T03:00:00.000Z")
  });

  await bridge.listen(0);
  try {
    const response = await fetch(`${bridge.baseUrl}/character-command`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ action: "LoadCharacter", characterId: "char-01" })
    });
    const body = await response.json();
    const saved = JSON.parse(await fs.readFile(settingsPath, "utf8"));

    assert.equal(response.status, 200);
    assert.equal(body.accepted, true);
    assert.equal(body.commandId, "cmd-char");
    assert.equal(saved.pendingCommand.action, "LoadCharacter");
    assert.equal(saved.pendingCommand.characterId, "char-01");
  } finally {
    await bridge.close();
  }
});

test("settings bridge returns last command result through GET /command-result", async () => {
  const tempRoot = await fs.mkdtemp(path.join(os.tmpdir(), "settings-bridge-"));
  const settingsPath = path.join(tempRoot, "settings.json");
  await fs.writeFile(
    settingsPath,
    JSON.stringify({
      lastCommandResult: {
        commandId: "cmd-7",
        action: "ApplyPreset",
        state: "error",
        message: "프리셋 없음",
        finishedAtUtc: "2026-10-09T03:00:00.000Z"
      }
    }),
    "utf8"
  );
  const bridge = createSettingsBridgeServer({ settingsPath });

  await bridge.listen(0);
  try {
    const response = await fetch(`${bridge.baseUrl}/command-result`);
    const body = await response.json();

    assert.equal(response.status, 200);
    assert.equal(body.ok, true);
    assert.equal(body.commandId, "cmd-7");
    assert.equal(body.state, "error");
  } finally {
    await bridge.close();
  }
});

test("settings bridge serves the character library snapshot through GET /characters", async () => {
  const tempRoot = await fs.mkdtemp(path.join(os.tmpdir(), "settings-bridge-"));
  const settingsPath = path.join(tempRoot, "settings.json");
  const libraryPath = path.join(tempRoot, "CharacterLibrary", "characters.json");
  await fs.mkdir(path.dirname(libraryPath), { recursive: true });
  await fs.writeFile(libraryPath, JSON.stringify({
    activeCharacterId: "abc",
    entries: [
      { id: "abc", displayName: "Miku", status: "ready", meta: { author: "YYB" } }
    ]
  }), "utf8");
  const bridge = createSettingsBridgeServer({ settingsPath, libraryPath });

  await bridge.listen(0);
  try {
    const response = await fetch(`${bridge.baseUrl}/characters`);
    const body = await response.json();

    assert.equal(response.status, 200);
    assert.equal(body.ok, true);
    assert.equal(body.activeCharacterId, "abc");
    assert.equal(body.characters.length, 1);
    assert.equal(body.characters[0].displayName, "Miku");
  } finally {
    await bridge.close();
  }
});

test("settings bridge returns an empty snapshot when the library file is missing", async () => {
  const tempRoot = await fs.mkdtemp(path.join(os.tmpdir(), "settings-bridge-"));
  const settingsPath = path.join(tempRoot, "settings.json");
  const libraryPath = path.join(tempRoot, "missing", "characters.json");
  const bridge = createSettingsBridgeServer({ settingsPath, libraryPath });

  await bridge.listen(0);
  try {
    const response = await fetch(`${bridge.baseUrl}/characters`);
    const body = await response.json();

    assert.equal(response.status, 200);
    assert.equal(body.ok, true);
    assert.deepEqual(body.characters, []);
  } finally {
    await bridge.close();
  }
});

test("settings bridge rejects requests from a disallowed Origin", async () => {
  const tempRoot = await fs.mkdtemp(path.join(os.tmpdir(), "settings-bridge-"));
  const settingsPath = path.join(tempRoot, "settings.json");
  const bridge = createSettingsBridgeServer({ settingsPath });

  await bridge.listen(0);
  try {
    const response = await fetch(`${bridge.baseUrl}/character-command`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Origin: "https://evil.example"
      },
      body: JSON.stringify({ action: "RemoveCharacter", characterId: "x" })
    });
    assert.equal(response.status, 403);
    const saved = JSON.parse(await fs.readFile(settingsPath, "utf8").catch(() => "{}"));
    assert.equal(saved.pendingCommand?.commandId ?? "", "");
  } finally {
    await bridge.close();
  }
});

test("settings bridge rejects opaque origin without a bridge token", async () => {
  const tempRoot = await fs.mkdtemp(path.join(os.tmpdir(), "settings-bridge-"));
  const settingsPath = path.join(tempRoot, "settings.json");
  const bridge = createSettingsBridgeServer({ settingsPath });

  await bridge.listen(0);
  try {
    const response = await fetch(`${bridge.baseUrl}/characters`, {
      headers: { Origin: "null" }
    });
    assert.equal(response.status, 403);
  } finally {
    await bridge.close();
  }
});

test("settings bridge accepts opaque origin with a valid bridge token", async () => {
  const tempRoot = await fs.mkdtemp(path.join(os.tmpdir(), "settings-bridge-"));
  const settingsPath = path.join(tempRoot, "settings.json");
  const libraryPath = path.join(tempRoot, "missing", "characters.json");
  const bridge = createSettingsBridgeServer({ settingsPath, libraryPath });

  await bridge.listen(0);
  try {
    // 프로덕션 Electron(file://) 요청 형태 재현 — Origin은 null이고
    // Sec-Fetch-Site는 cross-site여도 토큰이 맞으면 허용해야 한다.
    const response = await fetch(`${bridge.baseUrl}/characters`, {
      headers: {
        Origin: "null",
        "Sec-Fetch-Site": "cross-site",
        "X-Bridge-Token": bridge.token
      }
    });
    assert.equal(response.status, 200);
    assert.equal(response.headers.get("access-control-allow-origin"), "null");
  } finally {
    await bridge.close();
  }
});

test("settings bridge rejects opaque origin with a wrong bridge token", async () => {
  const tempRoot = await fs.mkdtemp(path.join(os.tmpdir(), "settings-bridge-"));
  const settingsPath = path.join(tempRoot, "settings.json");
  const bridge = createSettingsBridgeServer({ settingsPath });

  await bridge.listen(0);
  try {
    const response = await fetch(`${bridge.baseUrl}/characters`, {
      headers: {
        Origin: "null",
        "X-Bridge-Token": "wrong-token"
      }
    });
    assert.equal(response.status, 403);
  } finally {
    await bridge.close();
  }
});

test("settings bridge allows a localhost dev origin and echoes it in CORS", async () => {
  const tempRoot = await fs.mkdtemp(path.join(os.tmpdir(), "settings-bridge-"));
  const settingsPath = path.join(tempRoot, "settings.json");
  const libraryPath = path.join(tempRoot, "missing", "characters.json");
  const bridge = createSettingsBridgeServer({ settingsPath, libraryPath });

  await bridge.listen(0);
  try {
    const response = await fetch(`${bridge.baseUrl}/characters`, {
      headers: { Origin: "http://localhost:3000" }
    });
    assert.equal(response.status, 200);
    assert.equal(response.headers.get("access-control-allow-origin"), "http://localhost:3000");
  } finally {
    await bridge.close();
  }
});

test("settings bridge rejects a second character command while one is pending", async () => {
  const tempRoot = await fs.mkdtemp(path.join(os.tmpdir(), "settings-bridge-"));
  const settingsPath = path.join(tempRoot, "settings.json");
  const bridge = createSettingsBridgeServer({ settingsPath });

  await bridge.listen(0);
  try {
    const first = await fetch(`${bridge.baseUrl}/character-command`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ action: "LoadCharacter", characterId: "char-01" })
    });
    assert.equal(first.status, 200);

    const second = await fetch(`${bridge.baseUrl}/character-command`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ action: "RemoveCharacter", characterId: "char-01" })
    });
    assert.equal(second.status, 400);
    const saved = JSON.parse(await fs.readFile(settingsPath, "utf8"));
    assert.equal(saved.pendingCommand.action, "LoadCharacter");
  } finally {
    await bridge.close();
  }
});

// 재현(S6 전체 녹화 실기): 요청 처리 중 예외가 나도 브리지 프로세스가 죽지 않고 다음 요청을 처리해야 한다.
test("요청 처리 예외는 500 응답으로 바꾸고 서버는 계속 동작한다", async (t) => {
  const root = await fs.mkdtemp(path.join(os.tmpdir(), "bridge-error-"));
  const settingsPath = path.join(root, "settings.json");
  // 상태 파일 자리에 폴더를 두어 읽기가 EISDIR로 실패하게 한다.
  await fs.mkdir(`${settingsPath}.runtime-state.json`);
  const bridge = createSettingsBridgeServer({ settingsPath, port: 0 });
  await bridge.listen();
  t.after(async () => { await bridge.close(); await fs.rm(root, { recursive: true, force: true }); });
  const failed = await fetch(`${bridge.baseUrl}/state`);
  assert.equal(failed.status, 500);
  assert.match((await failed.json()).error, /상태를 읽지 못했습니다/);
  const health = await fetch(`${bridge.baseUrl}/health`);
  assert.equal(health.status, 200);
});
