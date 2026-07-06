import {
  buildCategoryNameMap,
  buildHomeProductQuery,
  flattenVisibleCategories,
} from "./home-filters";
import type { StoreOrderCategoryNode } from "./types";

function assertEqual<T>(actual: T, expected: T, label: string) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}

function assertDeepEqual(actual: unknown, expected: unknown, label: string) {
  const actualText = JSON.stringify(actual);
  const expectedText = JSON.stringify(expected);
  if (actualText !== expectedText) {
    throw new Error(`${label}. Expected: ${expectedText}, received: ${actualText}`);
  }
}

const query = buildHomeProductQuery({
  storeCode: "1024",
  keyword: "  HB001  ",
  categoryGUID: "cat-1",
  grade: "A",
  pageNumber: 2,
  pageSize: 18,
});

assertDeepEqual(
  query,
  {
    storeCode: "1024",
    itemNumber: "HB001",
    categoryGUID: "cat-1",
    grade: "A",
    pageNumber: 2,
    pageSize: 18,
    sortBy: "Default",
  },
  "Home 商品查询应只把单搜索框关键词传给 itemNumber"
);

assertEqual(
  Object.prototype.hasOwnProperty.call(query, "productName"),
  false,
  "Home 商品查询不应把关键词同时传给 productName"
);

const categories: StoreOrderCategoryNode[] = [
  {
    categoryGUID: "home",
    categoryName: "01.HOMEWARE",
    children: [
      { categoryGUID: "home-cup", categoryName: "Cups" },
      { categoryGUID: "home-box", categoryName: "Boxes" },
    ],
  },
  {
    categoryGUID: "hardware",
    categoryName: "02.HARDWARE",
    children: [{ categoryGUID: "hardware-tools", categoryName: "Tools" }],
  },
];

assertDeepEqual(
  flattenVisibleCategories(categories, []).map((row) => row.node.categoryGUID),
  ["home", "hardware"],
  "未展开时只渲染顶层分类"
);

assertDeepEqual(
  flattenVisibleCategories(categories, ["home"]).map((row) => ({
    id: row.node.categoryGUID,
    depth: row.depth,
  })),
  [
    { id: "home", depth: 0 },
    { id: "home-cup", depth: 1 },
    { id: "home-box", depth: 1 },
    { id: "hardware", depth: 0 },
  ],
  "展开后只渲染该分支子分类"
);

assertEqual(
  buildCategoryNameMap(categories).get("hardware-tools"),
  "Tools",
  "分类名称 map 应覆盖深层子分类"
);
