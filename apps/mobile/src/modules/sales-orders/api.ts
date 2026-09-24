import { apiClient } from "@/shared/api/client";
import {
  normalizeSalesOrderBranchCatalog,
  normalizeSalesOrderDetail,
  normalizeSalesOrderListPage,
} from "./api-normalization";
import type { SalesOrderQueryBody } from "./types";

const API_BASE = "/react/v1/posm-sales-orders";

export async function fetchSalesOrders(body: SalesOrderQueryBody, signal?: AbortSignal) {
  const response = await apiClient.post(`${API_BASE}/mobile-list`, body, { signal });
  const page = normalizeSalesOrderListPage(response.data);
  if (
    page.range.startDate !== body.startDate ||
    page.range.endDate !== body.endDate ||
    page.pageNumber !== body.pageNumber ||
    page.sortDirection !== body.sortDirection
  ) {
    // 区间、页码或排序不一致说明响应串台，宁可报错也不能把别的条件的数据拼进当前列表。
    throw Object.assign(new Error("Sales order list scope mismatch"), {
      code: "SALES_ORDER_INVALID_RESPONSE",
    });
  }
  return page;
}

export async function fetchSalesOrderBranches(signal?: AbortSignal) {
  const response = await apiClient.get(`${API_BASE}/mobile-branches`, { signal });
  return normalizeSalesOrderBranchCatalog(response.data);
}

export async function fetchSalesOrderDetail(orderGuid: string, signal?: AbortSignal) {
  const response = await apiClient.get(
    `${API_BASE}/detail/${encodeURIComponent(orderGuid)}`,
    { signal },
  );
  const detail = normalizeSalesOrderDetail(response.data);
  if (detail.order.orderGuid !== orderGuid) {
    throw Object.assign(new Error("Sales order detail mismatch"), {
      code: "SALES_ORDER_INVALID_RESPONSE",
    });
  }
  return detail;
}
