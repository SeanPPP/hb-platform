import type { StoreOrderProductItem } from "@/modules/shop/types";

export type ScanSource = "camera" | "hid";

export interface ScanFeedbackState {
  status: "ready" | "scanning" | "found" | "added" | "multiple" | "not_found" | "blocked" | "error" | "price_update_required" | "delisted";
  message: string;
  barcode?: string;
  productName?: string;
  /** 已下架命中时展示货号，便于和同名商品区分。 */
  itemNumber?: string;
  addedQuantity?: number;
}

export interface ScanSelectionState {
  barcode: string;
  scanTraceId?: string;
  storeCode?: string | null;
  source: ScanSource;
  items: StoreOrderProductItem[];
}
