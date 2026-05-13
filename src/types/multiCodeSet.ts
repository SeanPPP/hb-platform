export interface MulticodeSetItem {
  id?: string
  productCode: string
  setItemNumber?: string
  setBarcode?: string
  setPurchasePrice?: number
  setRetailPrice?: number
  isActive?: boolean
}

export interface GridDataRequest {
  productCode: string
  pageIndex?: number
  pageSize?: number
}

export interface GridDataResponse {
  items: MulticodeSetItem[]
  total: number
}

export interface BatchDeleteRequest {
  ids: string[]
}

export interface BatchUpdatePricesRequest {
  items: { id: string; setPurchasePrice?: number; setRetailPrice?: number }[]
}

export interface BatchUpdateStatusRequest {
  items: { id: string; isActive: boolean }[]
}
