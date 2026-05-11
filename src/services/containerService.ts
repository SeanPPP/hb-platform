import type { ApiResponse } from '../types/api'
import type {
  ComingSoonHomeContainer,
  ComingSoonHomeProduct,
  ContainerDetail,
  ContainerListResponse,
  ContainerMain,
  ContainerQueryRequest,
} from '../types/container'
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

function formatDateValue(date: Date) {
  return date.toISOString().slice(0, 10)
}

function addDays(date: Date, days: number) {
  const next = new Date(date)
  next.setDate(next.getDate() + days)
  return next
}

function toTimestamp(value?: string) {
  if (!value) {
    return Number.MAX_SAFE_INTEGER
  }

  const timestamp = new Date(value).getTime()
  return Number.isNaN(timestamp) ? Number.MAX_SAFE_INTEGER : timestamp
}

function toComingSoonProduct(item: ContainerDetail): ComingSoonHomeProduct {
  return {
    id: item.id,
    hguid: item.hguid,
    productCode: item.商品编码 ?? item.商品信息?.商品编码,
    itemNumber: item.商品信息?.货号,
    productName: item.商品信息?.商品名称,
    englishName: item.商品信息?.英文名称,
    productImage: item.商品信息?.商品图片,
    quantity: item.装柜数量,
    isNewProduct: item.是否新商品 ?? item.warehouseIsActive === false,
    warehouseIsActive: item.warehouseIsActive,
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

interface CheckConflictItem {
  hbProductNo?: string
  productCode?: string
}

interface CheckConflictsResponse {
  success: boolean
  data: Array<{ productCode: string; existingQuantity?: number; existingPieces?: number }>
  message?: string
}

interface AssignContainerItem {
  hbProductNo?: string
  productCode?: string
  quantity: number
  packingQuantity?: number
  unitVolume?: number
  notes?: string
}

interface AssignProductsResponse {
  success: boolean
  data: { created: number; updated: number; failed: Array<{ productCode: string; error: string }> }
  message?: string
}

export async function checkContainerConflicts(containerId: string, items: CheckConflictItem[]): Promise<CheckConflictsResponse> {
  const response = await request<CheckConflictsResponse>(`${API_BASE}/check-conflicts`, {
    method: 'POST',
    data: { ContainerId: containerId, Items: items },
  })
  return response
}

export async function assignProductsToContainer(containerId: string, items: AssignContainerItem[], resolution: 'override' | 'increase', notes?: string): Promise<AssignProductsResponse> {
  const response = await request<AssignProductsResponse>(`${API_BASE}/assign-products`, {
    method: 'POST',
    data: { ContainerId: containerId, Resolution: resolution, Notes: notes, Items: items },
  })
  return response
}

export async function getComingSoonContainers(): Promise<ComingSoonHomeContainer[]> {
  const today = new Date()
  const upcomingStart = formatDateValue(today)
  const upcomingEnd = formatDateValue(addDays(today, 56))
  const arrivedStart = formatDateValue(addDays(today, -7))
  const arrivedEnd = upcomingStart

  const [upcomingResult, arrivedResult] = await Promise.all([
    getContainerList({
      dateType: '预计到岸日期',
      startDate: upcomingStart,
      endDate: upcomingEnd,
      page: 1,
      pageSize: 100,
      sortBy: '预计到岸日期',
      sortDirection: 'asc',
    }),
    getContainerList({
      dateType: '实际到货日期',
      startDate: arrivedStart,
      endDate: arrivedEnd,
      page: 1,
      pageSize: 100,
      sortBy: '实际到货日期',
      sortDirection: 'desc',
    }),
  ])

  const containerMap = new Map<string, ContainerMain>()
  ;[...arrivedResult.containers, ...upcomingResult.containers].forEach((container) => {
    containerMap.set(container.hguid, container)
  })

  const containers = [...containerMap.values()].sort((left, right) => {
    const leftDate = left.实际到货日期 || left.预计到岸日期
    const rightDate = right.实际到货日期 || right.预计到岸日期
    return toTimestamp(leftDate) - toTimestamp(rightDate)
  })

  const productsList = await Promise.all(
    containers.map(async (container) => {
      const products = await getContainerProducts(container.hguid)
      return {
        ...container,
        商品列表: products.map(toComingSoonProduct),
      } satisfies ComingSoonHomeContainer
    }),
  )

  return productsList
}
