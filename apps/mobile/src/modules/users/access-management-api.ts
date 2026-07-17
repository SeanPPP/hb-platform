import { getAllStores } from "@/modules/shop/api";
import type { Store } from "@/modules/shop/types";
import { apiClient } from "@/shared/api/client";
import { buildAssignableRoleGuids } from "./access-management";
import type {
  AccessPermission,
  AccessPermissionCategory,
  AccessStoreOption,
  AssignUserAccessRolesInput,
  AssignUserAccessStoresInput,
  AssignUserDirectPermissionsInput,
  UserAccessPermissionInheritedSource,
  UserAccessPermissionAccess,
  UserAccessPermissionState,
  UserAccessRole,
  UserAccessStore,
} from "./access-management-types";

const INVALID_RESPONSE_MESSAGE = "Invalid access management response";

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function readValue(source: Record<string, unknown>, ...keys: string[]) {
  for (const key of keys) {
    if (Object.prototype.hasOwnProperty.call(source, key)) return source[key];
  }
  return undefined;
}

function unwrapAccessEnvelope(value: unknown): unknown {
  if (!isRecord(value)) return value;
  const success = readValue(
    value,
    "success",
    "isSuccess",
    "Success",
    "IsSuccess",
  );
  if (success === false) {
    const message = readValue(value, "message", "Message");
    // apiClient 正常会先抛出失败 envelope；此处保留严格兜底，禁止吞成空数据。
    throw new Error(typeof message === "string" ? message : "Request failed");
  }

  if (success === true) {
    const data = readValue(value, "data", "Data");
    if (data === undefined) throw new Error(INVALID_RESPONSE_MESSAGE);
    return data;
  }

  return value;
}

function readRequiredString(
  source: Record<string, unknown>,
  ...keys: string[]
) {
  const value = readValue(source, ...keys);
  if (typeof value !== "string" || !value.trim()) {
    throw new Error(INVALID_RESPONSE_MESSAGE);
  }
  return value.trim();
}

function readOptionalString(
  source: Record<string, unknown>,
  ...keys: string[]
) {
  const value = readValue(source, ...keys);
  return typeof value === "string" && value.trim() ? value.trim() : undefined;
}

function readOptionalBoolean(
  source: Record<string, unknown>,
  ...keys: string[]
) {
  const value = readValue(source, ...keys);
  return typeof value === "boolean" ? value : undefined;
}

function readRequiredBoolean(
  source: Record<string, unknown>,
  ...keys: string[]
) {
  const value = readOptionalBoolean(source, ...keys);
  if (value === undefined) throw new Error(INVALID_RESPONSE_MESSAGE);
  return value;
}

function normalizeStringArray(value: unknown) {
  if (!Array.isArray(value) || value.some((item) => typeof item !== "string")) {
    throw new Error(INVALID_RESPONSE_MESSAGE);
  }
  return Array.from(new Set(value.map((item) => item.trim()).filter(Boolean)));
}

function normalizeArray(value: unknown) {
  const unwrapped = unwrapAccessEnvelope(value);
  if (!Array.isArray(unwrapped)) throw new Error(INVALID_RESPONSE_MESSAGE);
  return unwrapped;
}

function normalizeUserAccessStore(value: unknown): UserAccessStore {
  if (!isRecord(value)) throw new Error(INVALID_RESPONSE_MESSAGE);
  const isActive = readOptionalBoolean(value, "isActive", "IsActive");
  const assignedAt = readOptionalString(value, "assignedAt", "AssignedAt");
  return {
    storeGUID: readRequiredString(value, "storeGUID", "StoreGUID"),
    storeCode: readRequiredString(value, "storeCode", "StoreCode"),
    storeName: readRequiredString(value, "storeName", "StoreName"),
    ...(isActive === undefined ? {} : { isActive }),
    isManageable:
      readOptionalBoolean(
        value,
        "isManageable",
        "IsManageable",
        "isPrimary",
        "IsPrimary",
      ) ?? false,
    ...(assignedAt ? { assignedAt } : {}),
  };
}

function normalizeAccessRole(value: unknown): UserAccessRole {
  if (!isRecord(value)) throw new Error(INVALID_RESPONSE_MESSAGE);
  const description = readOptionalString(value, "description", "Description");
  const isActive = readOptionalBoolean(value, "isActive", "IsActive");
  return {
    roleGUID: readRequiredString(value, "roleGUID", "RoleGUID"),
    roleName: readRequiredString(value, "roleName", "RoleName"),
    ...(description ? { description } : {}),
    ...(isActive === undefined ? {} : { isActive }),
  };
}

function normalizeAccessPermission(value: unknown): AccessPermission {
  if (!isRecord(value)) throw new Error(INVALID_RESPONSE_MESSAGE);
  const description = readOptionalString(value, "description", "Description");
  const isSystemPermission = readOptionalBoolean(
    value,
    "isSystemPermission",
    "IsSystemPermission",
  );
  if (isSystemPermission === undefined)
    throw new Error(INVALID_RESPONSE_MESSAGE);

  return {
    name: readRequiredString(value, "name", "Name"),
    displayName: readRequiredString(value, "displayName", "DisplayName"),
    ...(description ? { description } : {}),
    category: readRequiredString(value, "category", "Category"),
    isSystemPermission,
  };
}

function normalizePermissionCategory(value: unknown): AccessPermissionCategory {
  if (!isRecord(value)) throw new Error(INVALID_RESPONSE_MESSAGE);
  const permissions = readValue(value, "permissions", "Permissions");
  if (!Array.isArray(permissions)) throw new Error(INVALID_RESPONSE_MESSAGE);
  const description = readOptionalString(value, "description", "Description");

  return {
    category: readRequiredString(value, "category", "Category"),
    displayName: readRequiredString(value, "displayName", "DisplayName"),
    ...(description ? { description } : {}),
    permissions: permissions.map(normalizeAccessPermission),
  };
}

function normalizeInheritedSource(
  value: unknown,
): UserAccessPermissionInheritedSource {
  if (!isRecord(value)) throw new Error(INVALID_RESPONSE_MESSAGE);
  return {
    roleName: readRequiredString(value, "roleName", "RoleName"),
    permissionCodes: normalizeStringArray(
      readValue(value, "permissionCodes", "PermissionCodes"),
    ),
  };
}

export function normalizeUserAccessStoreList(value: unknown) {
  return normalizeArray(value).map(normalizeUserAccessStore);
}

export function normalizeAccessStoreCatalog(
  value: unknown,
): AccessStoreOption[] {
  return normalizeArray(value).map((item) => {
    if (!isRecord(item)) throw new Error(INVALID_RESPONSE_MESSAGE);
    const isActive = readOptionalBoolean(item, "isActive", "IsActive");
    return {
      storeGUID: readRequiredString(item, "storeGUID", "StoreGUID"),
      storeCode: readRequiredString(item, "storeCode", "StoreCode"),
      storeName: readRequiredString(item, "storeName", "StoreName"),
      ...(isActive === undefined ? {} : { isActive }),
    };
  });
}

export function normalizeAccessRoleList(value: unknown) {
  return normalizeArray(value).map(normalizeAccessRole);
}

export function normalizeAccessPermissionCatalog(value: unknown) {
  return normalizeArray(value).map(normalizePermissionCategory);
}

export function normalizeUserAccessPermissionState(
  value: unknown,
): UserAccessPermissionState {
  const unwrapped = unwrapAccessEnvelope(value);
  if (!isRecord(unwrapped)) throw new Error(INVALID_RESPONSE_MESSAGE);
  const inheritedSources = readValue(
    unwrapped,
    "inheritedSources",
    "InheritedSources",
  );
  if (!Array.isArray(inheritedSources))
    throw new Error(INVALID_RESPONSE_MESSAGE);

  return {
    userGuid: readRequiredString(unwrapped, "userGuid", "UserGuid", "UserGUID"),
    isSuperAdmin: readRequiredBoolean(
      unwrapped,
      "isSuperAdmin",
      "IsSuperAdmin",
    ),
    implicitAllPermissions: readRequiredBoolean(
      unwrapped,
      "implicitAllPermissions",
      "ImplicitAllPermissions",
    ),
    inheritedPermissionCodes: normalizeStringArray(
      readValue(
        unwrapped,
        "inheritedPermissionCodes",
        "InheritedPermissionCodes",
      ),
    ),
    directPermissionCodes: normalizeStringArray(
      readValue(unwrapped, "directPermissionCodes", "DirectPermissionCodes"),
    ),
    effectivePermissionCodes: normalizeStringArray(
      readValue(
        unwrapped,
        "effectivePermissionCodes",
        "EffectivePermissionCodes",
      ),
    ),
    inheritedSources: inheritedSources.map(normalizeInheritedSource),
  };
}

export function normalizeUserAccessPermissionAccess(
  value: unknown,
): UserAccessPermissionAccess {
  const unwrapped = unwrapAccessEnvelope(value);
  if (!isRecord(unwrapped)) throw new Error(INVALID_RESPONSE_MESSAGE);

  return {
    state: normalizeUserAccessPermissionState(
      readValue(unwrapped, "state", "State"),
    ),
    categories: normalizeAccessPermissionCatalog(
      readValue(unwrapped, "categories", "Categories"),
    ),
  };
}

function normalizeMutationResult(value: unknown) {
  const unwrapped = unwrapAccessEnvelope(value);
  if (typeof unwrapped !== "boolean") throw new Error(INVALID_RESPONSE_MESSAGE);
  return unwrapped;
}

function buildUserAccessPath(userGuid: string, suffix: string) {
  const normalizedUserGuid = userGuid.trim();
  if (!normalizedUserGuid) throw new Error("User GUID is required");
  return `/Users/guid/${encodeURIComponent(normalizedUserGuid)}/${suffix}`;
}

export async function fetchUserAccessStores(userGuid: string) {
  const response = await apiClient.get(buildUserAccessPath(userGuid, "stores"));
  return normalizeUserAccessStoreList(response.data);
}

export async function fetchUserAccessRoles(userGuid: string) {
  const response = await apiClient.get(buildUserAccessPath(userGuid, "roles"));
  return normalizeAccessRoleList(response.data);
}

export async function fetchUserAccessPermissionState(userGuid: string) {
  const response = await apiClient.get(
    buildUserAccessPath(userGuid, "permissions/state"),
  );
  return normalizeUserAccessPermissionState(response.data);
}

export async function fetchUserAccessPermissionAccess(userGuid: string) {
  const response = await apiClient.get(
    buildUserAccessPath(userGuid, "access-permissions"),
  );
  return normalizeUserAccessPermissionAccess(response.data);
}

export async function fetchAccessRoleCatalog() {
  const response = await apiClient.get("/Roles/active");
  return normalizeAccessRoleList(response.data);
}

export async function fetchAccessPermissionCatalog() {
  const response = await apiClient.get("/Roles/permissions");
  return normalizeAccessPermissionCatalog(response.data);
}

export async function fetchAccessStoreCatalog(
  loadStores: () => Promise<Store[]> = getAllStores,
) {
  return normalizeAccessStoreCatalog(await loadStores());
}

export async function assignUserAccessStores({
  userGuid,
  assignments,
}: AssignUserAccessStoresInput) {
  const body = assignments.map((assignment) => {
    const storeGUID = assignment.storeGUID.trim();
    if (!storeGUID) throw new Error("Store GUID is required");
    // 分店保存契约只发送关联主键和管理标记，禁止附带前端展示字段。
    return { StoreGUID: storeGUID, IsPrimary: assignment.isPrimary };
  });
  const response = await apiClient.post(
    buildUserAccessPath(userGuid, "stores"),
    body,
  );
  return normalizeMutationResult(response.data);
}

export async function assignUserAccessRoles({
  userGuid,
  roleGuids,
  roleCatalog,
}: AssignUserAccessRolesInput) {
  const response = await apiClient.post(
    buildUserAccessPath(userGuid, "roles"),
    {
      // StoreManager/店长/经理由分店管理关系派生，角色接口不得直接写入。
      RoleGuids: buildAssignableRoleGuids(
        roleCatalog,
        normalizeStringArray(roleGuids),
      ),
    },
  );
  return normalizeMutationResult(response.data);
}

export async function assignUserDirectPermissions({
  userGuid,
  permissions,
}: AssignUserDirectPermissionsInput) {
  const response = await apiClient.post(
    buildUserAccessPath(userGuid, "permissions"),
    { permissions: normalizeStringArray(permissions) },
  );
  return normalizeMutationResult(response.data);
}
