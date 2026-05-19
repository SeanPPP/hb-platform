export interface ProductLookupItem {
  productCode: string;
  productName: string;
  itemNumber?: string | null;
  barcode?: string | null;
  productImage?: string | null;
  matchSource?: string | null;
  matchValue?: string | null;
  productTypeLabel?: string | null;
  grade?: string | null;
}

export interface PricingEvaluationResult {
  rate?: number | null;
  strategySourceLabel?: string | null;
  strategyRuleLabel?: string | null;
}

export interface StorePriceEditable extends PricingEvaluationResult {
  uuid: string;
  storeCode?: string | null;
  storeName?: string | null;
  productCode?: string | null;
  storeProductCode?: string | null;
  supplierCode?: string | null;
  purchasePrice?: number | null;
  retailPrice?: number | null;
  discountRate?: number | null;
  isAutoPricing: boolean;
  isSpecialProduct: boolean;
  isActive: boolean;
}

export interface MultiCodeEditableItem extends PricingEvaluationResult {
  uuid: string;
  storeCode?: string | null;
  productCode?: string | null;
  multiCodeProductCode?: string | null;
  storeMultiCodeProductCode?: string | null;
  barcode?: string | null;
  purchasePrice?: number | null;
  retailPrice?: number | null;
  discountRate?: number | null;
  isAutoPricing: boolean;
  isSpecialProduct: boolean;
  isActive: boolean;
}

export interface ProductSetCodeItem {
  setCodeId: string;
  productCode: string;
  setProductCode: string;
  setItemNumber: string;
  setBarcode?: string | null;
  setPurchasePrice?: number | null;
  setRetailPrice?: number | null;
  setQuantity: number;
  setType: number;
  setTypeDescription?: string | null;
  isActive: boolean;
}

export interface StoreClearancePriceItem {
  uuid: string;
  storeCode?: string | null;
  storeName?: string | null;
  productCode?: string | null;
  clearanceBarcode?: string | null;
  clearancePrice?: number | null;
}

export interface ProductDetail {
  productCode: string;
  productName: string;
  itemNumber?: string | null;
  barcode?: string | null;
  productImage?: string | null;
  productType?: number | null;
  productTypeLabel?: string | null;
  grade?: string | null;
  localSupplierCode?: string | null;
  localSupplierName?: string | null;
  storePrice?: StorePriceEditable | null;
  clearancePrice?: StoreClearancePriceItem | null;
  setCodes: ProductSetCodeItem[];
  multiCodes: MultiCodeEditableItem[];
}

export interface StoreProductLookupRequest {
  keyword: string;
  storeCode?: string | null;
}

export interface UpdateStorePriceRequest {
  purchasePrice?: number | null;
  retailPrice?: number | null;
  discountRate?: number | null;
  isAutoPricing?: boolean;
  isSpecialProduct?: boolean;
  isActive?: boolean;
}

export interface UpdateMultiCodeRequest {
  purchasePrice?: number | null;
  retailPrice?: number | null;
  isAutoPricing?: boolean;
  isSpecialProduct?: boolean;
  isActive?: boolean;
}

export interface EvaluateAutoPricingRequest {
  productCode: string;
  storeCode?: string | null;
  forceAutoPricing?: boolean;
}

export interface EvaluateAutoPricingResult {
  productCode: string;
  storeCode?: string | null;
  storePriceUuid?: string | null;
  currentRetailPrice?: number | null;
  recalculatedRetailPrice?: number | null;
  currentRetailPriceFormatted: string;
  recalculatedRetailPriceFormatted: string;
  discountRate?: number | null;
  isAutoPricing: boolean;
  hasValidPurchasePrice: boolean;
  shouldUpdate: boolean;
}

export interface UpdateProductTypeRequest {
  productType: number;
  storeCode?: string | null;
}

export interface UpdateProductTypeResult {
  productCode: string;
  productType: number;
  productTypeLabel?: string | null;
}
