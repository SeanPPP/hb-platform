export const PRODUCT_GRADE_CONFIG = {
  A: { i18nKey: 'gradeA', i18nDescKey: 'gradeADesc', color: '#722ED1', tagColor: 'purple' as const, shopLabel: 'Core', shopTooltip: 'Must-have for all stores, high stock' },
  B: { i18nKey: 'gradeB', i18nDescKey: 'gradeBDesc', color: '#1890FF', tagColor: 'blue' as const, shopLabel: 'Watch', shopTooltip: 'Required for large stores, optional for small' },
  C: { i18nKey: 'gradeC', i18nDescKey: 'gradeCDesc', color: '#FA8C16', tagColor: 'orange' as const, shopLabel: 'Discontinue', shopTooltip: 'Discontinued, warehouse has stock to clear' },
  D: { i18nKey: 'gradeD', i18nDescKey: 'gradeDDesc', color: '#F5222D', tagColor: 'red' as const, shopLabel: 'Clearance', shopTooltip: 'Clearance - No Stock' },
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
  categoryGuid?: string
  categoryName?: string
  categoryChineseName?: string
  domesticPrice?: number
  importPrice?: number
  oemPrice?: number
  retailPrice?: number
  barcode?: string
  warehouseIsActive?: boolean | null
  minOrderQuantity?: number | null
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
  hbProductNo?: string
  categoryGuid?: string
  uncategorizedOnly?: boolean
  warehouseIsActive?: boolean
  domesticPriceMin?: number
  domesticPriceMax?: number
  importPriceMin?: number
  importPriceMax?: number
  oemPriceMin?: number
  oemPriceMax?: number
  sortField?: string
  sortDirection?: 'asc' | 'desc'
  signal?: AbortSignal
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
