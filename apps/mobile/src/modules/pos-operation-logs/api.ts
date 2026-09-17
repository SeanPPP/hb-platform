import { apiClient } from "@/shared/api/client";
import type { PosOperationLogQueryParams } from "./logic";
import { POS_OPERATION_LOG_PAGE_SIZE } from "./logic";
import {
  normalizePosOperationLogDetail,
  normalizePosOperationLogPage,
  normalizePosOperationLogSummary,
} from "./api-normalization";
import type {
  PosOperationLogDetail,
  PosOperationLogPage,
  PosOperationLogSummary,
} from "./types";

const BASE_URL = "/react/pos-operation-audits";

export async function fetchPosOperationLogs(
  params: PosOperationLogQueryParams,
  pageNumber: number,
  signal?: AbortSignal,
): Promise<PosOperationLogPage> {
  const response = await apiClient.get(BASE_URL, {
    params: { ...params, pageNumber, pageSize: POS_OPERATION_LOG_PAGE_SIZE },
    signal,
  });
  return normalizePosOperationLogPage(response.data);
}

/** 汇总接口忽略 outcome / 紧急覆盖 / 离线缓存三个参数，计数反映快捷过滤前的基数。 */
export async function fetchPosOperationLogSummary(
  params: PosOperationLogQueryParams,
  signal?: AbortSignal,
): Promise<PosOperationLogSummary> {
  const response = await apiClient.get(`${BASE_URL}/summary`, { params, signal });
  return normalizePosOperationLogSummary(response.data);
}

export async function fetchPosOperationLogDetail(
  eventId: string,
  signal?: AbortSignal,
): Promise<PosOperationLogDetail> {
  const response = await apiClient.get(`${BASE_URL}/${encodeURIComponent(eventId)}`, { signal });
  const detail = normalizePosOperationLogDetail(response.data);
  if (detail.eventId.toLowerCase() !== eventId.toLowerCase()) {
    // 事件串台会把别人的操作显示在当前详情页，宁可报错。
    throw Object.assign(new Error("Operation audit detail mismatch"), {
      code: "POS_OPERATION_LOG_INVALID_RESPONSE",
    });
  }
  return detail;
}
