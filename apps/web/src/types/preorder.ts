export type PreorderActivationStatus = 'Scheduled' | 'Active' | 'Closed' | 'Cancelled'
export type PreorderOrderStatus = 'Draft' | 'ReturnedForRevision' | 'Submitted' | 'NoDemand' | 'Processing' | 'Completed' | 'Cancelled'

export interface PreorderTemplateItem {
  productCode: string
  itemNumber: string
  productName: string
  productImage?: string
  importPrice: number
  retailPrice: number
  minimumOrderQuantity: number
  sortOrder: number
}

export interface PreorderTemplateStore {
  storeGuid: string
  storeCode: string
  storeName: string
}

export interface PreorderTemplateSummary {
  templateGuid: string
  name: string
  isEnabled: boolean
  revision: number
  notes?: string
  itemCount: number
  storeCount: number
  activationCount: number
  latestActivationAt?: string
  updatedAt?: string
}

export interface PreorderTemplateDetail extends PreorderTemplateSummary {
  items: PreorderTemplateItem[]
  stores: PreorderTemplateStore[]
}

export interface PreorderTemplatePayload {
  name: string
  isEnabled: boolean
  notes?: string
  expectedRevision?: number
  storeGuids: string[]
  items: Array<{ productCode: string; minimumOrderQuantity: number; sortOrder: number }>
}

export interface PreorderPasteRow {
  lineNumber: number
  itemNumber: string
  minimumOrderQuantity: number
}

export interface PreorderResolvedItem extends PreorderPasteRow {
  valid: boolean
  errorCode?: string
  message?: string
  productCode?: string
  productName?: string
  productImage?: string
  importPrice?: number
  retailPrice?: number
}

export interface PreorderActivationSummary {
  activationGuid: string
  templateGuid: string
  templateName: string
  sequenceNumber: number
  activationNumber: string
  startAtUtc: string
  endAtUtc: string
  status: PreorderActivationStatus
  targetStoreCount: number
  submittedCount: number
  noDemandCount: number
  pendingCount: number
  cancelledCount: number
}

export interface PreorderActivationItem extends PreorderTemplateItem {
  activationItemGuid: string
  packCount: number
  orderedQuantity: number
}

export interface PreorderActivationDetail extends PreorderActivationSummary {
  sourceTemplateRevision: number
  items: PreorderActivationItem[]
  stores: PreorderTemplateStore[]
  draftRevision: number
  orderStatus?: PreorderOrderStatus
  warehouseNotes?: string
}

export interface PreorderActiveResult {
  normalOrderBlocked: boolean
  activations: PreorderActivationSummary[]
}

export interface PreorderDraftPayload {
  storeCode: string
  expectedDraftRevision: number
  items: Array<{ activationItemGuid: string; packCount: number }>
}

export interface PreorderSubmitPayload extends PreorderDraftPayload {
  confirmNoDemand: boolean
}

export interface PreorderWarehouseOrderSummary {
  orderGuid: string
  orderNo: string
  storeCode: string
  storeName: string
  status: PreorderOrderStatus
  draftRevision: number
  submittedBy?: string
  submittedAt?: string
  skuCount: number
  totalPackCount: number
  totalQuantity: number
  totalImportAmount: number
  totalRetailAmount: number
  warehouseNotes?: string
}

export interface PreorderProductStatistic {
  activationItemGuid: string
  productCode: string
  itemNumber: string
  productName: string
  minimumOrderQuantity: number
  orderedStoreCount: number
  totalPackCount: number
  totalQuantity: number
  totalImportAmount: number
  totalRetailAmount: number
}

export interface PreorderActivationStatistics {
  activation?: PreorderActivationSummary
  targetStoreCount?: number
  submittedCount?: number
  noDemandCount?: number
  processingCount?: number
  completedCount?: number
  cancelledCount?: number
  pendingCount?: number
  products: PreorderProductStatistic[]
  orders: PreorderWarehouseOrderSummary[]
  pendingStores: PreorderTemplateStore[]
  storeProductQuantities: PreorderStoreProductQuantity[]
}

export interface PreorderStoreProductQuantity {
  storeGuid: string
  storeCode: string
  storeName: string
  orderStatus: PreorderOrderStatus
  activationItemGuid: string
  productCode: string
  packCount: number
  orderedQuantity: number
}

export interface PreorderMatrixCell {
  storeCode: string
  storeName: string
  packCount: number
  quantity: number
}

export interface PreorderMatrixRow {
  activationItemGuid: string
  productCode: string
  itemNumber: string
  productName: string
  minimumOrderQuantity: number
  storeCells: ReadonlyMap<string, PreorderMatrixCell>
}

export interface PreorderActivationPayload {
  expectedRevision: number
  startAtUtc: string
  endAtUtc: string
  storeGuids: string[]
}
