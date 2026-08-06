import assert from "node:assert/strict";
import {
  createScanLookupCache,
  getCachedScanLookupProduct,
} from "./scan-lookup-cache";
import { rememberScanLookupProductForMatch } from "./scan-lookup-policy";
import type { StoreOrderProductItem } from "@/modules/shop/types";

const product: StoreOrderProductItem = {
  productCode: "P-001",
  itemNumber: "ITEM-001",
  barcode: "BAR-001",
  minOrderQuantity: 1,
  stockQuantity: 10,
  isInStock: true,
};

const locationCache = createScanLookupCache(60_000);
rememberScanLookupProductForMatch(
  locationCache,
  "S001",
  "LOC-001",
  product,
  1_000,
  "locationBarcode",
);
assert.equal(
  getCachedScanLookupProduct(locationCache, "S001", "LOC-001", 1_001),
  null,
  "货位条码命中不得写入 60 秒扫码映射缓存",
);

const productCache = createScanLookupCache(60_000);
rememberScanLookupProductForMatch(
  productCache,
  "S001",
  "BAR-001",
  product,
  1_000,
  "productBarcode",
);
assert.deepEqual(
  getCachedScanLookupProduct(productCache, "S001", "BAR-001", 1_001),
  product,
  "普通商品条码命中仍应写入 60 秒扫码映射缓存",
);

console.log("scan-lookup-policy.test.ts: ok");
