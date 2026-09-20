import assert from "node:assert/strict";
import test from "node:test";
import {
  hasStoredDeviceSession,
  isOfflineProductQueryEligible,
} from "./offline-eligibility";

test("仅设备类会话且本地有设备会话时才允许离线", () => {
  assert.equal(
    isOfflineProductQueryEligible({ sessionKind: "device", hasStoredDeviceSession: true }),
    true,
  );
  assert.equal(
    isOfflineProductQueryEligible({ sessionKind: "deviceAccount", hasStoredDeviceSession: true }),
    true,
  );
});

test("普通账号、审核态或缺少设备会话时拒绝离线", () => {
  assert.equal(
    isOfflineProductQueryEligible({ sessionKind: "account", hasStoredDeviceSession: true }),
    false,
  );
  assert.equal(
    isOfflineProductQueryEligible({ sessionKind: "iosReview", hasStoredDeviceSession: true }),
    false,
  );
  assert.equal(
    isOfflineProductQueryEligible({ sessionKind: "device", hasStoredDeviceSession: false }),
    false,
  );
  assert.equal(
    isOfflineProductQueryEligible({ sessionKind: null, hasStoredDeviceSession: true }),
    false,
  );
});

test("设备会话必须同时具备 hardwareId 与 authCode", () => {
  assert.equal(hasStoredDeviceSession({ hardwareId: "hw", authCode: "code" }), true);
  assert.equal(hasStoredDeviceSession({ hardwareId: "hw", authCode: "" }), false);
  assert.equal(hasStoredDeviceSession({ hardwareId: null, authCode: "code" }), false);
  assert.equal(hasStoredDeviceSession(null), false);
});
