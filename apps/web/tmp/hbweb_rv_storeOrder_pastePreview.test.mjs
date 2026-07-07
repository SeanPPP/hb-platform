// src/pages/Warehouse/StoreOrders/pastePreview.test.ts
import { readFileSync } from "node:fs";
import path from "node:path";

// src/pages/Warehouse/StoreOrders/pastePreview.ts
function normalizeLookupKey(value) {
  return value?.trim().toLowerCase() ?? "";
}
function parseQuantity(rawQuantity, quantityColumnEnabled, quantityMode) {
  if (!quantityColumnEnabled) {
    return { quantity: 1, quantityValid: true };
  }
  const normalized = rawQuantity?.trim() ?? "";
  if (!normalized) {
    return { quantity: quantityMode === "inner" ? 1 : 0, quantityValid: true };
  }
  const isNonNegativeInteger = /^\d+$/.test(normalized);
  const quantity = isNonNegativeInteger ? Number.parseInt(normalized, 10) : 0;
  return {
    quantity,
    quantityRaw: rawQuantity,
    quantityValid: isNonNegativeInteger
  };
}
function parseStoreOrderPasteRows(pasteData, columnMapping, quantityMode = "direct") {
  return pasteData.split(/\r?\n/).map((row, index) => ({ row, index })).map(({ row, index }) => {
    if (!row.trim()) {
      return null;
    }
    const cols = row.split("	").map((col) => col.trim());
    const itemNumber = cols[columnMapping.itemNumber] || cols[0] || "";
    if (!itemNumber) {
      return null;
    }
    const quantityResult = parseQuantity(
      columnMapping.quantity >= 0 ? cols[columnMapping.quantity] : void 0,
      columnMapping.quantity >= 0,
      quantityMode
    );
    const rawPrice = columnMapping.price >= 0 ? cols[columnMapping.price] : void 0;
    const parsedPrice = rawPrice === void 0 ? Number.NaN : Number.parseFloat(rawPrice);
    const parsedItem = {
      rowIndex: index,
      itemNumber,
      quantity: quantityResult.quantity,
      quantityValid: quantityResult.quantityValid,
      price: Number.isFinite(parsedPrice) ? parsedPrice : void 0
    };
    if (quantityResult.quantityRaw !== void 0) {
      parsedItem.quantityRaw = quantityResult.quantityRaw;
    }
    return parsedItem;
  }).filter((item) => Boolean(item));
}
function createPastePreviewItems(parsedItems, lookupResult, existingLines2) {
  const productMap = /* @__PURE__ */ new Map();
  const existingMap = /* @__PURE__ */ new Map();
  lookupResult.forEach((entry) => {
    if (entry.lookupCode && entry.product) {
      productMap.set(normalizeLookupKey(entry.lookupCode), entry.product);
    }
  });
  existingLines2.forEach((line) => {
    if (line.productCode) {
      existingMap.set(normalizeLookupKey(line.productCode), line);
    }
  });
  return parsedItems.map((item) => {
    const product = productMap.get(normalizeLookupKey(item.itemNumber));
    const existingLine = product?.productCode ? existingMap.get(normalizeLookupKey(product.productCode)) : void 0;
    const isNewZeroQuantity = Boolean(product) && !existingLine && item.quantity === 0;
    const status = !item.quantityValid || isNewZeroQuantity ? "invalidQuantity" : product ? existingLine ? "existing" : "new" : "unmatched";
    const canImportQuantity = item.quantityValid && (item.quantity > 0 || Boolean(existingLine));
    return {
      ...item,
      product,
      valid: Boolean(product) && canImportQuantity,
      status,
      // 默认覆盖，用户可在预览中批量或逐条改成追加/跳过。
      action: "replace",
      existingQuantity: existingLine?.quantity,
      existingAllocQuantity: existingLine?.allocQuantity
    };
  });
}
function filterPastePreviewItems(items, filter) {
  if (filter === "all") return items;
  if (filter === "importable") return items.filter((item) => item.valid && item.action !== "skip");
  if (filter === "invalid") return items.filter((item) => item.status === "invalidQuantity");
  if (filter === "unmatched") return items.filter((item) => item.status === "unmatched");
  return items.filter((item) => item.status === "existing");
}
function setExistingPastePreviewAction(items, action) {
  return items.map((item) => item.status === "existing" && item.valid ? { ...item, action } : item);
}
function resolveInnerQuantityMultiplier(item) {
  const minOrderQuantity = item.product?.minOrderQuantity;
  return typeof minOrderQuantity === "number" && Number.isFinite(minOrderQuantity) && minOrderQuantity > 1 ? minOrderQuantity : 1;
}
function getPasteSubmitQuantity(item, quantityMode = "direct") {
  return quantityMode === "inner" ? item.quantity * resolveInnerQuantityMultiplier(item) : item.quantity;
}
function buildPasteSubmitItems(items, options = {}) {
  const quantityMode = options.quantityMode ?? "direct";
  return items.filter((item) => item.valid && item.action !== "skip" && item.product?.productCode).map((item) => ({
    productCode: item.product.productCode,
    quantity: getPasteSubmitQuantity(item, quantityMode),
    importPrice: item.price,
    action: item.action
  }));
}
function formatPastePreviewQuantity(item, quantityMode = "direct") {
  if (item.quantityValid) {
    return getPasteSubmitQuantity(item, quantityMode);
  }
  const rawQuantity = item.quantityRaw?.trim();
  return rawQuantity ? rawQuantity : "--";
}

// src/pages/Warehouse/StoreOrders/pasteOptimisticRows.ts
function toNumber(value) {
  return Number(value ?? 0);
}
function buildOptimisticDetailGUID(item) {
  return `optimistic-paste-${item.rowIndex}-${item.product?.productCode ?? item.itemNumber}`;
}
function recalculateLine(line) {
  const quantity = toNumber(line.quantity);
  const allocQuantity = toNumber(line.allocQuantity);
  const price = toNumber(line.price);
  const importPrice = toNumber(line.importPrice);
  const volume = line.volume;
  return {
    ...line,
    quantity,
    allocQuantity,
    price,
    importPrice,
    amount: price * allocQuantity,
    importAmount: importPrice * allocQuantity,
    orderVolume: volume === void 0 || volume === null ? line.orderVolume : volume * quantity,
    allocVolume: volume === void 0 || volume === null ? line.allocVolume : volume * allocQuantity
  };
}
function createLineFromPreview(item, targetField) {
  const product = item.product;
  const existingQuantity = toNumber(item.existingQuantity);
  const existingAllocQuantity = toNumber(item.existingAllocQuantity);
  return recalculateLine({
    detailGUID: buildOptimisticDetailGUID(item),
    productCode: product?.productCode ?? item.itemNumber,
    itemNumber: product?.itemNumber ?? item.itemNumber,
    barcode: product?.barcode,
    productName: product?.productName,
    productImage: product?.productImage,
    quantity: targetField === "quantity" ? existingQuantity : 0,
    allocQuantity: targetField === "allocQuantity" ? existingAllocQuantity : 0,
    price: toNumber(product?.oemPrice),
    amount: 0,
    importPrice: toNumber(item.price ?? product?.importPrice),
    importAmount: 0,
    minOrderQuantity: toNumber(product?.minOrderQuantity) || 1,
    isActive: true
  });
}
function applyPasteQuantity(line, item, targetField, quantityMode) {
  const writeQuantity = getPasteSubmitQuantity(item, quantityMode);
  const currentQuantity = targetField === "quantity" ? toNumber(line.quantity) : toNumber(line.allocQuantity);
  const nextQuantity = item.action === "append" ? currentQuantity + writeQuantity : writeQuantity;
  const nextLine = {
    ...line,
    importPrice: item.price ?? line.importPrice,
    [targetField]: nextQuantity
  };
  return recalculateLine(nextLine);
}
function buildPasteOptimisticRows({
  currentItems,
  previewItems,
  targetField,
  quantityMode = "direct"
}) {
  const lineMap = /* @__PURE__ */ new Map();
  const rowOrder = [];
  currentItems.forEach((line) => {
    if (!line.productCode) {
      return;
    }
    const productCodeKey = line.productCode.toLocaleLowerCase();
    lineMap.set(productCodeKey, line);
    rowOrder.push(productCodeKey);
  });
  previewItems.forEach((item) => {
    const productCode = item.product?.productCode;
    if (!item.valid || item.action === "skip" || !productCode) {
      return;
    }
    const productCodeKey = productCode.toLocaleLowerCase();
    const baseLine = lineMap.get(productCodeKey) ?? createLineFromPreview(item, targetField);
    const nextLine = applyPasteQuantity(baseLine, item, targetField, quantityMode);
    lineMap.set(productCodeKey, nextLine);
    if (!rowOrder.includes(productCodeKey)) {
      rowOrder.push(productCodeKey);
    }
  });
  return rowOrder.map((productCodeKey) => lineMap.get(productCodeKey)).filter((line) => Boolean(line));
}
function applyPasteOptimisticRowsToDetail(detail, optimisticRows) {
  return {
    ...detail,
    items: optimisticRows
  };
}
function resolvePasteOptimisticPendingAfterJob(pending, result) {
  if (!pending || pending.jobId !== result.jobId) {
    return pending;
  }
  return result.status === "Succeeded" || result.status === "Failed" ? null : pending;
}

// src/pages/Warehouse/StoreOrders/pastePreview.test.ts
var detailFile = path.resolve(process.cwd(), "src/pages/Warehouse/StoreOrders/Detail.tsx");
var packageFile = path.resolve(process.cwd(), "package.json");
var zhLocaleFile = path.resolve(process.cwd(), "src/i18n/locales/zh.json");
var enLocaleFile = path.resolve(process.cwd(), "src/i18n/locales/en.json");
var detailSource = readFileSync(detailFile, "utf8");
var packageSource = readFileSync(packageFile, "utf8");
var zhLocaleSource = readFileSync(zhLocaleFile, "utf8");
var enLocaleSource = readFileSync(enLocaleFile, "utf8");
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}. Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
function assertDeepEqual(actual, expected, message) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${message}. Expected: ${expectedJson}, received: ${actualJson}`);
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
var lookupRows = [
  {
    lookupCode: "HB001",
    product: {
      productCode: "P001",
      itemNumber: "HB001",
      productName: "Existing Product",
      minOrderQuantity: 1,
      stockQuantity: 0,
      isInStock: false
    }
  },
  {
    lookupCode: "HB002",
    product: {
      productCode: "P002",
      itemNumber: "HB002",
      productName: "New Product",
      minOrderQuantity: 1,
      stockQuantity: 0,
      isInStock: false
    }
  }
];
var existingLines = [
  {
    productCode: "P001",
    quantity: 3,
    allocQuantity: 5
  }
];
async function main() {
  const failures = [];
  const parseFailure = await runTest("\u7C98\u8D34\u89E3\u6790\u5E94\u628A\u666E\u901A\u7A7A\u6570\u91CF\u6309 0 \u5904\u7406\u5E76\u4FDD\u7559\u5176\u4ED6\u683C\u5F0F\u9519\u8BEF\u5F02\u5E38\u884C", () => {
    const rows = parseStoreOrderPasteRows("HB001	10\nHB002	\nHB003	0\nHB004	-2\nHB005	abc\nHB006	12abc\nHB007	1.5", {
      itemNumber: 0,
      quantity: 1,
      price: -1
    });
    assertEqual(rows.length, 7, "\u89E3\u6790\u5E94\u4FDD\u7559\u5168\u90E8\u975E\u7A7A\u8D27\u53F7\u884C");
    assertEqual(rows[0].quantityValid, true, "\u6B63\u6570\u6570\u91CF\u5E94\u6709\u6548");
    assertEqual(rows[1].quantity, 0, "\u666E\u901A\u6A21\u5F0F\u7A7A\u6570\u91CF\u5E94\u6309 0 \u89E3\u6790");
    assertEqual(rows[1].quantityValid, true, "\u666E\u901A\u6A21\u5F0F\u7A7A\u6570\u91CF\u5E94\u53EF\u5199\u5165 0");
    assertEqual(rows[2].quantityValid, true, "0 \u6570\u91CF\u5E94\u6709\u6548");
    assertEqual(rows[3].quantityValid, false, "\u8D1F\u6570\u6570\u91CF\u5E94\u65E0\u6548");
    assertEqual(rows[4].quantityValid, false, "\u975E\u6570\u5B57\u6570\u91CF\u5E94\u65E0\u6548");
    assertEqual(rows[5].quantityValid, false, "\u5E26\u6570\u5B57\u524D\u7F00\u7684\u683C\u5F0F\u9519\u8BEF\u6570\u91CF\u5E94\u65E0\u6548");
    assertEqual(rows[6].quantityValid, false, "\u5C0F\u6570\u6570\u91CF\u5E94\u65E0\u6548");
  });
  if (parseFailure) failures.push(parseFailure);
  const leadingEmptyColumnFailure = await runTest("\u7C98\u8D34\u89E3\u6790\u5E94\u4FDD\u7559 Excel \u524D\u7F6E\u7A7A\u5217\u4EE5\u5339\u914D\u5217\u6620\u5C04", () => {
    const rows = parseStoreOrderPasteRows("	HB001	10", {
      itemNumber: 1,
      quantity: 2,
      price: -1
    });
    assertEqual(rows.length, 1, "\u524D\u7F6E\u7A7A\u5217\u4E0D\u5E94\u5BFC\u81F4\u6574\u884C\u88AB\u8DF3\u8FC7");
    assertEqual(rows[0].itemNumber, "HB001", "\u8D27\u53F7\u5E94\u6309\u6620\u5C04\u8BFB\u53D6\u7B2C\u4E8C\u5217");
    assertEqual(rows[0].quantity, 10, "\u6570\u91CF\u5E94\u6309\u6620\u5C04\u8BFB\u53D6\u7B2C\u4E09\u5217");
    assertEqual(rows[0].quantityValid, true, "\u524D\u7F6E\u7A7A\u5217\u4E0D\u5E94\u5F71\u54CD\u6570\u91CF\u6821\u9A8C");
  });
  if (leadingEmptyColumnFailure) failures.push(leadingEmptyColumnFailure);
  const missingQuantityZeroFailure = await runTest("\u666E\u901A\u6A21\u5F0F\u53EA\u6709\u8D27\u53F7\u6CA1\u6709\u6570\u91CF\u5217\u5185\u5BB9\u65F6\u5E94\u5199\u5165 0 \u5230\u5DF2\u6709\u660E\u7EC6", () => {
    const rows = parseStoreOrderPasteRows("HB001", {
      itemNumber: 0,
      quantity: 1,
      price: -1
    });
    const preview = createPastePreviewItems(rows, lookupRows, existingLines);
    assertEqual(rows.length, 1, "\u53EA\u6709\u8D27\u53F7\u7684\u884C\u4E5F\u5E94\u88AB\u89E3\u6790");
    assertEqual(rows[0].quantity, 0, "\u7F3A\u5931\u6570\u91CF\u5217\u5185\u5BB9\u5E94\u5199\u5165 0");
    assertEqual(rows[0].quantityValid, true, "\u7F3A\u5931\u6570\u91CF\u5E94\u89C6\u4E3A\u6709\u6548");
    assertEqual(preview[0].valid, true, "\u5DF2\u6709\u660E\u7EC6\u5339\u914D\u5230\u5546\u54C1\u540E\u5E94\u53EF\u5BFC\u5165 0");
    assertDeepEqual(
      buildPasteSubmitItems(preview),
      [{ productCode: "P001", quantity: 0, action: "replace" }],
      "\u63D0\u4EA4 payload \u5E94\u4F7F\u7528\u6570\u91CF 0"
    );
  });
  if (missingQuantityZeroFailure) failures.push(missingQuantityZeroFailure);
  const disabledQuantityColumnFailure = await runTest("\u6570\u91CF\u5217\u9009\u62E9\u65E0\u65F6\u5E94\u9ED8\u8BA4\u6570\u91CF 1 \u5E76\u53EF\u5BFC\u5165", () => {
    const rows = parseStoreOrderPasteRows("HB001", {
      itemNumber: 0,
      quantity: -1,
      price: -1
    });
    const preview = createPastePreviewItems(rows, lookupRows, existingLines);
    assertEqual(rows[0].quantity, 1, "\u6570\u91CF\u5217\u9009\u62E9\u65E0\u65F6\u5E94\u9ED8\u8BA4 1");
    assertEqual(rows[0].quantityValid, true, "\u6570\u91CF\u5217\u9009\u62E9\u65E0\u5E94\u89C6\u4E3A\u6709\u6548");
    assertDeepEqual(
      buildPasteSubmitItems(preview),
      [{ productCode: "P001", quantity: 1, action: "replace" }],
      "\u63D0\u4EA4 payload \u5E94\u4F7F\u7528\u9ED8\u8BA4\u6570\u91CF 1"
    );
  });
  if (disabledQuantityColumnFailure) failures.push(disabledQuantityColumnFailure);
  const newZeroQuantityFailure = await runTest("\u65B0\u5546\u54C1\u6570\u91CF 0 \u4E0D\u5E94\u8FDB\u5165\u63D0\u4EA4 payload", () => {
    const rows = parseStoreOrderPasteRows("HB002	0", {
      itemNumber: 0,
      quantity: 1,
      price: -1
    });
    const preview = createPastePreviewItems(rows, lookupRows, existingLines);
    assertEqual(rows[0].quantity, 0, "\u663E\u5F0F 0 \u5E94\u6309 0 \u89E3\u6790");
    assertEqual(rows[0].quantityValid, true, "\u663E\u5F0F 0 \u672C\u8EAB\u662F\u6709\u6548\u6570\u91CF");
    assertEqual(preview[0].valid, false, "\u65B0\u5546\u54C1 0 \u4E0D\u5E94\u5BFC\u5165\u7A7A\u660E\u7EC6");
    assertEqual(preview[0].status, "invalidQuantity", "\u65B0\u5546\u54C1 0 \u5E94\u63D0\u793A\u4E3A\u4E0D\u53EF\u5BFC\u5165\u6570\u91CF");
    assertDeepEqual(buildPasteSubmitItems(preview), [], "\u65B0\u5546\u54C1 0 \u4E0D\u5E94\u8FDB\u5165\u63D0\u4EA4 payload");
  });
  if (newZeroQuantityFailure) failures.push(newZeroQuantityFailure);
  const quantityDisplayFailure = await runTest("\u5F02\u5E38\u6570\u91CF\u9884\u89C8\u5E94\u5C55\u793A\u539F\u59CB Excel \u5355\u5143\u683C\u503C\uFF0C\u666E\u901A\u7A7A\u6570\u91CF\u663E\u793A 0", () => {
    const rows = parseStoreOrderPasteRows("HB001	abc\nHB002	-2\nHB003	", {
      itemNumber: 0,
      quantity: 1,
      price: -1
    });
    const preview = createPastePreviewItems(rows, lookupRows, existingLines);
    assertEqual(formatPastePreviewQuantity(preview[0]), "abc", "\u975E\u6570\u5B57\u5F02\u5E38\u5E94\u5C55\u793A\u539F\u59CB\u503C");
    assertEqual(formatPastePreviewQuantity(preview[1]), "-2", "\u8D1F\u6570\u5F02\u5E38\u5E94\u5C55\u793A\u539F\u59CB\u503C");
    assertEqual(formatPastePreviewQuantity(preview[2]), 0, "\u666E\u901A\u7A7A\u6570\u91CF\u5E94\u663E\u793A 0");
  });
  if (quantityDisplayFailure) failures.push(quantityDisplayFailure);
  const previewFailure = await runTest("\u9884\u89C8\u5E94\u6807\u8BB0\u65B0\u589E\u3001\u5DF2\u5B58\u5728\u3001\u6570\u91CF\u5F02\u5E38\u548C\u672A\u5339\u914D\u72B6\u6001", () => {
    const parsedRows = parseStoreOrderPasteRows("HB001	10\nHB002	4\nHB003	-2\nHB404	7", {
      itemNumber: 0,
      quantity: 1,
      price: -1
    });
    const preview = createPastePreviewItems(parsedRows, lookupRows, existingLines);
    assertEqual(preview[0].status, "existing", "\u5DF2\u5B58\u5728\u5546\u54C1\u5E94\u6807\u8BB0 existing");
    assertEqual(preview[0].action, "replace", "\u5DF2\u5B58\u5728\u5546\u54C1\u9ED8\u8BA4\u8986\u76D6");
    assertEqual(preview[0].existingQuantity, 3, "\u5DF2\u5B58\u5728\u5546\u54C1\u5E94\u5E26\u8BA2\u8D27\u6570\u91CF");
    assertEqual(preview[0].existingAllocQuantity, 5, "\u5DF2\u5B58\u5728\u5546\u54C1\u5E94\u5E26\u53D1\u8D27\u6570\u91CF");
    assertEqual(preview[1].status, "new", "\u672A\u5728\u8BA2\u5355\u4E2D\u7684\u5339\u914D\u5546\u54C1\u5E94\u6807\u8BB0\u65B0\u589E");
    assertEqual(preview[2].status, "invalidQuantity", "\u6570\u91CF\u5F02\u5E38\u4F18\u5148\u5C55\u793A\u5F02\u5E38\u72B6\u6001");
    assertEqual(preview[3].status, "unmatched", "\u672A\u5339\u914D\u5546\u54C1\u5E94\u6807\u8BB0 unmatched");
    assertEqual(preview.filter((item) => item.valid).length, 2, "\u53EA\u6709\u65B0\u589E\u548C\u5DF2\u5B58\u5728\u6709\u6548\u884C\u53EF\u5BFC\u5165");
  });
  if (previewFailure) failures.push(previewFailure);
  const filterFailure = await runTest("\u9884\u89C8\u7B5B\u9009\u5E94\u652F\u6301\u5168\u90E8\u3001\u53EF\u5BFC\u5165\u3001\u5F02\u5E38\u3001\u672A\u5339\u914D\u3001\u5DF2\u5B58\u5728", () => {
    const parsedRows = parseStoreOrderPasteRows("HB001	10\nHB002	4\nHB003	-2\nHB404	7", {
      itemNumber: 0,
      quantity: 1,
      price: -1
    });
    const preview = createPastePreviewItems(parsedRows, lookupRows, existingLines);
    assertEqual(filterPastePreviewItems(preview, "all").length, 4, "\u5168\u90E8\u7B5B\u9009\u5E94\u8FD4\u56DE\u6240\u6709\u884C");
    assertEqual(filterPastePreviewItems(preview, "importable").length, 2, "\u53EF\u5BFC\u5165\u7B5B\u9009\u5E94\u8FD4\u56DE\u6709\u6548\u884C");
    assertEqual(filterPastePreviewItems(preview, "invalid").length, 1, "\u5F02\u5E38\u7B5B\u9009\u5E94\u8FD4\u56DE\u6570\u91CF\u5F02\u5E38\u884C");
    assertEqual(filterPastePreviewItems(preview, "unmatched").length, 1, "\u672A\u5339\u914D\u7B5B\u9009\u5E94\u8FD4\u56DE\u672A\u5339\u914D\u884C");
    assertEqual(filterPastePreviewItems(preview, "existing").length, 1, "\u5DF2\u5B58\u5728\u7B5B\u9009\u5E94\u8FD4\u56DE\u5DF2\u5B58\u5728\u884C");
  });
  if (filterFailure) failures.push(filterFailure);
  const submitFailure = await runTest("\u63D0\u4EA4\u9879\u5E94\u643A\u5E26\u9010\u884C\u52A8\u4F5C\u5E76\u8FC7\u6EE4\u5F02\u5E38\u3001\u672A\u5339\u914D\u548C\u8DF3\u8FC7\u884C", () => {
    const parsedRows = parseStoreOrderPasteRows("HB001	10\nHB002	4\nHB003	0\nHB404	7", {
      itemNumber: 0,
      quantity: 1,
      price: -1
    });
    const preview = setExistingPastePreviewAction(
      createPastePreviewItems(parsedRows, lookupRows, existingLines),
      "append"
    ).map((item) => item.product?.productCode === "P002" ? { ...item, action: "skip" } : item);
    assertDeepEqual(
      buildPasteSubmitItems(preview),
      [{ productCode: "P001", quantity: 10, action: "append" }],
      "\u63D0\u4EA4 payload \u5E94\u53EA\u5305\u542B\u6709\u6548\u4E14\u672A\u8DF3\u8FC7\u7684\u884C\uFF0C\u5E76\u4FDD\u7559 action"
    );
  });
  if (submitFailure) failures.push(submitFailure);
  const innerQuantitySubmitFailure = await runTest("inner \u6A21\u5F0F\u63D0\u4EA4\u6570\u91CF\u5E94\u4E58\u5546\u54C1\u4E2D\u5305\u6570/\u6700\u5C0F\u8BA2\u8D27\u91CF", () => {
    const parsedRows = parseStoreOrderPasteRows("HB-INNER	2", {
      itemNumber: 0,
      quantity: 1,
      price: -1
    });
    const preview = createPastePreviewItems(
      parsedRows,
      [
        {
          lookupCode: "HB-INNER",
          product: {
            productCode: "P-INNER",
            itemNumber: "HB-INNER",
            productName: "Inner Product",
            minOrderQuantity: 12,
            stockQuantity: 0,
            isInStock: false
          }
        }
      ],
      []
    );
    assertDeepEqual(
      buildPasteSubmitItems(preview, { quantityMode: "inner" }),
      [{ productCode: "P-INNER", quantity: 24, action: "replace" }],
      "inner \u6A21\u5F0F\u5E94\u628A Excel \u6570\u91CF 2 \u6362\u7B97\u6210 24 \u540E\u63D0\u4EA4"
    );
    assertDeepEqual(
      buildPasteSubmitItems(preview),
      [{ productCode: "P-INNER", quantity: 2, action: "replace" }],
      "\u9ED8\u8BA4\u6A21\u5F0F\u4ECD\u5E94\u4FDD\u6301\u539F\u59CB Excel \u6570\u91CF"
    );
    assertEqual(formatPastePreviewQuantity(preview[0], "inner"), 24, "inner \u6A21\u5F0F\u9884\u89C8\u5E94\u663E\u793A\u6700\u7EC8\u5199\u5165\u6570\u91CF");
  });
  if (innerQuantitySubmitFailure) failures.push(innerQuantitySubmitFailure);
  const innerMissingQuantitySubmitFailure = await runTest("inner \u6A21\u5F0F\u9047\u5230\u7A7A\u6570\u91CF\u5E94\u5148\u9ED8\u8BA4 1 \u518D\u4E58\u5546\u54C1\u4E2D\u5305\u6570", () => {
    const parsedRows = parseStoreOrderPasteRows("HB-INNER	", {
      itemNumber: 0,
      quantity: 1,
      price: -1
    }, "inner");
    const preview = createPastePreviewItems(
      parsedRows,
      [
        {
          lookupCode: "HB-INNER",
          product: {
            productCode: "P-INNER",
            itemNumber: "HB-INNER",
            productName: "Inner Product",
            minOrderQuantity: 12,
            stockQuantity: 0,
            isInStock: false
          }
        }
      ],
      []
    );
    assertEqual(parsedRows[0].quantity, 1, "\u7A7A\u6570\u91CF\u5E94\u5148\u6309 1 \u89E3\u6790");
    assertDeepEqual(
      buildPasteSubmitItems(preview, { quantityMode: "inner" }),
      [{ productCode: "P-INNER", quantity: 12, action: "replace" }],
      "inner \u6A21\u5F0F\u5E94\u628A\u9ED8\u8BA4\u6570\u91CF 1 \u6362\u7B97\u6210 12 \u540E\u63D0\u4EA4"
    );
    assertEqual(formatPastePreviewQuantity(preview[0], "inner"), 12, "inner \u6A21\u5F0F\u9884\u89C8\u5E94\u663E\u793A\u9ED8\u8BA4 1 \u6362\u7B97\u540E\u7684\u6700\u7EC8\u6570\u91CF");
  });
  if (innerMissingQuantitySubmitFailure) failures.push(innerMissingQuantitySubmitFailure);
  const innerExplicitZeroFailure = await runTest("inner \u6A21\u5F0F\u663E\u5F0F 0 \u5E94\u6309 0 \u4FDD\u7559\uFF0C\u4E0D\u56DE\u9000\u9ED8\u8BA4 1", () => {
    const parsedRows = parseStoreOrderPasteRows("HB001	0", {
      itemNumber: 0,
      quantity: 1,
      price: -1
    }, "inner");
    const preview = createPastePreviewItems(parsedRows, lookupRows, existingLines);
    assertEqual(parsedRows[0].quantity, 0, "inner \u6A21\u5F0F\u663E\u5F0F 0 \u5E94\u4FDD\u6301 0");
    assertDeepEqual(
      buildPasteSubmitItems(preview, { quantityMode: "inner" }),
      [{ productCode: "P001", quantity: 0, action: "replace" }],
      "\u5DF2\u6709\u660E\u7EC6\u663E\u5F0F 0 \u5E94\u63D0\u4EA4 0"
    );
    assertEqual(formatPastePreviewQuantity(preview[0], "inner"), 0, "inner \u663E\u5F0F 0 \u9884\u89C8\u4E5F\u5E94\u663E\u793A 0");
  });
  if (innerExplicitZeroFailure) failures.push(innerExplicitZeroFailure);
  const innerQuantityFallbackFailure = await runTest("inner \u6A21\u5F0F\u9047\u5230\u65E0\u6548\u4E2D\u5305\u6570\u5E94\u6309 1 \u56DE\u9000", () => {
    const baseItem = {
      rowIndex: 0,
      itemNumber: "HB-FALLBACK",
      quantity: 3,
      quantityValid: true,
      valid: true,
      status: "new",
      action: "replace"
    };
    const buildItem = (minOrderQuantity) => ({
      ...baseItem,
      product: {
        productCode: `P-FALLBACK-${String(minOrderQuantity)}`,
        itemNumber: "HB-FALLBACK",
        productName: "Fallback Product",
        minOrderQuantity,
        stockQuantity: 0,
        isInStock: false
      }
    });
    const invalidItems = [buildItem(0), buildItem(1), buildItem(void 0), buildItem(Number.NaN)];
    assertDeepEqual(
      buildPasteSubmitItems(invalidItems, { quantityMode: "inner" }).map((item) => item.quantity),
      [3, 3, 3, 3],
      "0\u30011\u3001\u7A7A\u503C\u548C NaN \u90FD\u5E94\u6309 1 \u56DE\u9000\uFF0C\u907F\u514D\u63D0\u4EA4\u975E\u6CD5\u6570\u91CF"
    );
  });
  if (innerQuantityFallbackFailure) failures.push(innerQuantityFallbackFailure);
  const optimisticRowsFailure = await runTest("\u4E50\u89C2\u9884\u89C8\u884C\u5E94\u6309\u8986\u76D6/\u8FFD\u52A0\u751F\u6210\u4E34\u65F6\u8BA2\u5355\u660E\u7EC6", () => {
    const parsedRows = parseStoreOrderPasteRows("HB001	10	1.5\nHB002	4	2.5", {
      itemNumber: 0,
      quantity: 1,
      price: 2
    });
    const preview = setExistingPastePreviewAction(
      createPastePreviewItems(parsedRows, lookupRows, existingLines),
      "append"
    );
    const currentItems = [
      {
        detailGUID: "detail-1",
        productCode: "P001",
        itemNumber: "HB001",
        productName: "Existing Product",
        quantity: 3,
        allocQuantity: 5,
        price: 9,
        amount: 27,
        importPrice: 1,
        importAmount: 5,
        minOrderQuantity: 1,
        isActive: true
      }
    ];
    const rows = buildPasteOptimisticRows({
      currentItems,
      previewItems: preview,
      targetField: "allocQuantity"
    });
    assertEqual(rows.length, 2, "\u5F53\u524D\u9875\u884C\u548C\u672C\u6B21\u65B0\u589E\u6709\u6548\u884C\u90FD\u5E94\u663E\u793A");
    assertEqual(rows[0].detailGUID, "detail-1", "\u5DF2\u6709\u884C\u5E94\u4FDD\u7559\u771F\u5B9E detailGUID");
    assertEqual(rows[0].quantity, 3, "\u5199\u5165\u53D1\u8D27\u6570\u91CF\u65F6\u4E0D\u5E94\u6539\u5DF2\u6709\u8BA2\u8D27\u6570\u91CF");
    assertEqual(rows[0].allocQuantity, 15, "\u8FFD\u52A0\u52A8\u4F5C\u5E94\u57FA\u4E8E\u5F53\u524D\u53D1\u8D27\u6570\u91CF\u7D2F\u52A0");
    assertEqual(rows[0].importPrice, 1.5, "\u7C98\u8D34\u4EF7\u683C\u5E94\u4E34\u65F6\u53CD\u6620\u5230\u8FDB\u8D27\u4EF7");
    assertEqual(rows[1].productCode, "P002", "\u65B0\u589E\u6709\u6548\u884C\u5E94\u5408\u6210\u4E34\u65F6\u660E\u7EC6");
    assertEqual(rows[1].allocQuantity, 4, "\u65B0\u589E\u6709\u6548\u884C\u5E94\u5199\u5165\u76EE\u6807\u53D1\u8D27\u6570\u91CF");
    assert(rows[1].detailGUID.startsWith("optimistic-paste-"), "\u65B0\u589E\u884C\u5E94\u4F7F\u7528\u4E34\u65F6 detailGUID");
  });
  if (optimisticRowsFailure) failures.push(optimisticRowsFailure);
  const optimisticInnerQuantityFailure = await runTest("inner \u6A21\u5F0F\u4E50\u89C2\u9884\u89C8\u6570\u91CF\u5E94\u548C\u63D0\u4EA4\u6570\u91CF\u4E00\u81F4", () => {
    const parsedRows = parseStoreOrderPasteRows("HB-INNER	2", {
      itemNumber: 0,
      quantity: 1,
      price: -1
    });
    const preview = createPastePreviewItems(
      parsedRows,
      [
        {
          lookupCode: "HB-INNER",
          product: {
            productCode: "P-INNER",
            itemNumber: "HB-INNER",
            productName: "Inner Product",
            minOrderQuantity: 12,
            stockQuantity: 0,
            isInStock: false
          }
        }
      ],
      []
    );
    const rows = buildPasteOptimisticRows({
      currentItems: [],
      previewItems: preview,
      targetField: "allocQuantity",
      quantityMode: "inner"
    });
    const [submitItem] = buildPasteSubmitItems(preview, { quantityMode: "inner" });
    assertEqual(rows[0].allocQuantity, submitItem.quantity, "\u4E50\u89C2\u53D1\u8D27\u6570\u91CF\u5FC5\u987B\u548C\u63D0\u4EA4 payload \u6570\u91CF\u4E00\u81F4");
    assertEqual(formatPastePreviewQuantity(preview[0], "inner"), submitItem.quantity, "\u9884\u89C8\u5C55\u793A\u6570\u91CF\u4E5F\u5E94\u548C\u63D0\u4EA4 payload \u4E00\u81F4");
  });
  if (optimisticInnerQuantityFailure) failures.push(optimisticInnerQuantityFailure);
  const optimisticDetailTotalsFailure = await runTest("\u4E50\u89C2\u9884\u89C8\u53EA\u66FF\u6362\u8868\u683C\u884C\u4E0D\u8986\u76D6\u670D\u52A1\u5668\u6574\u5355\u5408\u8BA1", () => {
    const originalDetail = {
      orderGUID: "order-1",
      totalAmount: 999,
      totalQuantity: 100,
      totalImportAmount: 888,
      totalVolume: 77,
      totalAllocQuantity: 66,
      totalSKU: 55,
      itemsTotal: 44,
      items: []
    };
    const optimisticRows = [
      {
        detailGUID: "optimistic-paste-1",
        productCode: "P001",
        itemNumber: "HB001",
        productName: "Preview Product",
        quantity: 1,
        allocQuantity: 2,
        price: 3,
        amount: 6,
        importPrice: 4,
        importAmount: 8,
        minOrderQuantity: 1,
        isActive: true
      }
    ];
    const nextDetail = applyPasteOptimisticRowsToDetail(originalDetail, optimisticRows);
    assertEqual(nextDetail.items, optimisticRows, "\u4E34\u65F6\u9884\u89C8\u5E94\u66FF\u6362\u5F53\u524D\u8868\u683C\u884C");
    assertEqual(nextDetail.itemsTotal, 44, "\u8FDC\u7A0B\u5206\u9875\u603B\u6570\u5E94\u4FDD\u6301\u670D\u52A1\u5668\u771F\u5B9E\u503C");
    assertEqual(nextDetail.totalQuantity, 100, "\u6574\u5355\u8BA2\u8D27\u6570\u91CF\u5E94\u4FDD\u6301\u670D\u52A1\u5668\u771F\u5B9E\u503C");
    assertEqual(nextDetail.totalAllocQuantity, 66, "\u6574\u5355\u53D1\u8D27\u6570\u91CF\u5E94\u4FDD\u6301\u670D\u52A1\u5668\u771F\u5B9E\u503C");
    assertEqual(nextDetail.totalImportAmount, 888, "\u6574\u5355\u91D1\u989D\u5E94\u4FDD\u6301\u670D\u52A1\u5668\u771F\u5B9E\u503C");
    assertEqual(nextDetail.totalSKU, 55, "\u6574\u5355 SKU \u6570\u5E94\u4FDD\u6301\u670D\u52A1\u5668\u771F\u5B9E\u503C");
  });
  if (optimisticDetailTotalsFailure) failures.push(optimisticDetailTotalsFailure);
  const optimisticPendingFailure = await runTest("\u4E50\u89C2\u9884\u89C8 pending \u5E94\u5728\u6210\u529F\u6216\u5931\u8D25\u7EC8\u6001\u6E05\u7406", () => {
    const pending = { jobId: "job-1", orderGUID: "order-1" };
    assertDeepEqual(
      resolvePasteOptimisticPendingAfterJob(pending, { jobId: "job-1", status: "Running" }),
      pending,
      "\u8FD0\u884C\u4E2D\u72B6\u6001\u4E0D\u5E94\u6E05\u7406 pending"
    );
    assertEqual(
      resolvePasteOptimisticPendingAfterJob(pending, { jobId: "job-1", status: "Succeeded" }),
      null,
      "\u6210\u529F\u7EC8\u6001\u5E94\u6E05\u7406 pending"
    );
    assertEqual(
      resolvePasteOptimisticPendingAfterJob(pending, { jobId: "job-1", status: "Failed" }),
      null,
      "\u5931\u8D25\u7EC8\u6001\u5E94\u6E05\u7406 pending"
    );
    assertDeepEqual(
      resolvePasteOptimisticPendingAfterJob(pending, { jobId: "job-2", status: "Succeeded" }),
      pending,
      "\u5176\u5B83 job \u7EC8\u6001\u4E0D\u5E94\u6E05\u7406\u5F53\u524D pending"
    );
  });
  if (optimisticPendingFailure) failures.push(optimisticPendingFailure);
  const detailUiFailure = await runTest("\u8BE6\u60C5\u9875\u7C98\u8D34\u9884\u89C8\u5E94\u4E0D\u5206\u9875\u5E76\u63D0\u4F9B\u7B5B\u9009\u548C\u6279\u91CF\u9010\u6761\u52A8\u4F5C", () => {
    assert(detailSource.includes("buildPasteSubmitItems") && detailSource.includes("from './pastePreview'"), "\u8BE6\u60C5\u9875\u5E94\u590D\u7528 pastePreview helper \u751F\u6210\u63D0\u4EA4\u9879");
    assert(detailSource.includes("pagination={false}"), "\u7C98\u8D34\u9884\u89C8\u8868\u683C\u5E94\u5173\u95ED\u5206\u9875");
    assert(
      !detailSource.includes("pagination={{ pageSize: 8, hideOnSinglePage: true }}"),
      "\u7C98\u8D34\u9884\u89C8\u8868\u683C\u4E0D\u5E94\u4FDD\u7559\u6BCF\u9875 8 \u884C\u5206\u9875"
    );
    assert(detailSource.includes("key: 'rowIndex'"), "\u7C98\u8D34\u9884\u89C8\u8868\u683C\u5E94\u63D0\u4F9B\u884C\u53F7\u5217");
    assert(detailSource.includes("record.rowIndex + 1"), "\u884C\u53F7\u5217\u5E94\u663E\u793A Excel \u539F\u59CB\u884C\u53F7");
    assert(detailSource.includes("pastePreviewFilter"), "\u8BE6\u60C5\u9875\u5E94\u7EF4\u62A4\u7C98\u8D34\u9884\u89C8\u7B5B\u9009\u72B6\u6001");
    assert(detailSource.includes("setExistingPastePreviewAction"), "\u8BE6\u60C5\u9875\u5E94\u63D0\u4F9B\u5DF2\u5B58\u5728\u884C\u6279\u91CF\u8BBE\u7F6E\u52A8\u4F5C");
    assert(detailSource.includes("handleChangePastePreviewAction"), "\u8BE6\u60C5\u9875\u5E94\u652F\u6301\u9010\u884C\u4FEE\u6539\u52A8\u4F5C");
    assert(detailSource.includes("dataIndex: 'action'"), "\u7C98\u8D34\u9884\u89C8\u8868\u683C\u5E94\u5C55\u793A\u884C\u7EA7\u64CD\u4F5C\u5217");
    assert(detailSource.includes("getStoreOrderDetailFull(detail.orderGUID)"), "\u89E3\u6790\u65F6\u5E94\u52A0\u8F7D\u6574\u5355\u660E\u7EC6\u5224\u65AD\u5DF2\u5B58\u5728\u5546\u54C1");
    assert(detailSource.includes("createStoreOrderPasteReplaceJob"), "\u5BFC\u5165\u786E\u8BA4\u5E94\u521B\u5EFA\u540E\u7AEF\u540E\u53F0 job");
    assert(detailSource.includes("getStoreOrderPasteReplaceJob"), "\u5BFC\u5165\u786E\u8BA4\u5E94\u8F6E\u8BE2\u540E\u7AEF job \u72B6\u6001");
    assert(detailSource.includes("createStoreOrderPasteReplaceJobPoller"), "\u8BE6\u60C5\u9875\u5E94\u4F7F\u7528\u72EC\u7ACB\u7C98\u8D34\u5BFC\u5165 poller");
    assert(detailSource.includes("stopPasteReplacePollingRef.current?.()"), "\u8BE6\u60C5\u9875\u5378\u8F7D\u6216\u5207\u6362\u8BA2\u5355\u65F6\u5E94\u6E05\u7406\u5BFC\u5165\u8F6E\u8BE2");
    assert(detailSource.includes("notification.success"), "\u5BFC\u5165\u5B8C\u6210\u5E94\u4F7F\u7528\u53F3\u4E0A\u89D2 notification \u63D0\u793A");
    assert(detailSource.includes("buildPasteOptimisticRows"), "\u5BFC\u5165\u4EFB\u52A1\u63D0\u4EA4\u6210\u529F\u540E\u5E94\u5148\u751F\u6210\u4E34\u65F6\u9884\u89C8\u660E\u7EC6");
    assert(detailSource.includes("pasteOptimisticPending"), "\u8BE6\u60C5\u9875\u5E94\u7EF4\u62A4 Excel \u7C98\u8D34\u4E34\u65F6\u9884\u89C8 pending \u72B6\u6001");
    assert(detailSource.includes("applyPasteOptimisticRowsToDetail"), "\u4E34\u65F6\u9884\u89C8\u5E94\u53EA\u66FF\u6362\u8868\u683C\u884C\u5E76\u4FDD\u7559\u670D\u52A1\u5668\u5408\u8BA1");
    assert(detailSource.includes("isPasteOptimisticPreviewActive"), "\u4E34\u65F6\u9884\u89C8\u671F\u95F4\u5E94\u7981\u7528\u4F9D\u8D56\u771F\u5B9E\u660E\u7EC6\u7684\u7F16\u8F91\u5165\u53E3");
    assert(detailSource.includes("\u5DF2\u5148\u663E\u793A\u672C\u6B21 Excel \u9884\u89C8"), "\u8BE6\u60C5\u9875\u5E94\u5C55\u793A\u4E34\u65F6\u9884\u89C8\u53CB\u597D\u8BF4\u660E");
    assert(detailSource.includes("resolvePasteOptimisticPendingAfterJob"), "\u8BE6\u60C5\u9875\u5E94\u5728\u4EFB\u52A1\u7EC8\u6001\u6E05\u7406\u4E34\u65F6\u9884\u89C8 pending \u72B6\u6001");
  });
  if (detailUiFailure) failures.push(detailUiFailure);
  const innerTargetUiFailure = await runTest("\u8BE6\u60C5\u9875\u5E94\u63D0\u4F9B\u53D1\u8D27\u6570\u91CF\u6309 inner \u5199\u5165\u76EE\u6807\u5E76\u6620\u5C04\u56DE\u540E\u7AEF\u53D1\u8D27\u5B57\u6BB5", () => {
    assert(detailSource.includes("type StoreOrderPasteWriteTarget = StoreOrderPasteTargetField | 'allocQuantityByInner'"), "\u8BE6\u60C5\u9875\u5E94\u4F7F\u7528\u672C\u5730 UI \u5199\u5165\u76EE\u6807\u6269\u5C55 inner \u9009\u9879");
    assert(detailSource.includes("resolvePasteTargetField(writeTarget: StoreOrderPasteWriteTarget): StoreOrderPasteTargetField"), "\u8BE6\u60C5\u9875\u5E94\u628A UI \u5199\u5165\u76EE\u6807\u8F6C\u6362\u6210\u540E\u7AEF\u76EE\u6807\u5B57\u6BB5");
    assert(detailSource.includes("writeTarget === 'allocQuantityByInner' ? 'allocQuantity' : writeTarget"), "inner \u5199\u5165\u76EE\u6807\u5E94\u6620\u5C04\u4E3A\u540E\u7AEF allocQuantity");
    assert(detailSource.includes("writeTarget === 'allocQuantityByInner' ? 'inner' : 'direct'"), "inner \u5199\u5165\u76EE\u6807\u5E94\u542F\u7528\u63D0\u4EA4\u6570\u91CF\u6362\u7B97");
    assert(detailSource.includes('<Radio value="allocQuantityByInner">'), "\u5F39\u7A97\u5199\u5165\u76EE\u6807\u5E94\u5C55\u793A\u6309 inner \u7684\u53D1\u8D27\u6570\u91CF\u9009\u9879");
    assert(detailSource.includes("t('storeOrders.detail.allocQuantityByInnerHelp')"), "\u6309 inner \u9009\u9879\u5E94\u6709\u53CB\u597D\u8BF4\u660E");
  });
  if (innerTargetUiFailure) failures.push(innerTargetUiFailure);
  const defaultQuantityCopyFailure = await runTest("\u4E2D\u82F1\u6587\u7C98\u8D34\u6587\u6848\u5E94\u8BF4\u660E\u666E\u901A\u7A7A\u6570\u91CF\u5199\u5165 0 \u4E14 inner \u7A7A\u6570\u91CF\u9ED8\u8BA4 1", () => {
    assert(zhLocaleSource.includes("\u6570\u91CF\u5217\u4E3A\u7A7A\u5199\u5165 0"), "\u4E2D\u6587\u6587\u6848\u5E94\u8BF4\u660E\u666E\u901A\u7A7A\u6570\u91CF\u5199\u5165 0");
    assert(zhLocaleSource.includes("\u6570\u91CF\u5217\u4E3A\u7A7A\u65F6\u6309 1 \u518D\u4E58\u5546\u54C1\u4E2D\u5305\u6570"), "\u4E2D\u6587\u6587\u6848\u5E94\u8BF4\u660E inner \u7A7A\u6570\u91CF\u9ED8\u8BA4 1 \u540E\u6362\u7B97");
    assert(enLocaleSource.includes("blank cells in the quantity column write 0"), "\u82F1\u6587\u6587\u6848\u5E94\u8BF4\u660E blank cells write 0");
    assert(enLocaleSource.includes("default to 1 before multiplying"), "\u82F1\u6587\u6587\u6848\u5E94\u8BF4\u660E inner blank cells default to 1 before multiplying");
  });
  if (defaultQuantityCopyFailure) failures.push(defaultQuantityCopyFailure);
  const packageFailure = await runTest("store-order-detail \u6D4B\u8BD5\u811A\u672C\u5E94\u63A5\u5165\u7C98\u8D34\u9884\u89C8\u6D4B\u8BD5", () => {
    assert(packageSource.includes("src/pages/Warehouse/StoreOrders/pastePreview.test.ts"), "test:store-order-detail \u5E94\u8FD0\u884C pastePreview.test.ts");
    assert(packageSource.includes("src/pages/Warehouse/StoreOrders/pasteReplaceJobPolling.test.ts"), "test:store-order-detail \u5E94\u8FD0\u884C pasteReplaceJobPolling.test.ts");
  });
  if (packageFailure) failures.push(packageFailure);
  if (failures.length > 0) {
    throw new Error(`\u5171\u6709 ${failures.length} \u4E2A\u6D4B\u8BD5\u5931\u8D25
- ${failures.join("\n- ")}`);
  }
  console.log("pastePreview.test: ok");
}
await main();
