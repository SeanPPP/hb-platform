import assert from "node:assert/strict";
import {
  buildNonSensitiveProfilePayload,
  getChangedSensitiveFields,
  getSensitiveAccountSummary,
  getSensitiveStatusView,
  isSensitiveVersionConflict,
  refreshEmployeeProfileAfterIdentityMutation,
  selectSensitiveDraft,
  shouldRefreshSensitiveProfile,
} from "./sensitive-profile";
import type {
  EmployeeProfile,
  EmployeeProfileSensitiveChangeRequest,
  SensitiveEmployeeProfilePayload,
} from "./types";

const formal: EmployeeProfile = {
  username: "employee-a",
  phone: "0400000000",
  bankBsb: "123-456",
  bankAccountNumber: "111122223333",
  superannuationCompanyName: "Future Super",
  superannuationCompanyCode: "FS01",
  superannuationAccountNumber: "SUPER-0001",
  birthday: "1990-01-02",
  gender: "female",
  employmentType: "fullTime",
  avatarUrl: "https://cdn/avatar.jpg",
  identityType: "passport",
  identityId: "P1234567",
  identityPhotoUrl: "https://cdn/formal-identity.jpg",
  address: "1 Queen Street",
};

const pending: EmployeeProfileSensitiveChangeRequest = {
  requestId: 42,
  status: "Pending",
  bankBsb: "654-321",
  bankAccountNumber: "999988883333",
  superannuationCompanyName: "Future Super",
  superannuationCompanyCode: "FS01",
  superannuationAccountNumber: "SUPER-0002",
  identityType: "driverLicence",
  identityId: "DL7654321",
  hasIdentityPhoto: true,
  identityPhotoUrl: "https://cdn/pending-identity.jpg",
  baseSensitiveRevision: 3,
  submittedAt: "2026-07-16T00:00:00Z",
  changedFields: ["bankBsb", "bankAccountNumber", "identityPhotoUrl"],
};

assert.deepEqual(
  selectSensitiveDraft(formal, pending),
  {
    bankBsb: pending.bankBsb,
    bankAccountNumber: pending.bankAccountNumber,
    superannuationCompanyName: pending.superannuationCompanyName,
    superannuationCompanyCode: pending.superannuationCompanyCode,
    superannuationAccountNumber: pending.superannuationAccountNumber,
    identityType: pending.identityType,
    identityId: pending.identityId,
  },
  "Pending 申请存在时必须优先载入待审快照"
);

assert.deepEqual(
  selectSensitiveDraft(formal, { ...pending, status: "Rejected" }),
  {
    bankBsb: formal.bankBsb,
    bankAccountNumber: formal.bankAccountNumber,
    superannuationCompanyName: formal.superannuationCompanyName,
    superannuationCompanyCode: formal.superannuationCompanyCode,
    superannuationAccountNumber: formal.superannuationAccountNumber,
    identityType: formal.identityType,
    identityId: formal.identityId,
  },
  "非 Pending 状态重新填报时必须回退正式资料"
);

const sameLastFourDraft: SensitiveEmployeeProfilePayload = {
  ...selectSensitiveDraft(formal, null),
  bankAccountNumber: "777766663333",
};
assert.ok(
  getChangedSensitiveFields(formal, sameLastFourDraft).includes("bankAccountNumber"),
  "末四位相同但完整账号不同仍必须识别为变更"
);
assert.equal(
  getSensitiveAccountSummary(" 1111 2222-3333 "),
  "•••• 3333",
  "账号只能显示规范化后的末四位摘要"
);

const nonSensitivePayload = buildNonSensitiveProfilePayload({
  phone: " 0400000000 ",
  birthday: " 1990-01-02 ",
  gender: " female ",
  employmentType: " fullTime ",
  address: " 1 Queen Street ",
});
assert.deepEqual(nonSensitivePayload, {
  phone: "0400000000",
  birthday: "1990-01-02",
  gender: "female",
  employmentType: "fullTime",
  address: "1 Queen Street",
});
for (const sensitiveKey of [
  "bankBsb",
  "bankAccountNumber",
  "superannuationCompanyName",
  "superannuationCompanyCode",
  "superannuationAccountNumber",
  "identityType",
  "identityId",
  "identityPhotoUrl",
]) {
  assert.equal(
    sensitiveKey in nonSensitivePayload,
    false,
    `旧 PUT /me payload 不得包含敏感字段 ${sensitiveKey}`
  );
}

assert.deepEqual(
  getSensitiveStatusView({
    ...pending,
    status: "Rejected",
    reviewReason: "证件号码不清晰",
  }),
  {
    statusKey: "status.rejected",
    canRefill: true,
    reviewReason: "证件号码不清晰",
    submittedAt: pending.submittedAt,
    changedFields: pending.changedFields,
  },
  "Rejected 状态必须保留拒绝原因、提交时间和变更字段，并允许重新填报"
);
assert.equal(
  getSensitiveStatusView({ ...pending, status: "Approved" }).statusKey,
  "status.approved",
  "Approved 状态文案键必须准确"
);
assert.equal(
  getSensitiveStatusView({ ...pending, status: "Superseded" }).statusKey,
  "status.superseded",
  "Superseded 状态文案键必须准确"
);

assert.equal(shouldRefreshSensitiveProfile("focus", true, "active"), true);
assert.equal(shouldRefreshSensitiveProfile("manual", true, "background"), true);
assert.equal(shouldRefreshSensitiveProfile("app-active", true, "active"), true);
assert.equal(shouldRefreshSensitiveProfile("app-active", true, "background"), false);
assert.equal(shouldRefreshSensitiveProfile("focus", false, "active"), false);
assert.equal(
  isSensitiveVersionConflict({ response: { status: 409 } }),
  true,
  "HTTP 409 必须提示重新填报"
);
assert.equal(
  isSensitiveVersionConflict(new Error("EMPLOYEE_PROFILE_SENSITIVE_VERSION_CONFLICT")),
  true,
  "业务 envelope 的版本冲突码也必须被兼容"
);

async function main() {
  const refreshOrder: string[] = [];
  await refreshEmployeeProfileAfterIdentityMutation({
    refetchSensitive: async () => {
      refreshOrder.push("sensitive");
    },
    refetchFormal: async () => {
      refreshOrder.push("formal");
    },
  });
  assert.deepEqual(
    refreshOrder,
    ["sensitive", "formal"],
    "证件照完成或删除后必须先刷新待审快照，再刷新正式资料"
  );

  console.log("sensitive-profile.test.ts: ok");
}

void main();
