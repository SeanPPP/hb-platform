import { apiClient } from "@/shared/api/client";
import type { EmployeeProfile, UpdateEmployeeProfilePayload } from "@/modules/employee-profile/types";

type ApiRecord = Record<string, unknown>;

function asString(value: unknown): string {
  return typeof value === "string" ? value.trim() : "";
}

function normalizeEmployeeProfile(payload: unknown): EmployeeProfile {
  const data = (payload && typeof payload === "object" ? payload : {}) as ApiRecord;

  return {
    username: asString(data.username ?? data.userName ?? data.UserName),
    displayName: asString(data.displayName ?? data.DisplayName ?? data.fullName ?? data.FullName) || undefined,
    bankBsb: asString(data.bankBsb ?? data.BankBsb),
    bankAccountNumber: asString(data.bankAccountNumber ?? data.BankAccountNumber),
    superannuationCompanyName: asString(
      data.superannuationCompanyName ?? data.SuperannuationCompanyName
    ),
    superannuationCompanyCode: asString(
      data.superannuationCompanyCode ?? data.SuperannuationCompanyCode
    ),
    superannuationAccountNumber: asString(
      data.superannuationAccountNumber ?? data.SuperannuationAccountNumber
    ),
    birthday: asString(data.birthday ?? data.Birthday),
    gender: asString(data.gender ?? data.Gender),
    employmentType: asString(data.employmentType ?? data.EmploymentType),
    avatarUrl: asString(data.avatarUrl ?? data.AvatarUrl),
    identityId: asString(data.identityId ?? data.IdentityId),
    identityPhotoUrl: asString(data.identityPhotoUrl ?? data.IdentityPhotoUrl),
    address: asString(data.address ?? data.Address),
    createdAt: asString(data.createdAt ?? data.CreatedAt) || undefined,
    updatedAt: asString(data.updatedAt ?? data.UpdatedAt) || undefined,
  };
}

export async function getMyEmployeeProfileApi(): Promise<EmployeeProfile> {
  const response = await apiClient.get("/EmployeeProfiles/me");
  return normalizeEmployeeProfile(response.data);
}

export async function updateMyEmployeeProfileApi(
  payload: UpdateEmployeeProfilePayload
): Promise<EmployeeProfile> {
  const response = await apiClient.put("/EmployeeProfiles/me", payload);
  return normalizeEmployeeProfile(response.data);
}
