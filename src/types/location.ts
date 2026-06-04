export interface LocationProduct {
  productCode?: string
  itemNumber?: string
  productBarcode?: string
  productName?: string
  productImage?: string
}

export interface LocationItem {
  locationGuid: string
  locationCode?: string
  locationBarcode?: string
  status?: number | null
  locationType?: number | null
  products: LocationProduct[]
  updatedAt?: string
  updatedBy?: string
}

export interface LocationFilterParams {
  locationType?: number | null
  isUsed?: boolean | null
  locationCode?: string
  locationBarcode?: string
  updatedBy?: string
  status?: number | null
  pageNumber?: number
  pageSize?: number
  sortBy?: string
  sortDirection?: 'asc' | 'desc'
  filters?: Record<string, string[]>
}

export interface LocationListResponse {
  items: LocationItem[]
  total: number
  pageNumber: number
  pageSize: number
}

export interface CreateLocationParams {
  locationCode: string
  locationBarcode?: string
  locationType?: number | null
  status?: number | null
}

export interface UpdateLocationParams {
  locationCode: string
  locationBarcode?: string
  locationType?: number | null
  status?: number | null
}
