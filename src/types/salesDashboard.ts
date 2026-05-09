export interface BestSellerProduct {
  productCode: string
  itemNumber?: string
  productImage?: string
  productName?: string
  quantity: number
  salesAmount: number
  rank: number
}

export interface BestSellerResponse {
  products: BestSellerProduct[]
  total: number
  pageIndex: number
  pageSize: number
  totalPages: number
}
