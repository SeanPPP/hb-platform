import assert from "node:assert/strict";
import { buildShopProductsQueryKey } from "./product-query-key";
import type { StoreOrderProductQuery } from "./types";

const query: StoreOrderProductQuery = {
  storeCode: "S001",
  itemNumber: "ITEM-001",
  pageNumber: 1,
  pageSize: 18,
};

const ordinaryKey = buildShopProductsQueryKey(query, false);
const locationLookupKey = buildShopProductsQueryKey(query, true);

assert.equal(ordinaryKey[1], query, "商品查询对象必须继续位于 queryKey[1]");
assert.equal(locationLookupKey[1], query, "带货位能力的商品查询对象必须继续位于 queryKey[1]");
assert.equal(ordinaryKey[2], false, "普通账号查询 key 应记录普通权限范围");
assert.equal(locationLookupKey[2], true, "货位查询权限范围应记录在 query key 中");
assert.notDeepEqual(ordinaryKey, locationLookupKey, "不同权限范围不能共用商品查询缓存");

console.log("use-products-key.test.ts: ok");
