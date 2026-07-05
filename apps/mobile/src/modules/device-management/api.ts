import { normalizeDeviceStatus } from "@/modules/device-management/status";
import type {
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
