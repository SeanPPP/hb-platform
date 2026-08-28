import {
  normalizeSharedSaleCartV1,
  toSharedSaleCartV1,
  SharedSaleCartValidationError,
  type SharedSaleCartV1,
  type SharedPricingStateV1,
  type SharedSaleLineV1,
} from "@hb/pos-domain/features/shared-held-orders/shared-sale-cart-v1";

import type { PricingCartStateSnapshot } from "@/core/contracts";

export { SharedSaleCartValidationError };

/**
 * V2 canonical：与 V1 冻结 wire 逐字段对齐，仅 line 增加
 * catalogDiscountBasisPoints（0..10000 bps）。V1 文件不修改、不放宽。
 */

export type SharedSaleLineV2 = SharedSaleLineV1 &
  Readonly<{
    catalogDiscountBasisPoints: number;
  }>;

export type SharedPricingStateV2 = Omit<
  SharedPricingStateV1,
  "lines"
> &
  Readonly<{
    lines: readonly SharedSaleLineV2[];
  }>;

export type SharedSaleCartV2 = Readonly<{
  version: 2;
  pricingState: SharedPricingStateV2;
}>;

export type SharedSaleCartPayload = SharedSaleCartV1 | SharedSaleCartV2;

const V2_CART_KEYS = new Set(["version", "pricingState"]);
const V2_LINE_KEYS = new Set([
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
  "catalogDiscountBasisPoints",
]);
const MAX_CATALOG_BASIS_POINTS = 10_000;

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function invalid(message: string): SharedSaleCartValidationError {
  return new SharedSaleCartValidationError("SHARED_CART_INVALID", message);
}

function deepFreeze<T>(value: T): T {
  if (value === null || typeof value !== "object") return value;
  const record = value as Record<string, unknown>;
  for (const key of Object.keys(record)) {
    deepFreeze(record[key]);
  }
  return Object.freeze(value);
}

function assertOnlyKeys(
  value: Record<string, unknown>,
  allowed: ReadonlySet<string>,
  label: string,
): void {
  for (const key of Object.keys(value)) {
    if (!allowed.has(key)) {
      throw invalid(`${label} contains unsupported field: ${key}`);
    }
  }
}

function normalizeBasisPoints(value: unknown, label: string): number {
  if (
    typeof value !== "number" ||
    !Number.isSafeInteger(value) ||
    value < 0 ||
    value > MAX_CATALOG_BASIS_POINTS
  ) {
    throw invalid(
      `${label} must be a safe integer between 0 and ${MAX_CATALOG_BASIS_POINTS}`,
    );
  }
  return value;
}

/** V2 专用校验：catalog 折扣 bps 与 promotion 模式不得共存。 */
function validateCatalogDiscountConflict(
  basisPoints: number,
  discountState: unknown,
): void {
  if (
    basisPoints > 0 &&
    isRecord(discountState) &&
    discountState.mode === "promotion"
  ) {
    throw invalid(
      "catalogDiscountBasisPoints must not coexist with promotion discount state",
    );
  }
}

function stripBasisPointsFromPricing(
  pricingState: Record<string, unknown>,
): Readonly<{ v1PricingState: Record<string, unknown>; basisByLineIndex: number[] }> {
  const lines = pricingState.lines;
  if (!Array.isArray(lines)) {
    throw invalid("pricingState lines must be an array");
  }
  const basisByLineIndex: number[] = [];
  const v1Lines = lines.map((line) => {
    if (!isRecord(line)) {
      throw invalid("cart line must be an object");
    }
    assertOnlyKeys(line, V2_LINE_KEYS, "cart line");
    const basisPoints = normalizeBasisPoints(
      line.catalogDiscountBasisPoints,
      "catalogDiscountBasisPoints",
    );
    validateCatalogDiscountConflict(basisPoints, line.discountState);
    const { catalogDiscountBasisPoints: _bps, ...rest } = line;
    basisByLineIndex.push(basisPoints);
    return rest;
  });
  return {
    v1PricingState: { ...pricingState, lines: v1Lines },
    basisByLineIndex,
  };
}

/** 按版本分派规范化：V1 保持冻结，V2 走 V2 契约。 */
export function normalizeSharedSaleCart(
  input: unknown,
): SharedSaleCartV1 | SharedSaleCartV2 {
  if (!isRecord(input)) {
    throw invalid("shared sale cart must be an object");
  }
  if (input.version === 1) {
    return normalizeSharedSaleCartV1(input);
  }
  if (input.version === 2) {
    return normalizeSharedSaleCartV2(input);
  }
  throw new SharedSaleCartValidationError(
    "SHARED_CART_VERSION_UNSUPPORTED",
    `unsupported shared sale cart version: ${String(input.version)}`,
  );
}

/** V2 规范化：复用 V1 的冻结逐字段校验，然后叠加 catalog baseline。 */
export function normalizeSharedSaleCartV2(input: unknown): SharedSaleCartV2 {
  if (!isRecord(input)) {
    throw invalid("shared sale cart must be an object");
  }
  assertOnlyKeys(input, V2_CART_KEYS, "shared sale cart");
  if (input.version !== 2) {
    throw new SharedSaleCartValidationError(
      "SHARED_CART_VERSION_UNSUPPORTED",
      `unsupported shared sale cart version: ${String(input.version)}`,
    );
  }
  const pricing = input.pricingState;
  if (!isRecord(pricing)) {
    throw invalid("pricingState must be an object");
  }

  const { v1PricingState, basisByLineIndex } =
    stripBasisPointsFromPricing(pricing);
  const v1 = normalizeSharedSaleCartV1({
    version: 1,
    pricingState: v1PricingState,
  });

  return deepFreeze({
    version: 2,
    pricingState: {
      ...v1.pricingState,
      lines: v1.pricingState.lines.map((line, index) => ({
        ...line,
        catalogDiscountBasisPoints: basisByLineIndex[index] ?? 0,
      })),
    },
  }) as SharedSaleCartV2;
}

export function hasCatalogBaseline(cart: SharedSaleCartV2): boolean {
  return cart.pricingState.lines.some(
    (line) => line.catalogDiscountBasisPoints > 0,
  );
}

/**
 * 版本敏感的 canonical 指纹：即使 V1 与无 baseline 的 V2 字段值等价，
 * 版本也必须保留在指纹中，避免幂等事实跨 wire 契约误合并。
 */
export function sharedSaleCartFingerprint(input: SharedSaleCartPayload): string {
  return JSON.stringify(normalizeSharedSaleCart(input));
}

export function sameSharedSaleCart(
  left: SharedSaleCartPayload,
  right: SharedSaleCartPayload,
): boolean {
  return sharedSaleCartFingerprint(left) === sharedSaleCartFingerprint(right);
}

/** V1 -> V2：无 catalog baseline 的完整恢复。 */
export function v1ToV2(cart: SharedSaleCartV1): SharedSaleCartV2 {
  const v1 = normalizeSharedSaleCartV1(cart);
  return deepFreeze({
    version: 2,
    pricingState: {
      ...v1.pricingState,
      lines: v1.pricingState.lines.map((line) => ({
        ...line,
        catalogDiscountBasisPoints: 0,
      })),
    },
  }) as SharedSaleCartV2;
}

/** V2 -> V1：有 catalog baseline 时为有损降级，必须拒绝。 */
export function v2ToV1(cart: SharedSaleCartV2): SharedSaleCartV1 {
  const v2 = normalizeSharedSaleCartV2(cart);
  if (hasCatalogBaseline(v2)) {
    throw invalid("cannot downgrade V2 cart with catalog baseline to V1");
  }
  const pricing = v2.pricingState;
  return normalizeSharedSaleCartV1({
    version: 1,
    pricingState: {
      ...pricing,
      lines: pricing.lines.map(
        ({ catalogDiscountBasisPoints: _bps, ...line }) => line,
      ),
    },
  });
}
/**
 * 显式映射器：复用 V1 的结构转换，再补充 line 级 catalogDiscountBasisPoints。
 * PricingCartStateSnapshot 对旧快照保留可选字段；缺失时按 0 写入 V2，
 * 使新 V2 wire 始终显式表达 catalog baseline。
 */
export function toSharedSaleCartV2(
  snapshot: Readonly<PricingCartStateSnapshot>,
): SharedSaleCartV2 {
  // V1 映射器会拒绝有损降级；V2 只借用其余冻结字段的结构转换，
  // 因此先显式清零临时副本中的 catalog baseline，再逐行补回 V2 字段。
  const v1 = toSharedSaleCartV1({
    ...snapshot,
    lines: snapshot.lines.map((line) => ({
      ...line,
      catalogDiscountBasisPoints: 0,
    })),
  });
  return normalizeSharedSaleCartV2({
    version: 2,
    pricingState: {
      ...v1.pricingState,
      lines: v1.pricingState.lines.map((line, index) => {
        const rawLine = snapshot.lines[index] as
          | { catalogDiscountBasisPoints?: unknown }
          | undefined;
        const catalogDiscountBasisPoints =
          rawLine?.catalogDiscountBasisPoints === undefined
            ? 0
            : rawLine.catalogDiscountBasisPoints;
        return {
          ...line,
          catalogDiscountBasisPoints,
        };
      }),
    },
  });
}
