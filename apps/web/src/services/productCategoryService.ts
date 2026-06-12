import type { ApiResponse } from '../types/api'
import type {
  CreateProductCategoryDto,
  ProductCategoryDto,
  UpdateProductCategoryDto,
} from '../types/productCategory'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/product-categories'

export async function getProductCategoryTree(): Promise<ProductCategoryDto[]> {
  const response = await request.get<ApiResponse<ProductCategoryDto[]>>(`${API_BASE}/tree`)
  return unwrapApiData(response) ?? []
}

export async function createProductCategory(dto: CreateProductCategoryDto): Promise<ProductCategoryDto> {
  const response = await request.post<ApiResponse<ProductCategoryDto>>(API_BASE, dto)
  return unwrapApiData(response)
}

export async function updateProductCategory(
  guid: string,
  dto: UpdateProductCategoryDto,
): Promise<ProductCategoryDto> {
  const response = await request.put<ApiResponse<ProductCategoryDto>>(`${API_BASE}/${guid}`, dto)
  return unwrapApiData(response)
}

export async function deleteProductCategory(guid: string): Promise<void> {
  await request.delete(`${API_BASE}/${guid}`)
}
