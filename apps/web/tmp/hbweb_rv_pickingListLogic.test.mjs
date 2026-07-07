// src/pages/Warehouse/StoreOrders/pickingListLogic.test.ts
import fs from "node:fs";
import path from "node:path";

// src/pages/Warehouse/StoreOrders/volumeFormat.ts
function formatStoreOrderVolume(value) {
  if (value === void 0 || value === null) {
    return "--";
  }
  return value.toFixed(2);
}

// src/pages/Warehouse/StoreOrders/pickingListLogic.ts
var DEFAULT_PDF_PAGINATION_OPTIONS = {
  pageHeightMm: 297,
  pagePaddingTopMm: 6,
  pagePaddingBottomMm: 12,
  headerHeightMm: 20,
  tableHeaderHeightMm: 10,
  footerHeightMm: 12,
  // 默认行高与打印 CSS 的 9mm 明细行保持一致，避免 PDF 预分页后在行中间切断。
  rowHeightMm: 9,
  finalSummaryHeightMm: 22
};
function formatExcelCurrency(value) {
  return Number(Number(value ?? 0).toFixed(2));
}
function resolvePickingListRowsPerPdfPage(options, shouldReserveSummarySpace) {
  const summaryHeight = shouldReserveSummarySpace ? options.finalSummaryHeightMm : 0;
  const availableHeight = options.pageHeightMm - options.pagePaddingTopMm - options.pagePaddingBottomMm - options.headerHeightMm - options.tableHeaderHeightMm - options.footerHeightMm - summaryHeight;
  return Math.max(1, Math.floor(availableHeight / options.rowHeightMm));
}
function resolveRowsBeforeSummaryPage(remaining, regularRowsPerPage, summaryRowsPerPage, shortSummaryTailRows) {
  const rowsBeforeSummaryPage = Math.min(regularRowsPerPage, Math.max(1, remaining - 1));
  const summaryRows = remaining - rowsBeforeSummaryPage;
  const maxMergedSummaryRows = summaryRowsPerPage + shortSummaryTailRows;
  return summaryRows <= shortSummaryTailRows && remaining <= maxMergedSummaryRows ? remaining : rowsBeforeSummaryPage;
}
function resolvePickingDisplayQuantity(quantity, allocQuantity) {
  const numericQuantity = Number(quantity);
  if (Number.isFinite(numericQuantity) && numericQuantity > 0) {
    return numericQuantity;
  }
  const numericAllocQuantity = Number(allocQuantity);
  return Number.isFinite(numericAllocQuantity) && numericAllocQuantity > 0 ? numericAllocQuantity : null;
}
function formatInnerPackCount(quantity, allocQuantity, minOrderQuantity) {
  if (typeof minOrderQuantity !== "number" || !Number.isFinite(minOrderQuantity) || minOrderQuantity <= 1) {
    return "";
  }
  const displayQuantity = resolvePickingDisplayQuantity(quantity, allocQuantity);
  if (displayQuantity === null) {
    return "";
  }
  const innerPackCount = displayQuantity / minOrderQuantity;
  if (!Number.isFinite(innerPackCount)) {
    return "";
  }
  return Number.isInteger(innerPackCount) ? String(innerPackCount) : innerPackCount.toFixed(1);
}
function formatPickingOrderQuantity(quantity, allocQuantity) {
  return resolvePickingDisplayQuantity(quantity, allocQuantity) ?? "";
}
function buildPickingListExcelData(order, items, texts, meta = {
  orderNoText: order.orderNo || order.orderGUID || "",
  storeText: order.storeCode || "",
  orderDateText: order.orderDate || "",
  printTimeText: "",
  totalOrderVolumeText: typeof order.totalOrderVolume === "number" ? formatStoreOrderVolume(order.totalOrderVolume) : typeof order.totalVolume === "number" ? formatStoreOrderVolume(order.totalVolume) : "--"
}) {
  return {
    sheetName: texts.sheetName,
    overviewRows: [
      [texts.orderNoLabel, meta.orderNoText],
      [texts.storeLabel, meta.storeText],
      [texts.orderDateLabel, meta.orderDateText],
      [texts.printTimeLabel, meta.printTimeText]
    ],
    detailHeader: [
      texts.detailHeaders.index,
      texts.detailHeaders.itemNumber,
      texts.detailHeaders.location,
      texts.detailHeaders.productName,
      texts.detailHeaders.importPrice,
      texts.detailHeaders.rrp,
      texts.detailHeaders.innerPackCount,
      texts.detailHeaders.orderQuantity
    ],
    detailRows: items.map((item, index) => [
      index + 1,
      item.itemNumber || "",
      item.locationCode || "",
      item.productName || "",
      formatExcelCurrency(item.importPrice),
      item.rrp === void 0 || item.rrp === null ? "" : formatExcelCurrency(item.rrp),
      formatInnerPackCount(item.quantity, item.allocQuantity, item.minOrderQuantity),
      formatPickingOrderQuantity(item.quantity, item.allocQuantity)
    ]),
    remarksRow: order.remarks ? [texts.remarksLabel, order.remarks] : void 0,
    totalRows: [
      [texts.totalSKULabel, order.totalSKU ?? items.length],
      [texts.totalOrderQtyLabel, order.totalQuantity],
      [texts.totalShipQtyLabel, order.totalAllocQuantity ?? 0],
      [texts.totalOrderVolumeLabel, meta.totalOrderVolumeText]
    ]
  };
}
function buildPickingListPdfPages(items, hasSummary, paginationOptions = {}) {
  const options = {
    ...DEFAULT_PDF_PAGINATION_OPTIONS,
    ...paginationOptions
  };
  const regularRowsPerPage = resolvePickingListRowsPerPdfPage(options, false);
  const summaryRowsPerPage = resolvePickingListRowsPerPdfPage(options, hasSummary);
  const shortSummaryTailRows = 2;
  const pages = [];
  let startIndex = 0;
  while (startIndex < items.length) {
    const remaining = items.length - startIndex;
    const canFitRestWithSummary = remaining <= summaryRowsPerPage;
    const shouldBalanceTailPages = hasSummary && !canFitRestWithSummary && remaining <= regularRowsPerPage + summaryRowsPerPage;
    const rowsForPage = canFitRestWithSummary ? summaryRowsPerPage : shouldBalanceTailPages ? resolveRowsBeforeSummaryPage(remaining, regularRowsPerPage, summaryRowsPerPage, shortSummaryTailRows) : regularRowsPerPage;
    const pageItems = items.slice(startIndex, startIndex + rowsForPage);
    pages.push({
      items: pageItems,
      startIndex,
      hasHeader: true,
      footerKind: "pageNumber",
      showSummary: hasSummary && canFitRestWithSummary
    });
    startIndex += pageItems.length;
  }
  if (pages.length === 0) {
    pages.push({
      items: [],
      startIndex: 0,
      hasHeader: true,
      footerKind: "pageNumber",
      showSummary: hasSummary
    });
  }
  pages.forEach((page, pageIndex) => {
    page.showSummary = hasSummary && pageIndex === pages.length - 1;
  });
  return pages;
}

// src/pages/Warehouse/StoreOrders/pickingListLogic.test.ts
function assertEqual(actual, expected, label) {
  if (actual !== expected) {
    throw new Error(`${label}\u3002Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
function assertDeepEqual(actual, expected, label) {
  const actualText = JSON.stringify(actual);
  const expectedText = JSON.stringify(expected);
  if (actualText !== expectedText) {
    throw new Error(`${label}\u3002Expected: ${expectedText}, received: ${actualText}`);
  }
}
function runTest(name, execute) {
  execute();
  console.log(`ok - ${name}`);
}
runTest("minOrderQuantity \u6709\u6548\u65F6\u5E94\u8FD4\u56DE\u7EAF\u6570\u5B57\u683C\u5F0F\u7684\u5185\u5305\u88C5\u6570\u91CF", () => {
  assertEqual(formatInnerPackCount(12, void 0, 12), "1", "\u6574\u9664\u65F6\u5E94\u663E\u793A\u6574\u6570\u4E14\u4E0D\u5E26\u5C0F\u6570");
  assertEqual(formatInnerPackCount(18, void 0, 12), "1.5", "\u975E\u6574\u9664\u65F6\u5E94\u4FDD\u7559 1 \u4F4D\u5C0F\u6570");
  assertEqual(formatInnerPackCount(0, 12, 12), "1", "\u8BA2\u8D27\u6570\u91CF\u4E3A 0 \u65F6\u5E94\u4F7F\u7528\u53D1\u8D27\u6570\u515C\u5E95\u8BA1\u7B97\u5305\u6570");
  assertEqual(formatInnerPackCount(0, 18, 12), "1.5", "\u53D1\u8D27\u6570\u515C\u5E95\u540E\u975E\u6574\u9664\u65F6\u5E94\u4FDD\u7559 1 \u4F4D\u5C0F\u6570");
});
runTest("minOrderQuantity \u4E3A\u7A7A 0 1 \u6216\u65E0\u6548\u65F6\u5E94\u8FD4\u56DE\u7A7A\u5B57\u7B26\u4E32", () => {
  assertEqual(formatInnerPackCount(24, void 0, void 0), "", "minOrderQuantity \u4E3A\u7A7A\u65F6\u5E94\u663E\u793A\u7A7A\u5B57\u7B26\u4E32");
  assertEqual(formatInnerPackCount(24, void 0, 0), "", "minOrderQuantity \u4E3A 0 \u65F6\u5E94\u663E\u793A\u7A7A\u5B57\u7B26\u4E32");
  assertEqual(formatInnerPackCount(24, void 0, 1), "", "minOrderQuantity \u4E3A 1 \u65F6\u5E94\u663E\u793A\u7A7A\u5B57\u7B26\u4E32");
  assertEqual(formatInnerPackCount(24, void 0, Number.NaN), "", "minOrderQuantity \u975E\u6CD5\u65F6\u5E94\u663E\u793A\u7A7A\u5B57\u7B26\u4E32");
});
runTest("\u5206\u5E97\u8BA2\u8D27\u4F53\u79EF\u5E94\u7EDF\u4E00\u4FDD\u7559\u4E24\u4F4D\u5C0F\u6570", () => {
  assertEqual(formatStoreOrderVolume(7.648), "7.65", "\u4F53\u79EF\u5E94\u56DB\u820D\u4E94\u5165\u5230\u4E24\u4F4D\u5C0F\u6570");
  assertEqual(formatStoreOrderVolume(0), "0.00", "\u96F6\u4F53\u79EF\u4E5F\u5E94\u663E\u793A\u4E24\u4F4D\u5C0F\u6570");
  assertEqual(formatStoreOrderVolume(void 0), "--", "\u7F3A\u5931\u4F53\u79EF\u5E94\u663E\u793A\u5360\u4F4D\u7B26");
});
var excelTexts = {
  sheetName: "Picking List",
  orderNoLabel: "Order No.",
  storeLabel: "Store",
  orderDateLabel: "Order Date",
  printTimeLabel: "Print Time",
  remarksLabel: "Remarks",
  totalSKULabel: "Total SKU",
  totalOrderQtyLabel: "Total Order Qty",
  totalShipQtyLabel: "Total Ship Qty",
  totalOrderVolumeLabel: "Order Volume",
  detailHeaders: {
    index: "#",
    itemNumber: "\u8D27\u53F7",
    location: "\u8D27\u4F4D",
    productName: "\u5546\u54C1\u540D\u79F0",
    importPrice: "\u8FDB\u53E3\u4EF7",
    rrp: "RRP",
    innerPackCount: "\u5185\u5305\u88C5\u6570\u91CF",
    orderQuantity: "\u8BA2\u8D27\u6570\u91CF"
  }
};
var excelItems = [
  {
    detailGUID: "detail-1",
    productCode: "P-001",
    itemNumber: "A-001",
    barcode: "111",
    productName: "\u5546\u54C1 A",
    quantity: 12,
    allocQuantity: 10,
    price: 0,
    amount: 0,
    importPrice: 3.5,
    importAmount: 0,
    minOrderQuantity: 12,
    isActive: true,
    locationCode: "L-01",
    rrp: 5.5
  },
  {
    detailGUID: "detail-2",
    productCode: "P-002",
    itemNumber: "A-002",
    barcode: "222",
    productName: "\u5546\u54C1 B",
    quantity: 18,
    allocQuantity: 9,
    price: 0,
    amount: 0,
    importPrice: 4,
    importAmount: 0,
    minOrderQuantity: 12,
    isActive: true,
    locationCode: "L-02",
    rrp: 6
  },
  {
    detailGUID: "detail-3",
    productCode: "P-003",
    itemNumber: "A-003",
    barcode: "333",
    productName: "\u5546\u54C1 C",
    quantity: 0,
    allocQuantity: 15,
    price: 0,
    amount: 0,
    importPrice: 8,
    importAmount: 0,
    minOrderQuantity: 0,
    isActive: true,
    locationCode: "L-03"
  },
  {
    detailGUID: "detail-4",
    productCode: "P-004",
    itemNumber: "A-004",
    barcode: "444",
    productName: "\u5546\u54C1 D",
    quantity: 24,
    allocQuantity: 24,
    price: 0,
    amount: 0,
    importPrice: 2,
    importAmount: 0,
    minOrderQuantity: 1,
    isActive: true,
    locationCode: "L-04",
    rrp: 3
  }
];
var excelOrder = {
  orderGUID: "order-1",
  orderNo: "SO-001",
  storeCode: "ST-01",
  totalAmount: 0,
  totalQuantity: 37,
  totalImportAmount: 0,
  totalVolume: 0,
  totalOrderVolume: 12.3456,
  remarks: "\u8BF7\u4F18\u5148\u5904\u7406",
  totalAllocQuantity: 19,
  totalSKU: 3,
  itemsTotal: 4,
  orderDate: "2026-06-01T00:00:00.000Z",
  items: excelItems
};
runTest("\u914D\u8D27\u5355 Excel \u6570\u636E\u5E94\u5305\u542B\u56FA\u5B9A\u5217\u987A\u5E8F\u3001\u5907\u6CE8\u548C\u603B\u8BA1\u4FE1\u606F", () => {
  const excelData = buildPickingListExcelData(excelOrder, excelItems, excelTexts);
  assertEqual(excelData.sheetName, "Picking List", "sheet \u540D\u79F0\u5E94\u6765\u81EA\u4F20\u5165\u6587\u6848");
  assertDeepEqual(
    excelData.overviewRows,
    [
      ["Order No.", "SO-001"],
      ["Store", "ST-01"],
      ["Order Date", "2026-06-01T00:00:00.000Z"],
      ["Print Time", ""]
    ],
    "Excel \u6982\u89C8\u4ECD\u5E94\u4FDD\u7559\u8BA2\u5355\u65E5\u671F\u548C\u6253\u5370\u65F6\u95F4\u5143\u6570\u636E"
  );
  assertDeepEqual(
    excelData.detailHeader,
    ["#", "\u8D27\u53F7", "\u8D27\u4F4D", "\u5546\u54C1\u540D\u79F0", "\u8FDB\u53E3\u4EF7", "RRP", "\u5185\u5305\u88C5\u6570\u91CF", "\u8BA2\u8D27\u6570\u91CF"],
    "\u660E\u7EC6\u5217\u987A\u5E8F\u5E94\u9690\u85CF\u53D1\u8D27\u6570\u5217"
  );
  assertEqual(excelData.detailRows.every((row) => row.length === 8), true, "Excel \u660E\u7EC6\u884C\u5E94\u4FDD\u6301 8 \u5217");
  assertDeepEqual(excelData.detailRows.map((row) => row[6]), ["1", "1.5", "", ""], "\u5185\u5305\u88C5\u6570\u91CF\u5E94\u590D\u7528\u7EDF\u4E00\u683C\u5F0F\u5316\u903B\u8F91");
  assertDeepEqual(excelData.detailRows.map((row) => row[7]), [12, 18, 15, 24], "\u8BA2\u8D27\u6570\u4E3A 0 \u65F6\u5E94\u4F7F\u7528\u53D1\u8D27\u6570\u515C\u5E95");
  assertDeepEqual(excelData.remarksRow, ["Remarks", "\u8BF7\u4F18\u5148\u5904\u7406"], "\u5907\u6CE8\u884C\u5E94\u4FDD\u7559\u539F\u59CB\u5907\u6CE8");
  assertDeepEqual(
    excelData.totalRows,
    [
      ["Total SKU", 3],
      ["Total Order Qty", 37],
      ["Total Ship Qty", 19],
      ["Order Volume", "12.35"]
    ],
    "\u603B\u8BA1\u884C\u5E94\u5305\u542B SKU\u3001\u8BA2\u8D27\u6570\u3001\u53D1\u8D27\u6570\u548C\u4E24\u4F4D\u5C0F\u6570\u8BA2\u8D27\u4F53\u79EF"
  );
});
runTest("\u914D\u8D27\u5355\u8BA2\u8D27\u6570\u4E3A\u7A7A\u6216\u4E3A 0 \u65F6\u5E94\u4F7F\u7528\u53D1\u8D27\u6570\u515C\u5E95", () => {
  assertEqual(formatPickingOrderQuantity(12, 9), 12, "\u8BA2\u8D27\u6570\u6709\u6548\u65F6\u5E94\u4F18\u5148\u663E\u793A\u8BA2\u8D27\u6570");
  assertEqual(formatPickingOrderQuantity(0, 12), 12, "\u8BA2\u8D27\u6570\u4E3A 0 \u65F6\u5E94\u663E\u793A\u53D1\u8D27\u6570");
  assertEqual(formatPickingOrderQuantity(void 0, 8), 8, "\u8BA2\u8D27\u6570\u7F3A\u5931\u65F6\u5E94\u663E\u793A\u53D1\u8D27\u6570");
  assertEqual(formatPickingOrderQuantity(0, 0), "", "\u8BA2\u8D27\u6570\u548C\u53D1\u8D27\u6570\u90FD\u4E3A\u7A7A\u65F6\u5E94\u663E\u793A\u7A7A\u5B57\u7B26\u4E32");
});
runTest("\u914D\u8D27\u5355\u5305\u6570\u5E94\u4F7F\u7528\u8BA2\u8D27\u6570\u5217\u540C\u53E3\u5F84\u6570\u91CF\u4F5C\u4E3A\u5206\u5B50", () => {
  assertEqual(formatInnerPackCount(0, 12, 12), "1", "MC020-16 \u4E3B\u52A8\u914D\u8D27 12 \u4E14\u4E2D\u5305\u6570 12 \u65F6\u5E94\u663E\u793A 1 \u5305");
  assertEqual(formatInnerPackCount(0, 18, 12), "1.5", "\u4E3B\u52A8\u914D\u8D27 18 \u4E14\u4E2D\u5305\u6570 12 \u65F6\u5E94\u663E\u793A 1.5 \u5305");
  assertEqual(formatInnerPackCount(void 0, 12, 12), "1", "\u8BA2\u8D27\u6570\u7F3A\u5931\u65F6\u4E5F\u5E94\u4F7F\u7528\u53D1\u8D27\u6570\u515C\u5E95\u8BA1\u7B97\u5305\u6570");
  assertEqual(formatInnerPackCount(0, 0, 12), "", "\u8BA2\u8D27\u6570\u548C\u53D1\u8D27\u6570\u90FD\u4E3A\u7A7A\u65F6\u5305\u6570\u5E94\u663E\u793A\u7A7A\u767D");
});
runTest("\u914D\u8D27\u5355\u6253\u5370\u5E94\u53D6\u6D88\u56FA\u5B9A 30 \u884C\u5206\u9875\u5E76\u4EA4\u7ED9 A4 \u6253\u5370\u6D41\u586B\u6EE1\u9875\u9762", () => {
  const pickingListSource = fs.readFileSync(path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/PickingList.tsx"), "utf8");
  assertEqual(pickingListSource.includes("PICKING_PRINT_ROWS_PER_PAGE"), false, "\u6253\u5370\u7EC4\u4EF6\u4E0D\u5E94\u4FDD\u7559\u56FA\u5B9A 30 \u884C\u5206\u9875\u5E38\u91CF");
  assertEqual(pickingListSource.includes("buildPickingPrintPages"), false, "\u6253\u5370\u7EC4\u4EF6\u4E0D\u5E94\u6309\u56FA\u5B9A\u884C\u6570\u9884\u5207\u9875");
});
runTest("\u914D\u8D27\u5355 PDF \u5206\u9875\u5E94\u6309 A4 \u53EF\u7528\u9AD8\u5EA6\u8BA1\u7B97\u5E76\u4E3A\u6700\u540E\u4E00\u9875\u4FDD\u7559\u6C47\u603B\u533A\u57DF", () => {
  const items = Array.from({ length: 8 }, (_, index) => ({
    ...excelItems[0],
    detailGUID: `pdf-detail-${index}`,
    itemNumber: `PDF-${index}`
  }));
  const pages = buildPickingListPdfPages(items, true, {
    pageHeightMm: 100,
    pagePaddingTopMm: 5,
    pagePaddingBottomMm: 5,
    headerHeightMm: 10,
    tableHeaderHeightMm: 5,
    footerHeightMm: 5,
    rowHeightMm: 10,
    finalSummaryHeightMm: 20
  });
  assertDeepEqual(
    pages.map((page) => page.items.length),
    [7, 1],
    "\u5C3E\u9875\u9700\u8981\u6C47\u603B\u65F6\u5E94\u4F18\u5148\u8BA9\u5012\u6570\u7B2C\u4E8C\u9875\u6EE1\u6392\uFF0C\u6700\u540E\u4E00\u9875\u53EF\u4EE5\u5C11"
  );
  assertEqual(pages[0].showSummary, false, "\u975E\u672B\u9875\u4E0D\u5E94\u663E\u793A\u5907\u6CE8\u548C\u6C47\u603B");
  assertEqual(pages[1].showSummary, true, "\u6700\u540E\u4E00\u9875\u5E94\u663E\u793A\u5907\u6CE8\u548C\u6C47\u603B");
});
runTest("\u914D\u8D27\u5355 PDF \u6BCF\u9875\u5E94\u5E26\u9875\u5934\u5143\u6570\u636E\u4E14\u9875\u811A\u53EA\u627F\u8F7D\u9875\u7801", () => {
  const pages = buildPickingListPdfPages(excelItems, true, {
    pageHeightMm: 70,
    pagePaddingTopMm: 5,
    pagePaddingBottomMm: 5,
    headerHeightMm: 10,
    tableHeaderHeightMm: 5,
    footerHeightMm: 5,
    rowHeightMm: 10,
    finalSummaryHeightMm: 10
  });
  pages.forEach((page) => {
    assertEqual(page.hasHeader, true, "\u6BCF\u4E2A PDF \u5206\u9875\u90FD\u5E94\u6E32\u67D3\u4E1A\u52A1\u9875\u5934");
    assertEqual(page.footerKind, "pageNumber", "PDF \u9875\u811A\u53EA\u80FD\u663E\u793A\u9875\u7801");
  });
});
runTest("\u914D\u8D27\u5355 PDF \u9ED8\u8BA4\u5206\u9875\u6BCF\u9875\u5E94\u6309 9mm \u660E\u7EC6\u884C\u653E 26 \u884C\u5E76\u4FDD\u7559\u9875\u7801\u7A7A\u95F4", () => {
  const items = Array.from({ length: 31 }, (_, index) => ({
    ...excelItems[0],
    detailGUID: `footer-safe-${index}`,
    itemNumber: `SAFE-${index}`
  }));
  const pages = buildPickingListPdfPages(items, false);
  assertEqual(pages[0].items.length, 26, "\u9ED8\u8BA4 PDF \u5206\u9875\u9996\u5F20 A4 \u5E94\u6309 9mm \u660E\u7EC6\u884C\u5BB9\u7EB3 26 \u884C");
  assertEqual(pages.length >= 2, true, "31 \u884C\u660E\u7EC6\u4E0D\u5E94\u7EE7\u7EED\u6324\u5728\u540C\u4E00\u9875\u5BFC\u81F4\u9875\u7801\u91CD\u53E0");
});
runTest("\u914D\u8D27\u5355 PDF \u5E26\u6C47\u603B\u7684\u5C3E\u9875\u5E94\u4F18\u5148\u8BA9\u5012\u6570\u7B2C\u4E8C\u9875\u6EE1\u6392", () => {
  const cases = [
    [23, [23]],
    [24, [24]],
    [25, [25]],
    [26, [25, 1]],
    [27, [26, 1]],
    [28, [26, 2]],
    [29, [26, 3]],
    [49, [26, 23]],
    [50, [26, 24]],
    [51, [26, 25]],
    [52, [26, 25, 1]],
    [75, [26, 26, 23]],
    [76, [26, 26, 24]],
    [77, [26, 26, 25]],
    [78, [26, 26, 25, 1]]
  ];
  for (const [itemCount, expectedPageSizes] of cases) {
    const items = Array.from({ length: itemCount }, (_, index) => ({
      ...excelItems[0],
      detailGUID: `summary-safe-${itemCount}-${index}`,
      itemNumber: `SUMMARY-${itemCount}-${index}`
    }));
    const pages = buildPickingListPdfPages(items, true);
    const summaryPage = pages[pages.length - 1];
    assertDeepEqual(pages.map((page) => page.items.length), expectedPageSizes, `${itemCount} \u884C\u65F6\u5E94\u4F18\u5148\u8BA9\u5012\u6570\u7B2C\u4E8C\u9875\u6EE1\u6392`);
    assertEqual(summaryPage.showSummary, true, `${itemCount} \u884C\u65F6\u6700\u540E\u4E00\u9875\u5E94\u663E\u793A\u6C47\u603B`);
    assertEqual(summaryPage.items.length <= 25, true, `${itemCount} \u884C\u65F6\u77ED\u5C3E\u5408\u5E76\u540E\u7684\u6C47\u603B\u9875\u4E0D\u5E94\u8D85\u8FC7 25 \u884C`);
  }
});
runTest("\u914D\u8D27\u5355 PDF \u6253\u5370\u8DEF\u5F84\u5E94\u4F7F\u7528\u5206\u9875 PDF \u4E14\u4E0D\u518D\u76F4\u63A5\u6253\u5370 HTML \u9875\u9762", () => {
  const pickingListSource = fs.readFileSync(path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/PickingList.tsx"), "utf8");
  assertEqual(pickingListSource.includes("printElementPagesAsPdf"), true, "\u6253\u5370\u6309\u94AE\u5E94\u8D70\u5206\u9875 PDF \u6253\u5370");
  assertEqual(pickingListSource.includes("downloadElementPagesAsPdf"), true, "\u4E0B\u8F7D\u6309\u94AE\u5E94\u8D70\u5206\u9875 PDF \u4E0B\u8F7D");
  assertEqual(pickingListSource.includes("window.print()"), false, "\u914D\u8D27\u5355\u4E0D\u5E94\u518D\u76F4\u63A5\u8C03\u7528\u6D4F\u89C8\u5668 HTML \u6253\u5370");
});
function readCssRule(source, selector) {
  const escapedSelector = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
  const match = source.match(new RegExp(`${escapedSelector}\\s*\\{([\\s\\S]*?)\\}`));
  return match?.[1] ?? "";
}
function readCssWidth(rule) {
  return Number(rule.match(/width:\s*(\d+(?:\.\d+)?)%/)?.[1] ?? Number.NaN);
}
function readCssNumber(rule, property) {
  return Number(rule.match(new RegExp(`${property}:\\s*(\\d+(?:\\.\\d+)?)`))?.[1] ?? Number.NaN);
}
runTest("\u914D\u8D27\u5355\u9875\u5934\u5E94\u5C06\u5E97\u540D\u548C\u5355\u53F7\u540C\u4E00\u884C\u5C45\u4E2D\u653E\u5927\u663E\u793A", () => {
  const pickingListSource = fs.readFileSync(path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/PickingList.tsx"), "utf8");
  const printCssSource = fs.readFileSync(path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/print.css"), "utf8");
  const headerSource = pickingListSource.slice(
    pickingListSource.indexOf("const renderPickingHeader"),
    pickingListSource.indexOf("return (", pickingListSource.indexOf("const renderPickingHeader"))
  );
  const headerRule = readCssRule(printCssSource, ".store-order-picking-header");
  const primaryRule = readCssRule(printCssSource, ".store-order-picking-primary");
  const primaryLineRule = readCssRule(printCssSource, ".store-order-picking-primary-line");
  const storeRule = readCssRule(printCssSource, ".store-order-picking-store");
  const orderNoRule = readCssRule(printCssSource, ".store-order-picking-order-no");
  const metaRule = readCssRule(printCssSource, ".store-order-picking-meta");
  assertEqual(headerSource.includes('className="store-order-picking-primary-line"'), true, "\u9875\u5934\u5E94\u6709\u5E97\u540D\u548C\u5355\u53F7\u540C\u4E00\u884C\u4E3B\u4FE1\u606F\u5BB9\u5668");
  assertEqual(headerSource.includes('className="store-order-picking-store"'), true, "\u5E97\u540D\u5E94\u6302\u8F7D\u4E3B\u5B57\u53F7\u6837\u5F0F");
  assertEqual(headerSource.includes("{displayStoreText}"), true, "\u4E3B\u4FE1\u606F\u884C\u5E94\u663E\u793A\u5E97\u540D");
  assertEqual(headerSource.includes('className="store-order-picking-order-no"'), true, "\u5355\u53F7\u5E94\u6302\u8F7D\u4E3B\u5B57\u53F7\u6837\u5F0F");
  assertEqual(headerSource.includes('className="store-order-picking-meta"'), true, "\u9875\u5934\u5E94\u663E\u793A\u8BA2\u5355\u65E5\u671F\u5BB9\u5668");
  assertEqual(headerSource.includes("t('warehouse.pickingList.printTime')"), false, "\u9875\u5934\u4E0D\u5E94\u7EE7\u7EED\u663E\u793A\u6253\u5370\u65F6\u95F4");
  assertEqual(headerSource.includes("t('warehouse.pickingList.orderDate')"), true, "\u9875\u5934\u5E94\u663E\u793A\u8BA2\u8D27\u65E5\u671F");
  assertEqual(headerSource.includes("formatPrintDate(order.orderDate, false, printLocale)"), true, "\u9875\u5934\u5E94\u683C\u5F0F\u5316\u8BA2\u5355\u65E5\u671F");
  assertEqual(headerSource.includes("formatPrintDate(undefined, true, printLocale)"), false, "\u9875\u5934\u4E0D\u5E94\u7EE7\u7EED\u8BA1\u7B97\u6253\u5370\u65F6\u95F4");
  assertEqual(printCssSource.includes(".store-order-picking-meta"), true, "\u6253\u5370\u6837\u5F0F\u5E94\u4FDD\u7559\u8BA2\u5355\u65E5\u671F\u5143\u4FE1\u606F\u6837\u5F0F");
  assertEqual(headerSource.includes("t('warehouse.pickingList.orderNoLabel')"), false, "\u4E3B\u4FE1\u606F\u884C\u4E0D\u5E94\u7EE7\u7EED\u663E\u793A\u8BA2\u5355\u53F7\u6587\u5B57\u6807\u7B7E");
  assertEqual(headerSource.includes("#{orderNoText}"), true, "\u4E3B\u4FE1\u606F\u884C\u5E94\u4F7F\u7528 # \u524D\u7F00\u663E\u793A\u5355\u53F7");
  assertEqual(headerSource.includes("t('warehouse.pickingList.storeLabel')"), false, "\u5E97\u540D\u4E0D\u5E94\u7EE7\u7EED\u4F5C\u4E3A\u53F3\u4FA7\u5C0F\u53F7\u5143\u6570\u636E\u663E\u793A");
  assertEqual(/grid-template-columns:\s*minmax\(80px,\s*1fr\)\s*minmax\(0,\s*2fr\)\s*minmax\(120px,\s*1fr\)/.test(headerRule), true, "\u9875\u5934\u5E94\u4F7F\u7528\u4E09\u680F\u5E03\u5C40\u627F\u8F7D\u5C45\u4E2D\u4E3B\u4FE1\u606F");
  assertEqual(/text-align:\s*center/.test(primaryRule), true, "\u4E3B\u4FE1\u606F\u533A\u5E94\u5C45\u4E2D");
  assertEqual(/display:\s*inline-flex/.test(primaryLineRule), true, "\u5E97\u540D\u548C\u5355\u53F7\u5E94\u6C34\u5E73\u6392\u5217");
  assertEqual(/white-space:\s*nowrap/.test(primaryLineRule), false, "\u4E3B\u4FE1\u606F\u884C\u4E0D\u5E94\u5F3A\u5236\u5E97\u540D\u548C\u5355\u53F7\u6574\u4F53\u5355\u884C\u663E\u793A");
  assertEqual(/gap:\s*18px/.test(primaryLineRule), true, "\u5E97\u540D\u548C\u5355\u53F7\u4E4B\u95F4\u5E94\u6709\u6E05\u6670\u95F4\u8DDD");
  assertEqual(/display:\s*-webkit-box/.test(storeRule), true, "\u5E97\u540D\u5E94\u542F\u7528\u591A\u884C\u622A\u65AD\u5BB9\u5668");
  assertEqual(/white-space:\s*normal/.test(storeRule), true, "\u5E97\u540D\u5E94\u5141\u8BB8\u81EA\u52A8\u6362\u884C");
  assertEqual(/-webkit-line-clamp:\s*2/.test(storeRule), true, "\u5E97\u540D\u6700\u591A\u663E\u793A\u4E24\u884C");
  assertEqual(/-webkit-box-orient:\s*vertical/.test(storeRule), true, "\u5E97\u540D\u4E24\u884C\u622A\u65AD\u5E94\u4F7F\u7528\u7EB5\u5411 box");
  assertEqual(/text-overflow:\s*ellipsis/.test(storeRule), false, "\u5E97\u540D\u4E0D\u5E94\u7EE7\u7EED\u4F7F\u7528\u5355\u884C\u7701\u7565\u903B\u8F91");
  assertEqual(readCssNumber(storeRule, "font-size"), 22, "\u5E97\u540D\u5B57\u53F7\u5E94\u653E\u5927\u5230 22px");
  assertEqual(readCssNumber(orderNoRule, "font-size"), 28, "\u5355\u53F7\u5B57\u53F7\u5E94\u653E\u5927\u5230 28px");
  assertEqual(/flex:\s*0\s+0\s+auto/.test(orderNoRule), true, "\u5355\u53F7\u4E0D\u5E94\u88AB\u957F\u5E97\u540D\u6324\u538B\u6536\u7F29");
  assertEqual(/font-weight:\s*800/.test(orderNoRule), true, "\u5355\u53F7\u5E94\u52A0\u7C97\u7A81\u51FA\u663E\u793A");
  assertEqual(readCssNumber(storeRule, "font-size") > readCssNumber(metaRule, "font-size"), true, "\u5E97\u540D\u5B57\u53F7\u5E94\u5927\u4E8E\u53F3\u4FA7\u8F85\u52A9\u4FE1\u606F");
  assertEqual(/flex-direction:\s*column/.test(metaRule), true, "\u53F3\u4FA7\u8F85\u52A9\u4FE1\u606F\u5E94\u4FDD\u6301\u5355\u5217\u663E\u793A");
  assertEqual(/align-items:\s*flex-end/.test(metaRule), true, "\u53F3\u4FA7\u8F85\u52A9\u4FE1\u606F\u5E94\u53F3\u5BF9\u9F50");
});
runTest("\u914D\u8D27\u5355\u6253\u5370\u884C\u9AD8\u548C\u5B57\u4F53\u5E94\u6309 9mm \u660E\u7EC6\u884C\u7A33\u5B9A\u5206\u9875", () => {
  const printCssSource = fs.readFileSync(path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/print.css"), "utf8");
  const tableRule = readCssRule(printCssSource, ".store-order-picking-table");
  const rowRule = readCssRule(printCssSource, ".store-order-picking-table tr");
  const bodyRowRule = readCssRule(printCssSource, ".store-order-picking-table tbody tr");
  const bodyCellRule = readCssRule(printCssSource, ".store-order-picking-table td");
  const headerCellRule = readCssRule(printCssSource, ".store-order-picking-table th");
  const indexRule = readCssRule(printCssSource, ".store-order-picking-table .col-index");
  const itemRule = readCssRule(printCssSource, ".store-order-picking-table .col-item");
  const locationRule = readCssRule(printCssSource, ".store-order-picking-table .col-location");
  const productRule = readCssRule(printCssSource, ".store-order-picking-table .col-product");
  const priceRule = readCssRule(printCssSource, ".store-order-picking-table .col-price");
  const innerPackRule = readCssRule(printCssSource, ".store-order-picking-table .col-inner-pack");
  const quantityRule = readCssRule(printCssSource, ".store-order-picking-table .col-qty");
  const headerTopBorderRule = readCssRule(printCssSource, ".store-order-picking-table thead tr:last-child th");
  const firstColumnBorderRule = readCssRule(printCssSource, ".store-order-picking-table th:first-child,\n.store-order-picking-table td:first-child");
  const zebraRule = readCssRule(printCssSource, ".store-order-picking-table tbody tr:nth-child(even) td");
  assertEqual(readCssNumber(tableRule, "font-size"), 15, "\u8868\u683C\u57FA\u7840\u5B57\u4F53\u5E94\u4E3A 15px");
  assertEqual(readCssNumber(tableRule, "line-height"), 1.35, "\u8868\u683C\u57FA\u7840\u884C\u9AD8\u5E94\u7EA6\u4E3A 1.35");
  assertEqual(/border-collapse:\s*separate/.test(tableRule), true, "\u914D\u8D27\u5355\u8868\u683C\u5E94\u4F7F\u7528 separate \u8FB9\u6846\u907F\u514D\u5185\u7EBF\u53E0\u52A0\u53D8\u7C97");
  assertEqual(/border-spacing:\s*0/.test(tableRule), true, "\u914D\u8D27\u5355\u8868\u683C separate \u8FB9\u6846\u5E94\u4FDD\u6301\u65E0\u95F4\u8DDD");
  assertEqual(/print-color-adjust:\s*exact/.test(tableRule), true, "\u8868\u683C\u6253\u5370\u5E94\u4FDD\u7559\u80CC\u666F\u8272");
  assertEqual(/-webkit-print-color-adjust:\s*exact/.test(tableRule), true, "\u8868\u683C\u6253\u5370\u5E94\u517C\u5BB9 Chromium \u80CC\u666F\u8272\u4FDD\u7559");
  assertEqual(/break-inside:\s*avoid-page/.test(rowRule), true, "\u884C\u5206\u9875\u5E94\u4F7F\u7528 avoid-page \u9632\u6B62\u884C\u4E2D\u95F4\u5207\u65AD");
  assertEqual(/break-inside:\s*avoid/.test(rowRule), true, "\u884C\u5206\u9875\u5E94\u4FDD\u7559\u901A\u7528 break-inside \u517C\u5BB9\u89C4\u5219");
  assertEqual(/page-break-inside:\s*avoid/.test(rowRule), true, "\u884C\u5206\u9875\u5E94\u4FDD\u7559\u65E7\u7248 page-break-inside \u517C\u5BB9\u89C4\u5219");
  assertEqual(/height:\s*9mm/.test(bodyRowRule), true, "\u660E\u7EC6\u884C\u5E94\u56FA\u5B9A\u4E3A 9mm \u9AD8");
  assertEqual(/height:\s*9mm/.test(bodyCellRule), true, "\u660E\u7EC6\u5355\u5143\u683C\u5E94\u56FA\u5B9A\u4E3A 9mm \u9AD8");
  assertEqual(/box-sizing:\s*border-box/.test(bodyCellRule), true, "\u660E\u7EC6\u5355\u5143\u683C\u56FA\u5B9A\u9AD8\u5EA6\u5E94\u5305\u542B\u8FB9\u6846\u548C\u5185\u8FB9\u8DDD");
  assertEqual(/padding:\s*0\.7mm 0\.6mm/.test(bodyCellRule), true, "\u660E\u7EC6\u5355\u5143\u683C padding \u5E94\u9002\u914D 9mm \u884C\u9AD8");
  assertEqual(/padding:\s*0\.7mm 0\.6mm/.test(headerCellRule), true, "\u8868\u5934\u5355\u5143\u683C padding \u5E94\u9002\u914D\u6253\u5370\u884C\u9AD8");
  assertEqual(/text-align:\s*center/.test(bodyCellRule), true, "\u660E\u7EC6\u5355\u5143\u683C\u6587\u672C\u5E94\u6C34\u5E73\u5C45\u4E2D");
  assertEqual(/text-align:\s*center/.test(headerCellRule), true, "\u8868\u5934\u5355\u5143\u683C\u6587\u672C\u5E94\u6C34\u5E73\u5C45\u4E2D");
  assertEqual(/vertical-align:\s*middle/.test(bodyCellRule), true, "\u660E\u7EC6\u5355\u5143\u683C\u6587\u672C\u5E94\u5782\u76F4\u5C45\u4E2D");
  assertEqual(/vertical-align:\s*middle/.test(headerCellRule), true, "\u8868\u5934\u5355\u5143\u683C\u6587\u672C\u5E94\u5782\u76F4\u5C45\u4E2D");
  assertEqual(/border:\s*1px solid #000/.test(bodyCellRule), false, "\u660E\u7EC6\u5355\u5143\u683C\u4E0D\u5E94\u4F7F\u7528\u56DB\u8FB9 border\uFF0C\u907F\u514D\u5185\u6846\u7EBF\u53E0\u52A0\u53D8\u7C97");
  assertEqual(/border:\s*1px solid #000/.test(headerCellRule), false, "\u8868\u5934\u5355\u5143\u683C\u4E0D\u5E94\u4F7F\u7528\u56DB\u8FB9 border\uFF0C\u907F\u514D\u5185\u6846\u7EBF\u53E0\u52A0\u53D8\u7C97");
  assertEqual(/border-right:\s*1px solid #000/.test(bodyCellRule), true, "\u660E\u7EC6\u5355\u5143\u683C\u5E94\u7ED8\u5236\u53F3\u4FA7\u9ED1\u8272\u5B9E\u7EBF");
  assertEqual(/border-bottom:\s*1px solid #000/.test(bodyCellRule), true, "\u660E\u7EC6\u5355\u5143\u683C\u5E94\u7ED8\u5236\u5E95\u90E8\u9ED1\u8272\u5B9E\u7EBF");
  assertEqual(/border-right:\s*1px solid #000/.test(headerCellRule), true, "\u8868\u5934\u5355\u5143\u683C\u5E94\u7ED8\u5236\u53F3\u4FA7\u9ED1\u8272\u5B9E\u7EBF");
  assertEqual(/border-bottom:\s*1px solid #000/.test(headerCellRule), true, "\u8868\u5934\u5355\u5143\u683C\u5E94\u7ED8\u5236\u5E95\u90E8\u9ED1\u8272\u5B9E\u7EBF");
  assertEqual(/border-top:\s*1px solid #000/.test(headerTopBorderRule), true, "\u5217\u5934\u884C\u5E94\u8865\u9876\u90E8\u9ED1\u8272\u5B9E\u7EBF\u4F5C\u4E3A\u8868\u683C\u4E0A\u5916\u6846");
  assertEqual(/border-left:\s*1px solid #000/.test(firstColumnBorderRule), true, "\u7B2C\u4E00\u5217\u5E94\u8865\u5DE6\u4FA7\u9ED1\u8272\u5B9E\u7EBF\u4F5C\u4E3A\u8868\u683C\u5DE6\u5916\u6846");
  assertEqual(readCssNumber(indexRule, "font-size"), 14.5, "\u884C\u53F7\u5B57\u4F53\u5E94\u4E3A 14.5px");
  assertEqual(readCssNumber(itemRule, "font-size"), 14.5, "\u8D27\u53F7\u5B57\u4F53\u5E94\u4E3A 14.5px");
  assertEqual(readCssNumber(locationRule, "font-size"), 14.5, "\u8D27\u4F4D\u5B57\u4F53\u5E94\u4E3A 14.5px");
  assertEqual(readCssNumber(productRule, "font-size"), 12.5, "\u5546\u54C1\u540D\u79F0\u5B57\u4F53\u5E94\u4E3A 12.5px");
  assertEqual(/padding-left:\s*1px/.test(indexRule), true, "\u884C\u53F7\u5217\u5DE6\u4FA7\u95F4\u8DDD\u5E94\u538B\u7F29\u5230 1px");
  assertEqual(/padding-right:\s*2px/.test(indexRule), true, "\u884C\u53F7\u5217\u53F3\u4FA7\u95F4\u8DDD\u5E94\u538B\u7F29\u5230 2px");
  assertEqual(/text-align:\s*right/.test(indexRule), false, "\u884C\u53F7\u5217\u4E0D\u5E94\u8986\u76D6\u57FA\u7840\u5C45\u4E2D\u5BF9\u9F50");
  assertEqual(/text-align:\s*right/.test(priceRule), false, "\u4EF7\u683C\u5217\u4E0D\u5E94\u8986\u76D6\u57FA\u7840\u5C45\u4E2D\u5BF9\u9F50");
  assertEqual(/white-space:\s*nowrap/.test(indexRule), true, "\u4E09\u4F4D\u884C\u53F7\u4E0D\u5E94\u6362\u884C");
  assertEqual(/font-weight:\s*700/.test(indexRule), true, "\u884C\u53F7\u5217\u5E94\u52A0\u7C97");
  assertEqual(/font-variant-numeric:\s*tabular-nums/.test(indexRule), true, "\u884C\u53F7\u5E94\u4F7F\u7528\u7B49\u5BBD\u6570\u5B57\u7A33\u5B9A\u5BF9\u9F50");
  assertEqual(/font-weight:\s*700/.test(quantityRule), true, "\u8BA2\u8D27\u6570\u5217\u5E94\u52A0\u7C97");
  assertEqual(zebraRule, "", "\u660E\u7EC6\u884C\u4E0D\u5E94\u4FDD\u7559\u6591\u9A6C\u7EB9\u80CC\u666F\u89C4\u5219");
  assertEqual(printCssSource.includes(".col-send-qty"), false, "\u53D1\u8D27\u6570\u660E\u7EC6\u5217\u5E94\u4ECE\u6253\u5370\u6837\u5F0F\u4E2D\u79FB\u9664");
  for (const rule of [priceRule, innerPackRule, quantityRule]) {
    assertEqual(readCssNumber(rule, "font-size"), 13, "\u6570\u5B57\u5217\u5B57\u4F53\u5E94\u4E3A 13px");
  }
});
runTest("\u914D\u8D27\u5355\u6253\u5370\u5217\u5BBD\u5E94\u6309\u8349\u56FE\u91CD\u65B0\u5206\u914D\u5E76\u4FDD\u7559 colgroup", () => {
  const printCssSource = fs.readFileSync(path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/print.css"), "utf8");
  const pickingListSource = fs.readFileSync(path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/PickingList.tsx"), "utf8");
  assertEqual(readCssWidth(readCssRule(printCssSource, ".store-order-picking-table .col-index")), 6, "\u884C\u53F7\u5217\u5E94\u4E3A 6%");
  assertEqual(readCssWidth(readCssRule(printCssSource, ".store-order-picking-table .col-item")), 18, "\u8D27\u53F7\u5217\u5E94\u4E3A 18%");
  assertEqual(readCssWidth(readCssRule(printCssSource, ".store-order-picking-table .col-location")), 17, "\u8D27\u4F4D\u5217\u5E94\u4E3A 17%");
  assertEqual(readCssWidth(readCssRule(printCssSource, ".store-order-picking-table .col-product")), 25, "\u5546\u54C1\u540D\u79F0\u5217\u5E94\u4E3A 25%");
  assertEqual(readCssWidth(readCssRule(printCssSource, ".store-order-picking-table .col-price")), 7, "\u4EF7\u683C\u5217\u5E94\u4E3A 7%");
  assertEqual(readCssWidth(readCssRule(printCssSource, ".store-order-picking-table .col-inner-pack")), 6.5, "\u5305\u6570\u5217\u5E94\u4E3A 6.5%");
  assertEqual(readCssWidth(readCssRule(printCssSource, ".store-order-picking-table .col-qty")), 13.5, "\u8BA2\u8D27\u6570\u91CF\u5217\u5E94\u5408\u5E76\u53D1\u8D27\u6570\u5217\u5BBD\u5EA6\u4E3A 13.5%");
  assertEqual(printCssSource.includes(".col-send-qty"), false, "\u53D1\u8D27\u6570\u660E\u7EC6\u5217\u6837\u5F0F\u5E94\u79FB\u9664");
  assertEqual(pickingListSource.includes("<colgroup>"), true, "\u56FA\u5B9A\u8868\u683C\u5E03\u5C40\u5E94\u4F7F\u7528 colgroup \u660E\u786E\u5217\u5BBD");
  assertDeepEqual(
    Array.from(pickingListSource.matchAll(/<col className="([^"]+)" \/>/g), (match) => match[1]).slice(0, 8),
    ["col-index", "col-item", "col-location", "col-product", "col-price", "col-price", "col-inner-pack", "col-qty"],
    "colgroup \u5E94\u6309\u8868\u5934\u987A\u5E8F\u5B9A\u4E49 8 \u5217\uFF0C\u5E76\u5305\u542B\u4E24\u5217\u4EF7\u683C\u5217"
  );
  assertEqual(pickingListSource.includes("colSpan={8}"), true, "\u6807\u9898\u884C\u5E94\u8DE8 8 \u5217");
  assertEqual(pickingListSource.includes("colSpan={9}"), false, "\u6807\u9898\u884C\u4E0D\u5E94\u7EE7\u7EED\u8DE8 9 \u5217");
  assertEqual(pickingListSource.includes("col-send-qty"), false, "\u914D\u8D27\u5355\u4E0D\u5E94\u518D\u6E32\u67D3\u72EC\u7ACB\u53D1\u8D27\u6570\u5217");
});
runTest("\u914D\u8D27\u5355\u6253\u5370\u8D27\u53F7\u8D27\u4F4D\u5E94\u4FDD\u7559\u95F4\u9694\u4E14\u540D\u79F0\u4EF7\u683C\u533A\u57DF\u66F4\u7D27\u51D1", () => {
  const printCssSource = fs.readFileSync(path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/print.css"), "utf8");
  const locationRule = readCssRule(printCssSource, ".store-order-picking-table .col-location");
  const productRule = readCssRule(printCssSource, ".store-order-picking-table .col-product");
  const priceRule = readCssRule(printCssSource, ".store-order-picking-table .col-price");
  const innerPackRule = readCssRule(printCssSource, ".store-order-picking-table .col-inner-pack");
  const quantityRule = readCssRule(printCssSource, ".store-order-picking-table .col-qty");
  assertEqual(/padding-left:\s*5px/.test(locationRule), true, "\u8D27\u4F4D\u5217\u5DE6\u4FA7\u95F4\u9694\u5E94\u4E3A 5px");
  assertEqual(/padding-right:\s*5px/.test(locationRule), true, "\u8D27\u4F4D\u5217\u53F3\u4FA7\u95F4\u9694\u5E94\u4E3A 5px");
  assertEqual(readCssWidth(productRule), 25, "\u5546\u54C1\u540D\u79F0\u5217\u5E94\u6536\u7A84\u5230 25%");
  assertEqual(readCssWidth(priceRule), 7, "\u4EF7\u683C\u5217\u5E94\u4E3A 7%");
  assertEqual(readCssWidth(quantityRule), 13.5, "\u8BA2\u8D27\u6570\u91CF\u5217\u5E94\u5438\u6536\u53D1\u8D27\u6570\u5217\u5BBD\u5EA6");
  for (const rule of [priceRule, innerPackRule, quantityRule]) {
    assertEqual(/padding-left:\s*1px/.test(rule), true, "\u6570\u5B57\u5217\u5DE6\u4FA7 padding \u5E94\u538B\u7F29\u5230 1px");
    assertEqual(/padding-right:\s*1px/.test(rule), true, "\u6570\u5B57\u5217\u53F3\u4FA7 padding \u5E94\u538B\u7F29\u5230 1px");
  }
});
runTest("\u914D\u8D27\u5355\u6253\u5370\u8868\u5934\u5E94\u5C06\u5185\u5305\u88C5\u6570\u91CF\u663E\u793A\u4E3A\u5305\u6570", () => {
  const pickingListSource = fs.readFileSync(path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/PickingList.tsx"), "utf8");
  assertEqual(
    pickingListSource.includes(`<th className="col-inner-pack">{t('warehouse.pickingList.innerPackShort')}</th>`),
    true,
    "\u6253\u5370\u8868\u5934\u5E94\u4F7F\u7528\u914D\u8D27\u5355\u4E13\u7528\u5305\u6570\u7FFB\u8BD1"
  );
  assertEqual(
    pickingListSource.includes("innerPackCount: t('warehouse.pickingList.innerPackShort')"),
    true,
    "Excel \u8868\u5934\u5E94\u590D\u7528\u914D\u8D27\u5355\u4E13\u7528\u5305\u6570\u7FFB\u8BD1"
  );
  assertEqual(pickingListSource.includes('<th className="col-inner-pack">\u5305\u6570</th>'), false, "\u6253\u5370\u8868\u5934\u4E0D\u5E94\u786C\u7F16\u7801\u201C\u5305\u6570\u201D");
  assertEqual(pickingListSource.includes(`<th className="col-inner-pack">{t('column.innerPackCount')}</th>`), false, "\u6253\u5370\u8868\u5934\u4E0D\u5E94\u7EE7\u7EED\u663E\u793A\u201C\u5185\u5305\u88C5\u6570\u91CF\u201D\u7FFB\u8BD1");
  assertEqual(pickingListSource.includes("innerPackCount: t('column.innerPackCount')"), false, "Excel \u8868\u5934\u4E0D\u5E94\u7EE7\u7EED\u663E\u793A\u201C\u5185\u5305\u88C5\u6570\u91CF\u201D\u7FFB\u8BD1");
});
runTest("\u914D\u8D27\u5355\u6253\u5370\u8868\u5934\u5E94\u5C06\u8BA2\u8D27\u6570\u91CF\u663E\u793A\u4E3A\u8BA2\u8D27\u6570", () => {
  const pickingListSource = fs.readFileSync(path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/PickingList.tsx"), "utf8");
  assertEqual(
    pickingListSource.includes(`<th className="col-qty">{t('warehouse.pickingList.orderQtyShort')}</th>`),
    true,
    "\u6253\u5370\u8868\u5934\u5E94\u4F7F\u7528\u914D\u8D27\u5355\u4E13\u7528\u8BA2\u8D27\u6570\u7FFB\u8BD1"
  );
  assertEqual(pickingListSource.includes('<th className="col-qty">\u8BA2\u8D27\u6570</th>'), false, "\u6253\u5370\u8868\u5934\u4E0D\u5E94\u786C\u7F16\u7801\u201C\u8BA2\u8D27\u6570\u201D");
  assertEqual(pickingListSource.includes(`<th className="col-qty">{t('column.orderQuantity')}</th>`), false, "\u6253\u5370\u8868\u5934\u4E0D\u5E94\u7EE7\u7EED\u663E\u793A\u201C\u8BA2\u8D27\u6570\u91CF\u201D\u7FFB\u8BD1");
  assertEqual(
    pickingListSource.includes('<td className="col-qty">{formatPickingOrderQuantity(item.quantity, item.allocQuantity)}</td>'),
    true,
    "\u8BA2\u8D27\u6570\u5355\u5143\u683C\u5E94\u4F7F\u7528\u53D1\u8D27\u6570\u515C\u5E95\u663E\u793A\u51FD\u6570"
  );
  assertEqual(
    pickingListSource.includes("{formatInnerPackCount(item.quantity, item.allocQuantity, item.minOrderQuantity)}"),
    true,
    "\u5305\u6570\u5355\u5143\u683C\u5E94\u4F20\u5165\u53D1\u8D27\u6570\u4F5C\u4E3A\u515C\u5E95\u5206\u5B50"
  );
  assertEqual(pickingListSource.includes('<th className="col-send-qty">'), false, "\u6253\u5370\u8868\u5934\u4E0D\u5E94\u7EE7\u7EED\u663E\u793A\u53D1\u8D27\u6570\u5217");
  assertEqual(pickingListSource.includes('<td className="col-send-qty">'), false, "\u660E\u7EC6\u884C\u4E0D\u5E94\u7EE7\u7EED\u663E\u793A\u53D1\u8D27\u6570\u5355\u5143\u683C");
});
runTest("\u914D\u8D27\u5355\u6253\u5370\u77ED\u8868\u5934\u5E94\u5305\u542B\u4E2D\u82F1\u6587\u7FFB\u8BD1", () => {
  const zhLocale = JSON.parse(fs.readFileSync(path.resolve(process.cwd(), "src/i18n/locales/zh.json"), "utf8"));
  const enLocale = JSON.parse(fs.readFileSync(path.resolve(process.cwd(), "src/i18n/locales/en.json"), "utf8"));
  assertEqual(zhLocale.warehouse.pickingList.innerPackShort, "\u5305\u6570", "\u4E2D\u6587\u5305\u6570\u77ED\u8868\u5934\u5E94\u5B58\u5728");
  assertEqual(zhLocale.warehouse.pickingList.orderQtyShort, "\u8BA2\u8D27\u6570", "\u4E2D\u6587\u8BA2\u8D27\u6570\u77ED\u8868\u5934\u5E94\u5B58\u5728");
  assertEqual(enLocale.warehouse.pickingList.innerPackShort, "INNER Pack", "\u82F1\u6587\u5305\u6570\u77ED\u8868\u5934\u5E94\u663E\u793A INNER Pack");
  assertEqual(enLocale.warehouse.pickingList.orderQtyShort, "Order Qty", "\u82F1\u6587\u8BA2\u8D27\u6570\u77ED\u8868\u5934\u5E94\u5B58\u5728");
});
runTest("\u914D\u8D27\u5355\u6253\u5370\u8D27\u53F7\u8D27\u4F4D\u5E94\u5355\u884C\u5B8C\u6574\u663E\u793A\u4E14\u4E0D\u80FD\u88AB\u7701\u7565\u9690\u85CF", () => {
  const printCssSource = fs.readFileSync(path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/print.css"), "utf8");
  const itemRule = readCssRule(printCssSource, ".store-order-picking-table .col-item");
  assertEqual(/padding-left:\s*10px/.test(itemRule), true, "\u8D27\u53F7\u5217\u5DE6\u4FA7\u95F4\u8DDD\u5E94\u4E3A 10px");
  assertEqual(/text-align:\s*center/.test(itemRule), true, "\u8D27\u53F7\u5217\u5E94\u5C45\u4E2D\u663E\u793A");
  for (const selector of [".store-order-picking-table .col-item", ".store-order-picking-table .col-location"]) {
    const rule = readCssRule(printCssSource, selector);
    assertEqual(rule.includes("white-space: nowrap"), true, `${selector} \u5E94\u4FDD\u6301\u5355\u884C\u663E\u793A`);
    assertEqual(/overflow:\s*hidden/.test(rule), false, `${selector} \u4E0D\u5E94\u9690\u85CF\u6EA2\u51FA\u5185\u5BB9`);
    assertEqual(/text-overflow:\s*ellipsis/.test(rule), false, `${selector} \u4E0D\u5E94\u4F7F\u7528\u7701\u7565\u53F7\u622A\u65AD`);
  }
});
runTest("\u914D\u8D27\u5355\u6253\u5370\u5546\u54C1\u540D\u79F0\u5E94\u81EA\u52A8\u6362\u884C\u5E76\u6700\u591A\u663E\u793A\u4E24\u884C", () => {
  const printCssSource = fs.readFileSync(path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/print.css"), "utf8");
  const productRule = readCssRule(printCssSource, ".store-order-picking-table .col-product");
  const nameRule = readCssRule(printCssSource, ".store-order-picking-name");
  assertEqual(productRule.includes("white-space: normal"), true, "\u5546\u54C1\u540D\u79F0\u5217\u5E94\u5141\u8BB8\u81EA\u52A8\u6362\u884C");
  assertEqual(/text-overflow:\s*ellipsis/.test(productRule), false, "\u5546\u54C1\u540D\u79F0\u5217\u4E0D\u5E94\u7EE7\u7EED\u4F7F\u7528\u5355\u884C\u7701\u7565\u53F7");
  assertEqual(/display:\s*-webkit-box/.test(nameRule), true, "\u5546\u54C1\u540D\u79F0\u5185\u5BB9\u5E94\u542F\u7528\u4E24\u884C\u622A\u65AD\u5BB9\u5668");
  assertEqual(/text-align:\s*center/.test(nameRule), true, "\u5546\u54C1\u540D\u79F0\u5185\u5BB9\u5E94\u5728\u5355\u5143\u683C\u5185\u6C34\u5E73\u5C45\u4E2D");
  assertEqual(nameRule.includes("white-space: normal"), true, "\u5546\u54C1\u540D\u79F0\u5185\u5BB9\u5E94\u5141\u8BB8\u81EA\u52A8\u6362\u884C");
  assertEqual(/overflow:\s*hidden/.test(nameRule), true, "\u5546\u54C1\u540D\u79F0\u8D85\u8FC7\u4E24\u884C\u65F6\u5E94\u9690\u85CF");
  assertEqual(/-webkit-line-clamp:\s*2/.test(nameRule), true, "\u5546\u54C1\u540D\u79F0\u6700\u591A\u663E\u793A\u4E24\u884C");
  assertEqual(/-webkit-box-orient:\s*vertical/.test(nameRule), true, "\u5546\u54C1\u540D\u79F0\u4E24\u884C\u622A\u65AD\u5E94\u4F7F\u7528\u7EB5\u5411 box");
  assertEqual(readCssNumber(nameRule, "line-height"), 1.15, "\u5546\u54C1\u540D\u79F0\u5185\u5BB9\u884C\u9AD8\u5E94\u63A7\u5236\u5728 9mm \u660E\u7EC6\u884C\u5185");
  assertEqual(/text-overflow:\s*ellipsis/.test(nameRule), false, "\u5546\u54C1\u540D\u79F0\u5185\u5BB9\u4E0D\u5E94\u7EE7\u7EED\u4F7F\u7528\u5355\u884C\u7701\u7565\u53F7");
});
runTest("\u914D\u8D27\u5355\u6253\u5370\u5E94\u7ED1\u5B9A\u4E13\u7528 A4 \u9875\u9762\u8FB9\u8DDD", () => {
  const printCssSource = fs.readFileSync(path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/print.css"), "utf8");
  assertEqual(printCssSource.includes("@page store-order-picking"), true, "\u5E94\u4FDD\u7559\u914D\u8D27\u5355\u4E13\u7528\u547D\u540D\u9875\u9762");
  assertEqual(
    /\.store-order-picking-paper\s*\{[\s\S]*?page:\s*store-order-picking/.test(printCssSource),
    true,
    "\u914D\u8D27\u5355\u7EB8\u5F20\u5143\u7D20\u5E94\u7ED1\u5B9A\u547D\u540D\u9875\u9762"
  );
});
runTest("\u914D\u8D27\u5355 PDF \u9875\u7801\u533A\u57DF\u5E94\u6709\u72EC\u7ACB\u5E95\u90E8\u7559\u767D", () => {
  const pickingListSource = fs.readFileSync(path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/PickingList.tsx"), "utf8");
  const printCssSource = fs.readFileSync(path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/print.css"), "utf8");
  const pageRule = readCssRule(printCssSource, ".store-order-pdf-page.store-order-picking-paper");
  const bodyRule = readCssRule(printCssSource, ".store-order-pdf-page-body");
  const pageNumberRule = readCssRule(printCssSource, ".store-order-pdf-page-number");
  assertEqual(/padding:\s*6mm 5mm 12mm/.test(pageRule), true, "PDF \u9875\u9762\u5E95\u90E8\u5E94\u9884\u7559 12mm");
  assertEqual(/padding-bottom:\s*12mm/.test(bodyRule), true, "PDF \u5185\u5BB9\u533A\u5E95\u90E8\u5E94\u907F\u5F00\u9875\u7801");
  assertEqual(/bottom:\s*4mm/.test(pageNumberRule), true, "\u9875\u7801\u5E94\u56FA\u5B9A\u5728\u5E95\u90E8\u7559\u767D\u533A\u57DF\u5185");
  assertEqual(
    pickingListSource.includes("t('warehouse.pickingList.pageNumber', { current: pageIndex + 1, total: pdfPages.length })"),
    true,
    "PDF \u9875\u7801\u5E94\u4F7F\u7528\u914D\u8D27\u5355\u4E13\u7528\u56FD\u9645\u5316\u6587\u6848"
  );
  assertEqual(pickingListSource.includes("\u7B2C ${pageIndex + 1} / ${pdfPages.length} \u9875"), false, "PDF \u9875\u7801\u4E0D\u5E94\u786C\u7F16\u7801\u4E2D\u6587\u6A21\u677F");
});
runTest("\u914D\u8D27\u5355 PDF \u9875\u7801\u5E94\u5305\u542B\u4E2D\u82F1\u6587\u7FFB\u8BD1", () => {
  const zhLocale = JSON.parse(fs.readFileSync(path.resolve(process.cwd(), "src/i18n/locales/zh.json"), "utf8"));
  const enLocale = JSON.parse(fs.readFileSync(path.resolve(process.cwd(), "src/i18n/locales/en.json"), "utf8"));
  assertEqual(zhLocale.warehouse.pickingList.pageNumber, "\u7B2C {{current}} / {{total}} \u9875", "\u4E2D\u6587 PDF \u9875\u7801\u5E94\u663E\u793A\u7B2C x / y \u9875");
  assertEqual(enLocale.warehouse.pickingList.pageNumber, "Page {{current}} / {{total}}", "\u82F1\u6587 PDF \u9875\u7801\u5E94\u663E\u793A Page x / y");
});
console.log("pickingListLogic.test: ok");
