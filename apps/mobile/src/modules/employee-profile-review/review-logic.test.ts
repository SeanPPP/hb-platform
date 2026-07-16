import assert from "node:assert/strict";
import {
  getReviewFailureKind,
  isRejectReasonValid,
  maskSensitiveValue,
  shouldDisableReviewActions,
} from "./review-logic";

assert.equal(maskSensitiveValue("123456789"), "••••6789");
assert.equal(maskSensitiveValue("123"), "••••123");
assert.equal(maskSensitiveValue(""), "--");
assert.equal(isRejectReasonValid("  wrong account  "), true);
assert.equal(isRejectReasonValid("  "), false);
assert.equal(shouldDisableReviewActions("Pending", false), false);
assert.equal(shouldDisableReviewActions("Approved", false), true);
assert.equal(shouldDisableReviewActions("Pending", true), true);

assert.equal(
  getReviewFailureKind({ response: { status: 409, data: { errorCode: "EMPLOYEE_PROFILE_SENSITIVE_VERSION_CONFLICT" } } }),
  "conflict"
);
assert.equal(
  getReviewFailureKind({ response: { status: 409, data: { code: "REQUEST_NOT_PENDING" } } }),
  "conflict"
);
assert.equal(getReviewFailureKind({ response: { status: 403 } }), "forbidden");
assert.equal(getReviewFailureKind(new Error("network")), "other");
