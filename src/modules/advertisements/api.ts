import type {
  AdvertisementItem,
  AdvertisementListQuery,
  AdvertisementMediaType,
  AdvertisementUploadSignature,
  AdvertisementUploadSignatureRequest,
  AdvertisementUpsertPayload,
  PagedResult,
} from "@/modules/advertisements/types";

const BASE_PATH = "/react/v1/advertisements";
const LIST_PAGE_SIZES = [20, 50, 100] as const;

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

function asNumber(value: unknown, fallback = 0) {
  const parsed = asNullableNumber(value);
  return parsed == null ? fallback : parsed;
}

function asNullableInt(value: unknown): number | null {
  const parsed = asNullableNumber(value);
  return parsed == null ? null : Math.trunc(parsed);
}

function asBoolean(value: unknown) {
  if (typeof value === "boolean") {
    return value;
  }
  if (typeof value === "number") {
    return value !== 0;
  }
  if (typeof value === "string") {
    const normalized = value.trim().toLowerCase();
    return normalized === "true" || normalized === "1" || normalized === "yes";
  }
  return false;
}

function trimText(value: unknown) {
  if (typeof value !== "string") {
    return undefined;
  }
  const trimmed = value.trim();
  return trimmed ? trimmed : undefined;
}

function normalizePage(value?: number) {
  return value && Number.isFinite(value) && value > 0 ? Math.trunc(value) : 1;
}

function normalizePageSize(value?: number) {
  const normalizedValue =
    value && Number.isFinite(value) ? Math.trunc(value) : undefined;
  return LIST_PAGE_SIZES.includes(
    normalizedValue as (typeof LIST_PAGE_SIZES)[number]
  )
    ? normalizedValue!
    : 20;
}

function asMediaType(value: unknown): AdvertisementMediaType {
  return String(value).toLowerCase() === "video" ? "video" : "image";
}

function unwrapPayload(payload: unknown) {
  if (Array.isArray(payload)) {
    return payload;
  }
  const root = asRecord(payload) ?? {};
  return pick(root, "data", "Data") ?? root;
}

function getArray(payload: unknown, ...keys: string[]) {
  if (Array.isArray(payload)) {
    return payload;
  }
  const root = asRecord(payload) ?? {};
  const value = pick(root, ...keys);
  return Array.isArray(value) ? value : [];
}

export function buildAdvertisementGridPayload(query: AdvertisementListQuery) {
  return {
    title: trimText(query.title),
    mediaType: trimText(query.mediaType),
    storeCode: trimText(query.storeCode),
    isEnabled:
      typeof query.isEnabled === "boolean" ? query.isEnabled : undefined,
    pageNumber: normalizePage(query.pageNumber),
    pageSize: normalizePageSize(query.pageSize),
  };
}

export function buildAdvertisementPayload(payload: AdvertisementUpsertPayload) {
  return {
    title: trimText(payload.title) ?? "",
    description: trimText(payload.description) ?? "",
    mediaType: asMediaType(payload.mediaType),
    mediaUrl: trimText(payload.mediaUrl) ?? "",
    thumbnailUrl: trimText(payload.thumbnailUrl) ?? "",
    objectKey: trimText(payload.objectKey) ?? "",
    originalFileName: trimText(payload.originalFileName) ?? "",
    contentType: trimText(payload.contentType) ?? "",
    fileSize: asNullableInt(payload.fileSize) ?? 0,
    effectiveStart: trimText(payload.effectiveStart) ?? "",
    effectiveEnd: trimText(payload.effectiveEnd) ?? "",
    isEnabled: Boolean(payload.isEnabled),
    sortOrder: asNullableInt(payload.sortOrder) ?? 0,
    stores: (payload.stores ?? [])
      .map((item) => ({
        storeCode: trimText(item.storeCode) ?? "",
      }))
      .filter((item) => item.storeCode),
  };
}

export function normalizeAdvertisement(raw: unknown): AdvertisementItem {
  const item = asRecord(raw) ?? {};
  return {
    id: asString(pick(item, "id", "Id", "ID", "advertisementId", "AdvertisementId")),
    title: asString(pick(item, "title", "Title")),
    description: asString(pick(item, "description", "Description")),
    mediaType: asMediaType(pick(item, "mediaType", "MediaType")),
    mediaUrl: asString(pick(item, "mediaUrl", "MediaUrl")),
    thumbnailUrl: asString(pick(item, "thumbnailUrl", "ThumbnailUrl")),
    objectKey: asString(pick(item, "objectKey", "ObjectKey")),
    originalFileName: asString(pick(item, "originalFileName", "OriginalFileName")),
    contentType: asString(pick(item, "contentType", "ContentType")),
    fileSize: asNullableInt(pick(item, "fileSize", "FileSize")),
    effectiveStart: asString(pick(item, "effectiveStart", "EffectiveStart")),
    effectiveEnd: asString(pick(item, "effectiveEnd", "EffectiveEnd")),
    isEnabled: asBoolean(pick(item, "isEnabled", "IsEnabled")),
    sortOrder: asNullableInt(pick(item, "sortOrder", "SortOrder")),
    stores: getArray(item, "stores", "Stores", "storeRanges", "StoreRanges").map((store) => {
      const storeRecord = asRecord(store) ?? {};
      return {
        storeCode: asString(pick(storeRecord, "storeCode", "StoreCode")),
      };
    }),
  };
}

export function normalizeAdvertisementsResponse(
  payload: unknown
): PagedResult<AdvertisementItem> {
  const data = unwrapPayload(payload);
  const record = asRecord(data) ?? {};
  return {
    items: getArray(data, "items", "Items", "rows", "Rows", "dataSource", "DataSource").map(
      normalizeAdvertisement
    ),
    total: asNumber(pick(record, "total", "Total", "totalCount", "TotalCount"), 0),
    pageNumber: asNumber(pick(record, "pageNumber", "PageNumber", "page", "Page"), 1),
    pageSize: asNumber(pick(record, "pageSize", "PageSize", "limit", "Limit"), 20),
  };
}

export function normalizeAdvertisementDetail(payload: unknown): AdvertisementItem | null {
  const data = unwrapPayload(payload);
  const detail = asRecord(
    pick(asRecord(data) ?? {}, "item", "Item", "advertisement", "Advertisement")
  );
  const record = detail ?? asRecord(data);
  if (!record) {
    return null;
  }
  return normalizeAdvertisement(record);
}

export function normalizeAdvertisementUploadSignature(
  payload: unknown
): AdvertisementUploadSignature {
  const data = unwrapPayload(payload);
  const record = asRecord(data) ?? {};
  const headersRecord = asRecord(pick(record, "headers", "Headers")) ?? {};
  const headers = Object.entries(headersRecord).reduce<Record<string, string>>(
    (accumulator, [key, value]) => {
      if (typeof value === "string") {
        accumulator[key] = value;
      }
      return accumulator;
    },
    {}
  );

  return {
    url: asString(pick(record, "url", "Url", "uploadUrl", "UploadUrl")),
    objectKey: asString(pick(record, "objectKey", "ObjectKey")),
    mediaUrl: asString(pick(record, "mediaUrl", "MediaUrl")) || undefined,
    uploadUrl: asString(pick(record, "uploadUrl", "UploadUrl")) || undefined,
    headers,
  };
}

export async function fetchAdvertisements(query: AdvertisementListQuery) {
  const client = await getApiClient();
  const response = await client.post(`${BASE_PATH}/grid`, buildAdvertisementGridPayload(query));
  return normalizeAdvertisementsResponse(response.data);
}

export async function fetchAdvertisementDetail(id: string) {
  const client = await getApiClient();
  const response = await client.get(`${BASE_PATH}/${encodeURIComponent(id)}`);
  return normalizeAdvertisementDetail(response.data);
}

export async function createAdvertisement(payload: AdvertisementUpsertPayload) {
  const client = await getApiClient();
  const response = await client.post(BASE_PATH, buildAdvertisementPayload(payload));
  return normalizeAdvertisementDetail(response.data);
}

export async function updateAdvertisement(id: string, payload: AdvertisementUpsertPayload) {
  const client = await getApiClient();
  const response = await client.put(
    `${BASE_PATH}/${encodeURIComponent(id)}`,
    buildAdvertisementPayload(payload)
  );
  return normalizeAdvertisementDetail(response.data);
}

export async function deleteAdvertisement(id: string) {
  const client = await getApiClient();
  await client.delete(`${BASE_PATH}/${encodeURIComponent(id)}`);
}

export async function setAdvertisementEnabled(id: string, enable: boolean) {
  const client = await getApiClient();
  await client.post(`${BASE_PATH}/${encodeURIComponent(id)}/enable`, undefined, {
    params: { enable },
  });
}

export async function createAdvertisementUploadSignature(
  payload: AdvertisementUploadSignatureRequest
) {
  const client = await getApiClient();
  const response = await client.post(`${BASE_PATH}/upload-signature`, {
    fileName: trimText(payload.fileName) ?? "",
    contentType: trimText(payload.contentType) ?? "application/octet-stream",
    fileSize: asNullableInt(payload.fileSize) ?? 0,
  });
  return normalizeAdvertisementUploadSignature(response.data);
}
