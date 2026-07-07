// src/pages/PosAdmin/LocalSupplierInvoices/InvoiceEdit/pasteDetails.ts
var defaultPasteFieldOrder = [
  "itemNumber",
  "barcode",
  "productName",
  "quantity",
  "purchasePrice",
  "newAutoRetailPrice",
  "retailPrice"
];
function normalizeCellLineBreaks(value) {
  return value.replace(/\r\n/g, "\n").replace(/\r/g, "\n");
}
function splitCellLines(value) {
  return normalizeCellLineBreaks(value).split("\n").map((line) => line.trim());
}
function mergeCellText(value) {
  if (value === void 0) return void 0;
  return normalizeCellLineBreaks(value).replace(/\n+/g, " ").replace(/\s+/g, " ").trim();
}
function hasCellLineBreak(value) {
  return value !== void 0 && /[\r\n]/.test(value);
}
function normalizeHeaderCell(value) {
  return mergeCellText(value)?.toLowerCase().replace(/[^a-z0-9\u4e00-\u9fa5]/g, "");
}
function isHeaderCell(field, value) {
  const normalized = normalizeHeaderCell(value);
  if (!normalized) return false;
  const headers = {
    itemNumber: ["itemno", "itemnumber", "item", "\u8D27\u53F7"],
    barcode: ["barcode", "\u6761\u7801"],
    productName: ["description", "desc", "productname", "\u5546\u54C1\u540D\u79F0"],
    quantity: ["invoiceqty", "qty", "quantity", "\u6570\u91CF"],
    purchasePrice: ["priceexgst", "price", "purchaseprice", "\u672C\u6B21\u8FDB\u8D27\u4EF7", "\u8FDB\u8D27\u4EF7"],
    newAutoRetailPrice: ["newautoretailprice", "\u65B0\u81EA\u52A8\u96F6\u552E\u4EF7"],
    retailPrice: ["retailprice", "\u96F6\u552E\u4EF7"]
  };
  return field !== "skip" && headers[field].includes(normalized);
}
function isPasteHeaderRow(cols, fieldOrder) {
  let mappedCells = 0;
  let headerCells = 0;
  fieldOrder.forEach((field, index) => {
    if (field === "skip" || !cols[index]?.trim()) return;
    mappedCells += 1;
    if (isHeaderCell(field, cols[index])) {
      headerCells += 1;
    }
  });
  return mappedCells > 0 && mappedCells === headerCells && headerCells >= 2;
}
function parsePasteCells(text) {
  if (!text.trim()) return [];
  const normalized = normalizeCellLineBreaks(text);
  const rows = [];
  let row = [];
  let cell = "";
  let inQuotedCell = false;
  for (let index = 0; index < normalized.length; index += 1) {
    const char = normalized[index];
    const nextChar = normalized[index + 1];
    if (char === '"') {
      if (inQuotedCell && nextChar === '"') {
        cell += '"';
        index += 1;
        continue;
      }
      if (inQuotedCell || cell.length === 0) {
        inQuotedCell = !inQuotedCell;
        continue;
      }
    }
    if (char === "	" && !inQuotedCell) {
      row.push(cell);
      cell = "";
      continue;
    }
    if (char === "\n" && !inQuotedCell) {
      row.push(cell);
      rows.push(row);
      row = [];
      cell = "";
      continue;
    }
    cell += char;
  }
  row.push(cell);
  rows.push(row);
  return rows.filter((currentRow) => currentRow.some((currentCell) => currentCell.trim()));
}
function parsePastedNumber(value) {
  if (!value?.trim()) return void 0;
  const normalized = value.trim().replace(/,/g, "").replace(/\s+/g, "").replace(/[^\d.-]/g, "");
  if (!normalized || normalized === "-" || normalized === "." || normalized === "-.") {
    return void 0;
  }
  const parsed = Number(normalized);
  return Number.isNaN(parsed) ? void 0 : parsed;
}
function parsePastedBarcode(value) {
  if (!value?.trim()) return { additionalBarcodes: [] };
  const normalized = value.trim().replace(/^'+/, "").replace(/条码|barcode|bar\s*code|ean|upc/gi, " ").replace(/[\s:：]+/g, "");
  const barcodes = [];
  const seen = /* @__PURE__ */ new Set();
  normalized.split(/[，,;；、]+/).map((barcode2) => barcode2.trim()).filter(Boolean).forEach((barcode2) => {
    const key = barcode2.toUpperCase();
    if (seen.has(key)) return;
    seen.add(key);
    barcodes.push(barcode2);
  });
  const [barcode, ...additionalBarcodes] = barcodes;
  return { barcode, additionalBarcodes };
}
function parsePastedItemNumber(value) {
  if (!value?.trim()) return void 0;
  const normalized = value.trim().replace(/^'+/, "");
  return normalized || void 0;
}
function getSmartSplitPlan(cols, fieldOrder) {
  const businessCells = fieldOrder.map((field, index) => ({ field, value: cols[index] })).filter(({ field, value }) => field !== "skip" && Boolean(value?.trim()));
  const businessLineCounts = businessCells.map(({ value }) => splitCellLines(value ?? "").length);
  const hasBusinessMultiline = businessLineCounts.some((count) => count > 1);
  const splitCount = businessLineCounts[0] ?? 0;
  const canSplit = businessCells.length > 1 && splitCount > 1 && businessLineCounts.every((count) => count === splitCount);
  return {
    canSplit,
    splitCount: canSplit ? splitCount : 1,
    hasBusinessMultiline
  };
}
function createSmartSplitCols(cols, fieldOrder, rowIndex) {
  return cols.map((value, index) => {
    const field = fieldOrder[index];
    if (field === "skip" || !value?.trim()) return value;
    return splitCellLines(value)[rowIndex] ?? value;
  });
}
function parsePasteColumns(cols, fieldOrder, options) {
  const row = {};
  fieldOrder.forEach((field, index) => {
    if (field === "skip") return;
    const value = mergeCellText(cols[index]);
    if (field === "quantity" || field === "purchasePrice" || field === "newAutoRetailPrice" || field === "retailPrice") {
      const parsedNumber = parsePastedNumber(value);
      row[field] = field === "retailPrice" && options.normalizeRetailPrice && parsedNumber !== void 0 ? normalizePastedRetailPrice(parsedNumber) : parsedNumber;
      return;
    }
    if (field === "barcode") {
      const parsedBarcode = parsePastedBarcode(value);
      row[field] = parsedBarcode.barcode;
      row.additionalBarcodes = parsedBarcode.additionalBarcodes.length ? parsedBarcode.additionalBarcodes : void 0;
      return;
    }
    if (field === "itemNumber") {
      row[field] = parsePastedItemNumber(value);
      return;
    }
    row[field] = value || void 0;
  });
  return row;
}
function analyzePasteMultilineCells(text, fieldOrder = defaultPasteFieldOrder) {
  const rows = parsePasteCells(text);
  let hasMultilineCells = false;
  let unsafeRecordCount = 0;
  rows.forEach((cols) => {
    const hasAnyMultilineCell = cols.some((value) => hasCellLineBreak(value));
    if (!hasAnyMultilineCell) return;
    hasMultilineCells = true;
    const plan = getSmartSplitPlan(cols, fieldOrder);
    if (plan.hasBusinessMultiline && !plan.canSplit) {
      unsafeRecordCount += 1;
    }
  });
  return { hasMultilineCells, unsafeRecordCount };
}
function getPasteTextMaxColumnCount(text) {
  const rows = parsePasteCells(text);
  if (!rows.length) return 0;
  return Math.max(...rows.map((row) => row.length));
}
function normalizePastedRetailPrice(price) {
  if (!Number.isFinite(price) || price < 3) return price;
  const cents = Math.round(price * 100);
  const integerCents = Math.floor(cents / 100) * 100;
  const decimalCents = cents - integerCents;
  if (decimalCents === 0) {
    return Number(((integerCents - 1) / 100).toFixed(2));
  }
  if (decimalCents <= 50) {
    return Number(((integerCents + 50) / 100).toFixed(2));
  }
  return Number(((integerCents + 99) / 100).toFixed(2));
}
function parsePasteText(text, fieldOrder = defaultPasteFieldOrder, options = {}) {
  if (!text.trim()) return [];
  const rows = parsePasteCells(text);
  const multilineCellMode = options.multilineCellMode ?? "merge";
  return rows.flatMap((cols) => {
    if (isPasteHeaderRow(cols, fieldOrder)) {
      return [];
    }
    if (multilineCellMode === "smartSplit") {
      const plan = getSmartSplitPlan(cols, fieldOrder);
      if (plan.canSplit) {
        return Array.from({ length: plan.splitCount }, (_, rowIndex) => parsePasteColumns(createSmartSplitCols(cols, fieldOrder, rowIndex), fieldOrder, options));
      }
    }
    return [parsePasteColumns(cols, fieldOrder, options)];
  });
}

// src/pages/PosAdmin/LocalSupplierInvoices/InvoiceEdit/pasteDetails.test.ts
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
async function runTest(name, execute) {
  try {
    await execute();
    console.log(`ok - ${name}`);
    return null;
  } catch (error) {
    const reason = error instanceof Error ? error.message : String(error);
    console.error(`not ok - ${name}`);
    console.error(reason);
    return `${name}: ${reason}`;
  }
}
async function main() {
  const failures = [];
  const retailNormalizeFailure = await runTest("\u96F6\u552E\u4EF7\u89C4\u8303\u5316\u5E94\u6309 3 \u5143\u8D77\u89C4\u5219\u5F52\u5230 .50 \u6216 .99", () => {
    assertEqual(normalizePastedRetailPrice(5), 4.99, "\u6574\u6570 5 \u5E94\u6539\u4E3A 4.99");
    assertEqual(normalizePastedRetailPrice(4.1), 4.5, "4.1 \u5E94\u6539\u4E3A 4.50");
    assertEqual(normalizePastedRetailPrice(4.6), 4.99, "4.6 \u5E94\u6539\u4E3A 4.99");
    assertEqual(normalizePastedRetailPrice(4.5), 4.5, "4.5 \u5E94\u4FDD\u6301 4.50");
    assertEqual(normalizePastedRetailPrice(3), 2.99, "\u6574\u6570 3 \u5E94\u6539\u4E3A 2.99");
    assertEqual(normalizePastedRetailPrice(1), 1, "\u6574\u6570 1 \u4E0D\u5E94\u6539\u53D8");
    assertEqual(normalizePastedRetailPrice(2), 2, "\u6574\u6570 2 \u4E0D\u5E94\u6539\u53D8");
    assertEqual(normalizePastedRetailPrice(2.9), 2.9, "\u5C0F\u4E8E 3 \u7684\u4EF7\u683C\u4E0D\u5E94\u6539\u53D8");
  });
  if (retailNormalizeFailure) failures.push(retailNormalizeFailure);
  const parseRetailFailure = await runTest("\u7C98\u8D34\u89E3\u6790\u9ED8\u8BA4\u53EA\u89C4\u8303\u5316\u96F6\u552E\u4EF7\u5217", () => {
    const parsed = parsePasteText(
      [
        "SKU-1	111	\u5546\u54C11	1	5	5	5",
        "SKU-2	222	\u5546\u54C12	1	4.1	5	4.1",
        "SKU-3	333	\u5546\u54C13	1	4.6	5	4.6",
        "SKU-4	444	\u5546\u54C14	1	1	5	1",
        "SKU-5	555	\u5546\u54C15	1	2	5	2"
      ].join("\n"),
      defaultPasteFieldOrder,
      { normalizeRetailPrice: true }
    );
    assertDeepEqual(
      parsed.map((item) => ({
        purchasePrice: item.purchasePrice,
        newAutoRetailPrice: item.newAutoRetailPrice,
        retailPrice: item.retailPrice
      })),
      [
        { purchasePrice: 5, newAutoRetailPrice: 5, retailPrice: 4.99 },
        { purchasePrice: 4.1, newAutoRetailPrice: 5, retailPrice: 4.5 },
        { purchasePrice: 4.6, newAutoRetailPrice: 5, retailPrice: 4.99 },
        { purchasePrice: 1, newAutoRetailPrice: 5, retailPrice: 1 },
        { purchasePrice: 2, newAutoRetailPrice: 5, retailPrice: 2 }
      ],
      "\u53EA\u5E94\u89C4\u8303\u5316 retailPrice\uFF0C\u4E0D\u5E94\u6539\u53D8 purchasePrice \u548C newAutoRetailPrice"
    );
  });
  if (parseRetailFailure) failures.push(parseRetailFailure);
  const disabledNormalizeFailure = await runTest("\u5173\u95ED\u96F6\u552E\u4EF7\u89C4\u8303\u5316\u65F6\u5E94\u4FDD\u7559\u539F\u59CB\u96F6\u552E\u4EF7\u6570\u5B57", () => {
    const parsed = parsePasteText("SKU-1	111	\u5546\u54C11	1	5	5	5", defaultPasteFieldOrder, {
      normalizeRetailPrice: false
    });
    assertEqual(parsed[0]?.retailPrice, 5, "\u5173\u95ED\u89C4\u8303\u5316\u540E retailPrice \u5E94\u4FDD\u6301 5");
    assertEqual(parsed[0]?.newAutoRetailPrice, 5, "\u5173\u95ED\u89C4\u8303\u5316\u540E newAutoRetailPrice \u5E94\u4FDD\u6301 5");
  });
  if (disabledNormalizeFailure) failures.push(disabledNormalizeFailure);
  const currencyFormatFailure = await runTest("\u8D27\u5E01\u683C\u5F0F\u96F6\u552E\u4EF7\u5E94\u5148\u89E3\u6790\u518D\u89C4\u8303\u5316", () => {
    const parsed = parsePasteText("SKU-1	111	\u5546\u54C11	1	A$5.00	AUD 5.00	$5.00", defaultPasteFieldOrder, {
      normalizeRetailPrice: true
    });
    assertEqual(parsed[0]?.purchasePrice, 5, "\u8FDB\u8D27\u4EF7\u8D27\u5E01\u683C\u5F0F\u5E94\u89E3\u6790\u4E3A 5");
    assertEqual(parsed[0]?.newAutoRetailPrice, 5, "\u65B0\u81EA\u52A8\u96F6\u552E\u4EF7\u8D27\u5E01\u683C\u5F0F\u4E0D\u5E94\u89C4\u8303\u5316");
    assertEqual(parsed[0]?.retailPrice, 4.99, "\u96F6\u552E\u4EF7\u8D27\u5E01\u683C\u5F0F\u5E94\u89E3\u6790\u540E\u89C4\u8303\u5316\u4E3A 4.99");
  });
  if (currencyFormatFailure) failures.push(currencyFormatFailure);
  const barcodeLabelFailure = await runTest("\u7C98\u8D34\u6761\u7801\u5217\u5E94\u53BB\u6389\u968F\u5355\u5143\u683C\u5E26\u5165\u7684\u6761\u7801\u6807\u7B7E", () => {
    const [suffixLabelRow] = parsePasteText("SKU-1	9357405070864 \u6761\u7801	\u5546\u54C11	1	5	5	5", defaultPasteFieldOrder);
    const [prefixLabelRow] = parsePasteText("SKU-2	\u6761\u7801\uFF1A9357405070864	\u5546\u54C12	1	5	5	5", defaultPasteFieldOrder);
    const [excelTextRow] = parsePasteText("SKU-3	'9357405070864	\u5546\u54C13	1	5	5	5", defaultPasteFieldOrder);
    assertEqual(suffixLabelRow?.barcode, "9357405070864", "\u6761\u7801\u5C3E\u90E8\u6807\u7B7E\u5E94\u88AB\u53BB\u6389");
    assertEqual(prefixLabelRow?.barcode, "9357405070864", "\u6761\u7801\u524D\u7F6E\u6807\u7B7E\u5E94\u88AB\u53BB\u6389");
    assertEqual(excelTextRow?.barcode, "9357405070864", "Excel \u6587\u672C\u6761\u7801\u524D\u5BFC\u5355\u5F15\u53F7\u5E94\u88AB\u53BB\u6389");
  });
  if (barcodeLabelFailure) failures.push(barcodeLabelFailure);
  const barcodeMultiCodeFailure = await runTest("\u7C98\u8D34\u6761\u7801\u5217\u9047\u5230\u9017\u53F7\u591A\u6761\u7801\u65F6\u5E94\u62C6\u51FA\u4E3B\u6761\u7801\u548C\u526F\u6761\u7801", () => {
    const [row] = parsePasteText(
      "88841	191554890459,191554890480,191554890497,191554890473,191554888418	Women Travel Perfume Assorted 35mL	48	1.6546",
      defaultPasteFieldOrder
    );
    assertEqual(row?.barcode, "191554890459", "\u9017\u53F7\u5206\u9694\u7684\u591A\u6761\u7801\u7B2C\u4E00\u6761\u5E94\u4F5C\u4E3A\u4E3B\u6761\u7801");
    assertDeepEqual(
      row?.additionalBarcodes,
      ["191554890480", "191554890497", "191554890473", "191554888418"],
      "\u5176\u4F59\u6761\u7801\u5E94\u4F5C\u4E3A\u526F\u6761\u7801\u4FDD\u7559"
    );
  });
  if (barcodeMultiCodeFailure) failures.push(barcodeMultiCodeFailure);
  const barcodeMultiSeparatorFailure = await runTest("\u7C98\u8D34\u6761\u7801\u5217\u5E94\u652F\u6301\u4E2D\u6587\u9017\u53F7\u5206\u53F7\u548C\u987F\u53F7\u5206\u9694\u526F\u6761\u7801", () => {
    const [row] = parsePasteText(
      "88842	191554882676\uFF0C191554882690;191554882669\u3001191554888425\uFF1B191554882676	Men Travel Perfume Assorted 35mL	48	1.6546",
      defaultPasteFieldOrder
    );
    assertEqual(row?.barcode, "191554882676", "\u7B2C\u4E00\u6761\u4ECD\u5E94\u4F5C\u4E3A\u4E3B\u6761\u7801");
    assertDeepEqual(
      row?.additionalBarcodes,
      ["191554882690", "191554882669", "191554888425"],
      "\u4E2D\u6587\u6807\u70B9\u5206\u9694\u7684\u526F\u6761\u7801\u5E94\u53BB\u91CD\u540E\u4FDD\u7559\u987A\u5E8F"
    );
  });
  if (barcodeMultiSeparatorFailure) failures.push(barcodeMultiSeparatorFailure);
  const headerRowFailure = await runTest("\u7C98\u8D34\u4F9B\u5E94\u5546\u8868\u683C\u65F6\u5E94\u81EA\u52A8\u8DF3\u8FC7\u8868\u5934\u884C", () => {
    const parsed = parsePasteText(
      [
        "Item No.	Barcode	Description	Invoice Qty	Price (ex GST)",
        "15085-1xLV5085	840417950853	Women Perfumen New Crystal Absolute	6	$2.5000"
      ].join("\n"),
      defaultPasteFieldOrder
    );
    assertEqual(parsed.length, 1, "\u8868\u5934\u884C\u4E0D\u5E94\u4F5C\u4E3A\u5546\u54C1\u660E\u7EC6\u63D0\u4EA4");
    assertEqual(parsed[0]?.itemNumber, "15085-1xLV5085", "\u7B2C\u4E00\u6761\u6570\u636E\u884C\u8D27\u53F7\u5E94\u4FDD\u7559");
    assertEqual(parsed[0]?.quantity, 6, "Invoice Qty \u5E94\u6620\u5C04\u5230\u6570\u91CF");
    assertEqual(parsed[0]?.purchasePrice, 2.5, "Price (ex GST) \u5E94\u6620\u5C04\u5230\u672C\u6B21\u8FDB\u8D27\u4EF7");
  });
  if (headerRowFailure) failures.push(headerRowFailure);
  const itemNumberQuoteFailure = await runTest("\u7C98\u8D34\u8D27\u53F7\u5217\u5E94\u53BB\u6389 Excel \u6587\u672C\u683C\u5F0F\u524D\u5BFC\u5355\u5F15\u53F7", () => {
    const [excelTextRow] = parsePasteText("'027000040	8719987314001	\u5546\u54C11	1	5	5	5", defaultPasteFieldOrder);
    const [multiQuoteRow] = parsePasteText("''SKU-1	8719987314002	\u5546\u54C12	1	5	5	5", defaultPasteFieldOrder);
    const [middleQuoteRow] = parsePasteText("SKU-'KEEP	8719987314003	\u5546\u54C13	1	5	5	5", defaultPasteFieldOrder);
    assertEqual(excelTextRow?.itemNumber, "027000040", "\u8D27\u53F7\u524D\u5BFC\u5355\u5F15\u53F7\u5E94\u88AB\u53BB\u6389");
    assertEqual(multiQuoteRow?.itemNumber, "SKU-1", "\u8D27\u53F7\u591A\u4E2A\u524D\u5BFC\u5355\u5F15\u53F7\u5E94\u5168\u90E8\u53BB\u6389");
    assertEqual(middleQuoteRow?.itemNumber, "SKU-'KEEP", "\u8D27\u53F7\u4E2D\u95F4\u5355\u5F15\u53F7\u5E94\u4FDD\u7559");
  });
  if (itemNumberQuoteFailure) failures.push(itemNumberQuoteFailure);
  const multilineMergeFailure = await runTest("\u9ED8\u8BA4\u5408\u5E76\u5355\u5143\u683C\u5185\u6362\u884C\u5E76\u53EA\u751F\u6210\u4E00\u6761\u660E\u7EC6", () => {
    const parsed = parsePasteText('SKU-1	111	"Gloves Powder\nx Thickn\ness 0.10mm)"	1	5	5	5', defaultPasteFieldOrder);
    const analysis = analyzePasteMultilineCells('SKU-1	111	"Gloves Powder\nx Thickn\ness 0.10mm)"	1	5	5	5', defaultPasteFieldOrder);
    assertEqual(parsed.length, 1, "\u5355\u5143\u683C\u5185\u6362\u884C\u4E0D\u5E94\u88AB\u62C6\u6210\u591A\u6761\u660E\u7EC6");
    assertEqual(parsed[0]?.productName, "Gloves Powder x Thickn ess 0.10mm)", "\u5546\u54C1\u540D\u5355\u5143\u683C\u5185\u6362\u884C\u5E94\u5408\u5E76\u4E3A\u7A7A\u683C");
    assertEqual(analysis.hasMultilineCells, true, "\u5E94\u68C0\u6D4B\u5230\u5355\u5143\u683C\u5185\u6362\u884C");
    assertEqual(analysis.unsafeRecordCount, 1, "\u53EA\u6709\u5546\u54C1\u540D\u591A\u884C\u65F6\u4E0D\u6EE1\u8DB3\u5B89\u5168\u62C6\u5206\u6761\u4EF6");
  });
  if (multilineMergeFailure) failures.push(multilineMergeFailure);
  const multilineSmartSplitFailure = await runTest("\u667A\u80FD\u62C6\u5206\u5E94\u5728\u6240\u6709\u4E1A\u52A1\u5217\u591A\u884C\u6570\u4E00\u81F4\u65F6\u62C6\u6210\u591A\u6761\u660E\u7EC6", () => {
    const parsed = parsePasteText(
      '"SKU-1\nSKU-2\nSKU-3"	"111\n222\n333"	"\u5546\u54C11\n\u5546\u54C12\n\u5546\u54C13"	"1\n2\n3"	"5\n6\n7"	"5.5\n6.5\n7.5"	"8\n9\n10"',
      defaultPasteFieldOrder,
      { multilineCellMode: "smartSplit" }
    );
    assertEqual(parsed.length, 3, "\u6240\u6709\u4E1A\u52A1\u5217\u90FD\u6709 3 \u884C\u65F6\u5E94\u62C6\u6210 3 \u6761");
    assertDeepEqual(
      parsed.map((item) => ({
        itemNumber: item.itemNumber,
        barcode: item.barcode,
        productName: item.productName,
        quantity: item.quantity,
        purchasePrice: item.purchasePrice,
        newAutoRetailPrice: item.newAutoRetailPrice,
        retailPrice: item.retailPrice
      })),
      [
        { itemNumber: "SKU-1", barcode: "111", productName: "\u5546\u54C11", quantity: 1, purchasePrice: 5, newAutoRetailPrice: 5.5, retailPrice: 8 },
        { itemNumber: "SKU-2", barcode: "222", productName: "\u5546\u54C12", quantity: 2, purchasePrice: 6, newAutoRetailPrice: 6.5, retailPrice: 9 },
        { itemNumber: "SKU-3", barcode: "333", productName: "\u5546\u54C13", quantity: 3, purchasePrice: 7, newAutoRetailPrice: 7.5, retailPrice: 10 }
      ],
      "\u667A\u80FD\u62C6\u5206\u5E94\u6309\u540C\u4E00\u884C\u53F7\u7EC4\u88C5\u5B57\u6BB5"
    );
  });
  if (multilineSmartSplitFailure) failures.push(multilineSmartSplitFailure);
  const multilinePartialFailure = await runTest("\u667A\u80FD\u62C6\u5206\u9047\u5230\u90E8\u5206\u5217\u591A\u884C\u65F6\u5E94\u81EA\u52A8\u5408\u5E76", () => {
    const parsed = parsePasteText('SKU-1	111	"\u5546\u54C11\n\u5546\u54C11\u8865\u5145"	1	5	5	5', defaultPasteFieldOrder, {
      multilineCellMode: "smartSplit"
    });
    assertEqual(parsed.length, 1, "\u53EA\u6709\u90E8\u5206\u5217\u591A\u884C\u65F6\u4E0D\u5E94\u9519\u4F4D\u62C6\u5206");
    assertEqual(parsed[0]?.productName, "\u5546\u54C11 \u5546\u54C11\u8865\u5145", "\u81EA\u52A8\u5408\u5E76\u65F6\u5E94\u628A\u5185\u90E8\u6362\u884C\u538B\u6210\u7A7A\u683C");
  });
  if (multilinePartialFailure) failures.push(multilinePartialFailure);
  const multilineMismatchFailure = await runTest("\u667A\u80FD\u62C6\u5206\u9047\u5230\u591A\u884C\u6570\u91CF\u4E0D\u4E00\u81F4\u65F6\u5E94\u81EA\u52A8\u5408\u5E76", () => {
    const parsed = parsePasteText(
      '"SKU-1\nSKU-2"	"111\n222\n333"	"\u5546\u54C11\n\u5546\u54C12"	"1\n2"	"5\n6"	"5\n6"	"8\n9"',
      defaultPasteFieldOrder,
      { multilineCellMode: "smartSplit" }
    );
    assertEqual(parsed.length, 1, "\u591A\u884C\u6570\u91CF\u4E0D\u4E00\u81F4\u65F6\u4E0D\u5E94\u62C6\u5206");
    assertEqual(parsed[0]?.itemNumber, "SKU-1 SKU-2", "\u81EA\u52A8\u5408\u5E76\u65F6\u5E94\u4FDD\u7559\u8D27\u53F7\u5185\u5BB9");
    assertEqual(parsed[0]?.productName, "\u5546\u54C11 \u5546\u54C12", "\u81EA\u52A8\u5408\u5E76\u65F6\u5E94\u4FDD\u7559\u5546\u54C1\u540D\u5185\u5BB9");
  });
  if (multilineMismatchFailure) failures.push(multilineMismatchFailure);
  const multilineSkipFailure = await runTest("\u667A\u80FD\u62C6\u5206\u5224\u65AD\u5E94\u5FFD\u7565\u8DF3\u8FC7\u5217", () => {
    const parsed = parsePasteText(
      '"SKU-1\nSKU-2"	"\u5907\u6CE81\n\u5907\u6CE82\n\u5907\u6CE83"	"111\n222"	"\u5546\u54C11\n\u5546\u54C12"	"1\n2"',
      ["itemNumber", "skip", "barcode", "productName", "quantity"],
      { multilineCellMode: "smartSplit" }
    );
    assertEqual(parsed.length, 2, "\u8DF3\u8FC7\u5217\u591A\u884C\u6570\u4E0D\u540C\u4E0D\u5E94\u963B\u6B62\u4E1A\u52A1\u5217\u62C6\u5206");
    assertEqual(parsed[1]?.itemNumber, "SKU-2", "\u7B2C\u4E8C\u884C\u8D27\u53F7\u5E94\u6765\u81EA\u7B2C\u4E8C\u4E2A\u5355\u5143\u683C\u884C");
    assertEqual(parsed[1]?.barcode, "222", "\u7B2C\u4E8C\u884C\u6761\u7801\u5E94\u6765\u81EA\u7B2C\u4E8C\u4E2A\u5355\u5143\u683C\u884C");
    assertEqual(parsed[1]?.productName, "\u5546\u54C12", "\u7B2C\u4E8C\u884C\u5546\u54C1\u540D\u5E94\u6765\u81EA\u7B2C\u4E8C\u4E2A\u5355\u5143\u683C\u884C");
  });
  if (multilineSkipFailure) failures.push(multilineSkipFailure);
  const multilineOrdinaryRowsFailure = await runTest("\u666E\u901A\u591A\u6761 Excel \u884C\u4E0D\u53D7\u591A\u884C\u5355\u5143\u683C\u89E3\u6790\u5F71\u54CD", () => {
    const parsed = parsePasteText(["SKU-1	111	\u5546\u54C11	1	5	5	5", "SKU-2	222	\u5546\u54C12	2	6	6	6"].join("\n"), defaultPasteFieldOrder, {
      multilineCellMode: "smartSplit"
    });
    assertEqual(parsed.length, 2, "\u666E\u901A\u4E24\u884C\u4ECD\u5E94\u89E3\u6790\u4E3A\u4E24\u6761\u660E\u7EC6");
    assertEqual(getPasteTextMaxColumnCount('SKU-1	111	"\u5546\u54C11\n\u8865\u5145"	1	5	5	5'), 7, "\u5217\u6570\u8BA1\u7B97\u5E94\u5FFD\u7565\u5355\u5143\u683C\u5185\u6362\u884C");
  });
  if (multilineOrdinaryRowsFailure) failures.push(multilineOrdinaryRowsFailure);
  if (failures.length) {
    throw new Error(failures.join("\n"));
  }
  console.log("pasteDetails.test: ok");
}
main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exit(1);
});
