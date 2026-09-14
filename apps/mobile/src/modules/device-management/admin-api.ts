import type {
  ActivationCodeCreatePayload,
  ActivationCodeCreateResult,
  DeviceActivationGrant,
  DeviceActivationGrantList,
  DeviceActivationStatus,
  DeviceActivationSystem,
  DeviceRegistrationDetail,
  EmergencyLoginGrant,
  EmergencyLoginGrantCreateResult,
  ManageableAccount,
  ManageableStore,
  MobileActivationCodeCreatePayload,
  UpdateDeviceRegistrationPayload,
} from "@/modules/device-management/admin-types";

const POS_ACTIVATION_PATH = "/react/v1/device-activation-codes";
const MOBILE_ACTIVATION_PATH = "/react/v1/mobile-device-activation-codes";
const DEVICE_REGISTRATION_PATH = "/react/v1/device-registration";
const EMERGENCY_LOGIN_PATH = "/react/v1/emergency-login-grants";

async function getApiClient() {
  const { apiClient } = await import("@/shared/api/client");
  return apiClient;
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;
}

function pick(record: Record<string, unknown>, ...keys: string[]) {
  for (const key of keys) {
    if (record[key] !== undefined && record[key] !== null) {
      return record[key];
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

function asNullableString(value: unknown): string | null {
  return value === null ? null : asString(value) ?? null;
}

function asNumber(value: unknown, fallback: number) {
  const numeric = typeof value === "string" && value.trim() ? Number(value) : value;
  return typeof numeric === "number" && Number.isFinite(numeric) ? numeric : fallback;
}

function asBoolean(value: unknown, fallback = false) {
  if (typeof value === "boolean") return value;
  if (typeof value === "string") {
    const normalized = value.trim().toLowerCase();
    if (["true", "1", "yes"].includes(normalized)) return true;
    if (["false", "0", "no"].includes(normalized)) return false;
  }
  return fallback;
}

function normalizeSystem(value: unknown): DeviceActivationSystem | null {
  const system = asString(value);
  return system === "Windows" || system === "iPadOS" || system === "Android" || system === "iOS"
    ? system
    : null;
}

function normalizeStatus(value: unknown): DeviceActivationStatus {
  switch (asString(value)?.toLowerCase()) {
    case "expired": return "Expired";
    case "revoked": return "Revoked";
    case "consumed": return "Consumed";
    default: return "Available";
  }
}

function normalizeActivationGrant(value: unknown): DeviceActivationGrant | null {
  const record = asRecord(value);
  if (!record) return null;
  const grantId = asString(pick(record, "grantId", "GrantId"));
  const storeCode = asString(pick(record, "storeCode", "StoreCode"));
  const deviceSystem = normalizeSystem(pick(record, "deviceSystem", "DeviceSystem"));
  if (!grantId || !storeCode || !deviceSystem) return null;
  const consumptionKind = asString(pick(record, "consumptionKind", "ConsumptionKind"));
  return {
    grantId,
    storeCode,
    storeName: asNullableString(pick(record, "storeName", "StoreName")),
    deviceSystem,
    status: normalizeStatus(pick(record, "status", "Status")),
    createdAtUtc: asString(pick(record, "createdAtUtc", "CreatedAtUtc")) ?? "",
    createdBy: asString(pick(record, "createdBy", "CreatedBy")) ?? "",
    reason: asString(pick(record, "reason", "Reason", "createReason", "CreateReason")) ?? "",
    expiresAtUtc: asString(pick(record, "expiresAtUtc", "ExpiresAtUtc")) ?? "",
    revokedAtUtc: asNullableString(pick(record, "revokedAtUtc", "RevokedAtUtc")),
    revokedBy: asNullableString(pick(record, "revokedBy", "RevokedBy")),
    revokeReason: asNullableString(pick(record, "revokeReason", "RevokeReason")),
    consumedAtUtc: asNullableString(pick(record, "consumedAtUtc", "ConsumedAtUtc")),
    consumedHardwareId: asNullableString(pick(record, "consumedHardwareId", "ConsumedHardwareId")),
    consumedDeviceCode: asNullableString(pick(record, "consumedDeviceCode", "ConsumedDeviceCode")),
    consumptionKind: consumptionKind === "Initial" || consumptionKind === "Rebind" ? consumptionKind : null,
    previousStoreCode: asNullableString(pick(record, "previousStoreCode", "PreviousStoreCode")),
    previousDeviceCode: asNullableString(pick(record, "previousDeviceCode", "PreviousDeviceCode")),
    targetUserGuid: asNullableString(pick(record, "targetUserGuid", "TargetUserGuid")),
    targetUsername: asNullableString(pick(record, "targetUsername", "TargetUsername")),
    targetFullName: asNullableString(pick(record, "targetFullName", "TargetFullName")),
  };
}

function normalizeDeviceRegistration(value: unknown): DeviceRegistrationDetail {
  const record = asRecord(value) ?? {};
  return {
    id: asNumber(pick(record, "id", "ID", "Id"), 0),
    hardwareId: asString(pick(record, "hardwareId", "HardwareId", "设备硬件识别码")) ?? "",
    systemDeviceNumber: asString(pick(record, "systemDeviceNumber", "SystemDeviceNumber", "系统设备编号")) ?? "",
    storeCode: asNullableString(pick(record, "storeCode", "StoreCode", "分店代码")),
    storeName: asNullableString(pick(record, "storeName", "StoreName", "分店名称")),
    deviceType: asString(pick(record, "deviceType", "DeviceType", "设备类型")) ?? "",
    deviceSystem: asString(pick(record, "deviceSystem", "DeviceSystem", "设备系统")) ?? "",
    status: asNumber(pick(record, "status", "Status", "设备状态"), -1),
    statusDescription: asString(pick(record, "statusDescription", "StatusDescription", "状态描述", "设备状态描述")) ?? "",
    allowTransactions: asBoolean(pick(record, "allowTransactions", "AllowTransactions", "是否允许交易"), true),
    remark: asNullableString(pick(record, "remark", "remarks", "Remark", "Remarks", "备注")),
    createdAt: asNullableString(pick(record, "createdAt", "CreatedAt", "创建时间")),
    lastModified: asNullableString(pick(record, "lastModified", "LastModified", "最后修改时间")),
    createdBy: asNullableString(pick(record, "createdBy", "CreatedBy", "创建人")),
    lastModifiedBy: asNullableString(pick(record, "lastModifiedBy", "LastModifiedBy", "最后修改人")),
    isOnline: asBoolean(pick(record, "isOnline", "IsOnline", "是否在线")),
    lastHeartbeatAt: asNullableString(pick(record, "lastHeartbeatAt", "LastHeartbeatAt", "最后心跳时间")),
    currentCashierId: asNullableString(pick(record, "currentCashierId", "CurrentCashierId", "当前收银员ID")),
    currentCashierName: asNullableString(pick(record, "currentCashierName", "CurrentCashierName", "当前收银员姓名")),
    cashierLoginAt: asNullableString(pick(record, "cashierLoginAt", "CashierLoginAt", "收银员登录时间")),
  };
}

function normalizeEmergencyGrant(value: unknown): EmergencyLoginGrant | null {
  const record = asRecord(value);
  if (!record) return null;
  const grantId = asString(pick(record, "grantId", "GrantId"));
  const storeCode = asString(pick(record, "storeCode", "StoreCode"));
  if (!grantId || !storeCode) return null;
  const rawStatus = asString(pick(record, "status", "Status"))?.toLowerCase();
  return {
    grantId,
    storeCode,
    businessDate: asString(pick(record, "businessDate", "BusinessDate")) ?? "",
    keyId: asString(pick(record, "keyId", "KeyId")) ?? "",
    permissionProfile: "AllPosTerminal",
    issuedBy: asString(pick(record, "issuedBy", "IssuedBy")) ?? "",
    reason: asString(pick(record, "reason", "Reason", "issuedReason", "IssuedReason")) ?? "",
    issuedAtUtc: asString(pick(record, "issuedAtUtc", "IssuedAtUtc")) ?? "",
    expiresAtUtc: asString(pick(record, "expiresAtUtc", "ExpiresAtUtc")) ?? "",
    revokedBy: asNullableString(pick(record, "revokedBy", "RevokedBy")),
    revokedAtUtc: asNullableString(pick(record, "revokedAtUtc", "RevokedAtUtc")),
    revokeReason: asNullableString(pick(record, "revokeReason", "RevokeReason", "revokedReason", "RevokedReason")),
    status: rawStatus === "active" ? "Active" : rawStatus === "revoked" ? "Revoked" : "Expired",
  };
}

function normalizeStore(value: unknown): ManageableStore | null {
  const record = asRecord(value);
  const storeCode = record && asString(pick(record, "storeCode", "StoreCode"));
  const storeName = record && asString(pick(record, "storeName", "StoreName"));
  return storeCode && storeName ? { storeCode, storeName } : null;
}

export async function getDeviceRegistrationDetail(id: number) {
  const apiClient = await getApiClient();
  const response = await apiClient.get(`${DEVICE_REGISTRATION_PATH}/${id}`);
  return normalizeDeviceRegistration(response.data);
}

export async function updateDeviceRegistration(id: number, payload: UpdateDeviceRegistrationPayload) {
  const apiClient = await getApiClient();
  const response = await apiClient.put(`${DEVICE_REGISTRATION_PATH}/${id}`, {
    设备类型: payload.deviceType,
    设备系统: payload.deviceSystem,
    是否允许交易: payload.allowTransactions,
    备注: payload.remark,
  });
  return normalizeDeviceRegistration(response.data);
}

export async function getActivationGrants(
  path: typeof POS_ACTIVATION_PATH | typeof MOBILE_ACTIVATION_PATH,
  params: { page: number; pageSize: number; storeCode?: string; deviceSystem?: DeviceActivationSystem; status?: DeviceActivationStatus }
): Promise<DeviceActivationGrantList> {
  const apiClient = await getApiClient();
  const response = await apiClient.get(path, { params });
  const record = asRecord(response.data) ?? {};
  const rawItems = pick(record, "items", "Items", "grants", "Grants");
  const items = Array.isArray(rawItems)
    ? rawItems.map(normalizeActivationGrant).filter((item): item is DeviceActivationGrant => Boolean(item))
    : [];
  const pageSize = asNumber(pick(record, "pageSize", "PageSize"), Math.max(items.length, 20));
  const total = asNumber(pick(record, "total", "Total"), items.length);
  return { items, total, page: asNumber(pick(record, "page", "Page"), 1), pageSize, totalPages: asNumber(pick(record, "totalPages", "TotalPages"), Math.max(1, Math.ceil(total / pageSize))) };
}

export const getPosActivationGrants = (params: Parameters<typeof getActivationGrants>[1]) => getActivationGrants(POS_ACTIVATION_PATH, params);
export const getMobileActivationGrants = (params: Parameters<typeof getActivationGrants>[1]) => getActivationGrants(MOBILE_ACTIVATION_PATH, params);

export async function getManageableStores(path: typeof POS_ACTIVATION_PATH | typeof MOBILE_ACTIVATION_PATH) {
  const apiClient = await getApiClient();
  const response = await apiClient.get(`${path}/manageable-stores`);
  return Array.isArray(response.data) ? response.data.map(normalizeStore).filter((item): item is ManageableStore => Boolean(item)) : [];
}

export const getPosManageableStores = () => getManageableStores(POS_ACTIVATION_PATH);
export const getMobileManageableStores = () => getManageableStores(MOBILE_ACTIVATION_PATH);

export async function getMobileManageableAccounts(storeCode: string): Promise<ManageableAccount[]> {
  const apiClient = await getApiClient();
  const response = await apiClient.get(`${MOBILE_ACTIVATION_PATH}/manageable-accounts`, { params: { storeCode } });
  if (!Array.isArray(response.data)) return [];
  return response.data.flatMap((value) => {
    const record = asRecord(value);
    const userGuid = record && asString(pick(record, "userGuid", "UserGuid"));
    const username = record && asString(pick(record, "username", "Username"));
    return userGuid && username ? [{ userGuid, username, fullName: asNullableString(pick(record!, "fullName", "FullName")) }] : [];
  });
}

async function createActivationCode(path: typeof POS_ACTIVATION_PATH | typeof MOBILE_ACTIVATION_PATH, payload: ActivationCodeCreatePayload | MobileActivationCodeCreatePayload): Promise<ActivationCodeCreateResult> {
  const apiClient = await getApiClient();
  const response = await apiClient.post(path, { ...payload, reason: payload.reason.trim() });
  const data = asRecord(response.data);
  const grant = normalizeActivationGrant(data ? pick(data, "grant", "Grant") ?? data : null);
  const activationCode = data ? asString(pick(data, "activationCode", "ActivationCode")) : undefined;
  if (!grant || !activationCode) throw new Error("DEVICE_ACTIVATION_CREATE_RESPONSE_INVALID");
  return { grant, activationCode };
}

export const createPosActivationCode = (payload: ActivationCodeCreatePayload) => createActivationCode(POS_ACTIVATION_PATH, payload);
export const createMobileActivationCode = (payload: MobileActivationCodeCreatePayload) => createActivationCode(MOBILE_ACTIVATION_PATH, payload);

async function revokeActivationCode(path: typeof POS_ACTIVATION_PATH | typeof MOBILE_ACTIVATION_PATH, grantId: string, reason: string) {
  const apiClient = await getApiClient();
  const response = await apiClient.post(`${path}/${encodeURIComponent(grantId)}/revoke`, { reason: reason.trim() });
  const grant = normalizeActivationGrant(response.data);
  if (!grant) throw new Error("DEVICE_ACTIVATION_REVOKE_RESPONSE_INVALID");
  return grant;
}

export const revokePosActivationCode = (grantId: string, reason: string) => revokeActivationCode(POS_ACTIVATION_PATH, grantId, reason);
export const revokeMobileActivationCode = (grantId: string, reason: string) => revokeActivationCode(MOBILE_ACTIVATION_PATH, grantId, reason);

export async function getEmergencyLoginGrant(storeCode: string) {
  const apiClient = await getApiClient();
  const response = await apiClient.get(EMERGENCY_LOGIN_PATH, { params: { storeCode } });
  if (Array.isArray(response.data)) {
    const grants = response.data.map(normalizeEmergencyGrant).filter((grant): grant is EmergencyLoginGrant => Boolean(grant));
    return grants.find((grant) => grant.status === "Active") ?? grants[0] ?? null;
  }
  return normalizeEmergencyGrant(response.data);
}

export async function createEmergencyLoginGrant(storeCode: string, reason: string): Promise<EmergencyLoginGrantCreateResult> {
  const apiClient = await getApiClient();
  const response = await apiClient.post(EMERGENCY_LOGIN_PATH, { storeCode, reason: reason.trim() });
  const data = asRecord(response.data);
  const grant = normalizeEmergencyGrant(data ? pick(data, "grant", "Grant") : null);
  const token = data ? asString(pick(data, "token", "Token")) : undefined;
  if (!grant || !token) throw new Error("EMERGENCY_LOGIN_CREATE_RESPONSE_INVALID");
  return { grant, token };
}

export async function revokeEmergencyLoginGrant(grantId: string, reason: string) {
  const apiClient = await getApiClient();
  const response = await apiClient.post(`${EMERGENCY_LOGIN_PATH}/${encodeURIComponent(grantId)}/revoke`, { reason: reason.trim() });
  const grant = normalizeEmergencyGrant(response.data);
  if (!grant) throw new Error("EMERGENCY_LOGIN_REVOKE_RESPONSE_INVALID");
  return grant;
}
