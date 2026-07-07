// src/types/storeOrder.ts
var StoreOrderStatusOptions = [
  { value: 0 /* ShoppingCart */, label: "\u8D2D\u7269\u8F66", color: "default" },
  { value: 1 /* Submitted */, label: "\u5DF2\u63D0\u4EA4", color: "processing" },
  { value: 2 /* Completed */, label: "\u5DF2\u5B8C\u6210", color: "success" },
  { value: 3 /* Picking */, label: "\u914D\u8D27\u4E2D", color: "warning" }
];
var StoreOrderStatusLabelMap = Object.fromEntries(
  StoreOrderStatusOptions.map((item) => [item.value, item.label])
);
var StoreOrderStatusColorMap = Object.fromEntries(
  StoreOrderStatusOptions.map((item) => [item.value, item.color])
);

// src/pages/Warehouse/StoreOrders/storeOrderDetailPermissions.ts
function deriveStoreOrderDetailPermissions(flowStatus) {
  const canEditOrder = flowStatus === 1 /* Submitted */ || flowStatus === 3 /* Picking */;
  const canStartPicking = flowStatus === 1 /* Submitted */;
  const canCompleteOrder = flowStatus === 1 /* Submitted */ || flowStatus === 3 /* Picking */;
  return {
    canEditOrder,
    canEditOutboundDate: true,
    canStartPicking,
    canCompleteOrder,
    isReadonlyOrder: !canEditOrder
  };
}

// src/pages/Warehouse/StoreOrders/storeOrderDetailPermissions.test.ts
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
runTest("\u5DF2\u63D0\u4EA4\u548C\u914D\u8D27\u4E2D\u8BA2\u5355\u5E94\u5141\u8BB8\u7F16\u8F91\u660E\u7EC6", () => {
  assertDeepEqual(
    deriveStoreOrderDetailPermissions(1 /* Submitted */),
    {
      canEditOrder: true,
      canEditOutboundDate: true,
      canStartPicking: true,
      canCompleteOrder: true,
      isReadonlyOrder: false
    },
    "\u5DF2\u63D0\u4EA4\u8BA2\u5355\u6743\u9650\u77E9\u9635\u4E0D\u6B63\u786E"
  );
  assertDeepEqual(
    deriveStoreOrderDetailPermissions(3 /* Picking */),
    {
      canEditOrder: true,
      canEditOutboundDate: true,
      canStartPicking: false,
      canCompleteOrder: true,
      isReadonlyOrder: false
    },
    "\u914D\u8D27\u4E2D\u8BA2\u5355\u6743\u9650\u77E9\u9635\u4E0D\u6B63\u786E"
  );
});
runTest("\u8D2D\u7269\u8F66 \u5DF2\u5B8C\u6210 \u672A\u77E5\u72B6\u6001\u90FD\u5E94\u6309\u53EA\u8BFB\u5904\u7406", () => {
  assertDeepEqual(
    deriveStoreOrderDetailPermissions(0 /* ShoppingCart */),
    {
      canEditOrder: false,
      canEditOutboundDate: true,
      canStartPicking: false,
      canCompleteOrder: false,
      isReadonlyOrder: true
    },
    "\u8D2D\u7269\u8F66\u8BA2\u5355\u6743\u9650\u77E9\u9635\u4E0D\u6B63\u786E"
  );
  assertDeepEqual(
    deriveStoreOrderDetailPermissions(2 /* Completed */),
    {
      canEditOrder: false,
      canEditOutboundDate: true,
      canStartPicking: false,
      canCompleteOrder: false,
      isReadonlyOrder: true
    },
    "\u5DF2\u5B8C\u6210\u8BA2\u5355\u6743\u9650\u77E9\u9635\u4E0D\u6B63\u786E"
  );
  assertDeepEqual(
    deriveStoreOrderDetailPermissions(void 0),
    {
      canEditOrder: false,
      canEditOutboundDate: true,
      canStartPicking: false,
      canCompleteOrder: false,
      isReadonlyOrder: true
    },
    "\u7F3A\u5931\u72B6\u6001\u5E94\u6309\u53EA\u8BFB\u5904\u7406"
  );
  assertDeepEqual(
    deriveStoreOrderDetailPermissions(999),
    {
      canEditOrder: false,
      canEditOutboundDate: true,
      canStartPicking: false,
      canCompleteOrder: false,
      isReadonlyOrder: true
    },
    "\u672A\u77E5\u72B6\u6001\u5E94\u6309\u53EA\u8BFB\u5904\u7406"
  );
});
console.log("storeOrderDetailPermissions.test: ok");
