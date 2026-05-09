import type { ApiResponse } from '../types/api'
import type {
  CreateLocalSupplierDto,
  LocalSupplierDto,
  LocalSupplierQueryDto,
  SyncLocalSupplierResult,
} from '../types/localSupplier'
import request, { unwrapApiData } from '../utils/request'

export async function getActiveLocalSuppliers(): Promise<LocalSupplierDto[]> {
  const response = await request.get<ApiResponse<LocalSupplierDto[]>>('/api/react/v1/local-suppliers/active')
  return unwrapApiData(response) ?? []
}

export async function getLocalSuppliers(
  params?: LocalSupplierQueryDto,
): Promise<{ items: LocalSupplierDto[]; total: number }> {
  const response = await request.get<ApiResponse<{ items: LocalSupplierDto[]; total: number }>>(
    '/api/react/v1/local-suppliers',
    { params: params as Record<string, unknown> },
  )
  return unwrapApiData(response)
}

export async function syncLocalSuppliers(params?: {
  since?: string
  overwrite?: boolean
}): Promise<SyncLocalSupplierResult> {
  const response = await request.post<ApiResponse<SyncLocalSupplierResult>>(
    '/api/react/v1/local-suppliers/sync',
    params ?? {},
  )
  return unwrapApiData(response)
}

export async function createLocalSupplier(data: CreateLocalSupplierDto): Promise<LocalSupplierDto> {
  const response = await request.post<ApiResponse<LocalSupplierDto>>('/api/react/v1/local-suppliers', data)
  return unwrapApiData(response)
}
