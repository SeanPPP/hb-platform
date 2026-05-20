# App 排班与打卡功能设计方案

> **给后续执行代理的要求：** 实施前必须先使用 `superpowers:writing-plans` 写实施计划。本文件是已确认的设计输入，不能跳过计划直接编码。

**目标：** 创建第一版考勤模块，支持员工可用时间上报、周排班、App 基础打卡、店长审核、分店公共假期、年假/病假/公共假期申请与审核。

**架构：** 统一后端 `/Users/sean/DEV/HBBblazorweb-master-vite` 负责所有考勤数据、权限和业务规则。Expo App `/Users/sean/DEV/HbwebExpo/HbwebExpoApp` 新增底部 `考勤` Tab，覆盖员工和店长工作流。Web 端 `/Users/sean/DEV/hbweb_rv` 新增 `门店运营 / 排班考勤` 页面，用于周排班、打卡记录、审核、公共假期和 Admin-only 考勤设置。

**技术栈：** .NET API、SqlSugar 风格 Model/Service/Controller、React/Ant Design Web 管理端、Expo React Native App，并沿用现有认证、导航和国际化模式。

---

## 已确认范围

- 第一版只做基础手动打卡，不强制 GPS、拍照、人脸识别、Wi-Fi 校验。
- App 底部新增独立 Tab：`考勤`。
- 员工可以在 App 上报自己可上班的时间段，方便店长排班。
- 排班按周表展示，从周一到周日。
- StoreStaff 可以查看自己的排班，也可以查看自己相关分店的周排班表。
- StoreManager 可以查看、创建、编辑、取消自己管理的多个分店排班。
- StoreManager 可以维护自己管理分店的公共假期。
- Admin 可以管理全部分店、全部公共假期和全部考勤设置。
- 迟到/早退缓冲时间默认 5 分钟，并且可以在后台设置中修改。
- 第一版考勤设置只允许 Admin 修改。
- 公共假期按分店配置，每个分店可以不同。
- 公共假期不等于停业，公共假期也可能营业。
- 公共假期营业状态支持 `Open`、`Closed`、`Partial`。
- `Partial` 第一版只做显示和排班提醒，不强制限制排班时间。
- 年假、病假、公共假期申请都需要审核。
- 打卡时间按分店时区做业务判断，模型中保留 `StoreTimeZone`。支持 `Australia/Brisbane`、`Australia/Melbourne`，默认 `Australia/Sydney`。

## 角色与权限

StoreStaff：

- 上报自己可上班的时间段。
- 查看自己的今日排班和本周排班。
- 查看自己相关分店的周排班表。
- 自己上班打卡、下班打卡。
- 提交年假、病假、公共假期申请。
- 查看自己的申请状态和打卡状态。

StoreManager：

- 查看自己管理分店员工上报的可用时间段，用作排班参考。
- 查看自己管理的所有分店排班。
- 创建、编辑、取消自己管理分店的排班。
- 查看自己管理分店的打卡记录。
- 审核自己管理分店的异常打卡。
- 配置自己管理分店的公共假期。
- 审核自己管理分店员工的年假、病假、公共假期申请。

Admin：

- 查看和管理全部排班、打卡记录、审核记录、分店公共假期。
- 修改全局考勤设置。

建议权限常量：

```text
Attendance.Schedule.ViewSelf
Attendance.Schedule.ViewStore
Attendance.Schedule.EditManagedStore
Attendance.Availability.SubmitSelf
Attendance.Availability.ViewManagedStore
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

## 后端设计

后端根目录：

```text
/Users/sean/DEV/HBBblazorweb-master-vite
```

建议新增或修改文件：

```text
BlazorApp.Shared/DTOs/AttendanceDtos.cs
BlazorApp.Shared/Models/HBweb/Attendance/AttendanceSchedule.cs
BlazorApp.Shared/Models/HBweb/Attendance/AttendancePunch.cs
BlazorApp.Shared/Models/HBweb/Attendance/AttendanceApproval.cs
BlazorApp.Shared/Models/HBweb/Attendance/AttendanceAvailability.cs
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

核心模型：

```text
AttendanceSchedule 排班表
- Id
- ScheduleGuid
- StoreCode 分店编码
- UserGuid 员工用户 GUID
- WorkDate 工作日期
- StartTime 上班时间
- EndTime 下班时间
- Status: Active / Cancelled
- Remark 备注
- CreatedAt / CreatedBy
- UpdatedAt / UpdatedBy
```

```text
AttendanceAvailability 员工可用时间上报
- Id
- AvailabilityGuid
- StoreCode 分店编码
- UserGuid 员工用户 GUID
- WeekStartDate 周一日期
- AvailableDate 可上班日期
- StartTime 可上班开始时间
- EndTime 可上班结束时间
- Status: Active / Cancelled
- Remark 备注
- CreatedAt / CreatedBy
- UpdatedAt / UpdatedBy
```

```text
AttendancePunch 打卡记录
- Id
- PunchGuid
- ScheduleGuid nullable
- StoreCode 分店编码
- UserGuid 员工用户 GUID
- WorkDate 工作日期
- StoreTimeZone 分店时区，默认 Australia/Sydney
- PunchType: ClockIn / ClockOut
- PunchTimeUtc UTC 打卡时间
- PunchTimeLocal 分店本地打卡时间
- Status: Normal / Late / EarlyLeave / NoSchedule / Duplicate / PendingApproval / Approved / Rejected
- DeviceId nullable
- Source: App
- Remark 备注
- CreatedAt / CreatedBy
```

```text
AttendanceApproval 审核记录
- Id
- ApprovalGuid
- SourceType: Punch / Leave
- SourceGuid 来源记录 GUID
- StoreCode 分店编码
- ApplicantUserGuid 申请人
- ReviewerUserGuid nullable 审核人
- ReviewStatus: Pending / Approved / Rejected / Cancelled
- ReviewRemark nullable
- ReviewedAt nullable
- CreatedAt / CreatedBy
```

```text
AttendanceStoreHoliday 分店公共假期
- Id
- HolidayGuid
- StoreCode 分店编码
- HolidayDate 假期日期
- HolidayName 假期名称
- BusinessStatus: Open / Closed / Partial
- OpenTime nullable
- CloseTime nullable
- IsPaidHoliday 是否带薪假期
- Remark 备注
- CreatedAt / CreatedBy
- UpdatedAt / UpdatedBy
```

```text
AttendanceLeaveRequest 请假/公共假期申请
- Id
- LeaveGuid
- StoreCode 分店编码
- UserGuid 员工用户 GUID
- LeaveType: AnnualLeave / SickLeave / PublicHoliday
- StartDate
- EndDate
- StartTime nullable
- EndTime nullable
- Reason 申请原因
- AttachmentUrl nullable
- Status: Pending / Approved / Rejected / Cancelled
- ReviewedBy nullable
- ReviewedAt nullable
- ReviewRemark nullable
- CreatedAt / CreatedBy
```

```text
AttendanceSettings 考勤设置
- Id
- LateGraceMinutes default 5
- EarlyLeaveGraceMinutes default 5
- AllowNoSchedulePunch default true
- RequireApprovalForLate default true
- RequireApprovalForEarlyLeave default true
- RequireApprovalForNoSchedule default true
- UpdatedAt / UpdatedBy
```

## API 设计

统一使用 React API 风格：

```text
/api/react/v1/attendance
```

排班：

```text
GET    /schedules
POST   /schedules
PUT    /schedules/{scheduleGuid}
DELETE /schedules/{scheduleGuid}
GET    /schedules/week
```

App 员工自助：

```text
GET  /my/today
GET  /my/week
GET  /my/availability
POST /my/availability
PUT  /my/availability/{availabilityGuid}
POST /my/availability/{availabilityGuid}/cancel
POST /punch
GET  /my/leave-requests
POST /my/leave-requests
POST /my/leave-requests/{leaveGuid}/cancel
```

员工可用时间：

```text
GET /availability
```

打卡记录与审核：

```text
GET  /punches
GET  /approvals
GET  /approvals/pending
POST /approvals/{approvalGuid}/approve
POST /approvals/{approvalGuid}/reject
```

分店公共假期：

```text
GET    /holidays
POST   /holidays
PUT    /holidays/{holidayGuid}
DELETE /holidays/{holidayGuid}
```

考勤设置：

```text
GET /settings
PUT /settings
```

## 打卡规则

- 打卡业务判断使用分店时区，不使用服务器本地时区。
- 第一版支持的分店时区为 `Australia/Brisbane`、`Australia/Melbourne`，未配置时默认 `Australia/Sydney`。
- 后端保存 `PunchTimeUtc`，同时保存按 `StoreTimeZone` 转换后的 `PunchTimeLocal` 和 `WorkDate`。
- `WorkDate` 必须按分店本地日期计算，不能直接使用服务器日期。
- 上班打卡时间 `PunchTimeLocal > Schedule.StartTime + LateGraceMinutes` 时判定为迟到。
- 下班打卡时间 `PunchTimeLocal < Schedule.EndTime - EarlyLeaveGraceMinutes` 时判定为早退。
- 默认允许未排班打卡，但会生成待审核记录。
- 重复打卡会生成异常状态并进入审核。
- 正常打卡不需要审核。
- 迟到、早退、未排班、重复打卡默认都需要审核。
- 审核通过的异常打卡视为有效考勤记录。
- 审核拒绝的异常打卡仍保留记录，但不作为有效考勤。

## 周排班规则

- 周视图固定从周一到周日。
- Web 端周排班支持按分店、员工、周筛选。
- App 员工视图默认展示当前周，并突出显示当前员工自己的班次。
- 员工可用时间上报也按周组织，店长排班时按员工和日期展示可用时间段。
- 员工可用时间只是排班参考，不自动生成班次，也不强制限制店长排班。
- 一个员工同一天可以有多个班次。
- 第一版应禁止同一员工在同一分店、同一天出现时间重叠的班次。
- 公共假期要显示在周表的日期头或日期单元格中。
- `Closed` 公共假期排班时显示提醒。
- `Partial` 公共假期显示特殊营业时间和提醒，但不阻止保存。

## App 设计

App 根目录：

```text
/Users/sean/DEV/HbwebExpo/HbwebExpoApp
```

建议新增或修改文件：

```text
app/(tabs)/attendance.tsx
src/modules/attendance/api.ts
src/modules/attendance/types.ts
src/components/attendance/TodayPunchCard.tsx
src/components/attendance/AvailabilityForm.tsx
src/components/attendance/WeeklyScheduleTable.tsx
src/components/attendance/LeaveRequestCard.tsx
src/components/attendance/ManagerApprovalList.tsx
src/locales/zh/screens/attendance.json
src/locales/en/screens/attendance.json
app/(tabs)/_layout.tsx
src/locales/zh/common.json
src/locales/en/common.json
```

App `考勤` Tab 页面分区：

```text
今日打卡
- 今日班次
- 上班/下班打卡按钮
- 当前打卡状态
- 如当天为公共假期，显示公共假期提示

本周排班
- 周一到周日周排班表
- 用户有多个相关分店时显示分店选择
- 员工可以只读查看相关分店周排班
- 当前员工自己的班次需要明显突出

可上班时间
- 员工按周填写自己可上班时间段
- 支持一天填写多个时间段
- 支持修改或取消已上报时间段
- 上报内容只作为店长排班参考，不等于正式排班

请假申请
- 年假申请
- 病假申请
- 公共假期申请
- 查看自己的申请状态

店长审核
- 仅 StoreManager 显示
- 管理分店选择
- 异常打卡审核
- 年假/病假/公共假期审核
```

## Web 端设计

Web 根目录：

```text
/Users/sean/DEV/hbweb_rv
```

建议新增或修改文件：

```text
src/pages/StoreOperations/Attendance/index.tsx
src/services/attendanceService.ts
src/router/routes.tsx
src/utils/access.ts
```

菜单位置：

```text
门店运营
- 排班考勤
```

页面 Tab：

```text
周排班
- 分店、周、员工筛选
- 周一到周日周表
- 展示员工上报的可用时间段作为排班参考
- 新增、编辑、取消班次
- StoreManager 只能操作自己管理的分店

员工可用时间
- 按分店、周、员工查看可上班时间
- StoreManager 只能查看自己管理分店员工的上报内容
- 用于辅助排班，不直接替代正式班次

打卡记录
- 上班、下班打卡记录
- 支持正常、迟到、早退、未排班、重复打卡、已通过、已拒绝筛选

审核中心
- 异常打卡审核
- 年假审核
- 病假审核
- 公共假期审核

公共假期
- 分店公共假期列表
- BusinessStatus 支持 Open / Closed / Partial
- Partial 支持 OpenTime / CloseTime

考勤设置
- 仅 Admin 可见和可修改
- 迟到缓冲分钟数
- 早退缓冲分钟数
- 是否允许未排班打卡
- 各类异常是否需要审核
```

## 导航与访问控制

后端导航需要在用户拥有考勤权限时暴露 Web 菜单。App 导航需要给已登录的 StoreStaff、StoreManager、Admin 暴露 `attendance` 路由。设备匿名模式第一版不显示 `考勤` Tab，除非后续明确要求设备模式也支持考勤。

访问控制必须前后端都做：

- 后端按当前用户角色和可管理/相关分店范围过滤数据。
- Web 端通过 `access.ts` 隐藏无权限操作。
- App 端对 StoreStaff 隐藏店长审核和编辑入口。

## 第一版不做

- GPS 围栏。
- 拍照打卡。
- 人脸识别。
- Wi-Fi 或设备位置强校验。
- 工资计算。
- 年假/病假余额自动累计。
- 跨天夜班。
- 班次模板和周期性自动生成排班。
- `Partial` 公共假期营业时间强制限制。

## 验收标准

- Admin 可以配置全局考勤设置，包括迟到/早退缓冲分钟数。
- StoreManager 只能创建和编辑自己管理分店的排班。
- StoreManager 可以查看自己管理分店员工上报的可用时间段。
- StoreManager 只能配置自己管理分店的公共假期。
- StoreStaff 可以在 App 查看自己的排班和相关分店周排班。
- StoreStaff 可以在 App 按周上报、修改、取消自己的可上班时间段。
- App 底部存在 `考勤` Tab。
- 员工可以在 App 完成上班/下班打卡。
- 迟到、早退、未排班、重复打卡会生成待审核记录。
- 员工可以提交年假、病假、公共假期申请。
- StoreManager 可以通过或拒绝自己管理分店的打卡和请假/公共假期申请。
- 每个分店的公共假期都可以设置为 Open、Closed、Partial。
- `Partial` 公共假期显示特殊营业时间和提醒，但不阻止保存排班。
- 打卡记录保存 UTC 时间、分店本地时间、分店时区，并按分店本地时间判断迟到/早退。
