// src/pages/DomesticPurchase/DomesticProducts/setItemsBulkPaste.ts
var PRICE_FIELDS = /* @__PURE__ */ new Set(["domesticPrice", "oemPrice"]);
function roundPrice(value) {
  return Math.round((value + Number.EPSILON) * 100) / 100;
}
function parseSetItemPrice(value) {
  const normalized = value.trim().replace(/,/g, "").replace(/[^\d.-]/g, "");
  if (!normalized || normalized === "-" || normalized === "." || normalized === "-.") {
    return void 0;
  }
  const parsed = Number(normalized);
  if (!Number.isFinite(parsed) || parsed < 0) {
    return void 0;
  }
  return roundPrice(parsed);
}
function parseClipboardColumn(clipboardText) {
  const values = clipboardText.replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n").map((line) => line.split("	")[0]?.trim() ?? "");
  while (values.length && values[values.length - 1] === "") {
    values.pop();
  }
  return values;
}
function createEmptySetItem(id) {
  return { id };
}
function applySetItemColumnPaste({
  items,
  startRowId,
  field,
  clipboardText,
  createId
}) {
  const values = parseClipboardColumn(clipboardText);
  const startIndex = startRowId ? items.findIndex((item) => item.id === startRowId) : 0;
  if (startIndex < 0 || !values.length) {
    return { items, appliedCount: 0, skippedCount: 0 };
  }
  const nextItems = [...items];
  let appliedCount = 0;
  let skippedCount = 0;
  values.forEach((rawValue, offset) => {
    const targetIndex = startIndex + offset;
    while (nextItems.length <= targetIndex) {
      nextItems.push(createEmptySetItem(createId(nextItems.length)));
    }
    if (!rawValue) {
      return;
    }
    if (PRICE_FIELDS.has(field)) {
      const price = parseSetItemPrice(rawValue);
      if (price === void 0) {
        skippedCount += 1;
        return;
      }
      nextItems[targetIndex] = { ...nextItems[targetIndex], [field]: price };
      appliedCount += 1;
      return;
    }
    nextItems[targetIndex] = { ...nextItems[targetIndex], [field]: rawValue };
    appliedCount += 1;
  });
  return { items: nextItems, appliedCount, skippedCount };
}
function calculateSetItemPriceTotals(items) {
  let hasDomesticPrice = false;
  let domesticPriceTotal = 0;
  let hasOemPrice = false;
  let oemPriceTotal = 0;
  items.forEach((item) => {
    if (item.domesticPrice !== void 0 && item.domesticPrice !== null) {
      hasDomesticPrice = true;
      domesticPriceTotal += item.domesticPrice;
    }
    if (item.oemPrice !== void 0 && item.oemPrice !== null) {
      hasOemPrice = true;
      oemPriceTotal += item.oemPrice;
    }
  });
  return {
    hasDomesticPrice,
    domesticPriceTotal: hasDomesticPrice ? roundPrice(domesticPriceTotal) : void 0,
    hasOemPrice,
    oemPriceTotal: hasOemPrice ? roundPrice(oemPriceTotal) : void 0
  };
}
function buildSetProductPriceSyncPayload(product, totals) {
  if (!totals.hasDomesticPrice && !totals.hasOemPrice) {
    return void 0;
  }
  return {
    productName: product.name,
    englishProductName: product.nameEn,
    barcode: product.barcode,
    productSpecification: product.specs,
    productType: product.productType,
    // 主码价格更新仍复用现有接口；未同步的价格带回当前值，避免国内价-only/零售价-only 保存时被覆盖为空。
    domesticPrice: totals.hasDomesticPrice ? totals.domesticPriceTotal : product.domesticPrice,
    oemPrice: totals.hasOemPrice ? totals.oemPriceTotal : product.labelPrice,
    importPrice: product.importPrice,
    packingQuantity: product.packingQty,
    unitVolume: product.volume,
    middlePackQuantity: product.middlePackQty,
    packingSize: product.packingSize,
    material: product.material,
    remarks: product.remark,
    productImage: product.productImage,
    isActive: product.isActive
  };
}

// src/pages/DomesticPurchase/DomesticProducts/setItemsBulkPaste.test.ts
function assertEqual(actual, expected, label) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
function assertDeepEqual(actual, expected, label) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`);
  }
}
var baseItems = [
  { id: "row-1", productName: "\u65E7\u5546\u54C11", setProductNo: "NO1", setBarcode: "BC1", domesticPrice: 1 },
  { id: "row-2", productName: "\u65E7\u5546\u54C12", setProductNo: "NO2", setBarcode: "BC2", oemPrice: 2 }
];
var productNameResult = applySetItemColumnPaste({
  items: baseItems,
  startRowId: "row-2",
  field: "productName",
  clipboardText: "\u65B0\u54C1A\n\u65B0\u54C1B\n\u65B0\u54C1C",
  createId: (index) => `temp-${index}`
});
assertDeepEqual(
  productNameResult.items,
  [
    { id: "row-1", productName: "\u65E7\u5546\u54C11", setProductNo: "NO1", setBarcode: "BC1", domesticPrice: 1 },
    { id: "row-2", productName: "\u65B0\u54C1A", setProductNo: "NO2", setBarcode: "BC2", oemPrice: 2 },
    { id: "temp-2", productName: "\u65B0\u54C1B" },
    { id: "temp-3", productName: "\u65B0\u54C1C" }
  ],
  "\u7C98\u8D34\u5546\u54C1\u540D\u79F0\u5E94\u4ECE\u5F53\u524D\u884C\u5199\u5165\uFF0C\u5E76\u5728\u884C\u6570\u4E0D\u8DB3\u65F6\u65B0\u589E\u7A7A\u5B50\u9879"
);
assertEqual(productNameResult.appliedCount, 3, "\u5546\u54C1\u540D\u79F0\u7C98\u8D34\u5E94\u7EDF\u8BA1\u6210\u529F\u5199\u5165\u6570\u91CF");
assertEqual(productNameResult.skippedCount, 0, "\u5546\u54C1\u540D\u79F0\u7C98\u8D34\u4E0D\u5E94\u8DF3\u8FC7\u6709\u6548\u5185\u5BB9");
var emptyColumnResult = applySetItemColumnPaste({
  items: [],
  field: "productName",
  clipboardText: "\u7A7A\u8868\u9996\u884C\n\u7A7A\u8868\u7B2C\u4E8C\u884C",
  createId: (index) => `empty-${index}`
});
assertDeepEqual(
  emptyColumnResult.items,
  [
    { id: "empty-0", productName: "\u7A7A\u8868\u9996\u884C" },
    { id: "empty-1", productName: "\u7A7A\u8868\u7B2C\u4E8C\u884C" }
  ],
  "\u7A7A\u8868\u70B9\u51FB\u5217\u5934\u7C98\u8D34\u65F6\u5E94\u4ECE\u7B2C\u4E00\u884C\u5F00\u59CB\u81EA\u52A8\u521B\u5EFA\u5B50\u9879"
);
var skipBlankRowsResult = applySetItemColumnPaste({
  items: [],
  field: "productName",
  clipboardText: "\u7B2C\u4E00\u884C\n\n	\n\u7B2C\u4E8C\u884C\n",
  createId: (index) => `skip-blank-${index}`
});
assertDeepEqual(
  skipBlankRowsResult.items,
  [
    { id: "skip-blank-0", productName: "\u7B2C\u4E00\u884C" },
    { id: "skip-blank-1" },
    { id: "skip-blank-2" },
    { id: "skip-blank-3", productName: "\u7B2C\u4E8C\u884C" }
  ],
  "\u7C98\u8D34\u65F6\u5E94\u4FDD\u7559 Excel \u7A7A\u5355\u5143\u683C\u884C\u4F4D\uFF0C\u907F\u514D\u540E\u7EED\u6570\u636E\u9519\u884C"
);
assertEqual(skipBlankRowsResult.appliedCount, 2, "\u7A7A\u5355\u5143\u683C\u4E0D\u5E94\u8BA1\u5165\u6210\u529F\u5199\u5165\u6570\u91CF");
var priceResult = applySetItemColumnPaste({
  items: baseItems,
  startRowId: "row-1",
  field: "domesticPrice",
  clipboardText: "\uFFE51,234.50\nabc\n$6",
  createId: (index) => `price-${index}`
});
assertDeepEqual(
  priceResult.items,
  [
    { id: "row-1", productName: "\u65E7\u5546\u54C11", setProductNo: "NO1", setBarcode: "BC1", domesticPrice: 1234.5 },
    { id: "row-2", productName: "\u65E7\u5546\u54C12", setProductNo: "NO2", setBarcode: "BC2", oemPrice: 2 },
    { id: "price-2", domesticPrice: 6 }
  ],
  "\u4EF7\u683C\u7C98\u8D34\u5E94\u6E05\u7406\u8D27\u5E01\u7B26\u53F7\u548C\u5343\u5206\u4F4D\uFF0C\u65E0\u6548\u503C\u8DF3\u8FC7\u4F46\u4FDD\u7559\u884C\u4F4D"
);
assertEqual(priceResult.appliedCount, 2, "\u4EF7\u683C\u7C98\u8D34\u5E94\u53EA\u7EDF\u8BA1\u6709\u6548\u4EF7\u683C");
assertEqual(priceResult.skippedCount, 1, "\u4EF7\u683C\u7C98\u8D34\u5E94\u7EDF\u8BA1\u65E0\u6548\u4EF7\u683C");
assertEqual(parseSetItemPrice("\uFFE51,200.30"), 1200.3, "\u4EF7\u683C\u89E3\u6790\u5E94\u79FB\u9664\u4EBA\u6C11\u5E01\u7B26\u53F7\u548C\u5343\u5206\u4F4D");
assertEqual(parseSetItemPrice("USD 8.5"), 8.5, "\u4EF7\u683C\u89E3\u6790\u5E94\u79FB\u9664\u5B57\u6BCD\u8D27\u5E01\u6807\u8BC6");
assertEqual(parseSetItemPrice("abc"), void 0, "\u4EF7\u683C\u89E3\u6790\u9047\u5230\u65E0\u6548\u5185\u5BB9\u5E94\u8FD4\u56DE undefined");
assertDeepEqual(
  calculateSetItemPriceTotals([
    { id: "a", domesticPrice: 1.1 },
    { id: "b", domesticPrice: 2.2, oemPrice: 0 },
    { id: "c" }
  ]),
  {
    hasDomesticPrice: true,
    domesticPriceTotal: 3.3,
    hasOemPrice: true,
    oemPriceTotal: 0
  },
  "\u5408\u8BA1\u5E94\u53EA\u5728\u5B58\u5728\u975E\u7A7A\u4EF7\u683C\u65F6\u8FD4\u56DE\u5BF9\u5E94\u5408\u8BA1\uFF0C0 \u4E5F\u5E94\u89C6\u4E3A\u975E\u7A7A\u4EF7\u683C"
);
assertDeepEqual(
  calculateSetItemPriceTotals([{ id: "a" }, { id: "b" }]),
  {
    hasDomesticPrice: false,
    domesticPriceTotal: void 0,
    hasOemPrice: false,
    oemPriceTotal: void 0
  },
  "\u5168\u90E8\u4EF7\u683C\u4E3A\u7A7A\u65F6\u4E0D\u5E94\u751F\u6210\u4E3B\u7801\u8986\u76D6\u503C"
);
var setProduct = {
  id: "set-1",
  supplierCode: "SUP",
  supplierName: "\u4F9B\u5E94\u5546",
  name: "\u4E3B\u5957\u88C5",
  nameEn: "Main Set",
  itemNumber: "SET001",
  barcode: "BAR001",
  specs: "\u89C4\u683C",
  productType: 1 /* SET */,
  domesticPrice: 9,
  labelPrice: 19,
  importPrice: 5,
  packingQty: 10,
  volume: 1.25,
  middlePackQty: 2,
  packingSize: "10x10",
  material: "\u7EB8",
  remark: "\u5907\u6CE8",
  productImage: "https://example.com/a.png",
  isActive: true,
  createdAt: "2026-01-01T00:00:00.000Z"
};
assertDeepEqual(
  buildSetProductPriceSyncPayload(setProduct, {
    hasDomesticPrice: true,
    domesticPriceTotal: 12,
    hasOemPrice: false,
    oemPriceTotal: void 0
  }),
  {
    productName: "\u4E3B\u5957\u88C5",
    englishProductName: "Main Set",
    barcode: "BAR001",
    productSpecification: "\u89C4\u683C",
    productType: 1 /* SET */,
    domesticPrice: 12,
    oemPrice: 19,
    importPrice: 5,
    packingQuantity: 10,
    unitVolume: 1.25,
    middlePackQuantity: 2,
    packingSize: "10x10",
    material: "\u7EB8",
    remarks: "\u5907\u6CE8",
    productImage: "https://example.com/a.png",
    isActive: true
  },
  "\u4EC5\u540C\u6B65\u56FD\u5185\u4EF7\u65F6\u5E94\u4FDD\u7559\u4E3B\u7801\u5F53\u524D\u96F6\u552E\u4EF7\uFF0C\u907F\u514D\u8986\u76D6\u4E3A\u7A7A"
);
assertDeepEqual(
  buildSetProductPriceSyncPayload(setProduct, {
    hasDomesticPrice: false,
    domesticPriceTotal: void 0,
    hasOemPrice: true,
    oemPriceTotal: 23
  }),
  {
    productName: "\u4E3B\u5957\u88C5",
    englishProductName: "Main Set",
    barcode: "BAR001",
    productSpecification: "\u89C4\u683C",
    productType: 1 /* SET */,
    domesticPrice: 9,
    oemPrice: 23,
    importPrice: 5,
    packingQuantity: 10,
    unitVolume: 1.25,
    middlePackQuantity: 2,
    packingSize: "10x10",
    material: "\u7EB8",
    remarks: "\u5907\u6CE8",
    productImage: "https://example.com/a.png",
    isActive: true
  },
  "\u4EC5\u540C\u6B65\u96F6\u552E\u4EF7\u65F6\u5E94\u4FDD\u7559\u4E3B\u7801\u5F53\u524D\u56FD\u5185\u4EF7\uFF0C\u907F\u514D\u8986\u76D6\u4E3A\u7A7A"
);
assertEqual(
  buildSetProductPriceSyncPayload(setProduct, {
    hasDomesticPrice: false,
    domesticPriceTotal: void 0,
    hasOemPrice: false,
    oemPriceTotal: void 0
  }),
  void 0,
  "\u5B50\u9879\u4EF7\u683C\u5168\u90E8\u4E3A\u7A7A\u65F6\u4E0D\u5E94\u751F\u6210\u4E3B\u7801\u66F4\u65B0 payload"
);
