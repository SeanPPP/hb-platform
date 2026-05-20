import type { ApiResponse, PagedResult } from '../types/api'
import type {
  EmployeeProfileDetailDto,
  EmployeeProfileQueryDto,
  EmployeeProfileSummaryDto,
  SaveEmployeeProfilePayload,
} from '../types/employeeProfile'
import request, { unwrapApiData, unwrapPagedResult } from '../utils/request'

const ADMIN_BASE_PATH = '/api/EmployeeProfiles/admin'
const ME_BASE_PATH = '/api/EmployeeProfiles/me'

type BackendEmployeeProfile = Record<string, unknown>

function asString(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim() ? value.trim() : undefined
}

function mapEmployeeProfile<T extends EmployeeProfileSummaryDto>(raw: BackendEmployeeProfile): T {
  return {
    id: asString(raw.id) ?? asString(raw.employeeInfoId) ?? asString(raw.EmployeeInfoId),
    userGUID: asString(raw.userGUID) ?? asString(raw.UserGUID) ?? asString(raw.userGuid),
    userId: asString(raw.userId) ?? asString(raw.UserId) ?? asString(raw.userGUID) ?? asString(raw.UserGUID),
    username: asString(raw.username) ?? asString(raw.Username),
    displayName: asString(raw.displayName) ?? asString(raw.DisplayName) ?? asString(raw.fullName) ?? asString(raw.FullName),
    bankBsb: asString(raw.bankBsb) ?? asString(raw.bankBSB) ?? asString(raw.BankBSB),
    bankAccountNumber: asString(raw.bankAccountNumber) ?? asString(raw.bankACC) ?? asString(raw.BankACC),
    superannuationCompanyName: asString(raw.superannuationCompanyName) ?? asString(raw.SuperannuationCompanyName),
    superannuationCompanyCode: asString(raw.superannuationCompanyCode) ?? asString(raw.SuperannuationCompanyCode),
    superannuationAccountNumber:
      asString(raw.superannuationAccountNumber) ?? asString(raw.superannuationAccount) ?? asString(raw.SuperannuationAccount),
    birthday: asString(raw.birthday) ?? asString(raw.Birthday),
    gender: (asString(raw.gender) ?? asString(raw.Gender)) as T['gender'],
    employmentType: (asString(raw.employmentType) ?? asString(raw.employeeType) ?? asString(raw.EmployeeType)) as T['employmentType'],
    avatarUrl: asString(raw.avatarUrl) ?? asString(raw.AvatarUrl),
    identityId: asString(raw.identityId) ?? asString(raw.IdentityId),
    identityPhotoUrl: asString(raw.identityPhotoUrl) ?? asString(raw.IdentityPhotoUrl),
    address: asString(raw.address) ?? asString(raw.Address),
    createdAt: asString(raw.createdAt) ?? asString(raw.CreatedAt),
    updatedAt: asString(raw.updatedAt) ?? asString(raw.UpdatedAt),
  } as T
}

function toBackendPayload(payload: SaveEmployeeProfilePayload) {
  return {
    userGUID: payload.userGUID ?? payload.userId,
    bankBsb: payload.bankBsb,
    bankAccountNumber: payload.bankAccountNumber,
    superannuationCompanyName: payload.superannuationCompanyName,
    superannuationCompanyCode: payload.superannuationCompanyCode,
    superannuationAccountNumber: payload.superannuationAccountNumber,
    birthday: payload.birthday,
    gender: payload.gender,
    employmentType: payload.employmentType,
    avatarUrl: payload.avatarUrl,
    identityId: payload.identityId,
    identityPhotoUrl: payload.identityPhotoUrl,
    address: payload.address,
  }
}

export async function getAdminEmployeeProfiles(params: EmployeeProfileQueryDto): Promise<PagedResult<EmployeeProfileSummaryDto>> {
  const response = await request.get<ApiResponse<PagedResult<EmployeeProfileSummaryDto>>>(ADMIN_BASE_PATH, {
    params: {
      page: params.page,
      pageSize: params.pageSize,
      search: params.keyword,
    },
  })
  const result = unwrapPagedResult(response)
  return {
    ...result,
    items: result.items.map((item) => mapEmployeeProfile<EmployeeProfileSummaryDto>(item as BackendEmployeeProfile)),
  }
}

export async function getAdminEmployeeProfile(id: string): Promise<EmployeeProfileDetailDto> {
  const response = await request.get<ApiResponse<EmployeeProfileDetailDto>>(`${ADMIN_BASE_PATH}/${id}`)
  return mapEmployeeProfile<EmployeeProfileDetailDto>(unwrapApiData(response) as BackendEmployeeProfile)
}

export async function saveAdminEmployeeProfile(payload: SaveEmployeeProfilePayload): Promise<EmployeeProfileDetailDto> {
  const userGuid = payload.userGUID ?? payload.userId
  if (!userGuid) {
    throw new Error('Missing user GUID')
  }
  const response = await request.put<ApiResponse<EmployeeProfileDetailDto>>(
    `${ADMIN_BASE_PATH}/${userGuid}`,
    toBackendPayload(payload),
  )
  return mapEmployeeProfile<EmployeeProfileDetailDto>(unwrapApiData(response) as BackendEmployeeProfile)
}

export async function getMyEmployeeProfile(): Promise<EmployeeProfileDetailDto> {
  const response = await request.get<ApiResponse<EmployeeProfileDetailDto>>(ME_BASE_PATH)
  return mapEmployeeProfile<EmployeeProfileDetailDto>(unwrapApiData(response) as BackendEmployeeProfile)
}

export async function updateMyEmployeeProfile(payload: SaveEmployeeProfilePayload): Promise<EmployeeProfileDetailDto> {
  const response = await request.put<ApiResponse<EmployeeProfileDetailDto>>(ME_BASE_PATH, toBackendPayload(payload))
  return mapEmployeeProfile<EmployeeProfileDetailDto>(unwrapApiData(response) as BackendEmployeeProfile)
}
