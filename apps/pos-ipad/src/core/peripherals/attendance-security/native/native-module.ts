import { requireNativeModule } from "expo";

import type { HbAttendanceSecurityNativeModule } from "./types";

let cachedNativeModule: HbAttendanceSecurityNativeModule | null = null;

export function requireHbAttendanceSecurityNativeModule(): HbAttendanceSecurityNativeModule {
  cachedNativeModule ??=
    requireNativeModule<HbAttendanceSecurityNativeModule>(
      "HBAttendanceSecurity",
    );
  return cachedNativeModule;
}
