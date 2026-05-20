import type { PagedResult } from './api'

export type AttendanceScheduleStatus = 'Draft' | 'Active' | 'Cancelled'
export type AttendanceAvailabilityStatus = 'Active' | 'Cancelled'
export type AttendancePunchType = 'ClockIn' | 'ClockOut'
export type AttendancePunchStatus =
  | 'Normal'
  | 'Late'
  | 'EarlyLeave'
  | 'NoSchedule'
  | 'Duplicate'
  | 'PendingApproval'
  | 'Approved'
  | 'Rejected'
export type AttendanceApprovalSourceType = 'Punch' | 'Leave'
export type AttendanceReviewStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled'
export type AttendanceHolidayBusinessStatus = 'Open' | 'Closed' | 'Partial'
export type AttendanceLeaveType = 'AnnualLeave' | 'SickLeave' | 'PublicHoliday'

export interface AttendanceQuery {
  page?: number
  pageSize?: number
  storeCode?: string
  userGuid?: string
  weekStartDate?: string
  fromDate?: string
  toDate?: string
  status?: string
  keyword?: string
}

export interface AttendanceScheduleDto {
  scheduleGuid: string
  storeCode: string
  storeName?: string
  userGuid: string
  userName?: string
  workDate: string
  startTime: string
  endTime: string
  status: AttendanceScheduleStatus
  remark?: string
  createdAt?: string
  updatedAt?: string
}

export interface SaveAttendanceSchedulePayload {
  storeCode: string
  userGuid: string
  workDate: string
  startTime: string
  endTime: string
  status?: AttendanceScheduleStatus
  remark?: string
}

export interface AttendanceAvailabilityDto {
  availabilityGuid: string
  storeCode: string
  storeName?: string
  userGuid: string
  userName?: string
  weekStartDate: string
  availableDate: string
  startTime: string
  endTime: string
  status: AttendanceAvailabilityStatus
  remark?: string
  createdAt?: string
}

export interface AttendancePunchDto {
  punchGuid: string
  scheduleGuid?: string
  storeCode: string
  storeName?: string
  userGuid: string
  userName?: string
  workDate: string
  storeTimeZone?: string
  punchType: AttendancePunchType
  punchTimeUtc?: string
  punchTimeLocal?: string
  status: AttendancePunchStatus
  deviceId?: string
  source?: string
  remark?: string
  createdAt?: string
}

export interface AttendanceApprovalDto {
  approvalGuid: string
  sourceType: AttendanceApprovalSourceType
  sourceGuid: string
  storeCode: string
  storeName?: string
  applicantUserGuid: string
  applicantName?: string
  reviewerUserGuid?: string
  reviewerName?: string
  reviewStatus: AttendanceReviewStatus
  reviewRemark?: string
  reviewedAt?: string
  createdAt?: string
}

export interface ReviewAttendanceApprovalPayload {
  reviewRemark?: string
}

export interface AttendanceStoreHolidayDto {
  holidayGuid: string
  storeCode: string
  storeName?: string
  holidayDate: string
  holidayName: string
  businessStatus: AttendanceHolidayBusinessStatus
  openTime?: string
  closeTime?: string
  isPaidHoliday?: boolean
  remark?: string
  createdAt?: string
  updatedAt?: string
}

export interface SaveAttendanceHolidayPayload {
  storeCode: string
  holidayDate: string
  holidayName: string
  businessStatus: AttendanceHolidayBusinessStatus
  openTime?: string
  closeTime?: string
  isPaidHoliday?: boolean
  remark?: string
}

export interface AttendanceSettingsDto {
  lateGraceMinutes: number
  earlyLeaveGraceMinutes: number
  allowNoSchedulePunch: boolean
  requireApprovalForLate: boolean
  requireApprovalForEarlyLeave: boolean
  requireApprovalForNoSchedule: boolean
  updatedAt?: string
  updatedBy?: string
}

export type SaveAttendanceSettingsPayload = Pick<
  AttendanceSettingsDto,
  | 'lateGraceMinutes'
  | 'earlyLeaveGraceMinutes'
  | 'allowNoSchedulePunch'
  | 'requireApprovalForLate'
  | 'requireApprovalForEarlyLeave'
  | 'requireApprovalForNoSchedule'
>

export type AttendancePagedResult<T> = PagedResult<T>
