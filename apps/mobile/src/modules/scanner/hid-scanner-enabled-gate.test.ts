import assert from "node:assert/strict";
import { createHidScannerEnabledGate } from "./hid-scanner-enabled-gate";

const gate = createHidScannerEnabledGate(true);
const pendingIdleSubmit = gate.captureSubmission();
assert.equal(pendingIdleSubmit(), true, "enabled scanner accepts the current fallback submission");

gate.setEnabled(false);
assert.equal(gate.isEnabled(), false, "disabled scanner rejects direct submit");
assert.equal(pendingIdleSubmit(), false, "disabling invalidates an already scheduled idle submit");
assert.equal(gate.captureSubmission()(), false, "disabled scanner cannot schedule a fallback submit");

gate.setEnabled(true);
assert.equal(
  pendingIdleSubmit(),
  false,
  "re-enabling does not revive text cached before the disabled interval"
);
assert.equal(gate.captureSubmission()(), true, "new input after re-enable may submit");

console.log("hid-scanner-enabled-gate.test.ts: ok");
