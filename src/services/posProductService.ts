import type { ApiResponse } from '../types/api'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/products'

export interface PosProductDto {
  productCode: string
  productCategoryGUID: string
  localSupplierCode?: string
  itemNumber?: string
  barcode?: string
  productName: string
  productType?: number
  middlePackageQuantity?: number
  purchasePrice?: number
  retailPrice?: number
  isAutoPricing: boolean
  productImage?: string
  isActive: boolean
  isSpecialProduct: boolean
  warehouseCategoryGUID?: string
  updatedAt?: string
  updatedBy?: string
}

export async function getPosProductById(productCode: string): Promise<PosProductDto> {
  const response = await request.get<ApiResponse<PosProductDto>>(`${API_BASE}/${productCode}`)
  return unwrapApiData(response)
}

export async function getPosProducts(params: {
  search?: string
  localSupplierCode?: string
  pageNumber?: number
  pageSize?: number
}) {
  const response = await request.post<ApiResponse<{ items: PosProductDto[]; total: number }>>(
    `${API_BASE}/list`,
    params,
  )
  return unwrapApiData(response)
}
