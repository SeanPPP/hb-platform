import assert from "node:assert/strict";
import test from "node:test";

import { resolveActivationHardwareId } from "./device-activation-intent";

test("redeem 使用本机 installation hardwareId", () => {
  assert.equal(
    resolveActivationHardwareId("redeem", "installation-hardware", null),
    "installation-hardware",
  );
});

test("rebind 固定沿用旧 binding hardwareId，避免本地与服务端漂移", () => {
  assert.equal(
    resolveActivationHardwareId("rebind", "installation-hardware", {
      hardwareId: "bound-hardware",
    }),
    "bound-hardware",
  );
  assert.throws(
    () => resolveActivationHardwareId("rebind", "installation-hardware", null),
    /DEVICE_ACCOUNT_REBIND_REQUIRES_CURRENT_BINDING/,
  );
});
