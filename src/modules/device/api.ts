import { Platform } from "react-native";
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
  const response = await apiClient.post("/register", payload);
  return normalizeDeviceProfile(response.data);
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
