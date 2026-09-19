/**
 * 促销海报纯逻辑：不依赖 React Native / apiClient，便于 node 直接单测。
 * 海报内容全部是英文（后端出 PDF，字体没有中文字形），App 界面文案走 i18n。
 */
import {
  PROMO_POSTER_KINDS,
  PROMO_POSTER_SIZES,
  PROMO_POSTER_STYLES,
  type PromoPosterDefaults,
  type PromoPosterDraft,
  type PromoPosterKind,
  type PromoPosterMultiBuyOffer,
  type PromoPosterPdfRequest,
  type PromoPosterQueueItem,
  type PromoPosterSize,
  type PromoPosterSpec,
  type PromoPosterStyle,
} from "./types";

/** 后端单次最多生成 200 张，队列也按这个上限拦截。 */
export const PROMO_POSTER_QUEUE_LIMIT = 200;
/** 英文品名最多 80 个字符；海报上最多两行，过长会被后端截断。 */
export const PROMO_POSTER_TITLE_MAX_LENGTH = 80;
/** 价格上限：四位整数以内，再大海报数字会被压得过小。 */
export const PROMO_POSTER_PRICE_MAX = 9999.99;

/** 拼到 A4 时每页可放的张数。 */
export const PROMO_POSTER_PER_A4: Record<PromoPosterSize, number> = {
  A4: 1,
  A5: 2,
  A6: 4,
  A7: 8,
};

/** 成品尺寸（毫米），用于编辑页说明。 */
export const PROMO_POSTER_SIZE_MM: Record<PromoPosterSize, { width: number; height: number }> = {
  A4: { width: 210, height: 297 },
  A5: { width: 148, height: 210 },
  A6: { width: 105, height: 148 },
  A7: { width: 74, height: 105 },
};

export type PromoPosterAvailability = Record<PromoPosterKind, boolean>;

// ---------------------------------------------------------------- 通用工具

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function pick(raw: Record<string, unknown>, ...keys: string[]) {
  for (const key of keys) {
    if (raw[key] !== undefined && raw[key] !== null) {
      return raw[key];
    }
  }
  return undefined;
}

function asString(value: unknown) {
  if (typeof value === "string") return value;
  if (typeof value === "number" && Number.isFinite(value)) return String(value);
  return "";
}

function asNullableNumber(value: unknown): number | null {
  if (value === null || value === undefined) return null;
  if (typeof value === "string" && !value.trim()) return null;
  const parsed = typeof value === "string" ? Number(value) : value;
  return typeof parsed === "number" && Number.isFinite(parsed) ? parsed : null;
}

function asBoolean(value: unknown) {
  if (typeof value === "boolean") return value;
  if (typeof value === "number") return value !== 0;
  if (typeof value === "string") {
    const normalized = value.trim().toLowerCase();
    return normalized === "true" || normalized === "1";
  }
  return false;
}

function roundMoney(value: number) {
  return Math.round(value * 100) / 100;
}

function sameMoney(a: number, b: number) {
  return Math.abs(a - b) < 0.005;
}

function pad2(value: number) {
  return value.toString().padStart(2, "0");
}

export function isPromoPosterKind(value: unknown): value is PromoPosterKind {
  return typeof value === "string" && (PROMO_POSTER_KINDS as readonly string[]).includes(value);
}

export function isPromoPosterStyle(value: unknown): value is PromoPosterStyle {
  return typeof value === "string" && (PROMO_POSTER_STYLES as readonly string[]).includes(value);
}

export function isPromoPosterSize(value: unknown): value is PromoPosterSize {
  return typeof value === "string" && (PROMO_POSTER_SIZES as readonly string[]).includes(value);
}

// ---------------------------------------------------------------- 日期

const DATE_ONLY_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;
const MONTH_NAMES = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/** 本地日期转 YYYY-MM-DD（海报日期都是门店本地日期，不做时区换算）。 */
export function formatDateOnly(date: Date) {
  return `${date.getFullYear()}-${pad2(date.getMonth() + 1)}-${pad2(date.getDate())}`;
}

function parseDateOnlyParts(value: string) {
  const match = value.match(DATE_ONLY_PATTERN);
  if (!match) return null;
  const year = Number(match[1]);
  const month = Number(match[2]);
  const day = Number(match[3]);
  const parsed = new Date(year, month - 1, day);
  if (parsed.getFullYear() !== year || parsed.getMonth() !== month - 1 || parsed.getDate() !== day) {
    return null;
  }
  return { year, month, day, date: parsed };
}

export function isValidDateOnly(value: string) {
  return parseDateOnlyParts(value) !== null;
}

export function addDaysToDateOnly(value: string, days: number) {
  const parts = parseDateOnlyParts(value);
  if (!parts) return value;
  const next = new Date(parts.year, parts.month - 1, parts.day + days);
  return formatDateOnly(next);
}

/** 促销起止时间（如 2026-09-19T00:00:00）只取日期部分；无法识别时返回空字符串。 */
export function toDateOnly(value: string | null | undefined) {
  const head = (value ?? "").trim().slice(0, 10);
  return isValidDateOnly(head) ? head : "";
}

/** 海报上的英文日期：19 Sep / 19 Sep 2026。 */
export function formatPosterDay(value: string, withYear: boolean) {
  const parts = parseDateOnlyParts(value);
  if (!parts) return "";
  const base = `${parts.day} ${MONTH_NAMES[parts.month - 1]}`;
  return withYear ? `${base} ${parts.year}` : base;
}

/** 有效期页脚：完整版「Valid 19 Sep – 2 Oct 2026」，A7 短版「Until 2 Oct」。 */
export function formatPosterValidity(validFrom: string | undefined, validTo: string | undefined, short: boolean) {
  const to = validTo && isValidDateOnly(validTo) ? validTo : "";
  const from = validFrom && isValidDateOnly(validFrom) ? validFrom : "";
  if (!to) return "";
  if (short || !from) return `Until ${formatPosterDay(to, !short)}`;
  return `Valid ${formatPosterDay(from, false)} – ${formatPosterDay(to, true)}`;
}

// ---------------------------------------------------------------- 金额

/** 解析价格输入：允许前导 $ 与空格，最多两位小数；必须大于 0 且不超过上限。 */
export function parsePosterPrice(text: string): number | null {
  const normalized = text.trim().replace(/^\$\s*/, "").replace(/,/g, "");
  if (!/^(\d{1,4}(\.\d{0,2})?|\.\d{1,2})$/.test(normalized)) {
    return null;
  }
  const value = Number(normalized);
  if (!Number.isFinite(value) || value <= 0 || value > PROMO_POSTER_PRICE_MAX) {
    return null;
  }
  return roundMoney(value);
}

/** 输入框回填用：固定两位小数；空值返回空字符串。 */
export function formatPriceInput(value: number | null | undefined) {
  return typeof value === "number" && Number.isFinite(value) && value > 0 ? value.toFixed(2) : "";
}

/** 海报/列表上的金额：$9.09；trimZeroCents 时整数金额显示为 $10。 */
export function formatPosterMoney(value: number, trimZeroCents = false) {
  const fixed = roundMoney(value).toFixed(2);
  return trimZeroCents && fixed.endsWith(".00") ? `$${fixed.slice(0, -3)}` : `$${fixed}`;
}

/** 大价格拆成整数与角分，角分固定两位。 */
export function splitPosterPrice(value: number) {
  const [dollars, cents] = roundMoney(Math.max(0, value)).toFixed(2).split(".");
  return { dollars, cents };
}

/** 列表上的价格文本：多件价「3 for $10」，其它「$9.09」。 */
export function formatPosterPriceText(spec: Pick<PromoPosterSpec, "kind" | "price" | "quantity">) {
  if (spec.kind === "multibuy" && spec.quantity) {
    return `${spec.quantity} for ${formatPosterMoney(spec.price, true)}`;
  }
  return formatPosterMoney(spec.price);
}

/**
 * 海报上的节省信息：
 * - special：SAVE = 原价 − 现价
 * - clearance：百分比 = (原价 − 清仓价) / 原价
 * - multibuy：SAVE = 单价 × 件数 − 组合价（按本商品计算，与后端一致）
 * 不成立（≤0）时返回 null，海报不显示该信息。
 */
export function computePosterSaving(spec: Pick<PromoPosterSpec, "kind" | "price" | "wasPrice" | "quantity" | "unitPrice">) {
  if ((spec.kind === "special" || spec.kind === "clearance") && spec.wasPrice && spec.wasPrice > spec.price) {
    const amount = roundMoney(spec.wasPrice - spec.price);
    return { amount, percent: Math.round((amount / spec.wasPrice) * 100) };
  }
  if (spec.kind === "multibuy" && spec.unitPrice && spec.quantity) {
    const amount = roundMoney(spec.unitPrice * spec.quantity - spec.price);
    if (amount > 0) {
      return { amount, percent: Math.round((amount / (spec.unitPrice * spec.quantity)) * 100) };
    }
  }
  return null;
}

// ---------------------------------------------------------------- 可用性

/**
 * 扫码页海报入口的启用规则（与同页标签按钮一致）：
 * 特价=本店折扣率>0；多件价=有进行中的多件促销；清仓=设置了清货价；新品始终可用。
 */
export function resolveScanPosterAvailability(input: {
  discountRate: number | null | undefined;
  activePromotionCount: number;
  clearancePrice: number | null | undefined;
}): PromoPosterAvailability {
  return {
    special: typeof input.discountRate === "number" && input.discountRate > 0,
    multibuy: input.activePromotionCount > 0,
    new: true,
    clearance: typeof input.clearancePrice === "number" && input.clearancePrice > 0,
  };
}

/** 编辑页以后端 defaults 为准：多件价还要求确实返回了促销。 */
export function resolveDefaultsAvailability(defaults: PromoPosterDefaults): PromoPosterAvailability {
  return {
    special: defaults.canSpecial,
    multibuy: defaults.canMultiBuy && defaults.multiBuyOffers.length > 0,
    new: true,
    clearance: defaults.canClearance,
  };
}

/** 进入编辑页时的类型：请求的类型不可用时回落到新品（始终可用）。 */
export function resolveInitialPosterKind(requested: unknown, availability: PromoPosterAvailability): PromoPosterKind {
  return isPromoPosterKind(requested) && availability[requested] ? requested : "new";
}

// ---------------------------------------------------------------- defaults 接口归一化

function normalizeOffer(raw: unknown): PromoPosterMultiBuyOffer | null {
  const record = asRecord(raw);
  if (!record) return null;
  const promotionId = asString(pick(record, "promotionId", "PromotionId", "id", "Id")).trim();
  const applyQuantity = Math.trunc(asNullableNumber(pick(record, "applyQuantity", "ApplyQuantity")) ?? 0);
  const fixedPrice = asNullableNumber(pick(record, "fixedPrice", "FixedPrice")) ?? 0;
  if (!promotionId || applyQuantity < 2 || fixedPrice <= 0) {
    return null;
  }
  return {
    promotionId,
    name: asString(pick(record, "name", "Name")).trim(),
    applyQuantity,
    fixedPrice: roundMoney(fixedPrice),
    effectiveStart: asString(pick(record, "effectiveStart", "EffectiveStart")),
    effectiveEnd: asString(pick(record, "effectiveEnd", "EffectiveEnd")),
    productsCount: Math.max(0, Math.trunc(asNullableNumber(pick(record, "productsCount", "ProductsCount")) ?? 0)),
  };
}

export function normalizePromoPosterDefaults(raw: unknown): PromoPosterDefaults | null {
  const record = asRecord(raw);
  if (!record) return null;
  const productCode = asString(pick(record, "productCode", "ProductCode")).trim();
  if (!productCode) return null;
  const offersRaw = pick(record, "multiBuyOffers", "MultiBuyOffers");
  const multiBuyOffers = Array.isArray(offersRaw)
    ? offersRaw.map(normalizeOffer).filter((offer): offer is PromoPosterMultiBuyOffer => offer !== null)
    : [];
  const positive = (value: unknown) => {
    const parsed = asNullableNumber(value);
    return parsed !== null && parsed > 0 ? roundMoney(parsed) : null;
  };
  return {
    productCode,
    itemNumber: asString(pick(record, "itemNumber", "ItemNumber")).trim(),
    productName: asString(pick(record, "productName", "ProductName")).trim(),
    englishName: asString(pick(record, "englishName", "EnglishName")).trim(),
    posterTitle: asString(pick(record, "posterTitle", "PosterTitle")).trim(),
    retailPrice: positive(pick(record, "retailPrice", "RetailPrice")),
    discountRate: asNullableNumber(pick(record, "discountRate", "DiscountRate")),
    discountedPrice: positive(pick(record, "discountedPrice", "DiscountedPrice")),
    clearancePrice: positive(pick(record, "clearancePrice", "ClearancePrice")),
    multiBuyOffers,
    canSpecial: asBoolean(pick(record, "canSpecial", "CanSpecial")),
    canMultiBuy: asBoolean(pick(record, "canMultiBuy", "CanMultiBuy")),
    canClearance: asBoolean(pick(record, "canClearance", "CanClearance")),
  };
}

// ---------------------------------------------------------------- 默认草稿

/** 按类型重置价格/日期字段；品名、风格、尺寸保持店员当前选择。 */
export function applyPosterKind(
  draft: PromoPosterDraft,
  defaults: PromoPosterDefaults,
  kind: PromoPosterKind,
  today: string,
): PromoPosterDraft {
  const base: PromoPosterDraft = {
    ...draft,
    kind,
    price: "",
    wasPrice: "",
    quantity: "",
    unitPrice: "",
    mixAndMatch: false,
    offerId: null,
    validFrom: "",
    validTo: "",
    inStoreSince: "",
  };
  switch (kind) {
    case "special":
      return {
        ...base,
        price: formatPriceInput(defaults.discountedPrice),
        wasPrice: formatPriceInput(defaults.retailPrice),
      };
    case "clearance":
      return {
        ...base,
        price: formatPriceInput(defaults.clearancePrice),
        wasPrice: formatPriceInput(defaults.retailPrice),
      };
    case "new":
      return { ...base, price: formatPriceInput(defaults.retailPrice), inStoreSince: today };
    case "multibuy":
      return applyMultiBuyOffer(base, defaults, defaults.multiBuyOffers[0]?.promotionId ?? null);
  }
}

/** 多件价：取选中促销的件数、组合价与起止日期，单价取零售价；促销含多个商品时为 Mix & match。 */
export function applyMultiBuyOffer(
  draft: PromoPosterDraft,
  defaults: PromoPosterDefaults,
  offerId: string | null,
): PromoPosterDraft {
  const offer = defaults.multiBuyOffers.find((item) => item.promotionId === offerId);
  if (!offer) {
    return { ...draft, offerId: null, price: "", quantity: "", unitPrice: formatPriceInput(defaults.retailPrice), mixAndMatch: false, validFrom: "", validTo: "" };
  }
  return {
    ...draft,
    offerId: offer.promotionId,
    price: formatPriceInput(offer.fixedPrice),
    quantity: String(offer.applyQuantity),
    unitPrice: formatPriceInput(defaults.retailPrice),
    mixAndMatch: offer.productsCount > 1,
    validFrom: toDateOnly(offer.effectiveStart),
    validTo: toDateOnly(offer.effectiveEnd),
  };
}

export function createPosterDraft(
  defaults: PromoPosterDefaults,
  options: { kind: PromoPosterKind; style: PromoPosterStyle; size: PromoPosterSize; today: string },
): PromoPosterDraft {
  const empty: PromoPosterDraft = {
    kind: options.kind,
    style: options.style,
    size: options.size,
    title: defaults.posterTitle,
    price: "",
    wasPrice: "",
    quantity: "",
    unitPrice: "",
    mixAndMatch: false,
    offerId: null,
    validFrom: "",
    validTo: "",
    inStoreSince: "",
  };
  return applyPosterKind(empty, defaults, options.kind, options.today);
}

// ---------------------------------------------------------------- 校验

/** 字体能印的额外标点：– — ‘ ’ “ ” × • …（× 已在 Latin-1 范围内，列出便于对照需求）。 */
const EXTRA_PRINTABLE_CHARS = new Set(["–", "—", "‘", "’", "“", "”", "×", "•", "…"]);

/**
 * 海报字体只覆盖拉丁字符：U+0020–U+024F（排除 U+007F–U+009F 控制字符）加上少量常见标点。
 * 中文、emoji 等都印不出来，必须拦在提交前。
 */
export function isPrintablePosterChar(char: string) {
  if (EXTRA_PRINTABLE_CHARS.has(char)) return true;
  const code = char.codePointAt(0) ?? 0;
  if (code >= 0x20 && code <= 0x7e) return true;
  return code >= 0xa0 && code <= 0x24f;
}

/** 品名规范化：换行/制表符转空格，合并连续空白并去首尾空格。 */
export function normalizePosterTitle(title: string) {
  return title.replace(/\s+/g, " ").trim();
}

export type PosterTitleError =
  | { code: "required" }
  | { code: "tooLong"; max: number }
  | { code: "unprintable"; chars: string };

export function validatePosterTitle(title: string): PosterTitleError | null {
  const normalized = normalizePosterTitle(title);
  if (!normalized) return { code: "required" };
  const invalid: string[] = [];
  for (const char of Array.from(normalized)) {
    if (!isPrintablePosterChar(char) && !invalid.includes(char)) {
      invalid.push(char);
    }
  }
  if (invalid.length > 0) {
    // 最多展示 8 个不可打印字符，避免整段中文名塞满提示。
    return { code: "unprintable", chars: invalid.slice(0, 8).join("") };
  }
  if (Array.from(normalized).length > PROMO_POSTER_TITLE_MAX_LENGTH) {
    return { code: "tooLong", max: PROMO_POSTER_TITLE_MAX_LENGTH };
  }
  return null;
}

export type PosterFieldErrorCode = "required" | "invalid" | "wasNotHigher" | "rangeInvalid";

export interface PromoPosterDraftErrors {
  title?: PosterTitleError;
  price?: PosterFieldErrorCode;
  wasPrice?: PosterFieldErrorCode;
  quantity?: PosterFieldErrorCode;
  unitPrice?: PosterFieldErrorCode;
  offer?: PosterFieldErrorCode;
  validity?: PosterFieldErrorCode;
  inStoreSince?: PosterFieldErrorCode;
}

function priceError(text: string): PosterFieldErrorCode | undefined {
  if (!text.trim()) return "required";
  return parsePosterPrice(text) === null ? "invalid" : undefined;
}

function hasErrors(errors: PromoPosterDraftErrors) {
  return Object.values(errors).some((value) => value !== undefined);
}

export type BuildPosterSpecResult =
  | { ok: true; spec: PromoPosterSpec }
  | { ok: false; errors: PromoPosterDraftErrors };

/** 草稿 → 海报内容：按类型校验并只保留该类型需要的字段。 */
export function buildPosterSpec(draft: PromoPosterDraft, defaults: PromoPosterDefaults): BuildPosterSpecResult {
  const errors: PromoPosterDraftErrors = {};
  const titleError = validatePosterTitle(draft.title);
  if (titleError) errors.title = titleError;
  errors.price = priceError(draft.price);

  const price = parsePosterPrice(draft.price);
  const common = {
    kind: draft.kind,
    style: draft.style,
    size: draft.size,
    productCode: defaults.productCode,
    itemNumber: defaults.itemNumber,
    title: normalizePosterTitle(draft.title),
  };
  let extra: Partial<PromoPosterSpec> = {};

  if (draft.kind === "special" || draft.kind === "clearance") {
    errors.wasPrice = priceError(draft.wasPrice);
    const wasPrice = parsePosterPrice(draft.wasPrice);
    // WAS 必须高于现价，否则海报上的 SAVE / % OFF 会是负数。
    if (price !== null && wasPrice !== null && wasPrice <= price) {
      errors.wasPrice = "wasNotHigher";
    }
    extra = { wasPrice: wasPrice ?? undefined };
    if (draft.kind === "special") {
      const from = draft.validFrom.trim();
      const to = draft.validTo.trim();
      if (from || to) {
        if (!from || !to) {
          errors.validity = "required";
        } else if (!isValidDateOnly(from) || !isValidDateOnly(to)) {
          errors.validity = "invalid";
        } else if (from > to) {
          errors.validity = "rangeInvalid";
        } else {
          extra = { ...extra, validFrom: from, validTo: to };
        }
      }
    }
  } else if (draft.kind === "new") {
    const since = draft.inStoreSince.trim();
    if (!since) errors.inStoreSince = "required";
    else if (!isValidDateOnly(since)) errors.inStoreSince = "invalid";
    else extra = { inStoreSince: since };
  } else {
    const offer = defaults.multiBuyOffers.find((item) => item.promotionId === draft.offerId);
    if (!offer) errors.offer = "required";
    const quantity = Number(draft.quantity.trim());
    if (!draft.quantity.trim()) errors.quantity = "required";
    else if (!Number.isInteger(quantity) || quantity < 2 || quantity > 99) errors.quantity = "invalid";
    errors.unitPrice = priceError(draft.unitPrice);
    const unitPrice = parsePosterPrice(draft.unitPrice);
    extra = {
      quantity: Number.isInteger(quantity) ? quantity : undefined,
      unitPrice: unitPrice ?? undefined,
      mixAndMatch: draft.mixAndMatch,
      validFrom: isValidDateOnly(draft.validFrom) ? draft.validFrom : undefined,
      validTo: isValidDateOnly(draft.validTo) ? draft.validTo : undefined,
    };
  }

  if (hasErrors(errors) || price === null) {
    return { ok: false, errors };
  }
  return { ok: true, spec: stripUndefined({ ...common, ...extra, price }) };
}

function stripUndefined(spec: PromoPosterSpec): PromoPosterSpec {
  const result: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(spec)) {
    if (value !== undefined) result[key] = value;
  }
  return result as unknown as PromoPosterSpec;
}

/**
 * 海报价与收银价不一致提示：特价现价 ≠ 本店折后价、清仓价 ≠ 已设清货价。
 * 顾客按海报价期待结账，收银却按系统价，必须提醒店员。
 */
export function resolvePriceMismatch(
  draft: Pick<PromoPosterDraft, "kind" | "price">,
  defaults: Pick<PromoPosterDefaults, "discountedPrice" | "clearancePrice">,
): { kind: "special" | "clearance"; expected: number } | null {
  const price = parsePosterPrice(draft.price);
  if (price === null) return null;
  if (draft.kind === "special" && defaults.discountedPrice !== null && !sameMoney(price, defaults.discountedPrice)) {
    return { kind: "special", expected: defaults.discountedPrice };
  }
  if (draft.kind === "clearance" && defaults.clearancePrice !== null && !sameMoney(price, defaults.clearancePrice)) {
    return { kind: "clearance", expected: defaults.clearancePrice };
  }
  return null;
}

// ---------------------------------------------------------------- 队列与拼版

/** PDF 页数：拼版时同尺寸按每页容量向上取整（A4 每页 1 张）；不拼版每张一页。 */
export function countPosterPages(sizes: readonly PromoPosterSize[], impose: boolean) {
  if (!impose) return sizes.length;
  const counts = new Map<PromoPosterSize, number>();
  for (const size of sizes) counts.set(size, (counts.get(size) ?? 0) + 1);
  let pages = 0;
  for (const [size, count] of counts) pages += Math.ceil(count / PROMO_POSTER_PER_A4[size]);
  return pages;
}

export interface PromoPosterSizeGroup<T> {
  size: PromoPosterSize;
  count: number;
  /** 拼版时该尺寸占用的 A4 页数。 */
  imposedPages: number;
  items: T[];
}

/** 按尺寸分组：数量多的在前，数量相同按 A4→A7 顺序。 */
export function groupPostersBySize<T extends { poster: Pick<PromoPosterSpec, "size"> }>(items: readonly T[]) {
  const groups = new Map<PromoPosterSize, T[]>();
  for (const item of items) {
    const list = groups.get(item.poster.size) ?? [];
    list.push(item);
    groups.set(item.poster.size, list);
  }
  return [...groups.entries()]
    .map(([size, list]): PromoPosterSizeGroup<T> => ({
      size,
      count: list.length,
      imposedPages: Math.ceil(list.length / PROMO_POSTER_PER_A4[size]),
      items: list,
    }))
    .sort((a, b) => b.count - a.count || PROMO_POSTER_SIZES.indexOf(a.size) - PROMO_POSTER_SIZES.indexOf(b.size));
}

export type QueueAdditionResult = "ok" | "full" | "storeConflict";

/** 入队前检查：超过上限拒绝；队列里是其它分店的海报时需要店员确认替换（PDF 请求只有一个 storeCode）。 */
export function resolveQueueAddition(items: readonly Pick<PromoPosterQueueItem, "storeCode">[], storeCode: string): QueueAdditionResult {
  const normalized = storeCode.trim().toLowerCase();
  if (items.some((item) => item.storeCode.trim().toLowerCase() !== normalized)) {
    return "storeConflict";
  }
  return items.length >= PROMO_POSTER_QUEUE_LIMIT ? "full" : "ok";
}

/** 生成 PDF 请求体：按类型只保留后端需要的字段。 */
export function buildPromoPosterPdfRequest(
  storeCode: string,
  impose: boolean,
  posters: readonly PromoPosterSpec[],
): PromoPosterPdfRequest {
  return {
    storeCode,
    impose,
    posters: posters.map((poster) => {
      const base = {
        kind: poster.kind,
        style: poster.style,
        size: poster.size,
        productCode: poster.productCode,
        itemNumber: poster.itemNumber,
        title: poster.title,
        price: poster.price,
      };
      switch (poster.kind) {
        case "special":
          return stripUndefined({ ...base, wasPrice: poster.wasPrice, validFrom: poster.validFrom, validTo: poster.validTo });
        case "clearance":
          return stripUndefined({ ...base, wasPrice: poster.wasPrice });
        case "new":
          return stripUndefined({ ...base, inStoreSince: poster.inStoreSince });
        case "multibuy":
          return stripUndefined({
            ...base,
            quantity: poster.quantity,
            unitPrice: poster.unitPrice,
            mixAndMatch: poster.mixAndMatch ?? false,
            validFrom: poster.validFrom,
            validTo: poster.validTo,
          });
      }
    }),
  };
}

/** 本地 PDF 文件名：HB-Posters-20260919-153045.pdf，带时分秒避免覆盖正在分享的旧文件。 */
export function buildPromoPosterFileName(now: Date) {
  const date = `${now.getFullYear()}${pad2(now.getMonth() + 1)}${pad2(now.getDate())}`;
  const time = `${pad2(now.getHours())}${pad2(now.getMinutes())}${pad2(now.getSeconds())}`;
  return `HB-Posters-${date}-${time}.pdf`;
}

// ---------------------------------------------------------------- 本地持久化

export interface PromoPosterQueueSnapshot {
  items: PromoPosterQueueItem[];
  style: PromoPosterStyle;
  size: PromoPosterSize;
  impose: boolean;
}

export const DEFAULT_PROMO_POSTER_QUEUE_SNAPSHOT: PromoPosterQueueSnapshot = {
  items: [],
  style: "classic",
  size: "A6",
  impose: true,
};

function normalizeStoredSpec(raw: unknown): PromoPosterSpec | null {
  const record = asRecord(raw);
  if (!record) return null;
  const { kind, style, size } = record;
  const price = asNullableNumber(record.price);
  const productCode = asString(record.productCode).trim();
  const title = asString(record.title).trim();
  if (!isPromoPosterKind(kind) || !isPromoPosterStyle(style) || !isPromoPosterSize(size)) return null;
  if (price === null || price <= 0 || !productCode || validatePosterTitle(title) !== null) return null;
  const optionalNumber = (value: unknown) => asNullableNumber(value) ?? undefined;
  const optionalDate = (value: unknown) => (typeof value === "string" && isValidDateOnly(value) ? value : undefined);
  return stripUndefined({
    kind,
    style,
    size,
    productCode,
    itemNumber: asString(record.itemNumber).trim(),
    title,
    price,
    wasPrice: optionalNumber(record.wasPrice),
    quantity: optionalNumber(record.quantity),
    unitPrice: optionalNumber(record.unitPrice),
    mixAndMatch: typeof record.mixAndMatch === "boolean" ? record.mixAndMatch : undefined,
    validFrom: optionalDate(record.validFrom),
    validTo: optionalDate(record.validTo),
    inStoreSince: optionalDate(record.inStoreSince),
  });
}

/** 读取本地保存的队列：结构不对的条目直接丢弃，避免旧版本数据让页面崩溃。 */
export function normalizeStoredQueueSnapshot(raw: unknown): PromoPosterQueueSnapshot {
  const record = asRecord(raw);
  if (!record) return { ...DEFAULT_PROMO_POSTER_QUEUE_SNAPSHOT, items: [] };
  const rawItems = Array.isArray(record.items) ? record.items : [];
  const items: PromoPosterQueueItem[] = [];
  for (const rawItem of rawItems) {
    const item = asRecord(rawItem);
    const poster = normalizeStoredSpec(item?.poster);
    const id = asString(item?.id).trim();
    const storeCode = asString(item?.storeCode).trim();
    if (!item || !poster || !id || !storeCode) continue;
    items.push({
      id,
      storeCode,
      productName: asString(item.productName),
      addedAt: asString(item.addedAt),
      poster,
    });
    if (items.length >= PROMO_POSTER_QUEUE_LIMIT) break;
  }
  return {
    items,
    style: isPromoPosterStyle(record.style) ? record.style : DEFAULT_PROMO_POSTER_QUEUE_SNAPSHOT.style,
    size: isPromoPosterSize(record.size) ? record.size : DEFAULT_PROMO_POSTER_QUEUE_SNAPSHOT.size,
    impose: typeof record.impose === "boolean" ? record.impose : DEFAULT_PROMO_POSTER_QUEUE_SNAPSHOT.impose,
  };
}

// ---------------------------------------------------------------- PDF 接口错误体

/** UTF-8 解码：优先 TextDecoder，缺失时手工解码（Hermes 旧版本兜底）。 */
export function decodeUtf8(bytes: Uint8Array) {
  if (typeof TextDecoder !== "undefined") {
    try {
      return new TextDecoder("utf-8").decode(bytes);
    } catch {
      // 继续走手工解码
    }
  }
  let result = "";
  let index = 0;
  while (index < bytes.length) {
    const byte = bytes[index++];
    // 截断的多字节序列读到 undefined 时按 0 处理，只影响错误提示文字，不会抛错。
    const next = () => (bytes[index++] ?? 0) & 0x3f;
    let codePoint = byte;
    if (byte >= 0xf0) {
      codePoint = ((byte & 0x07) << 18) | (next() << 12) | (next() << 6) | next();
    } else if (byte >= 0xe0) {
      codePoint = ((byte & 0x0f) << 12) | (next() << 6) | next();
    } else if (byte >= 0xc0) {
      codePoint = ((byte & 0x1f) << 6) | next();
    }
    result += codePoint <= 0x10ffff ? String.fromCodePoint(codePoint) : "�";
  }
  return result;
}

/**
 * arraybuffer 模式下失败响应体也是二进制：解码成 JSON 对象，
 * 让通用错误提示能读到后端的 message。无法识别时返回 null。
 */
export function parseBinaryJsonBody(data: unknown): Record<string, unknown> | null {
  let text: string | null = null;
  if (data instanceof ArrayBuffer) {
    text = decodeUtf8(new Uint8Array(data));
  } else if (ArrayBuffer.isView(data)) {
    text = decodeUtf8(new Uint8Array(data.buffer, data.byteOffset, data.byteLength));
  } else if (typeof data === "string") {
    text = data;
  } else {
    return asRecord(data);
  }
  if (!text || !text.trim()) return null;
  try {
    return asRecord(JSON.parse(text));
  } catch {
    return null;
  }
}

export function extractBinaryErrorMessage(data: unknown) {
  const body = parseBinaryJsonBody(data);
  const message = body ? asString(pick(body, "message", "Message")).trim() : "";
  return message || null;
}
