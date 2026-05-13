import type { ApiResponse, PagedResult } from '../types/api'
import request from '../utils/request'
import { unwrapApiData, unwrapPagedResult } from '../utils/request'
import type {
  ProductGradeListItem,
  ProductGradeListParams,
  ProductGradeListResult,
  CreateProductGradePayload,
  BatchUpdateGradePayload,
  PasteImportGradePayload,
  PasteImportResult,
  ProductGradeBrief,
  BatchUpdateGradePricePayload,
  BatchUpdateGradePriceResult,
} from '../types/productGrade'

const API_BASE = '/api/react/v1/product-grades'

type ApiItem = Record<string, unknown>

function transformGradeItem(raw: ApiItem): ProductGradeListItem {
  return {
    id: String(raw.id ?? raw.Id ?? ''),
    productCode: String(raw.productCode ?? raw.ProductCode ?? ''),
    grade: String(raw.grade ?? raw.Grade ?? ''),
    supplierCode: raw.supplierCode != null ? String(raw.supplierCode) : undefined,
    supplierName: raw.supplierName != null ? String(raw.supplierName) : undefined,
    hbProductNo: raw.hbProductNo != null ? String(raw.hbProductNo) : undefined,
    productName: raw.productName != null ? String(raw.productName) : undefined,
    productImage: raw.productImage != null ? String(raw.productImage) : undefined,
    domesticPrice: raw.domesticPrice != null ? Number(raw.domesticPrice) : undefined,
    importPrice: raw.importPrice != null ? Number(raw.importPrice) : undefined,
    oemPrice: raw.oemPrice != null ? Number(raw.oemPrice) : undefined,
    retailPrice: raw.retailPrice != null ? Number(raw.retailPrice) : undefined,
    barcode: raw.barcode != null ? String(raw.barcode) : undefined,
    createdAt: String(raw.createdAt ?? raw.CreatedAt ?? ''),
    updatedAt: raw.updatedAt != null ? String(raw.updatedAt) : undefined,
    createdBy: raw.createdBy != null ? String(raw.createdBy) : undefined,
    updatedBy: raw.updatedBy != null ? String(raw.updatedBy) : undefined,
  }
}

export async function getProductGradeList(params: ProductGradeListParams): Promise<ProductGradeListResult> {
  const response = await request.get<ApiResponse<PagedResult<ApiItem>>>(API_BASE, {
    params: {
      page: params.page || 1,
      pageSize: params.pageSize || 20,
      search: params.search || undefined,
      grade: params.grade || undefined,
      supplierCode: params.supplierCode || undefined,
      sortField: params.sortField || undefined,
      sortDirection: params.sortDirection || undefined,
    },
  })
  const result = unwrapPagedResult(response)
  return {
    items: result.items.filter((item) => item != null).map(transformGradeItem),
    total: result.total,
    page: result.page,
    pageSize: result.pageSize,
  }
}

export async function createOrUpdateProductGrade(data: CreateProductGradePayload): Promise<ProductGradeListItem> {
  const response = await request.post<ApiResponse<ApiItem>>(API_BASE, data)
  return transformGradeItem(unwrapApiData(response) as ApiItem)
}

export async function batchUpdateGrades(data: BatchUpdateGradePayload): Promise<boolean> {
  const response = await request.put<ApiResponse<boolean>>(`${API_BASE}/batch`, data)
  return unwrapApiData(response)
}

export async function pasteImportGrades(data: PasteImportGradePayload): Promise<PasteImportResult> {
  const response = await request.post<ApiResponse<PasteImportResult>>(`${API_BASE}/paste-import`, data)
  return unwrapApiData(response) as PasteImportResult
}

export async function deleteProductGrade(id: string): Promise<boolean> {
  const response = await request.delete<ApiResponse<boolean>>(`${API_BASE}/${id}`)
  return unwrapApiData(response)
}

export async function getGradesByProductCodes(productCodes: string[]): Promise<ProductGradeBrief[]> {
  const response = await request.post<ApiResponse<ProductGradeBrief[]>>(`${API_BASE}/by-codes`, productCodes)
  return (unwrapApiData(response) as ProductGradeBrief[]) ?? []
}

export async function batchUpdateGradePrices(data: BatchUpdateGradePricePayload): Promise<BatchUpdateGradePriceResult> {
  const response = await request.put<ApiResponse<BatchUpdateGradePriceResult>>(`${API_BASE}/batch-price`, data)
  return unwrapApiData(response) as BatchUpdateGradePriceResult
}
