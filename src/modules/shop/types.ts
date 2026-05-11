import type { UserStoreDto } from "@/modules/auth/types";
import type {
  StoreOrderProductItem,
  StoreOrderCartItem,
  StoreOrderCart,
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
  StoreOrderCart,
  StoreOrderProductListResult,
  StoreOrderScanLookupResult,
};

export interface StoreOrderProductQuery {
  storeCode?: string;
  itemNumber?: string;
  productName?: string;
  categoryGUID?: string;
  pageNumber: number;
  pageSize: number;
  sortBy?: "Default" | "PriceAsc" | "PriceDesc" | "Name";
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
