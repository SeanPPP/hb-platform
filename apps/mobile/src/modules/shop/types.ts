import type { UserStoreDto } from "@/modules/auth/types";
import type {
  StoreOrderProductItem,
  StoreOrderCartItem,
  StoreOrderCart as BaseStoreOrderCart,
  StoreOrderProductListResult,
  StoreOrderScanLookupResult,
} from "@/modules/orders/types";

export const STORE_SELECTION_STORAGE_KEY = "shop:selectedStoreCode";

export interface Store extends UserStoreDto {
  storeCode: string;
  storeName: string;
}

export type {
  StoreOrderProductItem,
  StoreOrderCartItem,
  StoreOrderProductListResult,
  StoreOrderScanLookupResult,
};

export interface StoreOrderCart extends BaseStoreOrderCart {
  totalSku: number;
  importTotal?: number;
  importTotalAmount?: number;
}

export interface StoreOrderCartMutationSummary {
  orderGUID: string;
  storeCode?: string;
  totalAmount: number;
  totalImportAmount: number;
  totalQuantity: number;
  totalSku: number;
}

export interface StoreOrderCartMutationResult {
  summary: StoreOrderCartMutationSummary;
  changedItem: StoreOrderCartItem | null;
  productCode: string;
  removed: boolean;
}

export interface StoreOrderScanLookupAddResult {
  barcode: string;
  matchType?: string;
  items: StoreOrderProductItem[];
  added: boolean;
  cart: StoreOrderCartMutationResult | null;
}

export interface StoreOrderProductQuery {
  storeCode?: string;
  itemNumber?: string;
  productName?: string;
  categoryGUID?: string;
  grade?: string;
  pageNumber: number;
  pageSize: number;
  sortBy?: "Default" | "PriceAsc" | "PriceDesc" | "Name";
}

export interface StoreOrderProductGradeOption {
  grade: string;
  label: string;
  value: string;
}

export interface StoreOrderDynamicData {
  productCode: string;
  lastOrderDate?: string;
  lastQuantity?: number;
  lastAllocQuantity?: number;
  cartQuantity: number;
}

export interface StoreOrderDynamicDataRequest {
  storeCode: string;
  productCodes: string[];
}

export interface StoreOrderCategoryNode {
  categoryGUID: string;
  categoryName: string;
  children?: StoreOrderCategoryNode[];
}

export interface AddToCartPayload {
  storeCode: string;
  productCode: string;
  quantity: number;
  importPrice?: number;
}

export interface UpdateCartQuantityPayload {
  storeCode: string;
  productCode: string;
  quantity: number;
  importPrice?: number;
}

export type ProductDynamicDataMap = Record<string, StoreOrderDynamicData>;
