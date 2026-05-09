import type { ProductType } from './domesticProduct'

export interface ProductPrefixCodeItem {
  prefixCode: string
  supplierCode: string
  supplierName?: string
  prefixName: string
  prefixDescription?: string
  isActive: boolean
  sortOrder?: number
  createdAt?: string
  updatedAt?: string
}

export interface PrefixCodeListParams {
  page?: number
  pageSize?: number
  search?: string
  supplierCode?: string
  isActive?: boolean
  sortField?: string
  sortDirection?: 'asc' | 'desc'
}

export interface PrefixCodeListResult {
  items: ProductPrefixCodeItem[]
  total: number
  page: number
  pageSize: number
}

export interface SavePrefixCodePayload {
  supplierCode?: string
  prefixName: string
  prefixDescription?: string
  isActive?: boolean
  sortOrder?: number
}

export interface PrefixCodeProductItem {
  productCode: string
  supplierCode?: string
  supplierName?: string
  productName?: string
  englishProductName?: string
  hbProductNo?: string
  barcode?: string
  productSpecification?: string
  productType: ProductType
  productTypeName?: string
  domesticPrice?: number
  oemPrice?: number
  importPrice?: number
  packingQuantity?: number
  unitVolume?: number
  middlePackQuantity?: number
  packingSize?: string
  material?: string
  remarks?: string
  productImage?: string
  isActive: boolean
  createdAt?: string
}

export interface PrefixCodeProductsResult {
  items: PrefixCodeProductItem[]
  total: number
  page: number
  pageSize: number
}
