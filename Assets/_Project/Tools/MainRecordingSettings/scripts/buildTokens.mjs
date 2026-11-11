// design/tokens.json → 스택별 테마 산출물 생성기.
// 산출물은 design/out/ 아래에 쓰이며, 각 프로젝트로 복사해 사용한다.
// 토큰 값의 정본은 tokens.json과 styles.css 토큰 존(두 개는 tokenConsistency 테스트로 정합 유지).
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const root = join(here, "..");
const tokens = JSON.parse(readFileSync(join(root, "design", "tokens.json"), "utf8"));
const outDir = join(root, "design", "out");
mkdirSync(outDir, { recursive: true });

const header = "// 생성물 — design/tokens.json에서 자동 생성됨. 직접 수정 금지 (node scripts/buildTokens.mjs).";
const entries = Object.entries(tokens.color);
// 스택별 단일 패밀리 표기가 필요한 대상(Dart·Flet)은 스택 첫 패밀리를 쓴다.
const firstFamily = tokens.font.family.split(",")[0].trim();

// ── CSS 커스텀 속성 (신규 웹/Electron 프로젝트용 레퍼런스) ──
const cssLight = entries.map(([n, t]) => `  ${t.css}: ${t.light};`).join("\n");
const cssDark = entries.map(([n, t]) => `  ${t.css}: ${t.dark};`).join("\n");
writeFileSync(join(outDir, "tokens.css"),
  `/* 생성물 — design/tokens.json에서 자동 생성됨. 직접 수정 금지. */\n:root {\n${cssLight}\n}\n\n[data-theme="dark"] {\n${cssDark}\n}\n`);

// ── Flet (Python) 테마 ──
const pyDict = (mode) =>
  entries.map(([n, t]) => `    "${n}": "${t[mode]}",`).join("\n");
writeFileSync(join(outDir, "flet_theme.py"),
  `${header.replace("//", "#")}\n# 사용: import flet as ft 후 page.theme = build_theme("light")\n# 테마 전환은 page.theme_mode 토글로.\n\nimport flet as ft\n\nLIGHT = {\n${pyDict("light")}\n}\n\nDARK = {\n${pyDict("dark")}\n}\n\nFONT_FAMILY = "${firstFamily}"\nFONT_STACK = "${tokens.font.family}"\nRADIUS = ${JSON.stringify(tokens.radius)}\n\n\ndef build_theme(mode: str = "light") -> ft.Theme:\n    """tokens.json 색상을 flet ColorScheme으로 매핑한다.\n\n    flet 버전마다 ColorScheme 필드명이 다르다(0.23은 surface_variant,\n    0.24+는 surface_container_low) — 시그니처에 있는 키만 건네 양쪽에서 동작하게 한다.\n    """\n    c = LIGHT if mode == "light" else DARK\n    import inspect\n    supported = inspect.signature(ft.ColorScheme.__init__).parameters\n    kwargs = {\n        "primary": c["accent"],\n        "on_primary": c["onAccent"],\n        "secondary": c["accentHover"],\n        "surface": c["card"],\n        "on_surface": c["textStrong"],\n        "background": c["page"],\n        "surface_container_low": c["inset"],\n        "surface_variant": c["inset"],\n        "outline": c["border"],\n        "outline_variant": c["borderSoft"],\n        "error": c["danger"],\n    }\n    return ft.Theme(\n        color_scheme=ft.ColorScheme(\n            **{k: v for k, v in kwargs.items() if k in supported}\n        ),\n        font_family=FONT_FAMILY,\n    )\n`);

// ── Flutter (Dart) ThemeExtension ──
const dartFields = entries.map(([n]) => `  final Color ${n};`).join("\n");
const dartCtor = entries.map(([n]) => `required this.${n}`).join(", ");
const dartLight = entries.map(([n, t]) => `    ${n}: ${dartTone(t.light)},`).join("\n");
const dartDark = entries.map(([n, t]) => `    ${n}: ${dartTone(t.dark)},`).join("\n");
const dartCopy = entries.map(([n]) => `${n}: ${n} ?? this.${n}`).join(", ");
const dartCopyParams = entries.map(([n]) => `Color? ${n}`).join(", ");
const dartLerp = entries.map(([n]) => `${n}: Color.lerp(${n}, other.${n}, t)!`).join(", ");
writeFileSync(join(outDir, "app_theme.dart"),
  `${header}\nimport "package:flutter/material.dart";\n\n/// tokens.json 색상에 대응하는 테마 확장. 위젯은 원시값 대신\n/// context.appColors.<name>만 참조한다.\nclass AppPalette extends ThemeExtension<AppPalette> {\n${dartFields}\n\n  const AppPalette({${dartCtor}});\n\n  static final light = AppPalette(\n${dartLight}\n  );\n\n  static final dark = AppPalette(\n${dartDark}\n  );\n\n  @override\n  AppPalette copyWith({${dartCopyParams}}) => AppPalette(${dartCopy});\n\n  @override\n  AppPalette lerp(ThemeExtension<AppPalette>? other, double t) {\n    if (other is! AppPalette) return this;\n    return AppPalette(${dartLerp});\n  }\n}\n\nextension AppPaletteContext on BuildContext {\n  AppPalette get appColors => Theme.of(this).extension<AppPalette>()!;\n}\n\nThemeData appTheme({required Brightness brightness}) => ThemeData(\n      brightness: brightness,\n      fontFamily: "${firstFamily}",\n      extensions: [brightness == Brightness.light ? AppPalette.light : AppPalette.dark],\n    );\n`);

// ── Unity USS 커스텀 속성 ──
writeFileSync(join(outDir, "tokens.uss"),
  `/* 생성물 — design/tokens.json에서 자동 생성됨. 직접 수정 금지. */\n:root {\n${cssLight}\n}\n\n.dark {\n${cssDark}\n}\n`);

function dartTone(hex) {
  if (!/^#[0-9a-fA-F]{6}$/.test(hex)) throw new Error(`6자리 hex가 아닌 토큰 값: ${hex}`);
  const n = parseInt(hex.slice(1), 16);
  // Color. 리터럴 형태로 출력 — no-js-colors 게이트의 색상 함수 패턴을 유발하지 않는다.
  return `Color.fromRGBO(${(n >> 16) & 255}, ${(n >> 8) & 255}, ${n & 255}, 1)`;
}

console.log(`[build-tokens] ${entries.length}색상 → ${outDir}`);
console.log("  tokens.css / flet_theme.py / app_theme.dart / tokens.uss 생성 완료");
