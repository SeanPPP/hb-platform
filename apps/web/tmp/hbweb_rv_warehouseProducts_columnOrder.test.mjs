// src/pages/Warehouse/Products/columnOrder.ts
function mergeWarehouseProductColumnOrder(savedOrder, availableOrder) {
  const availableSet = new Set(availableOrder);
  const seen = /* @__PURE__ */ new Set();
  const merged = [];
  for (const value of savedOrder ?? []) {
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
function moveWarehouseProductColumnOrder(currentOrder, activeKey, overKey) {
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
function isWarehouseProductColumnOrderCustomized(currentOrder, defaultOrder) {
  const normalizedOrder = mergeWarehouseProductColumnOrder(currentOrder, defaultOrder);
  return normalizedOrder.length !== defaultOrder.length || normalizedOrder.some((key, index) => key !== defaultOrder[index]);
}

// src/pages/Warehouse/Products/columnOrder.test.ts
function assertDeepEqual(actual, expected, message) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${message}\u3002Expected: ${expectedJson}, received: ${actualJson}`);
  }
}
assertDeepEqual(
  mergeWarehouseProductColumnOrder(["name", "itemNumber"], ["itemNumber", "name", "barcode"]),
  ["name", "itemNumber", "barcode"],
  "\u5546\u54C1\u7BA1\u7406\u5217\u987A\u5E8F\u5E94\u4FDD\u7559\u5DF2\u4FDD\u5B58\u987A\u5E8F\uFF0C\u5E76\u81EA\u52A8\u8FFD\u52A0\u65B0\u589E\u5217"
);
assertDeepEqual(
  mergeWarehouseProductColumnOrder(["removed", "name", "name", "barcode"], ["itemNumber", "name", "barcode"]),
  ["name", "barcode", "itemNumber"],
  "\u5546\u54C1\u7BA1\u7406\u5217\u987A\u5E8F\u5E94\u8FC7\u6EE4\u5E9F\u5F03\u5217\u548C\u91CD\u590D\u5217"
);
assertDeepEqual(
  mergeWarehouseProductColumnOrder(["removed", "barcode"], ["itemNumber", "name", "barcode"]),
  ["barcode", "itemNumber", "name"],
  "\u5546\u54C1\u7BA1\u7406\u5217\u987A\u5E8F\u6E05\u7406\u5E9F\u5F03\u5217\u540E\u5E94\u6309\u9ED8\u8BA4\u987A\u5E8F\u8865\u9F50\u7F3A\u5931\u5217"
);
assertDeepEqual(
  moveWarehouseProductColumnOrder(["itemNumber", "name", "barcode"], "barcode", "itemNumber"),
  ["barcode", "itemNumber", "name"],
  "\u5546\u54C1\u7BA1\u7406\u5217\u62D6\u62FD\u5E94\u628A active \u5217\u79FB\u52A8\u5230 over \u5217\u4F4D\u7F6E"
);
assertDeepEqual(
  moveWarehouseProductColumnOrder(["itemNumber", "name", "barcode"], "missing", "name"),
  ["itemNumber", "name", "barcode"],
  "\u5546\u54C1\u7BA1\u7406\u5217\u62D6\u62FD\u9047\u5230\u65E0\u6548\u5217\u65F6\u5E94\u4FDD\u6301\u539F\u987A\u5E8F"
);
assertDeepEqual(
  isWarehouseProductColumnOrderCustomized(["itemNumber", "name", "barcode"], ["itemNumber", "name", "barcode"]),
  false,
  "\u5546\u54C1\u7BA1\u7406\u5217\u987A\u5E8F\u4E0E\u9ED8\u8BA4\u987A\u5E8F\u4E00\u81F4\u65F6\u4E0D\u5E94\u89C6\u4E3A\u81EA\u5B9A\u4E49"
);
assertDeepEqual(
  isWarehouseProductColumnOrderCustomized(["name", "itemNumber", "barcode"], ["itemNumber", "name", "barcode"]),
  true,
  "\u5546\u54C1\u7BA1\u7406\u5217\u987A\u5E8F\u4E0E\u9ED8\u8BA4\u987A\u5E8F\u4E0D\u4E00\u81F4\u65F6\u5E94\u89C6\u4E3A\u81EA\u5B9A\u4E49"
);
console.log("warehouseProducts.columnOrder.test: ok");
