export interface PromotionListDto {
  id: string
  name: string
  effectiveStart: string
  effectiveEnd: string
  isEnabled: boolean
  isExclusive: boolean
  priority: number
  applyQuantity: number
  fixedPrice: number
  productsCount: number
  storesCount: number
}

export interface PromotionProductItemDto {
  id: string
  productCode: string
  unitWeight: number
}

export interface PromotionStoreItemDto {
  id: string
  storeCode: string
}

export interface PromotionDetailDto {
  id: string
  name: string
  description?: string
  effectiveStart: string
  effectiveEnd: string
  isEnabled: boolean
  isExclusive: boolean
  priority: number
  applyQuantity: number
  fixedPrice: number
  maxApplicationsPerOrder?: number
  products: PromotionProductItemDto[]
  stores: PromotionStoreItemDto[]
}

export interface CreatePromotionDto {
  name: string
  description?: string
  effectiveStart: string
  effectiveEnd: string
  isEnabled: boolean
  isExclusive: boolean
  priority: number
  applyQuantity: number
  fixedPrice: number
  maxApplicationsPerOrder?: number
  products: PromotionProductItemDto[]
  stores: PromotionStoreItemDto[]
}

export type UpdatePromotionDto = CreatePromotionDto

export interface CartItemInputDto {
  productCode: string
  qty: number
  unitPrice: number
}

export interface PromotionEvaluateRequest {
  storeCode: string
  items: CartItemInputDto[]
}

export interface AppliedPromotionInfo {
  promotionId: string
  appliedBundles: number
}

export interface PriceAdjustmentDto {
  productCode: string
  qtyAdjusted: number
  adjustedUnitPrice: number
}

export interface PromotionEvaluateResponse {
  appliedPromotions: AppliedPromotionInfo[]
  adjustedItems: PriceAdjustmentDto[]
  totalDiscount: number
}
