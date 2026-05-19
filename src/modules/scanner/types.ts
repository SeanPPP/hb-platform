import type { StoreOrderProductItem } from "@/modules/shop/types";

export type ScanSource = "camera" | "hid";

export interface ScanFeedbackState {
  status: "ready" | "scanning" | "found" | "added" | "multiple" | "not_found" | "blocked" | "error" | "price_update_required";
  message: string;
  barcode?: string;
  productName?: string;
  addedQuantity?: number;
}

export interface ScanSelectionState {
  barcode: string;
  source: ScanSource;
  items: StoreOrderProductItem[];
}
