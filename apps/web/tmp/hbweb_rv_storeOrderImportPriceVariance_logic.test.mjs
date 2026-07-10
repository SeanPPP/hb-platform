// src/pages/Warehouse/StoreOrderImportPriceVariance/storeOrderImportPriceVariance.logic.test.ts
import { readFileSync } from "node:fs";
import path from "node:path";
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
var pageSource = readFileSync(path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrderImportPriceVariance/index.tsx"), "utf8");
var routeSource = readFileSync(path.resolve(process.cwd(), "src/router/routes.tsx"), "utf8");
var zhLocale = JSON.parse(readFileSync(path.resolve(process.cwd(), "src/i18n/locales/zh.json"), "utf8"));
var enLocale = JSON.parse(readFileSync(path.resolve(process.cwd(), "src/i18n/locales/en.json"), "utf8"));
assert(
  pageSource.includes("dataIndex: 'productImage'") && pageSource.includes("dataIndex: 'domesticPrice'") && pageSource.includes("dataIndex: 'unitVolume'") && pageSource.includes("dataIndex: 'packingQuantity'") && pageSource.includes("dataIndex: 'warehouseImportPrice'") && pageSource.includes("dataIndex: 'firstContainerImportPrice'") && pageSource.includes("dataIndex: 'originalImportAmountTotal'") && pageSource.includes("dataIndex: 'baselineImportAmountTotal'") && pageSource.includes("dataIndex: 'varianceAmountTotal'"),
  "\u5546\u54C1\u6C47\u603B\u4E3B\u8868\u5FC5\u987B\u5305\u542B\u5546\u54C1\u56FE\u7247\u3001\u56FD\u5185\u4EF7\u683C\u3001\u4F53\u79EF\u3001\u88C5\u7BB1\u6570\u3001\u5F53\u524D\u4ED3\u5E93\u8FDB\u8D27\u4EF7\u683C\u3001\u9996\u6B21\u8FDB\u8D27\u4EF7\u548C\u4E09\u9879\u91D1\u989D\u5408\u8BA1\u5217"
);
var editablePriceBlockStart = pageSource.indexOf("const renderEditablePriceCell");
var editablePriceBlockEnd = pageSource.indexOf("const openBatchWarehouseImportPriceModal");
var editablePriceBlock = pageSource.slice(editablePriceBlockStart, editablePriceBlockEnd);
assert(
  pageSource.includes("updateStoreOrderImportPriceVarianceDomesticPrice") && pageSource.includes("updateStoreOrderImportPriceVarianceWarehouseImportPrice") && pageSource.includes("function parsePriceDraft") && pageSource.includes("type EditablePriceField = 'domesticPrice' | 'warehouseImportPrice'") && pageSource.includes("const priceInputRefs = useRef") && pageSource.includes("const savingPriceKeyRef = useRef") && pageSource.includes("savingPriceKeyRef.current === key") && pageSource.includes('inputMode="decimal"') && pageSource.includes("event.currentTarget.select()") && pageSource.includes("event.key === 'ArrowUp'") && pageSource.includes("event.key === 'ArrowDown'") && pageSource.includes("event.key === 'Enter'") && pageSource.includes("event.key === 'Escape'") && editablePriceBlockStart >= 0 && editablePriceBlockEnd > editablePriceBlockStart && !editablePriceBlock.includes("<InputNumber") && !pageSource.includes('type="number"'),
  "\u56FD\u5185\u4EF7\u683C\u548C\u5F53\u524D\u4ED3\u5E93\u8FDB\u8D27\u4EF7\u683C\u5217\u5FC5\u987B\u4F7F\u7528\u666E\u901A Input \u5185\u8054\u7F16\u8F91\uFF0C\u652F\u6301\u5168\u9009\u3001\u65B9\u5411\u952E\u3001\u56DE\u8F66\u4FDD\u5B58\u3001Esc \u53D6\u6D88\uFF0C\u4E14\u4E0D\u80FD\u51FA\u73B0\u6570\u5B57\u52A0\u51CF\u63A7\u4EF6"
);
var warehouseImportPriceColumnIndex = pageSource.indexOf("dataIndex: 'warehouseImportPrice'");
var firstContainerImportPriceColumnIndex = pageSource.indexOf("dataIndex: 'firstContainerImportPrice'");
assert(
  warehouseImportPriceColumnIndex >= 0 && firstContainerImportPriceColumnIndex > warehouseImportPriceColumnIndex,
  "\u5F53\u524D\u4ED3\u5E93\u8FDB\u8D27\u4EF7\u683C\u5217\u5FC5\u987B\u4F4D\u4E8E\u9996\u6B21\u8D27\u67DC\u4EF7\u5217\u524D\u9762"
);
assert(
  pageSource.includes("const [selectedRowKeys, setSelectedRowKeys] = useState<Key[]>([])") && pageSource.includes("rowSelection={{") && pageSource.includes("selectedRowKeys,") && pageSource.includes("preserveSelectedRowKeys: true") && pageSource.includes("getCheckboxProps: (row) => ({ disabled: !row.productCode })") && pageSource.includes("openBatchWarehouseImportPriceModal") && pageSource.includes("handleBatchWarehouseImportPriceSave") && pageSource.includes("batchUpdateStoreOrderImportPriceVarianceWarehouseImportPrice({") && pageSource.includes("productCodes,") && pageSource.includes("warehouseImportPrice: values.warehouseImportPrice ?? 0") && pageSource.includes("setSelectedRowKeys([])") && pageSource.includes("await loadData()") && pageSource.includes("title={t('storeOrders.importPriceVariance.batchWarehouseImportPriceTitle'") && pageSource.includes("<InputNumber") && pageSource.includes("\u6279\u91CF\u4FEE\u6539\u53EA\u63D0\u4EA4\u5546\u54C1\u7F16\u7801\u548C\u7EDF\u4E00\u7684\u65B0\u5F53\u524D\u53C2\u8003\u8FDB\u8D27\u4EF7"),
  "\u5546\u54C1\u6C47\u603B\u4E3B\u8868\u5FC5\u987B\u652F\u6301\u52FE\u9009\u5546\u54C1\u540E\u6279\u91CF\u4FEE\u6539\u5F53\u524D\u53C2\u8003\u8FDB\u8D27\u4EF7\uFF0C\u6210\u529F\u540E\u6E05\u7A7A\u9009\u62E9\u5E76\u5237\u65B0\u7EDF\u8BA1\u7ED3\u679C"
);
assert(
  pageSource.includes("dataIndex: 'supplierCode'") && pageSource.includes("t('storeOrders.importPriceVariance.domesticSupplier')") && pageSource.includes('name="supplierCode"'),
  "\u9875\u9762\u5FC5\u987B\u5305\u542B\u56FD\u5185\u4F9B\u5E94\u5546\u7B5B\u9009\u7EC4\u4EF6\u548C\u56FD\u5185\u4F9B\u5E94\u5546\u5217"
);
assert(
  pageSource.includes("import { getActiveChinaSuppliers }") && pageSource.includes("function DomesticSupplierFilterSelect") && pageSource.includes("getActiveChinaSuppliers(currentController.signal)") && pageSource.includes("onOpenChange={handleSupplierOpenChange}"),
  "\u56FD\u5185\u4F9B\u5E94\u5546\u8FC7\u6EE4\u7EC4\u4EF6\u5FC5\u987B\u590D\u7528 getActiveChinaSuppliers \u5E76\u5728\u9996\u6B21\u5C55\u5F00\u65F6\u52A0\u8F7D"
);
assert(
  pageSource.includes("const DEFAULT_PAGE_SIZE = 20") && pageSource.includes("const DEFAULT_SORT_BY = 'absoluteVarianceAmount'") && pageSource.includes("const DEFAULT_SORT_DESCENDING = true"),
  "\u9875\u9762\u9ED8\u8BA4\u5206\u9875\u548C\u6392\u5E8F\u5FC5\u987B\u7B26\u5408\u540E\u7AEF\u7EDF\u8BA1\u9875\u5951\u7EA6"
);
assert(
  pageSource.includes("dataIndex: 'varianceAmountTotal'") && pageSource.includes("key: 'varianceAmountTotal'") && pageSource.includes("const DEFAULT_SORT_BY = 'absoluteVarianceAmount'"),
  "\u5546\u54C1\u6C47\u603B\u5DEE\u989D\u5408\u8BA1\u5217\u70B9\u51FB\u6392\u5E8F\u5FC5\u987B\u53D1\u9001\u6709\u7B26\u53F7 varianceAmountTotal\uFF0C\u9ED8\u8BA4\u9996\u5C4F\u624D\u4F7F\u7528\u7EDD\u5BF9\u5DEE\u989D\u6392\u5E8F"
);
assert(
  pageSource.includes("getStoreOrderImportPriceVariance(query)") && pageSource.includes("onChange={handleTableChange}"),
  "\u4E3B\u8868\u5FC5\u987B\u901A\u8FC7\u670D\u52A1\u7AEF\u63A5\u53E3\u52A0\u8F7D\u5E76\u54CD\u5E94\u8868\u683C\u5206\u9875\u6392\u5E8F"
);
assert(
  pageSource.includes("const [supplierSummaries, setSupplierSummaries]") && pageSource.includes("setSupplierSummaries(result.supplierSummaries)") && pageSource.includes("const supplierSummaryColumns") && pageSource.includes("<Table<StoreOrderImportPriceVarianceSupplierSummary>") && pageSource.includes("supplierVarianceRankingTitle") && pageSource.includes("noSupplierVarianceData") && pageSource.includes("dataIndex: 'increaseVarianceAmountTotal'") && pageSource.includes("dataIndex: 'decreaseVarianceAmountTotal'") && pageSource.includes("defaultPageSize: 50") && pageSource.includes("pageSizeOptions: [20, 50, 100]") && pageSource.includes("compareSupplierText") && pageSource.includes("compareSupplierNumber") && pageSource.includes("sorter: (left, right)") && pageSource.includes("const supplierSummaryRegionRef = useRef<HTMLDivElement | null>(null)") && pageSource.includes("const [supplierSummaryTableScrollY, setSupplierSummaryTableScrollY]") && pageSource.includes("maxHeight: 'calc(100vh - 32px)'") && pageSource.includes("scroll={{ x: 1120, y: supplierSummaryTableScrollY }}") && !pageSource.includes("result.supplierSummaries.slice(0, 10)") && !pageSource.includes("SUPPLIER_SUMMARY_PLACEHOLDER_COUNT"),
  "\u9875\u9762\u5FC5\u987B\u7528\u5355\u5F20\u4E00\u5C4F\u5185\u53EF\u6EDA\u52A8\u3001\u53EF\u6392\u5E8F\u7684\u8868\u683C\u5C55\u793A\u6240\u6709\u56FD\u5185\u4F9B\u5E94\u5546\u5DEE\u989D\u7EDF\u8BA1\uFF0C\u5E76\u9ED8\u8BA4\u6BCF\u9875 50 \u6761"
);
assert(
  pageSource.includes("useLayoutEffect") && pageSource.includes("const tableRegionRef = useRef<HTMLDivElement | null>(null)") && pageSource.includes("const [tableScrollY, setTableScrollY]") && pageSource.includes("height: 'calc(100vh - 32px)'") && pageSource.includes("region.clientHeight") && pageSource.includes("scroll={{ x: 2000, y: tableScrollY }}") && pageSource.includes("\u4E3B\u8868\u548C\u4F9B\u5E94\u5546\u7EDF\u8BA1\u90FD\u628A\u6EDA\u52A8\u9650\u5236\u5728\u8868\u683C body \u5185") && pageSource.includes("overflow: 'hidden'"),
  "\u4E3B\u8868\u533A\u57DF\u5FC5\u987B\u6309\u4E00\u5C4F\u9AD8\u5EA6\u5C55\u793A\uFF0C\u5E76\u6839\u636E\u8868\u683C\u533A\u57DF\u81EA\u8EAB\u9AD8\u5EA6\u8BA1\u7B97 body \u5185\u90E8\u6EDA\u52A8\u9AD8\u5EA6"
);
assert(
  pageSource.includes("getStoreOrderImportPriceVarianceDetails({") && pageSource.includes("productCode: selectedProduct.productCode") && pageSource.includes("<Modal") && pageSource.includes("onChange={handleDetailTableChange}"),
  "\u70B9\u51FB\u5546\u54C1\u8BA2\u5355\u660E\u7EC6\u5FC5\u987B\u6253\u5F00\u5F39\u7A97\u5E76\u901A\u8FC7 details \u63A5\u53E3\u670D\u52A1\u7AEF\u5206\u9875\u52A0\u8F7D"
);
assert(
  pageSource.includes("...filters") && pageSource.includes("supplierCode: trimText(values.supplierCode)"),
  "\u4E3B\u8868\u7B5B\u9009\u548C\u5F39\u7A97\u660E\u7EC6\u5FC5\u987B\u5171\u4EAB\u5F53\u524D\u7B5B\u9009\u6761\u4EF6\uFF0C\u5305\u62EC\u56FD\u5185\u4F9B\u5E94\u5546"
);
assert(
  pageSource.includes("import { useNavigate } from 'react-router-dom'") && pageSource.includes("const navigate = useNavigate()"),
  "\u9875\u9762\u5FC5\u987B\u4F7F\u7528 useNavigate \u6253\u5F00\u8BA2\u5355\u548C\u8D27\u67DC\u660E\u7EC6\u9875"
);
assert(
  pageSource.includes("navigate(`/warehouse/store-order/detail/${row.orderGUID}`, {") && pageSource.includes("state: { orderNo: row.orderNo }"),
  "\u5F39\u7A97\u8BA2\u5355\u53F7\u5217\u5FC5\u987B\u8DF3\u8F6C\u5230\u5BF9\u5E94\u8BA2\u8D27\u660E\u7EC6\u5E76\u4F20\u5165\u8BA2\u5355\u53F7\u4F5C\u4E3A\u8BE6\u60C5\u9875\u521D\u59CB\u6807\u9898"
);
assert(
  pageSource.includes("navigate(`/warehouse/container/detail/${row.firstContainerCode}`)"),
  "\u9996\u6B21\u8D27\u67DC\u7F16\u53F7\u5217\u5FC5\u987B\u8DF3\u8F6C\u5230\u5BF9\u5E94\u8D27\u67DC\u660E\u7EC6\u9875"
);
var routeStart = routeSource.indexOf("path: '/warehouse/store-order-import-price-variance'");
var routeEnd = routeSource.indexOf("path: '/warehouse/store-order/detail/:id'", routeStart);
var routeBlock = routeSource.slice(routeStart, routeEnd);
assert(routeStart >= 0 && routeEnd > routeStart, "\u8DEF\u7531\u5FC5\u987B\u6CE8\u518C\u9996\u6B21\u8D27\u67DC\u4EF7\u5DEE\u5F02\u7EDF\u8BA1\u9875");
assert(routeBlock.includes("title: 'menu.storeOrderImportPriceVariance'"), "\u8DEF\u7531\u6807\u9898 key \u5FC5\u987B\u7B26\u5408\u83DC\u5355\u5951\u7EA6");
assert(routeBlock.includes("icon: 'BarChartOutlined'"), "\u8DEF\u7531\u56FE\u6807\u5E94\u4F7F\u7528 BarChartOutlined");
assert(
  routeBlock.includes("accessKey: 'canManageStoreOrderImportPriceVariance'"),
  "\u8DEF\u7531\u6743\u9650\u5FC5\u987B\u6536\u675F\u5230\u9996\u67DC\u4EF7\u5DEE\u5F02\u4E13\u7528\u4ED3\u5E93\u7BA1\u7406\u5458\u6743\u9650"
);
var fallbackStart = routeSource.indexOf("function buildWarehouseStaffMenus");
var fallbackEnd = routeSource.indexOf("export function buildMenus", fallbackStart);
var fallbackBlock = routeSource.slice(fallbackStart, fallbackEnd);
assert(
  fallbackBlock.includes("key: '/warehouse/store-orders'") && !fallbackBlock.includes("key: '/warehouse/store-order-import-price-variance'"),
  "\u4ED3\u5E93\u5458\u5DE5 fallback \u83DC\u5355\u53EA\u80FD\u4FDD\u7559\u5206\u5E97\u8BA2\u8D27\u5217\u8868\uFF0C\u4E0D\u80FD\u66B4\u9732\u9996\u67DC\u4EF7\u5DEE\u5F02\u7EDF\u8BA1\u9875"
);
assert(
  zhLocale.menu.storeOrderImportPriceVariance === "\u9996\u6B21\u8D27\u67DC\u4EF7\u5DEE\u5F02\u7EDF\u8BA1" && enLocale.menu.storeOrderImportPriceVariance === "First Container Price Variance",
  "\u4E2D\u82F1\u6587\u83DC\u5355\u6587\u6848\u5FC5\u987B\u5B58\u5728"
);
assert(
  zhLocale.storeOrders.importPriceVariance.originalImportAmount === "\u539F\u59CB\u91D1\u989D" && zhLocale.storeOrders.importPriceVariance.baselineImportAmount === "\u57FA\u51C6\u91D1\u989D" && zhLocale.storeOrders.importPriceVariance.varianceAmount === "\u5DEE\u989D",
  "\u4E2D\u6587\u7EDF\u8BA1\u9875\u6838\u5FC3\u660E\u7EC6\u5217\u6587\u6848\u5FC5\u987B\u81EA\u7136\u53EF\u8BFB"
);
assert(
  zhLocale.storeOrders.importPriceVariance.domesticSupplier === "\u56FD\u5185\u4F9B\u5E94\u5546" && zhLocale.storeOrders.importPriceVariance.productImage === "\u5546\u54C1\u56FE\u7247" && zhLocale.storeOrders.importPriceVariance.domesticPrice === "\u56FD\u5185\u4EF7\u683C" && zhLocale.storeOrders.importPriceVariance.warehouseImportPrice === "\u5F53\u524D\u4ED3\u5E93\u8FDB\u8D27\u4EF7\u683C" && enLocale.storeOrders.importPriceVariance.warehouseImportPrice === "Current Warehouse Import Price" && zhLocale.storeOrders.importPriceVariance.unitVolume === "\u4F53\u79EF" && zhLocale.storeOrders.importPriceVariance.packingQuantity === "\u88C5\u7BB1\u6570",
  "\u4E2D\u6587\u5546\u54C1\u6C47\u603B\u5217\u6587\u6848\u5FC5\u987B\u5B58\u5728"
);
assert(
  zhLocale.storeOrders.importPriceVariance.batchWarehouseImportPrice === "\u6279\u91CF\u4FEE\u6539\u5F53\u524D\u53C2\u8003\u8FDB\u8D27\u4EF7" && zhLocale.storeOrders.importPriceVariance.batchWarehouseImportPriceTitle === "\u6279\u91CF\u4FEE\u6539\u5F53\u524D\u53C2\u8003\u8FDB\u8D27\u4EF7 ({{count}} \u4E2A\u5546\u54C1)" && zhLocale.storeOrders.importPriceVariance.batchSaveWarehouseImportPriceSuccess === "\u5DF2\u6279\u91CF\u4FDD\u5B58 {{count}} \u4E2A\u5546\u54C1\u7684\u4ED3\u5E93\u8FDB\u8D27\u4EF7\u683C" && enLocale.storeOrders.importPriceVariance.batchWarehouseImportPrice === "Batch update reference import price" && enLocale.storeOrders.importPriceVariance.batchWarehouseImportPriceTitle === "Batch Update Reference Import Price ({{count}} products)" && enLocale.storeOrders.importPriceVariance.batchSaveWarehouseImportPriceSuccess === "Saved warehouse import price for {{count}} products",
  "\u6279\u91CF\u4FEE\u6539\u5F53\u524D\u53C2\u8003\u8FDB\u8D27\u4EF7\u7684\u4E2D\u82F1\u6587\u6309\u94AE\u3001\u6807\u9898\u548C\u6210\u529F\u6587\u6848\u5FC5\u987B\u5B58\u5728"
);
assert(
  zhLocale.storeOrders.importPriceVariance.directionIncrease === "\u591A\u6536" && zhLocale.storeOrders.importPriceVariance.directionDecrease === "\u5C11\u6536" && enLocale.storeOrders.importPriceVariance.directionIncrease === "Overcharged" && enLocale.storeOrders.importPriceVariance.directionDecrease === "Undercharged",
  "\u5DEE\u989D\u65B9\u5411\u6587\u6848\u5FC5\u987B\u8868\u8FBE\u8BA2\u5355\u8FDB\u8D27\u4EF7\u76F8\u5BF9\u9996\u6B21\u8D27\u67DC\u4EF7\u7684\u591A\u6536/\u5C11\u6536\u8BED\u4E49"
);
assert(
  zhLocale.storeOrders.importPriceVariance.supplierVarianceRankingTitle === "\u56FD\u5185\u4F9B\u5E94\u5546\u5DEE\u989D\u7EDF\u8BA1" && zhLocale.storeOrders.importPriceVariance.increaseVarianceAmountTotal === "\u591A\u6536\u5408\u8BA1" && zhLocale.storeOrders.importPriceVariance.decreaseVarianceAmountTotal === "\u5C11\u6536\u5408\u8BA1" && zhLocale.storeOrders.importPriceVariance.productCount === "\u5546\u54C1\u6570" && zhLocale.storeOrders.importPriceVariance.detailCount === "\u660E\u7EC6\u6570" && zhLocale.storeOrders.importPriceVariance.noSupplierVarianceData === "\u6682\u65E0\u4F9B\u5E94\u5546\u5DEE\u989D\u6570\u636E" && zhLocale.storeOrders.importPriceVariance.totalSuppliers === "\u5171 {{total}} \u4E2A\u4F9B\u5E94\u5546" && enLocale.storeOrders.importPriceVariance.supplierVarianceRankingTitle === "Domestic Supplier Variance" && enLocale.storeOrders.importPriceVariance.increaseVarianceAmountTotal === "Overcharged Total" && enLocale.storeOrders.importPriceVariance.decreaseVarianceAmountTotal === "Undercharged Total" && enLocale.storeOrders.importPriceVariance.productCount === "Products" && enLocale.storeOrders.importPriceVariance.detailCount === "Details" && enLocale.storeOrders.importPriceVariance.noSupplierVarianceData === "No supplier variance data" && enLocale.storeOrders.importPriceVariance.totalSuppliers === "{{total}} suppliers",
  "\u4E2D\u82F1\u6587\u4F9B\u5E94\u5546\u5DEE\u989D\u7EDF\u8BA1\u8868\u683C\u6587\u6848\u5FC5\u987B\u5B58\u5728"
);
console.log("storeOrderImportPriceVariance.logic.test: ok");
