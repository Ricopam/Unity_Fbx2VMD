import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

// UI 감사 모드 — 렌더된 화면을 상태별로 캡처하고 결정적 DOM 검사를 실행해
// audit.json/report.md를 남긴다. 코드 리뷰 하네스의 UI 대응물로,
// 결정적 실패만 종료코드로 게이트하고 휴리스틱·주관 항목은 기록만 한다.
const UI_AUDIT_PREFIX = "--ui-audit-dir=";
const UI_AUDIT_BASELINE_WRITE = "--ui-audit-write-baseline";
const OVERALL_TIMEOUT_MS = 90000;
const STATE_SETTLE_MS = 150;

export function isUiAuditMode({ argv = process.argv } = {}) {
  return argv.some((arg) => arg.startsWith(UI_AUDIT_PREFIX));
}

export function getUiAuditDir({ argv = process.argv } = {}) {
  const arg = argv.find((value) => typeof value === "string" && value.startsWith(UI_AUDIT_PREFIX));
  return arg ? arg.slice(UI_AUDIT_PREFIX.length).trim() : "";
}

export function isUiAuditBaselineWrite({ argv = process.argv } = {}) {
  return argv.includes(UI_AUDIT_BASELINE_WRITE);
}

export function attachUiAudit({ app, mainWindow, bridge, applyConfig }) {
  const outDir = getUiAuditDir();
  if (!outDir) {
    console.log("UI_AUDIT_FAIL missing --ui-audit-dir");
    app.exit(2);
    return;
  }

  let finished = false;
  const finish = (code, message) => {
    if (finished) {
      return;
    }
    finished = true;
    clearTimeout(timeout);
    console.log(message);
    app.exit(code);
  };

  const timeout = setTimeout(() => {
    finish(2, `UI_AUDIT_TIMEOUT ${outDir}`);
  }, OVERALL_TIMEOUT_MS);

  // 콘솔 메시지는 실행 내내 누적 — 오류·경고는 휴리스틱으로만 기록한다.
  // (브리지 미연결 등 환경 의존 메시지가 섞이므로 게이트 대상이 아니다.)
  const consoleMessages = [];
  const LEVEL_NAMES = { verbose: 0, debug: 0, info: 1, warning: 2, error: 3 };
  mainWindow.webContents.on("console-message", (event) => {
    const level = typeof event.level === "number" ? event.level : (LEVEL_NAMES[event.level] ?? 1);
    consoleMessages.push({ level, message: event.message, line: event.lineNumber, sourceId: event.sourceId });
  });

  mainWindow.webContents.once("did-fail-load", (_event, errorCode, errorDescription, validatedURL) => {
    finish(2, `UI_AUDIT_LOAD_FAIL ${errorCode} ${errorDescription} ${validatedURL}`);
  });

  mainWindow.webContents.once("did-finish-load", async () => {
    try {
      await applyConfig(mainWindow, bridge);
      const baselinePath = path.resolve(fileURLToPath(import.meta.url), "../../tests/ui-audit-baseline.json");
      const result = await runUiAudit({ mainWindow, outDir, consoleMessages, baselinePath });
      await writeAuditOutputs({ outDir, result });
      if (isUiAuditBaselineWrite()) {
        await writeBaseline({ baselinePath, result });
      }
      // 베이스라인 기록 모드는 "수용 확정" 동작 — 게이트 결과와 무관하게 성공 종료한다.
      const code = isUiAuditBaselineWrite() ? 0 : (result.summary.deterministicFailures > 0 ? 1 : 0);
      finish(code, `UI_AUDIT_DONE new=${result.summary.deterministicFailures} baseline=${result.summary.baselineAccepted} heuristic=${result.summary.heuristicRisks} out=${outDir}`);
    } catch (error) {
      finish(2, `UI_AUDIT_ERROR ${error.message}`);
    }
  });
}

async function runUiAudit({ mainWindow, outDir, consoleMessages, baselinePath }) {
  await fs.mkdir(outDir, { recursive: true });
  await mainWindow.webContents.executeJavaScript("document.fonts.ready.then(() => true)");
  await delay(STATE_SETTLE_MS);

  const states = [];
  const runState = async (name, prepareSource) => {
    if (prepareSource) {
      await mainWindow.webContents.executeJavaScript(prepareSource);
      await delay(STATE_SETTLE_MS);
    }
    const screenshot = `${name}.png`;
    const image = await mainWindow.webContents.capturePage();
    await fs.writeFile(path.join(outDir, screenshot), image.toPNG());
    const findings = await mainWindow.webContents.executeJavaScript(`(${collectPageFindings.toString()})()`);
    states.push({ name, screenshot, findings: findings ?? [] });
  };

  const panelTargets = await mainWindow.webContents.executeJavaScript(
    "[...document.querySelectorAll('[data-panel-target]')].map((button) => button.dataset.panelTarget)"
  );

  // 초기 DOM은 data-theme="dark"(index.html)이고 light는 속성 부재로 표현된다.
  // 각 상태를 명시 지정한 뒤 원래 값으로 복원해 이후 캡처가 실제 기본 테마를 쓰게 한다.
  const shellExpr = "document.querySelector('.settings-app')";
  const originalTheme = await mainWindow.webContents.executeJavaScript(
    `(() => { const s = ${shellExpr}; return s ? (s.dataset.theme ?? null) : null; })()`
  );
  await runState("main-light", `${shellExpr} && (delete ${shellExpr}.dataset.theme)`);
  await runState("main-dark", `${shellExpr} && (${shellExpr}.dataset.theme = 'dark')`);
  // theme을 원복하고 다음 상태를 진행 — 복원 캡처는 산출물이 아니므로 찍지 않는다.
  await mainWindow.webContents.executeJavaScript(
    originalTheme == null
      ? `${shellExpr} && (delete ${shellExpr}.dataset.theme)`
      : `${shellExpr} && (${shellExpr}.dataset.theme = ${JSON.stringify(originalTheme)})`
  );

  const originalSize = mainWindow.getSize();
  const originalMinSize = mainWindow.getMinimumSize();
  try {
    mainWindow.setMinimumSize(1, 1);
    mainWindow.setSize(480, 675);
    await runState("main-narrow", null);
  } finally {
    mainWindow.setMinimumSize(originalMinSize[0], originalMinSize[1]);
    mainWindow.setSize(originalSize[0], originalSize[1]);
  }

  for (const target of panelTargets ?? []) {
    await runState(
      `panel-${target}`,
      `document.querySelector('[data-panel-target=' + ${JSON.stringify(target)} + ']')?.click()`
    );
  }

  // 같은 요소가 여러 상태에서 적발되면 하나로 합산 — 상태 수만큼 부풀려
  // 보고되지 않게 id|selector 기준으로 중복을 제거한다.
  const deterministicMap = new Map();
  const heuristic = [];
  for (const state of states) {
    for (const finding of state.findings) {
      if (finding.severity === "deterministic") {
        const key = `${finding.id}|${finding.selector}`;
        if (!deterministicMap.has(key)) {
          deterministicMap.set(key, { state: state.name, ...finding });
        }
      } else {
        heuristic.push({ state: state.name, ...finding });
      }
    }
  }
  const consoleFindings = (consoleMessages ?? []).filter((entry) => entry.level >= 2)
    .map((entry) => ({ severity: "heuristic", id: "console-message", message: `console level=${entry.level} ${entry.message}` }));

  // 기존 부채는 베이스라인에 등록된 경우만 게이트를 통과 — 게이트는
  // "이번 변경에서 새로 생긴" 결정적 실패만 책임진다(design-baseline 래칫과 동일).
  const baseline = await readBaseline(baselinePath);
  const deterministic = [];
  const baselineAccepted = [];
  for (const finding of deterministicMap.values()) {
    const key = `${finding.id}|${finding.selector}`;
    (baseline.has(key) ? baselineAccepted : deterministic).push(finding);
  }

  return {
    states,
    consoleMessages: consoleMessages ?? [],
    deterministic,
    baselineAccepted,
    heuristic: [...heuristic, ...consoleFindings],
    summary: {
      deterministicFailures: deterministic.length,
      baselineAccepted: baselineAccepted.length,
      heuristicRisks: heuristic.length + consoleFindings.length,
      statesCaptured: states.length
    }
  };
}

async function readBaseline(baselinePath) {
  try {
    const parsed = JSON.parse(await fs.readFile(baselinePath, "utf8"));
    return new Map(Object.entries(parsed.accepted ?? {}));
  } catch {
    return new Map();
  }
}

async function writeBaseline({ baselinePath, result }) {
  const accepted = {};
  for (const finding of result.deterministic.concat(result.baselineAccepted)) {
    accepted[`${finding.id}|${finding.selector}`] = {
      message: finding.message,
      firstSeen: new Date().toISOString(),
      state: finding.state
    };
  }
  await fs.writeFile(baselinePath, JSON.stringify({ accepted }, null, 2) + "\n");
}

// 페이지 내부에서 실행되는 자기완결 검사 함수 — 외부 스코프를 참조하지 않는다.
function collectPageFindings() {
  const findings = [];
  const counts = {};
  const CAP_PER_RULE = 20;
  const add = (id, severity, message, el) => {
    counts[id] = (counts[id] ?? 0) + 1;
    if (counts[id] > CAP_PER_RULE) return;
    findings.push({ id, severity, message, selector: el ? describe(el) : "" });
  };
  const describe = (el) => {
    const id = el.id ? `#${el.id}` : "";
    const cls = typeof el.className === "string" && el.className.trim()
      ? `.${el.className.trim().split(/\s+/).slice(0, 2).join(".")}`
      : "";
    // 같은 서명 요소가 여럿이면 형제 내 위치를 붙여 dedupe·baseline 키가
    // 서로 다른 요소를 하나로 합치지 않게 한다.
    const sibs = el.parentElement
      ? [...el.parentElement.children].filter((c) => c.tagName === el.tagName)
      : [el];
    const nth = sibs.indexOf(el);
    return `${el.tagName.toLowerCase()}${id}${cls}${nth > 0 ? `~${nth + 1}` : ""}`;
  };
  const isVisible = (el) => {
    const rect = el.getBoundingClientRect();
    const style = getComputedStyle(el);
    return rect.width > 0 && rect.height > 0 && style.visibility !== "hidden" && style.display !== "none";
  };

  const view = document.querySelector("[data-panel-view]:not([hidden])");
  if (view && view.scrollWidth > view.clientWidth + 1) {
    add("horizontal-overflow", "deterministic", `활성 패널 가로 넘침 (${view.scrollWidth}>${view.clientWidth})`, view);
  }

  for (const el of document.querySelectorAll("button")) {
    if (!isVisible(el)) continue;
    const name = (el.textContent || "").trim() || el.getAttribute("aria-label") || el.getAttribute("aria-labelledby") || el.getAttribute("title") || (el.querySelector("img[alt]") ? "img" : "");
    if (!name) add("unlabeled-control", "deterministic", "텍스트·레이블 없는 버튼", el);
  }
  for (const el of document.querySelectorAll("input,select,textarea")) {
    if (!isVisible(el)) continue;
    const labeled = el.id && document.querySelector(`label[for="${CSS.escape(el.id)}"]`);
    const named = labeled || el.getAttribute("aria-label") || el.getAttribute("aria-labelledby") || el.getAttribute("placeholder") || el.closest("label");
    if (!named) add("unlabeled-input", "deterministic", "레이블 없는 입력 필드", el);
  }
  for (const el of document.querySelectorAll("img")) {
    if (!isVisible(el)) continue;
    if (el.getAttribute("alt") == null) add("missing-alt", "deterministic", "alt 속성 없는 이미지", el);
  }

  const textElements = [];
  for (const el of document.querySelectorAll("body *")) {
    if (!isVisible(el)) continue;
    const hasDirectText = [...el.childNodes].some((n) => n.nodeType === 3 && n.textContent.trim());
    if (hasDirectText) textElements.push(el);
  }

  const parseChannels = (value) => {
    const m = /rgba?\(\s*([\d.]+)[\s,]+([\d.]+)[\s,]+([\d.]+)(?:[\s,/]+([\d.]+))?\s*\)/.exec(value ?? "");
    if (!m) return null;
    return { r: Number(m[1]), g: Number(m[2]), b: Number(m[3]), a: m[4] == null ? 1 : Number(m[4]) };
  };
  const luminance = ({ r, g, b }) => {
    const f = (v) => {
      const s = v / 255;
      return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
    };
    return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
  };
  const effectiveBackground = (el) => {
    // 반투명 조상 배경을 불투명으로 취급하면 칩(알파 틴트) 같은 요소의 대비가
    // 크게 오측정된다 — 조상을 위로 올라가며 알파 합성으로 실측 배경을 복원한다.
    let node = el;
    let acc = { r: 255, g: 255, b: 255 };
    while (node && node !== document.documentElement) {
      const color = parseChannels(getComputedStyle(node).backgroundColor);
      if (color && color.a > 0) {
        acc = {
          r: color.a * color.r + (1 - color.a) * acc.r,
          g: color.a * color.g + (1 - color.a) * acc.g,
          b: color.a * color.b + (1 - color.a) * acc.b,
        };
        if (color.a >= 0.99) break;
      }
      node = node.parentElement;
    }
    return acc;
  };

  for (const el of textElements) {
    const style = getComputedStyle(el);
    const size = parseFloat(style.fontSize);
    if (size > 0 && size < 10) {
      add("tiny-text", "heuristic", `10px 미만 텍스트(${size}px)`, el);
    }
    const fg = parseChannels(style.color);
    if (!fg || fg.a < 0.5) continue;
    const bg = effectiveBackground(el);
    // 텍스트 알파는 배경 위에 합성된 채로 렌더된다 — WCAG 대비도 합성 색 기준.
    if (fg.a < 1) {
      fg.r = fg.a * fg.r + (1 - fg.a) * bg.r;
      fg.g = fg.a * fg.g + (1 - fg.a) * bg.g;
      fg.b = fg.a * fg.b + (1 - fg.a) * bg.b;
    }
    const ratio = (Math.max(luminance(fg), luminance(bg)) + 0.05) / (Math.min(luminance(fg), luminance(bg)) + 0.05);
    const weight = parseInt(style.fontWeight, 10) || 400;
    const large = size >= 24 || (size >= 18.66 && weight >= 700);
    const min = large ? 3 : 4.5;
    if (ratio < min) {
      add("low-contrast", "deterministic", `대비 ${ratio.toFixed(2)}:1 (기준 ${min}:1)`, el);
    }
  }

  for (const el of document.querySelectorAll("a")) {
    if (!isVisible(el)) continue;
    const href = el.getAttribute("href");
    if (!href || href === "#") add("dead-link", "heuristic", "href가 없거나 placeholder인 링크", el);
  }
  return findings;
}

async function writeAuditOutputs({ outDir, result }) {
  const audit = {
    generatedAt: new Date().toISOString(),
    states: result.states,
    deterministic: result.deterministic,
    baselineAccepted: result.baselineAccepted,
    heuristic: result.heuristic,
    consoleMessages: result.consoleMessages,
    summary: result.summary
  };
  await fs.writeFile(path.join(outDir, "audit.json"), JSON.stringify(audit, null, 2));

  const lines = [
    "# UI Audit Report",
    "",
    `- 생성: ${audit.generatedAt}`,
    `- 상태 캡처: ${result.summary.statesCaptured}`,
    `- 신규 결정적 실패: ${result.summary.deterministicFailures}`,
    `- 기존 수용(베이스라인): ${result.summary.baselineAccepted}`,
    `- 휴리스틱 리스크: ${result.summary.heuristicRisks}`,
    "",
    "판정 규칙: 신규 결정적 실패만 게이트 대상. 베이스라인 수용 항목과",
    "휴리스틱은 사람 또는 리뷰 에이전트가 판단한다. 베이스라인 갱신은",
    "`npm run audit:ui:baseline` — 갱신 자체가 리뷰 대상이다.",
    ""
  ];
  const section = (title, items) => {
    lines.push(`## ${title} (${items.length})`, "");
    if (items.length === 0) {
      lines.push("- 없음", "");
      return;
    }
    for (const item of items) {
      const shot = item.state ? ` — 증거: ${item.state}.png` : "";
      const sel = item.selector ? ` [${item.selector}]` : "";
      lines.push(`- \`${item.id}\` ${item.message}${sel}${shot}`);
    }
    lines.push("");
  };
  section("신규 결정적 실패", result.deterministic);
  section("기존 수용(베이스라인)", result.baselineAccepted);
  section("휴리스틱 리스크", result.heuristic);
  lines.push("## 캡처된 상태", "");
  for (const state of result.states) {
    lines.push(`- ${state.name}: ${state.screenshot} (findings ${state.findings.length})`);
  }
  lines.push("");
  await fs.writeFile(path.join(outDir, "report.md"), lines.join("\n"));
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
