import type { EmployeeProfile, UpdateEmployeeProfilePayload } from "./types";

export function toEmployeeProfileDraft(profile: EmployeeProfile): UpdateEmployeeProfilePayload {
  return {
    bankBsb: profile.bankBsb,
    bankAccountNumber: profile.bankAccountNumber,
    superannuationCompanyName: profile.superannuationCompanyName,
    superannuationCompanyCode: profile.superannuationCompanyCode,
    superannuationAccountNumber: profile.superannuationAccountNumber,
    birthday: profile.birthday,
    gender: profile.gender,
    employmentType: profile.employmentType,
    identityId: profile.identityId,
    address: profile.address,
  };
}

export function syncEmployeeProfileDraft(
  currentDraft: UpdateEmployeeProfilePayload,
  profile: EmployeeProfile,
  hasInitialized: boolean
) {
  // 图片即时保存会刷新资料缓存，已编辑的银行和地址草稿不能被缓存更新覆盖。
  return hasInitialized ? currentDraft : toEmployeeProfileDraft(profile);
}
