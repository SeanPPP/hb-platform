import assert from "node:assert/strict";
import {
  getCashierBarcodeQueryKey,
  getEmployeeProfileQueryKey,
  resolveEmployeeProfileIdentity,
  shouldResetEmployeeProfileDraft,
} from "./cache-keys";

const userA = { userGuid: "USER-A", userGUID: "USER-A", username: "admin-a" };
const userB = { userGuid: "USER-B", userGUID: "USER-B", username: "admin-b" };

assert.equal(resolveEmployeeProfileIdentity(userA), "user-a", "优先使用稳定用户 GUID 并归一化");
assert.notDeepEqual(
  getEmployeeProfileQueryKey(resolveEmployeeProfileIdentity(userA)),
  getEmployeeProfileQueryKey(resolveEmployeeProfileIdentity(userB)),
  "不同账号的员工资料查询键必须隔离"
);
assert.notDeepEqual(
  getCashierBarcodeQueryKey(resolveEmployeeProfileIdentity(userA)),
  getCashierBarcodeQueryKey(resolveEmployeeProfileIdentity(userB)),
  "不同账号的收银条码查询键必须隔离"
);
assert.equal(shouldResetEmployeeProfileDraft("user-a", "user-b"), true, "账号变化必须重置草稿初始化状态");
assert.equal(shouldResetEmployeeProfileDraft("user-a", "user-a"), false, "同一账号刷新图片不得重置草稿");

console.log("cache-keys.test.ts: ok");
