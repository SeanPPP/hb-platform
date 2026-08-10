import type { ApiResponse } from '../types/api'
import type {
  CreateProductCategoryDto,
  ProductCategoryDto,
  UpdateProductCategoryDto,
} from '../types/productCategory'
import request, { unwrapApiData } from '../utils/request'

const API_BASE = '/api/react/v1/product-categories'

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

// 后端可能返回 camelCase/PascalCase/CategoryGuid 等历史变体，统一按别名读取。
function readFirstValue(source: Record<string, unknown>, aliases: string[]): unknown {
  for (const alias of aliases) {
    const value = source[alias]
    if (value !== undefined && value !== null && value !== '') {
      return value
    }
  }
  return undefined
}

// 分类树归一化：兼容 categoryGUID/CategoryGUID/CategoryGuid 与 categoryName/CategoryName，
// 递归处理 children，输出前端统一的 guid/name/parentGuid/sortOrder/isActive。
export function normalizeProductCategoryDto(raw: unknown): ProductCategoryDto {
  if (!isRecord(raw)) {
    return { guid: '', name: '', isActive: true }
  }

  const guid = String(readFirstValue(raw, ['guid', 'categoryGUID', 'CategoryGUID', 'CategoryGuid']) ?? '')
  const name = String(readFirstValue(raw, ['name', 'categoryName', 'CategoryName']) ?? '')
  const parentGuidValue = readFirstValue(raw, ['parentGuid', 'parentGUID', 'ParentGUID'])
  const parentGuid = parentGuidValue === undefined ? undefined : String(parentGuidValue)
  const sortOrderValue = readFirstValue(raw, ['sortOrder', 'SortOrder'])
  const sortOrder = sortOrderValue === undefined ? undefined : Number(sortOrderValue)
  const isActiveValue = readFirstValue(raw, ['isActive', 'IsActive'])
  const isActive = typeof isActiveValue === 'boolean' ? isActiveValue : true
  const childrenValue = readFirstValue(raw, ['children', 'Children'])
  const children = Array.isArray(childrenValue) ? childrenValue.map(normalizeProductCategoryDto) : undefined

  const normalized: ProductCategoryDto = { guid, name, isActive }
  // 只写有值的可选字段，避免响应中出现 undefined 键。
  if (parentGuid !== undefined) normalized.parentGuid = parentGuid
  if (sortOrder !== undefined) normalized.sortOrder = sortOrder
  if (children !== undefined) normalized.children = children
  return normalized
}

export function normalizeProductCategoryTree(raw: unknown[]): ProductCategoryDto[] {
  return raw.map(normalizeProductCategoryDto)
}

export async function getProductCategoryTree(): Promise<ProductCategoryDto[]> {
  const response = await request.get<ApiResponse<ProductCategoryDto[]>>(`${API_BASE}/tree`)
  const data = unwrapApiData(response)
  return Array.isArray(data) ? normalizeProductCategoryTree(data) : []
}

export async function createProductCategory(dto: CreateProductCategoryDto): Promise<ProductCategoryDto> {
  // 后端 CreateProductCategoryDto 需要 categoryName/parentGUID/sortOrder/isActive 字段名。
  const response = await request.post<ApiResponse<unknown>>(API_BASE, {
    categoryName: dto.name,
    parentGUID: dto.parentGuid,
    sortOrder: dto.sortOrder,
    isActive: dto.isActive ?? true,
  })
  return normalizeProductCategoryDto(unwrapApiData(response))
}

export async function updateProductCategory(
  guid: string,
  dto: UpdateProductCategoryDto,
): Promise<ProductCategoryDto> {
  // 后端 UpdateProductCategoryDto 的 IsActive 缺省为 true，因此调用方必须显式保留当前状态。
  const response = await request.put<ApiResponse<unknown>>(`${API_BASE}/${guid}`, {
    categoryGUID: guid,
    categoryName: dto.name,
    parentGUID: dto.parentGuid,
    sortOrder: dto.sortOrder,
    isActive: dto.isActive,
  })
  return normalizeProductCategoryDto(unwrapApiData(response))
}

export async function deleteProductCategory(guid: string): Promise<void> {
  await request.delete(`${API_BASE}/${guid}`)
}
