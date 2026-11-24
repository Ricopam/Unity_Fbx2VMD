import { expect } from "e2e";
import { test } from "@e2e-dev/web";

// 설정창 핵심 플로우 e2e — 모두 로케이터·결정적 검증이라 모델 없이 실행된다.
// attempt마다 Electron이 새로 뜨고 bridge는 임의 포트·임시 설정 파일을 쓴다.

test("브리지 WebSocket에 연결되면 상태가 연결됨이 된다", async ({ app, browser }) => {
  await app.open();
  await expect(browser.locator("#statusText")).toHaveText("연결됨");
});

test("레일·트리 탐색으로 립싱크 패널을 연다", async ({ app, screen, browser }) => {
  await app.open();
  await screen.getByRole("button", "오디오 및 립싱크").tap();
  await screen.getByRole("button", "립싱크 및 오디오 파이프라인").tap();
  await expect(browser.locator('[data-panel-view="lipsync"]')).toBeVisible();
});

test("FBX 가져오기 요청이 성공 피드백과 이벤트 로그를 남긴다", async ({ app, screen, browser }) => {
  await app.open();
  await screen.getByRole("button", /^모션\(FBX\)/).tap();
  await screen.getByRole("button", "FBX 파일 가져오기").tap();
  await expect(browser.locator("#feedback")).toContainText("보냈습니다");
  // 이벤트 로그는 접힌 영역이므로 펼친 뒤 확인한다.
  await screen.getByText("이벤트 로그").tap();
  await expect(browser.locator("#eventLog")).toContainText("accepted");
});

// Unity 폴러가 없는 e2e 환경에서는 큐잉된 명령이 소비되지 않는다.
// 가져오기 대기 중에는 버튼을 잠가 두 번째 요청 자체를 막는다(브리지 중복 거절은 단위 테스트가 검증).
test("가져오기 응답을 기다리는 동안 두 번째 가져오기를 막는다", async ({ app, screen, browser }) => {
  await app.open();
  await screen.getByRole("button", /^모션\(FBX\)/).tap();
  const importButton = screen.getByRole("button", "FBX 파일 가져오기");
  await importButton.tap();
  await expect(browser.locator("#feedback")).toContainText("보냈습니다");

  await expect(importButton).toBeDisabled();
  await expect(importButton).toHaveText("가져오는 중...");
});

test("테마 토글이 메뉴에서 다크 테마를 끄고 켠다", async ({ app, screen }) => {
  await app.open();
  await screen.getByRole("button", "메뉴").tap();
  const themeToggle = screen.getByRole("menuitemcheckbox");
  await expect(themeToggle).toHaveAttribute("aria-checked", "true");
  await themeToggle.tap();
  await expect(themeToggle).toHaveAttribute("aria-checked", "false");
});

// e2e 모드는 Workbench spawn IPC를 차단하므로 개발자 패널은
// 결정적 오류 경로(상태 메시지 + 재시도 버튼)를 보여야 한다.
test("개발자 모드를 켜면 Workbench 항목이 나타나고 차단 오류를 보인다", async ({ app, screen, browser }) => {
  await app.open();
  await screen.getByRole("switch", "개발자 모드").tap();
  // 개발자 섹션은 캐릭터 사이드바 페이지 안에 있다.
  await screen.getByRole("button", "캐릭터").tap();
  await screen.getByRole("button", "E2E Workbench").tap();
  await expect(browser.locator("#workbenchStatus")).toContainText("Workbench 서버를 시작하지 못했습니다");
});
