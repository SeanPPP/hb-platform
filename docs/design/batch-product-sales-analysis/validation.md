# 批量货号销量分析：验证与交付记录

日期：2026-09-14。分支：`codex/batch-product-sales-20260914`，基线：`4e3918913`。全部实现位于独立工作树，原用户工作区未清理或覆盖。

## 自动检查

| 检查 | 命令及结果 |
| --- | --- |
| 前端专项 | `cd apps/web && npm run test:batch-product-sales-analysis` 通过；覆盖导入、逻辑、366 日图形、API 包络和门店快照、取消、净销量守恒及独立页面权限。 |
| 类型检查 | `cd apps/web && npm run typecheck` 通过。 |
| 完整前端构建 | `cd apps/web && npm run build -- --mode test` 通过；最终 Vite 构建 10.56 秒，命令退出码 0。该命令同时运行 tsc -b。 |
| CI 测试登记 | `cd apps/web && npm run test:inventory` 通过，新增专项测试可被现有清单发现。 |
| 后端及权限回归 | 以下筛选命令通过：255 项通过，0 跳过，总耗时 38.2056 秒。 |
| 变更检查 | `git diff --check` 通过；核对实际 diff，仅包含新增分析页、服务、测试、设计文档及路由/权限/导航等必需登记。 |

```sh
dotnet test services/backend/BlazorApp.Api.Tests/BlazorApp.Api.Tests.csproj \
  --no-restore \
  --filter 'FullyQualifiedName~BatchProductSalesAnalysis|FullyQualifiedName~NavigationServiceTests|FullyQualifiedName~RoleServicePermissionTests' \
  --logger 'console;verbosity=normal'
```

运行 SQL 集成测试时，进程通过 `BATCH_SALES_SQLSERVER_TEST_CONNECTION` 读取本轮本地测试连接；凭据未写入仓库。没有设置该变量的常规环境会跳过需要 SQL Server 的用例，因此不能将常规跳过结果代替本次实际执行证据。

## 真实 SQL Server 验证

使用本机隔离 SQL Server 2022 Developer 容器，loopback 端口 15496，无宿主数据卷。测试使用真实 SqlSugar 模型创建唯一命名的 catalog、POSM 和 HBS 测试库，并在结束后清理本轮准确库名。

- HBS：原单退货、别名消歧、空原商品代码和冲突原单关联，实际 SQL 执行通过，约 419ms。
- POSM：全单支付分摊、部分退货、退货表与销售行排重、设备分店回填、错误原单关联，实际 SQL 执行通过，约 317ms。
- POSM：缺少可选退货表时仍能读取已支付销售，实际 SQL 执行通过，约 134ms。

以上耗时只描述隔离小数据集测试，不代表全年或生产门店查询性能。最终日志 `Test Run Successful / Total tests: 255 / Passed: 255`，未访问生产数据库。

本轮发现并修复了历史 SQL 括号及聚合字段歧义问题，修复后全组 255 项重新通过。容器 `hb-batch-sales-qa-20260914` 已移除，两份本轮生成的临时测试凭据也已移除。

## 浏览器验收

验收入口 `apps/web/dev/batch-product-sales-preview/` 挂载正式 AdminLayout、页面、权限菜单和 API 客户端，仅在本地入口内模拟 fetch；不注册生产路由。顶部一直显示“演示数据”。

| 场景 | 实际结果 |
| --- | --- |
| Excel 导入 | 包含表头、重复值、前导零及 `000000` 数字格式的文件得到 3 个有效货号：001236、HB24018、006821。 |
| 格式及坏输入 | xls 明确提示不支持；CSV 多列和错误行不静默进入查询；保留有效首列文本规则与错误说明。 |
| 主流程 | 粘贴 → 查询 → 选商品 → 选分店 → 每日趋势/明细 → 导出，完成。 |
| 生效门店 | query 请求空 storeCodes 后，detail 使用服务器返回的 B1–B4；改选 B2 后只查询 B2，示例商品数量 72。 |
| 分店切换 | 切换分店使用已返回 daily 数据，未新增 detail 请求。 |
| 请求竞态 | P2 延迟 4 秒，80ms 后选择 P3；P2 请求 aborted，P3 完成并保留当前选择。 |
| 错误重试 | 模拟 detail 503 后显示错误，重试恢复结果。 |
| 无销量和未就绪 | 零交易明确显示“无销量”；缺统计显示等待说明，不显示虚构总销量 0。 |
| 图表 | 蓝/橙/灰分类，悬停及键盘聚焦能读取日期、数量和原价/折扣价信息；负值和 366 日几何有专项覆盖。 |
| CSV | 实际点击摘要和明细导出，捕获生成 Blob 内容并回读；摘要 11 行，明细 80 行，包含范围、商品、日、分店和分店日。文本防公式注入，负数字保留数值含义。未将 Blob 验证表述为系统下载对话框验收。 |
| 中英文 | 切换语言保留商品和查询范围，不增加数据请求。 |
| 窄屏 | 390px 视口结果完整，document.scrollWidth 与 clientWidth 相同，日明细独立滚动。 |

## 独立复核与剩余边界

独立 code-reviewer 对权限撤销、导航、门店范围、源数据分类、退货排重、历史映射、统计补齐、取消、超时和最终测试证据完成复核，未发现剩余阻断项。当前没有可调用的 DeepSeek-Pro 第二路角色，因此未声称完成第二路审查。

GitNexus 索引过期；新工作树 codebase-memory 索引曾成功生成，后续查询出现 transport closed。影响核验使用精确源码、调用点、实际 diff、权限回归和 SQL 集成用例补足。

发布前仍需使用真实授权账号验收：部署后的菜单入口和独立权限分配、真实门店数据及全年多店的生产规模耗时。每个原始源命令超时为 60 秒，已透传取消；该限制下的生产规模成功率尚未实测。生产配置与真实数据验收不能由 test mode 构建或本地演示替代。

交付状态：本地实现、验证和设计验收完成；尚未推送、创建 PR、合并或部署。

## 本地预览

```sh
cd apps/web
npm run dev -- --port 5196 --strictPort
```

打开 `http://localhost:5196/dev/batch-product-sales-preview/?lang=zh`，选择 2026-09-01 至 2026-09-13，并粘贴以下示例货号后查询：

```text
001236
HB24018
006821
HB23056
000875
HB25012
009102
HB22034
001236
HB24018
MISSING
DUPLICATE
```
