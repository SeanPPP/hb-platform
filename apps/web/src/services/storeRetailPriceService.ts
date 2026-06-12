import type { ApiResponse } from '../types/api'
import request, { RequestError, unwrapApiData } from '../utils/request'

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

export function assertStoreBatchSuccess(response: ApiResponse<BatchResultDto>, fallbackMessage: string): BatchResultDto {
  if (response.success === false || response.isSuccess === false) {
    throw new RequestError(response.message || fallbackMessage, 200, response)
  }

  const result = unwrapApiData(response) ?? {}
  const failed = Number(result.Failed ?? result.failed ?? 0)
  if (failed > 0) {
    throw new RequestError(response.message || `${fallbackMessage}：${failed} 条失败`, 200, response)
  }

  return result
}

export async function upsertForActiveStores(items: StoreRetailPriceUpsertActiveItem[]) {
  const response = await request<ApiResponse<BatchResultDto>>('/api/react/v1/store-retail-prices/upsert-active-stores', {
    method: 'POST',
    data: items,
  })
  return assertStoreBatchSuccess(response, '门店零售价更新失败')
}
