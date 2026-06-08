import type { ApiResponse } from '../types/api'
import type {
  BestSellerBranchSale,
  BestSellerProduct,
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
  BestSellerBranchSale,
  BestSellerProduct,
  BranchSalesAggregate,
  ChinaSupplierSalesRank,
  DateRange,
  ExecutiveBranchPerformance,
  ExecutiveHourlyTraffic,
  PagedSalesProductDetailWithDiscount,
  SupplierSalesRank,
  WeeklyHierarchyData,
} from '../types/salesDashboard'

function readNumber(value: unknown, fallback = 0) {
  return typeof value === 'number' && Number.isFinite(value) ? value : fallback
}

function readOptionalNumber(value: unknown) {
  return typeof value === 'number' && Number.isFinite(value) ? value : undefined
}

function readString(value: unknown) {
  return typeof value === 'string' && value.trim() ? value : undefined
}

function normalizeBestSellerBranchSale(raw: unknown): BestSellerBranchSale | null {
  if (!raw || typeof raw !== 'object') {
    return null
  }

  const record = raw as Record<string, unknown>
  const branchCode = readString(record.branchCode ?? record.BranchCode)

  if (!branchCode) {
    return null
  }

  return {
    branchCode,
    branchName: readString(record.branchName ?? record.BranchName),
    quantity: readNumber(record.quantity ?? record.Quantity),
  }
}

function normalizeBestSellerProduct(raw: unknown): BestSellerProduct | null {
  if (!raw || typeof raw !== 'object') {
    return null
  }

  const record = raw as Record<string, unknown>
  const productCode = readString(record.productCode ?? record.ProductCode)

  if (!productCode) {
    return null
  }

  const branchSales = Array.isArray(record.branchSales ?? record.BranchSales)
    ? ((record.branchSales ?? record.BranchSales) as unknown[])
        .map(normalizeBestSellerBranchSale)
        .filter((item): item is BestSellerBranchSale => item !== null)
    : undefined

  return {
    productCode,
    itemNumber: readString(record.itemNumber ?? record.ItemNumber),
    barcode: readString(record.barcode ?? record.Barcode),
    productImage: readString(record.productImage ?? record.ProductImage),
    productName: readString(record.productName ?? record.ProductName),
    quantity: readNumber(record.quantity ?? record.Quantity),
    salesAmount: readNumber(record.salesAmount ?? record.SalesAmount),
    rank: readNumber(record.rank ?? record.Rank),
    isActive: typeof (record.isActive ?? record.IsActive) === 'boolean'
      ? (record.isActive ?? record.IsActive) as boolean
      : undefined,
    minOrderQuantity: readOptionalNumber(record.minOrderQuantity ?? record.MinOrderQuantity),
    branchSalesCount: readOptionalNumber(record.branchSalesCount ?? record.BranchSalesCount),
    branchSales,
  }
}

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
  const products = Array.isArray(result.products ?? (result as Record<string, unknown>).Products)
    ? ((result.products ?? (result as Record<string, unknown>).Products) as unknown[])
        .map(normalizeBestSellerProduct)
        .filter((item): item is BestSellerProduct => item !== null)
    : []

  return {
    products,
    total: readNumber(result.total ?? (result as Record<string, unknown>).Total),
    pageIndex: readNumber(result.pageIndex ?? (result as Record<string, unknown>).PageIndex, 1),
    pageSize: readNumber(result.pageSize ?? (result as Record<string, unknown>).PageSize),
    totalPages: readNumber(result.totalPages ?? (result as Record<string, unknown>).TotalPages),
  }
}

export async function getBestSellers(
  startDate: string,
  endDate: string,
  branchCodes?: string[],
  pageIndex = 1,
  pageSize = 8,
  signal?: AbortSignal,
): Promise<BestSellerResponse> {
  const response = await request<ApiResponse<BestSellerResponse> | BestSellerResponse>(
    '/api/react/v1/dashboard/best-sellers',
    {
      method: 'GET',
      signal,
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
