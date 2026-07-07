import type { AxiosResponse } from "axios";
import { apiClient } from "@/shared/api/client";
import type {
  AlignDomesticProductCodeRequest,
  AlignDomesticProductCodeResult,
  ContainerDetailBatchActionResult,
  ContainerDetailBatchScope,
  ContainerDetailQuery,
  ContainerDetailQueryResult,
  ContainerExportRequest,
  ContainerExportResult,
  ContainerJob,
  ContainerListResponse,
  ContainerMain,
  ContainerQueryRequest,
  CreateContainerRequest,
  PushProductsToHqJob,
  PushProductsToHqJobRequest,
  SyncResult,
  UpdateContainerDetailRequest,
  UpdateContainerRequest,
} from "./types";
import {
  buildAlignDomesticProductCodePayload,
  buildContainerListPayload,
  normalizeCreateContainerResponse,
  normalizeAlignDomesticProductCodeResult,
  normalizeContainerDetailResponse,
  normalizeContainerDetailQueryResult,
  normalizeContainerJob,
  normalizeContainerListResponse,
  normalizePushProductsToHqJob,
  normalizeSyncResult,
  unwrapData,
} from "./query";

const CONTAINERS_PATH = "/react/v1/containers";
const CONTAINER_PRODUCTS_PATH = "/react/v1/container-products";
const PRODUCTS_PATH = "/react/v1/products";

type ExportData = ArrayBuffer | ArrayBufferView | Blob | string;

function ensureSuccess(data: unknown, fallbackMessage: string) {
  if (
    data &&
    typeof data === "object" &&
    ("success" in data || "isSuccess" in data)
  ) {
    const record = data as Record<string, unknown>;
    if (record.success === false || record.isSuccess === false) {
      throw new Error(typeof record.message === "string" ? record.message : fallbackMessage);
    }
  }
}

function pickNumber(data: unknown, key: string, fallback: number) {
  const payload = unwrapData(data);
  if (!payload || typeof payload !== "object") {
    return fallback;
  }
  const value = (payload as Record<string, unknown>)[key];
  const parsed = typeof value === "string" ? Number(value) : value;
  return typeof parsed === "number" && Number.isFinite(parsed) ? parsed : fallback;
}

function mapDetailUpdate(item: UpdateContainerDetailRequest) {
  return {
    HGUID: item.hguid,
    调整浮率: item.调整浮率,
    国内价格: item.国内价格,
    进口价格: item.进口价格,
    运输成本: item.运输成本,
    商品名称: item.商品名称,
    英文名称: item.英文名称,
    ClearEnglishName: item.ClearEnglishName,
    贴牌价格: item.贴牌价格,
    单件装箱数: item.单件装箱数,
    中包数: item.中包数,
    单件体积: item.单件体积,
    装柜数量: item.装柜数量,
    合计装柜体积: item.合计装柜体积,
    合计装柜金额: item.合计装柜金额,
    IsActive: item.IsActive,
    SkipRelatedProductSync: item.SkipRelatedProductSync,
  };
}

async function toBase64(data: ExportData) {
  const { fromByteArray } = await import("base64-js");
  if (data instanceof ArrayBuffer) {
    return fromByteArray(new Uint8Array(data));
  }
  if (ArrayBuffer.isView(data)) {
    return fromByteArray(new Uint8Array(data.buffer, data.byteOffset, data.byteLength));
  }
  if (typeof Blob !== "undefined" && data instanceof Blob) {
    return fromByteArray(new Uint8Array(await data.arrayBuffer()));
  }
  return typeof data === "string" ? data : "";
}

function getHeader(response: AxiosResponse, name: string) {
  const headers = response.headers as Record<string, unknown>;
  const value = headers[name] ?? headers[name.toLowerCase()];
  return Array.isArray(value) ? String(value[0] ?? "") : String(value ?? "");
}

function getFileNameFromDisposition(disposition: string, fallback: string) {
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1];
  if (encoded) {
    return decodeURIComponent(encoded);
  }
  return /filename="?([^";]+)"?/i.exec(disposition)?.[1] ?? fallback;
}

async function writeAndShareExport(
  response: AxiosResponse<ExportData>,
  fallbackFileName: string,
  fallbackContentType: string,
): Promise<ContainerExportResult> {
  const FileSystem = await import("expo-file-system/legacy");
  const Sharing = await import("expo-sharing");
  const fileName = getFileNameFromDisposition(
    getHeader(response, "content-disposition"),
    fallbackFileName,
  );
  const contentType = getHeader(response, "content-type") || fallbackContentType;
  const base64 = await toBase64(response.data);
  if (!base64) {
    throw new Error("导出文件为空");
  }

  const fileUri = `${FileSystem.documentDirectory ?? ""}${fileName}`;
  // 导出接口返回二进制；本地分享前必须转 Base64 写入 Expo 文件系统。
  await FileSystem.writeAsStringAsync(fileUri, base64, {
    encoding: FileSystem.EncodingType.Base64,
  });

  if (await Sharing.isAvailableAsync()) {
    await Sharing.shareAsync(fileUri, {
      mimeType: contentType,
      dialogTitle: fileName,
    });
  }

  return { fileUri, fileName, contentType };
}

async function postContainerAction(
  containerGuid: string,
  action: string,
  body: object,
) {
  const response = await apiClient.post<ContainerDetailBatchActionResult>(
    `${CONTAINERS_PATH}/${encodeURIComponent(containerGuid)}/actions/${action}`,
    body,
  );
  return {
    totalUpdated: pickNumber(response.data, "totalUpdated", 0),
    totalRequested: pickNumber(response.data, "totalRequested", 0),
  };
}

export async function getContainerList(query: ContainerQueryRequest = {}): Promise<ContainerListResponse> {
  const response = await apiClient.post(`${CONTAINERS_PATH}/list`, buildContainerListPayload(query));
  ensureSuccess(response.data, "获取货柜列表失败");
  return normalizeContainerListResponse(response.data, query);
}

export async function getContainerDetail(containerGuid: string): Promise<ContainerMain> {
  const response = await apiClient.get<unknown>(`${CONTAINERS_PATH}/${encodeURIComponent(containerGuid)}`);
  ensureSuccess(response.data, "获取货柜详情失败");
  return normalizeContainerDetailResponse(response.data);
}

export async function queryContainerProducts(
  containerGuid: string,
  query: ContainerDetailQuery,
): Promise<ContainerDetailQueryResult> {
  const response = await apiClient.post(
    `${CONTAINERS_PATH}/${encodeURIComponent(containerGuid)}/products/query`,
    {
      ...query,
      containerGuid,
    },
  );
  ensureSuccess(response.data, "查询货柜明细失败");
  return normalizeContainerDetailQueryResult(response.data, query);
}

export async function createContainer(data: CreateContainerRequest): Promise<string> {
  const response = await apiClient.post<unknown>(CONTAINERS_PATH, data);
  ensureSuccess(response.data, "创建货柜失败");
  return normalizeCreateContainerResponse(response.data);
}

export async function updateContainer(containerGuid: string, data: UpdateContainerRequest) {
  const response = await apiClient.put(`${CONTAINERS_PATH}/${encodeURIComponent(containerGuid)}`, data);
  ensureSuccess(response.data, "更新货柜失败");
  return true;
}

export async function batchUpdateDetails(updates: UpdateContainerDetailRequest[]) {
  const response = await apiClient.post(`${CONTAINERS_PATH}/batch-update-details`, updates.map(mapDetailUpdate));
  ensureSuccess(response.data, "批量更新货柜明细失败");
  return {
    totalUpdated: pickNumber(response.data, "totalUpdated", updates.length),
    totalRequested: pickNumber(response.data, "totalRequested", updates.length),
  };
}

export async function batchDeleteDetails(hguids: string[]) {
  const response = await apiClient.post(`${CONTAINERS_PATH}/batch-delete-details`, { hguids });
  ensureSuccess(response.data, "批量删除货柜明细失败");
  return {
    totalDeleted: pickNumber(response.data, "totalDeleted", hguids.length),
    totalRequested: pickNumber(response.data, "totalRequested", hguids.length),
  };
}

export async function applyFloatRate(
  containerGuid: string,
  scope: ContainerDetailBatchScope,
  floatRate: number,
) {
  return postContainerAction(containerGuid, "apply-float-rate", { ...scope, floatRate });
}

export async function applyPrices(
  containerGuid: string,
  scope: ContainerDetailBatchScope,
  prices: { importPrice?: number | null; oemPrice?: number | null },
) {
  return postContainerAction(containerGuid, "apply-prices", { ...scope, ...prices });
}

export async function recalculate(containerGuid: string, scope: ContainerDetailBatchScope) {
  return postContainerAction(containerGuid, "recalculate-costs", scope);
}

export async function backfill(containerGuid: string, scope: ContainerDetailBatchScope) {
  return postContainerAction(containerGuid, "backfill-last-prices", scope);
}

export async function syncContainersFromHq(startDate?: string): Promise<SyncResult> {
  const response = await apiClient.post(`${CONTAINERS_PATH}/sync-from-hq`, { startDate: startDate || undefined });
  ensureSuccess(response.data, "从 HQ 同步货柜失败");
  return normalizeSyncResult(response.data);
}

export async function pushContainersToHbSales(containerGuids: string[]): Promise<SyncResult> {
  const response = await apiClient.post(`${CONTAINERS_PATH}/push-to-hbsales`, { containerGuids });
  ensureSuccess(response.data, "推送 HBSales 失败");
  return normalizeSyncResult(response.data);
}

export async function createProductCreationJob(data: {
  operationId: string;
  containerGuid: string;
  detailHguids: string[];
}): Promise<ContainerJob> {
  const response = await apiClient.post(`${CONTAINER_PRODUCTS_PATH}/create-new-products/jobs`, data);
  ensureSuccess(response.data, "创建新商品任务失败");
  return normalizeContainerJob(response.data);
}

export async function getJob(jobId: string): Promise<ContainerJob> {
  const response = await apiClient.get(`${CONTAINER_PRODUCTS_PATH}/create-new-products/jobs/${encodeURIComponent(jobId)}`);
  ensureSuccess(response.data, "查询货柜任务失败");
  return normalizeContainerJob(response.data, jobId);
}

export async function wait(jobId: string, options: { pollIntervalMs?: number; timeoutMs?: number } = {}) {
  const pollIntervalMs = options.pollIntervalMs ?? 2000;
  const timeoutMs = options.timeoutMs ?? 10 * 60 * 1000;
  const startedAt = Date.now();

  while (Date.now() - startedAt <= timeoutMs) {
    const job = await getJob(jobId);
    if (job.status === "Succeeded" || job.status === "Failed") {
      return job;
    }
    await new Promise((resolve) => setTimeout(resolve, pollIntervalMs));
  }
  throw new Error("货柜任务轮询超时");
}

export async function createSubmitJob(data: { operationId: string; containerGuid: string }): Promise<ContainerJob> {
  const response = await apiClient.post(`${CONTAINER_PRODUCTS_PATH}/submit-container/jobs`, {
    ...data,
    detailHguids: [],
    submitContainer: true,
  });
  ensureSuccess(response.data, "提交整柜任务失败");
  return normalizeContainerJob(response.data);
}

export async function waitSubmitJob(jobId: string, options?: { pollIntervalMs?: number; timeoutMs?: number }) {
  return wait(jobId, options);
}

export async function createPushProductsToHqJob(data: PushProductsToHqJobRequest): Promise<PushProductsToHqJob> {
  const response = await apiClient.post(`${PRODUCTS_PATH}/push-to-hq/jobs`, data);
  ensureSuccess(response.data, "创建推送 HQ 任务失败");
  return normalizePushProductsToHqJob(response.data);
}

export async function getPushProductsToHqJob(jobId: string): Promise<PushProductsToHqJob> {
  const response = await apiClient.get(`${PRODUCTS_PATH}/push-to-hq/jobs/${encodeURIComponent(jobId)}`);
  ensureSuccess(response.data, "查询推送 HQ 任务失败");
  return normalizePushProductsToHqJob(response.data, jobId);
}

export async function waitPushProductsToHqJob(
  jobId: string,
  options: { pollIntervalMs?: number; timeoutMs?: number } = {},
) {
  const pollIntervalMs = options.pollIntervalMs ?? 2000;
  const timeoutMs = options.timeoutMs ?? 10 * 60 * 1000;
  const startedAt = Date.now();

  while (Date.now() - startedAt <= timeoutMs) {
    const job = await getPushProductsToHqJob(jobId);
    if (job.status === "Succeeded" || job.status === "Failed") {
      return job;
    }
    await new Promise((resolve) => setTimeout(resolve, pollIntervalMs));
  }
  throw new Error("推送 HQ 任务轮询超时");
}

export async function alignDomesticProductCode(
  payload: AlignDomesticProductCodeRequest,
): Promise<AlignDomesticProductCodeResult> {
  const response = await apiClient.post(
    `${CONTAINERS_PATH}/details/align-domestic-product-code`,
    buildAlignDomesticProductCodePayload(payload),
  );
  ensureSuccess(response.data, "对齐国内商品编码失败");
  return normalizeAlignDomesticProductCodeResult(response.data);
}

export async function exportContainerDetails(
  containerGuid: string,
  request: ContainerExportRequest,
): Promise<ContainerExportResult> {
  const format = request.format;
  const response = await apiClient.post<ExportData>(
    `${CONTAINERS_PATH}/${encodeURIComponent(containerGuid)}/products/export`,
    {
      format,
      query: request.query,
      selectedHguids: request.selectedHguids ?? [],
      columns: request.columns ?? [],
    },
    { responseType: "arraybuffer" },
  );
  const extension = format === "pdf" ? "pdf" : "xlsx";
  const contentType = format === "pdf"
    ? "application/pdf"
    : "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
  return writeAndShareExport(
    response,
    `${request.fileNameHint || "container-details"}.${extension}`,
    contentType,
  );
}
