export type PromotionScopeType = "StoreOnly" | "MultiStore" | "Headquarters";

export interface PromotionProductItem {
  id?: string;
  productCode: string;
  unitWeight: number;
}

export interface PromotionStoreItem {
  id?: string;
  storeCode: string;
}

export interface PromotionListItem {
  id: string;
  name: string;
  description?: string;
  effectiveStart: string;
  effectiveEnd: string;
  isEnabled: boolean;
  isExclusive: boolean;
  priority: number;
  applyQuantity: number;
  fixedPrice: number;
  maxApplicationsPerOrder?: number;
  productsCount: number;
  storesCount: number;
  products: PromotionProductItem[];
  stores: PromotionStoreItem[];
  scopeType: PromotionScopeType | null;
  canEditInStoreScope: boolean;
  canCopyToStore: boolean;
}

export type PromotionDetail = PromotionListItem;

export interface PromotionGridQuery {
  storeCode: string;
  keyword?: string;
  page?: number;
  pageSize?: number;
  sortModel?: Array<{ colId: string; sort: "asc" | "desc" }>;
}

export interface PromotionFormValues {
  id?: string;
  name: string;
  description?: string;
  storeCode: string;
  effectiveStart?: string;
  effectiveEnd?: string;
  isEnabled?: boolean;
  isExclusive?: boolean;
  priority?: number;
  applyQuantity?: number;
  fixedPrice?: number;
  maxApplicationsPerOrder?: number | null;
  products?: Array<{
    id?: string;
    productCode?: string;
    unitWeight?: number | string;
  }>;
}

export interface PromotionCopyRequest {
  sourcePromotionId: string;
  storeCode: string;
  name?: string;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  pageNumber: number;
  pageSize: number;
}
