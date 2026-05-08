import type { ApiResponse } from '../types/api'
import type { ContainerDetail, ContainerListResponse, ContainerMain, ContainerQueryRequest } from '../types/container'
import request from '../utils/request'

const API_BASE = '/api/react/v1/containers'

interface ContainerListApiResponse {
  success?: boolean
  message?: string
  data?: {
    items?: ContainerMain[]
    total?: number
    page?: number
    pageSize?: number
  }
}

function ensureSuccess(success?: boolean, message?: string, fallback?: string) {
  if (success === false) {
    throw new Error(message || fallback || '请求失败')
  }
}

export async function getContainerList(query: ContainerQueryRequest): Promise<ContainerListResponse> {
  const response = await request<ContainerListApiResponse>(`${API_BASE}/list`, {
    method: 'POST',
    data: {
      DateType: query.dateType || '预计到岸日期',
      StartDate: query.startDate,
      EndDate: query.endDate,
      Page: query.page || 1,
      PageSize: query.pageSize || 1000,
      ItemNumberFilter: query.itemNumberFilter,
      SortBy: query.sortBy || '预计到岸日期',
      SortDirection: query.sortDirection || 'desc',
    },
  })

  ensureSuccess(response.success, response.message, '获取货柜列表失败')

  const items = response.data?.items ?? []
  const total = response.data?.total ?? items.length
  const page = response.data?.page ?? query.page ?? 1
  const pageSize = response.data?.pageSize ?? query.pageSize ?? 1000

  return {
    containers: items,
    totalCount: total,
    page,
    pageSize,
    totalPages: pageSize > 0 ? Math.ceil(total / pageSize) : 1,
  }
}

export async function getContainerProducts(containerGuid: string): Promise<ContainerDetail[]> {
  const response = await request<ApiResponse<ContainerDetail[]> | ContainerDetail[]>(`${API_BASE}/${containerGuid}/products`, {
    method: 'GET',
  })

  if (Array.isArray(response)) {
    return response
  }

  ensureSuccess(response.success ?? response.isSuccess, response.message, '获取货柜商品列表失败')

  return response.data ?? []
}
