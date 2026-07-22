// src/pages/Warehouse/StoreOrders/storeOrderUnmatchedStore.logic.test.ts
import fs from "node:fs";
import path from "node:path";
function assert(condition, label) {
  if (!condition) {
    throw new Error(label);
  }
}
var pageSource = fs.readFileSync(path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/index.tsx"), "utf8");
var serviceSource = fs.readFileSync(path.resolve(process.cwd(), "src/services/storeOrderService.ts"), "utf8");
var compactCssSource = fs.readFileSync(path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/compact.css"), "utf8");
function extractSection(source, startMarker, endMarker, label) {
  const startIndex = source.indexOf(startMarker);
  assert(startIndex >= 0, `${label}\u672A\u627E\u5230\u8D77\u59CB\u6807\u8BB0`);
  const endIndex = source.indexOf(endMarker, startIndex + startMarker.length);
  assert(endIndex > startIndex, `${label}\u672A\u627E\u5230\u7ED3\u675F\u6807\u8BB0`);
  return source.slice(startIndex, endIndex);
}
assert(
  pageSource.includes("t('storeOrders.fixStoreGuid'") && pageSource.includes("openUnmatchedStoreModal"),
  "\u5206\u5E97\u8BA2\u8D27\u5217\u8868\u5DE5\u5177\u680F\u5E94\u63D0\u4F9B\u4FEE\u590D\u5206\u5E97 GUID \u5165\u53E3"
);
assert(
  pageSource.includes("getUnmatchedStoreOrderGroups()") && pageSource.includes("unmatchedStoreGroups") && pageSource.includes('rowKey="sourceStoreCode"'),
  "\u4FEE\u590D\u5F39\u7A97\u5E94\u6309\u65E7\u5206\u5E97 GUID \u805A\u5408\u663E\u793A\uFF0C\u4E0D\u5C55\u793A\u8BA2\u5355\u660E\u7EC6\u5217\u8868"
);
assert(
  pageSource.includes("batchMapStoreOrderStoreCode({ mappings })") && pageSource.includes("sourceStoreCode: group.sourceStoreCode") && pageSource.includes("targetStoreCode: unmatchedStoreTargets[group.sourceStoreCode]"),
  "\u4FEE\u590D\u4FDD\u5B58\u5E94\u63D0\u4EA4\u65E7 GUID \u5230\u76EE\u6807\u672C\u5730\u5206\u5E97\u7F16\u7801\u7684\u6620\u5C04"
);
assert(
  pageSource.includes("loadAllUnmatchedTargetStores") && pageSource.includes("UNMATCHED_TARGET_STORE_PAGE_SIZE = 500") && pageSource.includes("sortField: 'storeName'") && pageSource.includes("sortOrder: 'ascend'") && !pageSource.includes("isActive: true,\n          sortField: 'storeName'"),
  "\u76EE\u6807\u672C\u5730\u5206\u5E97\u5E94\u5206\u9875\u52A0\u8F7D\u6240\u6709\u5206\u5E97\u5E76\u6309\u540D\u79F0\u6392\u5E8F\uFF0C\u4E0D\u5E94\u53EA\u53D6\u542F\u7528\u5206\u5E97"
);
assert(
  pageSource.includes("stores.length >= result.total") && pageSource.includes("result.items.length < UNMATCHED_TARGET_STORE_PAGE_SIZE") && pageSource.includes("page += 1"),
  "\u76EE\u6807\u672C\u5730\u5206\u5E97\u5E94\u5FAA\u73AF\u52A0\u8F7D\u6240\u6709\u5206\u9875\uFF0C\u907F\u514D\u53EA\u663E\u793A\u7B2C\u4E00\u9875\u5206\u5E97"
);
assert(
  pageSource.includes("buildUnmatchedTargetStoreLabel") && pageSource.includes("store.brandName?.trim()") && pageSource.includes("store.address?.trim()") && pageSource.includes("`${storeName}\uFF08\u505C\u7528\uFF09`") && pageSource.includes("labelParts.join('\uFF5C')") && pageSource.includes("title: buildUnmatchedTargetStoreLabel(item)"),
  "\u76EE\u6807\u672C\u5730\u5206\u5E97\u9009\u9879\u5E94\u5C55\u793A\u5206\u5E97\u7F16\u7801\u3001\u540D\u79F0\u3001\u54C1\u724C\u3001\u5730\u5740\uFF0C\u5E76\u7701\u7565\u7A7A\u5B57\u6BB5\u5206\u9694\u7B26"
);
assert(
  pageSource.includes('width="min(1280px, calc(100vw - 48px))"') && !pageSource.includes("width={980}"),
  "\u4FEE\u590D\u5206\u5E97 GUID \u5F39\u7A97\u5E94\u4F7F\u7528\u66F4\u5BBD\u7684\u54CD\u5E94\u5F0F\u5BBD\u5EA6"
);
assert(
  pageSource.includes("scroll={{ x: 1138, y: 420 }}") && pageSource.includes("width: 520") && pageSource.includes("popupMatchSelectWidth={640}"),
  "\u76EE\u6807\u672C\u5730\u5206\u5E97\u5217\u548C\u4E0B\u62C9\u5E94\u6709\u660E\u786E\u5BBD\u5EA6\uFF0C\u907F\u514D\u54C1\u724C\u5730\u5740\u88AB\u8FC7\u65E9\u622A\u65AD"
);
assert(
  pageSource.includes("classNames={{ popup: { root: 'store-order-unmatched-target-popup' } }}") && compactCssSource.includes(".store-order-unmatched-target-popup .ant-select-item-option-content") && compactCssSource.includes("white-space: nowrap") && compactCssSource.includes("text-overflow: ellipsis"),
  "\u76EE\u6807\u672C\u5730\u5206\u5E97\u4E0B\u62C9\u5E94\u4F7F\u7528\u5C40\u90E8\u6837\u5F0F\u63A7\u5236\u957F\u6587\u672C\u5C55\u793A"
);
assert(
  pageSource.includes("\u5C55\u793A\u54C1\u724C\u548C\u5730\u5740\u7528\u4E8E\u4EBA\u5DE5\u786E\u8BA4 GUID \u5BF9\u5E94\u5206\u5E97") && pageSource.includes("\u4FDD\u5B58\u65F6\u4ECD\u53EA\u63D0\u4EA4\u76EE\u6807 StoreCode"),
  "\u76EE\u6807\u672C\u5730\u5206\u5E97\u9009\u9879\u5E94\u4FDD\u7559\u4E2D\u6587\u6CE8\u91CA\u8BF4\u660E\u5C55\u793A\u4FE1\u606F\u548C\u4FDD\u5B58\u503C\u7684\u8FB9\u754C"
);
var refreshSection = extractSection(
  pageSource,
  "const refreshCurrentList",
  "const loadUnmatchedStoreGroups",
  "\u5F53\u524D\u5217\u8868\u5237\u65B0 helper"
);
var saveMappingsSection = extractSection(
  pageSource,
  "const handleSaveUnmatchedStoreMappings",
  "const updateColumnFilters",
  "\u4FDD\u5B58\u672A\u5339\u914D\u5206\u5E97\u6620\u5C04"
);
assert(
  saveMappingsSection.includes("await Promise.all([refreshCurrentList(), loadBranches(), loadUnmatchedStoreGroups()])") && refreshSection.includes("if (!isMountedRef.current) {") && refreshSection.includes("loadDataRef.current?.(overrides)"),
  "\u4FEE\u590D\u6210\u529F\u540E\u5E94\u5728 mounted gate \u540E\u901A\u8FC7 current loader \u5237\u65B0\u4E3B\u5217\u8868\uFF0C\u5E76\u4FDD\u7559\u5206\u5E97\u7B5B\u9009\u548C\u672A\u5339\u914D\u805A\u5408\u5237\u65B0"
);
assert(
  serviceSource.includes("`${API_BASE}/unmatched-store-groups`") && serviceSource.includes("`${API_BASE}/batch-map-store-code`"),
  "\u524D\u7AEF\u670D\u52A1\u5C42\u5E94\u5C01\u88C5\u672A\u5339\u914D\u5206\u5E97\u805A\u5408\u548C\u6279\u91CF\u6620\u5C04\u63A5\u53E3"
);
console.log("storeOrderUnmatchedStore.logic.test: ok");
