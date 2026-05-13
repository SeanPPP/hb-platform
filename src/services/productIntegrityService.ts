import type { ApiResponse } from '../types/api'
import type {
  ProductIntegrityCheckResultDto,
  ProductIntegrityFixRequestDto,
  ProductIntegrityFixResultDto,
} from '../types/productIntegrity'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/product-integrity'

export async function checkIntegrity(storeCodes?: string[]): Promise<ProductIntegrityCheckResultDto> {
  const response = await request.post<ApiResponse<ProductIntegrityCheckResultDto>>(
    `${API_BASE}/check`,
    storeCodes || null,
  )
  return unwrapApiData(response)
}

export async function fixIntegrity(
  requestDto: ProductIntegrityFixRequestDto,
): Promise<ProductIntegrityFixResultDto> {
  const response = await request.post<ApiResponse<ProductIntegrityFixResultDto>>(
    `${API_BASE}/fix`,
    requestDto,
  )
  return unwrapApiData(response)
}
