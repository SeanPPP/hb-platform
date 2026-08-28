import assert from "node:assert/strict";
import test from "node:test";

import {
  auditActorPayload,
  auditActorSnapshotFromPayload,
} from "./audit-actor";

test("员工操作审计把 actor 三字段作为同一快照写入和读取", () => {
  const payload = auditActorPayload({
    cashierId: " CASHIER-1 ",
    cashierName: " Alice ",
    userGuid: null,
  });

  assert.deepEqual(payload, {
    requestingCashierId: "CASHIER-1",
    requestingCashierName: "Alice",
    requestingUserGuid: null,
  });
  assert.deepEqual(auditActorSnapshotFromPayload(payload), {
    cashierId: "CASHIER-1",
    cashierName: "Alice",
    userGuid: null,
  });
});

test("缺少任一 actor 字段的历史载荷不得被当作完整快照", () => {
  assert.equal(
    auditActorSnapshotFromPayload({
      requestingCashierId: "CASHIER-1",
      requestingCashierName: "Alice",
    }),
    null,
  );
});
