export type AttendancePunchType = "ClockIn" | "ClockOut";

export type AttendancePunchStatus =
  | "Normal"
  | "Late"
  | "EarlyLeave"
  | "NoSchedule"
  | "Duplicate"
  | "PendingApproval"
  | "Approved"
  | "Rejected"
  | string;

export type AttendanceAvailabilityStatus = "Submitted" | "Cancelled" | string;

export type AttendanceLeaveType = "AnnualLeave" | "SickLeave" | "PublicHoliday" | string;

export type AttendanceApprovalStatus = "Pending" | "Approved" | "Rejected" | string;

export type AttendanceScheduleStatus = "Draft" | "Active" | "Cancelled" | string;

export interface AttendanceSchedule {
  scheduleGuid: string;
  storeCode: string;
  storeName?: string;
  userGuid: string;
  employeeName?: string;
  workDate: string;
  startTime: string;
  endTime: string;
  status: AttendanceScheduleStatus;
  remark?: string;
  isMine: boolean;
  holidayName?: string;
  holidayBusinessStatus?: string;
}

export interface AttendancePunch {
  punchGuid: string;
  scheduleGuid?: string;
  storeCode?: string;
  storeName?: string;
  workDate: string;
  punchType: AttendancePunchType | string;
  punchTimeUtc?: string;
  punchTimeLocal?: string;
  status: AttendancePunchStatus;
  statusReason?: string;
}

export interface AttendanceToday {
  workDate: string;
  storeTimeZone?: string;
  holidayName?: string;
  holidayBusinessStatus?: string;
  schedules: AttendanceSchedule[];
  punches: AttendancePunch[];
  nextPunchType: AttendancePunchType;
  canClockIn: boolean;
  canClockOut: boolean;
}

export interface AttendanceWeekDay {
  workDate: string;
  dayOfWeek: number;
  holidayName?: string;
  holidayBusinessStatus?: string;
  schedules: AttendanceSchedule[];
}

export interface AttendanceWeek {
  weekStart: string;
  weekEnd: string;
  days: AttendanceWeekDay[];
}

export interface AttendanceAvailability {
  availabilityGuid: string;
  storeCode?: string;
  storeName?: string;
  workDate: string;
  startTime: string;
  endTime: string;
  note?: string;
  status: AttendanceAvailabilityStatus;
}

export interface AttendanceAvailabilityPayload {
  storeCode?: string;
  workDate: string;
  startTime: string;
  endTime: string;
  note?: string;
}

export interface AttendanceLeaveRequest {
  leaveGuid: string;
  storeCode?: string;
  storeName?: string;
  leaveType: AttendanceLeaveType;
  startDate: string;
  endDate: string;
  reason?: string;
  status: AttendanceApprovalStatus;
  submittedAt?: string;
}

export interface AttendanceLeaveRequestPayload {
  storeCode?: string;
  leaveType: AttendanceLeaveType;
  startDate: string;
  endDate: string;
  reason?: string;
}

export interface AttendanceApproval {
  approvalGuid: string;
  sourceGuid: string;
  sourceType: "Punch" | "Leave" | string;
  employeeName?: string;
  storeCode?: string;
  storeName?: string;
  workDate?: string;
  title: string;
  detail?: string;
  status: AttendanceApprovalStatus;
  submittedAt?: string;
}

export interface AttendanceApprovalPayload {
  approvalGuid: string;
  remark?: string;
}

export interface AttendanceScheduleWeekParams {
  storeCode?: string;
  weekStartDate?: string;
}

export interface AttendanceSchedulePayload {
  storeCode: string;
  userGuid: string;
  workDate: string;
  startTime: string;
  endTime: string;
  status?: AttendanceScheduleStatus;
  remark?: string;
}

export interface AttendanceScheduleUpdatePayload {
  workDate: string;
  startTime: string;
  endTime: string;
  status?: AttendanceScheduleStatus;
  remark?: string;
}

export interface AttendancePublishWeekPayload {
  storeCode: string;
  weekStartDate: string;
}
