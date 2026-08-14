import assert from "node:assert/strict";
import test from "node:test";

import {
  CashierLoginController,
  CashierLoginError,
  type CashierLoginRuntime,
} from "./cashier-login-controller";
import { useCashierLoginStore } from "./cashier-login-store";

import type { PosCashierSummary } from "@/core/runtime/production-pos-service-composition";

function cashier(
  overrides: Partial<PosCashierSummary> = {},
): PosCashierSummary {
  return {
    cashierId: "cashier-1",
    userGuid: "user-1",
    cashierName: "Cashier One",
    deviceCode: "IPAD-01",
    permissions: ["Permissions.PosTerminal.Sell"],
    source: "online",
    storeCode: "S1",
    ...overrides,
  };
}

function runtime(input: Readonly<{
  signIn: (barcode: string) => Promise<PosCashierSummary>;
  phase?: CashierLoginRuntime["state"]["phase"];
  device?: CashierLoginRuntime["state"]["device"];
}>): CashierLoginRuntime {
  return {
    state: {
      phase: input.phase ?? "ready",
      device: input.device ?? "authorized-online",
    },
    services: {
      cashierSession: { signIn: input.signIn },
    },
  };
}

test.afterEach(() => useCashierLoginStore.getState().clearActiveCashier());

test("登录控制器只提交条码，并只把公开收银员投影写入 Zustand", async () => {
  const calls: string[] = [];
  const unsafeRuntimeResult = {
    ...cashier(),
    emergencyGrant: "emergency-grant-secret",
    expiry: "2099-01-01T00:00:00.000Z",
    session: { authorizationToken: "cashier-authorization-secret" },
    token: "cashier-token-secret",
  } as unknown as PosCashierSummary;
  const controller = new CashierLoginController(useCashierLoginStore.getState());
  const result = await controller.login("  123456  ", runtime({
    signIn: async (barcode) => {
      calls.push(barcode);
      return unsafeRuntimeResult;
    },
  }));

  const expected = cashier();
  assert.deepEqual(calls, ["123456"]);
  assert.deepEqual(result.cashier, expected);
  assert.deepEqual(useCashierLoginStore.getState().activeCashier, expected);
  assert.deepEqual(
    JSON.parse(JSON.stringify(useCashierLoginStore.getState())),
    { activeCashier: expected },
  );
  assert.doesNotMatch(
    JSON.stringify(result),
    /session|token|expiry|emergencyGrant/i,
  );
});

test("公开投影保留旧合同可空的 userGuid", async () => {
  const controller = new CashierLoginController(useCashierLoginStore.getState());
  const result = await controller.login("legacy-cashier", runtime({
    signIn: async () => cashier({ userGuid: null }),
  }));

  assert.equal(result.cashier.userGuid, null);
});

test("登录服务拒绝时不留下活动身份", async () => {
  const controller = new CashierLoginController(useCashierLoginStore.getState());
  await controller.login("cached-1", runtime({
    signIn: async () => cashier({ source: "offline-cache" }),
  }));

  await assert.rejects(
    () => controller.login("rejected", runtime({
      signIn: async () => {
        throw new Error("explicit online rejection");
      },
    })),
    /explicit online rejection/,
  );
  assert.equal(useCashierLoginStore.getState().activeCashier, null);
});

test("锁定、未就绪和空条码都在调用组合根前 fail-closed", async () => {
  const controller = new CashierLoginController(useCashierLoginStore.getState());
  let calls = 0;
  const signIn = async () => {
    calls += 1;
    return cashier();
  };

  await assert.rejects(
    () => controller.login("1", runtime({ signIn, phase: "locked", device: "locked" })),
    cashierError("DEVICE_LOCKED"),
  );
  await assert.rejects(
    () => controller.login("1", runtime({ signIn, phase: "starting" })),
    cashierError("RUNTIME_NOT_READY"),
  );
  await assert.rejects(
    () => controller.login("   ", runtime({ signIn })),
    cashierError("BARCODE_REQUIRED"),
  );
  assert.equal(calls, 0);
  assert.equal(useCashierLoginStore.getState().activeCashier, null);
});

function cashierError(
  code: CashierLoginError["code"],
): (error: unknown) => boolean {
  return (error) => error instanceof CashierLoginError && error.code === code;
}
