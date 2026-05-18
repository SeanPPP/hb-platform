import type { ApiResponse } from '../types/api'
import request from '../utils/request'

export interface StoreRetailPriceUpsertActiveItem {
  ProductCode: string
  PurchasePrice?: number
  StoreRetailPriceValue?: number
  DiscountRate?: number
  IsActive?: boolean
  IsAutoPricing?: boolean
}

export interface BatchResultDto {
  Success?: number
  Failed?: number
  success?: number
  failed?: number
}

export async function upsertForActiveStores(items: StoreRetailPriceUpsertActiveItem[]) {
  return request<ApiResponse<BatchResultDto>>('/api/react/v1/store-retail-prices/upsert-active-stores', {
    method: 'POST',
    data: items,
  })
}
