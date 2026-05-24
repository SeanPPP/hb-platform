import type {
  AttendanceApprovalStatus,
  AttendancePunchStatus,
  AttendancePunchType,
  AttendanceWeek,
} from "@/modules/attendance/types";

export interface StaffAttendanceScheduleQueryParams {
  userGuid: string;
  storeCode?: string;
  weekStartDate?: string;
}

export interface StaffAttendanceRecordQueryParams {
  userGuid: string;
  storeCode?: string;
  limit?: number;
}

export type StaffAttendanceRecordType = "Punch" | "Leave" | "Approval" | string;

export interface StaffAttendanceRecord {
  recordGuid: string;
  type: StaffAttendanceRecordType;
  status: AttendancePunchStatus | AttendanceApprovalStatus | string;
  workDate: string;
  submittedAt?: string;
  storeCode?: string;
  storeName?: string;
  detail?: string;
  punchType?: AttendancePunchType | string;
  leaveType?: string;
  sourceType?: string;
  startTime?: string;
  endTime?: string;
}

export type StaffAttendanceWeek = AttendanceWeek;
