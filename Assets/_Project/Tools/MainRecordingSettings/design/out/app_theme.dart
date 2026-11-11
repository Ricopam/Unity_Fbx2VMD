// 생성물 — design/tokens.json에서 자동 생성됨. 직접 수정 금지 (node scripts/buildTokens.mjs).
import "package:flutter/material.dart";

/// tokens.json 색상에 대응하는 테마 확장. 위젯은 원시값 대신
/// context.appColors.<name>만 참조한다.
class AppPalette extends ThemeExtension<AppPalette> {
  final Color page;
  final Color line;
  final Color text;
  final Color muted;
  final Color accent;
  final Color lbAccent;
  final Color card;
  final Color cardHead;
  final Color inset;
  final Color hover;
  final Color border;
  final Color borderSoft;
  final Color textStrong;
  final Color textSub;
  final Color textFaint;
  final Color accentHover;
  final Color accentDim;
  final Color accentText;
  final Color onAccent;
  final Color ok;
  final Color chipOkText;
  final Color warn;
  final Color track;
  final Color danger;

  const AppPalette({required this.page, required this.line, required this.text, required this.muted, required this.accent, required this.lbAccent, required this.card, required this.cardHead, required this.inset, required this.hover, required this.border, required this.borderSoft, required this.textStrong, required this.textSub, required this.textFaint, required this.accentHover, required this.accentDim, required this.accentText, required this.onAccent, required this.ok, required this.chipOkText, required this.warn, required this.track, required this.danger});

  static final light = AppPalette(
    page: Color.fromRGBO(238, 242, 246, 1),
    line: Color.fromRGBO(216, 224, 232, 1),
    text: Color.fromRGBO(47, 59, 73, 1),
    muted: Color.fromRGBO(154, 162, 171, 1),
    accent: Color.fromRGBO(61, 57, 132, 1),
    lbAccent: Color.fromRGBO(61, 57, 132, 1),
    card: Color.fromRGBO(255, 255, 255, 1),
    cardHead: Color.fromRGBO(247, 249, 253, 1),
    inset: Color.fromRGBO(244, 246, 249, 1),
    hover: Color.fromRGBO(238, 242, 247, 1),
    border: Color.fromRGBO(217, 225, 234, 1),
    borderSoft: Color.fromRGBO(237, 241, 245, 1),
    textStrong: Color.fromRGBO(39, 52, 68, 1),
    textSub: Color.fromRGBO(83, 97, 115, 1),
    textFaint: Color.fromRGBO(109, 116, 130, 1),
    accentHover: Color.fromRGBO(74, 73, 130, 1),
    accentDim: Color.fromRGBO(231, 233, 247, 1),
    accentText: Color.fromRGBO(61, 57, 132, 1),
    onAccent: Color.fromRGBO(255, 255, 255, 1),
    ok: Color.fromRGBO(22, 130, 81, 1),
    chipOkText: Color.fromRGBO(4, 120, 87, 1),
    warn: Color.fromRGBO(138, 91, 0, 1),
    track: Color.fromRGBO(226, 232, 240, 1),
    danger: Color.fromRGBO(220, 38, 38, 1),
  );

  static final dark = AppPalette(
    page: Color.fromRGBO(14, 15, 22, 1),
    line: Color.fromRGBO(38, 43, 61, 1),
    text: Color.fromRGBO(232, 234, 242, 1),
    muted: Color.fromRGBO(107, 113, 144, 1),
    accent: Color.fromRGBO(122, 118, 224, 1),
    lbAccent: Color.fromRGBO(90, 86, 224, 1),
    card: Color.fromRGBO(26, 29, 41, 1),
    cardHead: Color.fromRGBO(30, 33, 48, 1),
    inset: Color.fromRGBO(20, 22, 31, 1),
    hover: Color.fromRGBO(35, 39, 56, 1),
    border: Color.fromRGBO(38, 43, 61, 1),
    borderSoft: Color.fromRGBO(32, 36, 58, 1),
    textStrong: Color.fromRGBO(232, 234, 242, 1),
    textSub: Color.fromRGBO(154, 160, 180, 1),
    textFaint: Color.fromRGBO(134, 140, 162, 1),
    accentHover: Color.fromRGBO(106, 102, 234, 1),
    accentDim: Color.fromRGBO(42, 42, 82, 1),
    accentText: Color.fromRGBO(183, 180, 242, 1),
    onAccent: Color.fromRGBO(255, 255, 255, 1),
    ok: Color.fromRGBO(45, 212, 167, 1),
    chipOkText: Color.fromRGBO(52, 211, 153, 1),
    warn: Color.fromRGBO(201, 168, 106, 1),
    track: Color.fromRGBO(46, 51, 71, 1),
    danger: Color.fromRGBO(248, 113, 113, 1),
  );

  @override
  AppPalette copyWith({Color? page, Color? line, Color? text, Color? muted, Color? accent, Color? lbAccent, Color? card, Color? cardHead, Color? inset, Color? hover, Color? border, Color? borderSoft, Color? textStrong, Color? textSub, Color? textFaint, Color? accentHover, Color? accentDim, Color? accentText, Color? onAccent, Color? ok, Color? chipOkText, Color? warn, Color? track, Color? danger}) => AppPalette(page: page ?? this.page, line: line ?? this.line, text: text ?? this.text, muted: muted ?? this.muted, accent: accent ?? this.accent, lbAccent: lbAccent ?? this.lbAccent, card: card ?? this.card, cardHead: cardHead ?? this.cardHead, inset: inset ?? this.inset, hover: hover ?? this.hover, border: border ?? this.border, borderSoft: borderSoft ?? this.borderSoft, textStrong: textStrong ?? this.textStrong, textSub: textSub ?? this.textSub, textFaint: textFaint ?? this.textFaint, accentHover: accentHover ?? this.accentHover, accentDim: accentDim ?? this.accentDim, accentText: accentText ?? this.accentText, onAccent: onAccent ?? this.onAccent, ok: ok ?? this.ok, chipOkText: chipOkText ?? this.chipOkText, warn: warn ?? this.warn, track: track ?? this.track, danger: danger ?? this.danger);

  @override
  AppPalette lerp(ThemeExtension<AppPalette>? other, double t) {
    if (other is! AppPalette) return this;
    return AppPalette(page: Color.lerp(page, other.page, t)!, line: Color.lerp(line, other.line, t)!, text: Color.lerp(text, other.text, t)!, muted: Color.lerp(muted, other.muted, t)!, accent: Color.lerp(accent, other.accent, t)!, lbAccent: Color.lerp(lbAccent, other.lbAccent, t)!, card: Color.lerp(card, other.card, t)!, cardHead: Color.lerp(cardHead, other.cardHead, t)!, inset: Color.lerp(inset, other.inset, t)!, hover: Color.lerp(hover, other.hover, t)!, border: Color.lerp(border, other.border, t)!, borderSoft: Color.lerp(borderSoft, other.borderSoft, t)!, textStrong: Color.lerp(textStrong, other.textStrong, t)!, textSub: Color.lerp(textSub, other.textSub, t)!, textFaint: Color.lerp(textFaint, other.textFaint, t)!, accentHover: Color.lerp(accentHover, other.accentHover, t)!, accentDim: Color.lerp(accentDim, other.accentDim, t)!, accentText: Color.lerp(accentText, other.accentText, t)!, onAccent: Color.lerp(onAccent, other.onAccent, t)!, ok: Color.lerp(ok, other.ok, t)!, chipOkText: Color.lerp(chipOkText, other.chipOkText, t)!, warn: Color.lerp(warn, other.warn, t)!, track: Color.lerp(track, other.track, t)!, danger: Color.lerp(danger, other.danger, t)!);
  }
}

extension AppPaletteContext on BuildContext {
  AppPalette get appColors => Theme.of(this).extension<AppPalette>()!;
}

ThemeData appTheme({required Brightness brightness}) => ThemeData(
      brightness: brightness,
      fontFamily: "Malgun Gothic",
      extensions: [brightness == Brightness.light ? AppPalette.light : AppPalette.dark],
    );
