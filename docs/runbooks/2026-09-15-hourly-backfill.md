# 2026-09-15 分时历史回填运行手册

## 边界

回填发布到独立的 `HourlySalesBackfillPublishedRow`，并由逐日 manifest 认证读取。不得覆盖旧 `HourlySalesStatistic`。当前规则版本必须是 `hourly-posm-hbsales-v1`；runner 在批次版本不符、迁移不完整、目标库不符或租约被占用时停止。报表端遇到不受支持的旧批次版本会返回不可用，不能把它当零值。

生产执行由主协调者统一进行。每个阶段先在单日验证，再扩展日期；旧 5001 写原表可继续，只需避免后台回填 worker 或第二个 runner 与本 runner 同时发布。runner 不依赖 `ActiveInstanceId`，也不更改它。

## 构建与容器投放

```bash
dotnet restore outputs/hourly-backfill-runner-20260915/HourlyBackfillRunner.csproj
dotnet build outputs/hourly-backfill-runner-20260915/HourlyBackfillRunner.csproj --no-restore
dotnet run --project outputs/hourly-backfill-runner-20260915/HourlyBackfillRunner.csproj --no-build -- --self-test
dotnet publish outputs/hourly-backfill-runner-20260915/HourlyBackfillRunner.csproj -c Release -r linux-x64 --self-contained false -o outputs/hourly-backfill-runner-20260915/publish-linux-x64
```

只把 publish 目录复制到已核对的容器临时目录。容器可直接使用现有 `ConnectionStrings__DefaultConnection`、`ConnectionStrings__HBPOSMConnection`、`ConnectionStrings__HBSalesRecord`；不复制本地生产凭据文件，不输出环境变量。若使用文件配置，传 `--config-root`。

## 受控迁移门

持久化 preview 前，数据库操作者必须先核对 HBweb 目标实例和库名，建立带时间戳且经过可读验证的备份或恢复点，并记录恢复命令与保留位置。随后只对该目标执行 [`20260915_CreateHourlySalesBackfill.sql`](../../services/backend/BlazorApp.Api/Data/Migrations/20260915_CreateHourlySalesBackfill.sql)；使用数据库团队既有的凭据注入和 `sqlcmd -b` 失败即停方式，不把连接串写入命令历史或日志。

迁移完成后以只读 SQL 核对：

- `HourlySalesBackfillBatch`、`HourlySalesBackfillDay`、`HourlySalesBackfillPublishedRow` 三表存在，外键和主键有效；
- `UX_HourlySalesBackfillDay_OneAppliedPerDate` 是已启用的唯一过滤索引；
- `TR_HourlySalesBackfillPublishedRow_Immutable` 存在且启用，直接 `UPDATE`/`DELETE` 的阻断由受控事务集成测试证明；
- `HourlySalesReadStatistic` view 存在，并按 Applied manifest、当前 rule version 联接 PublishedRow；旧版本或不完整结构必须返回不可用；
- runner 的 `SchemaReady()` 通过后，才允许进入持久化 preview。

迁移失败时停止，不运行 runner；按已记录恢复方案处理。应用后的业务回退只通过正式 rollback 状态机和 hash-CAS 撤销 manifest 认证，PublishedRow 作为不可变审计版本保留，不删除、不更新。

## 1. 纯只读预览

默认模式只读三个来源，逐日原子写 checkpoint，单日失败或规则无效会保留并继续下一日。首次只跑一天：

```bash
dotnet HourlyBackfillRunner.dll preview --start 2025-09-15 --end 2025-09-15 --checkpoint /tmp/hourly-preview-20250915.json --day-timeout-seconds 120 --delay-ms 250
```

确认所有 required sources 均为 `Success`，或以 `Empty` 明确认证真实零销售；非零日应有候选，金额/数量/订单日对账问题须解释后再跑全年。中断后追加 `--resume`；只重试失败日时同时加 `--retry-failed`。按单日 74 至 114 秒估算，365 日约 7.5 至 11.6 小时，实际以容器内采样为准。

## 2. 持久化预览批次

此阶段会写批次和逐日审计状态，但不发布报表数据。先运行一次任意只读命令取得 `TARGET ... fingerprint=HASH`，人工核对服务器和数据库名，再传完整 HASH：

```bash
dotnet HourlyBackfillRunner.dll preview --persist-batch --start 2025-09-15 --end 2026-09-14 --actor hourly-backfill-20260915 --confirm-target HASH --checkpoint /tmp/hourly-stage.json
```

runner 取得 `HourlySalesBackfillWorker/global` 数据库租约后创建批次，只调用带 batchId 的定向 `RunOneAsync`。每步后写 batchId、逐日状态、source/after hash、汇总和错误。租约丢失会在提交前阻断。禁止使用无 batchId 的旧触发接口。

`PreviewedWithIssues` 表示存在失败或无效日期；逐日查看 checkpoint，不能把这些日当零或直接忽略。`status --batch UUID --checkpoint PATH` 是只读状态查询，不取得写租约。

## 3. 应用发布版本

应用前必须完成代码/迁移核对，并从最新 `status` 输出或 checkpoint 取得 manifest hash。只给 runner 进程注入 `SalesStatistics__HourlyBackfillApplyEnabled=true`，无需修改容器持久 `.env` 或启用后台 worker。以下四个条件缺一即阻断：`--allow-apply`、目标 fingerprint、manifest hash、actor。

```bash
SalesStatistics__HourlyBackfillApplyEnabled=true dotnet HourlyBackfillRunner.dll apply --batch UUID --allow-apply --actor hourly-backfill-20260915 --confirm-target HASH --confirm-manifest MANIFEST_HASH --checkpoint /tmp/hourly-apply.json
```

每日期重新读取来源并做 hash 校验，逐日事务插入不可变 PublishedRow 并切换该日 manifest。apply 会连续读取两次来源，runner 的写步骤默认单日总时限为 300 秒；根据容器单日采样可用 `--day-timeout-seconds` 调到最多 600 秒，但每条来源 SQL 的 120 秒上限保持不变。单日失败记录为 Failed，其他日期继续；批次最终可能为 `AppliedWithIssues`。不要因命令退出码非零重复创建批次，先用原 batchId 查询状态。生产 apply 前必须先完成受控目标库上的单日多步 SQL 集成验证，解析 self-test 不代表发布链通过。

## 4. 重新验证

`revalidate` 会重读来源并更新 Applied day 的 Error，因此属于写操作，也需要目标确认和全局租约：

```bash
dotnet HourlyBackfillRunner.dll revalidate --batch UUID --actor hourly-backfill-20260915 --confirm-target HASH --checkpoint /tmp/hourly-revalidate.json
```

出现 `DRIFT` 时停止发布验收，保留 checkpoint，调查 source hash、published hash 和日目标。不要用零填充或重写旧统计掩盖差异。

## 中断、恢复与回退

- `Ctrl-C` 后使用同一个 batchId 和 checkpoint；纯只读阶段加 `--resume`。
- 租约冲突时先确认现有执行器，不删除或强占未过期租约。
- 应用批次的安全回退必须通过后端正式 rollback 状态机和 hash-CAS，只切 manifest；当前 runner 故意不暴露 rollback 命令。
- 保留所有 checkpoint、目标 fingerprint、batchId、manifest hash 和部署版本，避免混用规则版本。
- 后端部署、迁移完成、批次应用、报表读取、mobile OTA/客户端刷新和业务验收是独立状态，逐项记录。
