import assert from "node:assert/strict";
import {
  buildShopProductsQueryKey,
  resolveShopProductsPlaceholderData,
} from "./product-query-key";
import type { StoreOrderProductQuery } from "./types";

const firstPageQuery: StoreOrderProductQuery = {
  storeCode: "S001",
  pageNumber: 1,
  pageSize: 18,
};
const secondPageQuery: StoreOrderProductQuery = {
  ...firstPageQuery,
  pageNumber: 2,
};
const previousData = { items: [{ itemNumber: "ITEM-001" }] };

assert.equal(
  resolveShopProductsPlaceholderData(
    previousData,
    { queryKey: buildShopProductsQueryKey(firstPageQuery, true) },
    buildShopProductsQueryKey(secondPageQuery, false)[2],
  ),
  undefined,
  "货位权限从 true 切换为 false 时不得复用旧查询结果",
);

assert.equal(
  resolveShopProductsPlaceholderData(
    previousData,
    { queryKey: buildShopProductsQueryKey(firstPageQuery, false) },
    buildShopProductsQueryKey(secondPageQuery, true)[2],
  ),
  undefined,
  "货位权限从 false 切换为 true 时不得复用旧查询结果",
);

assert.equal(
  resolveShopProductsPlaceholderData(
    previousData,
    { queryKey: buildShopProductsQueryKey(firstPageQuery, true) },
    buildShopProductsQueryKey(secondPageQuery, true)[2],
  ),
  previousData,
  "相同权限范围内翻页时应继续复用上一页数据",
);

console.log("product-placeholder-data.test.ts: ok");
