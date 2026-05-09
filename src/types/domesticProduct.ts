export enum ProductType {
  NORMAL = 0,
  SET = 1,
  MULTICODE = 2,
}

export const ProductTypeLabels: Record<ProductType, string> = {
  [ProductType.NORMAL]: '普通商品',
  [ProductType.SET]: '套装商品',
  [ProductType.MULTICODE]: '多码商品',
}

export interface SupplierOption {
  code: string
  name: string
  shopNumber?: string
  contactPerson?: string
  phone?: string
}

export interface DomesticProductSetItem {
  id: string
  setProductCode?: string
  productCode?: string
  productName?: string
  setProductNo?: string
  setBarcode?: string
  domesticPrice?: number
  importPrice?: number
  oemPrice?: number
}

export interface DomesticProductItem {
  id: string
  rowNumber?: number
  supplierCode: string
  supplierName: string
  name: string
  nameEn?: string
  itemNumber: string
  barcode?: string
  specs?: string
  productImage?: string
  productType: ProductType
  domesticPrice?: number
  labelPrice?: number
  importPrice?: number
  packingQty?: number
  volume?: number
  middlePackQty?: number
  packingSize?: string
  material?: string
  remark?: string
  isActive: boolean
  createdAt: string
  updatedAt?: string
  createdBy?: string
  updatedBy?: string
  setItems?: DomesticProductSetItem[]
}

export interface DomesticProductGridQuery {
  page: number
  pageSize: number
  searchText?: string
  supplierCode?: string
  productType?: ProductType
  isActive?: boolean
  sortField?: string
  sortOrder?: 'ascend' | 'descend'
}

export interface DomesticProductGridResult {
  items: DomesticProductItem[]
  total: number
  page: number
  pageSize: number
}

export interface CreateDomesticProductPayload {
  supplierCode: string
  productName: string
  englishProductName?: string
  hbProductNo?: string
  barcode?: string
  productSpecification?: string
  productType: ProductType
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
  isActive?: boolean
}

export interface UpdateDomesticProductPayload {
  productName?: string
  englishProductName?: string
  barcode?: string
  productSpecification?: string
  productType?: ProductType
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
  isActive?: boolean
}
