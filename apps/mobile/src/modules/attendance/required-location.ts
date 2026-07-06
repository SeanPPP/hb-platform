import { Linking, Platform } from "react-native";
import * as Location from "expo-location";
import { DeviceStorage } from "@/modules/device/storage";
import type { DeviceProfile } from "@/modules/device/types";

export class RequiredLocationError extends Error {
  code = "LOCATION_REQUIRED";

  constructor(message = "Location permission is required") {
    super(message);
    this.name = "RequiredLocationError";
  }
}

export interface CapturedLocationPayload {
  locationLatitude: number;
  locationLongitude: number;
  locationAccuracy?: number;
  locationPermissionStatus: "granted";
  locationCapturedAtUtc: string;
}

export interface LoginDeviceLocationPayload extends CapturedLocationPayload {
  hardwareId: string;
  systemDeviceNumber?: string;
  deviceSystem: string;
  storeCode?: string;
}

export interface AttendanceDeviceContext {
  hardwareId: string;
  systemDeviceNumber?: string;
  deviceSystem: string;
}

function getCurrentDeviceSystem() {
  return Platform.OS === "ios" ? "iOS" : "Android";
}

function toOptionalString(value?: string | null) {
  const normalized = value?.trim();
  return normalized ? normalized : undefined;
}

export function isRequiredLocationError(error: unknown) {
  return error instanceof RequiredLocationError;
}

export async function collectRequiredLocation(): Promise<CapturedLocationPayload> {
  const permission = await Location.requestForegroundPermissionsAsync();
  if (permission.status !== "granted") {
    throw new RequiredLocationError();
  }

  // 登录和打卡是审计动作，必须用当前精确坐标，不能复用旧缓存。
  const position = await Location.getCurrentPositionAsync({
    accuracy: Location.Accuracy.High,
  });

  return {
    locationLatitude: position.coords.latitude,
    locationLongitude: position.coords.longitude,
    locationAccuracy: position.coords.accuracy ?? undefined,
    locationPermissionStatus: "granted",
    locationCapturedAtUtc: new Date(position.timestamp).toISOString(),
  };
}

export async function getAttendanceDeviceContext(): Promise<AttendanceDeviceContext> {
  const [hardwareId, session] = await Promise.all([
    DeviceStorage.getInstallationId(),
    DeviceStorage.getSession(),
  ]);

  return {
    hardwareId,
    systemDeviceNumber: toOptionalString(session?.systemDeviceNumber),
    deviceSystem: getCurrentDeviceSystem(),
  };
}

export async function collectLoginDeviceLocation(
  profile?: Pick<DeviceProfile, "systemDeviceNumber" | "deviceSystem" | "storeCode"> | null,
): Promise<LoginDeviceLocationPayload> {
  const [location, hardwareId, session] = await Promise.all([
    collectRequiredLocation(),
    DeviceStorage.getInstallationId(),
    DeviceStorage.getSession(),
  ]);

  return {
    ...location,
    hardwareId,
    systemDeviceNumber:
      toOptionalString(profile?.systemDeviceNumber) ??
      toOptionalString(session?.systemDeviceNumber),
    deviceSystem: toOptionalString(profile?.deviceSystem) ?? getCurrentDeviceSystem(),
    storeCode: toOptionalString(profile?.storeCode) ?? toOptionalString(session?.storeCode),
  };
}

export async function openLocationInSystemMap(
  latitude: number,
  longitude: number,
  label = "Attendance location",
): Promise<boolean> {
  const encodedLabel = encodeURIComponent(label);
  const urls =
    Platform.OS === "ios"
      ? [
          `maps://?ll=${latitude},${longitude}&q=${encodedLabel}`,
          `https://maps.apple.com/?ll=${latitude},${longitude}&q=${encodedLabel}`,
        ]
      : [
          `geo:${latitude},${longitude}?q=${latitude},${longitude}(${encodedLabel})`,
          `https://maps.google.com/?q=${latitude},${longitude}`,
        ];

  for (const url of urls) {
    try {
      const canOpen = await Linking.canOpenURL(url);
      if (canOpen) {
        await Linking.openURL(url);
        return true;
      }
    } catch {
      // 继续尝试下一个系统地图 URL，避免单个 handler 异常阻断用户查看。
    }
  }

  return false;
}
