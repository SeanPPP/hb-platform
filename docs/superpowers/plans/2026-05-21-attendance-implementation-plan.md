# App 排班与打卡功能实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 按中文设计文档实现员工可用时间上报、周排班、App 基础打卡、店长审核、分店公共假期、请假审核和 Admin-only 考勤设置。

**Architecture:** 后端先落统一契约和权限，Web 与 App 通过同一组 `/api/react/v1/attendance` 接口接入。后端负责所有角色边界、StoreManager 多分店范围、时区判断、状态流转；前端只做显示、提交和友好提示。三端文件边界分开，后端 worker 只写 `HBBblazorweb-master-vite`，App worker 只写 `HbwebExpo/HbwebExpoApp`，Web worker 只写 `hbweb_rv`。

**Tech Stack:** .NET API + SqlSugar-style models/services/controllers/tests, Expo React Native + React Native Paper + TanStack Query + i18n, React/Vite/Ant Design Web + shared request/access utilities.

---

## 参考设计

- 设计文档：`/Users/sean/DEV/HBBblazorweb-master-vite/docs/superpowers/specs/2026-05-21-attendance-design.md`
- 后端：`/Users/sean/DEV/HBBblazorweb-master-vite`
- App：`/Users/sean/DEV/HbwebExpo/HbwebExpoApp`
- Web：`/Users/sean/DEV/hbweb_rv`

## 解耦任务拆分

### Task 1: 后端考勤契约、权限、模型和核心服务

**Owner:** 后端 worker  
**Write scope:** 只修改 `/Users/sean/DEV/HBBblazorweb-master-vite`  
**Files:**
- Create: `BlazorApp.Shared/DTOs/AttendanceDtos.cs`
- Create: `BlazorApp.Shared/Models/HBweb/Attendance/AttendanceSchedule.cs`
- Create: `BlazorApp.Shared/Models/HBweb/Attendance/AttendanceAvailability.cs`
- Create: `BlazorApp.Shared/Models/HBweb/Attendance/AttendancePunch.cs`
- Create: `BlazorApp.Shared/Models/HBweb/Attendance/AttendanceApproval.cs`
- Create: `BlazorApp.Shared/Models/HBweb/Attendance/AttendanceStoreHoliday.cs`
- Create: `BlazorApp.Shared/Models/HBweb/Attendance/AttendanceLeaveRequest.cs`
- Create: `BlazorApp.Shared/Models/HBweb/Attendance/AttendanceSettings.cs`
- Create: `BlazorApp.Api/Interfaces/React/IAttendanceReactService.cs`
- Create: `BlazorApp.Api/Services/React/AttendanceReactService.cs`
- Create: `BlazorApp.Api/Controllers/React/ReactAttendanceController.cs`
- Create: `BlazorApp.Api.Tests/AttendanceReactServiceTests.cs`
- Modify: `BlazorApp.Shared/Constants/Permissions.cs`
- Modify: `BlazorApp.Api/Services/NavigationService.cs`
- Modify: `BlazorApp.Api/Program.cs`
- Modify: `BlazorApp.Api.Tests/BlazorApp.Api.Tests.csproj`

- [ ] **Step 1: 增加后端权限常量和中文权限名称**

在 `Permissions.cs` 新增 `Attendance` nested class，权限名和中文含义如下，分类统一为 `排班考勤`：

```text
Attendance.Schedule.ViewSelf = 查看自己的排班
Attendance.Schedule.ViewStore = 查看相关分店排班
Attendance.Schedule.EditManagedStore = 编辑管理分店排班
Attendance.Availability.SubmitSelf = 上报自己的可上班时间
Attendance.Availability.ViewManagedStore = 查看管理分店可上班时间
Attendance.Punch.Self = 本人打卡
Attendance.Punch.ViewManagedStore = 查看管理分店打卡记录
Attendance.Approval.ViewManagedStore = 查看管理分店审核记录
Attendance.Approval.ReviewManagedStore = 审核管理分店考勤
Attendance.Holiday.ViewStore = 查看分店公共假期
Attendance.Holiday.EditManagedStore = 编辑管理分店公共假期
Attendance.Leave.ApplySelf = 本人提交请假申请
Attendance.Leave.ViewManagedStore = 查看管理分店请假申请
Attendance.Leave.ReviewManagedStore = 审核管理分店请假申请
Attendance.Settings.Edit = 编辑考勤设置
Attendance.Admin.View = 查看全部考勤管理
```

- [ ] **Step 2: 创建模型和 DTO**

按设计文档创建模型，字段必须包含：

```text
ScheduleGuid, StoreCode, UserGuid, WorkDate, StartTime, EndTime, Status
AvailabilityGuid, WeekStartDate, AvailableDate, StartTime, EndTime, Status
PunchGuid, ScheduleGuid, StoreTimeZone, PunchTimeUtc, PunchTimeLocal, WorkDate, PunchType, Status
ApprovalGuid, SourceType, SourceGuid, ReviewStatus
HolidayGuid, HolidayDate, HolidayName, BusinessStatus, OpenTime, CloseTime
LeaveGuid, LeaveType, StartDate, EndDate, StartTime, EndTime, Status
LateGraceMinutes, EarlyLeaveGraceMinutes
```

DTO 必须覆盖前端需要的 query/create/update/result：周排班、可上班时间、今日打卡、打卡记录、审核、公共假期、请假、考勤设置。

- [ ] **Step 3: 实现 `AttendanceReactService`**

服务层必须复用 `ICurrentUserManageableStoreScopeService` 校验 StoreManager 管理分店范围；禁止只信任前端传入的 `storeCode`。员工相关查询按 `UserGUID` 过滤。打卡规则：

```text
StoreTimeZone 支持 Australia/Brisbane、Australia/Melbourne，默认 Australia/Sydney
保存 PunchTimeUtc、PunchTimeLocal、WorkDate
迟到: PunchTimeLocal > StartTime + LateGraceMinutes
早退: PunchTimeLocal < EndTime - EarlyLeaveGraceMinutes
未排班、迟到、早退、重复打卡生成 Pending approval
正常打卡不生成 approval
```

- [ ] **Step 4: 实现 `ReactAttendanceController`**

路由统一使用：

```text
/api/react/v1/attendance
```

必须实现设计文档列出的 endpoints：

```text
GET/POST/PUT/DELETE schedules
GET my/today, my/week, my/availability
POST/PUT/cancel my/availability
POST punch
GET/POST/cancel my/leave-requests
GET availability
GET punches
GET approvals, approvals/pending
POST approvals/{approvalGuid}/approve
POST approvals/{approvalGuid}/reject
GET/POST/PUT/DELETE holidays
GET/PUT settings
```

- [ ] **Step 5: 接入导航和 DI**

`Program.cs` 注册 `IAttendanceReactService`。`NavigationService.cs` 添加 Web 菜单 `/pos-admin/schedule-attendance`，TitleKey `menu.scheduleAttendance`，Permission 建议 `Attendance.Schedule.ViewStore`。App `FullAppMenu` 增加 `attendance` route，TitleKey `tabs.attendance`，Icon `calendar-clock`，权限建议 `Attendance.Schedule.ViewSelf`，设备匿名菜单不加入。

- [ ] **Step 6: 增加后端测试**

新增 `AttendanceReactServiceTests.cs`，至少覆盖：

```text
StoreManager 不能查非管理分店
员工可上报一周内多个可用时间段
同员工同分店同日重叠排班被拒绝
Brisbane/Melbourne/Sydney 时区转换生成正确 WorkDate
迟到/早退/未排班生成 Pending approval
Partial 公共假期不阻止排班
```

因为测试项目禁用了默认编译项，必须同步修改 `BlazorApp.Api.Tests.csproj`。

- [ ] **Step 7: 验证后端**

Run:

```bash
cd /Users/sean/DEV/HBBblazorweb-master-vite
dotnet build BlazorApp.sln
dotnet test BlazorApp.Api.Tests/BlazorApp.Api.Tests.csproj --filter "FullyQualifiedName~Attendance"
```

Expected: build passes; attendance tests pass.

### Task 2: App 考勤 Tab、API 归一化、员工和店长工作流

**Owner:** App worker  
**Write scope:** 只修改 `/Users/sean/DEV/HbwebExpo/HbwebExpoApp`  
**Depends on:** 后端 endpoint contract from Task 1. 可以先按设计文档 contract 实现，待后端完成后联调。  
**Files:**
- Create: `app/(tabs)/attendance.tsx`
- Create: `src/modules/attendance/api.ts`
- Create: `src/modules/attendance/types.ts`
- Create: `src/components/attendance/TodayPunchCard.tsx`
- Create: `src/components/attendance/AvailabilityForm.tsx`
- Create: `src/components/attendance/WeeklyScheduleTable.tsx`
- Create: `src/components/attendance/LeaveRequestCard.tsx`
- Create: `src/components/attendance/ManagerApprovalList.tsx`
- Create: `src/locales/zh/screens/attendance.json`
- Create: `src/locales/en/screens/attendance.json`
- Modify: `app/(tabs)/_layout.tsx`
- Modify: `src/shared/i18n/i18n.ts`
- Modify: `src/locales/zh/common.json`
- Modify: `src/locales/en/common.json`

- [ ] **Step 1: 定义 App attendance types 和 API**

`api.ts` 必须使用现有 `apiClient`，并兼容 camelCase/PascalCase 响应字段。实现：

```text
getMyAttendanceToday()
getMyAttendanceWeek()
getMyAvailability()
createAvailability()
updateAvailability()
cancelAvailability()
punchAttendance()
getMyLeaveRequests()
createLeaveRequest()
cancelLeaveRequest()
getPendingApprovals()
approveAttendanceApproval()
rejectAttendanceApproval()
```

- [ ] **Step 2: 新增 `考勤` Tab**

`_layout.tsx` 必须同步：

```text
TAB_PATHS union 增加 attendance
TAB_PATHS object 增加 attendance: "/(tabs)/attendance"
Tabs.Screen name="attendance"
href 使用 isRouteVisible("attendance")
icon 使用 MaterialCommunityIcons calendar-clock 或 calendar-check
title 使用 t("tabs.attendance")
```

- [ ] **Step 3: 注册 i18n**

新增 `attendance` namespace 的 zh/en JSON，修改 `i18n.ts` imports、resources、ns。`common.json` tabs 增加：

```text
zh: "attendance": "考勤"
en: "attendance": "Attendance"
```

- [ ] **Step 4: 实现页面和组件**

页面应使用现有 SafeAreaView、ScrollView、React Native Paper、React Query、Snackbar 模式。分区：

```text
今日打卡
本周排班
可上班时间
请假申请
店长审核，仅 StoreManager 显示
```

员工可上班时间支持一日多个时间段，提交后刷新 `my/availability` 和 `my/week`。店长审核列表调用 pending approvals。

- [ ] **Step 5: 验证 App 类型**

Run:

```bash
cd /Users/sean/DEV/HbwebExpo/HbwebExpoApp
npx tsc --noEmit
```

Expected: no TypeScript errors.

### Task 3: Web 排班考勤管理页、服务、权限和菜单

**Owner:** Web worker  
**Write scope:** 只修改 `/Users/sean/DEV/hbweb_rv`  
**Depends on:** 后端 endpoint contract from Task 1. 可以先按设计文档 contract 实现，待后端完成后联调。  
**Files:**
- Create: `src/pages/PosAdmin/ScheduleAttendance/index.tsx`
- Create: `src/services/scheduleAttendanceService.ts`
- Create: `src/types/scheduleAttendance.ts`
- Modify: `src/router/routes.tsx`
- Modify: `src/types/auth.ts`
- Modify: `src/utils/access.ts`
- Modify: `src/types/permissions.ts`
- Modify: `src/i18n/locales/zh.json`
- Modify: `src/i18n/locales/en.json`

- [ ] **Step 1: 定义 Web types 和 service**

`scheduleAttendanceService.ts` 使用现有 `request`、`unwrapApiData`、`unwrapPagedResult` 风格。覆盖：

```text
schedules week/list/create/update/delete
availability list
punches list
approvals list/approve/reject
holidays list/create/update/delete
settings get/update
```

- [ ] **Step 2: 增加前端权限**

`types/permissions.ts` 加 attendance constants，`types/auth.ts` 增加 AccessControl boolean，`utils/access.ts` 默认 false 并基于 `hasPermission()` 计算：

```text
canViewAttendanceSchedule
canEditAttendanceSchedule
canViewAttendanceAvailability
canViewAttendancePunches
canReviewAttendance
canEditAttendanceHoliday
canEditAttendanceSettings
```

- [ ] **Step 3: 增加路由和菜单**

在 `/pos-admin` 下新增：

```text
path: "/pos-admin/schedule-attendance"
title: "menu.scheduleAttendance"
icon: "CalendarOutlined" 或 "ScheduleOutlined"
keepAlive: true
accessKey: "canViewAttendanceSchedule"
```

同步 `iconMap` import 和 zh/en 菜单文案。

- [ ] **Step 4: 实现页面**

页面使用 AntD `Card`、`Tabs`、`Table`、`Form`、`DatePicker`、`Select`、`Drawer/Modal`。Tabs：

```text
周排班
员工可用时间
打卡记录
审核中心
公共假期
考勤设置
```

StoreManager 只能操作自己管理分店由后端保证，前端仍按权限隐藏按钮。`Partial` 公共假期只显示提醒，不阻止保存。

- [ ] **Step 5: 验证 Web**

Run:

```bash
cd /Users/sean/DEV/hbweb_rv
npm run build
```

Expected: TypeScript build and Vite build pass.

### Task 4: 集成验证和收尾

**Owner:** 主代理  
**Write scope:** 必要时跨三仓库做小型集成修正，不能重写 worker 完成的大块逻辑。  

- [ ] **Step 1: 检查三仓库状态**

Run:

```bash
git -C /Users/sean/DEV/HBBblazorweb-master-vite status --short
git -C /Users/sean/DEV/HbwebExpo status --short
git -C /Users/sean/DEV/hbweb_rv status --short
```

- [ ] **Step 2: 运行验证**

Run:

```bash
cd /Users/sean/DEV/HBBblazorweb-master-vite && dotnet build BlazorApp.sln
cd /Users/sean/DEV/HBBblazorweb-master-vite && dotnet test BlazorApp.Api.Tests/BlazorApp.Api.Tests.csproj --filter "FullyQualifiedName~Attendance"
cd /Users/sean/DEV/HbwebExpo/HbwebExpoApp && npx tsc --noEmit
cd /Users/sean/DEV/hbweb_rv && npm run build
```

- [ ] **Step 3: 修正集成不一致**

重点检查：

```text
后端 permission code 是否和 Web/App 使用一致
后端 app-menu routeName 是否为 attendance
Expo TAB_PATHS 是否包含 attendance
Web route path 是否与后端 NavigationService 菜单一致
DTO 字段 camel/Pascal 是否被前端归一化
```

- [ ] **Step 4: 最终提交**

如果 worker 没有各自提交，主代理按仓库分别创建聚焦提交，避免跨仓库混在一起。
