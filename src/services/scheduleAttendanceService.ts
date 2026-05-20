import type { ApiResponse, PagedResult } from '../types/api'
import type {
  AttendanceApprovalDto,
  AttendanceAvailabilityDto,
  AttendancePagedResult,
  AttendancePunchDto,
  AttendanceQuery,
  AttendanceScheduleDto,
  AttendanceSettingsDto,
  AttendanceStoreHolidayDto,
  ReviewAttendanceApprovalPayload,
  SaveAttendanceHolidayPayload,
  SaveAttendanceSchedulePayload,
  SaveAttendanceSettingsPayload,
} from '../types/scheduleAttendance'
import request, { unwrapApiData, unwrapPagedResult } from '../utils/request'

const API_BASE = '/api/react/v1/attendance'

function normalizePaged<T>(payload: ApiResponse<PagedResult<T>> | PagedResult<T> | T[]): AttendancePagedResult<T> {
  const data = unwrapApiData(payload)
  if (Array.isArray(data)) {
    return {
      items: data,
      total: data.length,
      page: 1,
      pageSize: data.length || 10,
      totalPages: 1,
    }
  }
  return unwrapPagedResult(payload as ApiResponse<PagedResult<T>> | PagedResult<T>)
}

export async function getAttendanceScheduleWeek(params: AttendanceQuery): Promise<AttendanceScheduleDto[]> {
  const response = await request.get<ApiResponse<AttendanceScheduleDto[]> | AttendanceScheduleDto[]>(
    `${API_BASE}/schedules/week`,
    { params: params as Record<string, unknown> },
  )
  return unwrapApiData(response) ?? []
}

export async function getAttendanceSchedules(params: AttendanceQuery): Promise<AttendancePagedResult<AttendanceScheduleDto>> {
  const response = await request.get<ApiResponse<PagedResult<AttendanceScheduleDto>> | PagedResult<AttendanceScheduleDto> | AttendanceScheduleDto[]>(
    `${API_BASE}/schedules`,
    { params: params as Record<string, unknown> },
  )
  return normalizePaged(response)
}

export async function createAttendanceSchedule(payload: SaveAttendanceSchedulePayload): Promise<AttendanceScheduleDto> {
  const response = await request.post<ApiResponse<AttendanceScheduleDto>>(`${API_BASE}/schedules`, payload)
  return unwrapApiData(response)
}

export async function updateAttendanceSchedule(
  scheduleGuid: string,
  payload: SaveAttendanceSchedulePayload,
): Promise<AttendanceScheduleDto> {
  const response = await request.put<ApiResponse<AttendanceScheduleDto>>(`${API_BASE}/schedules/${scheduleGuid}`, payload)
  return unwrapApiData(response)
}

export async function deleteAttendanceSchedule(scheduleGuid: string): Promise<void> {
  await request.delete<ApiResponse<boolean> | boolean>(`${API_BASE}/schedules/${scheduleGuid}`)
}

export async function publishAttendanceScheduleWeek(payload: { storeCode: string; weekStartDate: string }): Promise<void> {
  await request.post<ApiResponse<boolean> | boolean>(`${API_BASE}/schedules/publish-week`, payload)
}

export async function getAttendanceAvailability(params: AttendanceQuery): Promise<AttendancePagedResult<AttendanceAvailabilityDto>> {
  const response = await request.get<ApiResponse<PagedResult<AttendanceAvailabilityDto>> | PagedResult<AttendanceAvailabilityDto> | AttendanceAvailabilityDto[]>(
    `${API_BASE}/availability`,
    { params: params as Record<string, unknown> },
  )
  return normalizePaged(response)
}

export async function getAttendancePunches(params: AttendanceQuery): Promise<AttendancePagedResult<AttendancePunchDto>> {
  const response = await request.get<ApiResponse<PagedResult<AttendancePunchDto>> | PagedResult<AttendancePunchDto> | AttendancePunchDto[]>(
    `${API_BASE}/punches`,
    { params: params as Record<string, unknown> },
  )
  return normalizePaged(response)
}

export async function getAttendanceApprovals(params: AttendanceQuery): Promise<AttendancePagedResult<AttendanceApprovalDto>> {
  const response = await request.get<ApiResponse<PagedResult<AttendanceApprovalDto>> | PagedResult<AttendanceApprovalDto> | AttendanceApprovalDto[]>(
    `${API_BASE}/approvals`,
    { params: params as Record<string, unknown> },
  )
  return normalizePaged(response)
}

export async function getPendingAttendanceApprovals(params: AttendanceQuery): Promise<AttendancePagedResult<AttendanceApprovalDto>> {
  const response = await request.get<ApiResponse<PagedResult<AttendanceApprovalDto>> | PagedResult<AttendanceApprovalDto> | AttendanceApprovalDto[]>(
    `${API_BASE}/approvals/pending`,
    { params: params as Record<string, unknown> },
  )
  return normalizePaged(response)
}

export async function approveAttendanceApproval(
  approvalGuid: string,
  payload: ReviewAttendanceApprovalPayload,
): Promise<AttendanceApprovalDto> {
  const response = await request.post<ApiResponse<AttendanceApprovalDto>>(`${API_BASE}/approvals/${approvalGuid}/approve`, payload)
  return unwrapApiData(response)
}

export async function rejectAttendanceApproval(
  approvalGuid: string,
  payload: ReviewAttendanceApprovalPayload,
): Promise<AttendanceApprovalDto> {
  const response = await request.post<ApiResponse<AttendanceApprovalDto>>(`${API_BASE}/approvals/${approvalGuid}/reject`, payload)
  return unwrapApiData(response)
}

export async function getAttendanceHolidays(params: AttendanceQuery): Promise<AttendancePagedResult<AttendanceStoreHolidayDto>> {
  const response = await request.get<ApiResponse<PagedResult<AttendanceStoreHolidayDto>> | PagedResult<AttendanceStoreHolidayDto> | AttendanceStoreHolidayDto[]>(
    `${API_BASE}/holidays`,
    { params: params as Record<string, unknown> },
  )
  return normalizePaged(response)
}

export async function createAttendanceHoliday(payload: SaveAttendanceHolidayPayload): Promise<AttendanceStoreHolidayDto> {
  const response = await request.post<ApiResponse<AttendanceStoreHolidayDto>>(`${API_BASE}/holidays`, payload)
  return unwrapApiData(response)
}

export async function updateAttendanceHoliday(
  holidayGuid: string,
  payload: SaveAttendanceHolidayPayload,
): Promise<AttendanceStoreHolidayDto> {
  const response = await request.put<ApiResponse<AttendanceStoreHolidayDto>>(`${API_BASE}/holidays/${holidayGuid}`, payload)
  return unwrapApiData(response)
}

export async function deleteAttendanceHoliday(holidayGuid: string): Promise<void> {
  await request.delete<ApiResponse<boolean> | boolean>(`${API_BASE}/holidays/${holidayGuid}`)
}

export async function getAttendanceSettings(): Promise<AttendanceSettingsDto> {
  const response = await request.get<ApiResponse<AttendanceSettingsDto>>(`${API_BASE}/settings`)
  return unwrapApiData(response)
}

export async function updateAttendanceSettings(payload: SaveAttendanceSettingsPayload): Promise<AttendanceSettingsDto> {
  const response = await request.put<ApiResponse<AttendanceSettingsDto>>(`${API_BASE}/settings`, payload)
  return unwrapApiData(response)
}
