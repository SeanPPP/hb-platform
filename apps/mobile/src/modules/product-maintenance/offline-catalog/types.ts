/**
 * 移动端离线商品目录的协议类型。
 *
 * 与后端 `OfflineCatalogItemDto` 一一对应（services/backend/BlazorApp.Api 的
 * offline-catalog 模块）。行粒度为「一售卖码一行」：主条码 / 货号 / 商品编码 /
 * 套码 / 多码 / 清货码各自一行，商品级字段在每行上冗余，便于 SQLite 单表查询。
 * 字段顺序是校验和协议的一部分，新增字段必须同时更新 offline-catalog-checksum.ts
 * 与后端 OfflineCatalogChecksum。
 */
export type OfflineCatalogMatchSource =
  | "ProductBarcode"
  | "ItemNumber"
  | "ProductCode"
  | "SetBarcode"
  | "MultiBarcode"
  | "ClearanceBarcode";

export const OFFLINE_CATALOG_MATCH_SOURCES: readonly OfflineCatalogMatchSource[] = [
  "ProductBarcode",
  "ItemNumber",
  "ProductCode",
  "SetBarcode",
  "MultiBarcode",
  "ClearanceBarcode",
];

export interface OfflineCatalogItem {
  storeCode: string;
  /** 归并/游标/唯一键：`lookupCodeNormalizedmatchSourceproductCodecodeId`。 */
  lookupKey: string;
  lookupCode: string;
  lookupCodeNormalized: string;
  matchSource: OfflineCatalogMatchSource;
  productCode: string;
  productName: string;
  itemNumber: string | null;
  barcode: string | null;
  productImage: string | null;
  productType: number | null;
  grade: string | null;
  localSupplierCode: string | null;
  localSupplierName: string | null;
  storeName: string | null;
  storePriceUuid: string | null;
  purchasePrice: number | null;
  retailPrice: number | null;
  discountRate: number | null;
  isAutoPricing: boolean;
  isSpecialProduct: boolean;
  rate: number | null;
  strategySourceLabel: string | null;
  strategyRuleLabel: string | null;
  clearanceUuid: string | null;
  clearanceBarcode: string | null;
  clearancePrice: number | null;
  /** 套码 SetCodeId / 多码 SetCodeId；商品级行为 null。 */
  codeId: string | null;
  /** 多码在本店的投影 UUID（历史多码无 SetCodeId 时用它保存）。 */
  codeUuid: string | null;
  codeProductCode: string | null;
  codeItemNumber: string | null;
  codeRetailPrice: number | null;
  codePurchasePrice: number | null;
  codeQuantity: number | null;
  codeType: number | null;
  codeDiscountRate: number | null;
  codeIsAutoPricing: boolean | null;
  codeIsSpecialProduct: boolean | null;
  codeIsActive: boolean | null;
  /** 服务端最后更新时间（ISO，UTC 毫秒），无法确定时为 null。 */
  updatedAt: string | null;
  /** 业务字段 SHA256（服务端生成），用于 delta 归并比较。 */
  rowVersion: string;
}

export interface OfflineCatalogDeletedItem {
  storeCode: string;
  lookupKey: string;
  deletedAt: string | null;
}

export type OfflineCatalogSyncMode = "full" | "delta" | "noChange";

export interface OfflineCatalogSyncPlan {
  storeCode: string;
  generatedAt: string;
  mode: OfflineCatalogSyncMode;
  baseCatalogVersion: string | null;
  targetCatalogVersion: string;
  targetTotal: number;
  downloadLeaseId: string | null;
  deltaOperationCount: number | null;
}

export interface OfflineCatalogPage {
  storeCode: string;
  generatedAt: string;
  cursor: string | null;
  items: readonly OfflineCatalogItem[];
  nextCursor: string | null;
  hasMore: boolean;
  totalCount: number;
  catalogVersion: string;
  pageChecksum: string;
}

export interface OfflineCatalogDeltaPage {
  storeCode: string;
  generatedAt: string;
  baseCatalogVersion: string;
  targetCatalogVersion: string;
  cursor: string | null;
  items: readonly OfflineCatalogItem[];
  deletedItems: readonly OfflineCatalogDeletedItem[];
  nextCursor: string | null;
  hasMore: boolean;
  targetTotal: number;
  pageChecksum: string;
}

/** 已激活本地快照的摘要；横幅与状态行只展示这些信息。 */
export interface ActiveOfflineCatalogMetadata {
  snapshotId: string;
  storeCode: string;
  catalogVersion: string;
  itemCount: number;
  /** 服务端数据生成时间（ISO）。 */
  generatedAt: string;
  /** 本机激活时间（ISO）。 */
  activatedAt: string;
}

/** 业务/网络错误码统一以 `OFFLINE_CATALOG_` 前缀标识，供刷新协调器分类。 */
export class OfflineCatalogError extends Error {
  public constructor(
    message: string,
    public readonly code: string,
    public readonly status?: number,
  ) {
    super(message);
    this.name = "OfflineCatalogError";
  }
}

/** Unicode 空格分隔符（普通空格之外）；与后端 ItemNumberSpaceVariants 一致。 */
const ITEM_NUMBER_SPACE_VARIANTS =
  /[               　]/g;

/** 与服务端 `LookupCodeNormalized` 完全一致：空格变体统一为半角空格 → trim → 大写。 */
export function normalizeOfflineLookupCode(value: string | null | undefined): string {
  return (value ?? "").replace(ITEM_NUMBER_SPACE_VARIANTS, " ").trim().toUpperCase();
}

export const OFFLINE_CATALOG_LOOKUP_KEY_SEPARATOR = "";

export function buildOfflineLookupKey(input: {
  lookupCodeNormalized: string;
  matchSource: OfflineCatalogMatchSource;
  productCode: string;
  codeId: string | null;
}): string {
  return [
    input.lookupCodeNormalized,
    input.matchSource,
    input.productCode,
    input.codeId ?? "",
  ].join(OFFLINE_CATALOG_LOOKUP_KEY_SEPARATOR);
}
