// src/pages/PosAdmin/LocalSupplierPurchaseSalesAnalysis/columnOrder.test.ts
import { readFileSync } from "node:fs";

// src/pages/PosAdmin/LocalSupplierPurchaseSalesAnalysis/columnOrder.ts
var LOCAL_SUPPLIER_PURCHASE_SALES_ANALYSIS_DEFAULT_COLUMN_ORDER = [
  "supplierName",
  "previousPurchaseDate",
  "latestPurchaseDate",
  "purchaseIntervalDays",
  "salesBetweenPurchases",
  "salesQty30",
  "salesQty60",
  "salesQty90",
  "salesStatisticLastUpdate"
];
function mergeLocalSupplierPurchaseSalesAnalysisColumnOrder(savedOrder, availableOrder = LOCAL_SUPPLIER_PURCHASE_SALES_ANALYSIS_DEFAULT_COLUMN_ORDER) {
  const availableSet = new Set(availableOrder);
  const seen = /* @__PURE__ */ new Set();
  const merged = [];
  const savedValues = Array.isArray(savedOrder) ? savedOrder : [];
  for (const value of savedValues) {
    if (typeof value !== "string" || !availableSet.has(value) || seen.has(value)) {
      continue;
    }
    seen.add(value);
    merged.push(value);
  }
  for (const key of availableOrder) {
    if (!seen.has(key)) {
      merged.push(key);
    }
  }
  return merged;
}
function moveLocalSupplierPurchaseSalesAnalysisColumnOrder(currentOrder, activeKey, overKey) {
  if (typeof activeKey !== "string" || typeof overKey !== "string" || activeKey === overKey) {
    return [...currentOrder];
  }
  const fromIndex = currentOrder.indexOf(activeKey);
  const toIndex = currentOrder.indexOf(overKey);
  if (fromIndex < 0 || toIndex < 0) {
    return [...currentOrder];
  }
  const nextOrder = [...currentOrder];
  const [moved] = nextOrder.splice(fromIndex, 1);
  nextOrder.splice(toIndex, 0, moved);
  return nextOrder;
}
function isLocalSupplierPurchaseSalesAnalysisColumnOrderCustomized(currentOrder, defaultOrder2 = LOCAL_SUPPLIER_PURCHASE_SALES_ANALYSIS_DEFAULT_COLUMN_ORDER) {
  if (!currentOrder.length) {
    return false;
  }
  const normalizedOrder = mergeLocalSupplierPurchaseSalesAnalysisColumnOrder(currentOrder, defaultOrder2);
  return normalizedOrder.length !== defaultOrder2.length || normalizedOrder.some((key, index) => key !== defaultOrder2[index]);
}

// src/pages/PosAdmin/LocalSupplierPurchaseSalesAnalysis/columnOrder.test.ts
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}\u3002Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
function assertDeepEqual(actual, expected, message) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${message}\u3002Expected: ${expectedJson}, received: ${actualJson}`);
  }
}
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
var defaultOrder = [
  ...LOCAL_SUPPLIER_PURCHASE_SALES_ANALYSIS_DEFAULT_COLUMN_ORDER
];
var pageSource = readFileSync("src/pages/PosAdmin/LocalSupplierPurchaseSalesAnalysis/index.tsx", "utf8");
assertDeepEqual(
  mergeLocalSupplierPurchaseSalesAnalysisColumnOrder(
    ["salesQty90", "removed", "supplierName", "salesQty90"],
    defaultOrder
  ),
  [
    "salesQty90",
    "supplierName",
    "previousPurchaseDate",
    "latestPurchaseDate",
    "purchaseIntervalDays",
    "salesBetweenPurchases",
    "salesQty30",
    "salesQty60",
    "salesStatisticLastUpdate"
  ],
  "\u5206\u5E97\u8FDB\u8D27\u9500\u91CF\u5206\u6790\u5217\u987A\u5E8F\u5E94\u8FC7\u6EE4\u672A\u77E5\u5217\u3001\u53BB\u91CD\u5E76\u8865\u9F50\u65B0\u589E\u5217"
);
assertDeepEqual(
  mergeLocalSupplierPurchaseSalesAnalysisColumnOrder({ supplierName: true }, defaultOrder),
  defaultOrder,
  "\u5206\u5E97\u8FDB\u8D27\u9500\u91CF\u5206\u6790\u5217\u987A\u5E8F\u9047\u5230\u975E\u6570\u7EC4\u6301\u4E45\u5316\u503C\u65F6\u5E94\u56DE\u9000\u9ED8\u8BA4\u987A\u5E8F"
);
assertDeepEqual(
  moveLocalSupplierPurchaseSalesAnalysisColumnOrder(defaultOrder, "salesQty90", "supplierName"),
  [
    "salesQty90",
    "supplierName",
    "previousPurchaseDate",
    "latestPurchaseDate",
    "purchaseIntervalDays",
    "salesBetweenPurchases",
    "salesQty30",
    "salesQty60",
    "salesStatisticLastUpdate"
  ],
  "\u5206\u5E97\u8FDB\u8D27\u9500\u91CF\u5206\u6790\u5217\u62D6\u62FD\u5E94\u628A active \u5217\u79FB\u52A8\u5230 over \u5217\u4F4D\u7F6E"
);
assertDeepEqual(
  moveLocalSupplierPurchaseSalesAnalysisColumnOrder(defaultOrder, "missing", "supplierName"),
  defaultOrder,
  "\u5206\u5E97\u8FDB\u8D27\u9500\u91CF\u5206\u6790\u5217\u62D6\u62FD\u9047\u5230\u672A\u77E5 active \u5217\u65F6\u5E94\u4FDD\u6301\u539F\u987A\u5E8F"
);
assertDeepEqual(
  moveLocalSupplierPurchaseSalesAnalysisColumnOrder(defaultOrder, "salesQty90", "missing"),
  defaultOrder,
  "\u5206\u5E97\u8FDB\u8D27\u9500\u91CF\u5206\u6790\u5217\u62D6\u62FD\u9047\u5230\u672A\u77E5 over \u5217\u65F6\u5E94\u4FDD\u6301\u539F\u987A\u5E8F"
);
assertDeepEqual(
  moveLocalSupplierPurchaseSalesAnalysisColumnOrder(defaultOrder, "supplierName", "supplierName"),
  defaultOrder,
  "\u5206\u5E97\u8FDB\u8D27\u9500\u91CF\u5206\u6790\u5217\u62D6\u62FD active \u4E0E over \u76F8\u540C\u65F6\u5E94\u4FDD\u6301\u539F\u987A\u5E8F"
);
assertEqual(
  isLocalSupplierPurchaseSalesAnalysisColumnOrderCustomized(defaultOrder, defaultOrder),
  false,
  "\u5206\u5E97\u8FDB\u8D27\u9500\u91CF\u5206\u6790\u9ED8\u8BA4\u5217\u987A\u5E8F\u4E0D\u5E94\u5224\u5B9A\u4E3A\u5DF2\u81EA\u5B9A\u4E49"
);
assertEqual(
  isLocalSupplierPurchaseSalesAnalysisColumnOrderCustomized(
    moveLocalSupplierPurchaseSalesAnalysisColumnOrder(defaultOrder, "salesQty90", "supplierName"),
    defaultOrder
  ),
  true,
  "\u5206\u5E97\u8FDB\u8D27\u9500\u91CF\u5206\u6790\u62D6\u62FD\u5217\u987A\u5E8F\u540E\u5E94\u5224\u5B9A\u4E3A\u5DF2\u81EA\u5B9A\u4E49"
);
assertEqual(
  isLocalSupplierPurchaseSalesAnalysisColumnOrderCustomized([], defaultOrder),
  false,
  "\u5206\u5E97\u8FDB\u8D27\u9500\u91CF\u5206\u6790\u5217\u987A\u5E8F\u521D\u59CB\u5316\u4E3A\u7A7A\u65F6\u4E0D\u5E94\u8BEF\u5224\u4E3A\u5DF2\u81EA\u5B9A\u4E49"
);
assertEqual(
  defaultOrder.includes("image"),
  false,
  "\u5206\u5E97\u8FDB\u8D27\u9500\u91CF\u5206\u6790\u9ED8\u8BA4\u62D6\u62FD\u5217\u4E0D\u5E94\u5305\u542B\u56FE\u7247\u56FA\u5B9A\u5217"
);
assertEqual(
  defaultOrder.includes("itemNumber"),
  false,
  "\u5206\u5E97\u8FDB\u8D27\u9500\u91CF\u5206\u6790\u9ED8\u8BA4\u62D6\u62FD\u5217\u4E0D\u5E94\u5305\u542B\u8D27\u53F7\u540D\u79F0\u56FA\u5B9A\u5217"
);
assertEqual(
  defaultOrder.indexOf("previousPurchaseDate") < defaultOrder.indexOf("latestPurchaseDate"),
  true,
  "\u5206\u5E97\u8FDB\u8D27\u9500\u91CF\u5206\u6790\u9ED8\u8BA4\u62D6\u62FD\u5217\u4E2D\u4E0A\u6B21\u8FDB\u8D27\u5E94\u5728\u6700\u8FD1\u8FDB\u8D27\u524D"
);
assert(
  pageSource.includes("DndContext") && pageSource.includes("SortableContext") && pageSource.includes("useSortable") && pageSource.includes("horizontalListSortingStrategy"),
  "\u5206\u5E97\u8FDB\u8D27\u9500\u91CF\u5206\u6790\u8868\u5934\u62D6\u62FD\u5E94\u590D\u7528 @dnd-kit \u6A2A\u5411\u6392\u5E8F\u80FD\u529B"
);
assert(
  pageSource.includes("const LOCAL_SUPPLIER_PURCHASE_SALES_ANALYSIS_COLUMN_ORDER_STORAGE_KEY =") && pageSource.includes("hbweb_rv.localSupplierPurchaseSalesAnalysis.columnOrder.v1") && pageSource.includes("localStorage.setItem(") && pageSource.includes("localStorage.removeItem(") && pageSource.includes("mergeLocalSupplierPurchaseSalesAnalysisColumnOrder("),
  "\u5206\u5E97\u8FDB\u8D27\u9500\u91CF\u5206\u6790\u5217\u987A\u5E8F\u5E94\u4FDD\u5B58\u5230\u72EC\u7ACB localStorage key\uFF0C\u5E76\u517C\u5BB9\u5217\u589E\u5220"
);
assert(
  pageSource.includes("const STATIC_PURCHASE_SALES_ANALYSIS_COLUMN_KEYS = new Set(['image', 'itemNumber'])") && pageSource.includes("const fixedColumns = baseColumns.filter((column) =>") && pageSource.includes("STATIC_PURCHASE_SALES_ANALYSIS_COLUMN_KEYS.has(String(column.key))") && pageSource.includes("'data-column-key': String(column.key)") && pageSource.includes("return [...fixedColumns, ...draggableColumns]"),
  "\u5206\u5E97\u8FDB\u8D27\u9500\u91CF\u5206\u6790\u56FA\u5B9A\u5DE6\u5217\u4E0D\u5E94\u8FDB\u5165\u62D6\u62FD\u5217\u987A\u5E8F"
);
assert(
  pageSource.includes("components={{ header: { cell: DraggableHeaderCell } }}") && pageSource.includes("items={columnOrder.length ? columnOrder : draggableColumnKeys}") && pageSource.includes("activationConstraint:") && pageSource.includes("distance: 6"),
  "\u5206\u5E97\u8FDB\u8D27\u9500\u91CF\u5206\u6790\u8868\u683C\u5E94\u63A5\u5165\u53EF\u62D6\u62FD\u8868\u5934 cell\uFF0C\u5E76\u8BBE\u7F6E\u62D6\u62FD\u8DDD\u79BB\u907F\u514D\u8BEF\u89E6\u6392\u5E8F"
);
assert(
  pageSource.includes("catch {") && pageSource.includes("localStorage \u4E0D\u53EF\u7528\u65F6\u4E0D\u5F71\u54CD\u5F53\u524D\u9875\u9762\u5185\u62D6\u62FD\u6392\u5E8F\u3002") && pageSource.includes("localStorage \u4E0D\u53EF\u7528\u65F6\u4ECD\u6062\u590D\u5F53\u524D\u9875\u9762\u5185\u7684\u9ED8\u8BA4\u5217\u987A\u5E8F\u3002"),
  "\u5206\u5E97\u8FDB\u8D27\u9500\u91CF\u5206\u6790 localStorage \u5931\u8D25\u65F6\u4E0D\u5E94\u963B\u65AD\u62D6\u62FD\u6216\u91CD\u7F6E\u5217"
);
assert(
  pageSource.includes("const handleTableChange = (") && pageSource.includes("onChange={handleTableChange}") && pageSource.includes("\u5217\u5934\u6392\u5E8F\u5FC5\u987B\u900F\u4F20\u5230\u540E\u7AEF\uFF0C\u4E0D\u80FD\u53EA\u5728\u5F53\u524D\u9875\u672C\u5730\u6392\u5E8F\u3002"),
  "\u5206\u5E97\u8FDB\u8D27\u9500\u91CF\u5206\u6790\u5217\u5934\u62D6\u62FD\u4E0D\u80FD\u79FB\u9664\u65E2\u6709\u670D\u52A1\u7AEF\u6392\u5E8F\u56DE\u8C03"
);
console.log("localSupplierPurchaseSalesAnalysis.columnOrder.test: ok");
