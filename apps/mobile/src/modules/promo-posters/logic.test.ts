import assert from "node:assert/strict";
import {
  addDaysToDateOnly,
  applyMultiBuyOffer,
  applyPosterKind,
  buildPosterSpec,
  buildPromoPosterFileName,
  buildPromoPosterPdfRequest,
  computePosterSaving,
  countPosterPages,
  createPosterDraft,
  decodeUtf8,
  extractBinaryErrorMessage,
  formatPosterPriceText,
  formatPosterValidity,
  groupPostersBySize,
  isPrintablePosterChar,
  normalizePromoPosterDefaults,
  normalizeStoredQueueSnapshot,
  parseBinaryJsonBody,
  parsePosterPrice,
  PROMO_POSTER_QUEUE_LIMIT,
  resolveDefaultsAvailability,
  resolveInitialPosterKind,
  resolvePriceMismatch,
  resolveQueueAddition,
  resolveScanPosterAvailability,
  splitPosterPrice,
  toDateOnly,
  validatePosterTitle,
} from "./logic";
import type { PromoPosterDefaults, PromoPosterQueueItem, PromoPosterSpec } from "./types";

const TODAY = "2026-09-19";

// 风格值会贯穿草稿、PDF 请求和本地队列，避免新增风格只停留在 UI 选项。
const LOW_INK_STYLE = "low-ink" as const;

function createDefaults(overrides: Partial<PromoPosterDefaults> = {}): PromoPosterDefaults {
  const normalized = normalizePromoPosterDefaults({
    productCode: "P1",
    itemNumber: "K1048",
    productName: "不锈钢真空保温瓶 500ml",
    englishName: "Stainless Steel Vacuum Flask 500ml",
    posterTitle: "Stainless Steel Vacuum Flask 500ml",
    retailPrice: 12.99,
    discountRate: 0.3,
    discountedPrice: 9.09,
    clearancePrice: null,
    multiBuyOffers: [
      {
        promotionId: "promo-1",
        name: "3 for 10",
        applyQuantity: 3,
        fixedPrice: 10,
        effectiveStart: "2026-09-19T00:00:00",
        effectiveEnd: "2026-10-02T23:59:59",
        productsCount: 6,
      },
      {
        promotionId: "promo-2",
        name: "2 for 20",
        applyQuantity: 2,
        fixedPrice: 20,
        effectiveStart: "2026-09-01T00:00:00",
        effectiveEnd: "2026-12-31T23:59:59",
        productsCount: 1,
      },
    ],
    canSpecial: true,
    canMultiBuy: true,
    canClearance: false,
  });
  assert.ok(normalized);
  return { ...normalized, ...overrides };
}

// ---------------------------------------------------------------- 可用性

assert.deepEqual(
  resolveScanPosterAvailability({ discountRate: 0.3, activePromotionCount: 0, clearancePrice: null }),
  { special: true, multibuy: false, new: true, clearance: false },
  "特价始终可用；无促销、无清货价时对应按钮不可用；新品始终可用",
);
assert.deepEqual(
  resolveScanPosterAvailability({ discountRate: 0, activePromotionCount: 2, clearancePrice: 5 }),
  { special: true, multibuy: true, new: true, clearance: true },
);
assert.deepEqual(
  resolveScanPosterAvailability({ discountRate: null, activePromotionCount: 0, clearancePrice: 0 }),
  { special: true, multibuy: false, new: true, clearance: false },
  "清货价为 0 视为未设置",
);

const defaults = createDefaults();
assert.deepEqual(resolveDefaultsAvailability(defaults), { special: true, multibuy: true, new: true, clearance: false });
assert.equal(
  resolveDefaultsAvailability(createDefaults({ multiBuyOffers: [] })).multibuy,
  false,
  "canMultiBuy 为真但没有促销数据时多件价仍不可用",
);
assert.equal(resolveInitialPosterKind("special", resolveDefaultsAvailability(defaults)), "special");
assert.equal(resolveInitialPosterKind("clearance", resolveDefaultsAvailability(defaults)), "new", "不可用类型回落到新品");
assert.equal(resolveInitialPosterKind("bogus", resolveDefaultsAvailability(defaults)), "new");

// ---------------------------------------------------------------- defaults 归一化

assert.equal(normalizePromoPosterDefaults(null), null);
assert.equal(normalizePromoPosterDefaults({ productCode: "" }), null, "没有商品编码的响应视为无效");
const pascal = normalizePromoPosterDefaults({
  ProductCode: "P9",
  ItemNumber: "X1",
  PosterTitle: "",
  RetailPrice: "4.50",
  DiscountedPrice: 0,
  ClearancePrice: "3",
  MultiBuyOffers: [{ PromotionId: "a", ApplyQuantity: 1, FixedPrice: 5 }, { PromotionId: "b", ApplyQuantity: 2, FixedPrice: 7 }],
  CanClearance: true,
});
assert.ok(pascal);
assert.equal(pascal.posterTitle, "", "空英文名保持空字符串，由店员手填");
assert.equal(pascal.retailPrice, 4.5);
assert.equal(pascal.discountedPrice, null, "0 价格视为缺失");
assert.equal(pascal.clearancePrice, 3);
assert.deepEqual(pascal.multiBuyOffers.map((offer) => offer.promotionId), ["b"], "件数小于 2 的促销不能做多件价海报");
assert.equal(pascal.canClearance, true);

// ---------------------------------------------------------------- 默认草稿

const special = createPosterDraft(defaults, { kind: "special", style: "classic", size: "A6", today: TODAY });
assert.equal(special.title, "Stainless Steel Vacuum Flask 500ml");
assert.equal(special.price, "9.09", "特价现价取本店折后价");
assert.equal(special.wasPrice, "12.99", "特价原价取零售价");
assert.equal(special.validFrom, "");
assert.equal(special.validTo, "");

const noDiscountDefaults = createDefaults({ retailPrice: 2.5, discountRate: 0, discountedPrice: null, canSpecial: false });
assert.equal(resolveDefaultsAvailability(noDiscountDefaults).special, true, "兼容旧接口 canSpecial=false，特价不要求系统折扣");
const regularSpecial = createPosterDraft(noDiscountDefaults, { kind: "special", style: "classic", size: "A4", today: TODAY });
assert.equal(regularSpecial.price, "2.50");
assert.equal(regularSpecial.wasPrice, "");
const regularResult = buildPosterSpec(regularSpecial, noDiscountDefaults);
assert.ok(regularResult.ok);
assert.equal(regularResult.spec.price, 2.5);
assert.equal("wasPrice" in regularResult.spec, false);
assert.equal(computePosterSaving(regularResult.spec), null);
for (const discountedPrice of [2.5, 3]) {
  const draft = createPosterDraft({ ...noDiscountDefaults, discountedPrice }, { kind: "special", style: "classic", size: "A4", today: TODAY });
  assert.equal(draft.price, "2.50", "非真实折扣不能填成优惠价");
  assert.equal(draft.wasPrice, "");
}
const noRetailDefaults = createDefaults({ retailPrice: null, discountedPrice: null, canSpecial: false });
const manualSpecial = createPosterDraft(noRetailDefaults, { kind: "special", style: "classic", size: "A4", today: TODAY });
assert.equal(manualSpecial.price, "");
assert.equal(buildPosterSpec(manualSpecial, noRetailDefaults).ok, false);
assert.equal(buildPosterSpec({ ...manualSpecial, price: "2.50" }, noRetailDefaults).ok, true, "无零售价可手填后打印");


const clearanceDefaults = createDefaults({ clearancePrice: 5, canClearance: true });
const clearance = createPosterDraft(clearanceDefaults, { kind: "clearance", style: "modern", size: "A4", today: TODAY });
assert.equal(clearance.price, "5.00", "清仓价取已设清货价");
assert.equal(clearance.wasPrice, "12.99");
assert.equal(clearance.style, "modern");
const lowInkDraft = createPosterDraft(defaults, { kind: "special", style: LOW_INK_STYLE, size: "A6", today: TODAY });
assert.equal(lowInkDraft.style, LOW_INK_STYLE, "省彩墨风格应保留在初始草稿");

const fresh = createPosterDraft(defaults, { kind: "new", style: "classic", size: "A5", today: TODAY });
assert.equal(fresh.price, "12.99", "新品售价取零售价");
assert.equal(fresh.wasPrice, "");
assert.equal(fresh.inStoreSince, TODAY, "新品上架日期默认当天");

const multi = createPosterDraft(defaults, { kind: "multibuy", style: "classic", size: "A6", today: TODAY });
assert.equal(multi.offerId, "promo-1", "多件价默认取第一个促销");
assert.equal(multi.price, "10.00");
assert.equal(multi.quantity, "3");
assert.equal(multi.unitPrice, "12.99", "单价取原零售价");
assert.equal(multi.mixAndMatch, true, "促销含多个商品时为 Mix & match");
assert.equal(multi.validFrom, "2026-09-19");
assert.equal(multi.validTo, "2026-10-02");

const secondOffer = applyMultiBuyOffer(multi, defaults, "promo-2");
assert.equal(secondOffer.quantity, "2");
assert.equal(secondOffer.price, "20.00");
assert.equal(secondOffer.mixAndMatch, false, "只含本商品的促销不是 Mix & match");

// 切换类型只重置价格与日期，保留店员改过的品名、风格、尺寸
const edited = { ...special, title: "Custom Flask", style: "modern" as const, size: "A4" as const, validFrom: TODAY, validTo: "2026-10-02" };
const switched = applyPosterKind(edited, defaults, "new", TODAY);
assert.equal(switched.title, "Custom Flask");
assert.equal(switched.style, "modern");
assert.equal(switched.size, "A4");
assert.equal(switched.validFrom, "", "切到新品清掉特价有效期");
assert.equal(switched.price, "12.99");

// ---------------------------------------------------------------- 品名校验

assert.equal(validatePosterTitle("Stainless Steel Vacuum Flask 500ml"), null);
assert.equal(validatePosterTitle("Crème brûlée – “Deluxe” × 2 • 10% off…"), null, "拉丁扩展与常见标点可打印");
assert.deepEqual(validatePosterTitle("   "), { code: "required" });
assert.deepEqual(validatePosterTitle("保温瓶 Flask"), { code: "unprintable", chars: "保温瓶" }, "中文不能印");
assert.deepEqual(validatePosterTitle("Cup 😀"), { code: "unprintable", chars: "😀" }, "emoji 不能印");
assert.equal(validatePosterTitle("A".repeat(81))?.code, "tooLong");
assert.equal(isPrintablePosterChar(""), false, "C1 控制字符不可打印");
assert.equal(isPrintablePosterChar("ž"), true, "U+017E 在拉丁扩展 A 范围内");

// ---------------------------------------------------------------- 价格解析

assert.equal(parsePosterPrice("9.09"), 9.09);
assert.equal(parsePosterPrice("$12.99"), 12.99);
assert.equal(parsePosterPrice(" 10 "), 10);
assert.equal(parsePosterPrice(".5"), 0.5);
assert.equal(parsePosterPrice("0"), null, "价格必须大于 0");
assert.equal(parsePosterPrice("1.999"), null, "最多两位小数");
assert.equal(parsePosterPrice("abc"), null);
assert.equal(parsePosterPrice("12345"), null, "超过四位整数拒绝");
assert.equal(parsePosterPrice("-3"), null);

// ---------------------------------------------------------------- 草稿 → 海报内容

const specialResult = buildPosterSpec(special, defaults);
assert.equal(specialResult.ok, true);
assert.deepEqual(specialResult.ok && specialResult.spec, {
  kind: "special",
  style: "classic",
  size: "A6",
  productCode: "P1",
  itemNumber: "K1048",
  title: "Stainless Steel Vacuum Flask 500ml",
  price: 9.09,
  wasPrice: 12.99,
});

const specialWithDates = buildPosterSpec({ ...special, validFrom: TODAY, validTo: "2026-10-02" }, defaults);
assert.equal(specialWithDates.ok && specialWithDates.spec.validTo, "2026-10-02");

const halfDates = buildPosterSpec({ ...special, validFrom: TODAY }, defaults);
assert.equal(!halfDates.ok && halfDates.errors.validity, "required", "有效期只填一端要提示");
const reversed = buildPosterSpec({ ...special, validFrom: "2026-10-02", validTo: TODAY }, defaults);
assert.equal(!reversed.ok && reversed.errors.validity, "rangeInvalid");

const wasLower = buildPosterSpec({ ...special, wasPrice: "8.00" }, defaults);
assert.equal(!wasLower.ok && wasLower.errors.wasPrice, "wasNotHigher", "原价必须高于现价");

assert.equal(buildPosterSpec({ ...special, wasPrice: "" }, defaults).ok, true, "Special 原价可留空");
for (const wasPrice of ["0", "-1", "bad", "1.234", "9.09"]) {
  assert.equal(buildPosterSpec({ ...special, wasPrice }, defaults).ok, false, "填写的原价须有效且高于现价");
}
assert.equal(buildPosterSpec({ ...clearance, wasPrice: "" }, clearanceDefaults).ok, false, "清仓仍要求原价");

const badTitle = buildPosterSpec({ ...special, title: "保温瓶" }, defaults);
assert.equal(!badTitle.ok && badTitle.errors.title?.code, "unprintable");

const missingPrice = buildPosterSpec({ ...fresh, price: "" }, defaults);
assert.equal(!missingPrice.ok && missingPrice.errors.price, "required");

const freshResult = buildPosterSpec(fresh, defaults);
assert.equal(freshResult.ok && freshResult.spec.inStoreSince, TODAY);
assert.equal(freshResult.ok && "wasPrice" in freshResult.spec, false, "新品不带原价");

const multiResult = buildPosterSpec(multi, defaults);
assert.deepEqual(multiResult.ok && multiResult.spec, {
  kind: "multibuy",
  style: "classic",
  size: "A6",
  productCode: "P1",
  itemNumber: "K1048",
  title: "Stainless Steel Vacuum Flask 500ml",
  price: 10,
  quantity: 3,
  unitPrice: 12.99,
  mixAndMatch: true,
  validFrom: "2026-09-19",
  validTo: "2026-10-02",
});
const noOffer = buildPosterSpec({ ...multi, offerId: "missing" }, defaults);
assert.equal(!noOffer.ok && noOffer.errors.offer, "required");

// ---------------------------------------------------------------- 收银价不一致提示

assert.equal(resolvePriceMismatch(special, defaults), null);
assert.deepEqual(resolvePriceMismatch({ kind: "special", price: "8.99" }, defaults), { kind: "special", expected: 9.09 });
assert.deepEqual(
  resolvePriceMismatch({ kind: "clearance", price: "4.00" }, clearanceDefaults),
  { kind: "clearance", expected: 5 },
);
assert.equal(resolvePriceMismatch({ kind: "clearance", price: "5" }, clearanceDefaults), null);
assert.equal(resolvePriceMismatch({ kind: "new", price: "1.00" }, defaults), null, "新品不做一致性提示");
assert.equal(resolvePriceMismatch({ kind: "special", price: "x" }, defaults), null, "价格无效时不提示不一致");

// ---------------------------------------------------------------- 节省与展示

assert.deepEqual(computePosterSaving({ kind: "special", price: 9.09, wasPrice: 12.99 }), { amount: 3.9, percent: 30 });
assert.deepEqual(computePosterSaving({ kind: "clearance", price: 5, wasPrice: 14.99 }), { amount: 9.99, percent: 67 });
assert.deepEqual(computePosterSaving({ kind: "multibuy", price: 10, quantity: 3, unitPrice: 3.99 }), { amount: 1.97, percent: 16 });
assert.equal(computePosterSaving({ kind: "multibuy", price: 10, quantity: 3, unitPrice: 3 }), null, "没有节省时不显示 SAVE");
assert.equal(computePosterSaving({ kind: "new", price: 6.49 }), null);
assert.deepEqual(splitPosterPrice(9.09), { dollars: "9", cents: "09" });
assert.deepEqual(splitPosterPrice(10), { dollars: "10", cents: "00" });
assert.equal(formatPosterPriceText({ kind: "multibuy", price: 10, quantity: 3 }), "3 for $10");
assert.equal(formatPosterPriceText({ kind: "multibuy", price: 10.5, quantity: 3 }), "3 for $10.50");
assert.equal(formatPosterPriceText({ kind: "special", price: 9.09 }), "$9.09");
assert.equal(formatPosterValidity("2026-09-19", "2026-10-02", false), "Valid 19 Sep – 2 Oct 2026");
assert.equal(formatPosterValidity("2026-09-19", "2026-10-02", true), "Until 2 Oct");
assert.equal(formatPosterValidity(undefined, undefined, false), "");
assert.equal(toDateOnly("2026-10-02T23:59:59"), "2026-10-02");
assert.equal(toDateOnly("bad"), "");
assert.equal(addDaysToDateOnly("2026-09-19", 13), "2026-10-02");
assert.equal(addDaysToDateOnly("2026-12-31", 1), "2027-01-01");

// ---------------------------------------------------------------- 页数与分组

assert.equal(countPosterPages(["A6", "A6", "A6", "A4"], true), 2, "3 张 A6 拼 1 页 + 1 张 A4");
assert.equal(countPosterPages(["A6", "A6", "A6", "A6", "A6"], true), 2, "5 张 A6 需要 2 页");
assert.equal(countPosterPages(["A5", "A5", "A5"], true), 2);
assert.equal(countPosterPages(Array.from({ length: 8 }, () => "A7" as const), true), 1);
assert.equal(countPosterPages(["A7", "A5", "A6"], true), 3, "不同尺寸不混拼，各自占页");
assert.equal(countPosterPages(["A6", "A6", "A6", "A4"], false), 4, "不拼版时每张一页");
assert.equal(countPosterPages([], true), 0);

function queueItem(id: string, size: PromoPosterSpec["size"], storeCode = "S1"): PromoPosterQueueItem {
  return {
    id,
    storeCode,
    productName: "商品",
    addedAt: "2026-09-19T00:00:00.000Z",
    poster: { kind: "new", style: "classic", size, productCode: id, itemNumber: id, title: "Item", price: 1 },
  };
}

const groups = groupPostersBySize([queueItem("a", "A4"), queueItem("b", "A6"), queueItem("c", "A6"), queueItem("d", "A6")]);
assert.deepEqual(
  groups.map((group) => [group.size, group.count, group.imposedPages, group.items.map((item) => item.id)]),
  [
    ["A6", 3, 1, ["b", "c", "d"]],
    ["A4", 1, 1, ["a"]],
  ],
  "数量多的尺寸排前，组内保持加入顺序",
);
assert.deepEqual(
  groupPostersBySize([queueItem("a", "A7"), queueItem("b", "A5")]).map((group) => group.size),
  ["A5", "A7"],
  "数量相同按 A4→A7 顺序",
);

// ---------------------------------------------------------------- 入队规则

assert.equal(resolveQueueAddition([], "S1"), "ok");
assert.equal(resolveQueueAddition([queueItem("a", "A6", "s1")], "S1"), "ok", "分店编码比较忽略大小写");
assert.equal(resolveQueueAddition([queueItem("a", "A6", "S2")], "S1"), "storeConflict");
assert.equal(
  resolveQueueAddition(Array.from({ length: PROMO_POSTER_QUEUE_LIMIT }, (_, index) => queueItem(String(index), "A6")), "S1"),
  "full",
  "最多 200 张",
);

// ---------------------------------------------------------------- PDF 请求体

const request = buildPromoPosterPdfRequest("S1", true, [
  { ...(specialResult.ok ? specialResult.spec : ({} as PromoPosterSpec)), inStoreSince: TODAY, quantity: 9 },
  multiResult.ok ? multiResult.spec : ({} as PromoPosterSpec),
  { kind: "clearance", style: "modern", size: "A4", productCode: "P2", itemNumber: "W2093", title: "Boots", price: 5, wasPrice: 14.99, validFrom: TODAY },
  { kind: "new", style: "classic", size: "A5", productCode: "P3", itemNumber: "K5031", title: "Bowl", price: 6.49, wasPrice: 9, inStoreSince: TODAY },
  { kind: "new", style: LOW_INK_STYLE, size: "A7", productCode: "P4", itemNumber: "K5032", title: "Mug", price: 3.5, inStoreSince: TODAY },
]);
assert.equal(request.storeCode, "S1");
assert.equal(request.impose, true);
assert.equal(request.showLogo, true, "未指定时默认显示 Logo");
const hiddenLogoRequest = buildPromoPosterPdfRequest("S1", true, [regularResult.spec], false);
assert.equal(hiddenLogoRequest.showLogo, false);
assert.equal("wasPrice" in hiddenLogoRequest.posters[0], false);
assert.equal(buildPromoPosterPdfRequest("S1", false, [regularResult.spec], true).showLogo, true);
assert.deepEqual(request.posters[0], {
  kind: "special",
  style: "classic",
  size: "A6",
  productCode: "P1",
  itemNumber: "K1048",
  title: "Stainless Steel Vacuum Flask 500ml",
  price: 9.09,
  wasPrice: 12.99,
}, "特价只保留现价/原价/有效期，剔除其它类型字段");
assert.deepEqual(request.posters[1], multiResult.ok ? multiResult.spec : null, "多件价带件数、单价、Mix & match 与促销日期");
assert.deepEqual(request.posters[2], {
  kind: "clearance",
  style: "modern",
  size: "A4",
  productCode: "P2",
  itemNumber: "W2093",
  title: "Boots",
  price: 5,
  wasPrice: 14.99,
}, "清仓不带有效期");
assert.deepEqual(request.posters[3], {
  kind: "new",
  style: "classic",
  size: "A5",
  productCode: "P3",
  itemNumber: "K5031",
  title: "Bowl",
  price: 6.49,
  inStoreSince: TODAY,
}, "新品不传 wasPrice");
assert.equal(request.posters[4].style, LOW_INK_STYLE, "PDF 请求应保留省彩墨风格");

assert.equal(buildPromoPosterFileName(new Date(2026, 8, 19, 15, 30, 45)), "HB-Posters-20260919-153045.pdf");

// ---------------------------------------------------------------- 本地队列恢复

const restored = normalizeStoredQueueSnapshot({
  style: LOW_INK_STYLE,
  size: "A5",
  impose: false,
  items: [
    queueItem("ok", "A6"),
    { ...queueItem("low-ink", "A7"), poster: { ...queueItem("low-ink-source", "A7").poster, style: LOW_INK_STYLE } },
    { ...queueItem("bad-kind", "A6"), poster: { ...queueItem("x", "A6").poster, kind: "sale" } },
    { ...queueItem("bad-title", "A6"), poster: { ...queueItem("x", "A6").poster, title: "中文" } },
    { id: "", storeCode: "S1", poster: queueItem("x", "A6").poster },
    "garbage",
  ],
});
assert.deepEqual(restored.items.map((item) => item.id), ["ok", "low-ink"], "结构不对的条目直接丢弃");
assert.equal(restored.items[1].poster.style, LOW_INK_STYLE, "恢复队列条目应保留省彩墨风格");
assert.equal(restored.style, LOW_INK_STYLE);
assert.equal(restored.size, "A5");
assert.equal(restored.impose, false);
assert.equal(restored.showLogo, true, "旧队列没有 Logo 字段时默认开启");
assert.equal(normalizeStoredQueueSnapshot({ ...restored, showLogo: false }).showLogo, false, "恢复未完成批次时保留关闭设置");
assert.equal(normalizeStoredQueueSnapshot({ items: [], showLogo: false }).showLogo, true, "无待打印条目时恢复新批次默认值");
assert.deepEqual(normalizeStoredQueueSnapshot(null), { items: [], style: "classic", size: "A6", impose: true, showLogo: true });
assert.deepEqual(normalizeStoredQueueSnapshot({ style: "retro", size: "B5", impose: "yes" }), {
  items: [],
  style: "classic",
  size: "A6",
  impose: true,
  showLogo: true,
});

// ---------------------------------------------------------------- 错误体解码

const errorBytes = new TextEncoder().encode(JSON.stringify({ success: false, message: "最多 200 张海报" }));
assert.equal(extractBinaryErrorMessage(errorBytes.buffer), "最多 200 张海报", "arraybuffer 错误体解码出 message");
assert.equal(extractBinaryErrorMessage(errorBytes), "最多 200 张海报", "TypedArray 同样支持");
assert.deepEqual(parseBinaryJsonBody(errorBytes.buffer), { success: false, message: "最多 200 张海报" });
assert.equal(extractBinaryErrorMessage(new TextEncoder().encode("%PDF-1.7").buffer), null, "非 JSON 返回 null");
assert.equal(extractBinaryErrorMessage({ message: "plain" }), "plain");

// 手工 UTF-8 解码兜底（模拟没有 TextDecoder 的运行时）
const originalTextDecoder = globalThis.TextDecoder;
try {
  (globalThis as { TextDecoder?: unknown }).TextDecoder = undefined;
  assert.equal(decodeUtf8(new TextEncoder().encode("A–中😀")), "A–中😀");
} finally {
  (globalThis as { TextDecoder?: unknown }).TextDecoder = originalTextDecoder;
}

console.log("promo-posters logic tests passed");
