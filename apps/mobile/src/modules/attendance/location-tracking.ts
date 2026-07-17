import * as Location from "expo-location";
import * as TaskManager from "expo-task-manager";
import { createAttendanceLocationSample } from "@/modules/attendance/api";
import { isIosReviewSessionActive } from "@/modules/ios-review/session";
import type { AttendanceLocationSamplePayload } from "@/modules/attendance/types";
import {
  ATTENDANCE_LOCATION_TASK,
  LOCATION_SAMPLE_INTERVAL_MS,
  type ActiveShiftLocationContext,
  readActiveShiftContext,
  stopAttendanceLocationTracking,
  writeActiveShiftContext,
} from "@/modules/attendance/location-tracking-control";

function shouldUpload(lastUploadedAtUtc?: string) {
  if (!lastUploadedAtUtc) {
    return true;
  }

  const lastUploadedAt = new Date(lastUploadedAtUtc).getTime();
  return (
    Number.isNaN(lastUploadedAt) ||
    Date.now() - lastUploadedAt >= LOCATION_SAMPLE_INTERVAL_MS
  );
}

if (!TaskManager.isTaskDefined(ATTENDANCE_LOCATION_TASK)) {
  TaskManager.defineTask(ATTENDANCE_LOCATION_TASK, async ({ data, error }) => {
    if (isIosReviewSessionActive()) {
      // 审核会话即使收到遗留系统回调，也不得读取上下文或上传位置样本。
      return;
    }
    if (error) {
      console.warn("[attendance-location] 后台定位任务失败", error);
      return;
    }

    const context = await readActiveShiftContext();
    if (!context?.storeCode || !shouldUpload(context.lastUploadedAtUtc)) {
      return;
    }

    const locations = (data as { locations?: Location.LocationObject[] } | undefined)
      ?.locations;
    const latestLocation = locations?.at(-1);
    if (!latestLocation) {
      return;
    }

    const sample: AttendanceLocationSamplePayload = {
      storeCode: context.storeCode,
      hardwareId: context.hardwareId,
      systemDeviceNumber: context.systemDeviceNumber,
      deviceSystem: context.deviceSystem,
      eventType: "ShiftSample",
      locationLatitude: latestLocation.coords.latitude,
      locationLongitude: latestLocation.coords.longitude,
      locationAccuracy: latestLocation.coords.accuracy ?? undefined,
      locationPermissionStatus: "granted",
      locationCapturedAtUtc: new Date(latestLocation.timestamp).toISOString(),
    };

    try {
      // 班中定位是审计样本，成功上传后再记录节流时间，避免静默丢样本。
      await createAttendanceLocationSample(sample, { skipAuthRedirect: true });
      await writeActiveShiftContext({
        ...context,
        lastUploadedAtUtc: new Date().toISOString(),
      });
    } catch (uploadError) {
      console.warn("[attendance-location] 上传班中定位失败", uploadError);
    }
  });
}

export async function ensureAttendanceBackgroundLocationPermission() {
  if (isIosReviewSessionActive()) {
    return true;
  }

  if (await hasAttendanceBackgroundLocationPermission()) {
    return true;
  }

  const foreground = await Location.requestForegroundPermissionsAsync();
  if (foreground.status !== "granted") {
    return false;
  }

  const background = await Location.requestBackgroundPermissionsAsync();
  return background.status === "granted";
}

export async function hasAttendanceBackgroundLocationPermission() {
  const foreground = await Location.getForegroundPermissionsAsync();
  if (foreground.status !== "granted") {
    return false;
  }
  const background = await Location.getBackgroundPermissionsAsync();
  return background.status === "granted";
}

export async function startAttendanceLocationTracking(
  context: Omit<ActiveShiftLocationContext, "lastUploadedAtUtc">,
) {
  if (isIosReviewSessionActive()) {
    // 审核模式仅模拟班中定位状态，不写本地任务上下文或启动后台定位。
    return;
  }
  await writeActiveShiftContext(context);

  const hasStarted = await Location.hasStartedLocationUpdatesAsync(
    ATTENDANCE_LOCATION_TASK,
  );
  if (hasStarted) {
    return;
  }

  await Location.startLocationUpdatesAsync(ATTENDANCE_LOCATION_TASK, {
    accuracy: Location.Accuracy.Balanced,
    timeInterval: LOCATION_SAMPLE_INTERVAL_MS,
    distanceInterval: 0,
    // iOS 后台定位使用 deferredUpdatesInterval 才能按班中采样节奏批量回调。
    deferredUpdatesInterval: LOCATION_SAMPLE_INTERVAL_MS,
    deferredUpdatesDistance: 0,
    pausesUpdatesAutomatically: false,
    showsBackgroundLocationIndicator: true,
    foregroundService: {
      notificationTitle: "HB attendance location",
      notificationBody: "Location is sampled while you are clocked in.",
    },
  });
}

export { stopAttendanceLocationTracking };
