import type { ProductDetail } from "@/modules/product-maintenance/types";
import type { ProductLabelPrintPayload } from "@/modules/printer/types";
import { resolveLabelPrintPrice } from "./presentation";
import type { StorePriceUpdateTask } from "./types";

export interface PriceUpdateLabelJob {
  kind: "product" | "discount";
  payload: ProductLabelPrintPayload;
}

/**
 * 价格固定取任务里「更新后的价格」，不用详情里的分店价：
 * 改价刚提交时详情可能还是旧缓存，标签必须与任务展示给店员的新价一致。
 * 详情只补等级、供应商等任务里没有的标签字段；取不到详情时仍可用任务字段出标签。
 */
export function buildPriceUpdateLabelJob(
  task: StorePriceUpdateTask,
  detail: ProductDetail | null
): PriceUpdateLabelJob {
  const price = resolveLabelPrintPrice(task);
  return {
    kind: price.discountRate > 0 ? "discount" : "product",
    payload: {
      productName: detail?.productName || task.productName || task.productCode,
      itemNumber: detail?.itemNumber ?? task.itemNumber,
      grade: detail?.grade ?? null,
      supplierName: detail?.localSupplierName ?? null,
      barcode: task.barcode ?? detail?.barcode ?? null,
      retailPrice: price.retailPrice,
      discountRate: price.discountRate > 0 ? price.discountRate : null,
    },
  };
}
