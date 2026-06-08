import type { ApiResponse } from '../types/api'
import type {
  BatchDeleteRequest,
  BatchUpdatePricesRequest,
  BatchUpdateStatusRequest,
  GridDataRequest,
  GridDataResponse,
} from '../types/multiCodeSet'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/product-set-codes'

export async function getGridData(params: GridDataRequest): Promise<GridDataResponse> {
  const pageSize = params.pageSize ?? 200
  const pageIndex = Math.max(1, params.pageIndex ?? 1)
  const startRow = (pageIndex - 1) * pageSize
  const response = await request.post<ApiResponse<GridDataResponse>>(`${API_BASE}/grid`, {
    productCode: params.productCode,
    startRow,
    endRow: startRow + pageSize,
    pageSize,
    // 后端新版本读取 productCode，filterModel 用于兼容旧 grid 筛选链路。
    filterModel: {
      productCode: {
        filterType: 'text',
        type: 'equals',
        filter: params.productCode,
      },
    },
  })
  const result = unwrapApiData(response)
  const items = (result.items ?? [])
    .filter((item) => item.productCode === params.productCode)
    .map((item) => ({
      ...item,
      id: item.id ?? item.setCodeId,
    }))

  return {
    ...result,
    items,
    total: items.length,
  }
}

export async function batchUpdateStatus(data: BatchUpdateStatusRequest): Promise<void> {
  await request.put(`${API_BASE}/batch-status`, data)
}

export async function batchUpdatePrices(data: BatchUpdatePricesRequest): Promise<void> {
  await request.put(`${API_BASE}/batch-prices`, data)
}

export async function batchDelete(data: BatchDeleteRequest): Promise<void> {
  await request.delete(`${API_BASE}/batch-delete`, { data })
}

export async function batchUpdateBarcodes(data: {
  items: { id: string; setBarcode?: string }[]
}): Promise<void> {
  await request.put(`${API_BASE}/batch-barcodes`, data)
}

export async function batchCreateSetCodes(data: {
  items: {
    productCode: string
    setItemNumber?: string
    setBarcode?: string
    setPurchasePrice?: number
    setRetailPrice?: number
    isActive?: boolean
  }[]
}): Promise<void> {
  await request.post(`${API_BASE}/batch-create`, data)
}
