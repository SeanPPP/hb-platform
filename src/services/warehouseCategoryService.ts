import request from '../utils/request'

export interface WarehouseCategoryNode {
  categoryGUID: string
  parentGUID?: string
  categoryName: string
  chineseName?: string
  isActive: boolean
  children?: WarehouseCategoryNode[]
}

export async function getCategoryTree(): Promise<WarehouseCategoryNode[]> {
  const response = await request<{
    success?: boolean
    data?: WarehouseCategoryNode[]
  } | WarehouseCategoryNode[]>('/api/react/v1/warehouse-categories/tree')

  if (Array.isArray(response)) {
    return response
  }

  return Array.isArray(response.data) ? response.data : []
}

export async function batchAssignProducts(categoryGuid: string, productCodes: string[]): Promise<void> {
  await request('/api/react/v1/warehouse-categories/' + categoryGuid + '/products/batch-assign', {
    method: 'POST',
    data: {
      CategoryGuid: categoryGuid,
      ProductCodes: productCodes,
    },
  })
}
