import assert from "node:assert/strict";
import test from "node:test";

import type {
  CustomerDisplaySnapshot,
  DisplayStatus,
  ExternalCustomerDisplayPort,
} from "../../../contracts/external-display";

import {
  createExternalDisplayBridge,
  sanitizeCustomerDisplaySnapshot,
  type ExternalDisplayNativeModule,
  type NativeExternalDisplayStatusEvent,
  type NativeExternalDisplayStatus,
} from "./external-display-bridge";

const disconnectedStatus: NativeExternalDisplayStatus = {
  state: "disconnected",
  enabled: false,
  connected: false,
  revision: 0,
  widthPixels: 0,
  heightPixels: 0,
  scale: 0,
  reason: "native-module-unavailable",
};

const money = (cents: number) => ({ currency: "AUD" as const, cents });
const advertisementCacheRootUri =
  "file:///var/mobile/Containers/Data/customer-display-ads/";

const snapshot: CustomerDisplaySnapshot = {
  revision: 1,
  mode: "cart",
  items: [
    {
      name: "Sparkling Water",
      quantity: "2",
      amount: money(598),
    },
  ],
  gst: money(54),
  discount: money(100),
  total: money(498),
  change: money(0),
  advert: {
    kind: "image",
    localUri:
      "file:///var/mobile/Containers/Data/customer-display-ads/ad.png",
  },
};

test("adapter implements the frozen external display port", () => {
  const port: ExternalCustomerDisplayPort = createExternalDisplayBridge({
    advertisementCacheRootUri,
    nativeModule: null,
  });

  assert.equal(typeof port.publish, "function");
});

test("simulator or missing native module stays explicitly disconnected", async () => {
  const bridge = createExternalDisplayBridge({
    advertisementCacheRootUri,
    nativeModule: null,
  });

  assert.equal(await bridge.getStatus(), "disconnected");
  assert.equal(await bridge.setEnabled(true), undefined);
  assert.equal(await bridge.forceBlank(), undefined);
  assert.equal(await bridge.disableForSafety(), undefined);
  await assert.rejects(
    () => bridge.publish(snapshot),
    /native module is unavailable/i,
  );
});

test("安全清屏绕过 snapshot 发布并拒绝过期 producer 的伪成功结果", async () => {
  let forceBlankCalls = 0;
  let responseReason = "sensitive-content-reset";
  const nativeModule: ExternalDisplayNativeModule = {
    async getStatus() {
      return disconnectedStatus;
    },
    async setEnabled() {
      return disconnectedStatus;
    },
    async forceBlank() {
      forceBlankCalls += 1;
      return {
        ...disconnectedStatus,
        reason: responseReason,
      };
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
  const bridge = createExternalDisplayBridge({
    advertisementCacheRootUri,
    nativeModule,
  });

  await bridge.forceBlank();
  assert.equal(forceBlankCalls, 1);

  responseReason = "producer-session-expired";
  await assert.rejects(
    () => bridge.forceBlank(),
    /safe blank was rejected.*producer-session-expired/i,
  );
});

test("旧原生安全隐藏会传播 setEnabled 异常并拒绝过期 producer 的伪成功状态", async () => {
  let behavior: "throw" | "expired" | "disabled" = "throw";
  const setEnabledArguments: boolean[] = [];
  const nativeModule: ExternalDisplayNativeModule = {
    async getStatus() {
      return disconnectedStatus;
    },
    async setEnabled(enabled) {
      setEnabledArguments.push(enabled);
      if (behavior === "throw") {
        throw new Error("legacy native disable failed");
      }
      if (behavior === "disabled") {
        return {
          ...disconnectedStatus,
          reason: "external-display-disabled",
        };
      }
      return {
        ...disconnectedStatus,
        enabled: true,
        reason: "producer-session-expired",
      };
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
  const bridge = createExternalDisplayBridge({
    advertisementCacheRootUri,
    nativeModule,
  });

  await assert.rejects(
    async () => bridge.disableForSafety(),
    /legacy native disable failed/,
  );

  behavior = "expired";
  await assert.rejects(
    async () => bridge.disableForSafety(),
    (error: unknown) => {
      assert.equal(
        (error as { code?: string }).code,
        "EXTERNAL_DISPLAY_SAFE_DISABLE_REJECTED",
      );
      assert.match(
        (error as Error).message,
        /safe disable was rejected.*producer-session-expired/i,
      );
      return true;
    },
  );

  behavior = "disabled";
  await bridge.disableForSafety();
  assert.deepEqual(setEnabledArguments, [false, false, false]);
});

test("native revision gate can reject an older concurrent delivery", async () => {
  let latestRevision = 0;
  const deliveredRevisions: number[] = [];
  const nativeModule: ExternalDisplayNativeModule = {
    async getStatus() {
      return disconnectedStatus;
    },
    async setEnabled() {
      return disconnectedStatus;
    },
    async publishSnapshot(value) {
      if (value.revision === 1) {
        await new Promise((resolve) => setTimeout(resolve, 20));
      }

      deliveredRevisions.push(value.revision);
      const accepted = value.revision > latestRevision;
      if (accepted) {
        latestRevision = value.revision;
      }

      return {
        accepted,
        revision: value.revision,
        latestRevision,
        reason: accepted ? "accepted" : "stale-revision",
      };
    },
    async markReactSurfaceReady() {},
    async markReactSurfaceRendered() {},
    addListener() {
      return { remove() {} };
    },
  };
  const bridge = createExternalDisplayBridge({
    advertisementCacheRootUri,
    nativeModule,
  });

  const results = await Promise.allSettled([
    bridge.publish(snapshot),
    bridge.publish({ ...snapshot, revision: 2, total: money(698) }),
  ]);

  assert.deepEqual(deliveredRevisions, [2, 1]);
  assert.equal(latestRevision, 2);
  assert.equal(results[0]?.status, "rejected");
  assert.equal(results[1]?.status, "fulfilled");
});

test("status subscription exposes only the frozen status values", () => {
  let nativeListener:
    | ((event: NativeExternalDisplayStatusEvent) => void)
    | undefined;
  let removed = false;
  const nativeModule: ExternalDisplayNativeModule = {
    async getStatus() {
      return disconnectedStatus;
    },
    async setEnabled() {
      return disconnectedStatus;
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
    addListener(eventName, listener) {
      if (eventName === "onStatusChanged") {
        nativeListener = listener as (
          event: NativeExternalDisplayStatusEvent,
        ) => void;
      }
      return {
        remove() {
          removed = true;
        },
      };
    },
  };
  const statuses: DisplayStatus[] = [];
  const bridge = createExternalDisplayBridge({
    advertisementCacheRootUri,
    nativeModule,
  });
  const unsubscribe = bridge.subscribe((status) => statuses.push(status));

  nativeListener?.({
    ...disconnectedStatus,
    event: "ready",
    state: "ready",
    connected: true,
  });
  unsubscribe();

  assert.deepEqual(statuses, ["ready"]);
  assert.equal(removed, true);
});

test("strict snapshot schema rejects credentials, customer data and card references", () => {
  assert.throws(() =>
    sanitizeCustomerDisplaySnapshot(
      {
        ...snapshot,
        deviceAuthorization: "secret",
      },
      advertisementCacheRootUri,
    ),
  );
  assert.throws(() =>
    sanitizeCustomerDisplaySnapshot(
      {
        ...snapshot,
        items: [
          {
            ...snapshot.items[0]!,
            cardReference: "card-ref",
          },
        ],
      },
      advertisementCacheRootUri,
    ),
  );
  assert.throws(() =>
    sanitizeCustomerDisplaySnapshot(
      {
        ...snapshot,
        customerName: "Private Customer",
      },
      advertisementCacheRootUri,
    ),
  );
});

test("advertisements must be local files", () => {
  assert.throws(
    () =>
      sanitizeCustomerDisplaySnapshot(
        {
          ...snapshot,
          advert: {
            kind: "image",
            localUri: "https://example.com/ad.png",
          },
        },
        advertisementCacheRootUri,
      ),
    /local advertisement URI/,
  );
});
