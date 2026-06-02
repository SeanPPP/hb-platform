import type { ApiResponse } from '../types/api'
import request from '../utils/request'
import { assertStoreBatchSuccess, type BatchResultDto } from './storeRetailPriceService'

export interface StoreMultiCodePriceUpsertActiveItem {
  ProductCode: string
  PurchasePrice?: number
  MultiCodeRetailPrice?: number
  DiscountRate?: number
  IsActive?: boolean
  IsAutoPricing?: boolean
}

export async function upsertForActiveStores(items: StoreMultiCodePriceUpsertActiveItem[]) {
  const response = await request<ApiResponse<BatchResultDto>>('/api/react/v1/store-multi-code-prices/upsert-active-stores', {
    method: 'POST',
    data: items,
  })
  return assertStoreBatchSuccess(response, '门店多码价格更新失败')
}
