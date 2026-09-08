import { unwrapApiEnvelope } from "@/shared/api/api-envelope";
import type {
  WpfDeviceOption,
  WpfDevicePage,
  WpfRelease,
  WpfReleasePage,
  WpfReleasePolicyRequest,
  WpfReleaseQuery,
  WpfReleaseUpdateRequest,
  WpfStoreOption,
  WpfTargetScope,
} from "./types";

const BASE_PATH = "/wpf-app-releases";

async function getApiClient() {
  // 延迟加载客户端可让纯 API 契约测试在 Node 中运行，而实际请求仍统一走共享认证客户端。
  const { apiClient } = await import("@/shared/api/client");
  return apiClient;
}

type AnyRecord = Record<string, unknown>;

function asRecord(value: unknown): AnyRecord {
  return value && typeof value === "object" && !Array.isArray(value)
    ? (value as AnyRecord)
    : {};
}

function read(source: AnyRecord, ...keys: string[]) {
  for (const key of keys) {
    if (Object.prototype.hasOwnProperty.call(source, key)) return source[key];
  }
  return undefined;
}

function stringValue(source: AnyRecord, ...keys: string[]) {
  const value = read(source, ...keys);
  return typeof value === "string" ? value.trim() : null;
}

function numberValue(source: AnyRecord, ...keys: string[]) {
  const value = read(source, ...keys);
  const parsed = typeof value === "number" ? value : Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function booleanValue(source: AnyRecord, ...keys: string[]) {
  const value = read(source, ...keys);
  if (typeof value === "boolean") return value;
  if (typeof value === "string") return value.trim().toLowerCase() === "true";
  return false;
}

function stringArray(value: unknown) {
  return Array.isArray(value)
    ? [
        ...new Set(
          value
            .filter((item): item is string => typeof item === "string")
            .map((item) => item.trim())
            .filter(Boolean),
        ),
      ].sort()
    : [];
}

function positiveIntegerArray(value: unknown) {
  return Array.isArray(value)
    ? [
        ...new Set(
          value
            .map((item) => Number(item))
            .filter((item) => Number.isInteger(item) && item > 0),
        ),
      ].sort((a, b) => a - b)
    : [];
}

function normalizeScope(value: unknown): WpfTargetScope {
  const normalized =
    typeof value === "string" ? value.trim().toLowerCase() : "";
  return normalized === "stores" || normalized === "devices"
    ? normalized
    : "all";
}

function normalizeStore(value: unknown): WpfStoreOption | null {
  const raw = asRecord(value);
  const storeGuid = stringValue(raw, "storeGuid", "StoreGuid") ?? "";
  if (!storeGuid) return null;
  return {
    storeGuid,
    storeCode: stringValue(raw, "storeCode", "StoreCode") ?? "",
    storeName: stringValue(raw, "storeName", "StoreName") ?? "",
  };
}

function normalizeDevice(value: unknown): WpfDeviceOption | null {
  const raw = asRecord(value);
  const deviceRegistrationId = numberValue(
    raw,
    "deviceRegistrationId",
    "DeviceRegistrationId",
  );
  if (
    !deviceRegistrationId ||
    !Number.isInteger(deviceRegistrationId) ||
    deviceRegistrationId < 1
  )
    return null;
  return {
    deviceRegistrationId,
    systemDeviceNumber:
      stringValue(raw, "systemDeviceNumber", "SystemDeviceNumber") ?? "",
    storeCode: stringValue(raw, "storeCode", "StoreCode") ?? "",
    storeName: stringValue(raw, "storeName", "StoreName") ?? "",
    remarks: stringValue(raw, "remarks", "Remarks"),
  };
}

function normalizeStoreSummaries(value: unknown) {
  if (!Array.isArray(value)) return [];
  return value
    .map(normalizeStore)
    .filter((item): item is WpfStoreOption => item !== null);
}

function normalizeDeviceSummaries(value: unknown) {
  if (!Array.isArray(value)) return [];
  return value
    .map(normalizeDevice)
    .filter((item): item is WpfDeviceOption => item !== null);
}

export function normalizeWpfRelease(value: unknown): WpfRelease {
  const raw = asRecord(value);
  const targetScope = normalizeScope(read(raw, "targetScope", "TargetScope"));
  return {
    id: stringValue(raw, "id", "releaseId", "Id", "ReleaseId") ?? "",
    version: stringValue(raw, "version", "Version") ?? "",
    channel: stringValue(raw, "channel", "Channel") ?? "production",
    fileName: stringValue(raw, "fileName", "FileName") ?? "",
    fileSize: numberValue(raw, "fileSize", "FileSize"),
    sha256: stringValue(raw, "sha256", "Sha256"),
    installerType: (() => {
      const value = (
        stringValue(raw, "installerType", "InstallerType") ?? ""
      ).toLowerCase();
      return value === "exe" || value === "msi" ? value : null;
    })(),
    installerArguments: stringValue(
      raw,
      "installerArguments",
      "InstallerArguments",
    ),
    downloadUrl: stringValue(raw, "downloadUrl", "DownloadUrl"),
    objectKey: stringValue(
      raw,
      "objectKey",
      "cosObjectKey",
      "ObjectKey",
      "CosObjectKey",
    ),
    releaseNotes: stringValue(raw, "releaseNotes", "ReleaseNotes"),
    isActive: booleanValue(raw, "isActive", "active", "IsActive", "Active"),
    isCurrent: booleanValue(
      raw,
      "isCurrent",
      "current",
      "IsCurrent",
      "Current",
    ),
    isRollback: booleanValue(raw, "isRollback", "IsRollback"),
    forceUpdate: booleanValue(raw, "forceUpdate", "ForceUpdate"),
    minimumSupportedVersion: stringValue(
      raw,
      "minimumSupportedVersion",
      "MinimumSupportedVersion",
    ),
    targetVersion: stringValue(raw, "targetVersion", "TargetVersion"),
    targetScope,
    targetStoreGuids: stringArray(
      read(raw, "targetStoreGuids", "TargetStoreGuids"),
    ),
    targetDeviceRegistrationIds: positiveIntegerArray(
      read(raw, "targetDeviceRegistrationIds", "TargetDeviceRegistrationIds"),
    ),
    targetStoreSummaries: normalizeStoreSummaries(
      read(raw, "targetStoreSummaries", "TargetStoreSummaries"),
    ),
    targetDeviceSummaries: normalizeDeviceSummaries(
      read(raw, "targetDeviceSummaries", "TargetDeviceSummaries"),
    ),
    policyUpdatedAt: stringValue(raw, "policyUpdatedAt", "PolicyUpdatedAt"),
    policyUpdatedBy: stringValue(raw, "policyUpdatedBy", "PolicyUpdatedBy"),
    createdAt: stringValue(raw, "createdAt", "CreatedAt"),
    updatedAt: stringValue(
      raw,
      "updatedAt",
      "lastModifiedAt",
      "UpdatedAt",
      "LastModifiedAt",
    ),
  };
}

function pagePayload(
  value: unknown,
  fallback: { page: number; pageSize: number },
) {
  const raw = asRecord(value);
  const rawItems = read(raw, "items", "Items", "list", "List", "data", "Data");
  const items = Array.isArray(rawItems) ? rawItems : [];
  return {
    items,
    total:
      Number(
        read(raw, "total", "totalCount", "Total", "TotalCount") ?? items.length,
      ) || 0,
    page: Number(read(raw, "page", "Page") ?? fallback.page) || fallback.page,
    pageSize:
      Number(
        read(raw, "pageSize", "limit", "PageSize", "Limit") ??
          fallback.pageSize,
      ) || fallback.pageSize,
  };
}

export function buildReleaseQuery(input: Partial<WpfReleaseQuery> = {}) {
  const page =
    Number.isInteger(input.page) && (input.page ?? 0) > 0 ? input.page! : 1;
  const pageSize =
    Number.isInteger(input.pageSize) && (input.pageSize ?? 0) > 0
      ? input.pageSize!
      : 10;
  return {
    page,
    pageSize,
    channel: input.channel?.trim() || "production",
    includeDisabled: Boolean(input.includeDisabled),
  } satisfies WpfReleaseQuery;
}

export async function getWpfReleases(
  input: Partial<WpfReleaseQuery> = {},
): Promise<WpfReleasePage> {
  const query = buildReleaseQuery(input);
  const response = await (
    await getApiClient()
  ).get(BASE_PATH, { params: query });
  const data = unwrapApiEnvelope<unknown>(response.data);
  const page = pagePayload(data, query);
  return { ...page, items: page.items.map(normalizeWpfRelease) };
}

export async function getWpfTargetStores(): Promise<WpfStoreOption[]> {
  const response = await (
    await getApiClient()
  ).get(`${BASE_PATH}/target-options/stores`);
  const data = unwrapApiEnvelope<unknown>(response.data);
  const raw = asRecord(data);
  const values = Array.isArray(data)
    ? data
    : read(raw, "items", "Items", "data", "Data");
  return (Array.isArray(values) ? values : [])
    .map(normalizeStore)
    .filter((item): item is WpfStoreOption => item !== null);
}

export async function getWpfTargetDevices(
  input: { page?: number; pageSize?: number; keyword?: string } = {},
): Promise<WpfDevicePage> {
  const page =
    Number.isInteger(input.page) && (input.page ?? 0) > 0 ? input.page! : 1;
  const pageSize =
    Number.isInteger(input.pageSize) && (input.pageSize ?? 0) > 0
      ? input.pageSize!
      : 50;
  const params = {
    page,
    pageSize,
    keyword: input.keyword?.trim() || undefined,
  };
  const response = await (
    await getApiClient()
  ).get(`${BASE_PATH}/target-options/devices`, { params });
  const data = unwrapApiEnvelope<unknown>(response.data);
  const result = pagePayload(data, { page, pageSize });
  return {
    ...result,
    items: result.items
      .map(normalizeDevice)
      .filter((item): item is WpfDeviceOption => item !== null),
  };
}

function cleanOptional(value: string | null | undefined) {
  const trimmed = value?.trim();
  return trimmed || null;
}

export function buildReleaseUpdatePayload(
  input: WpfReleaseUpdateRequest,
): WpfReleaseUpdateRequest {
  return {
    ...(input.downloadUrl !== undefined
      ? { downloadUrl: cleanOptional(input.downloadUrl) }
      : {}),
    ...(input.sha256 !== undefined
      ? { sha256: cleanOptional(input.sha256) }
      : {}),
    ...(input.installerType !== undefined
      ? { installerType: input.installerType }
      : {}),
    ...(input.installerArguments !== undefined
      ? { installerArguments: cleanOptional(input.installerArguments) }
      : {}),
    ...(input.releaseNotes !== undefined
      ? { releaseNotes: cleanOptional(input.releaseNotes) }
      : {}),
    ...(input.isActive !== undefined ? { isActive: input.isActive } : {}),
  };
}

export async function updateWpfRelease(
  id: string,
  input: WpfReleaseUpdateRequest,
) {
  const releaseId = id.trim();
  if (!releaseId) throw new Error("WPF_RELEASE_ID_REQUIRED");
  const response = await (
    await getApiClient()
  ).put(
    `${BASE_PATH}/${encodeURIComponent(releaseId)}`,
    buildReleaseUpdatePayload(input),
  );
  return normalizeWpfRelease(unwrapApiEnvelope<unknown>(response.data));
}

export function buildPolicyPayload(
  input: WpfReleasePolicyRequest,
): WpfReleasePolicyRequest {
  const targetScope = normalizeScope(input.targetScope);
  return {
    channel: input.channel.trim().toLowerCase() || "production",
    targetVersion: input.targetVersion.trim(),
    minimumSupportedVersion: input.minimumSupportedVersion.trim(),
    forceUpdate: Boolean(input.forceUpdate),
    isRollback: Boolean(input.isRollback),
    ...(input.rollbackConfirmed === undefined
      ? {}
      : { rollbackConfirmed: Boolean(input.rollbackConfirmed) }),
    targetScope,
    targetStoreGuids:
      targetScope === "stores"
        ? [
            ...new Set(
              input.targetStoreGuids.map((item) => item.trim()).filter(Boolean),
            ),
          ].sort()
        : [],
    targetDeviceRegistrationIds:
      targetScope === "devices"
        ? [
            ...new Set(
              input.targetDeviceRegistrationIds.filter(
                (item) => Number.isInteger(item) && item > 0,
              ),
            ),
          ].sort((a, b) => a - b)
        : [],
  };
}

export async function saveWpfPolicy(input: WpfReleasePolicyRequest) {
  const payload = buildPolicyPayload(input);
  const response = await (
    await getApiClient()
  ).post(`${BASE_PATH}/policy/${encodeURIComponent(payload.channel)}`, payload);
  return unwrapApiEnvelope<unknown>(response.data);
}
