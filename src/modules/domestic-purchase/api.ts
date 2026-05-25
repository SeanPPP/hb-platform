import type {
  CreateDomesticProductBatchRequest,
  DomesticProductBatch,
  DomesticProductBatchDetail,
  DomesticProductBatchItem,
  DomesticProductListItem,
  DomesticProductListQuery,
  DomesticProductListResult,
  DomesticSupplierOption,
  ProductPrefixOption,
  UpdateDomesticProductBatchItemsRequest,
  UpdateDomesticProductRequest,
} from "@/modules/domestic-purchase/types";
import { ProductCreationType } from "@/modules/domestic-purchase/types";

const CREATION_BASE_PATH = "/v1/domestic-product-creation";
const DOMESTIC_PRODUCTS_PATH = "/v1/DomesticProducts";

async function getApiClient() {
  const { apiClient } = await import("@/shared/api/client");
  return apiClient;
}

function pick(raw: Record<string, unknown>, ...keys: string[]) {
  for (const key of keys) {
    if (raw[key] !== undefined && raw[key] !== null) {
      return raw[key];
    }
  }
  return undefined;
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function asString(value: unknown, fallback = ""): string {
  if (typeof value === "string") {
    return value;
  }
  if (typeof value === "number" && Number.isFinite(value)) {
    return String(value);
  }
  return fallback;
}

function asNumber(value: unknown, fallback = 0): number {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }
  if (typeof value === "string" && value.trim()) {
    const parsed = Number(value);
    return Number.isFinite(parsed) ? parsed : fallback;
  }
  return fallback;
}

function asNullableNumber(value: unknown): number | null {
  if (value === null || value === undefined) {
    return null;
  }
  if (typeof value === "string" && !value.trim()) {
    return null;
  }
  const parsed = typeof value === "string" ? Number(value) : value;
  return typeof parsed === "number" && Number.isFinite(parsed) ? parsed : null;
}

function asBoolean(value: unknown): boolean {
  if (typeof value === "boolean") {
    return value;
  }
  if (typeof value === "number") {
    return value !== 0;
  }
  if (typeof value === "string") {
    const normalized = value.trim().toLowerCase();
    if (!normalized) {
      return false;
    }
    if (normalized === "true") {
      return true;
    }
    if (normalized === "false") {
      return false;
    }
    const parsed = Number(normalized);
    if (Number.isFinite(parsed)) {
      return parsed !== 0;
    }
  }
  return false;
}

function toNumber(value: unknown, fallback = 0) {
  return asNumber(value, fallback);
}

function normalizeBatch(raw: unknown): DomesticProductBatch {
  const item = asRecord(raw) ?? {};
  return {
    batchNumber: asString(pick(item, "batchNumber", "BatchNumber")),
    supplierCode: asString(pick(item, "supplierCode", "SupplierCode")),
    supplierName: (pick(item, "supplierName", "SupplierName") ?? null) as string | null,
    prefixCode: (pick(item, "prefixCode", "PrefixCode") ?? null) as string | null,
    normalCount: toNumber(pick(item, "normalProductCount", "normalCount", "NormalProductCount", "NormalCount")),
    setCount: toNumber(pick(item, "setProductCount", "setCount", "SetProductCount", "SetCount")),
    totalCount: toNumber(pick(item, "totalCount", "TotalCount")),
    createdAt: asString(pick(item, "createdTime", "createdAt", "CreatedTime", "CreatedAt")),
    createdBy: (pick(item, "createdBy", "CreatedBy") ?? null) as string | null,
  };
}

function normalizeBatchItem(raw: unknown): DomesticProductBatchItem {
  const item = asRecord(raw) ?? {};
  const productCode = asString(pick(item, "productCode", "ProductCode"));
  const itemNumber = asString(
    pick(item, "itemNumber", "ItemNumber", "hbProductNo", "HBProductNo"),
    productCode
  );

  return {
    productCode: productCode || itemNumber,
    itemNumber,
    hbProductNo: asString(pick(item, "hbProductNo", "HBProductNo"), itemNumber),
    barcode: asString(pick(item, "barcode", "Barcode")),
    productName: asString(pick(item, "productName", "ProductName")),
    productType: toNumber(pick(item, "productType", "ProductType")) as ProductCreationType,
    privateLabelPrice: asNullableNumber(pick(item, "privateLabelPrice", "PrivateLabelPrice")),
    setQuantity: asNullableNumber(pick(item, "setQuantity", "SetQuantity")),
    setPrice: asNullableNumber(pick(item, "setPrice", "SetPrice")),
    parentItemNumber: (pick(
      item,
      "parentProductCode",
      "parentItemNumber",
      "ParentProductCode",
      "ParentItemNumber"
    ) ?? null) as string | null,
  };
}

function normalizeSupplier(raw: unknown): DomesticSupplierOption {
  const item = asRecord(raw) ?? {};
  return {
    supplierCode: asString(pick(item, "supplierCode", "SupplierCode")),
    supplierName: asString(pick(item, "supplierName", "SupplierName")),
  };
}

function normalizePrefix(raw: unknown): ProductPrefixOption {
  const item = asRecord(raw) ?? {};
  return {
    prefixCode: asString(pick(item, "prefixCode", "PrefixCode")),
    prefixName: asString(pick(item, "prefixName", "PrefixName")),
    prefixDescription: (pick(item, "prefixDescription", "PrefixDescription") ?? null) as string | null,
  };
}

function getPayloadItems(payload: unknown): unknown[] {
  const root = asRecord(payload) ?? {};
  const items = pick(root, "items", "Items");
  return Array.isArray(items) ? items : [];
}

export function normalizeDomesticProduct(raw: unknown): DomesticProductListItem {
  const item = asRecord(raw) ?? {};
  return {
    productCode: asString(pick(item, "productCode", "ProductCode")),
    supplierCode: asString(pick(item, "supplierCode", "SupplierCode")),
    supplierName: asString(pick(item, "supplierName", "SupplierName")),
    productName: asString(pick(item, "productName", "ProductName")),
    englishProductName: asString(pick(item, "englishProductName", "EnglishProductName")),
    hbProductNo: asString(pick(item, "hbProductNo", "HBProductNo", "HbProductNo")),
    barcode: asString(pick(item, "barcode", "Barcode")),
    productSpecification: asString(pick(item, "productSpecification", "ProductSpecification")),
    productType: asNumber(pick(item, "productType", "ProductType"), 0),
    domesticPrice: asNullableNumber(pick(item, "domesticPrice", "DomesticPrice")),
    oemPrice: asNullableNumber(pick(item, "oemPrice", "OEMPrice", "OemPrice")),
    importPrice: asNullableNumber(pick(item, "importPrice", "ImportPrice")),
    packingQuantity: asNullableNumber(pick(item, "packingQuantity", "PackingQuantity")),
    unitVolume: asNullableNumber(pick(item, "unitVolume", "UnitVolume")),
    middlePackQuantity: asNullableNumber(pick(item, "middlePackQuantity", "MiddlePackQuantity")),
    productImage: asString(pick(item, "productImage", "ProductImage")),
    isActive: asBoolean(pick(item, "isActive", "IsActive")),
  };
}

export function normalizeDomesticProductsListResponse(payload: unknown): DomesticProductListResult {
  const root = asRecord(payload) ?? {};
  const page = asNumber(pick(root, "page", "Page"), 1);
  const pageSize = asNumber(pick(root, "pageSize", "PageSize"), 20);
  const total = asNumber(pick(root, "total", "Total", "totalCount", "TotalCount"), 0);

  return {
    items: getPayloadItems(root).map(normalizeDomesticProduct),
    total,
    page,
    pageSize,
  };
}

function buildDomesticProductsParams(query: DomesticProductListQuery = {}) {
  return {
    page: query.page ?? 1,
    pageSize: query.pageSize ?? 20,
    supplierCode: query.supplierCode?.trim() || undefined,
    productNo: query.productNo?.trim() || undefined,
  };
}

async function toBase64(data: unknown) {
  const { fromByteArray } = await import("base64-js");
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
  const apiClient = await getApiClient();
  const response = await apiClient.get(`${CREATION_BASE_PATH}/batches`, {
    params: { page, pageSize },
  });
  const data = response.data as Record<string, unknown>;
  return {
    items: getPayloadItems(data).map(normalizeBatch),
    total: toNumber(pick(data, "total", "Total")),
    page: toNumber(pick(data, "page", "Page"), page),
    pageSize: toNumber(pick(data, "pageSize", "PageSize"), pageSize),
  };
}

export async function fetchDomesticProductBatchDetail(batchNumber: string): Promise<DomesticProductBatchDetail> {
  const apiClient = await getApiClient();
  const response = await apiClient.get(`${CREATION_BASE_PATH}/batch/${encodeURIComponent(batchNumber)}`);
  const batch = normalizeBatch(response.data);
  return {
    ...batch,
    items: getPayloadItems(response.data).map(normalizeBatchItem),
  };
}

export async function createDomesticProductBatch(payload: CreateDomesticProductBatchRequest) {
  const apiClient = await getApiClient();
  const response = await apiClient.post(`${CREATION_BASE_PATH}/batch`, payload);
  return response.data;
}

export async function updateDomesticProductBatchItems(
  batchNumber: string,
  payload: UpdateDomesticProductBatchItemsRequest
) {
  const apiClient = await getApiClient();
  const response = await apiClient.put(`${CREATION_BASE_PATH}/batch/${encodeURIComponent(batchNumber)}/items`, payload);
  return response.data;
}

export async function fetchDomesticProducts(
  query: DomesticProductListQuery = {}
): Promise<DomesticProductListResult> {
  const apiClient = await getApiClient();
  const response = await apiClient.get(DOMESTIC_PRODUCTS_PATH, {
    params: buildDomesticProductsParams(query),
  });
  return normalizeDomesticProductsListResponse(response.data);
}

export async function updateDomesticProduct(
  productCode: string,
  payload: UpdateDomesticProductRequest
): Promise<DomesticProductListItem> {
  const apiClient = await getApiClient();
  const response = await apiClient.put(`${DOMESTIC_PRODUCTS_PATH}/${encodeURIComponent(productCode)}`, payload);
  return normalizeDomesticProduct(response.data);
}

export async function fetchDomesticSuppliers() {
  const apiClient = await getApiClient();
  const response = await apiClient.get("/v1/ChinaSuppliers/active");
  return Array.isArray(response.data) ? response.data.map(normalizeSupplier).filter((item) => item.supplierCode) : [];
}

export async function fetchProductPrefixes(supplierCode: string) {
  const apiClient = await getApiClient();
  const response = await apiClient.get("/v1/ProductPrefixCodes", {
    params: { page: 1, pageSize: 100, isActive: true, supplierCode },
  });
  const data = response.data as Record<string, unknown>;
  return getPayloadItems(data).map(normalizePrefix).filter((item) => item.prefixName || item.prefixCode);
}

export async function exportDomesticProductBatch(batchNumber: string) {
  const apiClient = await getApiClient();
  const response = await apiClient.get(`${CREATION_BASE_PATH}/batch/${encodeURIComponent(batchNumber)}/export`, {
    responseType: "arraybuffer",
  });
  const base64 = await toBase64(response.data);
  if (!base64) {
    throw new Error("Empty export file");
  }

  const FileSystem = await import("expo-file-system/legacy");
  const Sharing = await import("expo-sharing");
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
