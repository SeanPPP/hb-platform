import type { ApiResponse } from '../types/api'
import type {
  BestSellerResponse,
  BranchSalesAggregate,
  ChinaSupplierSalesRank,
  DateRange,
  ExecutiveBranchPerformance,
  ExecutiveHourlyTraffic,
  PagedSalesProductDetailWithDiscount,
  SupplierSalesRank,
  WeeklyHierarchyData,
} from '../types/salesDashboard'
import request from '../utils/request'

export type {
  BranchSalesAggregate,
  ChinaSupplierSalesRank,
  DateRange,
  ExecutiveBranchPerformance,
  ExecutiveHourlyTraffic,
  PagedSalesProductDetailWithDiscount,
  SupplierSalesRank,
  WeeklyHierarchyData,
} from '../types/salesDashboard'

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

function unwrapApiResponse<T>(payload: ApiResponse<T> | T): ApiResponse<T> {
  if (payload && typeof payload === 'object' && ('success' in payload || 'isSuccess' in payload || 'data' in payload)) {
    return payload as ApiResponse<T>
  }
  return { success: true, data: payload as T }
}

export async function getSupplierSalesRank(
  dateRange: DateRange,
  topN = 20,
  branchCodes?: string[],
): Promise<ApiResponse<SupplierSalesRank[]>> {
  const response = await request<ApiResponse<SupplierSalesRank[]> | SupplierSalesRank[]>(
    '/api/react/v1/dashboard/supplier-sales-rank',
    {
      method: 'GET',
      params: {
        ...dateRange,
        topN,
        branchCodes,
      },
    },
  )

  return unwrapApiResponse(response)
}

export async function getChinaSupplierSalesRank(
  dateRange: DateRange,
  topN = 20,
  branchCodes?: string[],
): Promise<ApiResponse<ChinaSupplierSalesRank[]>> {
  const response = await request<ApiResponse<ChinaSupplierSalesRank[]> | ChinaSupplierSalesRank[]>(
    '/api/react/v1/dashboard/china-supplier-sales-rank',
    {
      method: 'GET',
      params: {
        ...dateRange,
        topN,
        branchCodes,
      },
    },
  )

  return unwrapApiResponse(response)
}

export async function getEnhancedSalesProductDetails(
  dateRange: DateRange,
  branchCodes?: string[],
  localSupplierCodes?: string[],
  chinaSupplierCodes?: string[],
  pageIndex = 1,
  pageSize = 100,
): Promise<ApiResponse<PagedSalesProductDetailWithDiscount>> {
  const response = await request<
    ApiResponse<PagedSalesProductDetailWithDiscount> | PagedSalesProductDetailWithDiscount
  >('/api/react/v1/dashboard/enhanced-sales-product-details', {
    method: 'GET',
    params: {
      ...dateRange,
      branchCodes,
      localSupplierCodes,
      chinaSupplierCodes,
      pageIndex,
      pageSize,
    },
  })

  return unwrapApiResponse(response)
}

export async function getBranchSalesAggregate(
  dateRange: DateRange,
  compareDateRange?: DateRange,
  branchCodes?: string[],
  supplierCodes?: string[],
): Promise<ApiResponse<BranchSalesAggregate[]>> {
  const response = await request<ApiResponse<BranchSalesAggregate[]> | BranchSalesAggregate[]>(
    '/api/react/v1/dashboard/branch-sales-aggregate',
    {
      method: 'GET',
      params: {
        startDate: dateRange.startDate,
        endDate: dateRange.endDate,
        compareStartDate: compareDateRange?.startDate,
        compareEndDate: compareDateRange?.endDate,
        branchCodes,
        supplierCodes,
      },
    },
  )

  return unwrapApiResponse(response)
}

export async function getWeeklyPerformanceHierarchy(
  dateRange: DateRange,
  branchCodes?: string[],
): Promise<ApiResponse<WeeklyHierarchyData[]>> {
  const response = await request<ApiResponse<WeeklyHierarchyData[]> | WeeklyHierarchyData[]>(
    '/api/react/v1/dashboard/weekly-performance-hierarchy',
    {
      method: 'GET',
      params: {
        ...dateRange,
        branchCodes,
      },
    },
  )

  return unwrapApiResponse(response)
}

export async function getExecutiveBranchPerformance(
  dateRange: DateRange,
  topN = 100,
  branchCodes?: string[],
): Promise<ApiResponse<ExecutiveBranchPerformance[]>> {
  const response = await request<ApiResponse<ExecutiveBranchPerformance[]> | ExecutiveBranchPerformance[]>(
    '/api/react/v1/dashboard/executive-branch-performance',
    {
      method: 'GET',
      params: {
        ...dateRange,
        topN,
        branchCodes,
      },
    },
  )

  return unwrapApiResponse(response)
}

export async function getExecutiveHourlyTraffic(
  dateRange: DateRange,
  branchCodes?: string[],
): Promise<ApiResponse<ExecutiveHourlyTraffic[]>> {
  const response = await request<ApiResponse<ExecutiveHourlyTraffic[]> | ExecutiveHourlyTraffic[]>(
    '/api/react/v1/dashboard/executive-hourly-traffic',
    {
      method: 'GET',
      params: {
        ...dateRange,
        branchCodes,
      },
    },
  )

  return unwrapApiResponse(response)
}
