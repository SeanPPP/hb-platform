import type { ApiResponse } from '../types/api'
import type {
  DeviceRegistrationDetail,
  DeviceRegistrationItem,
  DeviceRegistrationPagedResult,
  StoreOption,
  UpdateDeviceRegistrationApiPayload,
  UpdateDeviceRegistrationPayload,
} from '../types/deviceRegistration'
import request, { unwrapApiData } from '../utils/request'

const REACT_API_BASE = '/api/react/v1/device-registration'

function getString(raw: Record<string, unknown>, ...keys: string[]) {
  for (const key of keys) {
    const value = raw[key]
    if (typeof value === 'string') {
      return value
    }
  }
  return undefined
}

function getNullableString(raw: Record<string, unknown>, ...keys: string[]) {
  for (const key of keys) {
    const value = raw[key]
    if (typeof value === 'string') {
      return value
    }
    if (value === null) {
      return null
    }
  }
  return null
}

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
    storeName: getNullableString(raw, 'storeName', 'StoreName'),
    deviceType: String(raw.deviceType ?? raw.DeviceType ?? ''),
    deviceSystem: String(raw.deviceSystem ?? raw.DeviceSystem ?? ''),
    status: Number(raw.status ?? raw.Status ?? -1),
    statusDescription: String(raw.statusDescription ?? raw.StatusDescription ?? ''),
    remark: getNullableString(raw, 'remark', 'remarks', 'Remark', 'Remarks'),
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

export function normalizeDeviceRegistrationDetail(
  raw: Record<string, unknown>
): DeviceRegistrationDetail {
  return {
    id: Number(raw.id ?? raw.ID ?? raw.Id ?? 0),
    hardwareId: String(raw.hardwareId ?? raw.设备硬件识别码 ?? raw.HardwareId ?? ''),
    systemDeviceNumber: String(
      raw.systemDeviceNumber ?? raw.系统设备编号 ?? raw.SystemDeviceNumber ?? ''
    ),
    storeCode: getNullableString(raw, 'storeCode', '分店代码', 'StoreCode'),
    storeName: getNullableString(raw, 'storeName', '分店名称', 'StoreName'),
    deviceType: String(raw.deviceType ?? raw.设备类型 ?? raw.DeviceType ?? ''),
    deviceSystem: String(raw.deviceSystem ?? raw.设备系统 ?? raw.DeviceSystem ?? ''),
    status: Number(raw.status ?? raw.设备状态 ?? raw.Status ?? -1),
    statusDescription: String(
      raw.statusDescription ?? raw.设备状态描述 ?? raw.StatusDescription ?? ''
    ),
    remark: getNullableString(raw, 'remark', 'remarks', '备注', 'Remark', 'Remarks'),
    createdAt: getString(raw, 'createdAt', '创建时间', 'CreatedAt'),
    lastModified: getNullableString(raw, 'lastModified', '最后修改时间', 'LastModified'),
    createdBy: getNullableString(raw, 'createdBy', '创建人', 'CreatedBy'),
    lastModifiedBy: getNullableString(raw, 'lastModifiedBy', '最后修改人', 'LastModifiedBy'),
  }
}

export function buildUpdateDeviceRegistrationPayload(
  payload: UpdateDeviceRegistrationPayload
): UpdateDeviceRegistrationApiPayload {
  return {
    设备类型: payload.deviceType,
    设备系统: payload.deviceSystem,
    备注: payload.remark ?? '',
  }
}

export async function getDeviceRegistrations(params?: {
  page?: number
  pageSize?: number
  storeCode?: string
  deviceType?: string
  deviceSystem?: string
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
      deviceType: params?.deviceType,
      deviceSystem: params?.deviceSystem,
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

export async function getDeviceRegistrationDetail(id: number): Promise<DeviceRegistrationDetail> {
  const response = await request.get<ApiResponse<Record<string, unknown>>>(`${REACT_API_BASE}/${id}`)
  return normalizeDeviceRegistrationDetail(unwrapApiData(response) ?? {})
}

export async function updateDeviceRegistration(
  id: number,
  payload: UpdateDeviceRegistrationPayload
): Promise<DeviceRegistrationDetail> {
  const response = await request.put<ApiResponse<Record<string, unknown>>>(
    `${REACT_API_BASE}/${id}`,
    buildUpdateDeviceRegistrationPayload(payload)
  )
  return normalizeDeviceRegistrationDetail(unwrapApiData(response) ?? {})
}

export async function getStoreOptions(): Promise<StoreOption[]> {
  const response = await request.get<ApiResponse<StoreOption[]> | StoreOption[]>(
    '/api/Stores/all-by-name'
  )
  const payload = Array.isArray(response) ? response : response.data
  return Array.isArray(payload) ? payload : []
}
