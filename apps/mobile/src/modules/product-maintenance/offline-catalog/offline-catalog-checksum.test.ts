import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { existsSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import {
  buildOfflineCatalogDeltaCanonical,
  buildOfflineCatalogPageCanonical,
  calculateOfflineCatalogDeltaChecksum,
  calculateOfflineCatalogPageChecksum,
  formatBinary64,
  formatChecksumTimestamp,
  OFFLINE_CATALOG_DELTA_CHECKSUM_PREFIX,
  OFFLINE_CATALOG_PAGE_CHECKSUM_PREFIX,
} from "./offline-catalog-checksum";
import { buildOfflineLookupKey, normalizeOfflineLookupCode, type OfflineCatalogItem } from "./types";

const currentDir = dirname(fileURLToPath(import.meta.url));
/** 与后端 BlazorApp.Api.Tests/OfflineCatalogChecksumVectors.json 共享；设置 OFFLINE_CATALOG_VECTORS_UPDATE=1 重新生成。 */
const vectorsPath = resolve(
  currentDir,
  "../../../../../../services/backend/BlazorApp.Api.Tests/OfflineCatalogChecksumVectors.json",
);

const nodeSha256 = async (payload: string) => createHash("sha256").update(payload, "utf8").digest("hex");

function item(lookupCode: string, matchSource: OfflineCatalogItem["matchSource"], productCode: string, codeId: string | null, retailPrice: number | null): OfflineCatalogItem {
  const lookupCodeNormalized = normalizeOfflineLookupCode(lookupCode);
  return {
    storeCode: "S001",
    lookupKey: buildOfflineLookupKey({ lookupCodeNormalized, matchSource, productCode, codeId }),
    lookupCode,
    lookupCodeNormalized,
    matchSource,
    productCode,
    productName: "测试商品 Ünïcode",
    itemNumber: "AB 123",
    barcode: "9300000000017",
    productImage: "https://example.com/p.jpg",
    productType: 1,
    grade: "A",
    localSupplierCode: "200",
    localSupplierName: "Hotbargain",
    storeName: "Brisbane",
    storePriceUuid: "sp-1",
    purchasePrice: 1.5,
    retailPrice,
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
    codeUuid: codeId ? `uuid-${codeId}` : null,
    codeProductCode: codeId ? `${productCode}-${codeId}` : null,
    codeItemNumber: codeId ? "AB 123-1" : null,
    codeRetailPrice: codeId ? 9 : null,
    codePurchasePrice: codeId ? 3 : null,
    codeQuantity: codeId ? 2 : null,
    codeType: codeId ? 1 : null,
    codeDiscountRate: codeId ? 0.1 : null,
    codeIsAutoPricing: codeId ? true : null,
    codeIsSpecialProduct: codeId ? false : null,
    codeIsActive: codeId ? true : null,
    updatedAt: "2026-09-17T01:02:03.456Z",
    rowVersion: "ROW",
  };
}

const VECTOR_ITEMS = [
  item("9300000000017", "ProductBarcode", "P001", null, 4.5),
  item("set-1", "SetBarcode", "P001", "code-1", null),
];
const VECTOR_DELETED = { storeCode: "S001", lookupKey: "ZZZProductBarcodeP999", deletedAt: "2026-09-17T02:00:00.000Z" };
const VECTOR_BASE = "offline-catalog-v1:base";
const VECTOR_TARGET = "offline-catalog-v1:target";

test("formatBinary64 输出 IEEE754 大端十六进制并归一负零", () => {
  assert.equal(formatBinary64(4.5), "4012000000000000");
  assert.equal(formatBinary64(0), "0000000000000000");
  assert.equal(formatBinary64(-0), "0000000000000000");
  assert.equal(formatBinary64(0.2), "3fc999999999999a");
  assert.equal(formatBinary64(3), "4008000000000000");
  assert.throws(() => formatBinary64(Number.NaN));
});

test("时间戳统一为 UTC 毫秒 ISO", () => {
  assert.equal(formatChecksumTimestamp("2026-09-17T11:02:03.456+10:00"), "2026-09-17T01:02:03.456Z");
  assert.equal(formatChecksumTimestamp(null), "");
  assert.throws(() => formatChecksumTimestamp("not-a-date"));
});

test("canonical 编码使用 UTF-16 长度帧", () => {
  const canonical = buildOfflineCatalogPageCanonical([]);
  assert.equal(canonical, "33:HB-MOBILE-OFFLINE-CATALOG-PAGE-V1|16:0000000000000000|");
});

test("页与 delta 校验和带前缀且对字段变化敏感", async () => {
  const checksum = await calculateOfflineCatalogPageChecksum(VECTOR_ITEMS, nodeSha256);
  assert.ok(checksum.startsWith(OFFLINE_CATALOG_PAGE_CHECKSUM_PREFIX));
  const changed = await calculateOfflineCatalogPageChecksum(
    [{ ...VECTOR_ITEMS[0]!, retailPrice: 4.51 }, VECTOR_ITEMS[1]!],
    nodeSha256,
  );
  assert.notEqual(checksum, changed);

  const deltaChecksum = await calculateOfflineCatalogDeltaChecksum(
    { baseCatalogVersion: VECTOR_BASE, targetCatalogVersion: VECTOR_TARGET, items: VECTOR_ITEMS, deletedItems: [VECTOR_DELETED] },
    nodeSha256,
  );
  assert.ok(deltaChecksum.startsWith(OFFLINE_CATALOG_DELTA_CHECKSUM_PREFIX));
  // 操作顺序按 lookupKey 归并，输入顺序不影响结果。
  const reordered = await calculateOfflineCatalogDeltaChecksum(
    { baseCatalogVersion: VECTOR_BASE, targetCatalogVersion: VECTOR_TARGET, items: [...VECTOR_ITEMS].reverse(), deletedItems: [VECTOR_DELETED] },
    nodeSha256,
  );
  assert.equal(deltaChecksum, reordered);
  assert.ok(buildOfflineCatalogDeltaCanonical({ baseCatalogVersion: VECTOR_BASE, targetCatalogVersion: VECTOR_TARGET, items: [], deletedItems: [VECTOR_DELETED] }).includes("1:D|"));
});

test("与后端共享的固定测试向量一致", async () => {
  const pageChecksum = await calculateOfflineCatalogPageChecksum(VECTOR_ITEMS, nodeSha256);
  const deltaChecksum = await calculateOfflineCatalogDeltaChecksum(
    { baseCatalogVersion: VECTOR_BASE, targetCatalogVersion: VECTOR_TARGET, items: VECTOR_ITEMS, deletedItems: [VECTOR_DELETED] },
    nodeSha256,
  );
  // rowVersion = 行字段（不含 rowVersion）的 SHA256 大写；页 canonical 去掉 marker/count 帧即单行 canonical。
  const rowCanonical = buildOfflineCatalogPageCanonical([VECTOR_ITEMS[0]!]).replace(
    "33:HB-MOBILE-OFFLINE-CATALOG-PAGE-V1|16:3ff0000000000000|",
    "",
  );
  const rowVersion = (await nodeSha256(rowCanonical)).toUpperCase();
  const vectors = {
    items: VECTOR_ITEMS,
    deleted: VECTOR_DELETED,
    baseCatalogVersion: VECTOR_BASE,
    targetCatalogVersion: VECTOR_TARGET,
    pageChecksum,
    deltaChecksum,
    rowVersion,
  };
  if (process.env.OFFLINE_CATALOG_VECTORS_UPDATE === "1" || !existsSync(vectorsPath)) {
    writeFileSync(vectorsPath, `${JSON.stringify(vectors, null, 2)}\n`, "utf8");
  }
  const stored = JSON.parse(readFileSync(vectorsPath, "utf8")) as typeof vectors;
  assert.equal(stored.pageChecksum, pageChecksum, "页校验和与共享向量不一致，需同步后端实现或更新向量");
  assert.equal(stored.deltaChecksum, deltaChecksum, "delta 校验和与共享向量不一致");
  assert.equal(stored.rowVersion, rowVersion, "rowVersion 与共享向量不一致");
});
