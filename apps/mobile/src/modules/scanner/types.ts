import type { StoreOrderProductItem } from "@/modules/shop/types";

export type ScanSource = "camera" | "hid";

export interface ScanFeedbackState {
  status: "ready" | "scanning" | "found" | "added" | "multiple" | "not_found" | "blocked" | "error" | "price_update_required";
  message: string;
  barcode?: string;
  productName?: string;
  addedQuantity?: number;
  /** 未找到的原因是仓库暂停供货（而非扫错码）；首页据此把条码转成搜索词展示恢复计划。 */
  pausedSupply?: boolean;
}

export interface ScanSelectionState {
  barcode: string;
  scanTraceId?: string;
  storeCode?: string | null;
  source: ScanSource;
  items: StoreOrderProductItem[];
}
