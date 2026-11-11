// design/tokens.json ↔ build/styles.css 토큰 존의 정합 게이트.
// 두 정본이 어긋나면 실패 — 스택별 산출물(design/out/)이 CSS와 다른 값을 싣는 것을 막는다.
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";
import assert from "node:assert/strict";

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, "..");
const tokens = JSON.parse(readFileSync(join(root, "design", "tokens.json"), "utf8"));
const css = readFileSync(join(root, "build", "styles.css"), "utf8");

// 셀렉터 블록의 { ... } 본문에서 --name: value 선언을 수집한다.
function declarationsOf(selectorPattern) {
  const match = css.match(selectorPattern);
  if (!match) return null;
  const open = css.indexOf("{", match.index);
  let depth = 0, end = open;
  for (; end < css.length; end++) {
    if (css[end] === "{") depth++;
    else if (css[end] === "}" && --depth === 0) break;
  }
  const body = css.slice(open, end);
  const decls = {};
  for (const m of body.matchAll(/(--[\w-]+)\s*:\s*([^;]+);/g)) {
    decls[m[1]] = m[2].trim();
  }
  return decls;
}

const zones = {
  light: {
    ...declarationsOf(/:root\s*\{/),
    ...declarationsOf(/\/\* ── 하체 보정 패널 토큰[^/]*\*\/\s*\.app-shell\s*\{/),
  },
  dark: {
    ...declarationsOf(/\.settings-app\[data-theme="dark"\]\s*\{/),
    ...declarationsOf(/\.settings-app\[data-theme="dark"\]\s*\.app-shell\s*\{/),
  },
};

test("tokens.json의 모든 색상이 styles.css 토큰 존과 동일 값이다", () => {
  const problems = [];
  for (const [name, t] of Object.entries(tokens.color)) {
    for (const mode of ["light", "dark"]) {
      // 다크 존에 재정의가 없으면 라이트 값을 상속 — 그 경우 dark == light 여야 한다.
      const actual = zones[mode][t.css] ?? (mode === "dark" ? zones.light[t.css] : undefined);
      if (actual === undefined) {
        problems.push(`${t.css} (${name}.${mode}): styles.css에 선언 없음`);
      } else if (actual.toLowerCase() !== t[mode].toLowerCase()) {
        problems.push(`${t.css} (${name}.${mode}): tokens.json=${t[mode]} ≠ styles.css=${actual}`);
      }
    }
  }
  assert.deepEqual(problems, [], problems.join("\n"));
});
