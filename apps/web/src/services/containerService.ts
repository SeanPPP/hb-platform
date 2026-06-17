import type { ApiResponse } from '../types/api'
import type {
  ComingSoonHomeContainer,
  ComingSoonHomeContainerSummary,
  ComingSoonHomeProduct,
  ContainerDetailBatchActionResult,
  ContainerDetailBatchScope,
  ContainerDomesticSetCodeItem,
  ContainerDetail,
  ContainerDetailQuery,
  ContainerDetailQueryResult,
  CreateContainerRequest,
  DateFilterOption,
  ContainerListResponse,
  ContainerMain,
  ContainerQueryRequest,
  HqTranslationResult,
  SyncResult,
  UpdateContainerDomesticSetCodePriceItem,
  UpdateContainerDomesticSetCodePricesResult,
  UpdateContainerDetailRequest,
  UpdateContainerRequest,
} from '../types/container'
import request from '../utils/request'

const API_BASE = '/api/react/v1/containers'

type RawHqTranslationResult = HqTranslationResult & {
  totalCandidates?: number
  totalTranslated?: number
  totalSkipped?: number
  totalFailed?: number
  samples?: Record<string, string>
}

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

interface ContainerArrayApiResponse<T> {
  success?: boolean
  message?: string
  data?: T[]
}

function ensureSuccess(success?: boolean, message?: string, fallback?: string) {
  if (success === false) {
    throw new Error(message || fallback || '请求失败')
  }
}

function normalizeHqTranslationResult(result: RawHqTranslationResult = {}): HqTranslationResult {
  return {
    TotalCandidates: result.TotalCandidates ?? result.totalCandidates,
    TotalTranslated: result.TotalTranslated ?? result.totalTranslated,
    TotalSkipped: result.TotalSkipped ?? result.totalSkipped,
    TotalFailed: result.TotalFailed ?? result.totalFailed,
    Samples: result.Samples ?? result.samples,
  }
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
    barcode: item.商品信息?.条形码,
    productName: item.商品信息?.商品名称,
    englishName: item.商品信息?.英文名称,
    productImage: item.商品信息?.商品图片,
    quantity: item.装柜数量,
    retailPrice: item.商品信息?.零售价格,
    isNewProduct: item.是否新商品 ?? item.warehouseIsActive === false,
    warehouseIsActive: item.warehouseIsActive,
  }
}

const itemNumberCollator = new Intl.Collator('en', {
  numeric: true,
  sensitivity: 'base',
})

function compareComingSoonProducts(left: ComingSoonHomeProduct, right: ComingSoonHomeProduct) {
  const leftItemNumber = (left.itemNumber ?? '').trim()
  const rightItemNumber = (right.itemNumber ?? '').trim()

  if (leftItemNumber && !rightItemNumber) return -1
  if (!leftItemNumber && rightItemNumber) return 1

  const itemNumberCompare = itemNumberCollator.compare(leftItemNumber, rightItemNumber)
  if (itemNumberCompare !== 0) return itemNumberCompare

  const productCodeCompare = itemNumberCollator.compare(left.productCode ?? '', right.productCode ?? '')
  if (productCodeCompare !== 0) return productCodeCompare

  return itemNumberCollator.compare(left.productName ?? left.englishName ?? '', right.productName ?? right.englishName ?? '')
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

export async function queryContainerProducts(
  containerGuid: string,
  query: Omit<ContainerDetailQuery, 'containerGuid'> & Partial<Pick<ContainerDetailQuery, 'containerGuid'>>,
  signal?: AbortSignal,
): Promise<ContainerDetailQueryResult> {
  const { containerGuid: _ignoredContainerGuid, ...queryBody } = query
  const response = await request<ApiResponse<ContainerDetailQueryResult> | { success?: boolean; isSuccess?: boolean; message?: string; data?: ContainerDetailQueryResult }>(
    `${API_BASE}/${encodeURIComponent(containerGuid)}/products/query`,
    {
      method: 'POST',
      data: {
        // 后端契约要求 body 内也携带货柜 GUID，避免路由参数和查询体脱节。
        containerGuid,
        ...queryBody,
      },
      signal,
    },
  )

  ensureSuccess(response.success ?? response.isSuccess, response.message, '查询货柜商品明细失败')

  if (!response.data) {
    throw new Error('查询货柜商品明细失败')
  }

  return response.data
}

export async function getContainerDomesticSetCodes(productCode: string, signal?: AbortSignal): Promise<ContainerDomesticSetCodeItem[]> {
  const response = await request<ApiResponse<ContainerDomesticSetCodeItem[]> | { success?: boolean; isSuccess?: boolean; message?: string; data?: ContainerDomesticSetCodeItem[] }>(
    `${API_BASE}/products/${encodeURIComponent(productCode)}/domestic-set-codes`,
    {
      method: 'GET',
      signal,
    },
  )

  ensureSuccess(response.success ?? response.isSuccess, response.message, '获取套装多码数据失败')
  return response.data ?? []
}

export async function updateContainerDomesticSetCodePrices(
  productCode: string,
  items: UpdateContainerDomesticSetCodePriceItem[],
): Promise<UpdateContainerDomesticSetCodePricesResult> {
  const response = await request<ApiResponse<UpdateContainerDomesticSetCodePricesResult> | { success?: boolean; isSuccess?: boolean; message?: string; data?: UpdateContainerDomesticSetCodePricesResult }>(
    `${API_BASE}/products/${encodeURIComponent(productCode)}/domestic-set-codes/prices`,
    {
      method: 'PATCH',
      data: {
        // 只回写国内套装表价格字段，不同步仓库/POS 多码表。
        items,
      },
    },
  )

  ensureSuccess(response.success ?? response.isSuccess, response.message, '保存套装多码价格失败')
  return response.data ?? { updatedCount: 0 }
}

async function postContainerDetailAction<TBody extends object>(
  containerGuid: string,
  action: string,
  body: TBody,
  fallbackMessage: string,
): Promise<ContainerDetailBatchActionResult> {
  const response = await request<ApiResponse<ContainerDetailBatchActionResult> | { success?: boolean; isSuccess?: boolean; message?: string; data?: ContainerDetailBatchActionResult }>(
    `${API_BASE}/${encodeURIComponent(containerGuid)}/actions/${action}`,
    {
      method: 'POST',
      data: body,
    },
  )

  ensureSuccess(response.success ?? response.isSuccess, response.message, fallbackMessage)

  return response.data ?? { totalUpdated: 0 }
}

export async function applyContainerFloatRateByScope(
  containerGuid: string,
  scope: ContainerDetailBatchScope,
  floatRate: number,
): Promise<ContainerDetailBatchActionResult> {
  return postContainerDetailAction(containerGuid, 'apply-float-rate', {
    ...scope,
    // 批量写接口使用统一 scope；未勾选时 scope.query 代表当前筛选结果全量。
    floatRate,
  }, '批量调浮率失败')
}

export async function applyContainerPricesByScope(
  containerGuid: string,
  scope: ContainerDetailBatchScope,
  prices: { importPrice?: number | null; oemPrice?: number | null },
): Promise<ContainerDetailBatchActionResult> {
  return postContainerDetailAction(containerGuid, 'apply-prices', {
    ...scope,
    importPrice: prices.importPrice,
    oemPrice: prices.oemPrice,
  }, '批量改价失败')
}

export async function recalculateContainerCostsByScope(
  containerGuid: string,
  scope: ContainerDetailBatchScope,
): Promise<ContainerDetailBatchActionResult> {
  return postContainerDetailAction(containerGuid, 'recalculate-costs', scope, '重算成本失败')
}

export async function backfillContainerLastPricesByScope(
  containerGuid: string,
  scope: ContainerDetailBatchScope,
): Promise<ContainerDetailBatchActionResult> {
  return postContainerDetailAction(containerGuid, 'backfill-last-prices', scope, '回填上次价格失败')
}

export async function getContainerDetail(containerGuid: string): Promise<ContainerMain> {
  const response = await request<ApiResponse<ContainerMain> | { success?: boolean; isSuccess?: boolean; message?: string; data?: ContainerMain }>(
    `${API_BASE}/${encodeURIComponent(containerGuid)}`,
    { method: 'GET' },
  )

  ensureSuccess(response.success ?? response.isSuccess, response.message, '获取货柜详情失败')

  const data = 'data' in response ? response.data : undefined
  if (!data) {
    throw new Error('获取货柜详情失败')
  }
  return data
}

export async function getDateFilterOptions(): Promise<DateFilterOption[]> {
  const response = await request<ApiResponse<DateFilterOption[]> | { success?: boolean; isSuccess?: boolean; message?: string; data?: DateFilterOption[] }>(
    `${API_BASE}/date-filter-options`,
    { method: 'GET' },
  )

  ensureSuccess(response.success ?? response.isSuccess, response.message, '获取日期筛选项失败')

  return response.data?.length
    ? response.data
    : [
        { value: '预计到岸日期', label: '预计到岸日期' },
        { value: '实际到货日期', label: '实际到货日期' },
      ]
}

export async function createContainer(data: CreateContainerRequest): Promise<string> {
  const response = await request<{ success?: boolean; message?: string; data?: { containerGuid?: string } }>(API_BASE, {
    method: 'POST',
    data,
  })

  ensureSuccess(response.success, response.message, '创建货柜失败')
  return response.data?.containerGuid ?? ''
}

export async function updateContainer(containerGuid: string, data: UpdateContainerRequest): Promise<boolean> {
  const response = await request<{ success?: boolean; message?: string }>(`${API_BASE}/${encodeURIComponent(containerGuid)}`, {
    method: 'PUT',
    data,
  })

  ensureSuccess(response.success, response.message, '更新货柜失败')
  return true
}

export async function batchUpdateDetails(
  updates: UpdateContainerDetailRequest[],
): Promise<{ totalUpdated: number; totalRequested: number }> {
  const response = await request<{
    success?: boolean
    message?: string
    data?: { totalUpdated?: number; totalRequested?: number }
  }>(`${API_BASE}/batch-update-details`, {
    method: 'POST',
    data: updates.map((item) => ({
      HGUID: item.hguid,
      调整浮率: item.调整浮率,
      国内价格: item.国内价格,
      进口价格: item.进口价格,
      运输成本: item.运输成本,
      商品名称: item.商品名称,
      英文名称: item.英文名称,
      ClearEnglishName: item.ClearEnglishName,
      ProductCategoryGUID: item.ProductCategoryGUID,
      贴牌价格: item.贴牌价格,
      单件装箱数: item.单件装箱数,
      中包数: item.中包数,
      单件体积: item.单件体积,
      装柜数量: item.装柜数量,
      合计装柜体积: item.合计装柜体积,
      合计装柜金额: item.合计装柜金额,
      IsActive: item.IsActive,
      SkipRelatedProductSync: item.SkipRelatedProductSync,
    })),
  })

  ensureSuccess(response.success, response.message, '批量更新货柜明细失败')
  return {
    totalUpdated: response.data?.totalUpdated ?? updates.length,
    totalRequested: response.data?.totalRequested ?? updates.length,
  }
}

export async function batchDeleteDetails(hguids: string[]): Promise<{ totalDeleted: number; totalRequested: number }> {
  const response = await request<{
    success?: boolean
    message?: string
    data?: { totalDeleted?: number; totalRequested?: number }
  }>(`${API_BASE}/batch-delete-details`, {
    method: 'POST',
    data: { hguids },
  })

  ensureSuccess(response.success, response.message, '批量删除货柜明细失败')
  return {
    totalDeleted: response.data?.totalDeleted ?? hguids.length,
    totalRequested: response.data?.totalRequested ?? hguids.length,
  }
}

export async function syncContainersFromHq(startDate?: string): Promise<SyncResult> {
  const response = await request<{ success?: boolean; message?: string; data?: SyncResult }>(`${API_BASE}/sync-from-hq`, {
    method: 'POST',
    data: { startDate },
  })

  ensureSuccess(response.success, response.message, '从HQ同步货柜失败')
  return response.data ?? { isSuccess: response.success, message: response.message }
}

export async function translateHqProductNamesByContainerNumber(containerNumber: string): Promise<HqTranslationResult> {
  const response = await request<{ success?: boolean; message?: string; data?: RawHqTranslationResult } & RawHqTranslationResult>(
    '/api/react/v1/hq-products/translate-names/by-container-number',
    {
      method: 'POST',
      data: {
        ContainerNumbers: [containerNumber],
        OverwriteExisting: false,
      },
    },
  )

  ensureSuccess(response.success, response.message, '翻译HQ数据失败')
  return normalizeHqTranslationResult(response.data ?? response)
}

export async function pushContainersToHbSales(containerGuids: string[]): Promise<SyncResult> {
  const response = await request<{ success?: boolean; message?: string; data?: SyncResult }>(`${API_BASE}/push-to-hbsales`, {
    method: 'POST',
    data: { containerGuids },
  })

  ensureSuccess(response.success, response.message, '发送到HBSales失败')
  return response.data ?? { isSuccess: response.success, message: response.message }
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

export interface AssignContainerItem {
  hbProductNo?: string
  productCode?: string
  quantity: number
  packingQuantity?: number
  unitVolume?: number
  domesticPrice?: number
  oemPrice?: number
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

export async function getComingSoonContainerSummaries(): Promise<ComingSoonHomeContainerSummary[]> {
  const response = await request<ContainerArrayApiResponse<ContainerMain> | ContainerMain[]>(
    `${API_BASE}/coming-soon/summaries`,
    { method: 'GET' },
  )
  const containers = Array.isArray(response) ? response : response.data ?? []

  return containers.sort((left, right) => {
    const leftDate = left.实际到货日期 || left.预计到岸日期
    const rightDate = right.实际到货日期 || right.预计到岸日期
    return toTimestamp(leftDate) - toTimestamp(rightDate)
  })
}

export async function getComingSoonContainerProducts(containerGuid: string): Promise<ComingSoonHomeProduct[]> {
  const response = await request<ContainerArrayApiResponse<ContainerDetail> | ContainerDetail[]>(
    `${API_BASE}/coming-soon/${containerGuid}/products`,
    { method: 'GET' },
  )
  const products = Array.isArray(response) ? response : response.data ?? []
  return products.map(toComingSoonProduct).sort(compareComingSoonProducts)
}

export async function getComingSoonContainers(): Promise<ComingSoonHomeContainer[]> {
  const containers = await getComingSoonContainerSummaries()
  const productsList = await Promise.all(
    containers.map(async (container) => ({
      ...container,
      商品列表: await getComingSoonContainerProducts(container.hguid),
    } satisfies ComingSoonHomeContainer)),
  )

  return productsList
}
