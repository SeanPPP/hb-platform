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
  identityPhotoUrl?: string
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
  identityPhotoUrl?: string
  address?: string
}
