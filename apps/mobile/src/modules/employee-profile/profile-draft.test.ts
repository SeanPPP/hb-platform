import assert from "node:assert/strict";
import { syncEmployeeProfileDraft } from "./profile-draft";

const profile = {
  username: "admin",
  bankBsb: "server-bsb",
  bankAccountNumber: "server-account",
  superannuationCompanyName: "",
  superannuationCompanyCode: "",
  superannuationAccountNumber: "",
  birthday: "",
  gender: "",
  employmentType: "",
  avatarUrl: "https://cdn/new-avatar.jpg",
  identityId: "",
  identityPhotoUrl: "",
  address: "server-address",
};
const draft = {
  bankBsb: "draft-bsb",
  bankAccountNumber: "draft-account",
  superannuationCompanyName: "",
  superannuationCompanyCode: "",
  superannuationAccountNumber: "",
  birthday: "",
  gender: "",
  employmentType: "",
  identityId: "",
  address: "draft-address",
};

assert.deepEqual(
  syncEmployeeProfileDraft(draft, profile, true),
  draft,
  "图片即时保存刷新资料缓存时必须保留未保存表单草稿"
);
assert.equal(
  syncEmployeeProfileDraft(draft, profile, false).bankBsb,
  "server-bsb",
  "首次加载资料时应初始化表单"
);
assert.equal(
  syncEmployeeProfileDraft(draft, profile, false).address,
  "server-address",
  "切换账号后必须使用新账号资料重新初始化草稿"
);

console.log("profile-draft.test.ts: ok");
