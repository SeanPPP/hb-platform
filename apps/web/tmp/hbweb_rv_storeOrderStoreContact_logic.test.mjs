// src/pages/Warehouse/StoreOrders/storeOrderStoreContact.ts
function normalizeStoreContactValue(value) {
  return (value || "").trim();
}
function resolveStoreContactDraftValue({
  currentValue,
  previousStoreValue,
  nextStoreValue
}) {
  const normalizedCurrentValue = normalizeStoreContactValue(currentValue);
  const normalizedPreviousStoreValue = normalizeStoreContactValue(previousStoreValue);
  if (!normalizedCurrentValue || normalizedCurrentValue === normalizedPreviousStoreValue) {
    return nextStoreValue || "";
  }
  return currentValue || "";
}

// src/pages/Warehouse/StoreOrders/storeOrderStoreContact.logic.test.ts
function assertEqual(actual, expected, label) {
  if (actual !== expected) {
    throw new Error(`${label}\u3002Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
function runTest(name, execute) {
  execute();
  console.log(`ok - ${name}`);
}
runTest("\u5F53\u524D\u5730\u5740\u4E3A\u7A7A\u65F6\u5207\u6362\u5206\u5E97\u5E94\u81EA\u52A8\u5E26\u5165\u65B0\u5206\u5E97\u9ED8\u8BA4\u5730\u5740", () => {
  assertEqual(
    resolveStoreContactDraftValue({
      currentValue: "",
      previousStoreValue: "Old Address",
      nextStoreValue: "New Address"
    }),
    "New Address",
    "\u7A7A\u5730\u5740\u5E94\u81EA\u52A8\u5207\u6362\u4E3A\u65B0\u5206\u5E97\u9ED8\u8BA4\u503C"
  );
});
runTest("\u5F53\u524D\u90AE\u7BB1\u4ECD\u7B49\u4E8E\u4E0A\u4E00\u4E2A\u5206\u5E97\u9ED8\u8BA4\u503C\u65F6\u5207\u6362\u5206\u5E97\u5E94\u81EA\u52A8\u8986\u76D6", () => {
  assertEqual(
    resolveStoreContactDraftValue({
      currentValue: "old@store.com",
      previousStoreValue: "old@store.com",
      nextStoreValue: "new@store.com"
    }),
    "new@store.com",
    "\u4ECD\u662F\u65E7\u5206\u5E97\u9ED8\u8BA4\u90AE\u7BB1\u65F6\u5E94\u66FF\u6362\u4E3A\u65B0\u5206\u5E97\u9ED8\u8BA4\u90AE\u7BB1"
  );
});
runTest("\u5F53\u524D\u503C\u662F\u7528\u6237\u81EA\u5B9A\u4E49\u5185\u5BB9\u65F6\u5207\u6362\u5206\u5E97\u4E0D\u5E94\u8986\u76D6", () => {
  assertEqual(
    resolveStoreContactDraftValue({
      currentValue: "custom@example.com",
      previousStoreValue: "old@store.com",
      nextStoreValue: "new@store.com"
    }),
    "custom@example.com",
    "\u7528\u6237\u81EA\u5B9A\u4E49\u90AE\u7BB1\u5E94\u4FDD\u7559"
  );
});
runTest("\u4E0A\u4E00\u4E2A\u5206\u5E97\u9ED8\u8BA4\u503C\u4E3A\u7A7A\u65F6\u4E5F\u5E94\u4FDD\u7559\u7528\u6237\u81EA\u5B9A\u4E49\u5185\u5BB9", () => {
  assertEqual(
    resolveStoreContactDraftValue({
      currentValue: "18 Test Road",
      previousStoreValue: "",
      nextStoreValue: "22 New Road"
    }),
    "18 Test Road",
    "\u53EA\u6709\u7A7A\u503C\u6216\u7B49\u4E8E\u65E7\u9ED8\u8BA4\u503C\u65F6\u624D\u5E94\u81EA\u52A8\u66FF\u6362"
  );
});
console.log("storeOrderStoreContact.logic.test: ok");
