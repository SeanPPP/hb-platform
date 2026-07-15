import { apiClient } from "@/shared/api/client";
import type {
  PosTerminalPermissionOption,
  StoreUserPosTerminalPermissions,
  UpdateStoreUserPosTerminalPermissionsPayload,
} from "@/modules/users/types";

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function unwrapSuccessfulEnvelope(value: unknown): unknown {
  if (
    isRecord(value) &&
    value.success === true &&
    Object.prototype.hasOwnProperty.call(value, "data")
  ) {
    return value.data;
  }

  return value;
}

function normalizePermissionCodes(value: unknown): string[] {
  if (!Array.isArray(value)) return [];
  return value.filter((item): item is string => typeof item === "string");
}

function isPermissionOption(value: unknown): value is PosTerminalPermissionOption {
  return (
    isRecord(value) &&
    typeof value.code === "string" &&
    typeof value.name === "string" &&
    typeof value.group === "string" &&
    typeof value.description === "string"
  );
}

export function normalizeStoreUserPosTerminalPermissions(
  value: unknown
): StoreUserPosTerminalPermissions {
  const normalizedValue = unwrapSuccessfulEnvelope(value);
  const source = isRecord(normalizedValue) ? normalizedValue : {};

  return {
    mode: typeof source.mode === "string" ? source.mode : "",
    // 服务端响应不完整时只保留结构有效的权限项，避免页面读取字段时报错。
    assignablePermissions: Array.isArray(source.assignablePermissions)
      ? source.assignablePermissions.filter(isPermissionOption)
      : [],
    inheritedPermissionCodes: normalizePermissionCodes(
      source.inheritedPermissionCodes
    ),
    overriddenPermissionCodes: normalizePermissionCodes(
      source.overriddenPermissionCodes
    ),
    grantedPermissionCodes: normalizePermissionCodes(
      source.grantedPermissionCodes
    ),
    effectivePermissionCodes: normalizePermissionCodes(
      source.effectivePermissionCodes
    ),
  };
}

export function buildStoreUserPosTerminalPermissionsPath(
  userGuid: string,
  storeGuid: string
) {
  return `/Users/guid/${encodeURIComponent(userGuid)}/stores/${encodeURIComponent(storeGuid)}/pos-terminal-permissions`;
}

export async function fetchStoreUserPosTerminalPermissions(
  userGuid: string,
  storeGuid: string
): Promise<StoreUserPosTerminalPermissions> {
  const response = await apiClient.get(
    buildStoreUserPosTerminalPermissionsPath(userGuid, storeGuid)
  );
  return normalizeStoreUserPosTerminalPermissions(response.data);
}

export async function updateStoreUserPosTerminalPermissions(
  payload: UpdateStoreUserPosTerminalPermissionsPayload
): Promise<StoreUserPosTerminalPermissions> {
  const response = await apiClient.put(
    buildStoreUserPosTerminalPermissionsPath(payload.userGuid, payload.storeGuid),
    { grantedPermissionCodes: payload.grantedPermissionCodes }
  );
  return normalizeStoreUserPosTerminalPermissions(response.data);
}

export async function restoreStoreUserPosTerminalPermissions(
  userGuid: string,
  storeGuid: string
): Promise<StoreUserPosTerminalPermissions> {
  const response = await apiClient.delete(
    buildStoreUserPosTerminalPermissionsPath(userGuid, storeGuid)
  );
  return normalizeStoreUserPosTerminalPermissions(response.data);
}
