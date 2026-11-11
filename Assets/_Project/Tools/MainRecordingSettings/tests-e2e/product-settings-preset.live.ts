import fs from "node:fs/promises";
import path from "node:path";
import assert from "node:assert/strict";
import { test } from "@e2e-dev/web";
import { expect } from "e2e";
import { createUnityClient, UNITY_COMMANDS } from "./unity/unityClient.js";

test("실제 설정창 프리셋 저장·기본 지정·적용 경로를 확인한다", { timeout: 900000 }, async ({ app, screen, browser }) => {
  const fixture = JSON.parse(await fs.readFile(process.env.FBX2VMD_E2E_SESSION_FILE!, "utf8"));
  const unity = createUnityClient({ projectRoot: fixture.projectRoot });
  const read = async (file: string) => JSON.parse(await fs.readFile(file, "utf8"));
  const directory = path.dirname(fixture.settingsPath);
  const evidence: Record<string, unknown> = { runId: fixture.runId, scope: "preset" };
  const capture = async () => {
    const status = await unity.send(UNITY_COMMANDS.environment, { timeoutMs: 240_000 });
    return read(status.environment_state_path!);
  };
  const waitCommand = async (action: string, previousId: string) => {
    const deadline = Date.now() + 120000;
    while (Date.now() < deadline) {
      const result = (await read(fixture.settingsPath)).lastCommandResult;
      if (result?.action === action && result.commandId && result.commandId !== previousId && result.state !== "running") {
        assert.equal(result.state, "success", result.message);
        evidence[action] = result;
        return result;
      }
      await new Promise(resolve => setTimeout(resolve, 500));
    }
    throw new Error(`${action} 실제 처리 결과 대기 시간 초과`);
  };
  try {
    evidence.before = await capture();
    assert.equal((evidence.before as { play_mode: boolean }).play_mode, true);
    await app.open();
    await expect(browser.locator("#statusText")).toHaveText("연결됨");
    await screen.getByRole("button", "캐릭터").tap();
    await screen.getByRole("button", "캐릭터 라이브러리").tap();
    let lastId = (await read(fixture.settingsPath)).lastCommandResult?.commandId ?? "";
    await screen.getByRole("button", "VRM 파일 추가…").tap();
    const registered = await waitCommand("RegisterVrm", lastId);
    const library = await read(fixture.libraryPath);
    assert.equal(library.entries.length, 1);
    const entryId = library.entries[0].id;
    await screen.getByRole("button", "불러오기 (Load)").tap();
    lastId = (await waitCommand("LoadCharacter", registered.commandId)).commandId;
    assert.equal((await read(fixture.libraryPath)).activeCharacterId, entryId);
    const loaded = await capture();
    assert.equal(loaded.avatar_valid, true);

    await screen.getByRole("button", "현재 상태 저장").tap();
    lastId = (await waitCommand("SavePreset", lastId)).commandId;
    const presetDocPath = path.join(directory, "presets", `${entryId}.json`);
    const presetDoc = await read(presetDocPath);
    assert.equal(presetDoc.presets.length, 1);
    evidence.savedPresetId = presetDoc.presets[0].id;
    assert.equal((await read(fixture.libraryPath)).entries[0].defaultPresetId, presetDoc.presets[0].id);

    await screen.getByRole("button", "적용").tap();
    lastId = (await waitCommand("ApplyPreset", lastId)).commandId;

    await screen.getByRole("button", "기본 지정").tap();
    lastId = (await waitCommand("SetDefaultPreset", lastId)).commandId;
    evidence.controlsPassed = true;
  } finally {
    await fs.writeFile(path.join(directory, "preset-live-result.json"), JSON.stringify(evidence, null, 2));
  }
});
