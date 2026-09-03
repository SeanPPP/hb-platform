import type {
  MobileDeviceAccountTokenResponse,
  MobileDeviceAccountSessionStore,
  MobileDeviceAccountSessionUser,
  MobileDeviceActivationBinding,
  MobileDeviceActivationCommitResult,
  MobileDeviceActivationPreview,
} from "./types";

const GUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/iu;
const MAX_LEGACY_IDENTIFIER_LENGTH = 100;

function isBoundedIdentifier(value: string) {
  return Boolean(value.trim()) && value.length <= MAX_LEGACY_IDENTIFIER_LENGTH;
}

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === "object"
    ? (value as Record<string, unknown>)
    : {};
}

function readString(record: Record<string, unknown>, ...names: string[]) {
  for (const name of names) {
    if (typeof record[name] === "string") {
      return record[name] as string;
    }
  }
  return "";
}

function readOptionalString(
  record: Record<string, unknown>,
  ...names: string[]
): string | null {
  return readString(record, ...names) || null;
}

function readNumber(record: Record<string, unknown>, ...names: string[]) {
  for (const name of names) {
    const raw = record[name];
    if (typeof raw !== "number" && typeof raw !== "string") {
      continue;
    }
    const value = Number(raw);
    if (Number.isFinite(value)) {
      return value;
    }
  }
  return 0;
}

function readStringArray(record: Record<string, unknown>, ...names: string[]) {
  for (const name of names) {
    const value = record[name];
    if (Array.isArray(value) && value.every((item) => typeof item === "string")) {
      return value as string[];
    }
  }
  return null;
}

function normalizeSessionStore(value: unknown): MobileDeviceAccountSessionStore | null {
  const data = asRecord(value);
  const storeGuid = readString(data, "storeGuid", "StoreGuid");
  const storeCode = readString(data, "storeCode", "StoreCode");
  const storeName = readString(data, "storeName", "StoreName");
  const isPrimary = data.isPrimary ?? data.IsPrimary;
  if (
    !isBoundedIdentifier(storeGuid) ||
    !storeCode ||
    !storeName ||
    typeof isPrimary !== "boolean"
  ) {
    return null;
  }
  return { storeGuid, storeCode, storeName, isPrimary };
}

function normalizeSessionUser(value: unknown): MobileDeviceAccountSessionUser | null {
  const data = asRecord(value);
  const userGuid = readString(data, "userGuid", "UserGuid");
  const username = readString(data, "username", "Username");
  const roles = readStringArray(data, "roles", "Roles");
  const rawStores = data.stores ?? data.Stores;
  if (!isBoundedIdentifier(userGuid) || !username || !roles || !Array.isArray(rawStores)) {
    return null;
  }
  const stores = rawStores.map(normalizeSessionStore);
  if (stores.some((store) => !store)) {
    return null;
  }
  return {
    userGuid,
    username,
    fullName: readOptionalString(data, "fullName", "FullName"),
    roles,
    stores: stores as MobileDeviceAccountSessionStore[],
  };
}

function normalizeBinding(value: unknown): MobileDeviceActivationBinding | null {
  const data = asRecord(value);
  const bindingId = readString(data, "bindingId", "BindingId");
  const deviceRegistrationId = readNumber(
    data,
    "deviceRegistrationId",
    "DeviceRegistrationId",
  );
  const deviceCode = readString(data, "deviceCode", "DeviceCode");
  const storeCode = readString(data, "storeCode", "StoreCode");
  const deviceSystem = readString(data, "deviceSystem", "DeviceSystem");
  const targetUserGuid = readString(data, "targetUserGuid", "TargetUserGuid");
  const targetUsername = readString(data, "targetUsername", "TargetUsername");
  const boundAtUtc = readString(data, "boundAtUtc", "BoundAtUtc");
  if (
    !GUID_PATTERN.test(bindingId) ||
    !Number.isSafeInteger(deviceRegistrationId) ||
    deviceRegistrationId <= 0 ||
    !deviceCode ||
    !storeCode ||
    !deviceSystem ||
    !isBoundedIdentifier(targetUserGuid) ||
    !targetUsername ||
    !boundAtUtc
  ) {
    return null;
  }

  return {
    bindingId,
    deviceRegistrationId,
    deviceCode,
    storeCode,
    storeName: readString(data, "storeName", "StoreName") || storeCode,
    deviceSystem,
    targetUserGuid,
    targetUsername,
    targetFullName: readOptionalString(data, "targetFullName", "TargetFullName"),
    boundAtUtc,
  };
}

export function normalizeMobileDeviceActivationCommitResult(
  value: unknown,
): MobileDeviceActivationCommitResult {
  const data = asRecord(value);
  return {
    isAllowed: Boolean(data.isAllowed ?? data.IsAllowed),
    reasonCode: readString(data, "reasonCode", "ReasonCode"),
    message: readString(data, "message", "Message"),
    binding: normalizeBinding(data.binding ?? data.Binding),
  };
}

export function normalizeMobileDeviceActivationPreview(
  value: unknown,
): MobileDeviceActivationPreview {
  const data = asRecord(value);
  return {
    isAllowed: Boolean(data.isAllowed ?? data.IsAllowed),
    reasonCode: readString(data, "reasonCode", "ReasonCode"),
    storeCode: readOptionalString(data, "storeCode", "StoreCode"),
    storeName: readOptionalString(data, "storeName", "StoreName"),
    deviceSystem: readOptionalString(data, "deviceSystem", "DeviceSystem"),
    targetUsername: readOptionalString(data, "targetUsername", "TargetUsername"),
    targetFullName: readOptionalString(data, "targetFullName", "TargetFullName"),
    assignedStoreCount: readNumber(
      data,
      "assignedStoreCount",
      "AssignedStoreCount",
    ),
    expiresAtUtc: readOptionalString(data, "expiresAtUtc", "ExpiresAtUtc"),
    message: readString(data, "message", "Message"),
  };
}

export function normalizeMobileDeviceAccountToken(
  value: unknown,
): MobileDeviceAccountTokenResponse {
  const data = asRecord(value);
  const accessToken = readString(data, "accessToken", "AccessToken");
  const expiresAtUtc = readString(data, "expiresAtUtc", "ExpiresAtUtc");
  const tokenType = readString(data, "tokenType", "TokenType");
  const sessionKind = readString(data, "sessionKind", "SessionKind");
  const user = normalizeSessionUser(data.user ?? data.User);
  if (
    !accessToken ||
    !expiresAtUtc ||
    tokenType !== "Bearer" ||
    sessionKind !== "deviceAccount" ||
    !user
  ) {
    throw new Error("DEVICE_ACCOUNT_EXCHANGE_INVALID_RESPONSE");
  }

  return {
    accessToken,
    expiresAtUtc,
    tokenType,
    sessionKind: "deviceAccount",
    user,
  };
}
