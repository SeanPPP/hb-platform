// src/pages/PosmSalesOrders/posmSalesOrdersLogic.ts
function buildPosmSalesOrderListQuery(state, overrides = {}) {
  const nextState = { ...state, ...overrides };
  return {
    startDate: nextState.startDate,
    endDate: nextState.endDate,
    branchCode: nextState.branchCode || void 0,
    orderType: nextState.orderType,
    keyword: nextState.keyword || void 0,
    pageNumber: nextState.page,
    pageSize: nextState.pageSize
  };
}

// src/pages/PosmSalesOrders/posmSalesOrdersLogic.test.ts
function assertDeepEqual(actual, expected, label) {
  const actualJson = JSON.stringify(actual);
  const expectedJson = JSON.stringify(expected);
  if (actualJson !== expectedJson) {
    throw new Error(`${label}. Expected: ${expectedJson}, received: ${actualJson}`);
  }
}
var currentState = {
  startDate: "2026-06-10",
  endDate: "2026-06-11",
  branchCode: "S01",
  orderType: 1 /* Paid */,
  keyword: "invoice",
  page: 3,
  pageSize: 50
};
assertDeepEqual(
  buildPosmSalesOrderListQuery(currentState, { page: 1 }),
  {
    startDate: "2026-06-10",
    endDate: "2026-06-11",
    branchCode: "S01",
    orderType: 1 /* Paid */,
    keyword: "invoice",
    pageNumber: 1,
    pageSize: 50
  },
  "\u641C\u7D22\u65F6\u5E94\u4F7F\u7528\u663E\u5F0F page=1\uFF0C\u800C\u4E0D\u662F\u65E7\u7684\u7B2C 3 \u9875\u72B6\u6001"
);
assertDeepEqual(
  buildPosmSalesOrderListQuery(currentState, {
    startDate: "2026-06-12",
    endDate: "2026-06-12",
    branchCode: "",
    orderType: -1 /* All */,
    keyword: "",
    page: 1
  }),
  {
    startDate: "2026-06-12",
    endDate: "2026-06-12",
    branchCode: void 0,
    orderType: -1 /* All */,
    keyword: void 0,
    pageNumber: 1,
    pageSize: 50
  },
  "\u91CD\u7F6E\u65F6\u5E94\u4F7F\u7528\u540C\u4E00\u6B21\u8BA1\u7B97\u51FA\u7684\u9ED8\u8BA4\u7B5B\u9009\u6761\u4EF6\u7ACB\u5373\u8BF7\u6C42"
);
console.log("posmSalesOrdersLogic.test: ok");
