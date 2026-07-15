export const EMPLOYMENT_TYPES = ["fullTime", "partTime", "casual"] as const;

export type EmploymentType = (typeof EMPLOYMENT_TYPES)[number];

export const GENDERS = ["male", "female", "other"] as const;

export type Gender = (typeof GENDERS)[number];

export const EMPLOYEE_PROFILE_IMAGE_KINDS = ["avatar", "identityPhoto"] as const;

export type EmployeeProfileImageKind = (typeof EMPLOYEE_PROFILE_IMAGE_KINDS)[number];

export interface EmployeeProfile {
  username: string;
  displayName?: string;
  bankBsb: string;
  bankAccountNumber: string;
  superannuationCompanyName: string;
  superannuationCompanyCode: string;
  superannuationAccountNumber: string;
  birthday: string;
  gender: string;
  employmentType: string;
  avatarUrl: string;
  identityId: string;
  identityPhotoUrl: string;
  identityPhotoUrlExpiresAt?: string;
  address: string;
  createdAt?: string;
  updatedAt?: string;
}

export interface UpdateEmployeeProfilePayload {
  bankBsb: string;
  bankAccountNumber: string;
  superannuationCompanyName: string;
  superannuationCompanyCode: string;
  superannuationAccountNumber: string;
  birthday: string;
  gender: string;
  employmentType: string;
  identityId: string;
  address: string;
}

export interface DirectUploadRequest {
  fileName: string;
  contentType: string;
  fileSize: number;
}

export interface DirectUploadSignature {
  url: string;
  objectKey: string;
  headers: Record<string, string>;
}

export interface CashierBarcodeResponse {
  exists: boolean;
  barcode: string;
  format: string;
  printCount: number;
  createdAt?: string;
  updatedAt?: string;
}
