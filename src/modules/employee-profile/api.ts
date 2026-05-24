import { apiClient } from "@/shared/api/client";
import type {
  DirectUploadRequest,
  DirectUploadSignature,
  EmployeeProfile,
  EmployeeProfileImageKind,
  UpdateEmployeeProfilePayload,
} from "@/modules/employee-profile/types";

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

function normalizeDirectUploadSignature(payload: unknown): DirectUploadSignature {
  const data = (payload && typeof payload === "object" ? payload : {}) as ApiRecord;
  const headersValue = data.headers ?? data.Headers;
  const headers =
    headersValue && typeof headersValue === "object"
      ? Object.fromEntries(
          Object.entries(headersValue as Record<string, unknown>).map(([key, value]) => [
            key,
            typeof value === "string" ? value : String(value ?? ""),
          ])
        )
      : {};

  return {
    url: asString(data.url ?? data.Url),
    objectKey: asString(data.objectKey ?? data.ObjectKey),
    headers,
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

export async function getEmployeeProfileImageUploadSignature(
  kind: EmployeeProfileImageKind,
  request: DirectUploadRequest
): Promise<DirectUploadSignature> {
  const response = await apiClient.post("/EmployeeProfiles/me/image-upload-signature", {
    ...request,
    kind,
  });
  return normalizeDirectUploadSignature(response.data);
}

export async function uploadEmployeeProfileImageBlobToSignedUrl(
  blob: Blob,
  signature: DirectUploadSignature
) {
  const response = await fetch(signature.url, {
    method: "PUT",
    headers: signature.headers,
    body: blob,
  });

  if (!response.ok) {
    throw new Error(`Upload failed with status ${response.status}`);
  }

  return signature.objectKey;
}
