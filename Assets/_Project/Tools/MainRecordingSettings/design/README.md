# design/ — 크로스 스택 디자인 토큰 정본

이 앱의 색상·폰트·라운딩 의미 토큰을 기계 판독 가능한 형태로 둔다.
`build/styles.css`의 토큰 존(:root / .app-shell / dark 존)과 **값이 1:1로 대응**하며,
`tests/tokenConsistency.test.mjs`(`npm test`에 포함)가 둘의 어긋남을 차단한다.

## 파일

| 파일 | 역할 |
|---|---|
| `tokens.json` | 의미 토큰 정본 — `color.<이름>.{light,dark,css[,scope]}` |
| `out/` | 생성물 — `node scripts/buildTokens.mjs`가 덮어씀 (직접 수정 금지) |

## 다른 프로젝트로 이식

```bash
npm run build:tokens   # design/out/ 재생성
```

| 산출물 | 대상 | 사용 |
|---|---|---|
| `out/tokens.css` | 신규 웹/Electron | `:root`·`[data-theme="dark"]` 커스텀 속성 |
| `out/flet_theme.py` | Flet 앱 (LibraryKeeper, Ticket_AUTO) | `page.theme = build_theme("light")` |
| `out/app_theme.dart` | Flutter 앱 (MuseArchive, Ticket_AUTO_Android) | `ThemeData(extensions: [AppPalette.light])`, 위젯은 `context.appColors.<이름>`만 참조 |
| `out/tokens.uss` | Unity UI Toolkit | `:root`·`.dark` USS 변수 |

토큰을 바꾸려면 **tokens.json과 styles.css 토큰 존을 함께** 수정한 뒤
`npm test`로 정합을 확인한다 — 테스트가 어긋남을 진단한다.
Stitch 산출물 이식 규칙은 `.stitch/token-map.md`와 `DESIGN.md`를 따른다.
