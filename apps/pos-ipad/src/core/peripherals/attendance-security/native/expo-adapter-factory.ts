import { ExpoAttendanceSecurityAdapter } from "./expo-attendance-security-adapter";
import { requireHbAttendanceSecurityNativeModule } from "./native-module";

export function createExpoAttendanceSecurityAdapter(): ExpoAttendanceSecurityAdapter {
  return new ExpoAttendanceSecurityAdapter(
    requireHbAttendanceSecurityNativeModule(),
  );
}
