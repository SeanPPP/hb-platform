import assert from "node:assert/strict";
import test from "node:test";
import { buildOfflineLookupItems, buildOfflineProductDetail } from "./offline-catalog-detail";
import { buildOfflineLookupKey, normalizeOfflineLookupCode, type OfflineCatalogItem } from "./types";

function row(overrides: Partial<OfflineCatalogItem> & Pick<OfflineCatalogItem, "lookupCode" | "matchSource">): OfflineCatalogItem {
  const productCode = overrides.productCode ?? "P001";
  const codeId = overrides.codeId ?? null;
  const lookupCodeNormalized = normalizeOfflineLookupCode(overrides.lookupCode);
  return {
    storeCode: "S001",
    lookupKey: buildOfflineLookupKey({ lookupCodeNormalized, matchSource: overrides.matchSource, productCode, codeId }),
    lookupCodeNormalized,
    productCode,
    productName: "测试商品",
    itemNumber: "AB 123",
    barcode: "9300000000017",
    productImage: null,
    productType: 1,
    grade: "A",
    localSupplierCode: "200",
    localSupplierName: "Hotbargain",
    storeName: "Brisbane",
    storePriceUuid: "sp-1",
    purchasePrice: 1.5,
    retailPrice: 4.5,
    discountRate: 0.2,
    isAutoPricing: true,
    isSpecialProduct: false,
    rate: 3,
    strategySourceLabel: "全局",
    strategyRuleLabel: "1 - 5",
    clearanceUuid: "cp-1",
    clearanceBarcode: "CL0001",
    clearancePrice: 2,
    codeId,
    codeUuid: null,
    codeProductCode: null,
    codeItemNumber: null,
    codeRetailPrice: null,
    codePurchasePrice: null,
    codeQuantity: null,
    codeType: null,
    codeDiscountRate: null,
    codeIsAutoPricing: null,
    codeIsSpecialProduct: null,
    codeIsActive: null,
    updatedAt: "2026-09-17T01:02:03.456Z",
    rowVersion: "ROW",
    ...overrides,
  };
}

test("商品级行、套码行、多码行、清货行组装成完整详情", () => {
  const rows = [
    row({ lookupCode: "9300000000017", matchSource: "ProductBarcode" }),
    row({ lookupCode: "AB 123", matchSource: "ItemNumber" }),
    row({ lookupCode: "P001", matchSource: "ProductCode" }),
    row({
      lookupCode: "SET-B",
      matchSource: "SetBarcode",
      codeId: "set-2",
      codeProductCode: "P001-S2",
      codeItemNumber: "AB 123-2",
      codeRetailPrice: 9,
      codePurchasePrice: 3,
      codeQuantity: 2,
      codeType: 1,
      codeIsActive: true,
    }),
    row({
      lookupCode: "SET-A",
      matchSource: "SetBarcode",
      codeId: "set-1",
      codeProductCode: "P001-S1",
      codeRetailPrice: 8,
      codeQuantity: 1,
      codeType: 1,
      codeIsActive: false,
    }),
    row({
      lookupCode: "MULTI-1",
      matchSource: "MultiBarcode",
      codeId: "multi-1",
      codeUuid: "uuid-m1",
      codeProductCode: "P001-M1",
      codeRetailPrice: 4.5,
      codeDiscountRate: 0.1,
      codeIsAutoPricing: true,
      codeIsActive: true,
      codeType: 2,
    }),
    row({ lookupCode: "CL0001", matchSource: "ClearanceBarcode" }),
  ];

  const detail = buildOfflineProductDetail(rows);
  assert.ok(detail);
  assert.equal(detail.productCode, "P001");
  assert.equal(detail.productTypeLabel, "套装");
  assert.equal(detail.codesIncluded, true);
  assert.equal(detail.storePrice?.uuid, "sp-1");
  assert.equal(detail.storePrice?.retailPrice, 4.5);
  assert.equal(detail.storePrice?.strategyRuleLabel, "1 - 5");
  assert.equal(detail.clearancePrice?.clearanceBarcode, "CL0001");
  assert.equal(detail.clearancePrice?.clearancePrice, 2);
  assert.deepEqual(
    detail.setCodes.map((item) => item.setBarcode),
    ["SET-A", "SET-B"],
    "套码按条码排序",
  );
  assert.equal(detail.setCodes[0]?.isActive, false);
  assert.equal(detail.setCodes[1]?.setQuantity, 2);
  assert.equal(detail.setCodeCount, 2);
  assert.equal(detail.multiCodes.length, 1);
  assert.equal(detail.multiCodes[0]?.uuid, "uuid-m1");
  assert.equal(detail.multiCodes[0]?.setCodeId, "multi-1");
  assert.equal(detail.multiCodes[0]?.discountRate, 0.1);
  assert.equal(detail.multiCodeCount, 1);
});

test("没有分店价的商品 storePrice 为 null，没有清货行则 clearancePrice 为 null", () => {
  const detail = buildOfflineProductDetail([
    row({
      lookupCode: "9300000000017",
      matchSource: "ProductBarcode",
      storePriceUuid: null,
      clearanceUuid: null,
      clearanceBarcode: null,
    }),
  ]);
  assert.ok(detail);
  assert.equal(detail.storePrice, null);
  assert.equal(detail.clearancePrice, null);
  assert.equal(buildOfflineProductDetail([]), null);
});

test("候选列表：套码命中用套码条码，商品命中用主条码，去重并按名称排序", () => {
  const rows = [
    row({ lookupCode: "SET-A", matchSource: "SetBarcode", codeId: "set-1", productCode: "P002", productName: "乙商品" }),
    row({ lookupCode: "9300000000017", matchSource: "ProductBarcode", productName: "甲商品" }),
    row({ lookupCode: "9300000000017", matchSource: "ProductBarcode", productName: "甲商品" }),
  ];
  const items = buildOfflineLookupItems(rows, " 9300000000017 ");
  assert.equal(items.length, 2);
  assert.equal(items[0]?.productName, "乙商品");
  assert.equal(items[0]?.barcode, "SET-A");
  assert.equal(items[0]?.matchSource, "SetBarcode");
  assert.equal(items[1]?.barcode, "9300000000017");
  assert.equal(items[1]?.matchValue, "9300000000017");
  assert.equal(items[1]?.productTypeLabel, "套装");
});
