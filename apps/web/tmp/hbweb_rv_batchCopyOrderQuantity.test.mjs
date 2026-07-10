// src/pages/Warehouse/StoreOrders/batchCopyOrderQuantity.ts
function buildBatchCopyOrderQuantityPayload(lines) {
  const items = lines.map((line) => ({
    detailGUID: line.detailGUID,
    productCode: line.productCode,
    quantity: Number(line.quantity ?? 0)
  }));
  const overwriteCount = lines.filter((line) => Number(line.allocQuantity ?? 0) > 0).length;
  const zeroOrderQuantityCount = lines.filter((line) => Number(line.quantity ?? 0) === 0).length;
  return {
    items,
    overwriteCount,
    zeroOrderQuantityCount,
    shouldConfirm: lines.length > 0
  };
}
function shouldSubmitBatchCopyOrderQuantity(payload, confirmed = false) {
  return payload.shouldConfirm ? confirmed : false;
}

// src/pages/Warehouse/StoreOrders/batchCopyOrderQuantity.test.ts
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
function createLine(overrides) {
  return {
    detailGUID: overrides.detailGUID ?? `detail-${overrides.productCode ?? "P001"}`,
    productCode: overrides.productCode ?? "P001",
    quantity: overrides.quantity ?? 1,
    allocQuantity: overrides.allocQuantity,
    price: overrides.price ?? 0,
    amount: overrides.amount ?? 0,
    importPrice: overrides.importPrice ?? 0,
    importAmount: overrides.importAmount ?? 0,
    minOrderQuantity: overrides.minOrderQuantity ?? 1,
    isActive: overrides.isActive ?? true
  };
}
function runTest(name, execute) {
  try {
    execute();
    console.log(`ok - ${name}`);
  } catch (error) {
    console.error(`not ok - ${name}`);
    throw error;
  }
}
runTest("\u6279\u91CF\u590D\u5236\u5E94\u6309\u6BCF\u884C\u8BA2\u8D27\u6570\u91CF\u751F\u6210\u53D1\u8D27\u6570\u91CF payload", () => {
  const result = buildBatchCopyOrderQuantityPayload([
    createLine({ productCode: "P001", quantity: 12 }),
    createLine({ productCode: "P002", quantity: 7 })
  ]);
  assertDeepEqual(
    result.items,
    [
      { detailGUID: "detail-P001", productCode: "P001", quantity: 12 },
      { detailGUID: "detail-P002", productCode: "P002", quantity: 7 }
    ],
    "payload \u5E94\u4FDD\u7559\u6BCF\u884C\u660E\u7EC6\u548C\u4E0D\u540C\u8BA2\u8D27\u6570\u91CF"
  );
  assertEqual(result.overwriteCount, 0, "\u672A\u53D1\u8D27\u884C\u4E0D\u5E94\u8BA1\u5165\u8986\u76D6\u6570\u91CF");
  assertEqual(result.zeroOrderQuantityCount, 0, "\u666E\u901A\u8BA2\u8D27\u6570\u91CF\u4E0D\u5E94\u8BA1\u5165 0 \u8BA2\u8D27\u63D0\u793A");
  assertEqual(result.shouldConfirm, true, "\u666E\u901A\u6279\u91CF\u590D\u5236\u4E5F\u5E94\u5148\u4E8C\u6B21\u786E\u8BA4");
});
runTest("\u5DF2\u6709\u53D1\u8D27\u6570\u91CF\u5E94\u8BA1\u5165\u8986\u76D6\u63D0\u793A", () => {
  const result = buildBatchCopyOrderQuantityPayload([
    createLine({ productCode: "P001", quantity: 12, allocQuantity: 6 }),
    createLine({ productCode: "P002", quantity: 7, allocQuantity: 0 })
  ]);
  assertEqual(result.overwriteCount, 1, "\u53EA\u6709 allocQuantity > 0 \u624D\u7B97\u5DF2\u6709\u53D1\u8D27\u6570\u91CF");
  assertEqual(result.shouldConfirm, true, "\u8986\u76D6\u5DF2\u6709\u53D1\u8D27\u6570\u91CF\u9700\u8981\u4E8C\u6B21\u786E\u8BA4");
});
runTest("\u8BA2\u8D27\u6570\u91CF\u4E3A 0 \u65F6\u4ECD\u751F\u6210 0 \u53D1\u8D27 payload \u5E76\u8BA1\u5165\u63D0\u793A", () => {
  const result = buildBatchCopyOrderQuantityPayload([
    createLine({ productCode: "P-ZERO", quantity: 0, allocQuantity: 0 })
  ]);
  assertDeepEqual(
    result.items,
    [{ detailGUID: "detail-P-ZERO", productCode: "P-ZERO", quantity: 0 }],
    "\u8BA2\u8D27\u6570\u91CF 0 \u4E5F\u5E94\u5141\u8BB8\u590D\u5236\u4E3A\u53D1\u8D27\u6570\u91CF 0"
  );
  assertEqual(result.zeroOrderQuantityCount, 1, "\u8BA2\u8D27\u6570\u91CF 0 \u5E94\u8BA1\u5165\u63D0\u793A\u6570\u91CF");
  assertEqual(result.shouldConfirm, true, "\u8BA2\u8D27\u6570\u91CF 0 \u9700\u8981\u4E8C\u6B21\u786E\u8BA4");
});
runTest("\u9700\u8981\u786E\u8BA4\u7684\u590D\u5236\u64CD\u4F5C\u53D6\u6D88\u540E\u4E0D\u5E94\u7EE7\u7EED\u63D0\u4EA4", () => {
  const riskyPayload = buildBatchCopyOrderQuantityPayload([
    createLine({ productCode: "P001", quantity: 12, allocQuantity: 6 })
  ]);
  const safePayload = buildBatchCopyOrderQuantityPayload([
    createLine({ productCode: "P002", quantity: 7, allocQuantity: 0 })
  ]);
  assertEqual(shouldSubmitBatchCopyOrderQuantity(riskyPayload, false), false, "\u53D6\u6D88\u98CE\u9669\u786E\u8BA4\u540E\u4E0D\u5E94\u63D0\u4EA4");
  assertEqual(shouldSubmitBatchCopyOrderQuantity(riskyPayload, true), true, "\u786E\u8BA4\u98CE\u9669\u63D0\u793A\u540E\u5E94\u5141\u8BB8\u63D0\u4EA4");
  assertEqual(shouldSubmitBatchCopyOrderQuantity(safePayload, false), false, "\u53D6\u6D88\u666E\u901A\u590D\u5236\u786E\u8BA4\u540E\u4E5F\u4E0D\u5E94\u63D0\u4EA4");
  assertEqual(shouldSubmitBatchCopyOrderQuantity(safePayload, true), true, "\u786E\u8BA4\u666E\u901A\u590D\u5236\u540E\u5E94\u5141\u8BB8\u63D0\u4EA4");
  assertEqual(
    shouldSubmitBatchCopyOrderQuantity(
      {
        items: [],
        overwriteCount: 0,
        zeroOrderQuantityCount: 0,
        shouldConfirm: false
      },
      true
    ),
    false,
    "\u7A7A\u590D\u5236 payload \u4E0D\u5E94\u63D0\u4EA4"
  );
});
