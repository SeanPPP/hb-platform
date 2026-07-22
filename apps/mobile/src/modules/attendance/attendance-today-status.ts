import type { AttendanceToday } from "./types";

export type AttendanceTodayStatus =
  | "viewOnly"
  | "holiday"
  | "readyToClockIn"
  | "readyToClockOut"
  | "completed";

export function resolveAttendanceTodayStatus(
  today: AttendanceToday | undefined,
  allowPunch: boolean,
): AttendanceTodayStatus {
  if (!allowPunch) return "viewOnly";
  if (today?.holidayName) return "holiday";

  const states = (today?.scheduleSessions ?? [])
    .map((session) => session.scheduleState?.toLowerCase())
    .filter((state): state is string => Boolean(state));
  const allSchedulesCompleted = states.length > 0
    && states.length === (today?.scheduleSessions.length ?? 0)
    && states.every((state) => state === "completed");
  if (allSchedulesCompleted) return "completed";
  if (states.some((state) => state === "working" || state === "missingclockout")) {
    return "readyToClockOut";
  }
  if (states.some((state) => state === "onbreak")) return "readyToClockIn";
  const hasClockIn = (today?.punches ?? []).some((punch) => punch.punchType === "ClockIn");
  const hasClockOut = (today?.punches ?? []).some((punch) => punch.punchType === "ClockOut");
  // 兼容旧后端及 iOS Review：完整一组打卡且服务端关闭两种动作时，班次已经结束。
  if (hasClockIn && hasClockOut && !today?.canClockIn && !today?.canClockOut) {
    return "completed";
  }
  if (today?.canClockOut || today?.nextPunchType === "ClockOut") return "readyToClockOut";
  return "readyToClockIn";
}
