import assert from "node:assert/strict";
import { resolveQrDisplayValue } from "./qr-display";

function run() {
  assert.equal(
    resolveQrDisplayValue(" ORD-001 ", "guid-1"),
    "ORD-001",
    "primary business number should win after trimming",
  );

  assert.equal(
    resolveQrDisplayValue("   ", " guid-2 "),
    "guid-2",
    "fallback should be used when primary is blank",
  );

  assert.equal(
    resolveQrDisplayValue(undefined, "voucher-3"),
    "voucher-3",
    "fallback should be used when primary is missing",
  );

  assert.equal(
    resolveQrDisplayValue("", "   "),
    "",
    "blank primary and fallback should resolve to empty string",
  );

  console.log("qr-display.test.ts: ok");
}

run();
