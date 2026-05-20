import { Platform } from "react-native";
import { isAxiosError } from "axios";
import { apiClient } from "@/shared/api/client";
import type {
  DeviceProfile,
  DeviceRegistrationRequest,
  DeviceValidationRequest,
  DeviceValidationResult,
} from "@/modules/device/types";

function normalizeDeviceProfile(payload: unknown): DeviceProfile {
  const data = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;

  return {
    id: Number(data.deviceId ?? data.id ?? data.Id ?? 0),
    hardwareId:
      (typeof data.hardwareId === "string" && data.hardwareId) ||
      (typeof data.HardwareId === "string" && data.HardwareId) ||
      "",
    systemDeviceNumber:
      (typeof data.systemDeviceNumber === "string" && data.systemDeviceNumber) ||
      (typeof data.SystemDeviceNumber === "string" && data.SystemDeviceNumber) ||
      "",
    authCode:
      (typeof data.authCode === "string" && data.authCode) ||
      (typeof data.AuthCode === "string" && data.AuthCode) ||
      "",
    status: Number(data.status ?? data.Status ?? 0),
    statusDescription:
      (typeof data.statusDescription === "string" && data.statusDescription) ||
      (typeof data.StatusDescription === "string" && data.StatusDescription) ||
      resolveStatusDescription(Number(data.status ?? data.Status ?? 0)),
    deviceType:
      (typeof data.deviceType === "string" && data.deviceType) ||
      (typeof data.DeviceType === "string" && data.DeviceType) ||
      "Mobile",
    deviceSystem:
      (typeof data.deviceSystem === "string" && data.deviceSystem) ||
      (typeof data.DeviceSystem === "string" && data.DeviceSystem) ||
      (Platform.OS === "ios" ? "iOS" : "Android"),
    storeCode:
      (typeof data.storeCode === "string" && data.storeCode) ||
      (typeof data.StoreCode === "string" && data.StoreCode) ||
      null,
  };
}

function toObject(payload: unknown): Record<string, unknown> | null {
  return payload && typeof payload === "object" ? (payload as Record<string, unknown>) : null;
}

function readMessage(payload: unknown): string | null {
  const data = toObject(payload);
  if (!data) return null;

  const directMessage = data.message ?? data.Message ?? data.error ?? data.Error;
  if (typeof directMessage === "string" && directMessage.trim()) {
    return directMessage;
  }

  const nestedData = data.data ?? data.Data;
  if (nestedData && nestedData !== payload) {
    return readMessage(nestedData);
  }

  return null;
}

function maskHardwareId(hardwareId: string) {
  if (hardwareId.length <= 8) return hardwareId;
  return `${hardwareId.slice(0, 4)}...${hardwareId.slice(-4)}`;
}

function logRegisterDeviceFailure(payload: DeviceRegistrationRequest, error: unknown) {
  const responseData = isAxiosError(error) ? error.response?.data : undefined;
  const status = isAxiosError(error) ? error.response?.status : undefined;
  const statusText = isAxiosError(error) ? error.response?.statusText : undefined;
  const backendMessage = readMessage(responseData);
  const fallbackMessage = error instanceof Error ? error.message : "Unknown error";

  console.error("[Device Registration] 注册设备失败", {
    endpoint: "POST /register",
    storeCode: payload.storeCode || "(未选择门店)",
    deviceType: payload.deviceType,
    deviceSystem: payload.deviceSystem,
    hardwareId: maskHardwareId(payload.hardwareId),
    httpStatus: status ? `${status}${statusText ? ` ${statusText}` : ""}` : "(无 HTTP 响应)",
    message: backendMessage || fallbackMessage,
    backendResponse: responseData ?? null,
  });
}

function isDeviceAlreadyRegisteredError(error: unknown): boolean {
  if (!isAxiosError(error)) return false;
  const msg = readMessage(error.response?.data);
  return (
    msg !== null &&
    (msg.includes("已注册") ||
      msg.includes("already registered") ||
      msg.includes("already exists"))
  );
}

function resolveStatusDescription(status: number) {
  switch (status) {
    case -1:
      return "待确认";
    case 0:
      return "禁用";
    case 1:
      return "启用";
    case 2:
      return "锁定";
    case 3:
      return "未注册";
    default:
      return "未知";
  }
}

export async function registerDeviceApi(
  payload: DeviceRegistrationRequest
): Promise<DeviceProfile> {
  try {
    const response = await apiClient.post("/register", payload);
    return normalizeDeviceProfile(response.data);
  } catch (error) {
    if (isDeviceAlreadyRegisteredError(error)) {
      console.warn("[Device Registration] 设备已注册，尝试获取已有设备信息", {
        hardwareId: maskHardwareId(payload.hardwareId),
      });
      try {
        const existingProfile = await getDeviceProfileApi(payload.hardwareId);
        return {
          ...existingProfile,
          resolvedFromExisting: true,
        };
      } catch (profileError) {
        logRegisterDeviceFailure(payload, error);
        throw error;
      }
    }
    logRegisterDeviceFailure(payload, error);
    throw error;
  }
}

export async function validateDeviceAuthApi(
  payload: DeviceValidationRequest
): Promise<DeviceValidationResult> {
  const response = await apiClient.post("/validate-auth", payload);
  const data = (response.data && typeof response.data === "object"
    ? response.data
    : {}) as Record<string, unknown>;

  return {
    isValid: Boolean(data.isValid ?? data.IsValid),
    newAuthCode:
      (typeof data.newAuthCode === "string" && data.newAuthCode) ||
      (typeof data.NewAuthCode === "string" && data.NewAuthCode) ||
      null,
  };
}

export async function getDeviceProfileApi(hardwareId: string): Promise<DeviceProfile> {
  const response = await apiClient.get(`/by-hardware-id/${encodeURIComponent(hardwareId)}`);
  return normalizeDeviceProfile(response.data);
}
