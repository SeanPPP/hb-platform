export enum ProductCreationType {
  NORMAL = 0,
  SET = 1,
  SET_SUB_ITEM = 2,
}

export interface BatchInfo {
  batchNumber: string
  supplierCode: string
  supplierName: string
  prefixCode?: string
  normalCount: number
  setCount: number
  totalCount: number
  createdAt: string
  createdBy?: string
}

export interface BatchProductItem {
  itemNumber: string
  hbProductNo: string
  barcode: string
  productName: string
  productType: ProductCreationType
  privateLabelPrice?: number
  setQuantity?: number
  setPrice?: number
  parentItemNumber?: string
  subItems?: BatchProductItem[]
}

export interface BatchDetail {
  batchNumber: string
  supplierCode: string
  supplierName: string
  prefixCode?: string
  normalCount: number
  setCount: number
  totalCount: number
  createdAt: string
  createdBy?: string
  items: BatchProductItem[]
}

export interface CreateBatchRequest {
  supplierCode: string
  prefixCode?: string
  prefixName?: string
  items: Array<{
    productName: string
    productType: ProductCreationType
    privateLabelPrice?: number
    setQuantity?: number
    setPrice?: number
    parentItemNumber?: string
  }>
}

export interface UpdatePriceItem {
  itemNumber: string
  privateLabelPrice?: number
}

export interface BatchListParams {
  page?: number
  pageSize?: number
  supplierCode?: string
  startDate?: string
  endDate?: string
}

export interface PrefixCodeListParams {
  page?: number
  pageSize?: number
  search?: string
  supplierCode?: string
  isActive?: boolean
}

export interface PrefixCodeItem {
  prefixCode: string
  supplierCode: string
  supplierName?: string
  prefixName: string
  prefixDescription?: string
  isActive: boolean
  sortOrder?: number
  createdAt: string
}

export interface PrefixCodeResponse {
  success: boolean
  data: {
    items: PrefixCodeItem[]
    total: number
  }
  message?: string
}
