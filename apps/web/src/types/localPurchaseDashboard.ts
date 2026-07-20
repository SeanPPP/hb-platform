export interface LocalPurchaseMonthAmount {
  month: string
  warehouseAmount: number
  localSupplierAmount: number
  totalAmount: number
  salesAmount: number
}

export interface LocalPurchaseStoreSummary {
  storeCode: string
  storeName: string
  monthlyAmounts: LocalPurchaseMonthAmount[]
  warehouseAmount: number
  localSupplierAmount: number
  totalAmount: number
}

export interface LocalPurchaseDashboardResponse {
  endMonth: string
  months: string[]
  warehouseAmount: number
  localSupplierAmount: number
  totalAmount: number
  stores: LocalPurchaseStoreSummary[]
}

export interface LocalPurchaseSupplierMonthAmount {
  month: string
  amount: number
}

export interface LocalPurchaseSupplierSummary {
  rowKey: string
  sourceCode: string
  sourceType: 'WAREHOUSE_ORDER' | 'LOCAL_SUPPLIER'
  supplierCode?: string
  supplierName: string
  isWarehouse: boolean
  isUnassigned: boolean
  monthlyAmounts: LocalPurchaseSupplierMonthAmount[]
  totalAmount: number
}

export interface LocalPurchaseSupplierDetailResponse {
  storeCode: string
  storeName: string
  endMonth: string
  months: string[]
  warehouseAmount: number
  localSupplierAmount: number
  totalAmount: number
  suppliers: LocalPurchaseSupplierSummary[]
}
