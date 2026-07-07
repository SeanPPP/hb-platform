// src/pages/DomesticPurchase/ProductCreation/batchCreateRules.test.ts
import assert from "node:assert/strict";

// src/pages/DomesticPurchase/ProductCreation/batchCreateRules.ts
var normalizeCreateCount = (value) => Math.max(1, Math.floor(Number(value) || 1));
var createDraftProductKey = (prefix, index) => `${prefix}_${Date.now()}_${index}_${Math.random().toString(36).slice(2, 8)}`;
function createDraftSetSubItem(keyFactory = createDraftProductKey) {
  return {
    key: keyFactory("sub", 0),
    productName: ""
  };
}
function createDraftProduct(type, index, price, keyFactory = createDraftProductKey) {
  return {
    key: keyFactory("temp", index),
    productName: "",
    productType: type,
    privateLabelPrice: price ?? void 0,
    createCount: type === 1 /* SET */ ? 1 : void 0,
    setQuantity: type === 1 /* SET */ ? 1 : void 0,
    subItems: type === 1 /* SET */ ? [createDraftSetSubItem(keyFactory)] : void 0
  };
}
function isMeaningfulSetSubItem(subItem) {
  return Boolean(subItem.productName?.trim() || subItem.privateLabelPrice != null);
}
function getValidSetSubItems(subItems) {
  return (subItems || []).filter(isMeaningfulSetSubItem);
}
function findInvalidSetProduct(products2) {
  const invalidIndex = products2.findIndex((product) => product.productType === 1 /* SET */ && getValidSetSubItems(product.subItems).length === 0);
  if (invalidIndex < 0) return void 0;
  return {
    key: products2[invalidIndex].key,
    index: invalidIndex + 1
  };
}
function buildPreviewItems(products2, prefixCode) {
  let itemIndex = 1;
  return products2.flatMap((product) => {
    if (product.productType !== 1 /* SET */) {
      return [{ ...product, itemNumber: `${prefixCode}${String(itemIndex++).padStart(4, "0")}` }];
    }
    const expandedRows = [];
    const createCount = normalizeCreateCount(product.createCount);
    const validSubItems = getValidSetSubItems(product.subItems);
    for (let i = 0; i < createCount; i++) {
      const parentPreviewKey = `${product.key}_${i}`;
      expandedRows.push({ ...product, key: parentPreviewKey, itemNumber: `${prefixCode}${String(itemIndex++).padStart(4, "0")}` });
      validSubItems.forEach((subItem) => {
        expandedRows.push({
          ...subItem,
          key: `${parentPreviewKey}_${subItem.key}`,
          productName: subItem.productName?.trim() || "",
          productType: 2 /* SET_SUB_ITEM */,
          privateLabelPrice: subItem.privateLabelPrice ?? void 0,
          itemNumber: `${prefixCode}${String(itemIndex++).padStart(4, "0")}`,
          parentPreviewKey
        });
      });
    }
    return expandedRows;
  });
}
function applyBatchAddProducts({
  products: products2,
  selectedRowKeys,
  expandedRowKeys,
  type,
  count,
  price,
  mode,
  createProduct = createDraftProduct
}) {
  if (mode === "append") {
    const newProducts = Array.from({ length: count }, (_, index) => createProduct(type, products2.length + index, price));
    return {
      products: [...products2, ...newProducts],
      selectedRowKeys,
      expandedRowKeys: [
        ...expandedRowKeys,
        ...newProducts.filter((item) => item.productType === 1 /* SET */).map((item) => item.key)
      ]
    };
  }
  const nextProducts = Array.from({ length: count }, (_, index) => createProduct(type, index, price));
  return {
    products: nextProducts,
    selectedRowKeys: [],
    expandedRowKeys: nextProducts.filter((item) => item.productType === 1 /* SET */).map((item) => item.key)
  };
}
function buildCreateBatchItems(products2) {
  return products2.map((product) => ({
    productName: product.productName?.trim() || void 0,
    productType: product.productType,
    privateLabelPrice: product.privateLabelPrice ?? void 0,
    setQuantity: product.setQuantity ?? void 0,
    setPrice: product.setPrice ?? void 0,
    createCount: product.productType === 1 /* SET */ ? normalizeCreateCount(product.createCount) : void 0,
    subItems: product.productType === 1 /* SET */ ? getValidSetSubItems(product.subItems).map((subItem) => ({
      productName: subItem.productName?.trim() || void 0,
      productType: 2 /* SET_SUB_ITEM */,
      privateLabelPrice: subItem.privateLabelPrice ?? void 0
    })) : void 0
  }));
}

// src/pages/DomesticPurchase/ProductCreation/batchCreateRules.test.ts
var keyIndex = 0;
var nextKey = (prefix) => `${prefix}-${keyIndex++}`;
var products = [
  {
    key: "normal-1",
    productName: " \u666E\u901A\u5546\u54C1 ",
    productType: 0 /* NORMAL */,
    privateLabelPrice: 12.5
  },
  {
    key: "set-1",
    productName: " \u5957\u88C5\u5546\u54C1 ",
    productType: 1 /* SET */,
    createCount: 2.9,
    setQuantity: 1,
    setPrice: 25,
    subItems: [
      { key: "empty-sub", productName: " ", privateLabelPrice: null },
      { key: "sub-1", productName: " \u5B50\u9879\u5546\u54C1 ", privateLabelPrice: 8 }
    ]
  }
];
assert.equal(normalizeCreateCount(void 0), 1);
assert.equal(normalizeCreateCount(0), 1);
assert.equal(normalizeCreateCount(2.9), 2);
assert.equal(findInvalidSetProduct(products), void 0);
assert.deepEqual(findInvalidSetProduct([
  { key: "set-empty", productName: "", productType: 1 /* SET */, subItems: [] }
]), { key: "set-empty", index: 1 });
var requestItems = buildCreateBatchItems(products);
assert.equal(requestItems[0].productName, "\u666E\u901A\u5546\u54C1");
assert.equal(requestItems[0].createCount, void 0);
assert.equal(requestItems[1].productName, "\u5957\u88C5\u5546\u54C1");
assert.equal(requestItems[1].createCount, 2);
assert.equal(requestItems[1].subItems?.length, 1);
assert.equal(requestItems[1].subItems?.[0].productName, "\u5B50\u9879\u5546\u54C1");
keyIndex = 0;
var appended = applyBatchAddProducts({
  products: [createDraftProduct(0 /* NORMAL */, 0, null, nextKey)],
  selectedRowKeys: [],
  expandedRowKeys: [],
  type: 1 /* SET */,
  count: 2,
  price: 9.5,
  mode: "append",
  createProduct: (type, index, price) => createDraftProduct(type, index, price, nextKey)
});
assert.equal(appended.products.length, 3);
assert.equal(appended.products[0].productType, 0 /* NORMAL */);
assert.equal(appended.products[1].productType, 1 /* SET */);
assert.equal(appended.products[2].productType, 1 /* SET */);
assert.deepEqual(appended.expandedRowKeys, ["temp-1", "temp-3"]);
assert.equal(appended.products[1].subItems?.length, 1);
keyIndex = 0;
var overwritten = applyBatchAddProducts({
  products,
  selectedRowKeys: ["normal-1"],
  expandedRowKeys: ["set-1"],
  type: 1 /* SET */,
  count: 2,
  price: 10,
  mode: "overwrite",
  createProduct: (type, index, price) => createDraftProduct(type, index, price, nextKey)
});
assert.equal(overwritten.products.length, 2);
assert.ok(overwritten.products.every((product) => product.productType === 1 /* SET */));
assert.deepEqual(overwritten.selectedRowKeys, []);
assert.deepEqual(overwritten.expandedRowKeys, ["temp-0", "temp-2"]);
var previewItems = buildPreviewItems([
  {
    key: "set-preview",
    productName: "\u5957\u88C5",
    productType: 1 /* SET */,
    createCount: 2,
    subItems: [
      { key: "sub-preview", productName: " \u5B50\u9879 ", privateLabelPrice: 3 },
      { key: "price-only", productName: "   ", privateLabelPrice: 4 }
    ]
  }
], "HB");
assert.deepEqual(previewItems.map((item) => item.itemNumber), ["HB0001", "HB0002", "HB0003", "HB0004", "HB0005", "HB0006"]);
assert.equal(previewItems[1].productName, "\u5B50\u9879");
assert.equal(previewItems[2].productName, "");
