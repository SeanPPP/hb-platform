// src/pages/Warehouse/StoreOrders/syncSessionGuard.ts
async function ensureStoreOrderSyncSession({
  refreshSession,
  clearAuth,
  redirectToLogin,
  currentPath = "/warehouse/store-orders"
}) {
  const refreshed = await refreshSession();
  if (refreshed) {
    return true;
  }
  clearAuth();
  redirectToLogin(`/login?redirect=${encodeURIComponent(currentPath)}`);
  return false;
}

// src/pages/Warehouse/StoreOrders/syncSessionGuard.test.ts
function assertEqual(actual, expected, label) {
  if (actual !== expected) {
    throw new Error(`${label}. Expected: ${String(expected)}, received: ${String(actual)}`);
  }
}
var clearCount = 0;
var redirectedTo = "";
assertEqual(
  await ensureStoreOrderSyncSession({
    refreshSession: async () => true,
    clearAuth: () => {
      clearCount += 1;
    },
    redirectToLogin: (target) => {
      redirectedTo = target;
    }
  }),
  true,
  "\u5237\u65B0\u6210\u529F\u65F6\u5E94\u5141\u8BB8\u7EE7\u7EED\u540C\u6B65"
);
assertEqual(clearCount, 0, "\u5237\u65B0\u6210\u529F\u65F6\u4E0D\u5E94\u6E05\u7406\u767B\u5F55\u72B6\u6001");
assertEqual(redirectedTo, "", "\u5237\u65B0\u6210\u529F\u65F6\u4E0D\u5E94\u8DF3\u8F6C\u767B\u5F55\u9875");
assertEqual(
  await ensureStoreOrderSyncSession({
    refreshSession: async () => false,
    clearAuth: () => {
      clearCount += 1;
    },
    redirectToLogin: (target) => {
      redirectedTo = target;
    },
    currentPath: "/warehouse/store-orders?status=1"
  }),
  false,
  "\u5237\u65B0\u5931\u8D25\u65F6\u5E94\u963B\u6B62\u540C\u6B65"
);
assertEqual(clearCount, 1, "\u5237\u65B0\u5931\u8D25\u65F6\u5E94\u6E05\u7406\u767B\u5F55\u72B6\u6001");
assertEqual(
  redirectedTo,
  "/login?redirect=%2Fwarehouse%2Fstore-orders%3Fstatus%3D1",
  "\u5237\u65B0\u5931\u8D25\u65F6\u5E94\u5E26\u5F53\u524D\u9875\u9762\u8DF3\u8F6C\u767B\u5F55\u9875"
);
