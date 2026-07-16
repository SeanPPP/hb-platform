import { Linking, Platform } from "react-native";
import * as Location from "expo-location";
import { DeviceStorage } from "@/modules/device/storage";
import type { DeviceProfile } from "@/modules/device/types";
import { isIosReviewSessionActive } from "@/modules/ios-review/session";
import { IOS_REVIEW_LOCATION } from "@/modules/ios-review/helpers";

const IOS_REVIEW_HARDWARE_ID = "ios-review-device";

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

export interface LoginDeviceContextPayload {
  hardwareId: string;
  systemDeviceNumber?: string;
  deviceSystem: string;
  storeCode?: string;
}

export type OptionalLoginDeviceLocationPayload = LoginDeviceContextPayload & Partial<CapturedLocationPayload>;

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
  if (isIosReviewSessionActive()) {
    // 审核模式使用稳定的 Brisbane 演示坐标，不请求系统定位权限。
    return {
      locationLatitude: IOS_REVIEW_LOCATION.latitude,
      locationLongitude: IOS_REVIEW_LOCATION.longitude,
      locationAccuracy: IOS_REVIEW_LOCATION.accuracy,
      locationPermissionStatus: "granted",
      locationCapturedAtUtc: new Date().toISOString(),
    };
  }
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
  if (isIosReviewSessionActive()) {
    return {
      hardwareId: IOS_REVIEW_HARDWARE_ID,
      systemDeviceNumber: "REV-IOS-001",
      deviceSystem: "iOS",
    };
  }
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
  const [location, context] = await Promise.all([
    collectRequiredLocation(),
    collectLoginDeviceContext(profile),
  ]);

  return { ...context, ...location };
}

export async function collectLoginDeviceContext(
  profile?: Pick<DeviceProfile, "systemDeviceNumber" | "deviceSystem" | "storeCode"> | null,
): Promise<LoginDeviceContextPayload> {
  if (isIosReviewSessionActive()) {
    return {
      hardwareId: IOS_REVIEW_HARDWARE_ID,
      systemDeviceNumber: toOptionalString(profile?.systemDeviceNumber) ?? "REV-IOS-001",
      deviceSystem: toOptionalString(profile?.deviceSystem) ?? "iOS",
      storeCode: toOptionalString(profile?.storeCode) ?? "REV001",
    };
  }
  const [hardwareId, session] = await Promise.all([
    DeviceStorage.getInstallationId(),
    DeviceStorage.getSession(),
  ]);

  return {
    hardwareId,
    systemDeviceNumber:
      toOptionalString(profile?.systemDeviceNumber) ??
      toOptionalString(session?.systemDeviceNumber),
    deviceSystem: toOptionalString(profile?.deviceSystem) ?? getCurrentDeviceSystem(),
    storeCode: toOptionalString(profile?.storeCode) ?? toOptionalString(session?.storeCode),
  };
}

export async function collectOptionalLoginDeviceLocation(
  profile?: Pick<DeviceProfile, "systemDeviceNumber" | "deviceSystem" | "storeCode"> | null,
): Promise<OptionalLoginDeviceLocationPayload> {
  const [context, locationResult] = await Promise.all([
    collectLoginDeviceContext(profile),
    collectRequiredLocation()
      .then((location) => ({ ok: true as const, location }))
      .catch((error: unknown) => ({ ok: false as const, error })),
  ]);

  if (locationResult.ok) {
    return { ...context, ...locationResult.location };
  }

  // 普通账号密码登录不能被 Android 定位服务不可用卡死；定位只作为登录审计补充信息。
  console.warn("[login-location] 账号登录定位采集失败，继续按账号密码登录", locationResult.error);
  return context;
}

export async function openLocationInSystemMap(
  latitude: number,
  longitude: number,
  label = "Attendance location",
): Promise<boolean> {
  if (isIosReviewSessionActive()) {
    // 审核演示不跳转外部地图，页面仍保留固定坐标供核验。
    return false;
  }
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
