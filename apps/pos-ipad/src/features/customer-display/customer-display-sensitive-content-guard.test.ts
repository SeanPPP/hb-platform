import assert from "node:assert/strict";
import test from "node:test";

import { CustomerDisplaySensitiveContentGuard } from "./customer-display-sensitive-content-guard";

import type {
  CustomerDisplaySnapshot,
  DisplayStatus,
  ExternalCustomerDisplayPort,
} from "@/core/contracts";
import {
  createExternalDisplayBridge,
  type ExternalDisplayNativeModule,
  type NativeExternalDisplayStatus,
} from "@/core/peripherals/customer-display/native/external-display-bridge";

test("安全清屏在第三次发布成功时不触发原生兜底", async () => {
  let publishCalls = 0;
  let forceBlankCalls = 0;
  let disableCalls = 0;
  const display = fakeDisplay({
    onDisable: () => {
      disableCalls += 1;
    },
  });
  const capableDisplay = Object.assign(display, {
    async forceBlank() {
      forceBlankCalls += 1;
    },
  });
  const guard = new CustomerDisplaySensitiveContentGuard(
    {
      async clearSensitiveContent() {
        publishCalls += 1;
        return publishCalls < 3
          ? failedResult(publishCalls)
          : { status: "published", revision: publishCalls } as const;
      },
    },
    capableDisplay,
  );

  assert.deepEqual(await guard.clearSensitiveContent(), {
    status: "published",
    revision: 3,
  });
  assert.equal(publishCalls, 3);
  assert.equal(forceBlankCalls, 0);
  assert.equal(disableCalls, 0);
});

test("安全清屏发布连续失败后调用窄原生 forceBlank 能力", async () => {
  let publishCalls = 0;
  let forceBlankCalls = 0;
  let disableCalls = 0;
  const display = Object.assign(
    fakeDisplay({
      onDisable: () => {
        disableCalls += 1;
      },
    }),
    {
      async forceBlank() {
        forceBlankCalls += 1;
      },
    },
  );
  const guard = new CustomerDisplaySensitiveContentGuard(
    {
      async clearSensitiveContent() {
        publishCalls += 1;
        return failedResult(publishCalls);
      },
    },
    display,
  );

  assert.deepEqual(await guard.clearSensitiveContent(), failedResult(3));
  assert.equal(publishCalls, 3);
  assert.equal(forceBlankCalls, 1);
  assert.equal(disableCalls, 0);
});

test("原生 forceBlank 缺失、拒绝或发布挂起时最终隐藏外屏且不抛错", async () => {
  for (const nativeCapability of ["missing", "failed"] as const) {
    let publishCalls = 0;
    let disableCalls = 0;
    const baseDisplay = fakeDisplay({
      onDisable: () => {
        disableCalls += 1;
      },
    });
    const display =
      nativeCapability === "failed"
        ? Object.assign(baseDisplay, {
            async forceBlank() {
              throw new Error("native blank failed");
            },
          })
        : baseDisplay;
    const guard = new CustomerDisplaySensitiveContentGuard(
      {
        clearSensitiveContent() {
          publishCalls += 1;
          return new Promise(() => undefined);
        },
      },
      display,
      { operationTimeoutMs: 5, publishAttempts: 2 },
    );

    assert.deepEqual(await guard.clearSensitiveContent(), failedResult(0));
    assert.equal(publishCalls, 2);
    assert.equal(disableCalls, 1);
  }
});

test("专用安全隐藏失败会传播，guard 不得把最终失败当作成功", async () => {
  let forceBlankCalls = 0;
  let safetyDisableCalls = 0;
  let ordinaryDisableCalls = 0;
  const display = Object.assign(
    fakeDisplay({
      onDisable: () => {
        ordinaryDisableCalls += 1;
      },
    }),
    {
      async forceBlank() {
        forceBlankCalls += 1;
        throw new Error("native blank failed");
      },
      async disableForSafety() {
        safetyDisableCalls += 1;
        throw new Error("verified native disable failed");
      },
    },
  );
  const guard = new CustomerDisplaySensitiveContentGuard(
    {
      async clearSensitiveContent() {
        return failedResult(1);
      },
    },
    display,
    { publishAttempts: 1 },
  );

  await assert.rejects(
    () => guard.clearSensitiveContent(),
    /verified native disable failed/,
  );
  assert.equal(forceBlankCalls, 1);
  assert.equal(safetyDisableCalls, 1);
  assert.equal(ordinaryDisableCalls, 0);
});

test("普通兼容隐藏抛错时也会传播，guard 不得静默成功", async () => {
  const display: ExternalCustomerDisplayPort = {
    async getStatus() {
      return "ready";
    },
    async setEnabled(enabled) {
      assert.equal(enabled, false);
      throw new Error("legacy display disable failed");
    },
    async publish() {},
    subscribe() {
      return () => undefined;
    },
  };
  const guard = new CustomerDisplaySensitiveContentGuard(
    {
      async clearSensitiveContent() {
        return failedResult(1);
      },
    },
    display,
    { publishAttempts: 1 },
  );

  await assert.rejects(
    () => guard.clearSensitiveContent(),
    /legacy display disable failed/,
  );
});

test("真实 bridge 的旧原生拒绝隐藏时 guard 最终拒绝", async () => {
  const nativeStatus: NativeExternalDisplayStatus = {
    state: "ready",
    enabled: true,
    connected: true,
    revision: 12,
    widthPixels: 1920,
    heightPixels: 1080,
    scale: 1,
    reason: "producer-session-expired",
  };
  const setEnabledArguments: boolean[] = [];
  const nativeModule: ExternalDisplayNativeModule = {
    async getStatus() {
      return nativeStatus;
    },
    async setEnabled(enabled) {
      setEnabledArguments.push(enabled);
      return nativeStatus;
    },
    async publishSnapshot(value) {
      return {
        accepted: true,
        revision: value.revision,
        latestRevision: value.revision,
        reason: "accepted",
      };
    },
    async markReactSurfaceReady() {},
    async markReactSurfaceRendered() {},
    addListener() {
      return { remove() {} };
    },
  };
  const display = createExternalDisplayBridge({
    nativeModule,
  });
  const guard = new CustomerDisplaySensitiveContentGuard(
    {
      async clearSensitiveContent() {
        return failedResult(1);
      },
    },
    display,
    { publishAttempts: 1 },
  );

  await assert.rejects(
    () => guard.clearSensitiveContent(),
    /safe disable was rejected.*producer-session-expired/i,
  );
  assert.deepEqual(setEnabledArguments, [false]);
});

function failedResult(revision: number) {
  return {
    status: "failed",
    revision,
    errorCode: "DISPLAY_PUBLISH_FAILED",
  } as const;
}

function fakeDisplay(input: Readonly<{
  onDisable(): void;
}>): ExternalCustomerDisplayPort {
  return {
    async getStatus(): Promise<DisplayStatus> {
      return "ready";
    },
    async setEnabled(enabled: boolean): Promise<void> {
      if (!enabled) input.onDisable();
    },
    async publish(_snapshot: CustomerDisplaySnapshot): Promise<void> {},
    subscribe(): () => void {
      return () => undefined;
    },
  };
}
