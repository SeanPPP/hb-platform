import request, { RequestError } from '../utils/request'
import type {
  StoreSupplyStatus,
  StoreSupplyWatchSummary,
  SupplyNoticeInput,
  WarehouseSupplyNotice,
} from '../types/supplyNotice'

const WAREHOUSE_BASE = '/api/react/v1/product-warehouse/supply-notices'
const STORE_BASE = '/api/react/v1/store-order/supply'

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

/** 后端统一返回 { success, data, message }；请求封装可能已经剥过一层，这里两种都兼容。 */
function unwrapData<T>(payload: unknown, fallback: T): T {
  if (isRecord(payload) && 'data' in payload) {
    return (payload.data as T) ?? fallback
  }
  return (payload as T) ?? fallback
}

export async function queryWarehouseSupplyNotices(productCodes: string[]): Promise<WarehouseSupplyNotice[]> {
  if (!productCodes.length) {
    return []
  }
  const response = await request<unknown>(`${WAREHOUSE_BASE}/query`, {
    method: 'POST',
    data: { productCodes },
  })
  return unwrapData<WarehouseSupplyNotice[]>(response, [])
}

export interface UpsertSupplyNoticeResult {
  success: boolean
  successCount: number
  skippedProductCodes: string[]
  message: string
}

/** 对已下架商品登记或修改供货说明；在架或不存在的商品会被后端跳过并回报。 */
export async function upsertWarehouseSupplyNotices(
  productCodes: string[],
  notice: SupplyNoticeInput,
): Promise<UpsertSupplyNoticeResult> {
  const response = await request<unknown>(WAREHOUSE_BASE, {
    method: 'POST',
    data: { productCodes, notice },
  })
  return unwrapData<UpsertSupplyNoticeResult>(response, {
    success: false,
    successCount: 0,
    skippedProductCodes: [],
    message: '',
  })
}

/** 搜索或扫码零结果时调用：按条码 → 货号 → 商品编码精确查询暂停供货的商品。 */
export async function lookupStoreSupplyStatus(storeCode: string, code: string): Promise<StoreSupplyStatus[]> {
  const response = await request<unknown>(`${STORE_BASE}/lookup`, {
    method: 'POST',
    data: { storeCode, code },
  })
  return unwrapData<{ items?: StoreSupplyStatus[] }>(response, {}).items ?? []
}

export async function getStoreSupplyWatches(storeCode: string): Promise<StoreSupplyStatus[]> {
  const response = await request<unknown>(`${STORE_BASE}/watches/${encodeURIComponent(storeCode)}`)
  return unwrapData<StoreSupplyStatus[]>(response, [])
}

export async function getStoreSupplyWatchSummary(storeCode: string): Promise<StoreSupplyWatchSummary> {
  const response = await request<unknown>(`${STORE_BASE}/watches/${encodeURIComponent(storeCode)}/summary`)
  return unwrapData<StoreSupplyWatchSummary>(response, { watchingCount: 0, restockedCount: 0 })
}

export async function watchStoreSupply(storeCode: string, productCode: string): Promise<void> {
  await request<unknown>(`${STORE_BASE}/watches`, { method: 'POST', data: { storeCode, productCode } })
}

export async function unwatchStoreSupply(storeCode: string, productCode: string): Promise<void> {
  await request<unknown>(`${STORE_BASE}/watches/remove`, { method: 'POST', data: { storeCode, productCode } })
}

/** 确认“已恢复订货”提醒；不传 productCodes 表示确认该分店全部已恢复的关注。 */
export async function acknowledgeStoreSupplyRestocked(storeCode: string, productCodes?: string[]): Promise<number> {
  const response = await request<unknown>(`${STORE_BASE}/watches/acknowledge`, {
    method: 'POST',
    data: { storeCode, productCodes },
  })
  return unwrapData<number>(response, 0)
}

/** 提交订单被“暂停供货”拦截：后端返回 400 + errorCode SUPPLY_PAUSED，details 为受影响货号。 */
export function getSupplyPausedSubmitLabels(error: unknown): string[] | null {
  if (!(error instanceof RequestError)) {
    return null
  }
  const payload = isRecord(error.payload) ? error.payload : null
  const code = payload?.errorCode ?? payload?.code
  if (code !== 'SUPPLY_PAUSED' && !error.message.includes('SUPPLY_PAUSED')) {
    return null
  }
  const details = payload?.details
  return Array.isArray(details) ? details.map(String) : []
}
