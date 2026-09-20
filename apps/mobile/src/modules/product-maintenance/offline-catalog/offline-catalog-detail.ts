/**
 * 把离线目录行组装成商品查询页需要的 `ProductLookupItem[]` / `ProductDetail`。
 *
 * 目标是让页面下游（候选列表、详情卡片、标签打印）在离线态与在线态使用同一种
 * 数据结构，因此这里严格对齐 `src/modules/product-maintenance/api.ts` 的 normalize 语义。
 */
import type {
  MultiCodeEditableItem,
  ProductDetail,
  ProductLookupItem,
  ProductSetCodeItem,
  StoreClearancePriceItem,
  StorePriceEditable,
} from "@/modules/product-maintenance/types";
import type { OfflineCatalogItem, OfflineCatalogMatchSource } from "./types";

const PRODUCT_LEVEL_SOURCES = new Set<OfflineCatalogMatchSource>([
  "ProductBarcode",
  "ItemNumber",
  "ProductCode",
]);

function compareOrdinal(left: string, right: string): number {
  return left < right ? -1 : left > right ? 1 : 0;
}

function nullableTrim(value: string | null): string | null {
  const trimmed = value?.trim();
  return trimmed ? trimmed : null;
}

/** 与服务端 lookup 一致：按 `productCode|matchSource|barcode|itemNumber` 去重，按名称排序。 */
export function buildOfflineLookupItems(
  rows: readonly OfflineCatalogItem[],
  keyword: string,
): ProductLookupItem[] {
  const seen = new Set<string>();
  const items: ProductLookupItem[] = [];
  for (const row of rows) {
    const barcode = PRODUCT_LEVEL_SOURCES.has(row.matchSource)
      ? nullableTrim(row.barcode)
      : nullableTrim(row.lookupCode);
    const itemNumber = nullableTrim(row.itemNumber);
    const dedupeKey = `${row.productCode}|${row.matchSource}|${barcode ?? ""}|${itemNumber ?? ""}`;
    if (seen.has(dedupeKey)) {
      continue;
    }
    seen.add(dedupeKey);
    items.push({
      productCode: row.productCode,
      productName: row.productName,
      itemNumber,
      barcode,
      productImage: nullableTrim(row.productImage),
      matchSource: row.matchSource,
      matchValue: keyword.trim(),
      productTypeLabel: resolveProductTypeLabel(row.productType),
      grade: nullableTrim(row.grade),
    });
  }
  return items.sort((left, right) => compareOrdinal(left.productName, right.productName));
}

function resolveProductTypeLabel(productType: number | null): string | null {
  switch (productType) {
    case 0:
      return "普通";
    case 1:
      return "套装";
    case 2:
      return "多码";
    default:
      return null;
  }
}

function resolveSetTypeDescription(setType: number): string {
  switch (setType) {
    case 1:
      return "套装";
    case 2:
      return "多码";
    default:
      return "未知类型";
  }
}

function pickProductLevelRow(rows: readonly OfflineCatalogItem[]): OfflineCatalogItem | null {
  return (
    rows.find((row) => row.matchSource === "ProductBarcode") ??
    rows.find((row) => PRODUCT_LEVEL_SOURCES.has(row.matchSource)) ??
    rows[0] ??
    null
  );
}

function buildStorePrice(row: OfflineCatalogItem): StorePriceEditable | null {
  if (!row.storePriceUuid) {
    return null;
  }
  return {
    uuid: row.storePriceUuid,
    storeCode: row.storeCode,
    storeName: row.storeName,
    productCode: row.productCode,
    storeProductCode: null,
    supplierCode: row.localSupplierCode,
    purchasePrice: row.purchasePrice,
    retailPrice: row.retailPrice,
    discountRate: row.discountRate,
    isAutoPricing: row.isAutoPricing,
    isSpecialProduct: row.isSpecialProduct,
    isActive: true,
    rate: row.rate,
    strategySourceLabel: row.strategySourceLabel,
    strategyRuleLabel: row.strategyRuleLabel,
  };
}

function buildClearancePrice(rows: readonly OfflineCatalogItem[]): StoreClearancePriceItem | null {
  const row = rows.find((item) => item.matchSource === "ClearanceBarcode") ?? rows.find((item) => item.clearanceUuid);
  if (!row?.clearanceUuid) {
    return null;
  }
  return {
    uuid: row.clearanceUuid,
    storeCode: row.storeCode,
    storeName: row.storeName,
    productCode: row.productCode,
    clearanceBarcode: row.clearanceBarcode ?? (row.matchSource === "ClearanceBarcode" ? row.lookupCode : null),
    clearancePrice: row.clearancePrice,
  };
}

function buildSetCode(row: OfflineCatalogItem): ProductSetCodeItem {
  const setCodeId = row.codeId ?? row.codeUuid ?? row.lookupKey;
  return {
    setCodeId,
    productCode: row.productCode,
    setProductCode: row.codeProductCode ?? setCodeId,
    setItemNumber: row.codeItemNumber ?? "",
    setBarcode: row.lookupCode,
    setPurchasePrice: row.codePurchasePrice,
    setRetailPrice: row.codeRetailPrice,
    setQuantity: row.codeQuantity ?? 0,
    setType: row.codeType ?? 1,
    setTypeDescription: resolveSetTypeDescription(row.codeType ?? 1),
    isActive: row.codeIsActive ?? true,
  };
}

function buildMultiCode(row: OfflineCatalogItem): MultiCodeEditableItem {
  return {
    uuid: row.codeUuid ?? row.codeId ?? row.lookupKey,
    setCodeId: row.codeId ?? "",
    storeCode: row.storeCode,
    productCode: row.productCode,
    multiCodeProductCode: row.codeProductCode,
    storeMultiCodeProductCode: null,
    barcode: row.lookupCode,
    purchasePrice: row.codePurchasePrice,
    retailPrice: row.codeRetailPrice,
    discountRate: row.codeDiscountRate,
    isAutoPricing: row.codeIsAutoPricing ?? false,
    isSpecialProduct: row.codeIsSpecialProduct ?? false,
    isActive: row.codeIsActive ?? true,
    rate: null,
    strategySourceLabel: null,
    strategyRuleLabel: null,
  };
}

/**
 * 由同一商品在本店的全部离线行组装详情。返回全新对象，页面可直接 setDetail 并克隆做 dirty 比较。
 * 套码/多码按条码再按 id 排序，与服务端 `ORDER BY SetBarcode, SetCodeId` 一致。
 */
export function buildOfflineProductDetail(rows: readonly OfflineCatalogItem[]): ProductDetail | null {
  const base = pickProductLevelRow(rows);
  if (!base) {
    return null;
  }
  const setCodes = rows
    .filter((row) => row.matchSource === "SetBarcode")
    .map(buildSetCode)
    .sort(
      (left, right) =>
        compareOrdinal(left.setBarcode ?? "", right.setBarcode ?? "") ||
        compareOrdinal(left.setCodeId, right.setCodeId),
    );
  const multiCodes = rows
    .filter((row) => row.matchSource === "MultiBarcode")
    .map(buildMultiCode)
    .sort(
      (left, right) =>
        compareOrdinal(left.barcode ?? "", right.barcode ?? "") ||
        compareOrdinal(left.setCodeId || left.uuid, right.setCodeId || right.uuid),
    );

  return {
    productCode: base.productCode,
    productName: base.productName,
    itemNumber: base.itemNumber,
    barcode: base.barcode,
    productImage: base.productImage,
    productType: base.productType,
    productTypeLabel: resolveProductTypeLabel(base.productType),
    grade: base.grade,
    localSupplierCode: base.localSupplierCode,
    localSupplierName: base.localSupplierName,
    storePrice: buildStorePrice(base),
    clearancePrice: buildClearancePrice(rows),
    setCodes,
    multiCodes,
    setCodeCount: setCodes.length,
    multiCodeCount: multiCodes.length,
    codesIncluded: true,
  };
}
