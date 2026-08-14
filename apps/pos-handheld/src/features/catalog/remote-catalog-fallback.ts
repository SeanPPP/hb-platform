import {
  HbposApiError,
  unwrapHbposEnvelope,
  type HbposEnvelope,
  type HbposTransport,
} from "@/core/api/hbpos-api";
import type { LocalCatalogMatch } from "@/core/db/catalog-repository";
import type { components } from "@/generated/hbpos/schema";

type GeneratedLookupItem = components["schemas"]["CatalogLookupItemDto"];
type GeneratedLookupResponse = components["schemas"]["CatalogLookupResponse"];

/** 销售 runtime 可注入的最小本地目录读取面，避免 catalog feature 反向依赖销售实现。 */
export interface LocalCatalogReadPort {
  findExact(lookupCode: string): Promise<LocalCatalogMatch | null>;
  searchByName(
    query: string,
    limit: number,
    offset?: number,
  ): Promise<readonly LocalCatalogMatch[]>;
}

export type CatalogRemoteLookupResult = Readonly<{
  storeCode: string;
  lookupCode: string;
  lookupCodeNormalized: string;
  found: boolean;
  item: CatalogRemoteLookupItem | null;
}>;

export type CatalogRemoteLookupItem = Readonly<{
  storeCode: string;
  productCode: string;
  referenceCode: string | null;
  displayName: string;
  lookupCode: string;
  lookupCodeNormalized: string;
  itemNumber: string | null;
  barcode: string | null;
  retailPrice: number;
  priceSource: 0 | 1 | 2 | 3 | 4;
  priceSourceLabel: string;
  quantityFactor: number;
  updatedAt: string | null;
  rowVersion: string | null;
  productImage: string | null;
  discountRate: number | null;
  isSpecialProduct: boolean;
}>;

/** 远程回退的 Port；网络状态由组合根提供，feature 不自行猜测连通性。 */
export interface CatalogRemoteLookupPort {
  lookup(input: Readonly<{
    storeCode: string;
    lookupCode: string;
  }>): Promise<CatalogRemoteLookupResult>;
}

/**
 * Hbpos 查询 adapter。404 只接受明确的 LOOKUP_NOT_FOUND；门店不存在等其他 404
 * 仍是服务端拒绝，不能被折叠成“商品不存在”。
 */
export class HbposCatalogLookupApi implements CatalogRemoteLookupPort {
  public constructor(private readonly transport: HbposTransport) {}

  public async lookup(input: Readonly<{
    storeCode: string;
    lookupCode: string;
  }>): Promise<CatalogRemoteLookupResult> {
    const storeCode = requestText(input.storeCode, "storeCode");
    const lookupCode = requestText(input.lookupCode, "lookupCode");
    const lookupCodeNormalized = normalizeLookupCode(lookupCode);
    const response = await this.transport.request<
      HbposEnvelope<GeneratedLookupResponse>
    >({
      method: "GET",
      url: "/api/v1/catalog/sellable-items/lookup",
      params: { storeCode, lookupCode },
      acceptedStatuses: [404],
    });

    if (response.status === 404) {
      if (response.data.errorCode === "LOOKUP_NOT_FOUND") {
        return Object.freeze({
          storeCode,
          lookupCode,
          lookupCodeNormalized,
          found: false,
          item: null,
        });
      }
      throw new HbposApiError(
        response.data.message ?? "Catalog lookup was rejected.",
        {
          kind: "http",
          status: 404,
          ...(response.data.errorCode
            ? { code: response.data.errorCode }
            : {}),
        },
      );
    }

    const payload = unwrapHbposEnvelope(response.data);
    const responseStoreCode = responseText(payload.storeCode, "response.storeCode");
    const responseLookupCode = responseText(
      payload.lookupCode,
      "response.lookupCode",
    );
    const responseLookupNormalized = responseText(
      payload.lookupCodeNormalized,
      "response.lookupCodeNormalized",
    );
    if (
      responseStoreCode !== storeCode ||
      normalizeLookupCode(responseLookupCode) !== lookupCodeNormalized ||
      responseLookupNormalized !== lookupCodeNormalized
    ) {
      throw invalidResponse("response identity");
    }
    if (typeof payload.found !== "boolean") {
      throw invalidResponse("response.found");
    }
    if (!payload.found) {
      if (payload.item !== null && payload.item !== undefined) {
        throw invalidResponse("response.item");
      }
      return Object.freeze({
        storeCode,
        lookupCode: responseLookupCode,
        lookupCodeNormalized,
        found: false,
        item: null,
      });
    }

    if (payload.item === null || payload.item === undefined) {
      throw invalidResponse("response.item");
    }
    const item = normalizeItem(payload.item, {
      storeCode,
      lookupCodeNormalized,
    });
    return Object.freeze({
      storeCode,
      lookupCode: responseLookupCode,
      lookupCodeNormalized,
      found: true,
      item,
    });
  }
}

export type RemoteFallbackLocalCatalogPortOptions = Readonly<{
  storeCode: string;
  remote: CatalogRemoteLookupPort;
  isOnline: () => boolean | Promise<boolean>;
  local?: LocalCatalogReadPort;
}>;

/**
 * WPF 等价的在线精确查询回退：先读 active 本地目录；未命中时才在线查询。
 * 远端结果仅驻留当前进程缓存，不能绕开 SQLCipher 快照迁移或在离线时伪装为在线成功。
 */
export class RemoteFallbackLocalCatalogPort implements LocalCatalogReadPort {
  private readonly storeCode: string;
  private readonly remoteCache = new Map<string, LocalCatalogMatch>();
  private readonly pending = new Map<string, Promise<LocalCatalogMatch | null>>();

  public constructor(
    private readonly options: RemoteFallbackLocalCatalogPortOptions,
  ) {
    this.storeCode = requestText(options.storeCode, "storeCode");
  }

  public async findExact(lookupCode: string): Promise<LocalCatalogMatch | null> {
    const normalized = normalizeLookupCode(requestText(lookupCode, "lookupCode"));
    const localPort = this.options.local;
    if (localPort) {
      try {
        const local = await localPort.findExact(normalized);
        if (local !== null && local.storeCode === this.storeCode) {
          return local;
        }
      } catch {
        // 本地快照暂不可读时，只有明确在线状态才允许走服务端精确查询。
      }
    }
    const cached = this.remoteCache.get(normalized);
    if (cached) return cached;
    if (!(await this.options.isOnline())) return null;

    const pending = this.pending.get(normalized);
    if (pending) return pending;

    const lookup = this.fetchRemote(normalized);
    this.pending.set(normalized, lookup);
    try {
      return await lookup;
    } finally {
      if (this.pending.get(normalized) === lookup) {
        this.pending.delete(normalized);
      }
    }
  }

  public async searchByName(
    query: string,
    limit: number,
    offset?: number,
  ): Promise<readonly LocalCatalogMatch[]> {
    // WPF 远程回退是扫码/精确查询，不把名称搜索悄悄升级为网络请求。
    return this.options.local?.searchByName(query, limit, offset) ?? [];
  }

  private async fetchRemote(
    lookupCodeNormalized: string,
  ): Promise<LocalCatalogMatch | null> {
    try {
      const result = await this.options.remote.lookup({
        storeCode: this.storeCode,
        lookupCode: lookupCodeNormalized,
      });
      if (
        result.storeCode !== this.storeCode ||
        result.lookupCodeNormalized !== lookupCodeNormalized ||
        !result.found ||
        result.item === null ||
        result.item.storeCode !== this.storeCode ||
        result.item.lookupCodeNormalized !== lookupCodeNormalized
      ) {
        return null;
      }
      const item = toLocalCatalogMatch(result.item);
      this.remoteCache.set(lookupCodeNormalized, item);
      return item;
    } catch {
      // WPF 的远端查询异常保留“本地未命中”结果；认证拦截器仍已执行锁定。
      return null;
    }
  }
}

function toLocalCatalogMatch(item: CatalogRemoteLookupItem): LocalCatalogMatch {
  return Object.freeze({
    storeCode: item.storeCode,
    productCode: item.productCode,
    referenceCode: item.referenceCode,
    itemNumber: item.itemNumber,
    displayName: item.displayName,
    barcode: item.barcode,
    lookupCode: item.lookupCode,
    lookupCodeNormalized: item.lookupCodeNormalized,
    retailPriceCents: moneyCents(item.retailPrice, "item.retailPrice"),
    priceSource: item.priceSource,
    priceSourceLabel: item.priceSourceLabel,
    quantityFactor: item.quantityFactor,
    // 当前 CatalogLookupItemDto 不包含税率，保持本地目录同样的 null 表示。
    taxRateBasisPoints: null,
    updatedAtIso: item.updatedAt,
    rowVersion: item.rowVersion,
    productImage: item.productImage,
    discountRate: item.discountRate,
    isSpecialProduct: item.isSpecialProduct,
  });
}

function normalizeItem(
  item: GeneratedLookupItem,
  expected: Readonly<{ storeCode: string; lookupCodeNormalized: string }>,
): CatalogRemoteLookupItem {
  const storeCode = responseText(item.storeCode, "item.storeCode");
  const lookupCode = responseText(item.lookupCode, "item.lookupCode");
  const lookupCodeNormalized = responseText(
    item.lookupCodeNormalized,
    "item.lookupCodeNormalized",
  );
  if (
    storeCode !== expected.storeCode ||
    lookupCodeNormalized !== expected.lookupCodeNormalized ||
    normalizeLookupCode(lookupCode) !== lookupCodeNormalized
  ) {
    throw invalidResponse("item identity");
  }
  const priceSource = item.priceSource;
  if (
    priceSource !== 0 &&
    priceSource !== 1 &&
    priceSource !== 2 &&
    priceSource !== 3 &&
    priceSource !== 4
  ) {
    throw invalidResponse("item.priceSource");
  }
  const quantityFactor = finitePositiveNumber(
    item.quantityFactor,
    "item.quantityFactor",
  );
  const retailPrice = finiteNonNegativeNumber(
    item.retailPrice,
    "item.retailPrice",
  );
  moneyCents(retailPrice, "item.retailPrice");
  const updatedAt = optionalTimestamp(item.updatedAt, "item.updatedAt");
  return Object.freeze({
    storeCode,
    productCode: responseText(item.productCode, "item.productCode"),
    referenceCode: optionalText(item.referenceCode, "item.referenceCode"),
    displayName: responseText(item.displayName, "item.displayName"),
    lookupCode,
    lookupCodeNormalized,
    itemNumber: optionalText(item.itemNumber, "item.itemNumber"),
    barcode: optionalText(item.barcode, "item.barcode"),
    retailPrice,
    priceSource,
    priceSourceLabel: responseText(item.priceSourceLabel, "item.priceSourceLabel"),
    quantityFactor,
    updatedAt,
    rowVersion: optionalText(item.rowVersion, "item.rowVersion"),
    productImage: optionalText(item.productImage, "item.productImage"),
    discountRate: optionalFiniteNumber(item.discountRate, "item.discountRate"),
    isSpecialProduct: requiredBoolean(item.isSpecialProduct, "item.isSpecialProduct"),
  });
}

function requestText(value: unknown, field: string): string {
  if (typeof value !== "string") throw invalidRequest(field);
  const normalized = value.trim();
  if (
    normalized.length === 0 ||
    normalized.length > 128 ||
    /[\u0000-\u001f\u007f]/u.test(normalized)
  ) {
    throw invalidRequest(field);
  }
  return normalized;
}

function responseText(value: unknown, field: string): string {
  if (
    typeof value !== "string" ||
    value.length === 0 ||
    value.length > 4_096 ||
    /[\u0000-\u001f\u007f]/u.test(value)
  ) {
    throw invalidResponse(field);
  }
  return value;
}

function optionalText(value: unknown, field: string): string | null {
  if (value === null || value === undefined || value === "") return null;
  return responseText(value, field);
}

function optionalTimestamp(value: unknown, field: string): string | null {
  const timestamp = optionalText(value, field);
  if (timestamp !== null && Number.isNaN(new Date(timestamp).valueOf())) {
    throw invalidResponse(field);
  }
  return timestamp;
}

function optionalFiniteNumber(value: unknown, field: string): number | null {
  if (value === null || value === undefined) return null;
  return finiteNonNegativeNumber(value, field);
}

function finiteNonNegativeNumber(value: unknown, field: string): number {
  if (typeof value !== "number" || !Number.isFinite(value) || value < 0) {
    throw invalidResponse(field);
  }
  return value;
}

function finitePositiveNumber(value: unknown, field: string): number {
  const number = finiteNonNegativeNumber(value, field);
  if (number <= 0) throw invalidResponse(field);
  return number;
}

function requiredBoolean(value: unknown, field: string): boolean {
  if (typeof value !== "boolean") throw invalidResponse(field);
  return value;
}

function moneyCents(value: number, field: string): number {
  const scaled = value * 100;
  const cents = Math.round(scaled);
  if (
    !Number.isSafeInteger(cents) ||
    Math.abs(scaled - cents) > Number.EPSILON * Math.max(100, Math.abs(scaled))
  ) {
    throw invalidResponse(field);
  }
  return cents;
}

function normalizeLookupCode(value: string): string {
  return value.trim().toUpperCase();
}

function invalidRequest(field: string): HbposApiError {
  return new HbposApiError(`Catalog lookup request field is invalid: ${field}.`, {
    kind: "envelope",
    code: "CATALOG_LOOKUP_REQUEST_INVALID",
  });
}

function invalidResponse(field: string): HbposApiError {
  return new HbposApiError(`Catalog lookup response field is invalid: ${field}.`, {
    kind: "envelope",
    code: "CATALOG_LOOKUP_RESPONSE_INVALID",
  });
}
