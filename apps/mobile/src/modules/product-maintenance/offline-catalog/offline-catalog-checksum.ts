/**
 * 离线目录页校验和（与后端 OfflineCatalogChecksum 逐字节一致）。
 *
 * canonical 编码：每个字段输出 `{UTF-16 长度}:{内容}|`，整体 UTF-8 后 SHA256。
 * 数值统一用 IEEE754 binary64 大端 16 位小写十六进制（后端 decimal 先按 JSON
 * 十进制文本转 double，再编码），避免跨端十进制格式差异。可空数值为空串，
 * 布尔为 "1"/"0"，可空布尔为空串。时间戳统一为 UTC 毫秒 ISO（`2026-09-17T01:02:03.456Z`）。
 *
 * 字段顺序固定，见 `appendItemFields`；rowVersion 是派生值，不参与页校验和。
 */
import type { OfflineCatalogDeletedItem, OfflineCatalogItem } from "./types";

export const OFFLINE_CATALOG_PAGE_CHECKSUM_MARKER = "HB-MOBILE-OFFLINE-CATALOG-PAGE-V1";
export const OFFLINE_CATALOG_PAGE_CHECKSUM_PREFIX = "sha256-offline-catalog-page-v1:";
export const OFFLINE_CATALOG_DELTA_CHECKSUM_MARKER = "HB-MOBILE-OFFLINE-CATALOG-DELTA-V1";
export const OFFLINE_CATALOG_DELTA_CHECKSUM_PREFIX = "sha256-offline-catalog-delta-v1:";

export type OfflineCatalogDigest = (payload: string) => Promise<string>;

const BINARY64_BUFFER = new ArrayBuffer(8);
const BINARY64_VIEW = new DataView(BINARY64_BUFFER);
const HEX_CHARS = "0123456789abcdef";

/** IEEE754 binary64 大端十六进制（16 字符小写）；负零归一为正零。 */
export function formatBinary64(value: number): string {
  if (!Number.isFinite(value)) {
    throw new Error("Offline catalog checksum cannot encode a non-finite number.");
  }
  BINARY64_VIEW.setFloat64(0, Object.is(value, -0) ? 0 : value, false);
  const bytes = new Uint8Array(BINARY64_BUFFER);
  let hex = "";
  for (const byte of bytes) {
    hex += HEX_CHARS[byte >> 4] ?? "";
    hex += HEX_CHARS[byte & 15] ?? "";
  }
  return hex;
}

function nullableNumber(value: number | null): string {
  return value === null ? "" : formatBinary64(value);
}

function bool(value: boolean): string {
  return value ? "1" : "0";
}

function nullableBool(value: boolean | null): string {
  return value === null ? "" : bool(value);
}

/** 时间戳统一成 UTC 毫秒 ISO；非法值抛错，避免用错误格式默默通过校验。 */
export function formatChecksumTimestamp(value: string | null): string {
  if (value === null || value === "") {
    return "";
  }
  const parsed = new Date(value);
  if (Number.isNaN(parsed.valueOf())) {
    throw new Error(`Offline catalog checksum cannot encode timestamp: ${value}`);
  }
  return parsed.toISOString();
}

function appendField(parts: string[], value: string): void {
  parts.push(String(value.length), ":", value, "|");
}

function appendItemFields(parts: string[], item: OfflineCatalogItem): void {
  appendField(parts, item.storeCode);
  appendField(parts, item.lookupKey);
  appendField(parts, item.lookupCode);
  appendField(parts, item.lookupCodeNormalized);
  appendField(parts, item.matchSource);
  appendField(parts, item.productCode);
  appendField(parts, item.productName);
  appendField(parts, item.itemNumber ?? "");
  appendField(parts, item.barcode ?? "");
  appendField(parts, item.productImage ?? "");
  appendField(parts, nullableNumber(item.productType));
  appendField(parts, item.grade ?? "");
  appendField(parts, item.localSupplierCode ?? "");
  appendField(parts, item.localSupplierName ?? "");
  appendField(parts, item.storeName ?? "");
  appendField(parts, item.storePriceUuid ?? "");
  appendField(parts, nullableNumber(item.purchasePrice));
  appendField(parts, nullableNumber(item.retailPrice));
  appendField(parts, nullableNumber(item.discountRate));
  appendField(parts, bool(item.isAutoPricing));
  appendField(parts, bool(item.isSpecialProduct));
  appendField(parts, nullableNumber(item.rate));
  appendField(parts, item.strategySourceLabel ?? "");
  appendField(parts, item.strategyRuleLabel ?? "");
  appendField(parts, item.clearanceUuid ?? "");
  appendField(parts, item.clearanceBarcode ?? "");
  appendField(parts, nullableNumber(item.clearancePrice));
  appendField(parts, item.codeId ?? "");
  appendField(parts, item.codeUuid ?? "");
  appendField(parts, item.codeProductCode ?? "");
  appendField(parts, item.codeItemNumber ?? "");
  appendField(parts, nullableNumber(item.codeRetailPrice));
  appendField(parts, nullableNumber(item.codePurchasePrice));
  appendField(parts, nullableNumber(item.codeQuantity));
  appendField(parts, nullableNumber(item.codeType));
  appendField(parts, nullableNumber(item.codeDiscountRate));
  appendField(parts, nullableBool(item.codeIsAutoPricing));
  appendField(parts, nullableBool(item.codeIsSpecialProduct));
  appendField(parts, nullableBool(item.codeIsActive));
  appendField(parts, formatChecksumTimestamp(item.updatedAt));
}

export function buildOfflineCatalogPageCanonical(items: readonly OfflineCatalogItem[]): string {
  const parts: string[] = [];
  appendField(parts, OFFLINE_CATALOG_PAGE_CHECKSUM_MARKER);
  appendField(parts, formatBinary64(items.length));
  for (const item of items) {
    appendItemFields(parts, item);
  }
  return parts.join("");
}

export function buildOfflineCatalogDeltaCanonical(input: {
  baseCatalogVersion: string;
  targetCatalogVersion: string;
  items: readonly OfflineCatalogItem[];
  deletedItems: readonly OfflineCatalogDeletedItem[];
}): string {
  // 操作按 lookupKey 的 UTF-16 码元顺序归并（与服务端 Ordinal 比较一致）。
  const operations = [
    ...input.items.map((item) => ({ kind: "U" as const, key: item.lookupKey, item })),
    ...input.deletedItems.map((deleted) => ({ kind: "D" as const, key: deleted.lookupKey, deleted })),
  ].sort((left, right) => (left.key < right.key ? -1 : left.key > right.key ? 1 : 0));

  const parts: string[] = [];
  appendField(parts, OFFLINE_CATALOG_DELTA_CHECKSUM_MARKER);
  appendField(parts, input.baseCatalogVersion);
  appendField(parts, input.targetCatalogVersion);
  appendField(parts, formatBinary64(operations.length));
  for (const operation of operations) {
    if (operation.kind === "U") {
      appendField(parts, "U");
      appendItemFields(parts, operation.item);
    } else {
      appendField(parts, "D");
      appendField(parts, operation.deleted.storeCode);
      appendField(parts, operation.deleted.lookupKey);
      appendField(parts, formatChecksumTimestamp(operation.deleted.deletedAt));
    }
  }
  return parts.join("");
}

async function digestToPrefixed(
  canonical: string,
  digest: OfflineCatalogDigest,
  prefix: string,
): Promise<string> {
  const hex = (await digest(canonical)).trim().toLowerCase();
  if (!/^[0-9a-f]{64}$/.test(hex)) {
    throw new Error("Offline catalog digest returned an invalid SHA256 value.");
  }
  return `${prefix}${hex}`;
}

export function calculateOfflineCatalogPageChecksum(
  items: readonly OfflineCatalogItem[],
  digest: OfflineCatalogDigest,
): Promise<string> {
  return digestToPrefixed(
    buildOfflineCatalogPageCanonical(items),
    digest,
    OFFLINE_CATALOG_PAGE_CHECKSUM_PREFIX,
  );
}

export function calculateOfflineCatalogDeltaChecksum(
  input: Parameters<typeof buildOfflineCatalogDeltaCanonical>[0],
  digest: OfflineCatalogDigest,
): Promise<string> {
  return digestToPrefixed(
    buildOfflineCatalogDeltaCanonical(input),
    digest,
    OFFLINE_CATALOG_DELTA_CHECKSUM_PREFIX,
  );
}

/** 生产环境摘要：同步 require 让 Metro 打包原生桥接，Node 测试不加载 expo-crypto。 */
export async function expoSha256Digest(payload: string): Promise<string> {
  // eslint-disable-next-line @typescript-eslint/no-require-imports
  const Crypto = require("expo-crypto") as typeof import("expo-crypto");
  return Crypto.digestStringAsync(Crypto.CryptoDigestAlgorithm.SHA256, payload);
}
