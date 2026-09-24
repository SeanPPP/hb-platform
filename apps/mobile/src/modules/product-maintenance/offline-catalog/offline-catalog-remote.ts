/**
 * 离线目录远端适配器（参考 apps/pos-ipad/src/features/catalog/hbpos-catalog-remote.ts）。
 *
 * 职责：调用后端 offline-catalog 三个接口，校验响应形状、租约回显、版本一致性与页校验和。
 * 传输层通过 `OfflineCatalogTransport` 注入：生产用 apiClient（见 api.ts），测试用内存实现。
 * 任何校验失败都抛 `OfflineCatalogError`（code 以 OFFLINE_CATALOG_ 开头）。
 */
import {
  calculateOfflineCatalogDeltaChecksum,
  calculateOfflineCatalogPageChecksum,
  expoSha256Digest,
  type OfflineCatalogDigest,
} from "./offline-catalog-checksum";
import {
  OFFLINE_CATALOG_MATCH_SOURCES,
  OfflineCatalogError,
  type OfflineCatalogDeletedItem,
  type OfflineCatalogDeltaPage,
  type OfflineCatalogItem,
  type OfflineCatalogMatchSource,
  type OfflineCatalogPage,
  type OfflineCatalogSyncPlan,
} from "./types";

export interface OfflineCatalogTransport {
  get<T>(path: string, params: Record<string, string | number | undefined>, signal?: AbortSignal): Promise<T>;
}

export interface OfflineCatalogRemote {
  getSyncPlan(input: { storeCode: string; baseCatalogVersion: string | null; signal?: AbortSignal }): Promise<OfflineCatalogSyncPlan>;
  getPage(input: {
    storeCode: string;
    cursor: string | null;
    pageSize: number;
    catalogVersion?: string;
    downloadLeaseId?: string | null;
    signal?: AbortSignal;
  }): Promise<OfflineCatalogPage>;
  getDeltaPage(input: {
    storeCode: string;
    baseCatalogVersion: string;
    targetCatalogVersion: string;
    cursor: string | null;
    pageSize: number;
    downloadLeaseId?: string | null;
    signal?: AbortSignal;
  }): Promise<OfflineCatalogDeltaPage>;
}

export const OFFLINE_CATALOG_API_BASE = "/react/v1/store-product-maintenance/offline-catalog";

export function createOfflineCatalogRemote(
  transport: OfflineCatalogTransport,
  digest: OfflineCatalogDigest = expoSha256Digest,
): OfflineCatalogRemote {
  return {
    async getSyncPlan(input) {
      const raw = await transport.get<RawSyncPlan>(
        `${OFFLINE_CATALOG_API_BASE}/sync-plan`,
        {
          storeCode: input.storeCode,
          baseCatalogVersion: input.baseCatalogVersion ?? undefined,
        },
        input.signal,
      );
      return normalizeSyncPlan(raw, input);
    },
    async getPage(input) {
      const raw = await transport.get<RawPage>(
        `${OFFLINE_CATALOG_API_BASE}/page`,
        {
          storeCode: input.storeCode,
          cursor: input.cursor ?? undefined,
          pageSize: input.pageSize,
          catalogVersion: input.catalogVersion,
          downloadLeaseId: input.downloadLeaseId ?? undefined,
        },
        input.signal,
      );
      verifyLeaseEcho(input.downloadLeaseId, raw?.downloadLeaseId);
      const page = normalizePage(raw);
      const expected = await calculateOfflineCatalogPageChecksum(page.items, digest);
      if (page.pageChecksum.toLowerCase() !== expected) {
        throw new OfflineCatalogError("Offline catalog page checksum mismatch.", "OFFLINE_CATALOG_PAGE_CHECKSUM_MISMATCH");
      }
      return page;
    },
    async getDeltaPage(input) {
      const raw = await transport.get<RawDeltaPage>(
        `${OFFLINE_CATALOG_API_BASE}/delta/page`,
        {
          storeCode: input.storeCode,
          baseCatalogVersion: input.baseCatalogVersion,
          targetCatalogVersion: input.targetCatalogVersion,
          cursor: input.cursor ?? undefined,
          pageSize: input.pageSize,
          downloadLeaseId: input.downloadLeaseId ?? undefined,
        },
        input.signal,
      );
      verifyLeaseEcho(input.downloadLeaseId, raw?.downloadLeaseId);
      const page = normalizeDeltaPage(raw);
      if (page.baseCatalogVersion !== input.baseCatalogVersion || page.targetCatalogVersion !== input.targetCatalogVersion) {
        throw invalid("delta.version");
      }
      const expected = await calculateOfflineCatalogDeltaChecksum(
        {
          baseCatalogVersion: page.baseCatalogVersion,
          targetCatalogVersion: page.targetCatalogVersion,
          items: page.items,
          deletedItems: page.deletedItems,
        },
        digest,
      );
      if (page.pageChecksum.toLowerCase() !== expected) {
        throw new OfflineCatalogError("Offline catalog delta checksum mismatch.", "OFFLINE_CATALOG_DELTA_CHECKSUM_MISMATCH");
      }
      return page;
    },
  };
}

type RawSyncPlan = Record<string, unknown> | null | undefined;
type RawPage = Record<string, unknown> | null | undefined;
type RawDeltaPage = Record<string, unknown> | null | undefined;

function invalid(field: string): OfflineCatalogError {
  return new OfflineCatalogError(`Offline catalog response field is invalid: ${field}.`, "OFFLINE_CATALOG_RESPONSE_INVALID");
}

function verifyLeaseEcho(requested: string | null | undefined, echoed: unknown): void {
  if (!requested || echoed === requested) {
    return;
  }
  throw new OfflineCatalogError("Offline catalog download lease echo mismatch.", "OFFLINE_CATALOG_LEASE_MISMATCH");
}

/** 后端 DTO 属性可能是 PascalCase 或 camelCase；统一按两种键读取。 */
function pick(source: Record<string, unknown>, key: string): unknown {
  if (key in source) {
    return source[key];
  }
  const pascal = key.charAt(0).toUpperCase() + key.slice(1);
  return source[pascal];
}

function record(value: unknown, field: string): Record<string, unknown> {
  if (!value || typeof value !== "object") {
    throw invalid(field);
  }
  return value as Record<string, unknown>;
}

function requiredText(value: unknown, field: string): string {
  if (typeof value !== "string" || value.length === 0) {
    throw invalid(field);
  }
  return value;
}

function optionalText(value: unknown, field: string): string | null {
  if (value === null || value === undefined || value === "") {
    return null;
  }
  return requiredText(value, field);
}

function requiredNonNegativeInteger(value: unknown, field: string): number {
  if (!Number.isSafeInteger(value) || Number(value) < 0) {
    throw invalid(field);
  }
  return Number(value);
}

function optionalNonNegativeInteger(value: unknown, field: string): number | null {
  return value === null || value === undefined ? null : requiredNonNegativeInteger(value, field);
}

function requiredBoolean(value: unknown, field: string): boolean {
  if (typeof value !== "boolean") {
    throw invalid(field);
  }
  return value;
}

function optionalBoolean(value: unknown, field: string): boolean | null {
  return value === null || value === undefined ? null : requiredBoolean(value, field);
}

function optionalFiniteNumber(value: unknown, field: string): number | null {
  if (value === null || value === undefined) {
    return null;
  }
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw invalid(field);
  }
  return value;
}

function requiredTimestamp(value: unknown, field: string): string {
  const text = requiredText(value, field);
  const parsed = new Date(text);
  if (Number.isNaN(parsed.valueOf())) {
    throw invalid(field);
  }
  return parsed.toISOString();
}

function optionalTimestamp(value: unknown, field: string): string | null {
  const text = optionalText(value, field);
  return text === null ? null : requiredTimestamp(text, field);
}

function requiredArray(value: unknown, field: string): readonly unknown[] {
  if (!Array.isArray(value)) {
    throw invalid(field);
  }
  return value;
}

function requiredMatchSource(value: unknown): OfflineCatalogMatchSource {
  if (typeof value === "string" && (OFFLINE_CATALOG_MATCH_SOURCES as readonly string[]).includes(value)) {
    return value as OfflineCatalogMatchSource;
  }
  throw invalid("item.matchSource");
}

export function normalizeOfflineCatalogItem(value: unknown): OfflineCatalogItem {
  const source = record(value, "item");
  const text = (key: string) => optionalText(pick(source, key), `item.${key}`);
  const num = (key: string) => optionalFiniteNumber(pick(source, key), `item.${key}`);
  const bool = (key: string) => optionalBoolean(pick(source, key), `item.${key}`);
  return {
    storeCode: requiredText(pick(source, "storeCode"), "item.storeCode"),
    lookupKey: requiredText(pick(source, "lookupKey"), "item.lookupKey"),
    lookupCode: requiredText(pick(source, "lookupCode"), "item.lookupCode"),
    lookupCodeNormalized: requiredText(pick(source, "lookupCodeNormalized"), "item.lookupCodeNormalized"),
    matchSource: requiredMatchSource(pick(source, "matchSource")),
    productCode: requiredText(pick(source, "productCode"), "item.productCode"),
    productName: typeof pick(source, "productName") === "string" ? (pick(source, "productName") as string) : "",
    itemNumber: text("itemNumber"),
    barcode: text("barcode"),
    productImage: text("productImage"),
    productType: num("productType"),
    grade: text("grade"),
    localSupplierCode: text("localSupplierCode"),
    localSupplierName: text("localSupplierName"),
    storeName: text("storeName"),
    storePriceUuid: text("storePriceUuid"),
    purchasePrice: num("purchasePrice"),
    retailPrice: num("retailPrice"),
    discountRate: num("discountRate"),
    isAutoPricing: requiredBoolean(pick(source, "isAutoPricing") ?? false, "item.isAutoPricing"),
    isSpecialProduct: requiredBoolean(pick(source, "isSpecialProduct") ?? false, "item.isSpecialProduct"),
    rate: num("rate"),
    strategySourceLabel: text("strategySourceLabel"),
    strategyRuleLabel: text("strategyRuleLabel"),
    clearanceUuid: text("clearanceUuid"),
    clearanceBarcode: text("clearanceBarcode"),
    clearancePrice: num("clearancePrice"),
    codeId: text("codeId"),
    codeUuid: text("codeUuid"),
    codeProductCode: text("codeProductCode"),
    codeItemNumber: text("codeItemNumber"),
    codeRetailPrice: num("codeRetailPrice"),
    codePurchasePrice: num("codePurchasePrice"),
    codeQuantity: num("codeQuantity"),
    codeType: num("codeType"),
    codeDiscountRate: num("codeDiscountRate"),
    codeIsAutoPricing: bool("codeIsAutoPricing"),
    codeIsSpecialProduct: bool("codeIsSpecialProduct"),
    codeIsActive: bool("codeIsActive"),
    updatedAt: optionalTimestamp(pick(source, "updatedAt"), "item.updatedAt"),
    rowVersion: requiredText(pick(source, "rowVersion"), "item.rowVersion"),
  };
}

function normalizeDeletedItem(value: unknown): OfflineCatalogDeletedItem {
  const source = record(value, "deletedItem");
  return {
    storeCode: requiredText(pick(source, "storeCode"), "deletedItem.storeCode"),
    lookupKey: requiredText(pick(source, "lookupKey"), "deletedItem.lookupKey"),
    deletedAt: optionalTimestamp(pick(source, "deletedAt"), "deletedItem.deletedAt"),
  };
}

function normalizeSyncPlan(
  raw: RawSyncPlan,
  requested: { storeCode: string; baseCatalogVersion: string | null },
): OfflineCatalogSyncPlan {
  const source = record(raw, "syncPlan");
  const mode = pick(source, "mode");
  if (mode !== "full" && mode !== "delta" && mode !== "noChange") {
    throw invalid("syncPlan.mode");
  }
  const storeCode = requiredText(pick(source, "storeCode"), "syncPlan.storeCode");
  if (storeCode !== requested.storeCode) {
    throw invalid("syncPlan.storeCode");
  }
  const baseCatalogVersion = optionalText(pick(source, "baseCatalogVersion"), "syncPlan.baseCatalogVersion");
  if (baseCatalogVersion !== requested.baseCatalogVersion) {
    throw invalid("syncPlan.baseCatalogVersion");
  }
  return {
    storeCode,
    generatedAt: requiredTimestamp(pick(source, "generatedAt"), "syncPlan.generatedAt"),
    mode,
    baseCatalogVersion,
    targetCatalogVersion: requiredText(pick(source, "targetCatalogVersion"), "syncPlan.targetCatalogVersion"),
    targetTotal: requiredNonNegativeInteger(pick(source, "targetTotal"), "syncPlan.targetTotal"),
    downloadLeaseId: optionalText(pick(source, "downloadLeaseId"), "syncPlan.downloadLeaseId"),
    deltaOperationCount: optionalNonNegativeInteger(pick(source, "deltaOperationCount"), "syncPlan.deltaOperationCount"),
  };
}

function normalizePage(raw: RawPage): OfflineCatalogPage {
  const source = record(raw, "page");
  return {
    storeCode: requiredText(pick(source, "storeCode"), "page.storeCode"),
    generatedAt: requiredTimestamp(pick(source, "generatedAt"), "page.generatedAt"),
    cursor: optionalText(pick(source, "cursor"), "page.cursor"),
    items: requiredArray(pick(source, "items"), "page.items").map(normalizeOfflineCatalogItem),
    nextCursor: optionalText(pick(source, "nextCursor"), "page.nextCursor"),
    hasMore: requiredBoolean(pick(source, "hasMore"), "page.hasMore"),
    totalCount: requiredNonNegativeInteger(pick(source, "totalCount"), "page.totalCount"),
    catalogVersion: requiredText(pick(source, "catalogVersion"), "page.catalogVersion"),
    pageChecksum: requiredText(pick(source, "pageChecksum"), "page.pageChecksum"),
  };
}

function normalizeDeltaPage(raw: RawDeltaPage): OfflineCatalogDeltaPage {
  const source = record(raw, "delta");
  return {
    storeCode: requiredText(pick(source, "storeCode"), "delta.storeCode"),
    generatedAt: requiredTimestamp(pick(source, "generatedAt"), "delta.generatedAt"),
    baseCatalogVersion: requiredText(pick(source, "baseCatalogVersion"), "delta.baseCatalogVersion"),
    targetCatalogVersion: requiredText(pick(source, "targetCatalogVersion"), "delta.targetCatalogVersion"),
    cursor: optionalText(pick(source, "cursor"), "delta.cursor"),
    items: requiredArray(pick(source, "items"), "delta.items").map(normalizeOfflineCatalogItem),
    deletedItems: requiredArray(pick(source, "deletedItems"), "delta.deletedItems").map(normalizeDeletedItem),
    nextCursor: optionalText(pick(source, "nextCursor"), "delta.nextCursor"),
    hasMore: requiredBoolean(pick(source, "hasMore"), "delta.hasMore"),
    targetTotal: requiredNonNegativeInteger(pick(source, "targetTotal"), "delta.targetTotal"),
    pageChecksum: requiredText(pick(source, "pageChecksum"), "delta.pageChecksum"),
  };
}
