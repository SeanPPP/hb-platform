# WPF 主管付款结案与审计上传交接

## 1. 交接摘要

- 日期：2026-07-29
- 仓库：`D:\DevRepos\hb-platform`
- 分支：`main`
- 当前 HEAD：`080a6da3`
- 工作区：脏工作区，创建本文档前共 106 个变更项
- 提交状态：本轮未提交、未推送

本轮已完成“主管付款结案备注改为选填”的客户端修改与验证。金融安全条件没有放宽：

- 确认已付款：付款参考号或银行证据至少填写一项。
- 确认未付款：银行证据仍必填。
- 继续等待：备注和证据均可为空，付款保持 `Recovering` 和锁定状态。
- 备注仍限制为最多 500 个字符。
- 刷卡退款和分期退款的主管原因仍必填。

当前存在一个必须后续处理的跨端兼容缺口：主管付款结案审计会保存到本地并进入上传队列，但会被现有 Hbpos.Api 永久拒绝，因此暂时无法在云端“员工操作日志”查看。

## 2. 本轮已完成内容

### 2.1 付款结案校验

文件：

- `apps/pos-wpf/src/Hbpos.Client.Wpf/Services/CardPaymentRecoveryService.cs`

`CardPaymentSupervisorResolutionRules.TryNormalize` 已移除付款结案备注的统一必填规则，同时保留：

- 主管身份校验。
- 备注、证据和付款参考号长度限制。
- `ConfirmPaid` 的参考号或证据要求。
- `ConfirmNotPaid` 的银行证据要求。

空白备注会被归一化为 `string.Empty`，不会改变现有 record/interface。

### 2.2 SQLite 状态和主管日志

文件：

- `apps/pos-wpf/src/Hbpos.Client.Wpf/Services/LocalCardPaymentAttemptRepository.cs`
- `apps/pos-wpf/src/Hbpos.Client.Wpf/Services/LocalFinancialSupervisorResolutionRepository.cs`

行为：

- `ActiveSession` 允许 `Reason = ""`。
- `CardRefund` 和 `InstallmentRefund` 仍拒绝空白原因。
- `Reason TEXT NOT NULL` 保持不变，无数据库迁移。
- attempt 状态 CAS 更新与 `LocalFinancialSupervisorResolutions` INSERT 仍在同一 SQLite transaction。
- 只有证据、没有备注时，`ResponseText` 保存为 `Evidence: <证据>`，无前导空格。
- 备注和证据均为空时，`ResponseText` 保存空字符串；决定仍保存在 `ResponseCode`。

### 2.3 UI 文案

文件：

- `apps/pos-wpf/src/Hbpos.Client.Wpf/Resources/Strings.resx`
- `apps/pos-wpf/src/Hbpos.Client.Wpf/Resources/Strings.zh-CN.resx`

文案：

- 英文：`Supervisor note (optional)`
- 中文：`主管备注（选填）`

仅修改资源文案，没有修改弹窗布局、字段顺序或按钮。

## 3. 已完成验证

新增或调整的测试文件：

- `apps/pos-wpf/tests/Hbpos.Client.Tests/CardPaymentRecoveryServiceTests.cs`
- `apps/pos-wpf/tests/Hbpos.Client.Tests/CardRefundRecoveryPresenterTests.cs`
- `apps/pos-wpf/tests/Hbpos.Client.Tests/LocalFinancialSupervisorResolutionRepositoryTests.cs`
- `apps/pos-wpf/tests/Hbpos.Client.Tests/LocalizationAndSettingsTests.cs`

覆盖范围：

- 空备注 + 付款参考号可确认已付款。
- 空备注 + 银行证据可确认未付款。
- 空备注可继续等待，attempt 保持 `Recovering`，付款锁不解除。
- 已付款缺少参考号和证据仍失败。
- 未付款缺少银行证据仍失败。
- 超长备注仍失败。
- 真实 SQLite 双主管 CAS 竞争只有一个成功。
- 状态更新和主管日志 INSERT 原子提交。
- 空备注保存为 `""`。
- 刷卡退款、分期退款的空原因仍被拒绝。
- 中英文选填文案正确。

验证结果：

```text
定向测试：108/108 通过
WPF Release 构建：0 警告，0 错误
排除既有基线失败后的完整测试：2004/2004 通过
```

完整套件仍有一个既有失败，与本轮修改无关：

```text
TransactionHistoryViewModelTests.Suspended_order_labels_follow_localization_culture
Expected: "Suspended"
Actual: null
```

任务文件的 `git diff --check` 已通过。全工作区检查仍被无关文件阻断：

```text
apps/pos-wpf/src/Hbpos.Client.Wpf/Services/VoucherApiClient.cs:166
trailing whitespace
```

## 4. 当前审计数据链路

主管付款结案成功后：

1. `BuildPaymentSupervisorJournal` 创建稳定的 `AuditEventId` 和审计 payload。
2. attempt CAS 与 `LocalFinancialSupervisorResolutions` INSERT 同事务提交。
3. `FinancialSupervisorAuditReplayService.PersistAfterCommitAsync` 将 payload 写入 `OperationAuditOutbox`。
4. `OperationAuditUploadService` 向以下接口发送批次：

```text
POST api/v1/operation-audits/batch
```

客户端 payload 当前包含：

```text
OperationType = CARD_PAYMENT_SUPERVISOR_RESOLUTION
Outcome       = ConfirmPaid | ConfirmNotPaid | ContinueWaiting
SafeMessage   = 主管备注
Properties:
  attemptGuid
  operationGuid
  sessionId
  evidence
  financialReference
```

注意：`LocalFinancialSupervisorResolutions.AuditPersistedAt` 只表示 payload 已写入本地 outbox，不表示云端已经接收。

## 5. 已确认的云端审计阻断

服务端文件：

- `apps/pos-wpf/src/Hbpos.Api/Services/OperationAuditIngestService.cs`

当前服务端会拒绝上述事件：

1. `AllowedOperationTypes` 不包含 `CARD_PAYMENT_SUPERVISOR_RESOLUTION`。
2. `AllowedOutcomes` 只允许 `Succeeded`、`Denied`、`Failed`，不允许三态决定名称。
3. `AllowedPropertyKeys` 不包含 `attemptGuid`、`operationGuid`、`sessionId`、`evidence`、`financialReference`。

实际联网上传时，事件首先会收到：

```text
INVALID_OPERATION_TYPE
operationType is not supported
```

客户端随后会将该 outbox 事件标记为 `Rejected`，不会自动重试。即使只放行 operation type，仍会继续遇到 `INVALID_OUTCOME`；即使再修复 outcome，未加入白名单的证据和金融参考号仍会被服务端丢弃。

## 6. 当前在哪里查看

### 6.1 POS 同步中心

路径：

```text
同步中心 -> 审计日志
```

当前只能看到 EventId、状态、时间和上传错误，不显示备注或证据。联网上传后，该事件预计显示为 `Rejected / INVALID_OPERATION_TYPE`。

### 6.2 本地主管结案记录

数据库：

```text
%LOCALAPPDATA%\Hbpos.Client\hbpos_client.db
```

表：

```text
LocalFinancialSupervisorResolutions
```

只读查询：

```sql
SELECT
    ResolvedAt,
    Target,
    Decision,
    OperatorCashierId,
    OperatorUserGuid,
    OperatorName,
    Reason,
    Evidence,
    FinancialReference,
    AuditEventId,
    AuditPersistedAt
FROM LocalFinancialSupervisorResolutions
ORDER BY ResolvedAt DESC;
```

### 6.3 本地上传队列

数据库：

```text
%LOCALAPPDATA%\Hbpos.Client\hbpos_logs.db
```

表：

```text
OperationAuditOutbox
```

只读查询：

```sql
SELECT
    OccurredAtUtc,
    State,
    AttemptCount,
    LastErrorCode,
    LastErrorMessage,
    PayloadJson
FROM OperationAuditOutbox
ORDER BY OccurredAtUtc DESC;
```

查看本地数据库时建议先关闭 POS，或复制数据库及对应的 `-wal`、`-shm` 文件后只读查看，避免工具持有写锁。

### 6.4 Web 员工操作日志

目标页面：

```text
/pos-admin/operation-logs
```

权限：

```text
Permissions.PosTerminal.Audit.View
```

相关文件：

- `apps/web/src/pages/PosAdmin/OperationLogs/index.tsx`
- `apps/web/src/pages/PosAdmin/OperationLogs/operationLogsLogic.ts`
- `services/backend/BlazorApp.Api/Controllers/React/PosOperationAuditController.cs`

当前由于服务端拒绝，云端页面查不到这些主管付款结案事件。

此外，Web 的 `OPERATION_TYPE_KEYS` 尚未包含新事件类型。即使服务端放行，页面也会显示原始代码 `CARD_PAYMENT_SUPERVISOR_RESOLUTION`，并且操作类型下拉框不能直接选择该类型。

## 7. 下一步最小修复清单

建议按以下顺序完成，不修改金融状态机：

1. 客户端审计事件：
   - `OperationType` 保持稳定代码 `CARD_PAYMENT_SUPERVISOR_RESOLUTION`。
   - `Outcome` 改为服务端允许的 `Succeeded`。
   - 三态决定继续放在 `ReasonCode`。
2. Hbpos.Api：
   - 将 `CARD_PAYMENT_SUPERVISOR_RESOLUTION` 加入 `AllowedOperationTypes`。
   - 将主管结案所需属性加入 `AllowedPropertyKeys`。
   - 保留现有敏感信息清洗和长度限制。
3. Web：
   - 为新 operation type 增加中英文显示名称。
   - 加入操作类型筛选下拉。
   - 详情中将 `SafeMessage` 作为主管备注展示。
   - 从 `PropertiesJson` 展示证据和金融参考号。
4. 测试：
   - 客户端 payload 合约测试。
   - Hbpos.Api 接收并保存备注、证据、参考号测试。
   - 非白名单属性仍被过滤测试。
   - Web 映射、筛选和详情展示测试。
   - 真实 SQLite outbox 从 Pending 到成功确认的集成测试。

## 8. 后续验收标准

- [ ] 主管付款结案事件不再出现 `INVALID_OPERATION_TYPE`。
- [ ] 云端 `pos_operation_audit` 存在对应 `AuditEventId`。
- [ ] Web 员工操作日志可按“主管付款结案”筛选。
- [ ] 详情页显示决定、主管身份、备注、证据和付款参考号。
- [ ] 空备注显示为 `-`，不影响证据和参考号显示。
- [ ] ContinueWaiting 仍保持付款锁。
- [ ] 上传失败或离线时事件保持可恢复，不丢失本地主管日志。
- [ ] 重启重放使用原 `AuditEventId`，服务端重复接收为幂等 duplicate。
- [ ] 刷卡退款和分期退款的原因必填规则未改变。

## 9. 工作区注意事项

- 当前工作区包含大量其他支付、生命周期、设置和性能修改。
- 不得使用 `git reset --hard`、`git checkout --` 或批量覆盖清理。
- `LocalFinancialSupervisorResolutionRepository.cs` 及部分相关测试仍为未跟踪文件，是前序金融修复的一部分，不得遗漏。
- 后续提交必须按任务路径精确暂存，并继续使用中文提交信息和 reasonix。
- GitNexus 当前索引落后于工作区，曾返回旧的“备注必填”实现。涉及本交接内容时，应以当前源文件和测试结果为准；索引更新后再重新运行影响分析。
