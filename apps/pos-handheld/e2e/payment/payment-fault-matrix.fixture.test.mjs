import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const matrix = JSON.parse(
  readFileSync(
    new URL("../../test-fixtures/wpf-parity/payment/payment-fault-matrix.json", import.meta.url),
    "utf8",
  ),
);

test("payment fault matrix keeps safety-critical automated coverage and hardware boundaries explicit", () => {
  const byId = new Map(matrix.cases.map((entry) => [entry.id, entry]));
  for (const id of ["PAY-01", "PAY-02", "PAY-03", "PAY-04", "PAY-05", "RET-01", "RET-02"]) {
    assert.notEqual(byId.get(id)?.automation, "manual", `${id} must have an automated or contract seam`);
  }
  assert.equal(byId.get("PAY-06")?.automation, "pending-m12");
  for (const id of ["HW-01", "HW-02"]) {
    assert.equal(byId.get(id)?.automation, "manual");
  }
});
