export type OperationAuditOutcome = 'Succeeded' | 'Denied' | 'Failed'
export type OperationAuditSortField =
  | 'occurredAtUtc'
  | 'storeCode'
  | 'operationType'
  | 'amountDelta'
  | 'deviceCode'
  | 'outcome'
export type OperationAuditSortOrder = 'asc' | 'desc'

export interface OperationAuditQueryParams {
  fromUtc: string
  toUtc: string
  storeCode?: string
  cashierKeyword?: string
  deviceCode?: string
  operationType?: string
  outcome?: string
  productKeyword?: string
  orderGuid?: string
  keyword?: string
  pageNumber: number
  pageSize: number
  sortBy?: OperationAuditSortField
  sortOrder?: OperationAuditSortOrder
}

export interface OperationAuditListItem {
  eventId: string
  schemaVersion: number
  occurredAtUtc: string
  receivedAtUtc: string
  operationType: string
  outcome: OperationAuditOutcome
  cashierId?: string
  userGuid?: string
  cashierName?: string
  isOfflineCached: boolean
  isEmergencyOverride: boolean
  storeCode: string
  deviceCode: string
  appVersion?: string
  instanceId?: string
  orderGuid?: string
  receiptNumber?: string
  correlationId?: string
  traceId?: string
  paymentMethod?: string
  reasonCode?: string
  safeMessage?: string
  currencyCode: string
  paymentAmount?: number
  beforeGross?: number
  afterGross?: number
  beforeDiscount?: number
  afterDiscount?: number
  beforeActual?: number
  afterActual?: number
  amountDelta?: number
  productCount: number
  primaryProduct?: string
}

export interface OperationAuditDetailItem {
  eventId: string
  lineIndex: number
  productCode?: string
  itemNumber?: string
  referenceCode?: string
  lookupCode?: string
  displayName?: string
  lineKind?: string
  beforeQuantity?: number
  afterQuantity?: number
  quantityDelta?: number
  beforeUnitPrice?: number
  afterUnitPrice?: number
  unitPriceDelta?: number
  beforeDiscountAmount?: number
  afterDiscountAmount?: number
  discountAmountDelta?: number
  beforeGrossAmount?: number
  afterGrossAmount?: number
  grossAmountDelta?: number
  beforeActualAmount?: number
  afterActualAmount?: number
  actualAmountDelta?: number
}

export interface OperationAuditDetail extends OperationAuditListItem {
  propertiesJson?: string
  items: OperationAuditDetailItem[]
}
