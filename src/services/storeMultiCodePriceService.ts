import type { ApiResponse } from '../types/api'
import request from '../utils/request'
import type { BatchResultDto } from './storeRetailPriceService'

export interface StoreMultiCodePriceUpsertActiveItem {
  ProductCode: string
  PurchasePrice?: number
  MultiCodeRetailPrice?: number
  DiscountRate?: number
  IsActive?: boolean
  IsAutoPricing?: boolean
}

export async function upsertForActiveStores(items: StoreMultiCodePriceUpsertActiveItem[]) {
  return request<ApiResponse<BatchResultDto>>('/api/react/v1/store-multi-code-prices/upsert-active-stores', {
    method: 'POST',
    data: items,
  })
}
