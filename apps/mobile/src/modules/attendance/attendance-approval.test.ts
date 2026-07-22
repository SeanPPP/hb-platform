import assert from "node:assert/strict";
import {
  buildAttendanceApprovalReviewRequest,
  getSupplementalAttendanceApprovalDetail,
  isKnownAttendanceApprovalSourceType,
  validateAttendanceOvertimeApproval,
} from "./attendance-approval";

assert.equal(isKnownAttendanceApprovalSourceType("Punch"), true);
assert.equal(isKnownAttendanceApprovalSourceType("Leave"), true);
assert.equal(isKnownAttendanceApprovalSourceType("PunchAdjustment"), true);
assert.equal(isKnownAttendanceApprovalSourceType("Overtime"), true);
assert.equal(isKnownAttendanceApprovalSourceType("MissingClockOut"), true);
assert.equal(isKnownAttendanceApprovalSourceType("FutureApproval"), false);

assert.equal(getSupplementalAttendanceApprovalDetail({
  sourceType: "Punch",
  displayedTitle: "Punch exception",
  detail: "ClockIn · Late · Traffic delay",
}), "ClockIn · Late · Traffic delay");
assert.equal(getSupplementalAttendanceApprovalDetail({
  sourceType: "Leave",
  displayedTitle: "Leave request",
  detail: "2026-07-23 – 2026-07-25 · Family care",
}), "2026-07-23 – 2026-07-25 · Family care");
assert.equal(getSupplementalAttendanceApprovalDetail({
  sourceType: "Punch",
  displayedTitle: "Punch exception",
  detail: " Punch exception ",
}), undefined);
assert.equal(getSupplementalAttendanceApprovalDetail({
  sourceType: "Overtime",
  displayedTitle: "Overtime approval",
  detail: "backend system detail",
}), undefined);

assert.deepEqual(
  buildAttendanceApprovalReviewRequest({
    approvalGuid: "approval-1",
    remark: "  Reduced to verified time  ",
    approvedOvertimeMinutes: 30,
  }),
  {
    reviewRemark: "Reduced to verified time",
    approvedOvertimeMinutes: 30,
  },
);
assert.deepEqual(
  buildAttendanceApprovalReviewRequest({
    approvalGuid: "approval-2",
    remark: "   ",
  }),
  { reviewRemark: undefined },
);
assert.deepEqual(
  buildAttendanceApprovalReviewRequest({
    approvalGuid: "approval-3",
    remark: "Rejected",
    approvedOvertimeMinutes: 0,
  }),
  { reviewRemark: "Rejected", approvedOvertimeMinutes: 0 },
);

assert.equal(
  validateAttendanceOvertimeApproval({
    candidateMinutes: 60,
    approvedMinutes: 60,
    action: "approve",
  }),
  null,
);
assert.equal(
  validateAttendanceOvertimeApproval({
    candidateMinutes: 60,
    approvedMinutes: 45,
    action: "approve",
  }),
  "remarkRequired",
);
assert.equal(
  validateAttendanceOvertimeApproval({
    candidateMinutes: 60,
    approvedMinutes: 30,
    action: "approve",
    remark: "Approved to the rostered limit",
  }),
  null,
);
assert.equal(
  validateAttendanceOvertimeApproval({
    candidateMinutes: 60,
    approvedMinutes: 20,
    action: "approve",
    remark: "Not supported",
  }),
  "invalidIncrement",
);
assert.equal(
  validateAttendanceOvertimeApproval({
    candidateMinutes: 60,
    approvedMinutes: 75,
    action: "approve",
    remark: "Invalid request",
  }),
  "outOfRange",
);
assert.equal(
  validateAttendanceOvertimeApproval({
    candidateMinutes: 60,
    approvedMinutes: 0,
    action: "reject",
  }),
  "remarkRequired",
);

console.log("attendance-approval.test.ts: ok");
