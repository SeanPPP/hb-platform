import assert from "node:assert/strict";
import test from "node:test";

import { resolveAttendanceAuditRuntimeFactory } from "./attendance-audit-runtime";

test("只接受 services.attendanceAudit 的零参数 presenter 工厂", () => {
  const createPresenter = () => ({});
  assert.deepEqual(
    resolveAttendanceAuditRuntimeFactory({
      attendanceAudit: { createPresenter },
    }),
    { createPresenter },
  );
  assert.equal(resolveAttendanceAuditRuntimeFactory({}), null);
  assert.equal(
    resolveAttendanceAuditRuntimeFactory({
      attendanceAudit: { createPresenter: "unsafe" },
    }),
    null,
  );
});
