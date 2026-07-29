import assert from "node:assert/strict";
import test from "node:test";

import {
  CurrentCashierSession,
  CurrentCashierSessionError,
} from "./current-cashier-session";

import type { CashierLoginResult } from "@/core/security/cashier-authentication";

test("只返回脱敏冻结投影，权限复制去重且原始票据不会进入可信会话", () => {
  const registry = new CurrentCashierSession();
  const permissions = [
    "Permissions.PosTerminal.Sell",
    " Permissions.PosTerminal.Sell ",
    "Permissions.PosTerminal.CashDrawer.Open",
  ];
  const summary = registry.activate(
    registry.beginAuthentication(),
    loginResult({ permissionCodes: permissions }),
    { storeCode: "S1", deviceCode: "IPAD-1" },
  );
  permissions.push("Permissions.PosTerminal.Admin");

  assert.deepEqual(summary.permissions, [
    "Permissions.PosTerminal.CashDrawer.Open",
    "Permissions.PosTerminal.Sell",
  ]);
  assert.equal(Object.isFrozen(summary), true);
  assert.equal(Object.isFrozen(summary.permissions), true);
  assert.doesNotMatch(
    JSON.stringify({ summary, trusted: registry.require() }),
    /authorization-secret|emergency-grant/i,
  );
});

test("门店设备不匹配时不激活，旧 epoch 和 clear 后 lease 全部失效", () => {
  const registry = new CurrentCashierSession();
  const invalidEpoch = registry.beginAuthentication();
  assert.throws(
    () =>
      registry.activate(invalidEpoch, loginResult({ storeCode: "OTHER" }), {
        storeCode: "S1",
        deviceCode: "IPAD-1",
      }),
    hasCode("CASHIER_SESSION_IDENTITY_INVALID"),
  );
  assert.throws(
    () => registry.require(),
    hasCode("CURRENT_CASHIER_REQUIRED"),
  );

  const superseded = registry.beginAuthentication();
  const current = registry.beginAuthentication();
  assert.throws(
    () =>
      registry.activate(superseded, loginResult(), {
        storeCode: "S1",
        deviceCode: "IPAD-1",
      }),
    hasCode("CASHIER_AUTHENTICATION_SUPERSEDED"),
  );
  registry.activate(current, loginResult(), {
    storeCode: "S1",
    deviceCode: "IPAD-1",
  });
  const lease = registry.createLease();
  assert.equal(lease.get().cashierId, "cashier-1");
  registry.clear();
  assert.throws(
    () => lease.get(),
    hasCode("CURRENT_CASHIER_REQUIRED"),
  );
});

test("紧急登录只按可信单调有效期保留 lease，墙钟回拨不能延长且过期立即失效", () => {
  let systemUptimeMs = 10_000;
  let expiryNotifications = 0;
  const registry = new CurrentCashierSession(
    () => systemUptimeMs,
    () => {
      expiryNotifications += 1;
    },
  );
  const summary = registry.activate(
    registry.beginAuthentication(),
    {
      ...loginResult({
        isEmergencyOverride: true,
        authorizationExpiresAtUtc: "2026-07-28T10:01:00.000Z",
        emergencyGrantId: "10000000-0000-4000-8000-000000000001",
      }),
      emergencyTiming: {
        systemUptimeMs: 10_000,
        trustedNowEpochMs: Date.parse(
          "2026-07-28T10:00:00.000Z",
        ),
      },
      source: "emergency-override",
    },
    { storeCode: "S1", deviceCode: "IPAD-1" },
  );
  const lease = registry.createLease();

  assert.equal(summary.source, "emergency-override");
  assert.equal(lease.get().isEmergencyOverride, true);
  systemUptimeMs = 70_000;
  assert.throws(
    () => lease.get(),
    hasCode("CURRENT_CASHIER_REQUIRED"),
  );
  assert.throws(
    () => registry.require(),
    hasCode("CURRENT_CASHIER_REQUIRED"),
  );
  assert.equal(expiryNotifications, 1);
});

function loginResult(
  overrides: Readonly<Record<string, unknown>> = {},
): CashierLoginResult {
  return {
    source: "online",
    session: {
      authorizationToken: "authorization-secret",
      authorizationExpiresAtUtc: "2026-07-29T00:00:00.000Z",
      emergencyGrantId: "emergency-grant-secret",
      cashierId: "cashier-1",
      userGuid: "user-1",
      cashierName: "Cashier",
      storeCode: "S1",
      deviceCode: "IPAD-1",
      permissionCodes: ["Permissions.PosTerminal.Sell"],
      ...overrides,
    },
  };
}

function hasCode(
  code: CurrentCashierSessionError["code"],
): (error: unknown) => boolean {
  return (error) =>
    error instanceof CurrentCashierSessionError && error.code === code;
}
