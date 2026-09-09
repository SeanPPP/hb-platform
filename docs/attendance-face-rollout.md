# iPad 前置拍照考勤发布说明

## 变更范围与状态

客户端原生/runtime版本为0.2.1，新增独立 SQLCipher 考勤队列及 Keychain HMAC。中心服务和 Hbpos 网关的功能开关默认关闭。本功能可以与既有二维码打卡、销售和审计同步同时存在；未成功同步过人脸名单的设备仍默认进入原二维码页；已启用的设备仍可切回“二维码与审计”。本说明是发布步骤，不是生产发布记录。

流程为选择本店人员、确认上/下班、前置相机拍摄、本地事务成功、补传、服务端核验、正式记卡。没有历史状态建议上班。显示的员工编号使用现有唯一 `User.Username`；不会用员工登录条码作为公开编号。拍照后“下一位”清空选中人员和照片。

## 存储与认证边界

- iPad `face_attendance_*` 表由迁移M44建立，与订单和审计outbox分开。事件的门店、员工、硬件、类型、原始时间、顺序号、版本、摘要和签名在本地成功事务后不再改变。相机返回base64后立即清理其临时JPEG，再将照片与事件同一 SQLCipher 事务写入；失败提示“未记录成功”。没有按天删除未上传记录的任务。
- 首次使用必须联网认证设备并同步名单；以后沿用POS现有离线登录规则。只有应用运行或恢复前台时能自动补传，系统挂起/关闭期间保存在本机。订单、OTA、硬件及名单刷新失败不应阻塞考勤队列。
- 设备专用随机HMAC密钥保存在本机 `ThisDeviceOnly` Keychain，服务端密钥由持久化的ASP.NET Data Protection key ring加密。会话提供有效24小时的可信时间锚；锚有效期以**拍摄时刻**计算，上传可以更晚。时钟不连续、重启后无法证明连续性、锚失效仍保存原始事件但需要人工处理。
- iPad每次先GET同一eventGuid再POST，完整接收回执后才清除本地照片。中心事件和加密照片一行原子写入，相同内容重试幂等，不同内容409。核验任务有持久租约和退避，正式 `AttendancePunch.FaceEventGuid` 有唯一索引。
- Hbpos仅接受现有设备认证，覆盖客户端伪造的身份headers，再使用专用 `Attendance.FaceGateway` service token访问中心 `api/internal/attendance/face`。中心不信任客户端自己的人员管理权限声明。模板与照片只由中心解密并通过TLS发送给私有worker；回环地址允许HTTP做本机验证。
- 录入、照片查看、审核分别要求 `Attendance.Face.EnrollManagedStore` / `ViewPhotosManagedStore` / `ReviewManagedStore`，且使用两分钟内在线验证的管理票据和实时有效的分店权限。普通员工只能拍照与查看本机状态。三张照片必须合格且互相匹配，重新录入增加版本，撤销与正式写卡共用员工锁。相同录入照片、重复撤销及相同审核重放不会创建额外版本/考勤。
- 原始照片从中心完整接收起30天到期删除；关闭自动识别后仍继续保留期清理。保留事件、摘要、签名、处理状态、核验分数及审核人/原因。中心现有加密数据库备份也应遵循照片保留策略；恢复旧备份后先执行到期清理再开放照片读取。照片接口在到期后立即410，不依赖清理任务是否刚执行。

## 发布顺序

1. 按项目部署runbook只读核对目标环境、数据库和现行镜像；记录可回退镜像、数据库恢复点以及Data Protection key ring挂载。不要更换/删除现行key ring，否则原有二维码密钥及本功能模板无法解密。
2. 先发布兼容中心后端扩展，保持 `FaceAttendance__Enabled=false`。使用既有发布器的显式 `--schema=migrate` 流程执行 `FaceAttendanceSchemaMigrator`；先在隔离测试库执行。它只新增人脸相关表/列/索引以及可空 `AttendancePunch.FaceEventGuid`，可重复执行。回退保留这些扩展，不DROP表、不删除未处理事件。
3. 在自有服务器构建 `services/attendance-face` 镜像，核对固定模型摘要，并用授权 `GET /health/ready` 检查模型可用。配置中心 `FaceRecognition__BaseUrl` 为私有HTTPS入口，`FaceRecognition__SharedSecret` 与worker `ATTENDANCE_FACE_WORKER_TOKEN`一致；`FaceRecognition__Threshold=0.50` 是初始值。
4. 在管理后台现有service token签发面板选择“iPad 人脸考勤网关”，取得scope仅为 `Attendance.FaceGateway` 的token，存入网关私有配置 `HBPOS_ATTENDANCE_FACE_GATEWAY_TOKEN`。设置 `AttendanceFaceGateway__CenterBaseUrl` 和 `AttendanceFaceGateway__Enabled=true`。不要使用管理员个人token、二维码签名key或OTA发布token代替。
5. 完成真实样本校准和测试店验收后，配置中心 `FaceAttendance__StoreCodes__0`（以及后续编号项）为明确授权的测试门店代码，并启用 `FaceAttendance__Enabled=true`。允许名单为空时所有门店均关闭人脸接口；继续保留其他门店既有方式。
6. 原生构建并分发iPad0.2.1，包含新增HMAC接口及相机权限用途说明。不能把本变更作为runtime0.2.0的OTA发布。首次进入先联网同步名单，店长现场录入后才能离线拍照。
7. 核对实体iPad实际下载并运行的native/runtime版本，完成下列验收，才扩大门店范围。模拟器不提供真实前置摄像头及人脸准确率验收证据。

## 核心验收

- Brisbane时区设备09:00离线上班、17:00离线下班，18:00恢复网络，中心只产生两条正式记录，仍为09:00、17:00以及员工确认的类型。分别核对事件、唯一关联punch和页面状态，不只看HTTP200。
- 连续多员工、断网重连、应用重启、客户端收到响应前断网、中心正式事务提交后worker重启、重复POST；每个eventGuid只对应一条正式punch，未接收照片仍在本地。
- 无脸、多人、模糊、另一人的照片不生成正式考勤；识别服务5xx/超时留queued并退避。
- 时钟回拨、过期锚、跨午夜班次、门店时区、另一设备更晚记录、重复上下班、跨店未闭合/重叠段、班段上限；不确定事件进入需处理，不能按上传顺序反转类型。
- 模板撤销/重新录入、员工停用/调店、管理票据过期、仅照片/仅审核/仅录入权限、SQLCipher存储不足、相机拒绝权限、切后台和返回页面。
- 店长在最近记录查看原始照片、填写处理原因。确认原时间仍需通过1:1核验及既有排班规则；时间线冲突、资料变化需使用管理后台原有补卡/审批。审核不提供修改原始事件的接口。
- 销售、离线订单、审计同步和二维码扫码/补卡/审批照常回归。

## 灰度观察与回退

以下查询只读，不包含照片、模板和凭据：

```sql
SELECT StoreCode, Status, COUNT_BIG(*) AS EventCount,
       MIN(ReceivedAtUtc) AS OldestReceivedUtc
FROM dbo.FaceAttendanceEvent
GROUP BY StoreCode, Status;

SELECT ReasonCode, COUNT_BIG(*) AS EventCount
FROM dbo.FaceAttendanceEvent
WHERE ReceivedAtUtc >= DATEADD(day, -1, SYSUTCDATETIME())
GROUP BY ReasonCode;

SELECT FaceEventGuid, COUNT_BIG(*) AS PunchCount
FROM dbo.AttendancePunch
WHERE FaceEventGuid IS NOT NULL
GROUP BY FaceEventGuid HAVING COUNT_BIG(*) > 1;

SELECT COUNT_BIG(*) AS ExpiredPhotosRemaining
FROM dbo.FaceAttendanceEvent
WHERE RetainUntilUtc <= SYSUTCDATETIME() AND DATALENGTH(ProtectedPhoto) > 0;
```

门店同时查看iPad“待同步”数和最后员工同步时间；中心不能统计还没上传的本机事件。对queued积压、持续核验失败、needsReview数量和重复关联告警。关闭中心识别开关可以停止新自动核验，网关关闭会使客户端继续保留待上传队列；不要卸载iPad App、清空SQLCipher或删除Keychain来回退。回退到兼容客户端前先处理/导出队列，保留旧镜像、数据库恢复点及全部密钥，服务端照片到期清理继续运行。
