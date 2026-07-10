import * as Location from "expo-location";
import { AppAsyncStorage } from "@/shared/storage/async-storage";

export const ATTENDANCE_LOCATION_TASK = "hbweb-attendance-location-sample";
export const ACTIVE_SHIFT_STORAGE_KEY = "hbweb_attendance_active_shift_location";
export const LOCATION_SAMPLE_INTERVAL_MS = 20 * 60 * 1000;

export interface ActiveShiftLocationContext {
  storeCode: string;
  hardwareId?: string;
  systemDeviceNumber?: string;
  deviceSystem?: string;
  lastUploadedAtUtc?: string;
}

export async function readActiveShiftContext() {
  return AppAsyncStorage.getObject<ActiveShiftLocationContext>(
    ACTIVE_SHIFT_STORAGE_KEY,
  );
}

export async function writeActiveShiftContext(context: ActiveShiftLocationContext) {
  await AppAsyncStorage.setObject(ACTIVE_SHIFT_STORAGE_KEY, context);
}

export async function stopAttendanceLocationTracking() {
  // 会话失效时先清本地班中定位上下文，再停止原生后台任务，避免继续带旧凭证上传定位。
  await AppAsyncStorage.removeItem(ACTIVE_SHIFT_STORAGE_KEY);

  const hasStarted = await Location.hasStartedLocationUpdatesAsync(
    ATTENDANCE_LOCATION_TASK,
  );
  if (hasStarted) {
    await Location.stopLocationUpdatesAsync(ATTENDANCE_LOCATION_TASK);
  }
}
