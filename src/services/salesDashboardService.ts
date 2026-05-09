import type { ApiResponse } from '../types/api'
import type { BestSellerResponse } from '../types/salesDashboard'
import request from '../utils/request'

function unwrapBestSellerResponse(payload: ApiResponse<BestSellerResponse> | BestSellerResponse): BestSellerResponse {
  let current: unknown = payload

  for (let depth = 0; depth < 3; depth += 1) {
    if (!current || typeof current !== 'object' || !('data' in current)) {
      break
    }

    const record = current as {
      data?: unknown
      products?: unknown
      total?: unknown
      pageIndex?: unknown
      success?: boolean
      isSuccess?: boolean
      message?: string
    }
    const looksLikeResult =
      Array.isArray(record.products) || 'total' in record || 'pageIndex' in record

    if (looksLikeResult) {
      break
    }

    current = record.data
  }

  const result = (current ?? {}) as Partial<BestSellerResponse>

  return {
    products: Array.isArray(result.products) ? result.products : [],
    total: result.total ?? 0,
    pageIndex: result.pageIndex ?? 1,
    pageSize: result.pageSize ?? 0,
    totalPages: result.totalPages ?? 0,
  }
}

export async function getBestSellers(
  startDate: string,
  endDate: string,
  branchCodes?: string[],
  pageIndex = 1,
  pageSize = 8,
): Promise<BestSellerResponse> {
  const response = await request<ApiResponse<BestSellerResponse> | BestSellerResponse>(
    '/api/react/v1/dashboard/best-sellers',
    {
      method: 'GET',
      params: {
        startDate,
        endDate,
        branchCodes,
        pageIndex,
        pageSize,
      },
    },
  )

  return unwrapBestSellerResponse(response)
}
