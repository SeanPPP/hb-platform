import * as Location from "expo-location";
import { AppAsyncStorage } from "@/shared/storage/async-storage";
import { isIosReviewSessionActive } from "@/modules/ios-review/session";

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
  if (isIosReviewSessionActive()) {
    return null;
  }
  return AppAsyncStorage.getObject<ActiveShiftLocationContext>(
    ACTIVE_SHIFT_STORAGE_KEY,
  );
}

export async function writeActiveShiftContext(context: ActiveShiftLocationContext) {
  if (isIosReviewSessionActive()) {
    return;
  }
  await AppAsyncStorage.setObject(ACTIVE_SHIFT_STORAGE_KEY, context);
}

export async function stopAttendanceLocationTracking(options?: { force?: boolean }) {
  if (isIosReviewSessionActive() && !options?.force) {
    // 审核会话从未启动真实后台定位，退出时也不能改写普通设备的持久化上下文。
    return;
  }
  // 会话失效时先清本地班中定位上下文，再停止原生后台任务，避免继续带旧凭证上传定位。
  await AppAsyncStorage.removeItem(ACTIVE_SHIFT_STORAGE_KEY);

  const hasStarted = await Location.hasStartedLocationUpdatesAsync(
    ATTENDANCE_LOCATION_TASK,
  );
  if (hasStarted) {
    await Location.stopLocationUpdatesAsync(ATTENDANCE_LOCATION_TASK);
  }
}
