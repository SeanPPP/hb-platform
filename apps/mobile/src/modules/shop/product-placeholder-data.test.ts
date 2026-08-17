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
const keywordQuery: StoreOrderProductQuery = {
  ...firstPageQuery,
  itemNumber: "ITEM-001",
};

assert.equal(
  resolveShopProductsPlaceholderData(
    previousData,
    { queryKey: buildShopProductsQueryKey(firstPageQuery, true) },
    buildShopProductsQueryKey(secondPageQuery, false)[2],
    secondPageQuery,
  ),
  undefined,
  "货位权限从 true 切换为 false 时不得复用旧查询结果",
);

assert.equal(
  resolveShopProductsPlaceholderData(
    previousData,
    { queryKey: buildShopProductsQueryKey(firstPageQuery, false) },
    buildShopProductsQueryKey(secondPageQuery, true)[2],
    secondPageQuery,
  ),
  undefined,
  "货位权限从 false 切换为 true 时不得复用旧查询结果",
);

assert.equal(
  resolveShopProductsPlaceholderData(
    previousData,
    { queryKey: buildShopProductsQueryKey(firstPageQuery, true) },
    buildShopProductsQueryKey(secondPageQuery, true)[2],
    secondPageQuery,
  ),
  previousData,
  "相同权限范围内翻页时应继续复用上一页数据",
);

assert.equal(
  resolveShopProductsPlaceholderData(
    previousData,
    { queryKey: buildShopProductsQueryKey(keywordQuery, true) },
    true,
    { ...keywordQuery, pageNumber: 2 },
  ),
  previousData,
  "同一关键词正常翻页时仍应复用上一页数据",
);

assert.equal(
  resolveShopProductsPlaceholderData(
    previousData,
    { queryKey: buildShopProductsQueryKey(keywordQuery, true) },
    true,
    firstPageQuery,
  ),
  undefined,
  "清空关键词返回普通列表时不得继续展示旧搜索结果",
);

console.log("product-placeholder-data.test.ts: ok");
