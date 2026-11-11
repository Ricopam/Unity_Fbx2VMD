# 생성물 — design/tokens.json에서 자동 생성됨. 직접 수정 금지 (node scripts/buildTokens.mjs).
# 사용: import flet as ft 후 page.theme = build_theme("light")
# 테마 전환은 page.theme_mode 토글로.

import flet as ft

LIGHT = {
    "page": "#eef2f6",
    "line": "#d8e0e8",
    "text": "#2f3b49",
    "muted": "#9aa2ab",
    "accent": "#3d3984",
    "lbAccent": "#3d3984",
    "card": "#ffffff",
    "cardHead": "#f7f9fd",
    "inset": "#f4f6f9",
    "hover": "#eef2f7",
    "border": "#d9e1ea",
    "borderSoft": "#edf1f5",
    "textStrong": "#273444",
    "textSub": "#536173",
    "textFaint": "#6d7482",
    "accentHover": "#4a4982",
    "accentDim": "#e7e9f7",
    "accentText": "#3d3984",
    "onAccent": "#ffffff",
    "ok": "#168251",
    "chipOkText": "#047857",
    "warn": "#8a5b00",
    "track": "#e2e8f0",
    "danger": "#dc2626",
}

DARK = {
    "page": "#0e0f16",
    "line": "#262b3d",
    "text": "#e8eaf2",
    "muted": "#6b7190",
    "accent": "#7a76e0",
    "lbAccent": "#5a56e0",
    "card": "#1a1d29",
    "cardHead": "#1e2130",
    "inset": "#14161f",
    "hover": "#232738",
    "border": "#262b3d",
    "borderSoft": "#20243a",
    "textStrong": "#e8eaf2",
    "textSub": "#9aa0b4",
    "textFaint": "#868ca2",
    "accentHover": "#6a66ea",
    "accentDim": "#2a2a52",
    "accentText": "#b7b4f2",
    "onAccent": "#ffffff",
    "ok": "#2dd4a7",
    "chipOkText": "#34d399",
    "warn": "#c9a86a",
    "track": "#2e3347",
    "danger": "#f87171",
}

FONT_FAMILY = "Malgun Gothic"
FONT_STACK = "Malgun Gothic, Apple SD Gothic Neo, Noto Sans KR, system-ui, sans-serif"
RADIUS = {"sm":4,"md":8,"lg":16,"xl":24}


def build_theme(mode: str = "light") -> ft.Theme:
    """tokens.json 색상을 flet ColorScheme으로 매핑한다.

    flet 버전마다 ColorScheme 필드명이 다르다(0.23은 surface_variant,
    0.24+는 surface_container_low) — 시그니처에 있는 키만 건네 양쪽에서 동작하게 한다.
    """
    c = LIGHT if mode == "light" else DARK
    import inspect
    supported = inspect.signature(ft.ColorScheme.__init__).parameters
    kwargs = {
        "primary": c["accent"],
        "on_primary": c["onAccent"],
        "secondary": c["accentHover"],
        "surface": c["card"],
        "on_surface": c["textStrong"],
        "background": c["page"],
        "surface_container_low": c["inset"],
        "surface_variant": c["inset"],
        "outline": c["border"],
        "outline_variant": c["borderSoft"],
        "error": c["danger"],
    }
    return ft.Theme(
        color_scheme=ft.ColorScheme(
            **{k: v for k, v in kwargs.items() if k in supported}
        ),
        font_family=FONT_FAMILY,
    )
