import assert from "node:assert/strict";
import test from "node:test";

import {
  isActiveCashierBoundToDevice,
  resolveProtectedSalesRouteGate,
  resolvePosEntryRoute,
} from "./route-guard";

import type { PosRuntimeState } from "@/core/runtime/pos-runtime";

const ready: PosRuntimeState = {
  phase: "ready",
  database: "ready",
  backend: "reachable",
  device: "authorized-online",
};
const cashier = {
  cashierId: "C1",
  userGuid: "user-1",
  cashierName: "Cashier",
  storeCode: "S1",
  deviceCode: "IPAD-1",
  permissions: [],
  source: "online" as const,
};

test("入口只在设备与数据库就绪后进入登录或收银", () => {
  assert.equal(resolvePosEntryRoute(ready, null), "/login");
  assert.equal(resolvePosEntryRoute(ready, cashier), "/sales");
  assert.equal(
    resolvePosEntryRoute(
      { ...ready, phase: "ready-offline", backend: "offline", device: "authorized-local" },
      null,
    ),
    "/login",
  );
  assert.equal(
    resolvePosEntryRoute(
      { ...ready, phase: "starting", database: "opening", device: "unknown" },
      cashier,
    ),
    null,
  );
  assert.equal(
    resolvePosEntryRoute(
      { ...ready, phase: "locked", device: "locked" },
      cashier,
    ),
    "/registration",
  );
});

test("待审批永远回到注册页，活动收银员不能绕过", () => {
  assert.equal(
    resolvePosEntryRoute(
      {
        ...ready,
        phase: "pending-approval",
        device: "pending-approval",
      },
      cashier,
    ),
    "/registration",
  );
});

test("活动收银员必须绑定当前安全门店和设备", () => {
  assert.equal(
    isActiveCashierBoundToDevice(cashier, {
      storeCode: "S1",
      deviceCode: "IPAD-1",
    }),
    true,
  );
  assert.equal(
    isActiveCashierBoundToDevice(cashier, {
      storeCode: "S2",
      deviceCode: "IPAD-1",
    }),
    false,
  );
});

test("受保护销售页自身阻止直链、锁机和已失效收银员", () => {
  assert.equal(
    resolveProtectedSalesRouteGate(ready, cashier),
    "check-device-identity",
  );
  assert.equal(
    resolveProtectedSalesRouteGate(ready, null),
    "redirect-login",
  );
  assert.equal(
    resolveProtectedSalesRouteGate(
      { ...ready, phase: "locked", device: "locked" },
      cashier,
    ),
    "redirect-index",
  );
});
