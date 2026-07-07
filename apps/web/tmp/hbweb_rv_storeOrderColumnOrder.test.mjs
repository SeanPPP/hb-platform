// src/pages/Warehouse/StoreOrders/columnOrder.ts
function mergeStoreOrderListColumnOrder(savedOrder, availableOrder) {
  const availableSet = new Set(availableOrder);
  const seen = /* @__PURE__ */ new Set();
  const merged = [];
  const savedValues = Array.isArray(savedOrder) ? savedOrder : [];
  for (const value of savedValues) {
    if (typeof value !== "string" || !availableSet.has(value)) {
      continue;
    }
    if (seen.has(value)) {
      continue;
    }
    seen.add(value);
    merged.push(value);
  }
  for (const key of availableOrder) {
    if (!seen.has(key)) {
      merged.push(key);
    }
  }
  return merged;
}
function moveStoreOrderListColumnOrder(currentOrder, activeKey, overKey) {
  if (typeof activeKey !== "string" || typeof overKey !== "string" || activeKey === overKey) {
    return [...currentOrder];
  }
  const fromIndex = currentOrder.indexOf(activeKey);
  const toIndex = currentOrder.indexOf(overKey);
  if (fromIndex < 0 || toIndex < 0) {
    return [...currentOrder];
  }
  const nextOrder = [...currentOrder];
  const [moved] = nextOrder.splice(fromIndex, 1);
  nextOrder.splice(toIndex, 0, moved);
  return nextOrder;
}
function isStoreOrderListColumnOrderCustomized(currentOrder, defaultOrder) {
  if (!currentOrder.length) {
    return false;
  }
  const normalizedOrder = mergeStoreOrderListColumnOrder(currentOrder, defaultOrder);
  return normalizedOrder.length !== defaultOrder.length || normalizedOrder.some((key, index) => key !== defaultOrder[index]);
}

// src/pages/Warehouse/StoreOrders/storeOrderColumnOrder.test.ts
function assertDeepEqual(actual, expected, message) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${message}
Expected: ${expectedJson}
Actual: ${actualJson}`);
  }
}
function assertEqual(actual, expected, message) {
  if (actual !== expected) {
    throw new Error(`${message}
Expected: ${expected}
Actual: ${actual}`);
  }
}
var defaultColumnOrder = [
  "index",
  "orderNo",
  "storeCode",
  "orderDate",
  "flowStatus"
];
assertDeepEqual(
  mergeStoreOrderListColumnOrder(["storeCode", "unknown", "storeCode", "orderNo"], defaultColumnOrder),
  ["storeCode", "orderNo", "index", "orderDate", "flowStatus"],
  "\u5206\u5E97\u8BA2\u8D27\u5217\u8868\u5217\u987A\u5E8F\u5E94\u8FC7\u6EE4\u672A\u77E5\u5217\u3001\u53BB\u91CD\u5E76\u8865\u9F50\u65B0\u589E\u5217"
);
assertDeepEqual(
  mergeStoreOrderListColumnOrder({ storeCode: true }, defaultColumnOrder),
  defaultColumnOrder,
  "\u5206\u5E97\u8BA2\u8D27\u5217\u8868\u5217\u987A\u5E8F\u9047\u5230\u975E\u6570\u7EC4\u6301\u4E45\u5316\u503C\u65F6\u5E94\u56DE\u9000\u9ED8\u8BA4\u987A\u5E8F"
);
assertDeepEqual(
  moveStoreOrderListColumnOrder(defaultColumnOrder, "flowStatus", "orderNo"),
  ["index", "flowStatus", "orderNo", "storeCode", "orderDate"],
  "\u5206\u5E97\u8BA2\u8D27\u5217\u8868\u5217\u62D6\u62FD\u5E94\u628A active \u5217\u79FB\u52A8\u5230 over \u5217\u4F4D\u7F6E"
);
assertDeepEqual(
  moveStoreOrderListColumnOrder(defaultColumnOrder, "missing", "orderNo"),
  defaultColumnOrder,
  "\u5206\u5E97\u8BA2\u8D27\u5217\u8868\u5217\u62D6\u62FD\u9047\u5230\u672A\u77E5 active \u5217\u65F6\u5E94\u4FDD\u6301\u539F\u987A\u5E8F"
);
assertDeepEqual(
  moveStoreOrderListColumnOrder(defaultColumnOrder, "orderNo", "orderNo"),
  defaultColumnOrder,
  "\u5206\u5E97\u8BA2\u8D27\u5217\u8868\u5217\u62D6\u62FD active \u4E0E over \u76F8\u540C\u65F6\u5E94\u4FDD\u6301\u539F\u987A\u5E8F"
);
assertEqual(
  isStoreOrderListColumnOrderCustomized(defaultColumnOrder, defaultColumnOrder),
  false,
  "\u5206\u5E97\u8BA2\u8D27\u5217\u8868\u9ED8\u8BA4\u5217\u987A\u5E8F\u4E0D\u5E94\u5224\u5B9A\u4E3A\u5DF2\u81EA\u5B9A\u4E49"
);
assertEqual(
  isStoreOrderListColumnOrderCustomized(
    moveStoreOrderListColumnOrder(defaultColumnOrder, "flowStatus", "orderNo"),
    defaultColumnOrder
  ),
  true,
  "\u5206\u5E97\u8BA2\u8D27\u5217\u8868\u62D6\u62FD\u5217\u987A\u5E8F\u540E\u5E94\u5224\u5B9A\u4E3A\u5DF2\u81EA\u5B9A\u4E49"
);
assertEqual(
  isStoreOrderListColumnOrderCustomized([], defaultColumnOrder),
  false,
  "\u5206\u5E97\u8BA2\u8D27\u5217\u8868\u5217\u987A\u5E8F\u521D\u59CB\u5316\u4E3A\u7A7A\u65F6\u4E0D\u5E94\u8BEF\u5224\u4E3A\u5DF2\u81EA\u5B9A\u4E49"
);
console.log("storeOrderColumnOrder.test: ok");
