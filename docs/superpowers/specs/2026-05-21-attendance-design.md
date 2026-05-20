# Attendance Scheduling and Punch Design

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:writing-plans before implementation. This file is the approved design input; do not start coding until the implementation plan is reviewed.

**Goal:** Build a first-version attendance module that supports weekly scheduling, basic App punch-in/out, manager review, store-specific public holidays, and leave/public-holiday approval.

**Architecture:** The unified backend in `/Users/sean/DEV/HBBblazorweb-master-vite` owns all attendance data, permissions, and business rules. The Expo App in `/Users/sean/DEV/HbwebExpo/HbwebExpoApp` adds a bottom `考勤` tab for staff and store-manager workflows. The Web app in `/Users/sean/DEV/hbweb_rv` adds a `门店运营 / 排班考勤` management page for weekly schedules, punch records, approvals, holidays, and Admin-only settings.

**Tech Stack:** .NET API with SqlSugar-style models/services/controllers, React/Ant Design web management, Expo React Native App with existing auth/navigation/i18n patterns.

---

## Confirmed Scope

- First version uses basic manual punch. No required GPS, photo, face recognition, or Wi-Fi validation.
- App adds a dedicated bottom tab named `考勤`.
- Schedules are shown as weekly tables from Monday to Sunday.
- StoreStaff can view their own schedules and the weekly schedule table for related stores.
- StoreManager can view, create, edit, and cancel schedules for multiple stores they manage.
- StoreManager can manage public holidays for stores they manage.
- Admin can manage all stores, all holidays, and all attendance settings.
- Late and early-leave grace periods default to 5 minutes and are editable in the backend settings page.
- Attendance settings are editable by Admin only in version one.
- Public holidays are store-specific and can differ between stores.
- Public holidays can still be business days.
- Public holiday business status supports `Open`, `Closed`, and `Partial`.
- `Partial` only displays special hours and schedule warnings in version one; it does not hard-block schedules outside the special hours.
- Annual leave, sick leave, and public holiday requests require approval.

## Roles And Permissions

StoreStaff:

- View their own today/week schedules.
- View weekly schedule tables for related stores.
- Punch in and punch out for themselves.
- Submit annual leave, sick leave, and public holiday requests.
- View their own request and punch status.

StoreManager:

- View schedules for all managed stores.
- Create, edit, and cancel schedules for managed stores.
- View punch records for managed stores.
- Review abnormal punches for managed stores.
- Configure public holidays for managed stores.
- Review annual leave, sick leave, and public holiday requests for managed stores.

Admin:

- View and manage all schedules, punch records, approvals, and store holidays.
- Modify global attendance settings.

Suggested permission constants:

```text
Attendance.Schedule.ViewSelf
Attendance.Schedule.ViewStore
Attendance.Schedule.EditManagedStore
Attendance.Punch.Self
Attendance.Punch.ViewManagedStore
Attendance.Approval.ViewManagedStore
Attendance.Approval.ReviewManagedStore
Attendance.Holiday.ViewStore
Attendance.Holiday.EditManagedStore
Attendance.Leave.ApplySelf
Attendance.Leave.ViewManagedStore
Attendance.Leave.ReviewManagedStore
Attendance.Settings.Edit
Attendance.Admin.View
```

## Backend Design

Backend root:

```text
/Users/sean/DEV/HBBblazorweb-master-vite
```

Suggested files:

```text
BlazorApp.Shared/DTOs/AttendanceDtos.cs
BlazorApp.Shared/Models/HBweb/Attendance/AttendanceSchedule.cs
BlazorApp.Shared/Models/HBweb/Attendance/AttendancePunch.cs
BlazorApp.Shared/Models/HBweb/Attendance/AttendanceApproval.cs
BlazorApp.Shared/Models/HBweb/Attendance/AttendanceStoreHoliday.cs
BlazorApp.Shared/Models/HBweb/Attendance/AttendanceLeaveRequest.cs
BlazorApp.Shared/Models/HBweb/Attendance/AttendanceSettings.cs
BlazorApp.Api/Interfaces/React/IAttendanceReactService.cs
BlazorApp.Api/Services/React/AttendanceReactService.cs
BlazorApp.Api/Controllers/React/ReactAttendanceController.cs
BlazorApp.Shared/Constants/Permissions.cs
BlazorApp.Api/Services/NavigationService.cs
BlazorApp.Api/Program.cs
```

Core models:

```text
AttendanceSchedule
- Id
- ScheduleGuid
- StoreCode
- UserGuid
- WorkDate
- StartTime
- EndTime
- Status: Active / Cancelled
- Remark
- CreatedAt / CreatedBy
- UpdatedAt / UpdatedBy
```

```text
AttendancePunch
- Id
- PunchGuid
- ScheduleGuid nullable
- StoreCode
- UserGuid
- WorkDate
- PunchType: ClockIn / ClockOut
- PunchTime
- Status: Normal / Late / EarlyLeave / NoSchedule / Duplicate / PendingApproval / Approved / Rejected
- DeviceId nullable
- Source: App
- Remark
- CreatedAt / CreatedBy
```

```text
AttendanceApproval
- Id
- ApprovalGuid
- SourceType: Punch / Leave
- SourceGuid
- StoreCode
- ApplicantUserGuid
- ReviewerUserGuid nullable
- ReviewStatus: Pending / Approved / Rejected / Cancelled
- ReviewRemark nullable
- ReviewedAt nullable
- CreatedAt / CreatedBy
```

```text
AttendanceStoreHoliday
- Id
- HolidayGuid
- StoreCode
- HolidayDate
- HolidayName
- BusinessStatus: Open / Closed / Partial
- OpenTime nullable
- CloseTime nullable
- IsPaidHoliday
- Remark
- CreatedAt / CreatedBy
- UpdatedAt / UpdatedBy
```

```text
AttendanceLeaveRequest
- Id
- LeaveGuid
- StoreCode
- UserGuid
- LeaveType: AnnualLeave / SickLeave / PublicHoliday
- StartDate
- EndDate
- StartTime nullable
- EndTime nullable
- Reason
- AttachmentUrl nullable
- Status: Pending / Approved / Rejected / Cancelled
- ReviewedBy nullable
- ReviewedAt nullable
- ReviewRemark nullable
- CreatedAt / CreatedBy
```

```text
AttendanceSettings
- Id
- LateGraceMinutes default 5
- EarlyLeaveGraceMinutes default 5
- AllowNoSchedulePunch default true
- RequireApprovalForLate default true
- RequireApprovalForEarlyLeave default true
- RequireApprovalForNoSchedule default true
- UpdatedAt / UpdatedBy
```

## API Design

Use React API style under:

```text
/api/react/v1/attendance
```

Schedule:

```text
GET    /schedules
POST   /schedules
PUT    /schedules/{scheduleGuid}
DELETE /schedules/{scheduleGuid}
GET    /schedules/week
```

App self-service:

```text
GET  /my/today
GET  /my/week
POST /punch
GET  /my/leave-requests
POST /my/leave-requests
POST /my/leave-requests/{leaveGuid}/cancel
```

Punch records and approvals:

```text
GET  /punches
GET  /approvals
GET  /approvals/pending
POST /approvals/{approvalGuid}/approve
POST /approvals/{approvalGuid}/reject
```

Store holidays:

```text
GET    /holidays
POST   /holidays
PUT    /holidays/{holidayGuid}
DELETE /holidays/{holidayGuid}
```

Settings:

```text
GET /settings
PUT /settings
```

## Punch Rules

- Clock-in is late when `PunchTime > Schedule.StartTime + LateGraceMinutes`.
- Clock-out is early leave when `PunchTime < Schedule.EndTime - EarlyLeaveGraceMinutes`.
- No schedule punch is allowed by default but creates an approval record.
- Duplicate punch creates an abnormal status and requires review.
- Normal punches do not require approval.
- Late, early leave, no schedule, and duplicate punches require approval by default.
- Approved abnormal punches become accepted attendance records.
- Rejected abnormal punches remain visible but should not count as accepted attendance.

## Weekly Schedule Rules

- Week view always starts on Monday and ends on Sunday.
- Web schedule view can filter by store, employee, and week.
- App staff view defaults to the current week and highlights the current user.
- A user can have multiple shifts in one day.
- Version one should prevent overlapping shifts for the same employee in the same store and date.
- Public holidays are displayed in the week table header or day cell.
- Closed public holidays show a warning when scheduling.
- Partial public holidays show special hours and a warning but do not block saving.

## App Design

App root:

```text
/Users/sean/DEV/HbwebExpo/HbwebExpoApp
```

Suggested files:

```text
app/(tabs)/attendance.tsx
src/modules/attendance/api.ts
src/modules/attendance/types.ts
src/components/attendance/TodayPunchCard.tsx
src/components/attendance/WeeklyScheduleTable.tsx
src/components/attendance/LeaveRequestCard.tsx
src/components/attendance/ManagerApprovalList.tsx
src/locales/zh/screens/attendance.json
src/locales/en/screens/attendance.json
app/(tabs)/_layout.tsx
src/locales/zh/common.json
src/locales/en/common.json
```

App `考勤` tab sections:

```text
今日打卡
- Today schedule
- Clock-in / clock-out button
- Current punch status
- Public holiday notice if applicable

本周排班
- Monday to Sunday weekly table
- Related store selector when the user has multiple stores
- Staff can view related store weekly schedules read-only
- Current user's shifts are visually emphasized

请假申请
- Annual leave
- Sick leave
- Public holiday request
- Own request status list

店长审核
- Visible for StoreManager
- Managed store selector
- Abnormal punch review
- Leave/public-holiday review
```

## Web Design

Web root:

```text
/Users/sean/DEV/hbweb_rv
```

Suggested files:

```text
src/pages/StoreOperations/Attendance/index.tsx
src/services/attendanceService.ts
src/router/routes.tsx
src/utils/access.ts
```

Menu:

```text
门店运营
- 排班考勤
```

Page tabs:

```text
周排班
- Store, week, and employee filters
- Monday to Sunday table
- Create/edit/cancel shifts
- StoreManager limited to managed stores

打卡记录
- Clock-in and clock-out records
- Normal, late, early leave, no schedule, duplicate, approved, rejected filters

审核中心
- Abnormal punch approvals
- Annual leave approvals
- Sick leave approvals
- Public holiday approvals

公共假期
- Store-specific holiday list
- BusinessStatus Open / Closed / Partial
- OpenTime and CloseTime for Partial

考勤设置
- Admin only
- Late grace minutes
- Early-leave grace minutes
- No schedule punch allowance
- Approval requirements
```

## Navigation And Access

Backend navigation must expose the Web menu when the user has attendance permissions. App navigation must expose the `attendance` route for authenticated StoreStaff, StoreManager, and Admin users. Device-only anonymous mode should not receive the `attendance` tab unless a later requirement explicitly allows device attendance.

Access checks must be enforced in both layers:

- Backend filters data by current user's roles and managed/related store scope.
- Web hides actions based on `access.ts`.
- App hides StoreManager approval and edit affordances for StoreStaff.

## Out Of Scope For Version One

- GPS geofencing.
- Photo punch.
- Face recognition.
- Wi-Fi or device-location enforcement.
- Payroll calculation.
- Leave balance accrual.
- Cross-day overnight shift handling.
- Shift templates and recurring schedule generation.
- Hard enforcement of partial public holiday hours.

## Acceptance Criteria

- Admin can configure global attendance settings, including both grace periods.
- StoreManager can create and edit schedules only for managed stores.
- StoreManager can configure holidays only for managed stores.
- StoreStaff can view their own and related-store weekly schedules in the App.
- App has a bottom `考勤` tab.
- Staff can punch in and out from the App.
- Late, early leave, no schedule, and duplicate punch records create pending approvals.
- Staff can submit annual leave, sick leave, and public holiday requests.
- StoreManager can approve or reject managed-store punch and leave/public-holiday requests.
- Public holidays can be Open, Closed, or Partial per store.
- Partial public holidays display special hours and warning text but do not block schedule save.
