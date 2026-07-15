export type EmployeeProfileGender = 'male' | 'female' | 'other' | 'unknown'

export type EmployeeEmploymentType = 'fullTime' | 'partTime' | 'casual'

export interface EmployeeProfileQueryDto {
  keyword?: string
  page?: number
  pageSize?: number
}

export interface EmployeeProfileSummaryDto {
  id?: string
  userId?: string
  userGUID?: string
  username?: string
  displayName?: string
  bankBsb?: string
  bankAccountNumber?: string
  superannuationCompanyName?: string
  superannuationCompanyCode?: string
  superannuationAccountNumber?: string
  birthday?: string
  gender?: EmployeeProfileGender
  employmentType?: EmployeeEmploymentType
  avatarUrl?: string
  identityId?: string
  identityType?: string
  identityPhotoUrl?: string
  identityPhotoUrlExpiresAt?: string
  address?: string
  createdAt?: string
  updatedAt?: string
}

export interface EmployeeProfileDetailDto extends EmployeeProfileSummaryDto {}

export interface SaveEmployeeProfilePayload {
  id?: string
  userId?: string
  userGUID?: string
  username?: string
  displayName?: string
  bankBsb?: string
  bankAccountNumber?: string
  superannuationCompanyName?: string
  superannuationCompanyCode?: string
  superannuationAccountNumber?: string
  birthday?: string
  gender?: EmployeeProfileGender
  employmentType?: EmployeeEmploymentType
  avatarUrl?: string
  identityId?: string
  identityType?: string
  identityPhotoUrl?: string
  address?: string
}

export type EmployeeProfileSensitiveChangeStatus = 'Pending' | 'Approved' | 'Rejected' | 'Superseded'
export type EmployeeProfileSensitiveField =
  | 'bankBsb'
  | 'bankAccountNumber'
  | 'superannuationCompanyName'
  | 'superannuationCompanyCode'
  | 'superannuationAccountNumber'
  | 'identityType'
  | 'identityId'
  | 'identityPhotoUrl'

export interface EmployeeProfileSensitiveChangeQueryDto {
  keyword?: string
  page?: number
  pageSize?: number
  status?: EmployeeProfileSensitiveChangeStatus
}

export interface EmployeeProfileSensitiveChangeSummaryDto {
  requestId: number
  userGuid: string
  username?: string
  status: EmployeeProfileSensitiveChangeStatus
  bankBsb?: string
  bankAccountSummary?: string
  superannuationCompanyName?: string
  superannuationCompanyCode?: string
  superannuationAccountSummary?: string
  identityType?: string
  identityIdSummary?: string
  hasIdentityPhoto: boolean
  baseSensitiveRevision: number
  submittedAt: string
  reviewedAt?: string
  reviewReason?: string
  changedFields: EmployeeProfileSensitiveField[]
}

export interface EmployeeProfileSensitiveChangeDetailDto extends EmployeeProfileSensitiveChangeSummaryDto {
  bankAccountNumber?: string
  superannuationAccountNumber?: string
  identityId?: string
  identityPhotoUrl?: string
  identityPhotoUrlExpiresAt?: string
  submittedBy?: string
  reviewedBy?: string
}

export interface EmployeeProfileSensitiveReviewPayload {
  reason?: string
}

export interface EmployeeProfileSensitiveRejectPayload {
  reason: string
}
