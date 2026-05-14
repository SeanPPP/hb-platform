import type { ApiResponse } from '../types/api'
import type {
  DeviceRegistrationItem,
  DeviceRegistrationPagedResult,
  StoreOption,
} from '../types/deviceRegistration'
import request from '../utils/request'

function normalizeItem(raw: Record<string, unknown>): DeviceRegistrationItem {
  return {
    id: Number(raw.id ?? raw.Id ?? 0),
    hardwareId: String(raw.hardwareId ?? raw.HardwareId ?? ''),
    systemDeviceNumber: String(raw.systemDeviceNumber ?? raw.SystemDeviceNumber ?? ''),
    storeCode:
      typeof raw.storeCode === 'string'
        ? raw.storeCode
        : typeof raw.StoreCode === 'string'
          ? raw.StoreCode
          : null,
    deviceType: String(raw.deviceType ?? raw.DeviceType ?? ''),
    deviceSystem: String(raw.deviceSystem ?? raw.DeviceSystem ?? ''),
    status: Number(raw.status ?? raw.Status ?? -1),
    statusDescription: String(raw.statusDescription ?? raw.StatusDescription ?? ''),
    createdAt:
      typeof raw.createdAt === 'string'
        ? raw.createdAt
        : typeof raw.CreatedAt === 'string'
          ? raw.CreatedAt
          : undefined,
    lastModified:
      typeof raw.lastModified === 'string'
        ? raw.lastModified
        : typeof raw.LastModified === 'string'
          ? raw.LastModified
          : null,
    createdBy:
      typeof raw.createdBy === 'string'
        ? raw.createdBy
        : typeof raw.CreatedBy === 'string'
          ? raw.CreatedBy
          : null,
    lastModifiedBy:
      typeof raw.lastModifiedBy === 'string'
        ? raw.lastModifiedBy
        : typeof raw.LastModifiedBy === 'string'
          ? raw.LastModifiedBy
          : null,
  }
}

export async function getDeviceRegistrations(params?: {
  page?: number
  pageSize?: number
  storeCode?: string
}): Promise<DeviceRegistrationPagedResult> {
  const response = await request.get<
    ApiResponse<{
      devices?: Record<string, unknown>[]
      pagination?: {
        page?: number
        pageSize?: number
        total?: number
        totalPages?: number
      }
    }>
  >('/api/paged', {
    params: {
      page: params?.page ?? 1,
      pageSize: params?.pageSize ?? 50,
      storeCode: params?.storeCode,
    },
  })

  const data = response.data ?? {}
  const pagination = data.pagination ?? {}

  return {
    devices: Array.isArray(data.devices) ? data.devices.map(normalizeItem) : [],
    total: Number(pagination.total ?? 0),
    page: Number(pagination.page ?? params?.page ?? 1),
    pageSize: Number(pagination.pageSize ?? params?.pageSize ?? 50),
    totalPages: Number(pagination.totalPages ?? 1),
  }
}

export async function activateDevice(id: number) {
  return request.post<ApiResponse<object>>(`/api/${id}/activate`, {})
}

export async function disableDevice(id: number) {
  return request.post<ApiResponse<object>>(`/api/${id}/disable`, {})
}

export async function lockDevice(id: number) {
  return request.post<ApiResponse<object>>(`/api/${id}/lock`, {})
}

export async function getStoreOptions(): Promise<StoreOption[]> {
  const response = await request.get<ApiResponse<StoreOption[]> | StoreOption[]>(
    '/api/Stores/all-by-name'
  )
  const payload = Array.isArray(response) ? response : response.data
  return Array.isArray(payload) ? payload : []
}
