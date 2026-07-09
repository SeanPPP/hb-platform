import { normalizeDeviceStatus } from "@/modules/device-management/status";
import type { PersistedDeviceSession } from "@/modules/device/types";
import type {
  AppDeviceHeartbeatPayload,
  AppDeviceStatus,
  AppDeviceStatusListResult,
  AppDeviceStatusQuery,
  AppDeviceStatusSummary,
  DeviceManagementDevice,
  DeviceManagementListResult,
  DeviceManagementPagination,
  DeviceManagementQuery,
} from "@/modules/device-management/types";

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

function asString(value: unknown): string | undefined {
  if (typeof value === "string") {
    const trimmed = value.trim();
    return trimmed || undefined;
  }

  if (typeof value === "number" && Number.isFinite(value)) {
    return String(value);
  }

  return undefined;
}

function asNumber(value: unknown, fallback: number): number {
  const numericValue = typeof value === "string" && value.trim() ? Number(value) : value;
  return typeof numericValue === "number" && Number.isFinite(numericValue)
    ? numericValue
    : fallback;
}

function asBoolean(value: unknown, fallback = false): boolean {
  if (typeof value === "boolean") {
    return value;
  }

  if (typeof value === "string") {
    const normalized = value.trim().toLowerCase();
    if (["true", "1", "yes"].includes(normalized)) {
      return true;
    }
    if (["false", "0", "no"].includes(normalized)) {
      return false;
    }
  }

  return fallback;
}

function asOptionalNumber(value: unknown): number | undefined {
  const numericValue = typeof value === "string" && value.trim() ? Number(value) : value;
  return typeof numericValue === "number" && Number.isFinite(numericValue)
    ? numericValue
    : undefined;
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function unwrapListPayload(payload: unknown): Record<string, unknown> {
  const root = asRecord(payload);
  if (!root) {
    return {};
  }

  const data = asRecord(root.data ?? root.Data);
  return data ?? root;
}

function normalizeDevice(raw: unknown): DeviceManagementDevice | null {
  const record = asRecord(raw);
  if (!record) {
    return null;
  }

  const id =
    asString(pick(record, "id", "Id", "deviceId", "DeviceId", "deviceGUID", "DeviceGUID")) ??
    asString(pick(record, "hardwareId", "HardwareId"));

  if (!id) {
    return null;
  }

  return {
    id,
    hardwareId: asString(pick(record, "hardwareId", "HardwareId")) ?? "",
    systemDeviceNumber: asString(pick(record, "systemDeviceNumber", "SystemDeviceNumber")),
    deviceNumber: asString(pick(record, "deviceNumber", "DeviceNumber")),
    deviceType: asString(pick(record, "deviceType", "DeviceType")),
    deviceSystem: asString(pick(record, "deviceSystem", "DeviceSystem")),
    deviceName: asString(pick(record, "deviceName", "DeviceName", "name", "Name")),
    storeCode: asString(pick(record, "storeCode", "StoreCode")),
    storeName: asString(pick(record, "storeName", "StoreName")),
    status: normalizeDeviceStatus(pick(record, "status", "Status")),
    appVersion: asString(pick(record, "appVersion", "AppVersion")),
    platform: asString(pick(record, "platform", "Platform")),
    lastSeenAt: asString(pick(record, "lastSeenAt", "LastSeenAt", "lastOnlineAt", "LastOnlineAt")),
    createdAt: asString(pick(record, "createdAt", "CreatedAt")),
    updatedAt: asString(pick(record, "updatedAt", "UpdatedAt", "lastModified", "LastModified")),
  };
}

function normalizePagination(raw: unknown): DeviceManagementPagination {
  const record = asRecord(raw) ?? {};
  const pageNumber = asNumber(pick(record, "pageNumber", "PageNumber", "page", "Page"), 1);
  const pageSize = asNumber(pick(record, "pageSize", "PageSize", "limit", "Limit"), 20);
  const totalCount = asNumber(pick(record, "totalCount", "TotalCount", "total", "Total"), 0);
  const totalPages = asNumber(
    pick(record, "totalPages", "TotalPages"),
    pageSize > 0 ? Math.ceil(totalCount / pageSize) : 0
  );

  return {
    pageNumber,
    pageSize,
    totalCount,
    totalPages,
  };
}

export function normalizeDeviceManagementListResponse(payload: unknown): DeviceManagementListResult {
  const listPayload = unwrapListPayload(payload);
  const devicesPayload = pick(listPayload, "devices", "Devices");
  const devices = Array.isArray(devicesPayload)
    ? devicesPayload.map(normalizeDevice).filter((item): item is DeviceManagementDevice => Boolean(item))
    : [];

  return {
    devices,
    pagination: normalizePagination(pick(listPayload, "pagination", "Pagination")),
  };
}

export function buildListParams(query: DeviceManagementQuery) {
  return {
    page: query.pageNumber,
    pageSize: query.pageSize,
    keyword: query.keyword?.trim() || undefined,
    storeCode: query.storeCode || undefined,
    status: query.status ?? undefined,
    deviceSystem: query.deviceSystem?.trim() || undefined,
    deviceType: query.deviceType?.trim() || undefined,
  };
}

export async function fetchDeviceManagementDevices(
  query: DeviceManagementQuery = {}
): Promise<DeviceManagementListResult> {
  const apiClient = await getApiClient();
  const response = await apiClient.get("/mobile/device-management/paged", { params: buildListParams(query) });
  return normalizeDeviceManagementListResponse(response.data);
}

async function postDeviceAction(id: string | number, action: "activate" | "disable" | "lock") {
  const apiClient = await getApiClient();
  await apiClient.post(`/mobile/device-management/${encodeURIComponent(String(id))}/${action}`);
}

export function activateDevice(id: string | number) {
  return postDeviceAction(id, "activate");
}

export function disableDevice(id: string | number) {
  return postDeviceAction(id, "disable");
}

export function lockDevice(id: string | number) {
  return postDeviceAction(id, "lock");
}

function normalizeAppDeviceStatus(raw: unknown): AppDeviceStatus | null {
  const record = asRecord(raw);
  if (!record) {
    return null;
  }

  const hardwareId = asString(pick(record, "hardwareId", "HardwareId"));
  const id = asString(pick(record, "id", "Id")) ?? hardwareId;
  if (!id || !hardwareId) {
    return null;
  }

  return {
    id,
    hardwareId,
    systemDeviceNumber: asString(pick(record, "systemDeviceNumber", "SystemDeviceNumber")),
    deviceSystem: asString(pick(record, "deviceSystem", "DeviceSystem")),
    platform: asString(pick(record, "platform", "Platform")),
    storeCode: asString(pick(record, "storeCode", "StoreCode")),
    appVersion: asString(pick(record, "appVersion", "AppVersion")),
    appBuildVersion: asString(pick(record, "appBuildVersion", "AppBuildVersion")),
    runtimeVersion: asString(pick(record, "runtimeVersion", "RuntimeVersion")),
    channel: asString(pick(record, "channel", "Channel")),
    updateId: asString(pick(record, "updateId", "UpdateId")),
    updateSource: asString(pick(record, "updateSource", "UpdateSource")),
    lastSeenAtUtc: asString(pick(record, "lastSeenAtUtc", "LastSeenAtUtc", "lastSeenAt", "LastSeenAt")),
    isOnline: asBoolean(pick(record, "isOnline", "IsOnline")),
    lastAuthMode: asString(pick(record, "lastAuthMode", "LastAuthMode")),
    lastSeenUserGuid: asString(pick(record, "lastSeenUserGuid", "LastSeenUserGuid")),
    lastSeenUsername: asString(pick(record, "lastSeenUsername", "LastSeenUsername")),
    lastSeenUserFullName: asString(pick(record, "lastSeenUserFullName", "LastSeenUserFullName")),
    registeredDeviceId: asOptionalNumber(pick(record, "registeredDeviceId", "RegisteredDeviceId")),
  };
}

export function normalizeAppDeviceStatusListResponse(payload: unknown): AppDeviceStatusListResult {
  const listPayload = unwrapListPayload(payload);
  const devicesPayload = pick(listPayload, "items", "Items", "devices", "Devices", "data", "Data");
  const devices = Array.isArray(devicesPayload)
    ? devicesPayload
        .map(normalizeAppDeviceStatus)
        .filter((item): item is AppDeviceStatus => Boolean(item))
    : [];

  return {
    devices,
    pagination: normalizePagination(listPayload),
  };
}

export function normalizeAppDeviceStatusSummary(payload: unknown): AppDeviceStatusSummary {
  const record = unwrapListPayload(payload);
  return {
    total: asNumber(pick(record, "total", "Total"), 0),
    online: asNumber(pick(record, "online", "Online"), 0),
    offline: asNumber(pick(record, "offline", "Offline"), 0),
    android: asNumber(pick(record, "android", "Android"), 0),
    ios: asNumber(pick(record, "ios", "Ios", "iOS", "IOS"), 0),
    unknownSystem: asNumber(pick(record, "unknownSystem", "UnknownSystem"), 0),
  };
}

export function buildAppDeviceStatusListParams(query: AppDeviceStatusQuery = {}) {
  return {
    page: query.pageNumber,
    pageSize: query.pageSize,
    keyword: query.keyword?.trim() || undefined,
    storeCode: query.storeCode || undefined,
    deviceSystem: query.deviceSystem?.trim() || undefined,
    onlineState: query.onlineState && query.onlineState !== "all" ? query.onlineState : undefined,
  };
}

export async function fetchAppDeviceStatuses(
  query: AppDeviceStatusQuery = {}
): Promise<AppDeviceStatusListResult> {
  const apiClient = await getApiClient();
  const response = await apiClient.get("/mobile/app-device-status/paged", {
    params: buildAppDeviceStatusListParams(query),
  });
  return normalizeAppDeviceStatusListResponse(response.data);
}

export async function fetchAppDeviceStatusSummary(
  query: Omit<AppDeviceStatusQuery, "onlineState" | "pageNumber" | "pageSize"> = {}
): Promise<AppDeviceStatusSummary> {
  const apiClient = await getApiClient();
  const response = await apiClient.get("/mobile/app-device-status/summary", {
    params: {
      keyword: query.keyword?.trim() || undefined,
      storeCode: query.storeCode || undefined,
      deviceSystem: query.deviceSystem?.trim() || undefined,
    },
  });
  return normalizeAppDeviceStatusSummary(response.data);
}

export async function sendAppDeviceHeartbeat(
  payload: AppDeviceHeartbeatPayload,
  options?: { deviceSession?: PersistedDeviceSession | null }
) {
  const apiClient = await getApiClient();
  const deviceSession = options?.deviceSession;
  await apiClient.post("/mobile/app-device-status/heartbeat", payload, {
    headers:
      deviceSession?.hardwareId && deviceSession.authCode
        ? {
            "X-Device-Id": deviceSession.hardwareId,
            "X-Auth-Code": deviceSession.authCode,
          }
        : undefined,
  });
}
