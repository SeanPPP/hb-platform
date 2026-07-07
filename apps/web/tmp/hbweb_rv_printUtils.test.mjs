// src/pages/Warehouse/StoreOrders/printUtils.ts
var PDF_IMAGE_FORMAT = "JPEG";
var PDF_IMAGE_MIME_TYPE = "image/jpeg";
var PDF_IMAGE_QUALITY = 0.95;
function normalizePrintLocale(locale) {
  return locale?.toLowerCase().startsWith("en") ? "en-US" : "zh-CN";
}
function parseLeadingDateParts(value) {
  const dateParts = value.trim().match(/^(\d{4})[-/](\d{1,2})[-/](\d{1,2})/);
  if (!dateParts) {
    return null;
  }
  return {
    year: Number(dateParts[1]),
    month: Number(dateParts[2]),
    day: Number(dateParts[3])
  };
}
function formatPrintDate(value, withTime = true, locale) {
  if (!withTime && typeof value === "string") {
    const dateParts = parseLeadingDateParts(value);
    if (dateParts) {
      const localDate = new Date(dateParts.year, dateParts.month - 1, dateParts.day);
      const printLocale2 = normalizePrintLocale(locale);
      return localDate.toLocaleDateString(printLocale2);
    }
  }
  const target = value ? new Date(value) : /* @__PURE__ */ new Date();
  if (Number.isNaN(target.getTime())) {
    return value || "--";
  }
  const printLocale = normalizePrintLocale(locale);
  return withTime ? target.toLocaleString(printLocale, { hour12: false }) : target.toLocaleDateString(printLocale);
}
function formatDatePart(year, month, day) {
  return `${year}-${String(month).padStart(2, "0")}-${String(day).padStart(2, "0")}`;
}
function formatDocumentFileDate(value) {
  if (typeof value === "string") {
    const dateParts = parseLeadingDateParts(value);
    if (dateParts) {
      return formatDatePart(dateParts.year, dateParts.month, dateParts.day);
    }
  }
  const target = value ? new Date(value) : /* @__PURE__ */ new Date();
  const safeTarget = Number.isNaN(target.getTime()) ? /* @__PURE__ */ new Date() : target;
  return formatDatePart(safeTarget.getFullYear(), safeTarget.getMonth() + 1, safeTarget.getDate());
}
function sanitizeFileNamePart(value) {
  const normalized = (value || "").replace(/[\\/:*?"<>|]/g, "_").replace(/\s+/g, " ").trim().replace(/[\s_]+/g, "_");
  return normalized;
}
function buildDocumentFileName(prefix, storeName, orderNo, extension, fallbackTexts, datePart) {
  const unknownStoreText = fallbackTexts.unknownStore;
  const unknownOrderText = fallbackTexts.unknownOrder;
  const safePrefix = sanitizeFileNamePart(prefix);
  const safeStoreName = sanitizeFileNamePart(storeName || unknownStoreText);
  const safeOrderNo = sanitizeFileNamePart(orderNo || unknownOrderText);
  const safeDatePart = datePart ? sanitizeFileNamePart(datePart) : "";
  return [safePrefix, safeStoreName, safeOrderNo, safeDatePart].filter(Boolean).join("_") + `.${extension}`;
}
function buildPdfSlicePlan(imageHeight, pageHeightInPx, avoidBreakOffsets = []) {
  const normalizedImageHeight = Math.max(0, Math.floor(imageHeight));
  if (!Number.isFinite(normalizedImageHeight) || normalizedImageHeight <= 0) {
    return [];
  }
  const normalizedPageHeight = Math.max(1, Math.floor(pageHeightInPx));
  const normalizedBreakOffsets = Array.from(
    new Set(
      avoidBreakOffsets.filter((offset) => Number.isFinite(offset)).map((offset) => Math.floor(offset)).filter((offset) => offset > 0 && offset <= normalizedImageHeight)
    )
  ).sort((left, right) => left - right);
  const slices = [];
  let offsetY = 0;
  while (offsetY < normalizedImageHeight) {
    const defaultEndY = Math.min(offsetY + normalizedPageHeight, normalizedImageHeight);
    const candidateBreakOffsets = normalizedBreakOffsets.filter((breakOffset) => breakOffset > offsetY && breakOffset <= defaultEndY);
    const boundaryEndY = candidateBreakOffsets[candidateBreakOffsets.length - 1];
    const endY = boundaryEndY ?? defaultEndY;
    const height = Math.max(1, endY - offsetY);
    slices.push({ offsetY, height });
    offsetY += height;
  }
  return slices;
}
function paintPdfSlice(context, sourceCanvas, imageWidth, slice) {
  context.fillStyle = "#ffffff";
  context.fillRect(0, 0, imageWidth, slice.height);
  context.drawImage(
    sourceCanvas,
    0,
    slice.offsetY,
    imageWidth,
    slice.height,
    0,
    0,
    imageWidth,
    slice.height
  );
}
function getPdfSliceImageData(sliceCanvas) {
  return sliceCanvas.toDataURL(PDF_IMAGE_MIME_TYPE, PDF_IMAGE_QUALITY);
}
function collectElementBreakOffsets(root, rowSelector, footerSelector) {
  const rootTop = root.getBoundingClientRect().top;
  const rows = Array.from(root.querySelectorAll(rowSelector));
  const footer = footerSelector ? root.querySelector(footerSelector) : null;
  const offsets = rows.map((row) => row.getBoundingClientRect().top - rootTop);
  if (footer) {
    offsets.push(footer.getBoundingClientRect().top - rootTop);
  }
  offsets.push(root.scrollHeight);
  return offsets.filter((offset) => Number.isFinite(offset) && offset > 0);
}

// src/pages/Warehouse/StoreOrders/printUtils.test.ts
import fs from "node:fs";
import path from "node:path";
function assertDeepEqual(actual, expected, label) {
  const actualText = JSON.stringify(actual);
  const expectedText = JSON.stringify(expected);
  if (actualText !== expectedText) {
    throw new Error(`${label}\u3002Expected: ${expectedText}, received: ${actualText}`);
  }
}
function assertEqual(actual, expected, label) {
  if (actual !== expected) {
    throw new Error(`${label}\u3002Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
function runTest(name, execute) {
  execute();
  console.log(`ok - ${name}`);
}
runTest("\u5207\u7247\u9AD8\u5EA6\u5E94\u5411\u4E0B\u53D6\u6574\u4E3A\u6574\u6570\u50CF\u7D20\uFF0C\u5E76\u5B8C\u6574\u8986\u76D6\u5269\u4F59\u9AD8\u5EA6", () => {
  const slices = buildPdfSlicePlan(250, 104.7);
  assertDeepEqual(slices, [
    { offsetY: 0, height: 104 },
    { offsetY: 104, height: 104 },
    { offsetY: 208, height: 42 }
  ], "\u6D6E\u70B9\u9875\u9AD8\u5E94\u5207\u6210\u6574\u6570\u50CF\u7D20\u5207\u7247");
});
runTest("\u9875\u9AD8\u5C0F\u4E8E 1 \u50CF\u7D20\u65F6\u4E5F\u4E0D\u5E94\u751F\u6210\u7A7A\u5207\u7247", () => {
  const slices = buildPdfSlicePlan(3, 0.4);
  assertDeepEqual(slices, [
    { offsetY: 0, height: 1 },
    { offsetY: 1, height: 1 },
    { offsetY: 2, height: 1 }
  ], "\u6781\u5C0F\u9875\u9AD8\u65F6\u5E94\u56DE\u9000\u5230 1 \u50CF\u7D20\u5207\u7247");
  assertEqual(slices.reduce((sum, item) => sum + item.height, 0), 3, "\u6240\u6709\u5207\u7247\u9AD8\u5EA6\u4E4B\u548C\u5E94\u7B49\u4E8E\u539F\u56FE\u9AD8\u5EA6");
});
runTest("PDF \u5207\u7247\u5E94\u4F18\u5148\u8D34\u5408\u884C\u8FB9\u754C\uFF0C\u907F\u514D\u628A\u8868\u683C\u884C\u5207\u6210\u4E24\u534A", () => {
  const slices = buildPdfSlicePlan(260, 100, [30, 90, 150, 210, 260]);
  assertDeepEqual(slices, [
    { offsetY: 0, height: 90 },
    { offsetY: 90, height: 60 },
    { offsetY: 150, height: 60 },
    { offsetY: 210, height: 50 }
  ], "\u5207\u7247\u7ED3\u675F\u4F4D\u7F6E\u5E94\u4F18\u5148\u9009\u62E9\u5F53\u524D\u9875\u5185\u5BB9\u8303\u56F4\u5185\u7684\u6700\u540E\u4E00\u4E2A\u884C\u8FB9\u754C");
});
runTest("\u6CA1\u6709\u53EF\u7528\u884C\u8FB9\u754C\u65F6 PDF \u5207\u7247\u5E94\u56DE\u9000\u5230\u6807\u51C6\u9875\u9AD8", () => {
  const slices = buildPdfSlicePlan(260, 100, [140, 260]);
  assertDeepEqual(slices, [
    { offsetY: 0, height: 100 },
    { offsetY: 100, height: 40 },
    { offsetY: 140, height: 100 },
    { offsetY: 240, height: 20 }
  ], "\u884C\u8FB9\u754C\u4E0D\u5728\u5F53\u524D\u9875\u8303\u56F4\u5185\u65F6\u5E94\u4FDD\u6301\u524D\u8FDB\uFF0C\u907F\u514D\u6B7B\u5FAA\u73AF");
});
runTest("PDF \u907F\u514D\u5207\u65AD\u504F\u79FB\u5E94\u4ECE\u660E\u7EC6\u884C\u3001\u9875\u811A\u548C\u6EDA\u52A8\u9AD8\u5EA6\u6536\u96C6", () => {
  const createElement = (top) => ({
    getBoundingClientRect: () => ({ top })
  });
  const root = {
    scrollHeight: 260,
    getBoundingClientRect: () => ({ top: 10 }),
    querySelectorAll: (selector) => selector === "tbody tr" ? [createElement(20), createElement(80), createElement(140)] : [],
    querySelector: (selector) => selector === ".footer" ? createElement(220) : null
  };
  assertDeepEqual(
    collectElementBreakOffsets(root, "tbody tr", ".footer"),
    [10, 70, 130, 210, 260],
    "\u5E94\u8FD4\u56DE\u76F8\u5BF9\u6839\u5143\u7D20\u7684\u884C\u9876\u90E8\u3001\u9875\u811A\u9876\u90E8\u548C\u6839\u6EDA\u52A8\u9AD8\u5EA6"
  );
});
runTest("PDF \u56FE\u7247\u8F93\u51FA\u5E94\u9501\u5B9A JPEG \u683C\u5F0F\uFF0C\u907F\u514D\u56DE\u9000\u5230 PNG", () => {
  const calls = [];
  const fakeCanvas = {
    toDataURL: (mimeType, quality) => {
      calls.push([mimeType, quality]);
      return "data:image/jpeg;base64,abc";
    }
  };
  const imageData = getPdfSliceImageData(fakeCanvas);
  assertEqual(PDF_IMAGE_FORMAT, "JPEG", "\u5199\u5165 jsPDF \u7684\u56FE\u7247\u683C\u5F0F\u5E94\u4E3A JPEG");
  assertEqual(PDF_IMAGE_MIME_TYPE, "image/jpeg", "canvas \u8F93\u51FA MIME \u5E94\u4E3A image/jpeg");
  assertEqual(PDF_IMAGE_QUALITY, 0.95, "JPEG \u8D28\u91CF\u5E94\u4FDD\u6301 0.95");
  assertEqual(imageData, "data:image/jpeg;base64,abc", "\u5E94\u8FD4\u56DE canvas \u8F93\u51FA\u7684 JPEG data URL");
  assertDeepEqual(calls, [["image/jpeg", 0.95]], "toDataURL \u5E94\u6309 JPEG \u53C2\u6570\u8C03\u7528");
});
runTest("\u6587\u4EF6\u540D\u65E5\u671F\u5E94\u56FA\u5B9A\u683C\u5F0F\u5316\u4E3A yyyy-MM-dd", () => {
  assertEqual(formatDocumentFileDate("2026-06-04T12:30:00"), "2026-06-04", "ISO \u65E5\u671F\u5E94\u76F4\u63A5\u63D0\u53D6\u5E74\u6708\u65E5");
  assertEqual(formatDocumentFileDate("2026/6/4"), "2026-06-04", "\u659C\u6760\u65E5\u671F\u4E5F\u5E94\u8865\u96F6");
});
runTest("date-only \u53D1\u7968\u65E5\u671F\u663E\u793A\u4E0D\u5E94\u53D7 UTC \u8D1F\u65F6\u533A\u5F71\u54CD", () => {
  const originalTimezone = process.env.TZ;
  process.env.TZ = "America/Los_Angeles";
  try {
    assertEqual(formatPrintDate("2026-06-05", false, "en-US"), "6/5/2026", "date-only \u5B57\u7B26\u4E32\u5E94\u6309\u672C\u5730\u65E5\u671F\u7EC4\u4EF6\u663E\u793A");
  } finally {
    process.env.TZ = originalTimezone;
  }
});
runTest("\u6587\u6863\u6587\u4EF6\u540D\u53EA\u6709\u4F20\u5165\u65E5\u671F\u65F6\u624D\u8FFD\u52A0\u65E5\u671F\u540E\u7F00", () => {
  const fallbackTexts = { unknownStore: "UnknownStore", unknownOrder: "UnknownOrder" };
  assertEqual(
    buildDocumentFileName("INVOICE", "Bankstown", "2026-1049", "xlsx", fallbackTexts, "2026-06-17"),
    "INVOICE_Bankstown_2026-1049_2026-06-17.xlsx",
    "\u53D1\u7968\u6587\u4EF6\u540D\u5E94\u8FFD\u52A0 invoice \u65E5\u671F"
  );
  assertEqual(
    buildDocumentFileName("PICKING", "Bankstown", "2026-1049", "xlsx", fallbackTexts),
    "PICKING_Bankstown_2026-1049.xlsx",
    "\u672A\u4F20\u65E5\u671F\u65F6\u5E94\u4FDD\u6301\u539F\u6587\u4EF6\u540D\u683C\u5F0F"
  );
});
runTest("PDF \u5207\u7247\u7ED8\u5236\u5E94\u5148\u94FA\u767D\u5E95\u518D\u7ED8\u5236\u539F\u56FE\u5207\u7247", () => {
  const calls = [];
  const context = {
    set fillStyle(value) {
      calls.push(["fillStyle", value]);
    },
    fillRect: (...args) => calls.push(["fillRect", ...args]),
    drawImage: (...args) => calls.push(["drawImage", ...args])
  };
  const sourceCanvas = { width: 120, height: 300 };
  paintPdfSlice(context, sourceCanvas, 120, { offsetY: 40, height: 80 });
  assertDeepEqual(
    calls,
    [
      ["fillStyle", "#ffffff"],
      ["fillRect", 0, 0, 120, 80],
      ["drawImage", sourceCanvas, 0, 40, 120, 80, 0, 0, 120, 80]
    ],
    "\u7ED8\u5236\u987A\u5E8F\u548C\u53C2\u6570\u5E94\u9501\u4F4F\u767D\u5E95 JPEG \u5207\u7247\u903B\u8F91"
  );
});
runTest("\u5206\u9875 PDF \u5E94\u9010\u9875\u6E32\u67D3 A4 \u9875\u9762\u5E76\u5199\u5165 jsPDF", () => {
  const printUtilsSource = fs.readFileSync(path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/printUtils.ts"), "utf8");
  assertEqual(printUtilsSource.includes("createPdfDocumentFromPages"), true, "\u5E94\u63D0\u4F9B\u5206\u9875 PDF \u751F\u6210\u51FD\u6570");
  assertEqual(printUtilsSource.includes("querySelectorAll<HTMLElement>(pageSelector)"), true, "\u5206\u9875 PDF \u5E94\u6309\u9875\u9762\u9009\u62E9\u5668\u9010\u9875\u8BFB\u53D6 DOM");
  assertEqual(printUtilsSource.includes("pdf.addPage()"), true, "\u5206\u9875 PDF \u591A\u9875\u65F6\u5E94\u8FFD\u52A0 PDF \u9875\u9762");
  assertEqual(printUtilsSource.includes("pdf.addImage(imageData, PDF_IMAGE_FORMAT, 0, 0, 210, 297)"), true, "\u6BCF\u4E2A\u5206\u9875\u5E94\u6309 A4 \u5C3A\u5BF8\u5199\u5165 PDF");
});
runTest("\u5206\u9875 PDF \u6253\u5370\u5E94\u4F7F\u7528\u4E34\u65F6 Blob URL \u5E76\u6E05\u7406\u8D44\u6E90", () => {
  const printUtilsSource = fs.readFileSync(path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/printUtils.ts"), "utf8");
  assertEqual(printUtilsSource.includes("pdf.output('blob')"), true, "\u6253\u5370 PDF \u5E94\u8F93\u51FA Blob");
  assertEqual(printUtilsSource.includes("URL.createObjectURL(blob)"), true, "\u6253\u5370 PDF \u5E94\u521B\u5EFA\u4E34\u65F6 URL");
  assertEqual(printUtilsSource.includes("frame.contentWindow?.print()"), true, "\u6253\u5370 PDF \u5E94\u89E6\u53D1 iframe \u6253\u5370");
  assertEqual(printUtilsSource.includes("URL.revokeObjectURL(url)"), true, "\u6253\u5370\u5B8C\u6210\u540E\u5E94\u6E05\u7406\u4E34\u65F6 URL");
  assertEqual(printUtilsSource.includes("frame.remove()"), true, "\u6253\u5370\u5B8C\u6210\u540E\u5E94\u79FB\u9664\u4E34\u65F6 iframe");
});
console.log("printUtils.test: ok");
