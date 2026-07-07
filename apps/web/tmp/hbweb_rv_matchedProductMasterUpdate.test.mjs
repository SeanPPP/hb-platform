// src/pages/PosAdmin/LocalSupplierInvoices/InvoiceEdit/matchedProductMasterUpdate.ts
function normalizeText(value) {
  return value?.trim() ?? "";
}
function getMatchedProductMasterUpdateTarget(detail, invoice2) {
  return {
    itemNumber: normalizeText(detail.itemNumber),
    supplierCode: normalizeText(detail.supplierCode) || normalizeText(invoice2?.supplierCode)
  };
}
function buildMatchedProductMasterUpdatePayload(product, detail, invoice2) {
  const target = getMatchedProductMasterUpdateTarget(detail, invoice2);
  if (!target.itemNumber) {
    throw new Error("\u5F53\u524D\u660E\u7EC6\u7F3A\u5C11\u8D27\u53F7\uFF0C\u65E0\u6CD5\u66F4\u6362\u5339\u914D\u5546\u54C1\u4E3B\u6863");
  }
  if (!target.supplierCode) {
    throw new Error("\u5F53\u524D\u660E\u7EC6\u7F3A\u5C11\u4F9B\u5E94\u5546\uFF0C\u65E0\u6CD5\u66F4\u6362\u5339\u914D\u5546\u54C1\u4E3B\u6863");
  }
  return {
    ...product,
    productCategoryGUID: product.categoryGuid ?? product.productCategoryGUID,
    itemNumber: target.itemNumber,
    localSupplierCode: target.supplierCode
  };
}

// src/pages/PosAdmin/LocalSupplierInvoices/InvoiceEdit/matchedProductMasterUpdate.test.ts
function assert(condition, message) {
  if (!condition) {
    throw new Error(message);
  }
}
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}\u3002Expected: ${String(expected)}, received: ${String(actual)}`);
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
var matchedProduct = {
  productCode: "CRAFT301713",
  barcode: "9320760301713",
  productName: "Paint Pot",
  itemNumber: "OLD-ITEM",
  localSupplierCode: "OLD-SUP",
  localSupplierName: "Old Supplier",
  categoryGuid: "category-1",
  purchasePrice: 1.11,
  retailPrice: 2.5,
  isActive: true,
  isAutoPricing: true,
  isSpecialProduct: false,
  productImage: "/old.jpg"
};
var invoice = {
  invoiceGUID: "invoice-1",
  supplierCode: "HEADER-SUP",
  supplierName: "Header Supplier",
  createdAt: "2026-06-05T00:00:00Z"
};
async function main() {
  const failures = [];
  const payloadFailure = await runTest("\u66F4\u6362\u5339\u914D\u5546\u54C1\u4E3B\u6863\u65F6\u5E94\u4FDD\u7559\u5B8C\u6574\u5546\u54C1\u5B57\u6BB5\u5E76\u53EA\u8986\u76D6\u8D27\u53F7\u548C\u4F9B\u5E94\u5546", () => {
    const detail = {
      detailGUID: "detail-1",
      itemNumber: " NEW-ITEM ",
      supplierCode: " NEW-SUP "
    };
    const payload = buildMatchedProductMasterUpdatePayload(matchedProduct, detail, invoice);
    assertEqual(payload.productCode, matchedProduct.productCode, "\u5546\u54C1\u7F16\u7801\u5E94\u4FDD\u6301\u4E0D\u53D8");
    assertEqual(payload.barcode, matchedProduct.barcode, "\u6761\u7801\u5E94\u6CBF\u7528\u5B8C\u6574\u5546\u54C1\u8BE6\u60C5");
    assertEqual(payload.productName, matchedProduct.productName, "\u5546\u54C1\u540D\u79F0\u5E94\u6CBF\u7528\u5B8C\u6574\u5546\u54C1\u8BE6\u60C5");
    assertEqual(payload.categoryGuid, matchedProduct.categoryGuid, "\u524D\u7AEF\u5206\u7C7B\u5B57\u6BB5\u5E94\u6CBF\u7528\u5B8C\u6574\u5546\u54C1\u8BE6\u60C5");
    assertEqual(payload.productCategoryGUID, matchedProduct.categoryGuid, "\u540E\u7AEF\u5206\u7C7B\u5B57\u6BB5\u5E94\u4ECE\u5B8C\u6574\u5546\u54C1\u8BE6\u60C5\u663E\u5F0F\u6620\u5C04");
    assertEqual(payload.purchasePrice, matchedProduct.purchasePrice, "\u8FDB\u8D27\u4EF7\u5E94\u6CBF\u7528\u5B8C\u6574\u5546\u54C1\u8BE6\u60C5");
    assertEqual(payload.retailPrice, matchedProduct.retailPrice, "\u96F6\u552E\u4EF7\u5E94\u6CBF\u7528\u5B8C\u6574\u5546\u54C1\u8BE6\u60C5");
    assertEqual(payload.itemNumber, "NEW-ITEM", "\u8D27\u53F7\u5E94\u6765\u81EA\u5F53\u524D\u660E\u7EC6\u5E76\u53BB\u9664\u9996\u5C3E\u7A7A\u683C");
    assertEqual(payload.localSupplierCode, "NEW-SUP", "\u4F9B\u5E94\u5546\u5E94\u4F18\u5148\u6765\u81EA\u5F53\u524D\u660E\u7EC6\u5E76\u53BB\u9664\u9996\u5C3E\u7A7A\u683C");
  });
  if (payloadFailure) failures.push(payloadFailure);
  const fallbackFailure = await runTest("\u5F53\u524D\u660E\u7EC6\u7F3A\u4F9B\u5E94\u5546\u65F6\u5E94\u56DE\u9000\u5230\u53D1\u7968\u5934\u4F9B\u5E94\u5546", () => {
    const detail = {
      detailGUID: "detail-2",
      itemNumber: "HEADER-ITEM"
    };
    const target = getMatchedProductMasterUpdateTarget(detail, invoice);
    assertEqual(target.itemNumber, "HEADER-ITEM", "\u76EE\u6807\u8D27\u53F7\u5E94\u6765\u81EA\u5F53\u524D\u660E\u7EC6");
    assertEqual(target.supplierCode, "HEADER-SUP", "\u76EE\u6807\u4F9B\u5E94\u5546\u5E94\u56DE\u9000\u5230\u53D1\u7968\u5934");
  });
  if (fallbackFailure) failures.push(fallbackFailure);
  const backendCategoryFailure = await runTest("\u5B8C\u6574\u5546\u54C1\u8BE6\u60C5\u53EA\u8FD4\u56DE\u540E\u7AEF\u5206\u7C7B\u5B57\u6BB5\u65F6\u5E94\u4FDD\u7559 productCategoryGUID", () => {
    const productWithBackendCategory = {
      ...matchedProduct,
      categoryGuid: void 0,
      productCategoryGUID: "backend-category-1"
    };
    const payload = buildMatchedProductMasterUpdatePayload(
      productWithBackendCategory,
      { itemNumber: "NEW-BACKEND", supplierCode: "SUP-BACKEND" },
      invoice
    );
    assertEqual(payload.productCategoryGUID, "backend-category-1", "\u540E\u7AEF\u5206\u7C7B\u5B57\u6BB5\u4E0D\u5E94\u88AB undefined \u8986\u76D6");
  });
  if (backendCategoryFailure) failures.push(backendCategoryFailure);
  const missingItemNumberFailure = await runTest("\u5F53\u524D\u660E\u7EC6\u7F3A\u8D27\u53F7\u65F6\u5E94\u7981\u6B62\u66F4\u6362\u5339\u914D\u5546\u54C1\u4E3B\u6863", () => {
    let threw = false;
    try {
      buildMatchedProductMasterUpdatePayload(matchedProduct, { supplierCode: "SUP" }, invoice);
    } catch (error) {
      threw = error instanceof Error && error.message.includes("\u5F53\u524D\u660E\u7EC6\u7F3A\u5C11\u8D27\u53F7");
    }
    assert(threw, "\u7F3A\u8D27\u53F7\u65F6\u5E94\u629B\u51FA\u660E\u786E\u9519\u8BEF");
  });
  if (missingItemNumberFailure) failures.push(missingItemNumberFailure);
  const missingSupplierFailure = await runTest("\u5F53\u524D\u660E\u7EC6\u548C\u53D1\u7968\u5934\u90FD\u7F3A\u4F9B\u5E94\u5546\u65F6\u5E94\u7981\u6B62\u66F4\u6362\u5339\u914D\u5546\u54C1\u4E3B\u6863", () => {
    let threw = false;
    try {
      buildMatchedProductMasterUpdatePayload(
        matchedProduct,
        { itemNumber: "ITEM-4" },
        {}
      );
    } catch (error) {
      threw = error instanceof Error && error.message.includes("\u5F53\u524D\u660E\u7EC6\u7F3A\u5C11\u4F9B\u5E94\u5546");
    }
    assert(threw, "\u7F3A\u4F9B\u5E94\u5546\u65F6\u5E94\u629B\u51FA\u660E\u786E\u9519\u8BEF");
  });
  if (missingSupplierFailure) failures.push(missingSupplierFailure);
  if (failures.length) {
    console.error(`
${failures.length} test(s) failed:`);
    for (const failure of failures) {
      console.error(`- ${failure}`);
    }
    process.exit(1);
  }
}
void main();
