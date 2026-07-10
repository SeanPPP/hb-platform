// src/pages/PosAdmin/ProductManagement/productImageBatchScope.ts
function getDefaultSupplierImageBatchScope(selectedRowKeys) {
  return selectedRowKeys.length > 0 ? "selected" : "supplier";
}
function buildSupplierImageBatchScopeRequest(scope, selectedRowKeys) {
  if (scope !== "selected") {
    return {};
  }
  const productCodes = selectedRowKeys.map(String).filter(Boolean);
  if (!productCodes.length) {
    throw new Error("\u8BF7\u5148\u9009\u62E9\u5546\u54C1");
  }
  return { productCodes };
}

// src/pages/PosAdmin/ProductManagement/productImageBatchScope.test.ts
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
assertEqual(
  getDefaultSupplierImageBatchScope(["P001"]),
  "selected",
  "\u5DF2\u6709\u9009\u4E2D\u5546\u54C1\u65F6\u5E94\u9ED8\u8BA4\u66F4\u65B0\u9009\u4E2D\u5546\u54C1"
);
assertEqual(
  getDefaultSupplierImageBatchScope([]),
  "supplier",
  "\u6CA1\u6709\u9009\u4E2D\u5546\u54C1\u65F6\u5E94\u9ED8\u8BA4\u66F4\u65B0\u4F9B\u5E94\u5546\u5168\u90E8\u5546\u54C1"
);
assertDeepEqual(
  buildSupplierImageBatchScopeRequest("selected", ["P001", "P002"]),
  { productCodes: ["P001", "P002"] },
  "\u9009\u62E9\u66F4\u65B0\u9009\u4E2D\u5546\u54C1\u65F6\u5E94\u63D0\u4EA4 productCodes"
);
assertDeepEqual(
  buildSupplierImageBatchScopeRequest("supplier", ["P001", "P002"]),
  {},
  "\u9009\u62E9\u4F9B\u5E94\u5546\u5168\u90E8\u5546\u54C1\u65F6\u4E0D\u5E94\u63D0\u4EA4 productCodes"
);
var missingSelectionError = "";
try {
  buildSupplierImageBatchScopeRequest("selected", []);
} catch (error) {
  missingSelectionError = error instanceof Error ? error.message : "";
}
assertEqual(
  missingSelectionError,
  "\u8BF7\u5148\u9009\u62E9\u5546\u54C1",
  "\u9009\u62E9\u66F4\u65B0\u9009\u4E2D\u5546\u54C1\u4F46\u6CA1\u6709\u5DF2\u9009\u5546\u54C1\u65F6\u5E94\u63D0\u793A\u9009\u62E9\u5546\u54C1"
);
console.log("productImageBatchScope.test: ok");
