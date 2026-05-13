export const PRODUCT_GRADE_CONFIG = {
  A: { label: 'A级 - 核心商品', color: '#722ED1', tagColor: 'purple' as const, description: '所有分店必上，高库存', shopLabel: 'Core', shopTooltip: 'Must-have for all stores, high stock' },
  B: { label: 'B级 - 观察商品', color: '#1890FF', tagColor: 'blue' as const, description: '大店必上，小店酌情', shopLabel: 'Watch', shopTooltip: 'Required for large stores, optional for small' },
  C: { label: 'C级 - 清库存(有货)', color: '#FA8C16', tagColor: 'orange' as const, description: '超低价，仓库有货可订回帮清库存', shopLabel: 'Discontinue', shopTooltip: 'Discontinued, warehouse has stock to clear' },
  D: { label: 'D级 - 清库存(无货)', color: '#F5222D', tagColor: 'red' as const, description: '仓库无货', shopLabel: 'Clearance', shopTooltip: 'Clearance - No Stock' },
} as const

export type ProductGradeKey = keyof typeof PRODUCT_GRADE_CONFIG

export interface ProductGradeListItem {
  id: string
  productCode: string
  grade: string
  supplierCode?: string
  supplierName?: string
  hbProductNo?: string
  productName?: string
  productImage?: string
  domesticPrice?: number
  importPrice?: number
  oemPrice?: number
  retailPrice?: number
  barcode?: string
  createdAt: string
  updatedAt?: string
  createdBy?: string
  updatedBy?: string
}

export interface ProductGradeListParams {
  page?: number
  pageSize?: number
  search?: string
  grade?: string
  supplierCode?: string
  sortField?: string
  sortDirection?: 'asc' | 'desc'
}

export interface ProductGradeListResult {
  items: ProductGradeListItem[]
  total: number
  page: number
  pageSize: number
}

export interface CreateProductGradePayload {
  productCode: string
  grade: string
}

export interface BatchUpdateGradePayload {
  items: Array<{ productCode: string; grade: string }>
}

export interface PasteImportGradePayload {
  supplierCode: string
  productNumbers: string
  grade: string
}

export interface PasteImportPreviewItem {
  productNumber: string
  matched: boolean
  productCode?: string
  productName?: string
  productImage?: string
  existingGrade?: string
}

export interface PasteImportResult {
  totalCount: number
  matchedCount: number
  createdCount: number
  updatedCount: number
  previewItems: PasteImportPreviewItem[]
}

export interface ProductGradeBrief {
  productCode: string
  grade: string
}

export interface BatchUpdateGradePricePayload {
  productCodes: string[]
  targetDatabase: 'HBweb' | 'HQ'
  importPrice?: number
  oemPrice?: number
}

export interface BatchUpdateGradePriceResult {
  affectedCount: number
  success: boolean
  message?: string
}
