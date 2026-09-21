import { memo, type ReactNode } from "react";
import { Platform, StyleSheet, Text, View, type TextStyle } from "react-native";
import {
  computePosterSaving,
  formatPosterDay,
  formatPosterMoney,
  formatPosterValidity,
  splitPosterPrice,
} from "@/modules/promo-posters/logic";
import type { PromoPosterKind, PromoPosterSize, PromoPosterStyle } from "@/modules/promo-posters/types";

/**
 * 海报实时预览：用 RN View/Text 近似还原后端 PDF 的三种风格。
 * 做法：按设计稿的像素尺寸（A4 = 794 × 1123）排版，再整体 scale 到目标宽度，
 * 这样各尺寸的字号比例与设计稿一致；字体用系统自带的窄体/粗体近似，最终以 PDF 为准。
 */
export interface PromoPosterPreviewData {
  kind: PromoPosterKind;
  style: PromoPosterStyle;
  size: PromoPosterSize;
  title: string;
  itemNumber: string;
  /** 价格无效时为 null，预览显示占位。 */
  price: number | null;
  wasPrice?: number | null;
  quantity?: number | null;
  unitPrice?: number | null;
  mixAndMatch?: boolean;
  validFrom?: string;
  validTo?: string;
  inStoreSince?: string;
  showLogo?: boolean;
}

interface PromoPosterPreviewProps {
  data: PromoPosterPreviewData;
  /** 预览宽度（dp），高度按纸张比例自动计算。 */
  width: number;
}

// ---------------------------------------------------------------- 设计稿常量（gen.py / posters_b.py）

const RED = "#D40F16";
const GREEN = "#0A7A45";
const YELLOW = "#FFD000";
const INK = "#17181A";
const GREY = "#475467";
const LINE = "#D0D5DD";
const LOGO_NAVY = "#1D3A8A";

/** 经典风格各尺寸参数：m 白边、b 框线、pad 内边距、bpy/bpx 横幅内边距、K 新品标签、n 品名字号、P 价格字号上限、wz 补充信息字号、f 页脚字号。 */
const CLASSIC_SIZES = {
  A4: { w: 794, h: 1123, m: 23, b: 4, pad: 36, bpy: 22, bpx: 28, K: 150, n: 60, P: 430, wz: 34, f: 17, fpy: 12, gap: 18, sh: 18, lg: 50, short: false },
  A5: { w: 559, h: 794, m: 19, b: 3, pad: 24, bpy: 15, bpx: 20, K: 104, n: 40, P: 290, wz: 25, f: 13, fpy: 9, gap: 12, sh: 13, lg: 36, short: false },
  A6: { w: 397, h: 559, m: 19, b: 3, pad: 16, bpy: 11, bpx: 14, K: 72, n: 27, P: 200, wz: 18, f: 12, fpy: 7, gap: 8, sh: 10, lg: 28, short: false },
  A7: { w: 280, h: 397, m: 15, b: 2, pad: 12, bpy: 8, bpx: 10, K: 50, n: 18, P: 138, wz: 14, f: 12, fpy: 6, gap: 5, sh: 7, lg: 22, short: true },
} as const;

/** 现代风格各尺寸参数：R 色块圆角、fp 色块内边距、wcap 标题字号上限、pr 面板圆角、S 贴纸直径、info 补充信息字号。 */
const MODERN_SIZES = {
  A4: { w: 794, h: 1123, m: 23, R: 32, fp: 24, wcap: 190, pad: 32, pr: 24, n: 50, P: 380, S: 176, f: 16, lg: 40, gap: 20, info: 30, short: false },
  A5: { w: 559, h: 794, m: 19, R: 24, fp: 17, wcap: 134, pad: 22, pr: 18, n: 35, P: 265, S: 124, f: 13, lg: 28, gap: 14, info: 21, short: false },
  A6: { w: 397, h: 559, m: 19, R: 18, fp: 12, wcap: 95, pad: 15, pr: 14, n: 24, P: 185, S: 88, f: 12, lg: 22, gap: 10, info: 15, short: false },
  A7: { w: 280, h: 397, m: 15, R: 12, fp: 9, wcap: 66, pad: 11, pr: 10, n: 17, P: 128, S: 62, f: 12, lg: 18, gap: 7, info: 12, short: true },
} as const;

/** 省彩墨版仅标题和短线着色；参数与 LowInkPosterPainter 保持一致。 */
const LOW_INK_SIZES = {
  A4: { w: 794, h: 1123, m: 44, lg: 40, label: 38, n: 52, P: 340, info: 28, f: 17, gap: 18 },
  A5: { w: 559, h: 794, m: 32, lg: 30, label: 27, n: 37, P: 240, info: 20, f: 13, gap: 13 },
  A6: { w: 397, h: 559, m: 24, lg: 23, label: 19, n: 26, P: 168, info: 15, f: 11, gap: 9 },
  A7: { w: 280, h: 397, m: 19, lg: 18, label: 15, n: 19, P: 116, info: 12, f: 10, gap: 7 },
} as const;

type ClassicSize = (typeof CLASSIC_SIZES)[PromoPosterSize];
type ModernSize = (typeof MODERN_SIZES)[PromoPosterSize];
type LowInkSize = (typeof LOW_INK_SIZES)[PromoPosterSize];

const LOW_INK_THEME: Record<PromoPosterKind, { label: string; color: string }> = {
  special: { label: "SPECIAL", color: "#C6222A" },
  multibuy: { label: "MULTI-BUY", color: "#C6222A" },
  new: { label: "NEW ARRIVAL", color: "#176447" },
  clearance: { label: "CLEARANCE", color: "#C6222A" },
};

const MODERN_THEME: Record<PromoPosterKind, { bg: string; on: string; price: string; word: string; stickerBg: string; stickerFg: string }> = {
  special: { bg: "#E4252C", on: "#FFFFFF", price: "#E4252C", word: "special", stickerBg: INK, stickerFg: "#FFFFFF" },
  multibuy: { bg: "#FF6B00", on: INK, price: INK, word: "multi-buy", stickerBg: INK, stickerFg: "#FFFFFF" },
  new: { bg: "#2248F0", on: "#FFFFFF", price: "#2248F0", word: "new in", stickerBg: INK, stickerFg: "#FFFFFF" },
  clearance: { bg: "#FFD500", on: INK, price: INK, word: "clearance", stickerBg: INK, stickerFg: "#FFD500" },
};

// 系统字体近似：iOS 用 Avenir Next 窄体/粗体；Android 用 Roboto Condensed / Black。
const FONT_CONDENSED: TextStyle = Platform.select<TextStyle>({
  ios: { fontFamily: "AvenirNextCondensed-Heavy" },
  android: { fontFamily: "sans-serif-condensed", fontWeight: "700" },
  default: { fontWeight: "900" },
});
const FONT_HEAVY: TextStyle = Platform.select<TextStyle>({
  ios: { fontFamily: "AvenirNext-Heavy" },
  android: { fontFamily: "sans-serif-black" },
  default: { fontWeight: "900" },
});
const FONT_BOLD: TextStyle = Platform.select<TextStyle>({
  ios: { fontFamily: "AvenirNext-Bold" },
  android: { fontFamily: "sans-serif", fontWeight: "700" },
  default: { fontWeight: "700" },
});

/** 窄体数字/大写字母的平均字宽（em），用于估算大字能否放下；再配合 adjustsFontSizeToFit 兜底。 */
const CONDENSED_EM = 0.52;

function round(value: number) {
  return Math.round(value);
}

function textStyle(fontSize: number, lineHeightRatio = 1.1): TextStyle {
  // Android 行高小于字号会裁掉字形顶部，至少取 1 倍字号。
  const ratio = Platform.OS === "android" ? Math.max(lineHeightRatio, 1) : lineHeightRatio;
  return { fontSize, lineHeight: round(fontSize * ratio), includeFontPadding: false };
}

// ---------------------------------------------------------------- 公共块

/** 大价格：$ 与角分上标并与整数顶端对齐；price 为 null 时显示占位。 */
function BigPrice({ value, size, color, underlineCents }: { value: number | null; size: number; color: string; underlineCents: boolean }) {
  const { dollars, cents } = value === null ? { dollars: "--", cents: "" } : splitPosterPrice(value);
  const dollarSize = size * 0.36;
  const centSize = size * 0.42;
  // 上标与整数顶端对齐：行高=字号时字形顶部约在 0.17em 处，按字号差补偿。
  const topOffset = (small: number) => round(0.17 * (size - small));
  return (
    <View style={styles.row}>
      <Text style={[FONT_CONDENSED, textStyle(dollarSize, 1), { color, marginTop: topOffset(dollarSize), marginRight: round(size * 0.02) }]}>$</Text>
      <Text style={[FONT_CONDENSED, textStyle(size, 1), { color, letterSpacing: -size * 0.02 }]}>{dollars}</Text>
      {cents ? (
        <View style={{ marginTop: topOffset(centSize), marginLeft: round(size * 0.035) }}>
          <Text style={[FONT_CONDENSED, textStyle(centSize, 1), { color }]}>{cents}</Text>
          {underlineCents ? (
            <View style={{ height: Math.max(2, round(size * 0.028)), backgroundColor: color, marginTop: round(size * 0.01) }} />
          ) : null}
        </View>
      ) : null}
    </View>
  );
}

/** 多件价「3 FOR $10」：件数与金额同为大字，FOR 小字；整数金额不显示角分。 */
function DealPrice({
  quantity,
  value,
  size,
  color,
  forText,
  forColor,
  forStyle,
}: {
  quantity: number | null;
  value: number | null;
  size: number;
  color: string;
  forText: string;
  forColor: string;
  forStyle: TextStyle;
}) {
  const { dollars, cents } = value === null ? { dollars: "--", cents: "00" } : splitPosterPrice(value);
  const dollarSize = size * 0.36;
  const centSize = size * 0.42;
  const forSize = size * 0.21;
  const topOffset = (small: number) => round(0.17 * (size - small));
  return (
    <View style={styles.row}>
      <Text style={[FONT_CONDENSED, textStyle(size, 1), { color }]}>{quantity ?? "-"}</Text>
      <Text
        style={[
          forStyle,
          textStyle(forSize, 1),
          { color: forColor, marginTop: round(size * 0.42), marginHorizontal: round(size * 0.06), letterSpacing: forSize * 0.04 },
        ]}
      >
        {forText}
      </Text>
      <Text style={[FONT_CONDENSED, textStyle(dollarSize, 1), { color, marginTop: topOffset(dollarSize), marginRight: round(size * 0.02) }]}>$</Text>
      <Text style={[FONT_CONDENSED, textStyle(size, 1), { color, letterSpacing: -size * 0.02 }]}>{dollars}</Text>
      {cents !== "00" ? (
        <Text style={[FONT_CONDENSED, textStyle(centSize, 1), { color, marginTop: topOffset(centSize), marginLeft: round(size * 0.035) }]}>
          {cents}
        </Text>
      ) : null}
    </View>
  );
}

/** Hot Bargain 字标（App 内没有 logo 图片资源，用文字近似）。 */
function Wordmark({ height }: { height: number }) {
  return (
    <View style={{ height, justifyContent: "center" }}>
      <Text style={[FONT_HEAVY, textStyle(height * 0.34, 1), { color: RED }]}>HOT</Text>
      <Text style={[FONT_HEAVY, textStyle(height * 0.3, 1), { color: LOGO_NAVY }]}>BARGAIN</Text>
    </View>
  );
}

/** 按估算字宽计算价格字号：不超过该尺寸上限，数字位数多时自动缩小。 */
function fitPriceSize(value: number | null, available: number, cap: number, lead = 0) {
  const digits = value === null ? 2 : splitPosterPrice(value).dollars.length;
  const em = 0.62 + CONDENSED_EM * digits + lead;
  return Math.min(cap, Math.floor((available / em) * 0.97));
}

function priceWidthDigits(value: number | null) {
  return value === null ? 2 : splitPosterPrice(value).dollars.length;
}

/** 页脚文字：完整版两行右对齐，A7 合成一行。 */
function footerLines(data: PromoPosterPreviewData, short: boolean): string[] {
  const item = data.itemNumber ? (short ? `#${data.itemNumber}` : `Item ${data.itemNumber}`) : "";
  let left = "";
  let right = item;
  if (data.kind === "special" || data.kind === "multibuy") {
    left = formatPosterValidity(data.validFrom, data.validTo, short);
  } else if (data.kind === "clearance") {
    left = "While stocks last";
  } else {
    left = item;
    right = data.inStoreSince
      ? short
        ? `Since ${formatPosterDay(data.inStoreSince, false)}`
        : `In store since ${formatPosterDay(data.inStoreSince, true)}`
      : "";
  }
  const lines = [left, right].filter(Boolean);
  return short && lines.length > 1 ? [lines.join(" · ")] : lines;
}

function savingOf(data: PromoPosterPreviewData) {
  if (data.price === null) return null;
  return computePosterSaving({
    kind: data.kind,
    price: data.price,
    wasPrice: data.wasPrice ?? undefined,
    quantity: data.quantity ?? undefined,
    unitPrice: data.unitPrice ?? undefined,
  });
}

function displayTitle(data: PromoPosterPreviewData) {
  return data.title.trim() || "Product name";
}

// ---------------------------------------------------------------- 经典风格

const CLASSIC_BAND: Record<Exclude<PromoPosterKind, "new">, { word: string; bg: string; fg: string; em: number }> = {
  special: { word: "SPECIAL", bg: RED, fg: "#FFFFFF", em: 3.5 },
  multibuy: { word: "MULTI-BUY", bg: RED, fg: "#FFFFFF", em: 4.3 },
  clearance: { word: "CLEARANCE", bg: YELLOW, fg: INK, em: 4.6 },
};

function ClassicBand({ kind, z }: { kind: Exclude<PromoPosterKind, "new">; z: ClassicSize }) {
  const band = CLASSIC_BAND[kind];
  const available = z.w - 2 * z.m - 2 * z.b - 2 * z.bpx;
  const fontSize = Math.floor((available / band.em) * 0.95);
  // 自动缩放的文字使用字体自然行高，避免 iOS 将大标题压缩到不可见。
  return (
    <View style={{ backgroundColor: band.bg, paddingVertical: z.bpy, paddingHorizontal: z.bpx, alignItems: "center" }}>
      <Text
        numberOfLines={1}
        adjustsFontSizeToFit
        minimumFontScale={0.5}
        style={[FONT_CONDENSED, { fontSize, color: band.fg, letterSpacing: fontSize * 0.01, alignSelf: "stretch", textAlign: "center", includeFontPadding: false }]}
      >
        {band.word}
      </Text>
    </View>
  );
}

function ClassicNewHead({ z }: { z: ClassicSize }) {
  const tagFont = round(z.K * 0.92);
  const tagHeight = round(tagFont * 0.9 + 2 * z.bpy);
  const notch = round(z.K * 0.28);
  return (
    <View style={[styles.row, { alignItems: "center", gap: round(z.bpx * 0.8), paddingTop: z.pad, paddingHorizontal: z.pad }]}>
      <View style={[styles.row, { height: tagHeight }]}>
        <View style={{ height: tagHeight, justifyContent: "center", backgroundColor: GREEN, paddingLeft: z.bpx, paddingRight: round(z.bpx * 0.4) }}>
          <Text style={[FONT_CONDENSED, textStyle(tagFont, 0.9), { color: "#FFFFFF" }]}>NEW</Text>
        </View>
        {/* 箭头缺口：用透明上下边框画三角形 */}
        <View
          style={{
            width: 0,
            height: 0,
            borderTopWidth: tagHeight / 2,
            borderBottomWidth: tagHeight / 2,
            borderLeftWidth: notch,
            borderTopColor: "transparent",
            borderBottomColor: "transparent",
            borderLeftColor: GREEN,
          }}
        />
      </View>
      <View>
        <Text style={[FONT_CONDENSED, textStyle(tagFont * 0.42, 0.95), { color: GREEN, letterSpacing: tagFont * 0.01 }]}>JUST</Text>
        <Text style={[FONT_CONDENSED, textStyle(tagFont * 0.42, 0.95), { color: GREEN, letterSpacing: tagFont * 0.01 }]}>ARRIVED</Text>
      </View>
    </View>
  );
}

/** 清仓黄黑斜纹：一排旋转的黑条放在黄色底上。 */
function Stripes({ z }: { z: ClassicSize }) {
  const stripe = Math.max(5, round(z.sh * 0.75));
  const count = Math.ceil(z.w / (stripe * 2)) + 2;
  return (
    <View style={{ height: z.sh, backgroundColor: YELLOW, overflow: "hidden", flexDirection: "row" }}>
      {Array.from({ length: count }, (_, index) => (
        <View
          key={index}
          style={{
            position: "absolute",
            left: index * stripe * 2 - stripe,
            top: -z.sh,
            width: stripe,
            height: z.sh * 3,
            backgroundColor: INK,
            transform: [{ rotate: "45deg" }],
          }}
        />
      ))}
    </View>
  );
}

function Pill({ text, fontSize, bg, fg }: { text: string; fontSize: number; bg: string; fg: string }) {
  return (
    <View style={{ backgroundColor: bg, borderRadius: Math.max(2, round(fontSize * 0.15)), paddingHorizontal: round(fontSize * 0.35), paddingVertical: round(fontSize * 0.1) }}>
      <Text style={[FONT_BOLD, textStyle(fontSize, 1.2), { color: fg }]}>{text}</Text>
    </View>
  );
}

function WasText({ value, fontSize, strikeColor, color, lower }: { value: number; fontSize: number; strikeColor: string; color: string; lower?: boolean }) {
  return (
    <Text style={[FONT_BOLD, textStyle(fontSize, 1.2), { color }]}>
      {lower ? "was " : "WAS "}
      <Text style={{ textDecorationLine: "line-through", textDecorationColor: strikeColor }}>{formatPosterMoney(value)}</Text>
    </Text>
  );
}

function ClassicPoster({ data, z }: { data: PromoPosterPreviewData; z: ClassicSize }) {
  const border = data.kind === "new" ? GREEN : data.kind === "clearance" ? INK : RED;
  const inner = z.w - 2 * z.m - 2 * z.b - 2 * z.pad;
  const saving = savingOf(data);
  let top: ReactNode = null;
  let priceBlock: ReactNode = null;
  const info: ReactNode[] = [];

  if (data.kind === "multibuy") {
    const digits = priceWidthDigits(data.price) + String(data.quantity ?? 1).length;
    const size = Math.min(round(z.P * 0.78), Math.floor((inner / (1.1 + CONDENSED_EM * digits)) * 0.97));
    priceBlock = (
      <DealPrice quantity={data.quantity ?? null} value={data.price} size={size} color={RED} forText="FOR" forColor={RED} forStyle={FONT_CONDENSED} />
    );
    if (data.unitPrice) {
      info.push(<Text key="each" style={[FONT_BOLD, textStyle(z.wz, 1.2), { color: INK }]}>{`${formatPosterMoney(data.unitPrice)} EACH`}</Text>);
    }
    if (saving) info.push(<Pill key="save" text={`SAVE ${formatPosterMoney(saving.amount)}`} fontSize={z.wz} bg={INK} fg="#FFFFFF" />);
    if (data.mixAndMatch && data.quantity) {
      top = (
        <View style={{ alignSelf: "flex-start", borderWidth: Math.max(1.5, z.b * 0.6), borderColor: RED, borderRadius: Math.max(2, round(z.wz * 0.15)), paddingHorizontal: round(z.wz * 0.35), paddingVertical: round(z.wz * 0.08) }}>
          <Text style={[FONT_BOLD, textStyle(z.wz, 1.2), { color: RED }]}>{`Mix & match any ${data.quantity}`}</Text>
        </View>
      );
    }
  } else if (data.kind === "clearance") {
    const size = fitPriceSize(data.price, inner, round(z.P * 0.88), 0.2);
    const nowSize = Math.max(12, round(size * 0.15));
    priceBlock = (
      <View style={[styles.row, { alignItems: "center" }]}>
        <View style={{ width: round(nowSize * 1.3), height: round(nowSize * 3.4), alignItems: "center", justifyContent: "center", marginRight: round(size * 0.04) }}>
          <Text style={[FONT_BOLD, textStyle(nowSize, 1), { color: INK, width: round(nowSize * 3.4), textAlign: "center", letterSpacing: nowSize * 0.1, transform: [{ rotate: "-90deg" }] }]}>
            NOW
          </Text>
        </View>
        <BigPrice value={data.price} size={size} color={INK} underlineCents />
      </View>
    );
    if (data.wasPrice) info.push(<WasText key="was" value={data.wasPrice} fontSize={z.wz} strikeColor={RED} color={INK} />);
    if (saving) info.push(<Pill key="off" text={`${saving.percent}% OFF`} fontSize={z.wz} bg={INK} fg={YELLOW} />);
  } else if (data.kind === "new") {
    priceBlock = <BigPrice value={data.price} size={fitPriceSize(data.price, inner, round(z.P * 1.2))} color={INK} underlineCents />;
  } else {
    priceBlock = <BigPrice value={data.price} size={fitPriceSize(data.price, inner, z.P)} color={RED} underlineCents />;
    if (saving && data.wasPrice) info.push(<WasText key="was" value={data.wasPrice} fontSize={z.wz} strikeColor={RED} color={INK} />);
    if (saving) info.push(<Pill key="save" text={`SAVE ${formatPosterMoney(saving.amount)}`} fontSize={z.wz} bg={INK} fg="#FFFFFF" />);
  }

  const lines = footerLines(data, z.short);
  return (
    <View style={{ width: z.w, height: z.h, padding: z.m, backgroundColor: "#FFFFFF" }}>
      <View style={{ flex: 1, borderWidth: z.b, borderColor: border, overflow: "hidden" }}>
        {data.kind === "clearance" ? <Stripes z={z} /> : null}
        {data.kind === "new" ? <ClassicNewHead z={z} /> : <ClassicBand kind={data.kind} z={z} />}
        <View style={{ flex: 1, gap: z.gap, paddingTop: z.pad, paddingHorizontal: z.pad, paddingBottom: round(z.pad * 0.8) }}>
          <Text numberOfLines={2} style={[FONT_BOLD, textStyle(z.n, 1.1), { color: data.title.trim() ? INK : LINE }]}>
            {displayTitle(data)}
          </Text>
          {top}
          <View style={styles.flexSpacer} />
          <View style={{ gap: Math.max(6, round(z.gap * 0.8)) }}>
            {priceBlock}
            {info.length > 0 ? <View style={[styles.row, styles.wrap, { alignItems: "center", gap: Math.max(6, round(z.wz * 0.45)) }]}>{info}</View> : null}
          </View>
        </View>
        <View style={[styles.row, { alignItems: "center", justifyContent: "space-between", gap: 8, paddingVertical: z.fpy, paddingHorizontal: z.pad, borderTopWidth: 1, borderTopColor: LINE }]}>
          {data.showLogo !== false ? <Wordmark height={z.lg} /> : null}
          <View style={{ alignItems: "flex-end", gap: 2, flex: 1 }}>
            {lines.map((line) => (
              <Text key={line} numberOfLines={1} style={[FONT_BOLD, textStyle(z.f, 1.3), { color: GREY }]}>
                {line}
              </Text>
            ))}
          </View>
        </View>
        {data.kind === "clearance" ? <Stripes z={z} /> : null}
      </View>
    </View>
  );
}

// ---------------------------------------------------------------- 现代风格

function Sticker({ z, bg, fg, small, big, bigFirst }: { z: ModernSize; bg: string; fg: string; small: string; big: string; bigFirst?: boolean }) {
  const smallText = (
    <Text key="small" style={[FONT_HEAVY, textStyle(z.S * 0.14, 1), { color: fg, letterSpacing: z.S * 0.006 }]}>
      {small.toUpperCase()}
    </Text>
  );
  const bigText = (
    <Text key="big" numberOfLines={1} adjustsFontSizeToFit style={[FONT_CONDENSED, { fontSize: z.S * 0.34, color: fg, maxWidth: z.S * 0.86, includeFontPadding: false }]}>
      {big}
    </Text>
  );
  return (
    <View
      style={{
        width: z.S,
        height: z.S,
        borderRadius: z.S / 2,
        backgroundColor: bg,
        alignItems: "center",
        justifyContent: "center",
        gap: round(z.S * 0.03),
        marginBottom: -round(z.gap + z.pad * 0.85),
        transform: [{ rotate: "-10deg" }],
      }}
    >
      {bigFirst ? [bigText, smallText] : [smallText, bigText]}
    </View>
  );
}

function ModernPoster({ data, z }: { data: PromoPosterPreviewData; z: ModernSize }) {
  const theme = MODERN_THEME[data.kind];
  const saving = savingOf(data);
  const inner = z.w - 2 * z.m - 2 * z.fp - 2 * z.pad;
  let sticker: ReactNode = null;
  let priceBlock: ReactNode;
  let info: ReactNode = null;
  let mix: ReactNode = null;
  const infoStyle = [FONT_HEAVY, textStyle(z.info, 1.2), { color: GREY }];

  if (data.kind === "multibuy") {
    const digits = priceWidthDigits(data.price) + String(data.quantity ?? 1).length;
    const size = Math.min(round(z.P * 0.92), Math.floor((inner / (1.0 + CONDENSED_EM * digits)) * 0.97));
    priceBlock = (
      <DealPrice quantity={data.quantity ?? null} value={data.price} size={size} color={theme.price} forText="for" forColor="#E25A00" forStyle={FONT_HEAVY} />
    );
    if (data.unitPrice) info = <Text style={infoStyle}>{`${formatPosterMoney(data.unitPrice)} each`}</Text>;
    if (saving) sticker = <Sticker z={z} bg={theme.stickerBg} fg={theme.stickerFg} small="save" big={formatPosterMoney(saving.amount)} />;
    if (data.mixAndMatch && data.quantity) {
      mix = (
        <View style={{ alignSelf: "flex-start", backgroundColor: theme.bg, borderRadius: 999, paddingHorizontal: round(z.info * 0.45), paddingVertical: round(z.info * 0.15) }}>
          <Text style={[FONT_HEAVY, textStyle(z.info, 1.2), { color: INK }]}>{`mix & match any ${data.quantity}`}</Text>
        </View>
      );
    }
  } else {
    priceBlock = <BigPrice value={data.price} size={fitPriceSize(data.price, inner, z.P)} color={theme.price} underlineCents={false} />;
    if (data.kind === "new") {
      info = <Text style={infoStyle}>just arrived</Text>;
      sticker = <Sticker z={z} bg={theme.stickerBg} fg={theme.stickerFg} small="in store" big="NOW" />;
    } else {
      if (saving && data.wasPrice) info = <WasText value={data.wasPrice} fontSize={z.info} strikeColor={GREY} color={GREY} lower />;
      if (saving) {
        sticker =
          data.kind === "clearance" ? (
            <Sticker z={z} bg={theme.stickerBg} fg={theme.stickerFg} small="off" big={`${saving.percent}%`} bigFirst />
          ) : (
            <Sticker z={z} bg={theme.stickerBg} fg={theme.stickerFg} small="save" big={formatPosterMoney(saving.amount)} />
          );
      }
    }
  }

  const lines = footerLines(data, z.short);
  const frameWidth = z.w - 2 * z.m - 2 * z.fp;
  // 标题按「色块宽度 − 贴纸宽度」估算字号，上限 wcap；实际再由 adjustsFontSizeToFit 缩到一行放下。
  const wordSize = Math.min(z.wcap, Math.floor(((frameWidth - (sticker ? z.S * 1.08 : 0)) / (theme.word.length * 0.58)) * 0.97));
  return (
    <View style={{ width: z.w, height: z.h, padding: z.m, backgroundColor: "#FFFFFF" }}>
      <View style={{ flex: 1, backgroundColor: theme.bg, borderRadius: z.R, padding: z.fp, overflow: "hidden" }}>
        <View style={[styles.row, { alignItems: "flex-end", justifyContent: "space-between", gap: round(z.S * 0.08), marginTop: round(z.fp * 0.15), marginBottom: z.gap, zIndex: 2 }]}>
          <Text
            numberOfLines={1}
            adjustsFontSizeToFit
            minimumFontScale={0.5}
            style={[FONT_HEAVY, { fontSize: wordSize, flex: 1, color: theme.on, letterSpacing: -wordSize * 0.035, includeFontPadding: false }]}
          >
            {theme.word}
          </Text>
          {sticker}
        </View>
        <View style={{ flex: 1, backgroundColor: "#FFFFFF", borderRadius: z.pr, padding: z.pad, gap: z.gap, zIndex: 1 }}>
          <Text numberOfLines={2} style={[FONT_BOLD, textStyle(z.n, 1.08), { color: data.title.trim() ? INK : LINE, letterSpacing: -z.n * 0.01 }]}>
            {displayTitle(data)}
          </Text>
          {mix}
          <View style={styles.flexSpacer} />
          {priceBlock}
          {info}
        </View>
        <View style={[styles.row, { alignItems: "center", justifyContent: "space-between", gap: 8, marginTop: z.gap }]}>
          {data.showLogo !== false ? (
            <View style={{ backgroundColor: "#FFFFFF", borderRadius: round(z.lg * 0.25), paddingHorizontal: round(z.lg * 0.25), paddingVertical: round(z.lg * 0.12) }}>
              <Wordmark height={z.lg} />
            </View>
          ) : null}
          <View style={{ alignItems: "flex-end", gap: 2, flex: 1 }}>
            {lines.map((line) => (
              <Text key={line} numberOfLines={1} style={[FONT_HEAVY, textStyle(z.f, 1.25), { color: theme.on }]}>
                {line}
              </Text>
            ))}
          </View>
        </View>
      </View>
    </View>
  );
}

// ---------------------------------------------------------------- 省彩墨风格

function LowInkPoster({ data, z }: { data: PromoPosterPreviewData; z: LowInkSize }) {
  const theme = LOW_INK_THEME[data.kind];
  const available = z.w - 2 * z.m;
  const saving = savingOf(data);
  const multi = data.kind === "multibuy";
  const hasSaving = saving !== null && saving.amount > 0;
  const priceSize = Math.min(fitPriceSize(data.price, available, z.P), z.h * 0.23);
  const info = multi
    ? [data.unitPrice ? `${formatPosterMoney(data.unitPrice)} EACH` : "", hasSaving ? `SAVE ${formatPosterMoney(saving.amount)} WHEN YOU BUY ${data.quantity}` : ""]
    : hasSaving && data.wasPrice
      ? [`WAS ${formatPosterMoney(data.wasPrice)}`, `SAVE ${formatPosterMoney(saving.amount)}`]
      : [];
  const validity = data.kind === "clearance"
    ? "While stocks last"
    : data.kind === "new"
      ? data.inStoreSince ? `In store since ${formatPosterDay(data.inStoreSince, data.size !== "A7")}` : ""
      : formatPosterValidity(data.validFrom, data.validTo, data.size === "A7");
  const field = (top: number) => ({ position: "absolute" as const, left: z.m, top, width: available });

  return (
    <View style={{ width: z.w, height: z.h, backgroundColor: "#FFFFFF" }}>
      <Text style={[FONT_BOLD, textStyle(z.label), field(z.m), { color: theme.color, letterSpacing: z.label * 0.04 }]}>
        {theme.label}
      </Text>
      <View style={{ ...field(z.m + z.label * 1.3 + z.gap), width: available * 0.18, height: Math.max(1, z.w / 397), backgroundColor: theme.color }} />
      <Text numberOfLines={3} adjustsFontSizeToFit minimumFontScale={0.7}
        style={[FONT_BOLD, textStyle(z.n, 1.15), field(z.h * 0.18), { height: z.h * 0.18, color: "#000000" }]}>
        {displayTitle(data)}
      </Text>
      {multi ? (
        <Text style={[FONT_BOLD, textStyle(z.info * 1.5), field(z.h * 0.39), { color: "#000000" }]}>
          {data.quantity ?? "-"} FOR
        </Text>
      ) : null}
      <View style={field(z.h * 0.46)}>
        <BigPrice value={data.price} size={priceSize} color="#000000" underlineCents={false} />
      </View>
      <Text numberOfLines={1} adjustsFontSizeToFit minimumFontScale={0.7}
        style={[FONT_BOLD, textStyle(z.info), field(z.h * 0.70), { color: "#000000" }]}>
        {multi ? data.mixAndMatch ? `MIX & MATCH ANY ${data.quantity ?? "-"}` : `FOR ${data.quantity ?? "-"} ITEMS` : "EACH"}
      </Text>
      <View style={field(z.h * 0.75)}>
        {info.filter(Boolean).map((line) => (
          <Text key={line} numberOfLines={1} adjustsFontSizeToFit minimumFontScale={0.7}
            style={[FONT_BOLD, textStyle(z.info, 1.25), { color: "#000000" }]}>{line}</Text>
        ))}
      </View>
      <View style={{ ...field(z.h * 0.835), height: 1, backgroundColor: "#000000" }} />
      <Text numberOfLines={2} adjustsFontSizeToFit minimumFontScale={0.8}
        style={[FONT_BOLD, textStyle(z.f, 1.2), field(z.h * 0.85), { color: "#000000" }]}>{validity}</Text>
      <View style={{ position: "absolute", left: z.m, right: z.m, bottom: z.m, flexDirection: "row", alignItems: "flex-end", gap: z.gap }}>
        {data.showLogo !== false ? <Wordmark height={z.lg} /> : null}
        <Text numberOfLines={1} adjustsFontSizeToFit minimumFontScale={0.65}
          style={[FONT_BOLD, textStyle(z.f, 1.2), { color: "#000000", flex: 1, textAlign: "right" }]}>
          {data.itemNumber ? `Item ${data.itemNumber}` : ""}
        </Text>
      </View>
    </View>
  );
}

// ---------------------------------------------------------------- 入口

function PromoPosterPreviewComponent({ data, width }: PromoPosterPreviewProps) {
  const base = data.style === "low-ink" ? LOW_INK_SIZES[data.size] : data.style === "modern" ? MODERN_SIZES[data.size] : CLASSIC_SIZES[data.size];
  const scale = width / base.w;
  const height = round(base.h * scale);
  return (
    <View style={[styles.frame, { width, height }]} accessibilityElementsHidden importantForAccessibility="no-hide-descendants">
      {/* 按设计稿像素排版后整体缩放；transformOrigin 左上角，外框裁掉多余的布局尺寸 */}
      <View style={{ width: base.w, height: base.h, transform: [{ scale }], transformOrigin: "top left" }}>
        {data.style === "low-ink" ? (
          <LowInkPoster data={data} z={LOW_INK_SIZES[data.size]} />
        ) : data.style === "modern" ? (
          <ModernPoster data={data} z={MODERN_SIZES[data.size]} />
        ) : (
          <ClassicPoster data={data} z={CLASSIC_SIZES[data.size]} />
        )}
      </View>
    </View>
  );
}

export const PromoPosterPreview = memo(PromoPosterPreviewComponent);

/** 纸张宽高比（所有 A 系列一致，按设计稿像素取）。 */
export function promoPosterAspectRatio(style: PromoPosterStyle, size: PromoPosterSize) {
  const base = style === "low-ink" ? LOW_INK_SIZES[size] : style === "modern" ? MODERN_SIZES[size] : CLASSIC_SIZES[size];
  return base.h / base.w;
}

const styles = StyleSheet.create({
  frame: {
    overflow: "hidden",
    backgroundColor: "#FFFFFF",
  },
  row: {
    flexDirection: "row",
  },
  wrap: {
    flexWrap: "wrap",
  },
  flexSpacer: {
    flex: 1,
  },
});
