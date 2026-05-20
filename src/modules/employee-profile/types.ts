export const EMPLOYMENT_TYPES = ["fullTime", "partTime", "casual"] as const;

export type EmploymentType = (typeof EMPLOYMENT_TYPES)[number];

export const GENDERS = ["male", "female", "other"] as const;

export type Gender = (typeof GENDERS)[number];

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
  avatarUrl: string;
  identityId: string;
  identityPhotoUrl: string;
  address: string;
}

