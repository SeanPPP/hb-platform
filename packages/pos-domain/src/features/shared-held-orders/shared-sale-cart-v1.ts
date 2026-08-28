import { normalizeLineSyncProvenance } from "../../core/contracts/line-sync-provenance";
import type { PricingCartStateSnapshot } from "../../core/contracts/pricing-cart-state";
import type { BackendPriceSource } from "../../core/contracts/line-sync-provenance";
import { multiplyCentsAwayFromZero } from "../../core/contracts/money";

/**
 * 冻结 canonical：与 WPF SharedHeldOrderContracts.cs（SharedSaleCartV1）逐字段一致，
 * JSON camelCase、pricingState 嵌套。只接受普通 sale：
 *   pricingState.mode=sale、每条 kind=sale、return/original 字段为 null、
 *   basePriceSource 仅 catalog/manual；金额一律为整数分。
 * 不直接复用 PricingCartStateSnapshot 字段名（其 discount kind、promotion fixedPrice
 * 需经 toSharedSaleCartV1 显式映射）。
 */

export type SharedPromotionProductV1 = Readonly<{
  productCode: string;
  /** C# decimal：可为小数（称重商品），范围 0..1_000_000。 */
  unitWeight: number;
}>;

export type SharedPromotionV1 = Readonly<{
  id: string;
  name: string;
  effectiveStartIso: string;
  effectiveEndIso: string;
  isExclusive: boolean;
  priority: number;
  applyQuantity: number;
  /** 整数分；wire 上是标量，不复用快照的 Money 对象。 */
  fixedPriceCents: number;
  maxApplicationsPerOrder: number | null;
  products: readonly SharedPromotionProductV1[];
}>;

export type SharedLineSyncProvenanceV1 = Readonly<{
  referenceCode: string | null;
  priceSource: BackendPriceSource;
}>;

export type SharedLineDiscountStateV1 =
  | Readonly<{ mode: "none" }>
  | Readonly<{ mode: "manual-amount"; cents: number }>
  | Readonly<{ mode: "manual-percent"; basisPoints: number }>
  | Readonly<{
      mode: "promotion";
      cents: number;
      promotionIds: readonly string[];
    }>;

export type SharedSaleLineV1 = Readonly<{
  lineId: string;
  productCode: string;
  itemNumber: string | null;
  lookupCode: string;
  displayName: string;
  /** C# decimal：可为小数（称重商品），范围 >0..1_000_000。 */
  quantity: number;
  unitPriceCents: number;
  basePriceSource: "catalog" | "manual";
  syncProvenance: SharedLineSyncProvenanceV1 | null;
  kind: "sale";
  returnSourceKey: null;
  originalOrderGuid: null;
  originalOrderDetailGuid: null;
  discountState: SharedLineDiscountStateV1;
}>;

export type SharedPricingStateV1 = Readonly<{
  revision: number;
  mode: "sale";
  asOfIso: string;
  promotions: readonly SharedPromotionV1[];
  lines: readonly SharedSaleLineV1[];
}>;

export type SharedSaleCartV1 = Readonly<{
  version: 1;
  pricingState: SharedPricingStateV1;
}>;

export type SharedSaleCartValidationCode =
  | "SHARED_CART_VERSION_UNSUPPORTED"
  | "SHARED_CART_MODE_NOT_SALE"
  | "SHARED_CART_LINE_KIND_NOT_SALE"
  | "SHARED_CART_RETURN_ORIGINAL_NOT_EMPTY"
  | "SHARED_CART_INVALID";

/** 稳定机器码错误：legacy evaluator 据此转 Blocked 原因，禁止自由文本。 */
export class SharedSaleCartValidationError extends TypeError {
  public readonly code: SharedSaleCartValidationCode;

  public constructor(code: SharedSaleCartValidationCode, message: string) {
    super(message);
    this.name = "SharedSaleCartValidationError";
    this.code = code;
  }
}

const CART_KEYS = new Set(["version", "pricingState"]);
const PRICING_KEYS = new Set([
  "revision",
  "mode",
  "asOfIso",
  "promotions",
  "lines",
]);
const LINE_KEYS = new Set([
  "lineId",
  "productCode",
  "itemNumber",
  "lookupCode",
  "displayName",
  "quantity",
  "unitPriceCents",
  "basePriceSource",
  "syncProvenance",
  "kind",
  "returnSourceKey",
  "originalOrderGuid",
  "originalOrderDetailGuid",
  "discountState",
]);
const PROMOTION_KEYS = new Set([
  "id",
  "name",
  "effectiveStartIso",
  "effectiveEndIso",
  "isExclusive",
  "priority",
  "applyQuantity",
  "fixedPriceCents",
  "maxApplicationsPerOrder",
  "products",
]);
const PRODUCT_KEYS = new Set(["productCode", "unitWeight"]);
const ISO_OFFSET_SUFFIX = /(?:Z|[+-]\d{2}:\d{2})$/;
/** 金额统一为整数分，上限与 C# SharedSaleCartV1Constants.MaxCents 一致。 */
const MAX_AMOUNT_CENTS = 1_000_000_000_000;
const MAX_QUANTITY = 1_000_000;
const MAX_UNIT_WEIGHT = 1_000_000;
/** revision/priority/applyQuantity/maxApplications 的公共整数上限。 */
const MAX_COUNT = 1_000_000;
const MAX_LINES = 1_000;
const MAX_PROMOTIONS = 100;
const MAX_PROMOTION_PRODUCTS = 100;
const MAX_CODE_LENGTH = 64;
const MAX_NAME_LENGTH = 200;
const MAX_REFERENCE_LENGTH = 128;

/** 规范化入口：非法输入一律抛 SharedSaleCartValidationError（稳定 code）。 */
export function normalizeSharedSaleCartV1(input: unknown): SharedSaleCartV1 {
  if (!isRecord(input)) {
    throw invalid("SHARED_CART_INVALID", "shared sale cart must be an object");
  }
  assertOnlyKeys(input, CART_KEYS, "shared sale cart");

  if (input.version !== 1) {
    throw invalid(
      "SHARED_CART_VERSION_UNSUPPORTED",
      `unsupported shared sale cart version: ${String(input.version)}`,
    );
  }
  const pricing = input.pricingState;
  if (!isRecord(pricing)) {
    throw invalid("SHARED_CART_INVALID", "pricingState must be an object");
  }
  assertOnlyKeys(pricing, PRICING_KEYS, "pricingState");
  if (pricing.mode !== "sale") {
    throw invalid(
      "SHARED_CART_MODE_NOT_SALE",
      "shared sale cart only supports mode=sale",
    );
  }

  const revision = positiveSafeInteger(
    pricing.revision,
    "pricingState revision",
    MAX_COUNT,
  );
  const asOfIso = canonicalIso(pricing.asOfIso, "pricingState as-of time");
  if (!Array.isArray(pricing.promotions) || !Array.isArray(pricing.lines)) {
    throw invalid("SHARED_CART_INVALID", "pricingState collections must be arrays");
  }
  if (pricing.promotions.length > MAX_PROMOTIONS) {
    throw invalid(
      "SHARED_CART_INVALID",
      `pricingState promotions must not exceed ${MAX_PROMOTIONS} items`,
    );
  }
  if (pricing.lines.length < 1 || pricing.lines.length > MAX_LINES) {
    throw invalid(
      "SHARED_CART_INVALID",
      `pricingState lines must contain 1 to ${MAX_LINES} items`,
    );
  }

  const promotions = pricing.promotions.map(validatePromotion);
  const promotionIds = new Set(promotions.map((promotion) => promotion.id));
  if (promotionIds.size !== promotions.length) {
    throw invalid("SHARED_CART_INVALID", "promotion id must be unique");
  }
  const seenLineIds = new Set<string>();
  let totalGrossCents = 0;
  const lines = pricing.lines.map((line) => {
    const validated = validateLine(line, seenLineIds, promotionIds);
    totalGrossCents = accumulateGrossCents(totalGrossCents, validated.grossCents);
    return validated.line;
  });
  return deepFreeze({
    version: 1,
    pricingState: {
      revision,
      mode: "sale",
      asOfIso,
      promotions,
      lines,
    },
  }) as SharedSaleCartV1;
}

function validateLine(
  value: unknown,
  seenLineIds: Set<string>,
  promotionIds: ReadonlySet<string>,
): Readonly<{ line: SharedSaleLineV1; grossCents: number }> {
  if (!isRecord(value)) {
    throw invalid("SHARED_CART_INVALID", "cart line must be an object");
  }
  assertOnlyKeys(value, LINE_KEYS, "cart line");
  if (value.kind !== "sale") {
    throw invalid(
      "SHARED_CART_LINE_KIND_NOT_SALE",
      "shared sale cart only supports kind=sale lines",
    );
  }
  if (
    value.returnSourceKey !== null ||
    value.originalOrderGuid !== null ||
    value.originalOrderDetailGuid !== null
  ) {
    throw invalid(
      "SHARED_CART_RETURN_ORIGINAL_NOT_EMPTY",
      "shared sale cart lines must have empty return/original fields",
    );
  }

  const lineId = nonBlank(value.lineId, "cart line id", MAX_CODE_LENGTH);
  if (seenLineIds.has(lineId)) {
    throw invalid("SHARED_CART_INVALID", "duplicate cart line id");
  }
  seenLineIds.add(lineId);
  const productCode = nonBlank(value.productCode, "cart product code", MAX_CODE_LENGTH);
  const lookupCode = nonBlank(value.lookupCode, "cart lookup code", MAX_CODE_LENGTH);
  const displayName = nonBlank(value.displayName, "cart display name", MAX_NAME_LENGTH);
  const itemNumber = nullableText(value.itemNumber, "cart item number", MAX_CODE_LENGTH);
  const quantity = finiteQuantity(value.quantity, "cart quantity");
  const unitPriceCents = nonNegativeSafeInteger(
    value.unitPriceCents,
    "cart unit price",
    MAX_AMOUNT_CENTS,
  );
  const basePriceSource = value.basePriceSource;
  if (basePriceSource !== "catalog" && basePriceSource !== "manual") {
    throw invalid(
      "SHARED_CART_INVALID",
      "basePriceSource must be 'catalog' or 'manual'; promotion/open-item are not supported",
    );
  }
  const syncProvenance =
    value.syncProvenance === null || value.syncProvenance === undefined
      ? null
      : normalizeProvenance(value.syncProvenance);
  const grossCents = multiplyCents(quantity, unitPriceCents, "cart line gross");
  const discountState = validateDiscountState(
    value.discountState,
    grossCents,
    promotionIds,
  );
  return {
    line: deepFreeze({
      lineId,
      productCode,
      itemNumber,
      lookupCode,
      displayName,
      quantity,
      unitPriceCents,
      basePriceSource,
      syncProvenance,
      kind: "sale",
      returnSourceKey: null,
      originalOrderGuid: null,
      originalOrderDetailGuid: null,
      discountState,
    }) as SharedSaleLineV1,
    grossCents,
  };
}

function normalizeProvenance(value: unknown): SharedLineSyncProvenanceV1 {
  try {
    const provenance = normalizeLineSyncProvenance(value);
    if (
      provenance.referenceCode !== null &&
      provenance.referenceCode.length > MAX_REFERENCE_LENGTH
    ) {
      throw invalid(
        "SHARED_CART_INVALID",
        `cart sync provenance referenceCode must not exceed ${MAX_REFERENCE_LENGTH} characters`,
      );
    }
    return provenance;
  } catch (error) {
    if (error instanceof SharedSaleCartValidationError) {
      throw error;
    }
    throw invalid("SHARED_CART_INVALID", "invalid cart sync provenance");
  }
}

function validatePromotion(value: unknown): SharedPromotionV1 {
  if (!isRecord(value)) {
    throw invalid("SHARED_CART_INVALID", "cart promotion must be an object");
  }
  assertOnlyKeys(value, PROMOTION_KEYS, "cart promotion");
  const effectiveStartIso = canonicalIso(
    value.effectiveStartIso,
    "promotion start",
  );
  const effectiveEndIso = canonicalIso(value.effectiveEndIso, "promotion end");
  if (Date.parse(effectiveStartIso) > Date.parse(effectiveEndIso)) {
    throw invalid("SHARED_CART_INVALID", "promotion date range is invalid");
  }
  if (typeof value.isExclusive !== "boolean") {
    throw invalid("SHARED_CART_INVALID", "promotion exclusivity must be boolean");
  }
  if (
    !Array.isArray(value.products) ||
    value.products.length === 0 ||
    value.products.length > MAX_PROMOTION_PRODUCTS
  ) {
    throw invalid(
      "SHARED_CART_INVALID",
      `promotion products must contain 1 to ${MAX_PROMOTION_PRODUCTS} items`,
    );
  }
  const maxApplications =
    value.maxApplicationsPerOrder === null ||
    value.maxApplicationsPerOrder === undefined
      ? null
      : positiveSafeInteger(
          value.maxApplicationsPerOrder,
          "promotion max applications",
          MAX_COUNT,
        );
  return deepFreeze({
    id: nonBlank(value.id, "promotion id", MAX_CODE_LENGTH),
    name: nonBlank(value.name, "promotion name", MAX_NAME_LENGTH),
    effectiveStartIso,
    effectiveEndIso,
    isExclusive: value.isExclusive,
    priority: nonNegativeSafeInteger(
      value.priority,
      "promotion priority",
      MAX_COUNT,
    ),
    applyQuantity: positiveSafeInteger(
      value.applyQuantity,
      "promotion apply quantity",
      MAX_COUNT,
    ),
    fixedPriceCents: nonNegativeSafeInteger(
      value.fixedPriceCents,
      "promotion fixed price cents",
      MAX_AMOUNT_CENTS,
    ),
    maxApplicationsPerOrder: maxApplications,
    products: value.products.map(validatePromotionProduct),
  }) as SharedPromotionV1;
}

function validatePromotionProduct(value: unknown): SharedPromotionProductV1 {
  if (!isRecord(value)) {
    throw invalid("SHARED_CART_INVALID", "promotion product must be an object");
  }
  assertOnlyKeys(value, PRODUCT_KEYS, "promotion product");
  return deepFreeze({
    productCode: nonBlank(
      value.productCode,
      "promotion product code",
      MAX_CODE_LENGTH,
    ),
    unitWeight: finiteNonNegative(
      value.unitWeight,
      "promotion product unit weight",
      MAX_UNIT_WEIGHT,
    ),
  }) as SharedPromotionProductV1;
}

function validateDiscountState(
  value: unknown,
  gross: number,
  promotionIds: ReadonlySet<string>,
): SharedLineDiscountStateV1 {
  if (!isRecord(value) || typeof value.mode !== "string") {
    throw invalid("SHARED_CART_INVALID", "cart discount must be an object with mode");
  }
  switch (value.mode) {
    case "none": {
      assertOnlyKeys(value, new Set(["mode"]), "none discount");
      return deepFreeze({ mode: "none" });
    }
    case "manual-amount": {
      assertOnlyKeys(value, new Set(["mode", "cents"]), "manual-amount discount");
      const cents = nonNegativeSafeInteger(
        value.cents,
        "manual discount cents",
        MAX_AMOUNT_CENTS,
      );
      if (cents > gross) {
        throw invalid("SHARED_CART_INVALID", "manual discount exceeds gross");
      }
      return deepFreeze({ mode: "manual-amount", cents });
    }
    case "manual-percent": {
      assertOnlyKeys(
        value,
        new Set(["mode", "basisPoints"]),
        "manual-percent discount",
      );
      const basisPoints = nonNegativeSafeInteger(
        value.basisPoints,
        "manual percent discount",
      );
      if (basisPoints < 1 || basisPoints > 10_000) {
        throw invalid(
          "SHARED_CART_INVALID",
          "percent discount must be between 1 and 10000 basis points",
        );
      }
      return deepFreeze({ mode: "manual-percent", basisPoints });
    }
    case "promotion": {
      assertOnlyKeys(
        value,
        new Set(["mode", "cents", "promotionIds"]),
        "promotion discount",
      );
      const cents = nonNegativeSafeInteger(
        value.cents,
        "promotion discount cents",
        MAX_AMOUNT_CENTS,
      );
      if (cents > gross) {
        throw invalid("SHARED_CART_INVALID", "invalid promotion discount");
      }
      if (!Array.isArray(value.promotionIds) || value.promotionIds.length === 0) {
        throw invalid(
          "SHARED_CART_INVALID",
          "promotion ids must be a non-empty array",
        );
      }
      const ids = value.promotionIds.map((id) =>
        nonBlank(id, "promotion discount id", MAX_CODE_LENGTH),
      );
      if (new Set(ids).size !== ids.length) {
        throw invalid("SHARED_CART_INVALID", "promotion discount ids must be unique");
      }
      for (const id of ids) {
        if (!promotionIds.has(id)) {
          throw invalid(
            "SHARED_CART_INVALID",
            `promotion discount id must reference a frozen promotion: ${id}`,
          );
        }
      }
      return deepFreeze({ mode: "promotion", cents, promotionIds: ids });
    }
    default:
      throw invalid("SHARED_CART_INVALID", "unknown discount mode");
  }
}

/**
 * 显式映射器：把现有 PricingCartStateSnapshot（扁平 pricingState、discount kind、
 * promotion fixedPrice=Money）转成冻结 wire。只做字段名/结构转换，不做金额计算；
 * 合法性与稳定错误码统一由 normalizeSharedSaleCartV1 校验。
 */
export function toSharedSaleCartV1(
  snapshot: Readonly<PricingCartStateSnapshot>,
): SharedSaleCartV1 {
  if (
    snapshot.lines.some(
      (line) => (line.catalogDiscountBasisPoints ?? 0) !== 0,
    )
  ) {
    throw invalid(
      "SHARED_CART_INVALID",
      "cannot encode a catalog discount baseline in SharedSaleCartV1",
    );
  }
  return {
    version: 1,
    pricingState: {
      revision: snapshot.revision,
      mode: snapshot.mode,
      asOfIso: snapshot.asOfIso,
      promotions: snapshot.promotions.map((promotion) => ({
        id: promotion.id,
        name: promotion.name,
        effectiveStartIso: promotion.effectiveStartIso,
        effectiveEndIso: promotion.effectiveEndIso,
        isExclusive: promotion.isExclusive,
        priority: promotion.priority,
        applyQuantity: promotion.applyQuantity,
        fixedPriceCents: promotion.fixedPrice?.cents,
        maxApplicationsPerOrder: promotion.maxApplicationsPerOrder,
        products: promotion.products.map((product) => ({
          productCode: product.productCode,
          unitWeight: product.unitWeight,
        })),
      })),
      lines: snapshot.lines.map((line) => ({
        lineId: line.lineId,
        productCode: line.productCode,
        itemNumber: line.itemNumber,
        lookupCode: line.lookupCode,
        displayName: line.displayName,
        quantity: line.quantity,
        unitPriceCents: line.unitPriceCents,
        basePriceSource: line.basePriceSource,
        syncProvenance: line.syncProvenance ?? null,
        kind: line.kind,
        returnSourceKey: line.returnSourceKey,
        originalOrderGuid: line.originalOrderGuid,
        originalOrderDetailGuid: line.originalOrderDetailGuid,
        discountState: toDiscountState(line.discountState),
      })),
    },
  } as SharedSaleCartV1;
}

function toDiscountState(
  discount: PricingCartStateSnapshot["lines"][number]["discountState"],
): SharedLineDiscountStateV1 {
  switch (discount.kind) {
    case "none":
      return { mode: "none" };
    case "manual-amount":
      return { mode: "manual-amount", cents: discount.cents };
    case "manual-percent":
      return { mode: "manual-percent", basisPoints: discount.basisPoints };
    case "promotion":
      return {
        mode: "promotion",
        cents: discount.cents,
        promotionIds: discount.promotionIds,
      };
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function assertOnlyKeys(
  value: Record<string, unknown>,
  allowed: ReadonlySet<string>,
  label: string,
): void {
  for (const key of Object.keys(value)) {
    if (!allowed.has(key)) {
      throw invalid(
        "SHARED_CART_INVALID",
        `${label} contains unsupported field: ${key}`,
      );
    }
  }
}

function nonBlank(value: unknown, label: string, max = Number.MAX_SAFE_INTEGER): string {
  if (typeof value !== "string") {
    throw invalid("SHARED_CART_INVALID", `${label} must be a string`);
  }
  const normalized = value.trim();
  if (!normalized) {
    throw invalid("SHARED_CART_INVALID", `${label} must not be blank`);
  }
  if (normalized.length > max) {
    throw invalid(
      "SHARED_CART_INVALID",
      `${label} must not exceed ${max} characters`,
    );
  }
  return normalized;
}

function nullableText(
  value: unknown,
  label: string,
  max = Number.MAX_SAFE_INTEGER,
): string | null {
  if (value === null || value === undefined) return null;
  return nonBlank(value, label, max);
}

function nonNegativeSafeInteger(
  value: unknown,
  label: string,
  max = Number.MAX_SAFE_INTEGER,
): number {
  if (
    typeof value !== "number" ||
    !Number.isSafeInteger(value) ||
    value < 0 ||
    value > max
  ) {
    throw invalid(
      "SHARED_CART_INVALID",
      `${label} must be a safe integer between 0 and ${max}`,
    );
  }
  return value;
}

function positiveSafeInteger(
  value: unknown,
  label: string,
  max = Number.MAX_SAFE_INTEGER,
): number {
  if (
    typeof value !== "number" ||
    !Number.isSafeInteger(value) ||
    value < 1 ||
    value > max
  ) {
    throw invalid(
      "SHARED_CART_INVALID",
      `${label} must be a safe integer between 1 and ${max}`,
    );
  }
  return value;
}

/** C# decimal 语义：finite、>0、<=1_000_000，可为小数（称重商品）。 */
function finiteQuantity(value: unknown, label: string): number {
  if (
    typeof value !== "number" ||
    !Number.isFinite(value) ||
    value <= 0 ||
    value > MAX_QUANTITY
  ) {
    throw invalid(
      "SHARED_CART_INVALID",
      `${label} must be finite, positive and at most ${MAX_QUANTITY}`,
    );
  }
  return value;
}

/** C# decimal 语义：finite、>=0、<=1_000_000，可为小数。 */
function finiteNonNegative(value: unknown, label: string, max: number): number {
  if (
    typeof value !== "number" ||
    !Number.isFinite(value) ||
    value < 0 ||
    value > max
  ) {
    throw invalid(
      "SHARED_CART_INVALID",
      `${label} must be finite, non-negative and at most ${max}`,
    );
  }
  return value;
}

function multiplyCents(
  quantity: number,
  unitPriceCents: number,
  label: string,
): number {
  // 与 C# SharedHeldOrderService.Summarize 一致：
  // decimal.Round(UnitPriceCents * Quantity, MidpointRounding.AwayFromZero)。
  // 用十进制字符串/整数算法，避免 0.29 * 50 -> 14.499999999999998 错取 14。
  try {
    return multiplyCentsAwayFromZero(quantity, unitPriceCents, label);
  } catch {
    throw invalid(
      "SHARED_CART_INVALID",
      `${label} must be a finite safe-integer cents product`,
    );
  }
}

/** 安全累加：total 恒 <= MAX_SAFE_INTEGER，先比较再相加。
 *  MAX_SAFE_INTEGER - total 与 total + gross 均为 <= 2^53-1 的整数，精确无精度丢失。 */
function accumulateGrossCents(total: number, gross: number): number {
  if (gross > Number.MAX_SAFE_INTEGER - total) {
    throw invalid(
      "SHARED_CART_INVALID",
      `cart line gross total must not exceed ${Number.MAX_SAFE_INTEGER}`,
    );
  }
  return total + gross;
}

function canonicalIso(value: unknown, label: string): string {
  const normalized = nonBlank(value, label);
  if (!Number.isFinite(Date.parse(normalized))) {
    throw invalid("SHARED_CART_INVALID", `${label} must be a valid ISO time`);
  }
  const offset = ISO_OFFSET_SUFFIX.exec(normalized)?.[0];
  if (offset && offset !== "Z" && offset !== "+00:00" && offset !== "-00:00") {
    throw invalid("SHARED_CART_INVALID", `${label} must be a UTC ISO time`);
  }
  return normalized;
}

function invalid(
  code: SharedSaleCartValidationCode,
  message: string,
): SharedSaleCartValidationError {
  return new SharedSaleCartValidationError(code, message);
}

function deepFreeze<T>(value: T): T {
  if (value === null || typeof value !== "object") return value;
  const record = value as Record<string, unknown>;
  for (const key of Object.keys(record)) {
    deepFreeze(record[key]);
  }
  return Object.freeze(value);
}
