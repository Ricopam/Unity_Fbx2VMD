import { expect } from "e2e";
import { test } from "@e2e-dev/web";

// 최소 스모크 — Electron 창이 열리고 핵심 셸 요소가 보이는지 확인한다.
// 홈은 다섯 단계 작업 화면이며 FBX 가져오기는 모션 단계 화면에 있다.
test("설정창이 열리고 모션 단계에서 FBX 가져오기 버튼이 보인다", async ({ app, screen }) => {
  await app.open();
  await expect(screen.getByRole("button", "작업 단계")).toBeVisible();
  await screen.getByRole("button", /^모션\(FBX\)/).tap();
  await expect(screen.getByRole("button", "FBX 파일 가져오기")).toBeVisible();
  await expect(screen.getByRole("button", "메뉴")).toBeVisible();
});
