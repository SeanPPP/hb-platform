import assert from "node:assert/strict";
import {
  buildContainerDetailQuery,
  buildContainerListPayload,
  buildCreateProductsOperationId,
  buildPushProductsToHqOperationId,
  buildBatchScope,
  getCurrentPageDetailGuids,
  getDetailImageUrl,
  normalizeCreateContainerResponse,
  normalizeContainerDetailResponse,
  normalizeContainerDetailQueryResult,
  normalizeContainerListResponse,
  normalizeContainerJob,
  toPushProductsToHqItems,
  toggleCurrentPageSelection,
} from "./query";

const listPayload = buildContainerListPayload({
  page: 2,
  containerNumberFilter: " C-01 ",
  itemNumberFilter: " A100 ",
  loadingDateStart: " 2026-01-01 ",
  totalPiecesMin: 10,
  totalAmountMax: 999.5,
});

assert.equal(listPayload.DateType, "预计到岸日期");
assert.equal(listPayload.SortBy, "预计到岸日期");
assert.equal(listPayload.SortDirection, "desc");
assert.equal(listPayload.ContainerNumberFilter, "C-01");
assert.equal(listPayload.ItemNumberFilter, "A100");
assert.equal(listPayload.Page, 2);
assert.equal(listPayload.LoadingDateStart, "2026-01-01");
assert.equal(listPayload.TotalPiecesMin, 10);
assert.equal(listPayload.TotalAmountMax, 999.5);

const detailQuery = buildContainerDetailQuery("container-1", {
  keyword: " hb-1 ",
  selectedTags: ["all", "new"],
});

assert.equal(detailQuery.itemNumber, "hb-1");
assert.equal(detailQuery.sortBy, "itemNumber");
assert.equal(detailQuery.sortOrder, "ascend");
assert.deepEqual(detailQuery.selectedTags, ["new"]);
assert.equal(detailQuery.includeTotal, true);
assert.equal(detailQuery.includeStats, true);

assert.deepEqual(
  buildBatchScope(detailQuery, [" D2 ", "", "D1"]),
  { selectedHguids: ["D2", "D1"] },
);
assert.deepEqual(
  buildBatchScope(detailQuery, []),
  {
    query: {
      ...detailQuery,
      includeTotal: false,
      includeStats: false,
    },
  },
);

assert.equal(
  buildCreateProductsOperationId("container-1", [" b ", "a"]),
  "container-create-products:container-1:a,b",
);
assert.equal(
  buildPushProductsToHqOperationId("container-1", ["P2", "P1"], 2, ["retailPrice", "barcode"]),
  "container-push-hq:container-1:P1,P2:2:barcode,retailPrice",
);

const normalizedList = normalizeContainerListResponse({
  Items: [{ HGUID: "C1" }],
  TotalCount: 31,
  Page: 2,
  PageSize: 20,
});

assert.equal(normalizedList.containers[0]?.HGUID, "C1");
assert.equal(normalizedList.totalPages, 2);

const normalizedContainerDetail = normalizeContainerDetailResponse({
  success: true,
  data: { HGUID: "C1", 货柜编号: "CN-01" },
});

assert.equal(normalizedContainerDetail.HGUID, "C1");
assert.equal(normalizedContainerDetail.货柜编号, "CN-01");

assert.equal(
  normalizeCreateContainerResponse({
    success: true,
    data: { containerGuid: "C2" },
  }),
  "C2",
);

const normalizedDetail = normalizeContainerDetailQueryResult({
  Items: [{ HGUID: "D1" }],
  ItemsTotal: 40,
  PageNumber: 1,
  PageSize: 30,
  HasMore: true,
  TagStats: { all: 40, new: 3 },
}, detailQuery);

assert.equal(normalizedDetail.items[0]?.HGUID, "D1");
assert.equal(normalizedDetail.hasMore, true);
assert.equal(normalizedDetail.tagStats.all, 40);
assert.equal(normalizedDetail.tagStats.new, 3);
assert.equal(normalizedDetail.tagStats.inactive, 0);

assert.equal(
  getDetailImageUrl({
    商品图片: " https://cdn.example.com/detail.png ",
    商品信息: { 商品图片: "https://cdn.example.com/product.png" },
  }),
  "https://cdn.example.com/detail.png",
);
assert.equal(
  getDetailImageUrl({
    商品信息: { 商品图片: " https://cdn.example.com/product.png " },
  }),
  "https://cdn.example.com/product.png",
);
assert.equal(
  toPushProductsToHqItems([{
    商品图片: " https://cdn.example.com/detail.png ",
    商品信息: { 商品图片: "https://cdn.example.com/product.png" },
  }])[0]?.imageUrl,
  "https://cdn.example.com/detail.png",
);

const currentPageDetails = [
  { HGUID: " D1 " },
  { hguid: "D2" },
  { HGUID: " " },
  {},
];
assert.deepEqual(getCurrentPageDetailGuids(currentPageDetails), ["D1", "D2"]);
assert.deepEqual(toggleCurrentPageSelection(["OLD"], currentPageDetails), ["OLD", "D1", "D2"]);
assert.deepEqual(toggleCurrentPageSelection(["OLD", "D1"], currentPageDetails), ["OLD", "D1", "D2"]);
assert.deepEqual(toggleCurrentPageSelection(["OLD", "D1", "D2"], currentPageDetails), ["OLD"]);

const normalizedJob = normalizeContainerJob({
  JobId: "job-1",
  Status: "Completed",
  Result: {
    CreatedCount: 2,
    FailedCount: 1,
    Errors: [{ message: "bad" }],
  },
});

assert.equal(normalizedJob.jobId, "job-1");
assert.equal(normalizedJob.status, "Succeeded");
assert.equal(normalizedJob.result.createdCount, 2);
assert.equal(normalizedJob.result.failedCount, 1);
assert.equal(normalizedJob.result.errors.length, 1);

console.log("containers query tests passed");
