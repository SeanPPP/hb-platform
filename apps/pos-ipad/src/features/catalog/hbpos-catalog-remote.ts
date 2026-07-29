import type { CatalogPromotion } from "./catalog-snapshot-service";

import {
  HbposApiError,
  unwrapHbposEnvelope,
  type HbposEnvelope,
  type HbposTransport,
} from "@/core/api/hbpos-api";
import type { components } from "@/generated/hbpos/schema";

const CHECKSUM_SPECS = {
  1: {
    marker: "HBPOS-CATALOG-PAGE-CHECKSUM-V1",
    prefix: "sha256-catalog-page-v1:",
  },
  2: {
    marker: "HBPOS-CATALOG-PAGE-CHECKSUM-V2",
    prefix: "sha256-catalog-page-v2:",
  },
} as const;

export type CatalogPageChecksumVersion = keyof typeof CHECKSUM_SPECS;

type GeneratedCatalogPage = components["schemas"]["CatalogSyncPageResponse"] & {
  catalogVersion?: string | null;
  pageChecksum?: string | null;
};
type GeneratedCatalogItem = components["schemas"]["CatalogLookupItemDto"];
type GeneratedDeletedLookup = components["schemas"]["DeletedLookupDto"];
type GeneratedCatalogPromotions = components["schemas"]["CatalogPromotionsResponse"];
type GeneratedCatalogPromotion = components["schemas"]["CatalogPromotionRuleDto"];
type GeneratedCatalogPromotionProduct = components["schemas"]["CatalogPromotionProductDto"];

export type CatalogLookupItem = Readonly<{
  storeCode: string;
  productCode: string;
  referenceCode: string | null;
  displayName: string;
  lookupCode: string;
  lookupCodeNormalized: string;
  itemNumber: string | null;
  barcode: string | null;
  retailPrice: number;
  priceSource: components["schemas"]["PriceSourceKind"];
  priceSourceLabel: string;
  quantityFactor: number;
  updatedAt: string | null;
  rowVersion: string | null;
  productImage: string | null;
  discountRate: number | null;
  isSpecialProduct: boolean;
}>;

export type CatalogDeletedLookup = Readonly<{
  storeCode: string;
  lookupCode: string;
  lookupCodeNormalized: string;
  deletedAt: string | null;
}>;

export type VerifiedCatalogSyncPage = Readonly<{
  storeCode: string;
  generatedAt: string;
  cursor: string | null;
  items: readonly CatalogLookupItem[];
  deletedLookups: readonly CatalogDeletedLookup[];
  nextCursor: string | null;
  hasMore: boolean;
  totalCount: number;
  catalogVersion: string;
  pageChecksum: string;
}>;

export type CatalogPageDigest = (canonicalPayload: string) => Promise<string>;

export class HbposCatalogPageApi {
  public constructor(
    private readonly transport: HbposTransport,
    private readonly digest: CatalogPageDigest = expoSha256,
  ) {}

  public async getPage(input: Readonly<{
    storeCode: string;
    cursor: string | null;
    pageSize: number;
    catalogVersion?: string;
    signal?: AbortSignal;
  }>): Promise<VerifiedCatalogSyncPage> {
    const response = await this.transport.request<HbposEnvelope<GeneratedCatalogPage>>({
      method: "GET",
      url: "/api/v1/catalog/sellable-items/page",
      params: {
        storeCode: input.storeCode,
        cursor: input.cursor ?? undefined,
        pageSize: input.pageSize,
        catalogVersion: input.catalogVersion ?? undefined,
        checksumVersion: 2,
      },
      // WPF 目录下载同样不设隐藏固定超时；页面生命周期信号负责主动取消。
      timeoutMs: 0,
      ...(input.signal ? { signal: input.signal } : {}),
    });
    const page = normalizePage(unwrapHbposEnvelope(response.data));
    const calculatedChecksum = await calculateCatalogPageChecksum(
      page.items,
      this.digest,
      2,
    );
    if (page.pageChecksum.toLowerCase() !== calculatedChecksum) {
      throw new HbposApiError("Catalog page checksum verification failed.", {
        kind: "envelope",
        code: "CATALOG_PAGE_CHECKSUM_MISMATCH",
      });
    }
    return page;
  }

  /**
   * 促销定义会直接影响离线金额，故只接受生成合同中的白名单字段。新字段必须经
   * 人工审查后才能进入 definitionJson，避免后端扩展被客户端静默解释为价格规则。
   */
  public async getPromotions(input: Readonly<{
    storeCode: string;
    signal?: AbortSignal;
  }>): Promise<readonly CatalogPromotion[]> {
    const requestedStoreCode = requiredPromotionText(input.storeCode, "requested storeCode");
    const response = await this.transport.request<HbposEnvelope<GeneratedCatalogPromotions>>({
      method: "GET",
      url: "/api/v1/catalog/promotions",
      params: { storeCode: requestedStoreCode },
      timeoutMs: 0,
      ...(input.signal ? { signal: input.signal } : {}),
    });
    return normalizePromotions(
      unwrapHbposEnvelope(response.data),
      requestedStoreCode,
    );
  }
}

export async function calculateCatalogPageChecksum(
  items: readonly CatalogLookupItem[],
  digest: CatalogPageDigest = expoSha256,
  version: CatalogPageChecksumVersion = 1,
): Promise<string> {
  const spec = CHECKSUM_SPECS[version];
  const canonical = buildCanonicalPage(items, version);
  const hex = (await digest(canonical)).trim().toLowerCase();
  if (!/^[0-9a-f]{64}$/.test(hex)) {
    throw new HbposApiError("Catalog page digest returned an invalid SHA256 value.", {
      kind: "envelope",
      code: "CATALOG_PAGE_DIGEST_INVALID",
    });
  }
  return `${spec.prefix}${hex}`;
}

function normalizePage(page: GeneratedCatalogPage): VerifiedCatalogSyncPage {
  const items = requiredArray(page.items, "items").map(normalizeItem);
  return {
    storeCode: requiredText(page.storeCode, "storeCode"),
    generatedAt: requiredTimestamp(page.generatedAt, "generatedAt"),
    cursor: optionalText(page.cursor, "cursor"),
    items,
    deletedLookups: requiredArray(page.deletedLookups, "deletedLookups").map(normalizeDeletedLookup),
    nextCursor: optionalText(page.nextCursor, "nextCursor"),
    hasMore: requiredBoolean(page.hasMore, "hasMore"),
    totalCount: requiredNonNegativeInteger(page.totalCount, "totalCount"),
    catalogVersion: requiredText(page.catalogVersion, "catalogVersion"),
    pageChecksum: requiredText(page.pageChecksum, "pageChecksum"),
  };
}

function normalizePromotions(
  response: GeneratedCatalogPromotions,
  requestedStoreCode: string,
): readonly CatalogPromotion[] {
  if (requiredPromotionText(response.storeCode, "response storeCode") !== requestedStoreCode) {
    throw invalidPromotion("response storeCode");
  }
  // generatedAt 不入库，但必须可解析，防止接受半截或错版本的服务端响应。
  requiredPromotionTimestamp(response.generatedAt, "generatedAt");

  const identifiers = new Set<string>();
  return requiredPromotionArray(response.promotions, "promotions").map((promotion) => {
    const normalized = normalizePromotion(promotion);
    if (identifiers.has(normalized.promotionId)) {
      throw invalidPromotion("duplicate promotionId");
    }
    identifiers.add(normalized.promotionId);
    return normalized;
  });
}

function normalizePromotion(source: GeneratedCatalogPromotion): CatalogPromotion {
  const promotionId = requiredPromotionText(source.promotionId, "promotionId");
  const effectiveStart = requiredPromotionTimestamp(source.effectiveStart, "effectiveStart");
  const effectiveEnd = requiredPromotionTimestamp(source.effectiveEnd, "effectiveEnd");
  if (Date.parse(effectiveStart) > Date.parse(effectiveEnd)) {
    throw invalidPromotion("effective range");
  }

  const products = requiredPromotionArray(source.products, "products").map(
    normalizePromotionProduct,
  );
  if (products.length === 0) {
    throw invalidPromotion("products");
  }
  const productCodes = new Set<string>();
  for (const product of products) {
    if (productCodes.has(product.productCode)) {
      throw invalidPromotion("duplicate productCode");
    }
    productCodes.add(product.productCode);
  }

  const definition = {
    promotionId,
    name: requiredPromotionText(source.name, "name"),
    isExclusive: requiredPromotionBoolean(source.isExclusive, "isExclusive"),
    priority: requiredPromotionInteger(source.priority, "priority"),
    applyQuantity: requiredPromotionPositiveInteger(source.applyQuantity, "applyQuantity"),
    fixedPrice: requiredPromotionAmount(source.fixedPrice, "fixedPrice"),
    maxApplicationsPerOrder: optionalPromotionNonNegativeInteger(
      source.maxApplicationsPerOrder,
      "maxApplicationsPerOrder",
    ),
    effectiveStart,
    effectiveEnd,
    updatedAt: optionalPromotionTimestamp(source.updatedAt, "updatedAt"),
    products,
  };
  return {
    promotionId,
    // 属性插入顺序固定；这样同一服务端定义在所有 iPad 上产生完全相同的 JSON。
    definitionJson: JSON.stringify(definition),
    validFromIso: effectiveStart,
    validUntilIso: effectiveEnd,
    priority: definition.priority,
  };
}

function normalizePromotionProduct(
  source: GeneratedCatalogPromotionProduct,
): Readonly<{ productCode: string; unitWeight: number }> {
  return {
    productCode: requiredPromotionText(source.productCode, "product.productCode"),
    unitWeight: requiredPromotionPositiveInteger(source.unitWeight, "product.unitWeight"),
  };
}

function normalizeItem(item: GeneratedCatalogItem): CatalogLookupItem {
  return {
    storeCode: requiredText(item.storeCode, "item.storeCode"),
    productCode: requiredText(item.productCode, "item.productCode"),
    referenceCode: optionalText(item.referenceCode, "item.referenceCode"),
    displayName: requiredText(item.displayName, "item.displayName"),
    lookupCode: requiredText(item.lookupCode, "item.lookupCode"),
    lookupCodeNormalized: requiredText(item.lookupCodeNormalized, "item.lookupCodeNormalized"),
    itemNumber: optionalText(item.itemNumber, "item.itemNumber"),
    barcode: optionalText(item.barcode, "item.barcode"),
    retailPrice: requiredFiniteNumber(item.retailPrice, "item.retailPrice"),
    priceSource: requiredPriceSource(item.priceSource),
    priceSourceLabel: requiredText(item.priceSourceLabel, "item.priceSourceLabel"),
    quantityFactor: requiredFiniteNumber(item.quantityFactor, "item.quantityFactor"),
    updatedAt: optionalTimestamp(item.updatedAt, "item.updatedAt"),
    rowVersion: optionalText(item.rowVersion, "item.rowVersion"),
    productImage: optionalText(item.productImage, "item.productImage"),
    discountRate: optionalFiniteNumber(item.discountRate, "item.discountRate"),
    isSpecialProduct: requiredBoolean(item.isSpecialProduct, "item.isSpecialProduct"),
  };
}

function normalizeDeletedLookup(item: GeneratedDeletedLookup): CatalogDeletedLookup {
  return {
    storeCode: requiredText(item.storeCode, "deletedLookup.storeCode"),
    lookupCode: requiredText(item.lookupCode, "deletedLookup.lookupCode"),
    lookupCodeNormalized: requiredText(
      item.lookupCodeNormalized,
      "deletedLookup.lookupCodeNormalized",
    ),
    deletedAt: optionalTimestamp(item.deletedAt, "deletedLookup.deletedAt"),
  };
}

function buildCanonicalPage(
  items: readonly CatalogLookupItem[],
  version: CatalogPageChecksumVersion,
): string {
  const number = version === 1
    ? formatCanonicalNumberV1
    : formatCanonicalNumberV2;
  const values = [
    CHECKSUM_SPECS[version].marker,
    number(items.length),
  ];
  for (const item of items) {
    values.push(
      item.storeCode,
      item.productCode,
      item.referenceCode ?? "",
      item.displayName,
      item.lookupCode,
      item.lookupCodeNormalized,
      item.itemNumber ?? "",
      item.barcode ?? "",
      number(item.retailPrice),
      number(item.priceSource),
      item.priceSourceLabel,
      number(item.quantityFactor),
      item.updatedAt ?? "",
      item.productImage ?? "",
      item.discountRate === null ? "" : number(item.discountRate),
      item.isSpecialProduct ? "1" : "0",
    );
  }
  return values.map((value) => `${value.length}:${value}|`).join("");
}

async function expoSha256(payload: string): Promise<string> {
  // 延迟加载避免 Node 合同测试解析 React Native 入口；真机仍使用 Expo 原生 SHA256。
  const Crypto = await import("expo-crypto");
  return Crypto.digestStringAsync(Crypto.CryptoDigestAlgorithm.SHA256, payload);
}

function formatCanonicalNumberV1(value: number): string {
  if (!Number.isFinite(value)) {
    throw new HbposApiError("Catalog checksum cannot encode a non-finite number.", {
      kind: "envelope",
      code: "CATALOG_PAGE_VALUE_INVALID",
    });
  }
  if (Object.is(value, -0)) {
    return "0";
  }

  const text = String(value).toLowerCase();
  if (!text.includes("e")) {
    return text;
  }

  const [mantissa = "", exponentText = "0"] = text.split("e");
  const exponent = Number(exponentText);
  const negative = mantissa.startsWith("-");
  const unsignedMantissa = negative ? mantissa.slice(1) : mantissa;
  const [whole = "", fraction = ""] = unsignedMantissa.split(".");
  const digits = `${whole}${fraction}`;
  const decimalIndex = whole.length + exponent;
  const expanded = decimalIndex <= 0
    ? `0.${"0".repeat(-decimalIndex)}${digits}`
    : decimalIndex >= digits.length
      ? `${digits}${"0".repeat(decimalIndex - digits.length)}`
      : `${digits.slice(0, decimalIndex)}.${digits.slice(decimalIndex)}`;
  return negative ? `-${expanded}` : expanded;
}

function formatCanonicalNumberV2(value: number): string {
  if (!Number.isFinite(value)) {
    throw new HbposApiError("Catalog checksum cannot encode a non-finite number.", {
      kind: "envelope",
      code: "CATALOG_PAGE_VALUE_INVALID",
    });
  }
  const buffer = new ArrayBuffer(8);
  const view = new DataView(buffer);
  // 中文注释：服务端 decimal 没有负零；客户端也统一为正零后再编码。
  view.setFloat64(0, Object.is(value, -0) ? 0 : value, false);
  return Array.from(
    new Uint8Array(buffer),
    (byte) => byte.toString(16).padStart(2, "0"),
  ).join("");
}

function requiredText(value: unknown, field: string): string {
  if (typeof value !== "string" || value.length === 0) {
    throw invalidPage(field);
  }
  return value;
}

function optionalText(value: unknown, field: string): string | null {
  if (value === null || value === undefined || value === "") {
    return null;
  }
  return requiredText(value, field);
}

function requiredTimestamp(value: unknown, field: string): string {
  const timestamp = optionalTimestamp(value, field);
  if (timestamp === null) {
    throw invalidPage(field);
  }
  return timestamp;
}

function optionalTimestamp(value: unknown, field: string): string | null {
  const text = optionalText(value, field);
  if (text === null) {
    return null;
  }
  const parsed = new Date(text);
  if (Number.isNaN(parsed.valueOf())) {
    throw invalidPage(field);
  }
  return parsed.toISOString();
}

function requiredFiniteNumber(value: unknown, field: string): number {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw invalidPage(field);
  }
  return value;
}

function optionalFiniteNumber(value: unknown, field: string): number | null {
  if (value === null || value === undefined) {
    return null;
  }
  return requiredFiniteNumber(value, field);
}

function requiredBoolean(value: unknown, field: string): boolean {
  if (typeof value !== "boolean") {
    throw invalidPage(field);
  }
  return value;
}

function requiredNonNegativeInteger(value: unknown, field: string): number {
  if (!Number.isSafeInteger(value) || Number(value) < 0) {
    throw invalidPage(field);
  }
  return Number(value);
}

function requiredArray<T>(value: readonly T[] | null | undefined, field: string): readonly T[] {
  if (!Array.isArray(value)) {
    throw invalidPage(field);
  }
  return value;
}

function requiredPriceSource(value: unknown): components["schemas"]["PriceSourceKind"] {
  if (value !== 0 && value !== 1 && value !== 2 && value !== 3 && value !== 4) {
    throw invalidPage("item.priceSource");
  }
  return value;
}

function requiredPromotionText(value: unknown, field: string): string {
  if (typeof value !== "string" || value.trim().length === 0) {
    throw invalidPromotion(field);
  }
  return value.trim();
}

function requiredPromotionTimestamp(value: unknown, field: string): string {
  const timestamp = optionalPromotionTimestamp(value, field);
  if (timestamp === null) {
    throw invalidPromotion(field);
  }
  return timestamp;
}

function optionalPromotionTimestamp(value: unknown, field: string): string | null {
  if (value === null || value === undefined || value === "") {
    return null;
  }
  const text = requiredPromotionText(value, field);
  const parsed = Date.parse(text);
  if (!Number.isFinite(parsed)) {
    throw invalidPromotion(field);
  }
  return new Date(parsed).toISOString();
}

function requiredPromotionBoolean(value: unknown, field: string): boolean {
  if (typeof value !== "boolean") {
    throw invalidPromotion(field);
  }
  return value;
}

function requiredPromotionInteger(value: unknown, field: string): number {
  if (typeof value !== "number" || !Number.isSafeInteger(value)) {
    throw invalidPromotion(field);
  }
  return value;
}

function requiredPromotionPositiveInteger(value: unknown, field: string): number {
  const number = requiredPromotionInteger(value, field);
  if (number <= 0) {
    throw invalidPromotion(field);
  }
  return number;
}

function optionalPromotionNonNegativeInteger(value: unknown, field: string): number | null {
  if (value === null || value === undefined) {
    return null;
  }
  const number = requiredPromotionInteger(value, field);
  if (number < 0) {
    throw invalidPromotion(field);
  }
  return number;
}

function requiredPromotionAmount(value: unknown, field: string): number {
  if (typeof value !== "number" || !Number.isFinite(value) || value < 0) {
    throw invalidPromotion(field);
  }
  const cents = Math.round(value * 100);
  const tolerance = Number.EPSILON * Math.max(1, Math.abs(value * 100)) * 8;
  if (!Number.isSafeInteger(cents) || Math.abs(value * 100 - cents) > tolerance) {
    throw invalidPromotion(field);
  }
  return value;
}

function requiredPromotionArray<T>(
  value: readonly T[] | null | undefined,
  field: string,
): readonly T[] {
  if (!Array.isArray(value)) {
    throw invalidPromotion(field);
  }
  return value;
}

function invalidPage(field: string): HbposApiError {
  return new HbposApiError(`Catalog page field is invalid: ${field}.`, {
    kind: "envelope",
    code: "CATALOG_PAGE_INVALID",
  });
}

function invalidPromotion(field: string): HbposApiError {
  return new HbposApiError(`Catalog promotion field is invalid: ${field}.`, {
    kind: "envelope",
    code: "CATALOG_PROMOTIONS_INVALID",
  });
}
