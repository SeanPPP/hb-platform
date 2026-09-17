import { apiClient } from "@/shared/api/client";
import { normalizeWarehouseProductInsight } from "./api-normalization";
import type { WarehouseInsightRange } from "./types";

/**
 * 查询单个仓库商品的进销数据。
 * range 省略时由后端按澳洲总部业务日推导默认区间，避免设备时区导致日期偏移。
 */
export async function fetchWarehouseProductInsight(
  productCode: string,
  range: WarehouseInsightRange | null,
  signal?: AbortSignal,
) {
  const response = await apiClient.get("/react/v1/warehouse-product-insights", {
    params: { productCode, ...(range ?? {}) },
    signal,
  });
  const result = normalizeWarehouseProductInsight(response.data);
  if (
    result.product.productCode.toLowerCase() !== productCode.toLowerCase() ||
    (range != null &&
      (result.range.startDate !== range.startDate ||
        result.range.endDate !== range.endDate))
  ) {
    // 范围或商品不一致说明响应串台，宁可报错也不能把别的商品的数据显示成当前商品。
    throw Object.assign(new Error("Warehouse insight scope mismatch"), {
      code: "WAREHOUSE_INSIGHT_INVALID_RESPONSE",
    });
  }
  return result;
}
