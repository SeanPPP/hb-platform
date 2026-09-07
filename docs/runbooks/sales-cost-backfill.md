# 历史销售成本回填维护手册

本手册说明现有成本回填实现的使用和验收方法。它不表示迁移、部署或生产回填已经完成。

成本回填只修改 HBweb 中已有商品日统计的成本快照、总成本、毛利、毛利率，以及对应的澳洲／中国供应商成本汇总和发布版本。POSM 与 HBSales 只作为读取来源；销售额、销量、订单数和原始来源水位不在回填范围内。执行日期必须在 `2025-01-01` 至当前业务日之间。

## 启用条件

后端始终注册 `SalesCostBackfillWorker`。配置项 `SalesStatistics:CostBackfillAutoRetryEnabled` 只控制空闲时是否自动发现并调度实际成本缺口，未配置时等同 `false`。关闭该配置不会阻止管理员创建和执行手工批次。

生产环境应保持自动发现默认关闭，完成迁移、部署和样本验收后再决定是否启用：

```json
{
  "SalesStatistics": {
    "CostBackfillAutoRetryEnabled": false,
    "CostBackfillAutoRetryMaxDatesPerUtcDay": 31,
    "CostBackfillAutoRetryRecentCompletedDays": 7
  }
}
```

自动重试启用后仍按单日创建自动批次，并复用本手册中的冻结预览和 Apply 执行器。默认每个 UTC 日最多调度 31 个日期；最近 7 个已完成统计日优先，剩余名额保留给历史缺口轮转。历史候选按“从未检查优先，其次最久未检查”选择，来源更新后仍会在后续轮次重新获得机会。当前业务日不进入自动范围，避免在日统计尚未完成时读取不完整事实。

这两个额度可以通过配置覆盖，但必须是正整数且不超过 31。默认每天最多检查 31 个日期，其中近期窗口会占用部分额度，历史日期轮转通常需要约一个月；实际时间取决于缺口日期数与批次执行情况。额度只限制自动发现新日期，不限制管理员发起的手工 615 天批次，也不改变已经登记的自动批次。配置关闭后，Worker 仍会继续完成正在执行的批次；它只停止空闲时创建新的自动批次。

当前没有单独可靠的来源更新时间信号可用于把“来源已改变”的历史日期提前排序。`SourceHash` 只有在日期进入 Preview 时才会记录；提前判断变化需要重新读取该日期的销售、映射和成本来源，代价接近一次预览，因此自动调度不假定 `Product.UpdateTime` 等字段代表成本变更。来源补齐会在后续历史轮转中重新获得检查机会，具体延迟取决于当日近期缺口占用的名额和历史缺口数量。

执行 API 前必须先在 **HBweb 主库**运行受控迁移：

```text
services/backend/BlazorApp.Api/Data/Migrations/20260907_CreateSalesCostBackfill.sql
```

迁移脚本会检查 `DB_NAME() = HBweb`，在事务中取得 schema migration application lock，并且只创建以下审计表：

- `SalesCostBackfillBatch`
- `SalesCostBackfillDay`
- `SalesCostBackfillItem`

迁移不应在 POSM、HBSales 或总部数据库执行。执行迁移前必须核对环境和数据库，并准备可验证的恢复点。审计表缺失时，API 返回 `503`，Worker 不会自动建表或修改统计。

## 管理 API

所有接口位于 `/api/StatisticsJobTrigger/cost-backfill`，要求 `Admin` 角色。以下示例中的 `$BASE_URL` 和 `$TOKEN` 由运维环境提供。

### 创建预览

```bash
curl -X POST "$BASE_URL/api/StatisticsJobTrigger/cost-backfill/preview" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"startDate":"2026-09-06","endDate":"2026-09-06"}'
```

接口返回 `202 Accepted`、`batchId` 和 `Previewing`。返回成功只表示批次已登记；Worker 会按日期异步生成预览。必须轮询批次查询，直到批次离开运行状态。

### 查询批次和审计明细

```bash
curl "$BASE_URL/api/StatisticsJobTrigger/cost-backfill/$BATCH_ID?pageIndex=1&pageSize=100" \
  -H "Authorization: Bearer $TOKEN"
```

响应包含批次、逐日状态、候选／未解决／已应用汇总，以及分页审计明细。`pageSize` 最大为 100。

### 应用冻结批次

```bash
curl -X POST "$BASE_URL/api/StatisticsJobTrigger/cost-backfill/$BATCH_ID/apply" \
  -H "Authorization: Bearer $TOKEN"
```

接口返回 `202 Accepted` 和 `Applying`。Apply 只能引用已生成的冻结批次；它不会重新选择候选，也不接受临时商品列表。当前实现以日期为最小执行单元，一个日期内的全部 `Candidate` 会在同一事务中处理。

重复请求正在执行或已完成的 Apply 是幂等操作。事务型失败日期可通过再次请求 Apply 恢复；来源、销售事实或发布版本冲突的日期必须重新创建预览批次。

### 重试尚未冻结的预览日期

```bash
curl -X POST "$BASE_URL/api/StatisticsJobTrigger/cost-backfill/$BATCH_ID/retry-preview" \
  -H "Authorization: Bearer $TOKEN"
```

`retry-preview` 只处理 `Deferred`，或 `FailedOperation=Previewing` 的 `Failed` 日期，而且该日期不能已有审计明细。它会清空未提交的冻结字段和计数，将日期恢复为 `Pending`。自动批次对瞬时预览失败只自动重试一次，使用 `PreviewRetriedBy` 留痕；持续失败留给后续维护处理，避免热循环。

### 回滚已应用批次

```bash
curl -X POST "$BASE_URL/api/StatisticsJobTrigger/cost-backfill/$BATCH_ID/rollback" \
  -H "Authorization: Bearer $TOKEN"
```

Rollback 可用于 `Applied`、`AppliedWithExceptions` 或 `RollbackWithConflicts` 批次。它只恢复仍与本批次 `AfterJson` 完全一致的商品行，并要求当前供应商汇总和发布版本仍与该日冻结的 after publication 一致。

如果某行在 Apply 后被人工或其他任务修改，该行会成为 `RollbackConflict`，不会被覆盖。若整日发布快照已变化，该日回滚整体拒绝。成功恢复的行会重新汇总供应商成本并发布新版本。回滚不会删除审计记录，也不能代替数据库恢复点。

## 状态和异常

批次状态：

| 状态 | 含义 |
| --- | --- |
| `Previewing` | Worker 正在按日期生成冻结预览 |
| `Previewed` | 预览阶段已结束；仍需检查逐日异常和 `Unresolved` |
| `Applying` | 正在应用候选 |
| `Applied` | 所有可执行日期完成，且没有未解决项 |
| `AppliedWithExceptions` | 已应用部分候选，但仍有冲突、失败、延期或未解决项 |
| `RollingBack` | 正在按日期回滚 |
| `RolledBack` | 可恢复的修改已回滚且没有回滚冲突 |
| `RollbackWithConflicts` | 部分行或日期因 CAS 不匹配未恢复 |
| `Failed` | 批次规则版本不兼容等批次级失败 |

日期状态包括 `Pending`、`Previewed`、`Applied`、`RolledBack`、`Deferred`、`Conflict`、`Failed` 和 `RollbackConflict`。其中：

- `Deferred`：该日商品与供应商销售事实尚未形成完整发布快照；统计保持原状。
- `Conflict`：销售事实、来源哈希、目标行、供应商对账或发布版本在冻结后发生变化；该日事务回滚。
- `Failed`：数据库、锁或其他执行错误；`FailedOperation` 记录失败发生在 `Previewing`、`Applying` 或 `RollingBack`。
- `RollbackConflict`：当前数据不再等于本批次写入后的状态，回滚没有覆盖后续修改。

审计项状态包括 `Candidate`、`Unresolved`、`Applied`、`RolledBack` 和 `RollbackConflict`。常见 `Reason`／来源异常包括：

- `VerifiedCostGap`：证据和销售事实一致，可回填。
- `SourceMissing`、`CostEvidenceMissing:*`：没有足够的成本或原始明细证据。
- `SourceFactsDiffer`：来源重建出的销售事实与已发布事实不同。
- `IdentityConflict`：商品或 OpenItem 身份无法唯一确认。
- `ExistingCostConflict`、`ExistingUnitCostConflict`、`ExistingProfitConflict`、`ExistingMarginConflict`：已有成本字段互相矛盾。
- `OpenItemMissingOriginalSale`、`OpenItemPriceConflict`、`OpenItemMissingPrice`：退货原销售、原价字段或逐笔价格证据不完整。
- `StoreRetailPriceConflict`、`StoreRetailPriceUnitConflict`、`MissingStoreCostUnit`：门店候选价格或计价单位不满足一致性要求。

`Unresolved` 保留原成本缺口和具体原因；合法的零成本、零毛利、净退货和无销售记录不应仅因数值为零而被列为缺成本。

## 冻结、审计和规则版本

当前规则版本为 `cost-gap-v1`。版本保存在 `SalesCostBackfillBatch.RuleVersion`；运行时代码与批次版本不一致时，批次不能继续执行，必须重新预览。

预览按日期冻结以下内容：

- `SnapshotVersion`：根据数据库实际保存的商品日统计计算的持久化版本。
- `SourceHash`：原始销售明细、身份映射及本次涉及的成本来源的规范化哈希。
- `BeforePublicationJson`：澳洲／中国供应商事实、成本完整性计数和三类发布版本。
- 每条记录的 `BeforeJson`、`ProposedJson`、`EvidenceJson`、规则原因和检查时间。

Apply 前后都会重新核对商品版本、来源哈希和发布快照；写入后保存 `AfterJson` 与 `AfterPublicationJson`，供回滚 CAS 使用。

主要审计字段如下：

- 批次：日期范围、`RuleVersion`、`RequestedBy`、`AppliedBy`、`RolledBackBy`、`PreviewRetriedBy`、`Automatic`、状态、时间和错误。
- 日期：`SourceHash`、`SnapshotVersion`、`FailedOperation`、before／after publication、候选／未解决／已应用计数、状态和错误。
- 明细：日期、分店、供应商、商品、状态、原因、冲突原因、before／proposed／after 行影像和来源证据。

## 并发和事务边界

Worker 先取得 `SalesCostBackfillWorker/global` 的 30 分钟租约。每个日期还取得日刷新租约；2025 年日期先取得现有 2025 串行租约，再取得日期租约。

SQL Server 写入事务遵守固定顺序：

1. 2025 全局成本 application lock（仅 2025）。
2. 日期成本 application lock。
3. 回填取得商品成本总闸和按商品排序的成本锁；整日日常发布取得现有全局成本独占总闸，避免上万次逐商品 SQL 往返。
4. 重读目标行和来源证据，执行 CAS、成本更新、供应商汇总及版本发布。

日期租约在提交前续期；租约失效会拒绝提交。正常日统计刷新与失败状态写入也使用同一日期锁和状态 fence，避免旧刷新覆盖已回填版本。商品、供应商汇总、完整性计数和发布版本在同一主库事务内提交；供应商销售事实对账不一致时整日回滚。

## SQL Server 验证

并发锁和事务行为必须使用隔离的 SQL Server 测试实例验证。测试代码限定连接目标为 `127.0.0.1,11439`，会创建并删除临时数据库，禁止指向共享或生产实例。

```bash
export COST_BACKFILL_SQLSERVER_TEST_CONNECTION='<127.0.0.1,11439 专用测试实例连接串>'
dotnet test services/backend/BlazorApp.Api.Tests/BlazorApp.Api.Tests.csproj \
  --filter 'FullyQualifiedName~SalesCostBackfillSqlServerTests'
```

纯规则测试可单独运行：

```bash
dotnet test services/backend/BlazorApp.Api.Tests/BlazorApp.Api.Tests.csproj \
  --filter 'FullyQualifiedName~SalesCostBackfillTests|FullyQualifiedName~SalesStatisticsProductStoreDailyCostRulesTests'
```

验证证据应保留测试命令、退出码、实际执行／跳过数量，以及 SQL Server 专用测试使用的实例身份；不得记录连接串或凭据。

## 部署和历史执行顺序

1. 核对目标环境、HBweb 数据库、当前统计版本和恢复点；记录回退版本。保持自动发现配置为关闭。
2. 先部署包含统一成本规则、日期锁、维护 API 和 Worker 的后端，再在 HBweb 执行审计迁移。确认健康检查正常，并只读确认三张审计表存在。
3. 创建全范围预览以获得候选、未解决和异常清单。预览不写成本；检查所有分页结果及逐日状态。
4. 使用 `2026-09-06` 单日批次验证 OPEN ITEM、Dats `72750` 和 Hot Bargain 样本的证据、候选值及报表结果。API 不支持只应用三个商品；生产 Apply 会处理该日全部候选，因此应先在隔离环境验证，生产执行前必须审阅该日完整候选清单。
5. 样本 Apply 后独立读回商品、分店、澳洲／中国供应商报表和发布版本，并核对销售额、销量、订单数与执行前完全一致。若样本已改变原全范围预览覆盖的日期，重新生成用于正式执行的预览，不能继续依赖旧冻结证据。
6. 按日期串行执行最终范围。持续汇总 `Applied`、`Unresolved`、`Conflict`、`Deferred`、`Failed` 和回滚冲突数量；不要把 `Previewed` 或 `202 Accepted` 当作完成。
7. 执行完成后独立读回完整范围，验证供应商成本合计和商品成本合计一致、三类发布版本一致、移动端不再把无同期销售显示为待补全，并在下一次正常日刷新后再次确认成本未丢失。

窄部署期间不要直接把运行中的批次状态改成未定义的 `PreviewPaused`。Worker 只识别 `Previewing`、`Applying` 和 `RollingBack`，而且已开始的 `RunOne` 可能在外部状态修改后使用旧批次对象完成状态更新。应先停止自动发现配置，再对承载 Worker 的实例执行 graceful stop，等待当前日期事务完成或回滚、日期租约和 2025 串行租约释放，并确认 `SalesCostBackfillWorker/global` 已由原 owner 完成。仓库没有覆盖默认 .NET Host 30 秒 ShutdownTimeout 的配置；单日期观测约 40 秒时，部署平台的 termination grace period 至少应设置为数分钟。硬杀后不要清除或手工改写 30 分钟 global lease，等待其 owner/token 失效或按严格 owner/token CAS 运维规程处理。

若需要在不重启 API 的情况下做窄部署保护，只能使用单独的运维租约流程：等待 `SalesCostBackfillWorker/global` 以 `Status=Success` 且 `LeaseUntilUtc=NULL` 完成，最多等待 3 分钟；以独立 owner/token、15 分钟期限进行严格 CAS，占住 global lease 后再确认日期租约均已释放。不得抢占任何 `Running` 行，即使其 `LeaseUntilUtc` 已过期；部署完成后只以该 owner/token 释放。现有通用 `TryAcquireAsync` 条件比这个流程宽，不能直接当作严格运维 CAS。

生产验收结果应分别记录：代码部署、审计迁移、预览完成、样本 Apply、全范围 Apply、异常清单、独立数据读回、移动端显示和后续日刷新验证。没有实际执行和读回证据时，不得标记生产历史回填完成。
