import assert from "node:assert/strict";
import test from "node:test";

import {
  mapDeviceSessionToRuntime,
  reconcileDeviceSessionRuntime,
} from "./device-registration-state";

test("device registration maps approval, pending and explicit denial without inventing authorization", () => {
  assert.deepEqual(
    mapDeviceSessionToRuntime({ status: "authorized", deviceCode: "IPAD-1", storeCode: "S1" }),
    { backend: "reachable", device: "authorized-online" },
  );
  assert.deepEqual(
    mapDeviceSessionToRuntime({ status: "pending-approval", deviceCode: "IPAD-2", storeCode: "S1" }),
    { backend: "reachable", device: "pending-approval" },
  );
  assert.deepEqual(
    mapDeviceSessionToRuntime({ status: "disabled", message: "Hardware mismatch" }),
    { backend: "rejected", device: "locked" },
  );
  assert.deepEqual(
    mapDeviceSessionToRuntime({ status: "denied", message: "Store denied" }),
    { backend: "rejected", device: "registration-required" },
  );
});

test("设备批准后重建组合根，待审批和拒绝仅更新运行门禁", async () => {
  const updates: unknown[] = [];
  let retries = 0;
  const runtime = {
    updateOperationalState(input: unknown) {
      updates.push(input);
    },
    async retry() {
      retries += 1;
    },
  };

  await reconcileDeviceSessionRuntime(
    { status: "authorized", deviceCode: "IPAD-1", storeCode: "S1" },
    runtime,
  );
  assert.equal(retries, 1);
  assert.deepEqual(updates, []);

  await reconcileDeviceSessionRuntime(
    { status: "pending-approval", deviceCode: "IPAD-2", storeCode: "S1" },
    runtime,
  );
  assert.equal(retries, 1);
  assert.deepEqual(updates, [
    { backend: "reachable", device: "pending-approval" },
  ]);
});
