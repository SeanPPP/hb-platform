# 性能与质量基线脚本

这组脚本为 GitHub Actions 质量基线和发布健康验收提供无第三方运行时依赖的
Node.js 入口。所有 token 只从环境变量读取，禁止放进命令行参数、JSON、artifact
或日志。

## Quality baseline

`.github/workflows/quality-baseline.yml` 在 PR、`main` push 和每天 Brisbane 02:00
运行。PR/push 根据变更路径选择 `backend`、`web`、`pos-ipad`、
`pos-handheld`；nightly 和手动运行覆盖全部 lane。

每个 lane 生成 `QualityLaneResultV1`，记录 UTC 起止、毫秒用时和结论。汇总脚本
把实际取得计时的 lane 转换成服务端 `MetricBatchV1`：`schemaVersion` 固定为整数
`1`，每个 lane 产生一个白名单指标 `ci.run.duration`，lane 与结论分别放在
`dimensions.lane`/`dimensions.outcome`。缺失计时的 lane 不会用伪造的 0ms 污染
指标，但仍会在 summary 中明确标为失败。

事件环境按 GitHub 触发类型统一映射：`main` push、nightly 和手动运行写
`dimensions.environment=Production`，PR 写 `PullRequest`。该映射同时应用于 lane
用时和 Web bundle 指标，不能使用独立的 `CI` 环境，否则 Production 观察周期不会
取得 CI 样本。

Web lane 通过现有 Vite build 的 `--manifest` 参数生成 manifest，随后由
`collect-web-bundle.mjs` 同时解析 `.vite/manifest.json`（兼容根
`manifest.json`）与 `index.html` 的 module script、`modulepreload`、`preload` 和
stylesheet。它使用 Node zlib 的真实 gzip 算法生成 `WebBundleReportV1`：

- 首屏初始依赖闭包中 JS+CSS 的 raw/gzip 总量；
- 按 gzip 字节数计算的最大初始 JS chunk；
- 路由动态 chunk 及其 CSS 的 raw/gzip 明细，作为 artifact/summary 观察数据。

上报时 `web.first_screen.bytes` 取首屏 gzip 总量，
`web.largest_initial_chunk.bytes` 取最大初始 JS chunk gzip；两者单位均为 `bytes`，
并与 Web lane timing 合并到同一个 MetricBatchV1。raw 总量和动态路由 chunk 不作为
独立硬门禁指标，保留在 report artifact。manifest、入口或引用资源缺失会明确失败，
不会产生 0 bytes。若 `npm ci`、build 或测试先失败，仍会记录该 Web lane 的真实
`ci.run.duration`（结论为 failed），但不会采集、上传或用 `0 bytes` 伪造 bundle 指标。
所有资源必须位于 dist 内，路径穿越、外部 URL 和任何
symlink 都会被拒绝。

独立的 `web-bundle-budget.json` 是确定性构建门禁，不与仍处于 `observing` 的
Production P95 预算混用。`npm --prefix apps/web run verify:bundle` 会复用上述 collector
校验 dist/manifest，再验证主入口、首屏、最大初始 JS、任一 JS、Excel/PDF 异步资源、
禁入首屏的重型依赖，以及代表性页面动态入口。阈值按 KiB 固化在版本化预算中；等于
上限通过，超出任意 1 byte 即失败。Vite 可以把所有同步依赖合并进单一入口，因此
`modulepreload`/`preload` 可以为空，但 module script、manifest 入口与完整静态闭包仍为
必填且会递归复核。

两个 POS lane 会在 typecheck 后运行 `verify:metro-bundle`：iPad 导出 iOS、手持
POS 导出 Android。它调用本地 Expo CLI 的 `expo export --platform … --output-dir …`，
产物仅写入系统临时目录，并在成功或失败后清理，因此不会污染共享工作树。

当以下两个 GitHub secrets 同时存在时，always 汇总阶段会以 Bearer service token
POST 到固定路径
`/api/system/performance/automation-batches`：

- `QUALITY_BASELINE_SERVICE_URL`：仅允许 HTTPS origin，例如
  `https://example.internal`，不得附带路径、query、fragment 或凭据。
- `QUALITY_BASELINE_SERVICE_TOKEN`：必须是 `hbsvc_` service token。

为避免可修改 PR 工作流窃取长期 token，外部 POST 只在 `main` push、nightly 和
手动运行执行；PR 仍生成完整 batch、预算报告和 artifact，但不会注入这两个 secret。
如需把 PR 数据自动写入服务端，应另建由主分支控制的 `workflow_run` 上报器。

两者都缺失时不会触网，batch、budget report 和 summary 仍会保存；只配置其中一个
会作为配置错误失败。客户端拒绝 redirect，请求默认 10 秒超时，HTTP 错误不读取或
打印响应正文。

本地只运行脚本测试（不会触网）：

```bash
node --test scripts/performance/*.test.mjs
```

## SQL Server Snapshot 发布前门禁

性能总览、序列和正式冻结使用显式 SQL Server Snapshot 事务，以同时避开全局
`NOLOCK` 的脏读和冻结时的跨表锁序死锁。API 启动迁移只读检查当前数据库的
`snapshot_isolation_state`；若不是 `ON` 会明确失败，应用和部署脚本不会执行
`ALTER DATABASE`。

首次发布前，DBA 必须按现有数据库维护流程核对目标库、备份、长事务以及
tempdb/version store 容量，并在维护窗口手工执行
`scripts/performance/dba-enable-sqlserver-snapshot-isolation.sql`。该脚本幂等，只为
当前业务库开启 `ALLOW_SNAPSHOT_ISOLATION`，不会开启 `READ_COMMITTED_SNAPSHOT`，
也不会使用 `ROLLBACK IMMEDIATE` 终止现有事务。执行后必须确认
`snapshot_isolation_state_desc=ON`，再开始 API 发布。

## JSON budget

根 `quality-baseline-budget.json` 初始为空的 `observing` 配置，不包含任何本机测量
结果。Production 基线冻结且两项 Web 指标各有至少 30 个样本后，管理员可从
`/system/performance-baseline` 导出候选文件；首屏上限按精确原始样本 P95 加
`min(5%, 100 KiB)`，最大初始 chunk 按 P95 加 `min(5%, 50 KiB)`。导出文件仍须
人工评审，并通过独立 PR 替换仓库预算。预算规则示例：

```json
{
  "schemaVersion": "QualityBaselineBudgetV1",
  "mode": "frozen",
  "metrics": {
    "web.first_screen.bytes#lane=web": {
      "max": 1000000,
      "unit": "bytes",
      "required": true
    },
    "web.largest_initial_chunk.bytes#lane=web": {
      "max": 500000,
      "unit": "bytes",
      "required": true
    }
  }
}
```

以上数字只演示文件结构，不是当前冻结基线；阈值只能在观察数据完成评审后写入。
测试 fixture 使用人工小数值验证两项规则：`observing` 只记录超限且退出 0，
`frozen` 任一超限或 required 指标缺失即退出 1。

`observing` 会报告缺失、单位不匹配和越界，但退出码始终为 0；`frozen` 在本次选择
了指标所属 lane 时，任一 required metric 缺失或超出 min/max 才退出 1。因此
backend/POS-only 运行不会因未选择 Web lane 而触发 Web frozen budget。配置或 JSON
无效时退出 2。

## Release event

`report-release-event.mjs` 已接入三个仓库内实际 OTA 入口：`apps/pos-ipad`、
`apps/pos-handheld` 和 `apps/mobile` 的 publish 脚本均在 Expo 发布及后台登记成功后
上报 `deploy/accepted`。发布或登记失败不会产生 accepted deploy；reporter 失败会让
发布命令失败，避免把未完成验收视为成功。三条 OTA 脚本都要求独立的
`PERFORMANCE_SERVICE_URL` 和具有 `Service.WriteReleaseEvents` scope 的
`PERFORMANCE_SERVICE_TOKEN`；不得复用 OTA 登记 JWT 或仅有
`System.ManageAppDownloads` scope 的 token。

本仓库未发现 backend/web 的实际部署 workflow。`.github/workflows/quality-baseline.yml`
提供受保护的 `workflow_call` 发布验收契约，外部部署系统必须传入实际
`action=deploy|rollback` 与 `conclusion=accepted|failed`；因此成功 rollback 会被
记录为 `rollback/accepted`，失败发布为 `deploy/failed`，不会被误计入 accepted deploy。
它支持 `deploy|rollback` 和 `accepted|failed`，并强制要求
`--health-checked`。脚本根据 CI provider、run/attempt、环境、组件、动作、release、
commit 和开始时间生成确定性事件 ID；相同部署输入可安全重试，若同一 ID 的载荷不一致，
服务端返回 409 且不会启动或改写观察周期：

```bash
PERFORMANCE_SERVICE_URL=https://example.internal \
PERFORMANCE_SERVICE_TOKEN='<hbsvc service token>' \
node scripts/performance/report-release-event.mjs \
  --health-checked \
  --action deploy \
  --conclusion accepted \
  --component backend \
  --environment Production \
  --release-id release-20260825-1 \
  --commit-sha 0123456789abcdef0123456789abcdef01234567 \
  --started-at-utc 2026-08-25T01:55:00.000Z \
  --completed-at-utc 2026-08-25T02:00:00.000Z \
  --health-check-reference health-run-88
```

脚本只会 POST 到 `/api/system/performance/release-events`。示例使用占位 token，
不得把真实 token 写入 shell history；实际发布自动化应通过受保护环境变量注入。
