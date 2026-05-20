import { fromByteArray } from "base64-js";
import * as FileSystem from "expo-file-system/legacy";
import * as Sharing from "expo-sharing";
import { apiClient } from "@/shared/api/client";
import type {
  CreateDomesticProductBatchRequest,
  DomesticProductBatch,
  DomesticProductBatchDetail,
  DomesticProductBatchItem,
  DomesticSupplierOption,
  ProductPrefixOption,
} from "@/modules/domestic-purchase/types";
import { ProductCreationType } from "@/modules/domestic-purchase/types";

const CREATION_BASE_PATH = "/v1/domestic-product-creation";

function toNumber(value: unknown, fallback = 0) {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }
  if (typeof value === "string" && value.trim()) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : fallback;
  }
  return fallback;
}

function normalizeBatch(raw: unknown): DomesticProductBatch {
  const item = (raw && typeof raw === "object" ? raw : {}) as Record<string, unknown>;
  return {
    batchNumber: String(item.batchNumber ?? item.BatchNumber ?? ""),
    supplierCode: String(item.supplierCode ?? item.SupplierCode ?? ""),
    supplierName: (item.supplierName ?? item.SupplierName ?? null) as string | null,
    prefixCode: (item.prefixCode ?? item.PrefixCode ?? null) as string | null,
    normalCount: toNumber(item.normalProductCount ?? item.normalCount ?? item.NormalProductCount ?? item.NormalCount),
    setCount: toNumber(item.setProductCount ?? item.setCount ?? item.SetProductCount ?? item.SetCount),
    totalCount: toNumber(item.totalCount ?? item.TotalCount),
    createdAt: String(item.createdTime ?? item.createdAt ?? item.CreatedTime ?? item.CreatedAt ?? ""),
    createdBy: (item.createdBy ?? item.CreatedBy ?? null) as string | null,
  };
}

function normalizeBatchItem(raw: unknown): DomesticProductBatchItem {
  const item = (raw && typeof raw === "object" ? raw : {}) as Record<string, unknown>;
  const itemNumber = String(item.productCode ?? item.itemNumber ?? item.hbProductNo ?? item.HBProductNo ?? "");
  return {
    itemNumber,
    hbProductNo: String(item.hbProductNo ?? item.HBProductNo ?? itemNumber),
    barcode: String(item.barcode ?? item.Barcode ?? ""),
    productName: String(item.productName ?? item.ProductName ?? ""),
    productType: toNumber(item.productType ?? item.ProductType) as ProductCreationType,
    privateLabelPrice:
      item.privateLabelPrice == null && item.PrivateLabelPrice == null
        ? null
        : toNumber(item.privateLabelPrice ?? item.PrivateLabelPrice),
    setQuantity:
      item.setQuantity == null && item.SetQuantity == null
        ? null
        : toNumber(item.setQuantity ?? item.SetQuantity),
    setPrice:
      item.setPrice == null && item.SetPrice == null
        ? null
        : toNumber(item.setPrice ?? item.SetPrice),
    parentItemNumber: (item.parentProductCode ?? item.parentItemNumber ?? item.ParentProductCode ?? item.ParentItemNumber ?? null) as string | null,
  };
}

function normalizeSupplier(raw: unknown): DomesticSupplierOption {
  const item = (raw && typeof raw === "object" ? raw : {}) as Record<string, unknown>;
  return {
    supplierCode: String(item.supplierCode ?? item.SupplierCode ?? ""),
    supplierName: String(item.supplierName ?? item.SupplierName ?? ""),
  };
}

function normalizePrefix(raw: unknown): ProductPrefixOption {
  const item = (raw && typeof raw === "object" ? raw : {}) as Record<string, unknown>;
  return {
    prefixCode: String(item.prefixCode ?? item.PrefixCode ?? ""),
    prefixName: String(item.prefixName ?? item.PrefixName ?? ""),
    prefixDescription: (item.prefixDescription ?? item.PrefixDescription ?? null) as string | null,
  };
}

function getPayloadItems(payload: unknown): unknown[] {
  const root = payload && typeof payload === "object" ? (payload as Record<string, unknown>) : {};
  const items = root.items ?? root.Items;
  return Array.isArray(items) ? items : [];
}

function toBase64(data: unknown) {
  if (data instanceof ArrayBuffer) {
    return fromByteArray(new Uint8Array(data));
  }
  if (ArrayBuffer.isView(data)) {
    return fromByteArray(new Uint8Array(data.buffer, data.byteOffset, data.byteLength));
  }
  if (typeof data === "string") {
    return data;
  }
  return "";
}

export async function fetchDomesticProductBatches(page = 1, pageSize = 20) {
  const response = await apiClient.get(`${CREATION_BASE_PATH}/batches`, {
    params: { page, pageSize },
  });
  const data = response.data as Record<string, unknown>;
  return {
    items: getPayloadItems(data).map(normalizeBatch),
    total: toNumber(data.total ?? data.Total),
    page: toNumber(data.page ?? data.Page, page),
    pageSize: toNumber(data.pageSize ?? data.PageSize, pageSize),
  };
}

export async function fetchDomesticProductBatchDetail(batchNumber: string): Promise<DomesticProductBatchDetail> {
  const response = await apiClient.get(`${CREATION_BASE_PATH}/batch/${encodeURIComponent(batchNumber)}`);
  const batch = normalizeBatch(response.data);
  return {
    ...batch,
    items: getPayloadItems(response.data).map(normalizeBatchItem),
  };
}

export async function createDomesticProductBatch(payload: CreateDomesticProductBatchRequest) {
  const response = await apiClient.post(`${CREATION_BASE_PATH}/batch`, payload);
  return response.data;
}

export async function fetchDomesticSuppliers() {
  const response = await apiClient.get("/v1/ChinaSuppliers/active");
  return Array.isArray(response.data) ? response.data.map(normalizeSupplier).filter((item) => item.supplierCode) : [];
}

export async function fetchProductPrefixes(supplierCode: string) {
  const response = await apiClient.get("/v1/ProductPrefixCodes", {
    params: { page: 1, pageSize: 100, isActive: true, supplierCode },
  });
  const data = response.data as Record<string, unknown>;
  return getPayloadItems(data).map(normalizePrefix).filter((item) => item.prefixName || item.prefixCode);
}

export async function exportDomesticProductBatch(batchNumber: string) {
  const response = await apiClient.get(`${CREATION_BASE_PATH}/batch/${encodeURIComponent(batchNumber)}/export`, {
    responseType: "arraybuffer",
  });
  const base64 = toBase64(response.data);
  if (!base64) {
    throw new Error("Empty export file");
  }

  const fileName = `domestic-product-batch-${batchNumber}.xlsx`;
  const fileUri = `${FileSystem.documentDirectory ?? ""}${fileName}`;
  await FileSystem.writeAsStringAsync(fileUri, base64, {
    encoding: FileSystem.EncodingType.Base64,
  });

  if (await Sharing.isAvailableAsync()) {
    await Sharing.shareAsync(fileUri, {
      mimeType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
      UTI: "org.openxmlformats.spreadsheetml.sheet",
      dialogTitle: fileName,
    });
  }

  return fileUri;
}
